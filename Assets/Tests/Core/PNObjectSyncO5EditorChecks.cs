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

// Native checks of spawned-object sync stage O5 (Docs/SpawnedObjectSyncPlan.md): a blast or an area the authority makes is recorded, and another machine builds a view-only copy of it.
// The copy of a blast plays its telegraph and detonation and deals no damage; the copy of an area is drawn and applies no Buff or clock effect. Both end with the End record, and a late observer is brought up by the baseline.
public static class PNObjectSyncO5EditorChecks
{
    private sealed class Loop : IPNObjectSyncTransport
    {
        public PNObjectSyncService Target; public int Sent;
        public void Send(PNObjectRecord record) { Sent++; Target?.Receive(record); }
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception("Object sync O5 check " + count + ": " + message); count++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNWorldFixtureModule() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Host, EveContentCompatibility.Create(hub, docs, "test:o5"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile());
        var worlds = new PNWorldRuntime(session, scheduler); var anchor = EditorSceneManager.NewPreviewScene();
        var objects = new List<GameObject>(); PNObjectSyncService hostSync = null, clientSync = null, lateSync = null;
        PNBlastManager hostBlasts = null, clientBlasts = null, lateBlasts = null; PNAreaManager hostAreas = null, clientAreas = null, lateAreas = null;
        void Pump(Func<bool> until) { for (int i = 0; i < 1000 && !until(); i++) scheduler.AdvanceFrame(); if (!until()) throw new Exception("Fixture did not drain"); }
        GameObject NewObject(string name) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, anchor); objects.Add(obj); return obj; }
        try
        {
            var world = worlds.Create(EveId.Parse("fixture:world")); Pump(() => world.State == EveWorldState.Ready);
            var context = worlds.Get(world); var scene = context.PhysicsScene; double clock = 100d; long nextId = 992900;
            worlds.BeforeUnload = unloading => { foreach (var manager in new[] { hostBlasts, clientBlasts, lateBlasts }) manager?.ReleaseWorld(unloading); foreach (var manager in new[] { hostAreas, clientAreas, lateAreas }) manager?.ReleaseWorld(unloading); };
            PNBlastManager NewBlasts(string name) { var manager = NewObject(name).AddComponent<PNBlastManager>(); manager.Initialize(session); return manager; }
            PNAreaManager NewAreas(string name) { var manager = NewObject(name).AddComponent<PNAreaManager>(); manager.Initialize(session); return manager; }
            hostBlasts = NewBlasts("O5 host blasts"); clientBlasts = NewBlasts("O5 client blasts"); hostAreas = NewAreas("O5 host areas"); clientAreas = NewAreas("O5 client areas");
            hostSync = new PNObjectSyncService(true, () => clock); clientSync = new PNObjectSyncService(false, () => clock);
            var wire = new Loop { Target = clientSync }; hostSync.Transport = wire;
            hostBlasts.ObjectSync = hostSync; hostAreas.ObjectSync = hostSync; clientBlasts.ObjectSync = clientSync; clientAreas.ObjectSync = clientSync;

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
                clock += seconds;
                foreach (var manager in new[] { hostBlasts, clientBlasts, lateBlasts }) manager?.Advance(seconds);
                foreach (var manager in new[] { hostAreas, clientAreas, lateAreas }) manager?.Advance(seconds);
            }
            JObject BlastInput(double delay, double tail) => JObject.Parse("{'delay':" + delay + ",'tail':" + tail + ",'radius':1,'height':1,'value':{'amount':100},'canCrit':false,'tags':['base:magic']}");
            JObject AreaInput(double duration) => new JObject { ["radius"] = 2, ["reduction"] = .5, ["minimumReduction"] = .2, ["duration"] = duration, ["dependent"] = false, ["targetLayers"] = new JArray("Void"), ["layerMode"] = "Include" };
            IEnumerable<PNTimedBlast> Timed() => scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimedBlast>(true)).Where(x => x.IsRunning);

            var owner = Owner("O5 owner"); var victim = Victim("O5 victim", new Vector3(0, 0, 0)); var source = PNCombatSource.Capture(owner);
            Check(owner.IsReady && victim.IsReady, "the owner and the victim are ready");

            // ---- a blast: recorded on the authority, a copy elsewhere, one detonation, no damage from the copy ----
            float health = Health(victim);
            var made = hostBlasts.Create(new PNBlastCreateRequest(PNBlastGameplayContent.Cylinder, BlastInput(1, .5), source, scene, Vector3.zero, owner, PNBlastDependency.Independent));
            Check(made.Status == PNBlastStatus.Created, "the authority makes the blast (status " + made.Status + ", error " + made.Error + ")");
            Advance(.05f);
            Check(hostSync.SpawnsSent == 1 && wire.Sent == 1 && clientBlasts.ReplicaCount == 1 && hostBlasts.ReplicaCount == 0, "one Spawn record is sent and the other machine builds exactly one copy; the authority has none");
            var copies = Timed().Where(x => x.IsReplica).ToArray();
            Check(copies.Length == 1 && !copies[0].Detonated && Health(victim) == health, "the copy is running before its delay and nothing has been hurt yet");
            Advance(.6f);
            Check(copies[0].Age > .6f - 1e-3f && !copies[0].Detonated && Health(victim) == health, "the copy keeps the pace of the authority's clock through its delay (age " + copies[0].Age + ")");
            Advance(.5f);
            Check(copies[0].Detonated && clientBlasts.ReplicasDetonated == 1, "the copy plays its detonation once");
            float afterOne = Health(victim);
            Check(afterOne < health && copies[0].HitCount == 0, "the victim lost health once, from the authority's blast; the copy hit nobody (" + health + " -> " + afterOne + ")");
            Advance(.6f);
            Check(hostBlasts.ActiveCount == 0 && clientBlasts.ActiveCount == 0 && clientBlasts.ReplicaCount == 0 && hostSync.EndsSent == 1 && Health(victim) == afterOne, "both end with the End record, and nothing was hit twice");

            // ---- a blast ended before it detonates ends its copy the same way ----
            var early = hostBlasts.Create(new PNBlastCreateRequest(PNBlastGameplayContent.Cylinder, BlastInput(2, 0), source, scene, Vector3.zero, owner, PNBlastDependency.Action));
            Advance(.1f); Check(clientBlasts.ReplicaCount == 1, "a second blast has its copy");
            early.Handle.End(); Advance(.05f);
            Check(clientBlasts.ReplicaCount == 0 && clientBlasts.ReplicasDetonated == 1 && Health(victim) == afterOne, "ending the blast early ends its copy without a detonation");

            // ---- an area: a copy is drawn, applies no Buff and no clock effect ----
            var body = Owner("O5 area body"); body.transform.position = new Vector3(0, 0, 0); var buffs = body.GetComponent<PNBuffManager>().Buffs; var timeScale = body.GetComponent<EveTimeScale>();
            Physics.SyncTransforms();
            var area = hostAreas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), AreaInput(2), source, scene, Vector3.zero));
            Check(area.Status == PNAreaStatus.Created, "the authority makes the area");
            Advance(.05f);
            Check(hostSync.SpawnsSent == 3 && clientAreas.ReplicaCount == 1 && hostAreas.ReplicaCount == 0, "the area is recorded once and copied once");
            Check(Math.Abs(timeScale.Scale - .5f) < 1e-3f && buffs.Snapshot().Count == 1, "only the authority's area acts on the body: one Buff, the clock at half (scale " + timeScale.Scale + ", buffs " + buffs.Snapshot().Count + ")");
            Advance(2.2f);
            Check(hostAreas.ActiveCount == 0 && clientAreas.ActiveCount == 0 && clientAreas.ReplicaCount == 0 && buffs.Snapshot().Count == 0 && Math.Abs(timeScale.Scale - 1f) < 1e-3f, "both areas end; the body is free again");

            // ---- the shooter's guess: drawn at the cast, taken over by the record that carries the same cast key (no second area), removed if the authority never confirms ----
            clientAreas.PredictionClock = () => clock;
            PNAreaCreateRequest GuessOf(long castKey) => PNAreaCreateRequest.Replica(EveId.Parse("base:timescale_area"), AreaInput(2), scene, Vector3.zero, null, null, 1d).WithCast(castKey, 0);
            int adoptionsBefore = clientAreas.Adoptions;
            Check(clientAreas.CreateGuess(GuessOf(7001)).Status == PNAreaStatus.Created && clientAreas.GuessCount == 1 && clientAreas.ReplicaCount == 1, "the shooter's machine draws its guess at the cast");
            Check(buffs.Snapshot().Count == 0 && Math.Abs(timeScale.Scale - 1f) < 1e-3f, "a guess only draws: no Buff, no clock effect");
            Check(clientAreas.CreateGuess(GuessOf(7001)).Status == PNAreaStatus.Rejected && clientAreas.ReplicaCount == 1, "a second guess under the same cast name is refused");
            int sentBeforeGuess = hostSync.SpawnsSent;
            hostAreas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), AreaInput(2), source, scene, Vector3.zero).WithCast(7001, 0));
            Advance(.05f);
            Check(hostSync.SpawnsSent == sentBeforeGuess + 1 && clientAreas.Adoptions == adoptionsBefore + 1 && clientAreas.GuessCount == 0 && clientAreas.ReplicaCount == 1, "the authority's record takes the guess over: still one area on the shooter's machine (adoptions " + (clientAreas.Adoptions - adoptionsBefore) + ", copies " + clientAreas.ReplicaCount + ")");
            Check(Math.Abs(timeScale.Scale - .5f) < 1e-3f && buffs.Snapshot().Count == 1, "(and only the authority's area acts on the body)");
            Advance(2.2f);
            Check(clientAreas.ReplicaCount == 0 && hostAreas.ActiveCount == 0 && buffs.Snapshot().Count == 0, "the End record ends the taken-over guess with the real area");

            // ---- a time field's copy slows the non-entity time targets (a blast copy) like the authority's field does; entities still get no Buff from it ----
            var fieldInput = AreaInput(6); fieldInput["targetLayers"] = new JArray("Blast");
            hostAreas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), fieldInput, source, scene, Vector3.zero)); Advance(.05f);
            var slowBlast = hostBlasts.Create(new PNBlastCreateRequest(PNBlastGameplayContent.Cylinder, BlastInput(5, 0), source, scene, Vector3.zero, owner, PNBlastDependency.Independent));
            for (int step = 0; step < 15; step++) Advance(.1f);
            var slowReal = Timed().First(x => !x.IsReplica); var slowCopy = Timed().First(x => x.IsReplica);
            Check(slowReal.Age < 1.1f && Math.Abs(slowCopy.Age - slowReal.Age) < .25f && buffs.Snapshot().Count <= 1, "a blast copy inside the field's copy ages as slowly as the real blast (real " + slowReal.Age + ", copy " + slowCopy.Age + ")");
            slowBlast.Handle.End(); Advance(7f);
            Check(clientAreas.ReplicaCount == 0 && hostAreas.ActiveCount == 0 && clientBlasts.ReplicaCount == 0, "the field and the blast end on both machines");
            clientAreas.CreateGuess(GuessOf(7002)); Advance(.5f);
            Check(clientAreas.GuessCount == 1, "a guess nobody confirms waits a moment");
            Advance(.7f);
            Check(clientAreas.GuessCount == 0 && clientAreas.ActiveCount == 0, "and is removed when the authority never makes the area (the cast was refused)");

            // ---- area guess: a refused cast removes it at once; a taken-over one stays; a guess made at another size takes the record's ----
            clientAreas.CreateGuess(GuessOf(7003));
            Check(clientAreas.GuessCount == 1, "(a guess for a cast the authority will refuse)");
            clientAreas.DiscardPredicted(7003);
            Check(clientAreas.GuessCount == 0 && clientAreas.ActiveCount == 0, "a refused cast removes its guessed area at once, not after the hold");
            clientAreas.CreateGuess(GuessOf(7004));
            hostAreas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), AreaInput(2), source, scene, Vector3.zero).WithCast(7004, 0)); Advance(.05f);
            clientAreas.DiscardPredicted(7004);
            Check(clientAreas.Adoptions >= 2 && clientAreas.ActiveCount == 1, "discarding does not touch a guess the record already took over (it is the real area's copy)");
            Advance(2.2f);
            owner.SetSource(EveId.Parse("test:o5_area_scale"), new[] { new EveStatModifier(PNContentIds.AreaScale, 1d, 1, PNContentIds.Scale) });
            clientAreas.CreateGuess(GuessOf(7005));
            hostAreas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), AreaInput(2), PNCombatSource.Capture(owner), scene, Vector3.zero, owner).WithCast(7005, 0)); Advance(.05f);
            var adoptedArea = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimeScaleArea>(true)).Where(x => x.IsRunning && x.IsReplica).ToArray();
            Check(clientAreas.AdoptScaleFixes == 1 && adoptedArea.Length == 1 && Math.Abs(adoptedArea[0].SyncScale - 2d) < 1e-6, "a guess made with area_scale 1 takes the authority's 2 when the record takes it over (fixes " + clientAreas.AdoptScaleFixes + ")");
            owner.SetSource(EveId.Parse("test:o5_area_scale"), Array.Empty<EveStatModifier>()); Advance(2.2f);

            // ---- blast guess: draws, never hurts, taken over by the record, refused / orphaned guesses go, size corrected ----
            clientBlasts.PredictionClock = () => clock;
            PNBlastCreateRequest BlastGuessOf(long castKey, double delay, double tail = 0d) => PNBlastCreateRequest.Replica(PNBlastGameplayContent.Cylinder, BlastInput(delay, tail), scene, Vector3.zero, null, 1d).WithCast(castKey, 0);
            float beforeGuess = Health(victim); int detonatedBefore = clientBlasts.ReplicasDetonated;
            var lonely = clientBlasts.CreateGuess(BlastGuessOf(7010, .3));
            Check(lonely.Status == PNBlastStatus.Created && clientBlasts.GuessCount == 1 && clientBlasts.ReplicaCount == 1, "the shooter's machine draws its blast guess at the cast");
            Check(clientBlasts.CreateGuess(BlastGuessOf(7010, .3)).Status == PNBlastStatus.Rejected, "a second guess under the same cast name is refused");
            Advance(.4f);
            Check(clientBlasts.ReplicasDetonated == detonatedBefore + 1 && Health(victim) == beforeGuess, "the guess plays its detonation and hurts nobody");
            Advance(.2f);
            int sentBeforeBlast = hostSync.SpawnsSent; int blastAdoptions = clientBlasts.Adoptions; float beforeReal = Health(victim);
            clientBlasts.CreateGuess(BlastGuessOf(7011, 1, .5));
            hostBlasts.Create(new PNBlastCreateRequest(PNBlastGameplayContent.Cylinder, BlastInput(1, .5), source, scene, Vector3.zero, owner, PNBlastDependency.Independent).WithCast(7011, 0));
            Advance(.05f);
            Check(hostSync.SpawnsSent == sentBeforeBlast + 1 && clientBlasts.Adoptions == blastAdoptions + 1 && clientBlasts.GuessCount == 0 && clientBlasts.ReplicaCount == 1, "the authority's record takes the blast guess over: still one blast on the shooter's machine (adoptions " + (clientBlasts.Adoptions - blastAdoptions) + ", copies " + clientBlasts.ReplicaCount + ")");
            Advance(1.1f);
            Check(Health(victim) < beforeReal && Timed().Where(x => x.IsReplica).All(x => x.HitCount == 0), "the victim is hurt once, by the authority's blast; the taken-over guess hit nobody");
            Advance(.6f);
            Check(clientBlasts.ActiveCount == 0 && hostBlasts.ActiveCount == 0 && clientBlasts.ReplicaCount == 0, "the End record ends the taken-over guess with the real blast");
            clientBlasts.CreateGuess(BlastGuessOf(7012, 5)); clientBlasts.DiscardPredicted(7012);
            Check(clientBlasts.GuessCount == 0 && clientBlasts.ActiveCount == 0, "a refused cast removes its guessed blast at once");
            clientBlasts.CreateGuess(BlastGuessOf(7013, 5)); Advance(.5f);
            Check(clientBlasts.GuessCount == 1, "a blast guess nobody confirms waits a moment");
            Advance(.7f);
            Check(clientBlasts.GuessCount == 0 && clientBlasts.ActiveCount == 0, "and is removed when the authority never makes the blast");
            var held = clientBlasts.CreateGuess(BlastGuessOf(7014, 5)); held.Handle.End();
            Check(!held.Handle.IsAlive && clientBlasts.GuessCount == 0, "the Action that made a guess can end it through its receipt, as it ends the real blast");
            owner.SetSource(EveId.Parse("test:o5_blast_scale"), new[] { new EveStatModifier(PNContentIds.BlastScale, 1d, 1, PNContentIds.Scale) });
            clientBlasts.CreateGuess(BlastGuessOf(7015, 1));
            hostBlasts.Create(new PNBlastCreateRequest(PNBlastGameplayContent.Cylinder, BlastInput(1, 0), PNCombatSource.Capture(owner), scene, Vector3.zero, owner, PNBlastDependency.Independent).WithCast(7015, 0)); Advance(.05f);
            var adoptedBlast = Timed().Where(x => x.IsReplica).ToArray();
            Check(clientBlasts.AdoptScaleFixes == 1 && adoptedBlast.Length == 1 && Math.Abs(adoptedBlast[0].Radius - 2f) < 1e-4f, "a blast guess made with blast_scale 1 takes the authority's 2 (radius " + (adoptedBlast.Length > 0 ? adoptedBlast[0].Radius : -1f) + ")");
            owner.SetSource(EveId.Parse("test:o5_blast_scale"), Array.Empty<EveStatModifier>()); Advance(1.2f);

            // ---- a late observer is brought up by the baseline, then follows the End ----
            var running = hostBlasts.Create(new PNBlastCreateRequest(PNBlastGameplayContent.Cylinder, BlastInput(3, 0), source, scene, Vector3.zero, owner, PNBlastDependency.Independent));
            Advance(.8f);
            lateBlasts = NewBlasts("O5 late blasts"); lateAreas = NewAreas("O5 late areas"); lateSync = new PNObjectSyncService(false, () => clock);
            lateBlasts.ObjectSync = lateSync; lateAreas.ObjectSync = lateSync;
            var objectWorld = default(PNObjectWorld);
            foreach (var candidate in session.Worlds.Snapshot()) if (candidate.PhysicsOwner.IsValid() && candidate.PhysicsOwner == scene) objectWorld = PNObjectWorld.Of(candidate);
            var baseline = hostSync.Baseline(objectWorld);
            Check(baseline.Count == 1 && baseline[0].Kind == PNObjectKind.Blast && baseline[0].Age > .7f, "the authority's baseline holds the running blast with its age (" + (baseline.Count > 0 ? baseline[0].Age : -1f) + ")");
            lateSync.ApplyBaseline(objectWorld, baseline); lateBlasts.Advance(.01f);
            var lateCopy = Timed().Where(x => x.IsReplica).ToArray();
            Check(lateBlasts.ReplicaCount == 1 && lateCopy.Any(x => x.Age > .7f), "the late observer's copy starts at the blast's age, not at zero");
            wire.Target = clientSync; running.Handle.End(); Advance(.05f);
            Check(clientBlasts.ReplicaCount == 0, "(the blast ended early again)");
            lateSync.Receive(PNObjectRecord.End(PNObjectKind.Blast, baseline[0].Id, objectWorld, (byte)PNBlastEndReason.Requested, baseline[0].Position)); lateBlasts.Advance(.01f);
            Check(lateBlasts.ReplicaCount == 0, "the End record ends the late copy too");

            // ---- a record too long for the wire is refused, not cut ----
            var big = new JObject { ["delay"] = 1, ["tail"] = 0, ["radius"] = 1, ["height"] = 1, ["value"] = new JObject { ["amount"] = 1 }, ["tags"] = new JArray("base:magic"), ["pad"] = new string('x', PNObjectSyncLimits.InputChars + 10) };
            int refused = hostSync.Refused;
            int sentBefore = hostSync.SpawnsSent;
            var bigReceipt = hostBlasts.Create(new PNBlastCreateRequest(PNBlastGameplayContent.Cylinder, big, source, scene, Vector3.zero, owner, PNBlastDependency.Action));
            Advance(.05f);
            Check((bigReceipt.Status != PNBlastStatus.Created || hostSync.Refused == refused + 1) && hostSync.SpawnsSent == sentBefore && clientBlasts.ReplicaCount == 0,
                "a blast whose input does not fit the record is refused (by the blast itself or counted by the sync) and never reaches the copy side (status " + bigReceipt.Status + ", refused " + (hostSync.Refused - refused) + ", sent " + (hostSync.SpawnsSent - sentBefore) + ")");
            Advance(3f);
            Check(hostAreas.BorrowedCount == 0 && clientAreas.BorrowedCount == 0 && lateAreas.BorrowedCount == 0 && hostBlasts.BorrowedCount == 0 && clientBlasts.BorrowedCount == 0 && lateBlasts.BorrowedCount == 0 && PNTimeScaleAreaRendering.RegisteredCount == 0,
                "every real area / blast, copy and guess has gone back to its pool once its fade is over, and no visual registration is left (borrowed host " + hostAreas.BorrowedCount + "/" + hostBlasts.BorrowedCount + ", client " + clientAreas.BorrowedCount + "/" + clientBlasts.BorrowedCount + ", late " + lateAreas.BorrowedCount + "/" + lateBlasts.BorrowedCount + ", registered " + PNTimeScaleAreaRendering.RegisteredCount + ")");
            return "PASS " + count + " object sync O5 checks (blast and area records, view-only copies, no damage or Buff from a copy, End, late baseline, long input refused)";
        }
        finally
        {
            foreach (var manager in new[] { hostBlasts, clientBlasts, lateBlasts }) try { if (manager != null) manager.ObjectSync = null; manager?.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            foreach (var manager in new[] { hostAreas, clientAreas, lateAreas }) try { if (manager != null) manager.ObjectSync = null; manager?.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            foreach (var obj in objects.ToArray())
                if (obj != null && obj.GetComponent<EveEntityLoader>() != null) { try { obj.GetComponent<EveEntityLoader>().Unload(); obj.GetComponent<EveEntityIdentity>()?.ResetIdentity(); } catch (Exception error) { Debug.LogException(error); } UnityEngine.Object.DestroyImmediate(obj); }
            hostSync?.Dispose(); clientSync?.Dispose(); lateSync?.Dispose();
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var obj in objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(anchor); }
        }
    }
}
#endif
