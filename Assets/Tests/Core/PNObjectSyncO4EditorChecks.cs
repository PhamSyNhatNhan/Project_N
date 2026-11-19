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

// Native checks of stage O4 (Docs/SpawnedObjectSyncPlan.md 4.4 and 4.4a): the shooter's machine guesses a projectile, the authority's record takes the guess over instead of making a second one, the guess reports
// the hits it sees and the authority's real projectile takes them (it finds no target contacts itself), a refused or orphaned guess goes, a guess that ended before its record swallows the record.
public static class PNObjectSyncO4EditorChecks
{
    private sealed class Loop : IPNObjectSyncTransport
    {
        public readonly List<PNObjectRecord> All = new List<PNObjectRecord>(); public PNObjectSyncService Target;
        public void Send(PNObjectRecord record) { All.Add(record); Target?.Receive(record); }
    }
    private sealed class Sink : IPNSkillReportSink
    {
        public readonly List<(long Cast, long Target, int Step)> Hits = new List<(long, long, int)>(); public Action<long, PNStat, int> Relay;
        public void ReportHit(long castKey, PNStat target, int step) { Hits.Add((castKey, target.EntityId.Value, step)); Relay?.Invoke(castKey, target, step); }
        public void ReportDecision(long castKey, string point, JToken value) { }
    }
    private sealed class O4Module : EveIContentModule
    {
        public void Register(EveRegistryHub hub)
        {
            var registry = hub.Instances();
            var directional = registry.GetKind<PNProjectileDefinition>(PNProjectileGameplayContent.Directional);
            var data = JObject.Parse("{'speed':10,'acceleration':0,'speedMin':0,'speedMax':200,'life':{'mode':'Time','value':5},'volume':{'size':[0.4,0.4,0.4]},'environmentMask':['Wall'],'hitLimit':1}");
            registry.Register(directional.Derive(EveId.Parse("test:o4_shot"), data));
        }
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNWorldFixtureModule(), new O4Module() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Host, EveContentCompatibility.Create(hub, docs, "test:object_sync_o4"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile());
        var worlds = new PNWorldRuntime(session, scheduler); var anchor = EditorSceneManager.NewPreviewScene();
        var objects = new List<GameObject>(); PNProjectileManager host = null, client = null; PNObjectSyncService hostSync = null, clientSync = null;
        void Pump(Func<bool> until) { for (int i = 0; i < 1000 && !until(); i++) scheduler.AdvanceFrame(); if (!until()) throw new Exception("Fixture did not drain"); }
        GameObject NewObject(string name) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, anchor); objects.Add(obj); return obj; }
        PNProjectileManager NewManager(string name)
        {
            var manager = NewObject(name).AddComponent<PNProjectileManager>(); manager.Initialize(session, new PNProjectileProfile()); manager.ManualAdvance = true; return manager;
        }
        try
        {
            var world = worlds.Create(EveId.Parse("fixture:world")); Pump(() => world.State == EveWorldState.Ready);
            var scene = worlds.Get(world).PhysicsScene; double clock = 100d; long nextId = 990500;
            host = NewManager("O4 host projectiles"); client = NewManager("O4 client projectiles");
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
            var shooter = Entity(new Vector3(0, 1, 60), PNLayers.Void, "O4 shooter");
            var source = PNCombatSource.Capture(shooter); var layers = PNTargetLayers.Of(PNLayers.Enemy);
            var prepared = source.Snapshot.PrepareDamage(100, false, 0d, PNContentIds.True);
            double Hp(PNStat stat) => stat.GetCurrent(PNContentIds.Health);
            var shot = EveId.Parse("test:o4_shot");
            PNProjectileLaunch Guess(long cast, Sink sink) => new PNProjectileLaunch(shot, null, source, layers).AsPrediction(cast, sink);
            PNProjectileLaunch Real(long cast, bool shooterDecides) => new PNProjectileLaunch(shot, null, source, layers, prepared).WithCast(cast, shooterDecides);
            PNProjectileSpawn Spawn(Vector3 position, Vector3 direction, int index = 0) => new PNProjectileSpawn(scene, position, direction, null, index);
            void Advance(float seconds) { clock += seconds; client.Advance(seconds); host.Advance(seconds); }
            IEnumerable<PNProjectileInstance> Instances() => scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNProjectileInstance>()).Where(x => x.IsRunning);
            Advance(.05f);

            // The authority never makes a guess; a guess is the machine without authority's.
            var refused = host.Create(Guess(7000, null), Spawn(new Vector3(0, 1, 5), Vector3.right));
            Check(refused.Status == PNProjectileStatus.Rejected && host.Snapshot().Count == 0, "the authority refuses to make a guess");

            // 4.4a 1: the guess flies at once, costs no record, and the authority's record (same cast key and index) takes it over: still one projectile on the shooter's machine.
            var sink = new Sink();
            var made = client.Create(Guess(7001, sink), Spawn(new Vector3(0, 1, 10), Vector3.right));
            Check(made.Status == PNProjectileStatus.Created && client.Snapshot().Count == 1 && wire.All.Count == 0, "the shooter's machine makes the guess at once and sends nothing");
            var guess = Instances().Single();
            Check(guess.IsPrediction && !guess.IsReplica && guess.Launch.CastKey == 7001, "(it is a prediction of cast 7001, not a copy)");
            var real = host.Create(Real(7001, true), Spawn(new Vector3(0, 1, 10), Vector3.right));
            Advance(.05f);
            var spawnRecord = wire.All.Single(x => x.Type == PNObjectRecordType.Spawn);
            Check(real.Status == PNProjectileStatus.Created && spawnRecord.CastKey == "7001" && spawnRecord.CastIndex == 0, "the authority's record names the cast and the index");
            Check(client.Snapshot().Count == 1 && client.ReplicaCount == 1 && Instances().Count() == 2, "the record took the guess over: the shooter's machine still has one projectile (" + client.Snapshot().Count + "), the authority one");
            var authorityInstance = Instances().Single(x => !ReferenceEquals(x, guess));
            Check(guess.ObjectSeed == authorityInstance.ObjectSeed && guess.ObjectSeed != 0, "the guess, the real projectile and any copy are seeded the same (by the cast, not by the sync id)");

            // 4.4a 4: the real projectile of a shooter who guesses finds no target contacts itself; the guess sees the hit and reports it; the authority applies it once.
            var enemy = Entity(new Vector3(3, 1, 10), PNLayers.Enemy, "O4 enemy");
            sink.Relay = (cast, target, step) => host.AcceptReportedHit(cast, step - PNProjectileManager.ReportStepBase, target, .5f);
            Advance(.05f);
            double before = Hp(enemy);
            // Park the real projectile inside the enemy first, without the guess: it must deal nothing (the shooter decides).
            var parked = host.Create(Real(7002, true), Spawn(new Vector3(3, 1, 10), Vector3.right));
            for (int i = 0; i < 3; i++) Advance(.02f);
            Check(parked.Handle.IsAlive && Hp(enemy) == before, "a real projectile of a predicting shooter does not hit an enemy on its own");
            parked.Handle.End();
            for (int i = 0; i < 12 && Hp(enemy) == before; i++) Advance(.05f);
            Check(sink.Hits.Count == 1 && sink.Hits[0].Cast == 7001 && sink.Hits[0].Step == PNProjectileManager.ReportStepBase && sink.Hits[0].Target == enemy.EntityId.Value, "the guess reported the hit under its cast key and step");
            Check(Math.Abs(before - Hp(enemy) - 100d) < 1e-6, "the authority applied the report once: the prepared damage (" + (before - Hp(enemy)) + ")");
            Advance(.1f);
            Check(Instances().Count() == 0 && client.Snapshot().Count == 0 && host.Snapshot().Count == 0, "the hit ended the real projectile and the guess (limit 1)");
            Check(!host.AcceptReportedHit(7001, 0, enemy, .5f) && Math.Abs(before - Hp(enemy) - 100d) < 1e-6, "a second report for the ended projectile is refused (no double damage)");

            // A report for a target far from the real projectile, or on a layer it does not target, is refused.
            var far = Entity(new Vector3(0, 1, 400), PNLayers.Enemy, "O4 far enemy"); var friend = Entity(new Vector3(0, 1, 20), PNLayers.Void, "O4 friend");
            Advance(.05f);
            var aimed = host.Create(Real(7010, true), Spawn(new Vector3(0, 1, 20), Vector3.right)); Advance(.05f);
            Check(!host.AcceptReportedHit(7010, 0, far, .5f) && !host.AcceptReportedHit(7010, 0, friend, .5f) && !host.AcceptReportedHit(7010, 3, far, .5f) && aimed.Handle.IsAlive, "a report of a faraway target, an untargeted layer or another index is refused");
            aimed.Handle.End(); Advance(.05f);

            // 4.4a 3: a refused cast takes its guess back; a guess no record takes over goes after its time.
            var refusedCast = client.Create(Guess(7020, null), Spawn(new Vector3(0, 1, 30), Vector3.right)); Advance(.05f);
            Check(client.Snapshot().Count == 1, "(setup) a guess of cast 7020 flies");
            client.DiscardPredicted(7020); Advance(.05f);
            Check(client.Snapshot().Count == 0 && !refusedCast.Handle.IsAlive, "a refused cast takes its guessed projectile back");
            var orphan = client.Create(Guess(7021, null), Spawn(new Vector3(0, 1, 30), Vector3.right)); Advance(.5f);
            Check(orphan.Handle.IsAlive, "(setup) a guess that no record took over is kept for a while");
            Advance((float)PNProjectileManager.OrphanSeconds); Advance(.05f);
            Check(!orphan.Handle.IsAlive && client.Snapshot().Count == 0, "a guess that no record takes over goes after the orphan time");

            // 4.4a 5: a guess that ended before its record came swallows the record, so no second projectile shows.
            var early = client.Create(Guess(7030, null), Spawn(new Vector3(0, 1, 40), Vector3.right)); Advance(.05f);
            early.Handle.End(); Advance(.05f);
            Check(client.Snapshot().Count == 0, "(setup) the guess ended first");
            var late = host.Create(Real(7030, true), Spawn(new Vector3(0, 1, 40), Vector3.right)); Advance(.1f);
            Check(late.Status == PNProjectileStatus.Created && client.Snapshot().Count == 0 && client.ReplicaCount == 0, "the record of a guess that already ended shows nothing");
            late.Handle.End(); Advance(.05f);

            // 4.4a 2: only a spatial difference moves the guess: the host made its projectile heading 20 degrees off, so the guess turns by that; in time it stays where it is.
            var turned = client.Create(Guess(7040, null), Spawn(new Vector3(0, 1, 50), Vector3.right)); Advance(.05f);
            var turnedGuess = Instances().Single(x => x.Launch.CastKey == 7040); var guessAt = turnedGuess.Position;
            var skewed = Quaternion.AngleAxis(20f, Vector3.up) * Vector3.right;
            var skewedReal = host.Create(Real(7040, true), Spawn(new Vector3(0, 1, 50), skewed)); Advance(.0f);
            Advance(.05f);
            Check(Vector3.Angle(turnedGuess.Forward, skewed) < 3f, "a guess whose heading differs from the record's is turned to it (" + Vector3.Angle(turnedGuess.Forward, skewed) + " degrees apart)");
            Check(Vector3.Distance(turnedGuess.Position, guessAt) < 1.5f, "(and it keeps its place instead of jumping back to the record's)");
            turned.Handle.End(); skewedReal.Handle.End(); Advance(.05f);

            // A projectile nobody on this machine guessed (another player's) is an ordinary copy, seeded as the real one.
            var other = host.Create(Real(7050, false), Spawn(new Vector3(0, 1, 55), Vector3.right)); Advance(.1f);
            Check(client.ReplicaCount == 1 && client.Snapshot().Count == 1, "a projectile nobody guessed is built as an ordinary copy");
            var copySeed = Instances().Where(x => x.IsReplica).Select(x => x.ObjectSeed).Single(); var realSeed = Instances().Where(x => !x.IsReplica).Select(x => x.ObjectSeed).Single();
            Check(copySeed == realSeed && copySeed != 0, "(and its copy draws the same random numbers as the real one)");
            other.Handle.End(); Advance(.1f);
            Check(client.Snapshot().Count == 0 && host.Snapshot().Count == 0, "everything ended");
            return "PASS " + count + " object sync O4 checks (guess, takeover by record, report and one-time hit, refused cast, orphan, ended guess, spatial turn, copies)";
        }
        finally
        {
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
