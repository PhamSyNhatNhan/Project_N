#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Native checks of the hit claims (Docs/SpawnedObjectSyncPlan.md 4.4c, way B): the machine of a player decides the hits on him. While the policy says so the authority's projectile flies through that player, the copy on his
// machine tests only him and sends one claim, the authority checks the claim and applies the damage once; a claim for a player the policy does not hand over, from a copy far away, or a second one, is refused.
public static class PNHurtClaimEditorChecks
{
    private sealed class Loop : IPNObjectSyncTransport
    {
        public readonly List<PNObjectRecord> All = new List<PNObjectRecord>(); public PNObjectSyncService Target;
        public void Send(PNObjectRecord record) { All.Add(record); Target?.Receive(record); }
    }
    private sealed class Policy : IPNHurtClaimPolicy
    {
        public readonly HashSet<long> Ids = new HashSet<long>();
        public bool ClaimsDecide(PNStat target) => target != null && Ids.Contains(target.EntityId.Value);
    }
    private sealed class Sink : IPNHitReportSink
    {
        public PNStat Who; public readonly List<(PNObjectKind Kind, long Id, int Hit)> Sent = new List<(PNObjectKind, long, int)>(); public Action<long, int> Relay;
        public PNStat Claimant => Who;
        public bool SendHitReport(in PNHitReport report, System.Collections.Generic.IReadOnlyList<EveEntityId> victims) { Sent.Add((report.Kind, report.ObjectId, report.Detail)); Relay?.Invoke(report.ObjectId, report.Detail); return true; }
    }
    private sealed class ClaimModule : EveIContentModule
    {
        public void Register(EveRegistryHub hub)
        {
            var registry = hub.Instances();
            var directional = registry.GetKind<PNProjectileDefinition>(PNProjectileGameplayContent.Directional);
            var data = JObject.Parse("{'speed':10,'acceleration':0,'speedMin':0,'speedMax':200,'life':{'mode':'Time','value':5},'volume':{'size':[0.4,0.4,0.4]},'environmentMask':['Wall'],'hitLimit':1}");
            registry.Register(directional.Derive(EveId.Parse("test:claim_shot"), data));
        }
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception("Hurt claim check " + count + ": " + message); count++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNWorldFixtureModule(), new ClaimModule() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Host, EveContentCompatibility.Create(hub, docs, "test:hurt_claims"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile());
        var worlds = new PNWorldRuntime(session, scheduler); var anchor = EditorSceneManager.NewPreviewScene();
        var objects = new List<GameObject>(); PNProjectileManager host = null, client = null; PNObjectSyncService hostSync = null, clientSync = null;
        void Pump(Func<bool> until) { for (int i = 0; i < 1000 && !until(); i++) scheduler.AdvanceFrame(); if (!until()) throw new Exception("Fixture did not drain"); }
        GameObject NewObject(string name) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, anchor); objects.Add(obj); return obj; }
        PNProjectileManager NewManager(string name)
        {
            var manager = NewObject(name).AddComponent<PNProjectileManager>(); manager.Initialize(session, new PNProjectileProfile()); manager.ManualAdvance = true; return manager;
        }
        var sink = new Sink(); var damageKeys = new List<string>(); var earlyKeys = new List<string>();
        Action<PNCombatEvent, bool> onEvent = (e, early) => { if (e.Kind == PNCombatEventKind.Damage && e.HitKey != null) damageKeys.Add(e.HitKey); };
        Action<PNEarlyHit> onEarly = e => earlyKeys.Add(e.HitKey);
        PNCombatEvents.Observed += onEvent; PNCombatEvents.Early += onEarly;
        try
        {
            var world = worlds.Create(EveId.Parse("fixture:world")); Pump(() => world.State == EveWorldState.Ready);
            var scene = worlds.Get(world).PhysicsScene; double clock = 100d; long nextId = 991700;
            host = NewManager("Claim host projectiles"); client = NewManager("Claim client projectiles");
            worlds.BeforeUnload = unloading => { host.ReleaseWorld(unloading); client.ReleaseWorld(unloading); };
            hostSync = new PNObjectSyncService(true, () => clock); clientSync = new PNObjectSyncService(false, () => clock);
            var wire = new Loop { Target = clientSync }; hostSync.Transport = wire; host.ObjectSync = hostSync; client.ObjectSync = clientSync;
            PNStat Shooter()
            {
                var obj = NewObject("Claim shooter"); obj.SetActive(false); obj.layer = PNLayers.Void; SceneManager.MoveGameObjectToScene(obj, scene); obj.transform.position = new Vector3(0, 1, 60);
                var id = obj.AddComponent<EveEntityIdentity>(); id.Assign(new EveEntityId(nextId++), EveId.Parse("project_n:dummy"), 0); id.BindWorld(world, "chunk-1");
                obj.AddComponent<CapsuleCollider>().height = 2; obj.AddComponent<EveTimeScale>();
                var stat = obj.AddComponent<PNEnemyStat>(); obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); obj.GetComponent<PNBuffManager>().Advance(0); return stat;
            }
            PNStat Victim(string name, Vector3 at)
            {
                var obj = NewObject(name); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj, scene); obj.transform.position = at;
                var id = obj.AddComponent<EveEntityIdentity>(); id.Assign(new EveEntityId(nextId++), EveId.Parse("project_n:eve"), 0); id.BindWorld(world, "chunk-1");
                var stat = obj.AddComponent<PNPlayerStat>().AsPlayer(); obj.AddComponent<Rigidbody>().isKinematic = true; obj.AddComponent<EveTimeScale>();
                var capsule = obj.AddComponent<CapsuleCollider>(); capsule.radius = .3f; capsule.height = 1.6f; capsule.center = Vector3.up * .8f; obj.layer = PNLayers.Player;
                obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); obj.GetComponent<PNBuffManager>().Advance(0); return stat;
            }
            double Hp(PNStat stat) => stat.GetCurrent(PNContentIds.Health);
            var shooter = Shooter(); var source = PNCombatSource.Capture(shooter);
            var prepared = source.Snapshot.PrepareDamage(10, false, 0d, PNContentIds.True);
            var shot = EveId.Parse("test:claim_shot"); var layers = PNTargetLayers.Of(PNLayers.Player);
            PNProjectileLaunch Launch() => new PNProjectileLaunch(shot, null, source, layers, prepared);
            PNProjectileSpawn Spawn(Vector3 position) => new PNProjectileSpawn(scene, position, Vector3.right, null, 0);
            void Advance(float seconds) { for (float t = 0; t < seconds - 1e-4f; t += .02f) { clock += .02; client.Advance(.02f); host.Advance(.02f); } }
            IEnumerable<PNProjectileInstance> Instances() => scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNProjectileInstance>()).Where(x => x.IsRunning);
            Advance(.04f);
            var victim = Victim("Claim victim", new Vector3(0, 0, 0)); Physics.SyncTransforms(); Advance(.04f);
            Check(shooter.IsReady && victim.IsReady, "the shooter and the victim are ready");

            // ---- no policy (offline, the host's own player): the authority tests the victim itself, as before ----
            double before = Hp(victim);
            var plain = host.Create(Launch(), Spawn(new Vector3(-3, 0.8f, 0)));
            Check(plain.Status == PNProjectileStatus.Created, "the authority makes the bolt");
            Advance(.5f);
            Check(Hp(victim) < before && host.ClaimsAccepted == 0, "with no policy the authority hits the player itself (health " + before + " -> " + Hp(victim) + ")");

            // ---- the record names who the object can hurt, so the copy on the player's machine knows ----
            var policy = new Policy(); host.HurtClaimPolicy = policy; policy.Ids.Add(victim.EntityId.Value);
            sink.Who = victim; client.RegisterClaimSink(sink);
            double health = Hp(victim);
            var made = host.Create(Launch(), Spawn(new Vector3(-6, 0.8f, 0)));
            Advance(.02f);
            var copy = Instances().Single(x => x.IsReplica);
            Check(copy.Launch.TargetLayers.Contains(PNLayers.Player) && wire.All.Last(x => x.Type == PNObjectRecordType.Spawn).TargetMask == PNLayers.Bit(PNLayers.Player), "the Spawn record carries the target layers and the copy takes them");

            // ---- the player's machine decides: the authority's bolt flies through him, his copy claims once, the authority applies it once ----
            sink.Relay = (id, hit) => host.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Projectile, id), victim, 1.5f);
            Advance(.2f);
            Check(sink.Sent.Count == 0 && Hp(victim) == health, "before the bolt reaches him nothing is claimed or applied");
            Advance(.5f);
            Check(sink.Sent.Count == 1 && sink.Sent[0].Kind == PNObjectKind.Projectile && sink.Sent[0].Hit == 0, "his copy sent exactly one claim (" + sink.Sent.Count + ")");
            Check(host.ClaimsAccepted == 1 && Hp(victim) < health && Math.Abs((health - Hp(victim)) - (before - Hp(victim) < 0 ? 0 : 0) - 10d) < 5d, "the authority applied the damage once on the claim (health " + health + " -> " + Hp(victim) + ")");
            string expected = "o" + wire.All.Last(x => x.Type == PNObjectRecordType.Spawn).Id + ":" + PNHurtClaimLimits.ClaimKeyIndex(victim.EntityId.Value);
            Check(earlyKeys.Contains(expected) && damageKeys.Contains(expected), "the claimed hit is named by its victim on both sides (" + expected + "; early " + string.Join(",", earlyKeys) + ", damage " + string.Join(",", damageKeys) + ")");
            Check(!made.Handle.IsAlive || !Instances().Any(x => !x.IsReplica), "the bolt ended on the claimed hit (hit limit 1)");
            Advance(.2f);
            Check(!Instances().Any(), "the End record ended the copy");

            // ---- the authority did not test the victim itself while the policy held: a bolt that only passes his machine's copy is not applied without a claim ----
            sink.Relay = null; sink.Sent.Clear(); health = Hp(victim);
            host.Create(Launch(), Spawn(new Vector3(-6, 0.8f, 0)));
            Advance(1.2f);
            Check(sink.Sent.Count == 1 && Hp(victim) == health, "a claim that is never answered leaves the player unhurt (the machine, not the authority, decides)");
            foreach (var instance in Instances().ToArray()) { }
            Advance(5f);

            // ---- refusals ----
            Check(!host.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Projectile, 987654), victim, 1.5f) && host.ClaimRefusals.ContainsKey("gone"), "a claim for an object that is not there is refused");
            policy.Ids.Clear();
            var held = host.Create(Launch(), Spawn(new Vector3(-30, 0.8f, 0))); Advance(.05f);
            long heldId = wire.All.Last(x => x.Type == PNObjectRecordType.Spawn).Id; policy.Ids.Add(victim.EntityId.Value); policy.Ids.Remove(victim.EntityId.Value);
            Check(!host.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Projectile, heldId), victim, 1.5f) && host.ClaimRefusals.ContainsKey("policy"), "a claim for a player the policy does not hand over is refused (the authority decides him again, e.g. a silent machine)");
            policy.Ids.Add(victim.EntityId.Value);
            Check(!host.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Projectile, heldId), victim, 1.5f) && host.ClaimRefusals.ContainsKey("rule"), "a claim from far away (the object is 30 m off) is refused by the reach rule");
            Check(!host.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Projectile, heldId), victim, 1.5f) && host.ClaimRefusals.ContainsKey("again"), "the same player cannot claim the same object twice");
            held.Handle.End(); Advance(.3f);

            // ---- the policy gives the player back (his machine fell silent): the authority tests him itself again ----
            policy.Ids.Clear(); health = Hp(victim);
            host.Create(Launch(), Spawn(new Vector3(-3, 0.8f, 0))); Advance(.5f);
            Check(Hp(victim) < health, "once the policy hands the player back the authority hits him itself (health " + health + " -> " + Hp(victim) + ")");

            // ---- the record refuses a negative mask ----
            bool refusedMask = false;
            try { PNObjectRecord.Spawn(PNObjectKind.Projectile, 1, new PNObjectWorld(Guid.NewGuid(), 1), shot, null, new PNObjectVector(0, 0, 0), new PNObjectVector(1, 0, 0), 0, 0, 0, null, 0, 1, 1, 1, 0, 0, 0f, -1); }
            catch (FormatException) { refusedMask = true; }
            Check(refusedMask, "a negative target mask is refused");
            Advance(6f);
            Check(host.Snapshot().Count == 0 && client.Snapshot().Count == 0, "nothing is left flying");
        }
        finally
        {
            PNCombatEvents.Observed -= onEvent; PNCombatEvents.Early -= onEarly;
            try { client?.UnregisterClaimSink(sink); if (host != null) { host.ObjectSync = null; host.HurtClaimPolicy = null; } if (client != null) client.ObjectSync = null; } catch (Exception error) { Debug.LogException(error); }
            foreach (var manager in new[] { host, client }) try { manager?.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            foreach (var obj in objects.ToArray())
                if (obj != null && obj.GetComponent<EveEntityLoader>() != null) { try { obj.GetComponent<EveEntityLoader>().Unload(); obj.GetComponent<EveEntityIdentity>()?.ResetIdentity(); } catch (Exception error) { Debug.LogException(error); } }
            hostSync?.Dispose(); clientSync?.Dispose();
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var obj in objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(anchor); }
        }
        return "PASS " + count + " hurt claim checks (the machine of a player decides his hits: record carries the targets, one claim, applied once, refused when gone / not handed over / far / twice, taken back when the policy lets go)";
    }
}
#endif
