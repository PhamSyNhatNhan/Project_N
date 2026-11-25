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

// Native checks of the hit claims for blasts (V2) and areas (V3) (Docs/SpawnedObjectSyncPlan.md 4.4c, way B): while the policy hands a player to his own machine the authority's blast does not test him and its area does not
// poll him; his copy tests his body, claims once, and the authority applies the blast's own prepared damage (within the grace after the detonation) or keeps him inside the field until his machine says he left.
public static class PNHurtClaimBlastAreaEditorChecks
{
    private sealed class Loop : IPNObjectSyncTransport
    {
        public PNObjectSyncService Target;
        public void Send(PNObjectRecord record) { Target?.Receive(record); }
    }
    private sealed class Policy : IPNHurtClaimPolicy
    {
        public readonly HashSet<long> Ids = new HashSet<long>();
        public bool ClaimsDecide(PNStat target) => target != null && Ids.Contains(target.EntityId.Value);
    }
    private sealed class Sink : IPNHitReportSink
    {
        public PNStat Who; public readonly List<(PNObjectKind Kind, long Id, int Hit)> Sent = new List<(PNObjectKind, long, int)>(); public Action<PNObjectKind, long, int> Relay;
        public PNStat Claimant => Who;
        public bool SendHitReport(in PNHitReport report, System.Collections.Generic.IReadOnlyList<EveEntityId> victims) { Sent.Add((report.Kind, report.ObjectId, report.Detail)); Relay?.Invoke(report.Kind, report.ObjectId, report.Detail); return true; }
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception("Hurt claim blast/area check " + count + ": " + message); count++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNWorldFixtureModule(), new PNBlastStrikesProbeModule() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Host, EveContentCompatibility.Create(hub, docs, "test:hurt_claims_ba"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile());
        var worlds = new PNWorldRuntime(session, scheduler); var anchor = EditorSceneManager.NewPreviewScene();
        var objects = new List<GameObject>(); PNObjectSyncService hostSync = null, clientSync = null;
        PNBlastManager hostBlasts = null, clientBlasts = null; PNAreaManager hostAreas = null, clientAreas = null;
        void Pump(Func<bool> until) { for (int i = 0; i < 1000 && !until(); i++) scheduler.AdvanceFrame(); if (!until()) throw new Exception("Fixture did not drain"); }
        GameObject NewObject(string name) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, anchor); objects.Add(obj); return obj; }
        var sink = new Sink(); var damageKeys = new List<string>(); var earlyKeys = new List<string>();
        Action<PNCombatEvent, bool> onEvent = (e, early) => { if (e.Kind == PNCombatEventKind.Damage && e.HitKey != null) damageKeys.Add(e.HitKey); };
        Action<PNEarlyHit> onEarly = e => earlyKeys.Add(e.HitKey);
        PNCombatEvents.Observed += onEvent; PNCombatEvents.Early += onEarly;
        try
        {
            var world = worlds.Create(EveId.Parse("fixture:world")); Pump(() => world.State == EveWorldState.Ready);
            var worldContext = worlds.Get(world); var scene = worldContext.PhysicsScene; double clock = 100d; long nextId = 993400;
            worlds.BeforeUnload = unloading => { foreach (var manager in new[] { hostBlasts, clientBlasts }) manager?.ReleaseWorld(unloading); foreach (var manager in new[] { hostAreas, clientAreas }) manager?.ReleaseWorld(unloading); };
            PNBlastManager NewBlasts(string name) { var manager = NewObject(name).AddComponent<PNBlastManager>(); manager.Initialize(session); manager.PredictionClock = () => clock; return manager; }
            PNAreaManager NewAreas(string name) { var manager = NewObject(name).AddComponent<PNAreaManager>(); manager.Initialize(session); manager.PredictionClock = () => clock; return manager; }
            hostBlasts = NewBlasts("Claim host blasts"); clientBlasts = NewBlasts("Claim client blasts"); hostAreas = NewAreas("Claim host areas"); clientAreas = NewAreas("Claim client areas");
            hostSync = new PNObjectSyncService(true, () => clock); clientSync = new PNObjectSyncService(false, () => clock);
            var wire = new Loop { Target = clientSync }; hostSync.Transport = wire;
            hostBlasts.ObjectSync = hostSync; hostAreas.ObjectSync = hostSync; clientBlasts.ObjectSync = clientSync; clientAreas.ObjectSync = clientSync;
            var book = new PNHurtClaimBook(); var policy = new Policy();
            hostBlasts.ClaimBook = clientBlasts.ClaimBook = hostAreas.ClaimBook = clientAreas.ClaimBook = book;

            PNStat Owner(string name)
            {
                var obj = NewObject(name); obj.SetActive(false); obj.layer = PNLayers.Void; SceneManager.MoveGameObjectToScene(obj, scene); obj.transform.position = new Vector3(0, 0, 30);
                var id = obj.AddComponent<EveEntityIdentity>(); id.Assign(new EveEntityId(nextId++), EveId.Parse("project_n:dummy"), 0); id.BindWorld(world, "chunk-1");
                obj.AddComponent<CapsuleCollider>().height = 2; obj.AddComponent<EveTimeScale>();
                var stat = obj.AddComponent<PNEnemyStat>(); obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); obj.GetComponent<PNBuffManager>().Advance(0); return stat;
            }
            PNStat Victim(string name, Vector3 at)
            {
                var obj = NewObject(name); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj, scene); obj.transform.position = at;
                var id = obj.AddComponent<EveEntityIdentity>(); id.Assign(new EveEntityId(nextId++), EveId.Parse("project_n:eve"), 0); id.BindWorld(world, "chunk-1");
                var stat = obj.AddComponent<PNPlayerStat>().AsPlayer(); obj.AddComponent<Rigidbody>().isKinematic = true; obj.AddComponent<EveTimeScale>();
                var capsule = obj.AddComponent<CapsuleCollider>(); capsule.radius = .2f; capsule.height = 1.4f; capsule.center = Vector3.up * .7f; obj.layer = PNLayers.Player;
                obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); obj.GetComponent<PNBuffManager>().Advance(0); return stat;
            }
            float Health(PNStat stat) => (float)stat.GetCurrent(PNContentIds.Health);
            void Advance(float seconds)
            {
                for (float t = 0; t < seconds - 1e-4f; t += .05f) { clock += .05; hostBlasts.Advance(.05f); clientBlasts.Advance(.05f); hostAreas.Advance(.05f); clientAreas.Advance(.05f); }
            }
            JObject BlastInput(double delay, double tail) => JObject.Parse("{'delay':" + delay + ",'tail':" + tail + ",'radius':1.5,'height':1.5,'value':{'amount':10},'canCrit':false,'tags':['base:magic']}");
            JObject AreaInput(double duration) => new JObject { ["radius"] = 2, ["reduction"] = .5, ["minimumReduction"] = .2, ["duration"] = duration, ["dependent"] = false, ["targetLayers"] = new JArray("Player"), ["layerMode"] = "Include" };
            long SpawnId(PNObjectKind kind) => hostSync.Baseline(PNObjectWorld.Of(world)).Where(x => x.Kind == kind && x.Type == PNObjectRecordType.Spawn).Select(x => x.Id).LastOrDefault();

            var owner = Owner("Claim owner"); var victim = Victim("Claim victim", Vector3.zero); var far = Victim("Claim far", new Vector3(30, 0, 0)); var source = PNCombatSource.Capture(owner);
            Check(owner.IsReady && victim.IsReady && far.IsReady, "the owner and the victims are ready");
            Advance(.1f);

            // ================= V2: blast =================
            // no policy: the authority tests the victim itself at the detonation
            float health = Health(victim);
            hostBlasts.Create(new PNBlastCreateRequest(PNBlastGameplayContent.Cylinder, BlastInput(.5, 0), source, scene, Vector3.zero, owner, PNBlastDependency.Independent)); Advance(.7f);
            Check(Health(victim) < health && book.Accepted == 0, "with no policy the blast hurts the player itself (health " + health + " -> " + Health(victim) + ")");

            // the policy hands the victim to his machine: the blast leaves him, his copy claims once, the authority applies the prepared damage once
            book.Policy = policy; policy.Ids.Add(victim.EntityId.Value); policy.Ids.Add(far.EntityId.Value);
            sink.Who = victim; book.Register(sink); health = Health(victim);
            hostBlasts.Create(new PNBlastCreateRequest(PNBlastGameplayContent.Cylinder, BlastInput(.5, .5), source, scene, Vector3.zero, owner, PNBlastDependency.Independent));
            Advance(.1f);
            long blastId = SpawnId(PNObjectKind.Blast);
            Check(blastId > 0 && wire != null && clientBlasts.ReplicaCount == 1, "the copy of the blast is on the player's machine");
            sink.Relay = (kind, id, hit) => { if (kind == PNObjectKind.Blast) hostBlasts.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Blast, id, hit), victim, 1.5f); };
            Advance(.6f);
            Check(sink.Sent.Count == 1 && sink.Sent[0].Kind == PNObjectKind.Blast && sink.Sent[0].Id == blastId, "his copy sent exactly one claim for the blast (" + sink.Sent.Count + ")");
            Check(book.Accepted == 1 && Health(victim) < health, "the authority applied the blast's damage on the claim (health " + health + " -> " + Health(victim) + ")");
            string expected = PNBlastManager.HitName(blastId, 0, victim.EntityId.Value);
            Check(earlyKeys.Contains(expected) && damageKeys.Contains(expected), "the claimed hit is named by its victim on both sides (" + expected + ")");

            // refusals
            Check(!hostBlasts.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Blast, blastId), victim, 1.5f) && book.Refusals.ContainsKey("again"), "the same player cannot claim the same blast twice");
            Check(!hostBlasts.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Blast, blastId), far, 1.5f) && book.Refusals.ContainsKey("rule"), "a player 30 m from the blast is refused by the reach rule");
            Check(!hostBlasts.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Blast, 987654), victim, 1.5f) && book.Refusals.ContainsKey("gone"), "a claim for a blast that is not there is refused");
            Advance(1f);
            health = Health(victim);
            Check(!hostBlasts.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Blast, blastId), victim, 1.5f) && Health(victim) == health, "a claim after the grace is refused and the player is unhurt");

            // a claim that never comes: the player's machine decides, so the authority does not hurt him
            sink.Relay = null; sink.Sent.Clear(); health = Health(victim);
            hostBlasts.Create(new PNBlastCreateRequest(PNBlastGameplayContent.Cylinder, BlastInput(.3, .3), source, scene, Vector3.zero, owner, PNBlastDependency.Independent)); Advance(1.2f);
            Check(sink.Sent.Count == 1 && Health(victim) == health, "an unanswered claim leaves the player unhurt (the machine, not the authority, decides): sent " + sink.Sent.Count + ", health " + health + " -> " + Health(victim) + ", host replicas " + clientBlasts.ReplicaCount);

            // the machine that sees the player outside the shape does not claim
            sink.Sent.Clear(); victim.transform.position = new Vector3(10, 0, 0); Physics.SyncTransforms();
            hostBlasts.Create(new PNBlastCreateRequest(PNBlastGameplayContent.Cylinder, BlastInput(.3, .3), source, scene, Vector3.zero, owner, PNBlastDependency.Independent)); Advance(1f);
            Check(sink.Sent.Count == 0, "no claim when the copy sees the player outside the blast");
            victim.transform.position = Vector3.zero; Physics.SyncTransforms();

            // the policy gives the player back (his machine fell silent): the authority tests him itself again
            policy.Ids.Clear(); health = Health(victim);
            hostBlasts.Create(new PNBlastCreateRequest(PNBlastGameplayContent.Cylinder, BlastInput(.3, .3), source, scene, Vector3.zero, owner, PNBlastDependency.Independent)); Advance(.6f);
            Check(Health(victim) < health, "once the policy hands the player back the blast hurts him itself");
            Check(!hostBlasts.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Blast, blastId), victim, 1.5f) && book.Refusals.ContainsKey("policy"), "a claim for a player the policy does not hand over is refused");

            // ================= IL2: a blast with three strikes: one claim ticket per strike, named by the strike =================
            policy.Ids.Add(victim.EntityId.Value); sink.Sent.Clear(); damageKeys.Clear(); earlyKeys.Clear(); health = Health(victim); int acceptedBefore = book.Accepted;
            sink.Relay = (kind, id, hit) => { if (kind == PNObjectKind.Blast) hostBlasts.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Blast, id, hit), victim, 1.5f); };
            hostBlasts.Create(new PNBlastCreateRequest(PNBlastStrikesProbe.Id, new JObject(), source, scene, Vector3.zero, owner, PNBlastDependency.Independent));
            Advance(.1f);
            long strikesId = SpawnId(PNObjectKind.Blast);
            Check(strikesId > 0 && clientBlasts.ReplicaCount == 1, "the copy of the three-strike blast is on the player's machine");
            Advance(1.2f);
            Check(sink.Sent.Count == 3 && sink.Sent.Select(x => x.Hit).SequenceEqual(new[] { 0, 1, 2 }) && sink.Sent.All(x => x.Kind == PNObjectKind.Blast && x.Id == strikesId),
                "his copy claimed each strike once, the aftershock in OnEnd included (" + string.Join(",", sink.Sent.Select(x => x.Hit)) + ")");
            Check(book.Accepted - acceptedBefore == 3 && Health(victim) < health, "the authority applied the prepared damage once per strike (health " + health + " -> " + Health(victim) + ")");
            Check(Enumerable.Range(0, 3).All(i => earlyKeys.Contains(PNBlastManager.HitName(strikesId, i, victim.EntityId.Value)) && damageKeys.Contains(PNBlastManager.HitName(strikesId, i, victim.EntityId.Value))),
                "each claimed hit is named by blast, strike and victim on both sides (" + string.Join(",", damageKeys) + ")");
            Check(clientBlasts.ReplicaCount == 0 && hostBlasts.BorrowedCount == 0 && clientBlasts.BorrowedCount == 0, "the blast and its copy went back after their Ending");
            // refusals are per strike
            policy.Ids.Add(victim.EntityId.Value); sink.Relay = null; sink.Sent.Clear();
            hostBlasts.Create(new PNBlastCreateRequest(PNBlastStrikesProbe.Id, new JObject(), source, scene, Vector3.zero, owner, PNBlastDependency.Independent));
            Advance(.1f); long secondId = SpawnId(PNObjectKind.Blast); Advance(.5f);
            Check(sink.Sent.Count == 2, "two strikes so far, two claims sent (" + sink.Sent.Count + ")");
            Check(hostBlasts.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Blast, secondId, 1), victim, 1.5f), "strike 1 is claimed on its own ticket");
            Check(!hostBlasts.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Blast, secondId, 1), victim, 1.5f) && book.Refusals.ContainsKey("again"), "the same strike cannot be claimed twice");
            Check(hostBlasts.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Blast, secondId, 0), victim, 1.5f), "strike 0 is still its own claim after strike 1 was taken");
            Check(!hostBlasts.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Blast, secondId, 5), victim, 1.5f), "a strike that has not happened has no ticket");
            Advance(1.2f);

            // ================= V3: area =================
            policy.Ids.Add(victim.EntityId.Value); sink.Sent.Clear(); sink.Relay = null;
            var slow = victim.GetComponent<EveTimeScale>();
            hostAreas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), AreaInput(20), source, scene, Vector3.zero)); Advance(.2f);
            long areaId = SpawnId(PNObjectKind.Area);
            Check(areaId > 0 && clientAreas.ReplicaCount == 1, "the copy of the area is on the player's machine");
            Advance(1.5f);
            Check(sink.Sent.Count == 1 && sink.Sent[0].Kind == PNObjectKind.Area && sink.Sent[0].Hit == 1, "his copy told that he stands inside (" + string.Join(",", sink.Sent.Select(x => x.Kind + ":" + x.Hit)) + ")");
            Check(slow.ModifierCount == 0, "the authority did not put him in the field by its own test while his machine decides");
            Check(hostAreas.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Area, areaId, 1), victim, 1.5f), "the authority takes his presence claim");
            Advance(1.5f);
            Check(slow.ModifierCount > 0, "the field slows the player while his machine says he is inside");
            Check(!hostAreas.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Area, areaId, 1), far, 1.5f), "a claim of presence from 30 m away is refused");

            // he leaves: his copy says so, the authority lets him go
            sink.Relay = (kind, id, hit) => { if (kind == PNObjectKind.Area) hostAreas.AcceptHitReport(PNHitReport.TakenFromObject(PNObjectKind.Area, id, hit), victim, 1.5f); };
            victim.transform.position = new Vector3(10, 0, 0); Physics.SyncTransforms(); Advance(1.5f);
            Check(sink.Sent.Count >= 2 && sink.Sent[sink.Sent.Count - 1].Hit == 0, "his copy told that he left");
            Check(slow.ModifierCount == 0, "the field released him once his machine said he left");

            // he comes back, then his machine falls silent: the authority tests him itself
            victim.transform.position = Vector3.zero; Physics.SyncTransforms(); Advance(1.5f);
            Check(slow.ModifierCount > 0, "back inside, slowed again");
            policy.Ids.Clear(); victim.transform.position = new Vector3(10, 0, 0); Physics.SyncTransforms(); Advance(1.5f);
            Check(slow.ModifierCount == 0, "a player handed back is tested by the authority: outside, no slow");
            victim.transform.position = Vector3.zero; Physics.SyncTransforms(); Advance(1.5f);
            { var hits = new Collider[8]; int seen = scene.GetPhysicsScene().OverlapSphere(Vector3.zero, 2f, hits, ~0, QueryTriggerInteraction.Ignore); Check(slow.ModifierCount > 0, "handed back and inside, the authority's own test slows him (overlap sees " + seen + ", pos " + victim.transform.position + ", ids " + policy.Ids.Count + ", mods " + slow.ModifierCount + ", areas " + hostAreas.ActiveCount + ")"); }

            // ---- the record refuses a negative mask (V2 records carry the blast's targets) ----
            Advance(22f);
            Check(hostBlasts.ActiveCount == 0 && hostAreas.ActiveCount == 0 && clientBlasts.ActiveCount == 0 && clientAreas.ActiveCount == 0, "nothing is left running");
        }
        finally
        {
            PNCombatEvents.Observed -= onEvent; PNCombatEvents.Early -= onEarly;
            try { foreach (var manager in new[] { hostBlasts, clientBlasts }) if (manager != null) manager.ObjectSync = null; foreach (var manager in new[] { hostAreas, clientAreas }) if (manager != null) manager.ObjectSync = null; } catch (Exception error) { Debug.LogException(error); }
            foreach (var manager in new[] { hostBlasts, clientBlasts }) try { manager?.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            foreach (var manager in new[] { hostAreas, clientAreas }) try { manager?.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            foreach (var obj in objects.ToArray())
                if (obj != null && obj.GetComponent<EveEntityLoader>() != null) { try { obj.GetComponent<EveEntityLoader>().Unload(); obj.GetComponent<EveEntityIdentity>()?.ResetIdentity(); } catch (Exception error) { Debug.LogException(error); } }
            hostSync?.Dispose(); clientSync?.Dispose();
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var obj in objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(anchor); }
        }
        return "PASS " + count + " hurt claim blast/area checks (blast: the player's machine decides, one claim, prepared damage once, grace, refusals, handed back; area: presence in / out, authority keeps him in, handed back)";
    }
}
#endif
