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

// Native checks of the combat event channel on real stats (Docs/SpawnedObjectSyncPlan.md 4.4b part 3): every damage / heal / block result becomes one event with the full account, the hurt signal of the target passes it to
// whoever subscribed, and a guess's early hit is matched to the host's event (or cancelled) by the key the projectile gives the hit.
public static class PNCombatEventEditorChecks
{
    private sealed class Loop : IPNObjectSyncTransport
    {
        public PNObjectSyncService Target;
        public void Send(PNObjectRecord record) { Target?.Receive(record); }
    }
    private sealed class Sink : IPNHitReportSink
    {
        public Action<long, PNStat, int> Relay;
        public PNStat Who; public PNStat Claimant => Who;
        public bool SendHitReport(in PNHitReport report, IReadOnlyList<EveEntityId> victims) { foreach (var victim in victims) Relay?.Invoke(report.CastKey, Find(victim.Value), report.Step); return true; }
        // The test entities live in a preview scene, which FindObjectsByType does not see: look in the shooter's scene.
        private PNStat Find(long id) => Who == null ? null : Who.gameObject.scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNStat>()).FirstOrDefault(x => x.EntityId.Value == id);
    }
    private sealed class Module : EveIContentModule
    {
        public static readonly EveId Shot = EveId.Parse("test:combat_shot");
        public void Register(EveRegistryHub hub)
        {
            var registry = hub.Instances();
            var directional = registry.GetKind<PNProjectileDefinition>(PNProjectileGameplayContent.Directional);
            registry.Register(directional.Derive(Shot, JObject.Parse("{'speed':10,'acceleration':0,'speedMin':0,'speedMax':200,'life':{'mode':'Time','value':5},'volume':{'size':[0.4,0.4,0.4]},'environmentMask':['Wall'],'hitLimit':1}")));
        }
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception("Combat event check " + count + ": " + message); count++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNWorldFixtureModule(), new Module() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Host, EveContentCompatibility.Create(hub, docs, "test:combat_events"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile());
        var worlds = new PNWorldRuntime(session, scheduler); var anchor = EditorSceneManager.NewPreviewScene();
        var objects = new List<GameObject>(); PNProjectileManager host = null, client = null; PNObjectSyncService hostSync = null, clientSync = null;
        double now = 500d; PNCombatEvents.Reset(); PNCombatEvents.Clock = () => now;
        var told = new List<(PNCombatEvent Event, bool Matched)>(); Action<PNCombatEvent, bool> listener = (e, matched) => told.Add((e, matched));
        PNCombatEvents.Observed += listener;
        void Pump(Func<bool> until) { for (int i = 0; i < 1000 && !until(); i++) scheduler.AdvanceFrame(); if (!until()) throw new Exception("Fixture did not drain"); }
        GameObject NewObject(string name) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, anchor); objects.Add(obj); return obj; }
        PNProjectileManager NewManager(string name)
        {
            var manager = NewObject(name).AddComponent<PNProjectileManager>(); manager.Initialize(session, new PNProjectileProfile()); manager.ManualAdvance = true; return manager;
        }
        try
        {
            var world = worlds.Create(EveId.Parse("fixture:world")); Pump(() => world.State == EveWorldState.Ready);
            var scene = worlds.Get(world).PhysicsScene; double clock = 100d; long nextId = 991900;
            host = NewManager("Combat host projectiles"); client = NewManager("Combat client projectiles");
            worlds.BeforeUnload = unloading => { host.ReleaseWorld(unloading); client.ReleaseWorld(unloading); };
            hostSync = new PNObjectSyncService(true, () => clock); clientSync = new PNObjectSyncService(false, () => clock);
            var wire = new Loop { Target = clientSync }; hostSync.Transport = wire; host.ObjectSync = hostSync; client.ObjectSync = clientSync;
            host.PredictionClock = () => clock; client.PredictionClock = () => clock;
            PNStat Entity(Vector3 position, int layer, string name)
            {
                var obj = NewObject(name); obj.SetActive(false); obj.layer = layer; SceneManager.MoveGameObjectToScene(obj, scene); obj.transform.position = position;
                var id = obj.AddComponent<EveEntityIdentity>(); id.Assign(new EveEntityId(nextId++), EveId.Parse("project_n:dummy"), 0); id.BindWorld(world, "chunk-1");
                obj.AddComponent<CapsuleCollider>().height = 2; obj.AddComponent<EveTimeScale>();
                var stat = obj.AddComponent<PNEnemyStat>(); obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); obj.GetComponent<PNBuffManager>().Advance(0); return stat;
            }
            void Advance(float seconds) { clock += seconds; now += seconds; client.Advance(seconds); host.Advance(seconds); }
            var shooter = Entity(new Vector3(0, 1, 60), PNLayers.Void, "Combat shooter"); var enemy = Entity(new Vector3(3, 1, 10), PNLayers.Enemy, "Combat enemy");
            var source = PNCombatSource.Capture(shooter); var layers = PNTargetLayers.Of(PNLayers.Enemy);
            var prepared = source.Snapshot.PrepareDamage(10, false, 0d, PNContentIds.True);
            Advance(.05f);
            var signal = PNHurtSignal.For(enemy); var hurts = new List<PNHurtInfo>(); var deaths = new List<PNCombatEvent>(); var heals = new List<PNCombatEvent>(); var blocks = new List<PNCombatEvent>();
            signal.Hurt += hurts.Add; signal.Died += deaths.Add; signal.Healed += heals.Add; signal.Blocked += blocks.Add;

            // A damage result becomes one event with the whole account.
            double before = enemy.GetCurrent(PNContentIds.Health);
            var result = enemy.ReceiveDamage(source.Snapshot.CreateDamage(prepared).WithHit("K:0:0", 1f, 2f, 3f));
            Check(told.Count == 1 && told[0].Event.Kind == PNCombatEventKind.Damage && !told[0].Matched, "one damage result is one event");
            var e = told[0].Event;
            Check(e.TargetId == enemy.EntityId.Value && e.SourceId == shooter.EntityId.Value && e.World.IsValid && Math.Abs(e.Applied - result.AppliedDamage) < 1e-3 && Math.Abs(e.HealthBefore - before) < 32d && Math.Abs(e.HealthAfter - (before - result.AppliedDamage)) < 32d,
                "(target, source, world, applied and health before / after are the result's) target " + e.TargetId + "/" + enemy.EntityId.Value + " source " + e.SourceId + "/" + shooter.EntityId.Value + " world " + e.World.IsValid + " applied " + e.Applied + "/" + result.AppliedDamage + " before " + e.HealthBefore + "/" + before + " after " + e.HealthAfter);
            Check(e.Applied > 0f && e.Calculated >= e.Applied && !e.Killed && !e.Critical && e.HasPoint && e.PointX == 1f && e.PointY == 2f && e.PointZ == 3f && e.HitKey == "K:0:0" && e.Tags.Length > 0 && e.Serial == 1, "(the point, the hit key, the tags and the serial come with it)");
            Check(hurts.Count == 1 && hurts[0].Phase == PNHurtPhase.Confirmed && !hurts[0].MatchedEarly && hurts[0].Event == e && hurts[0].HitKey == "K:0:0" && hurts[0].Point == new Vector3(1, 2, 3), "the target's hurt signal tells the confirmed hit once");

            // A hit with no key / point (a Buff over time) still comes, without them.
            enemy.ReceiveDamage(source.Snapshot.CreateDamage(prepared));
            Check(told.Count == 2 && told[1].Event.HitKey == null && !told[1].Event.HasPoint && hurts.Count == 2, "a hit without a key or point has none in its event, and still reaches the signal");

            // A heal.
            before = enemy.GetCurrent(PNContentIds.Health);
            var healed = shooter.SendHeal(enemy, 5);
            Check(told.Count == 3 && told[2].Event.Kind == PNCombatEventKind.Heal && told[2].Event.TargetId == enemy.EntityId.Value && Math.Abs(told[2].Event.Applied - healed.AppliedHeal) < 1e-3 && told[2].Event.Applied > 0f && heals.Count == 1 && hurts.Count == 2, "a heal is one Heal event and reaches Healed, not Hurt");

            // A block: a Buff's gate refuses the hit.
            Action<PNDamageGate> block = gate => gate.Block(); enemy.BeforeDamage += block;
            double held = enemy.GetCurrent(PNContentIds.Health); var refusedResult = enemy.ReceiveDamage(source.Snapshot.CreateDamage(prepared)); enemy.BeforeDamage -= block;
            Check(refusedResult.Ignored && enemy.GetCurrent(PNContentIds.Health) == held && told.Count == 4 && told[3].Event.Kind == PNCombatEventKind.Blocked && blocks.Count == 1 && hurts.Count == 2, "a blocked hit is one Blocked event, changes nothing and is not a Hurt");

            // A kill: Hurt (with Killed) and Died, once.
            var lethal = source.Snapshot.PrepareDamage(1000000000, false, 0d, PNContentIds.True);
            enemy.ReceiveDamage(source.Snapshot.CreateDamage(lethal));
            var killed = told[4].Event;
            Check(killed.Killed && killed.HealthAfter == 0f && deaths.Count == 1 && deaths[0] == killed && hurts.Count == 3, "a kill is a Hurt event with Killed set and one Died");
            enemy.ReceiveDamage(source.Snapshot.CreateDamage(prepared));
            Check(deaths.Count == 1, "a hit on the dead tells Died no second time");

            // A bad listener does not hurt the authority's result.
            signal.Hurt += _ => throw new InvalidOperationException("reaction fault");
            var fresh = Entity(new Vector3(3, 1, 20), PNLayers.Enemy, "Combat fresh enemy"); Advance(.05f);
            var freshSignal = PNHurtSignal.For(fresh); freshSignal.Hurt += _ => throw new InvalidOperationException("reaction fault");
            double freshBefore = fresh.GetCurrent(PNContentIds.Health); fresh.ReceiveDamage(source.Snapshot.CreateDamage(prepared));
            Check(fresh.GetCurrent(PNContentIds.Health) < freshBefore, "a faulty reaction cannot undo the health the hit took");

            // The shooter's guess: early hit told at once, matched to the host's event by the key (cast, index, hit).
            told.Clear(); hurts.Clear();
            var guessEnemy = Entity(new Vector3(3, 1, 30), PNLayers.Enemy, "Combat guess enemy"); Advance(.05f);
            var guessSignal = PNHurtSignal.For(guessEnemy); var guessHurts = new List<PNHurtInfo>(); guessSignal.Hurt += guessHurts.Add;
            var sink = new Sink { Who = shooter }; sink.Relay = (cast, target, step) => host.AcceptHitReport(PNHitReport.DealtByCast(cast, step), target, .5f); client.RegisterClaimSink(sink);
            client.Create(new PNProjectileLaunch(Module.Shot, null, source, layers).AsPrediction(9101), new PNProjectileSpawn(scene, new Vector3(0, 1, 30), Vector3.right));
            host.Create(new PNProjectileLaunch(Module.Shot, null, source, layers, prepared).WithCast(9101, true), new PNProjectileSpawn(scene, new Vector3(0, 1, 30), Vector3.right));
            for (int i = 0; i < 40; i++) Advance(.05f);
            Check(guessHurts.Count == 2 && guessHurts[0].Phase == PNHurtPhase.Early && guessHurts[0].Event == null && guessHurts[0].HitKey == "9101:0:0" && guessHurts[0].HasPoint, "the guess's hit is told early, with the key and the guess's point (" + string.Join(" ", guessHurts.Select(x => x.Phase + ":" + x.HitKey)) + "; early " + PNCombatEvents.EarlyTold + " told " + PNCombatEvents.Told + ")");
            Check(guessHurts[1].Phase == PNHurtPhase.Confirmed && guessHurts[1].MatchedEarly && guessHurts[1].HitKey == "9101:0:0" && guessHurts[1].Event.Applied > 0f, "the host's event comes with the same key and is matched to the early hit");
            Check(PNCombatEvents.EarlyMatched == 1 && PNCombatEvents.EarlyCancels == 0 && PNCombatEvents.PendingEarly == 0, "(one match, no cancel, nothing left waiting)");

            client.UnregisterClaimSink(sink);     // from here the report never reaches the host
            // A guess nobody confirms (the report never reaches the host): the early hit is cancelled after the hold.
            var lonelyEnemy = Entity(new Vector3(3, 1, 40), PNLayers.Enemy, "Combat lonely enemy"); Advance(.05f);
            var lonelySignal = PNHurtSignal.For(lonelyEnemy); var lonely = new List<PNHurtInfo>(); lonelySignal.Hurt += lonely.Add;
            client.Create(new PNProjectileLaunch(Module.Shot, null, source, layers).AsPrediction(9102), new PNProjectileSpawn(scene, new Vector3(0, 1, 40), Vector3.right));
            for (int i = 0; i < 12; i++) Advance(.05f);
            Check(lonely.Count == 1 && lonely[0].Phase == PNHurtPhase.Early && PNCombatEvents.PendingEarly == 1, "an unreported guess hit is told early and waits");
            now += PNCombatEvents.EarlyHoldSeconds + .5d; PNCombatEvents.Expire(now);
            Check(lonely.Count == 2 && lonely[1].Phase == PNHurtPhase.Cancelled && lonely[1].HitKey == "9102:0:0" && PNCombatEvents.EarlyCancels == 1, "an early hit the host never confirms is cancelled after the hold");
            Check(lonelyEnemy.GetCurrent(PNContentIds.Health) == lonelyEnemy.GetMax(PNContentIds.Health), "(and nothing hurt the target: an early reaction is presentation only)");
            return "PASS " + count + " combat event checks (damage / heal / block / kill events, hurt signal, faulty reaction, early match and cancel through a guess)";
        }
        finally
        {
            PNCombatEvents.Observed -= listener; PNCombatEvents.Reset(); PNCombatEvents.Clock = null;
            try { if (host != null) host.ObjectSync = null; if (client != null) client.ObjectSync = null; } catch (Exception error) { Debug.LogException(error); }
            foreach (var manager in new[] { host, client }) try { manager?.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            foreach (var obj in objects.ToArray())
                if (obj != null && obj.GetComponent<EveEntityLoader>() != null) { try { obj.GetComponent<EveEntityLoader>().Unload(); obj.GetComponent<EveEntityIdentity>()?.ResetIdentity(); } catch (Exception error) { Debug.LogException(error); } UnityEngine.Object.DestroyImmediate(obj); }
            hostSync?.Dispose(); clientSync?.Dispose();
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var obj in objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(anchor); }
        }
    }
}
#endif
