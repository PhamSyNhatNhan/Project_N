#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Native checks of the part of stage O3 that lives below the wire (Docs/SpawnedObjectSyncPlan.md 4.3a and 4.7): the random numbers of an object are seeded by its record id, the authority audits the
// copies and sends a correction when one is off, a copy that declares itself deterministic really flies the authority's path, and every movement kind in the game is covered by such a check.
public static class PNObjectSyncO3EditorChecks
{
    private sealed class Loop : IPNObjectSyncTransport
    {
        public readonly List<PNObjectRecord> All = new List<PNObjectRecord>(); public bool Paused; public PNObjectSyncService Target;
        public void Send(PNObjectRecord record) { All.Add(record); if (!Paused) Target?.Receive(record); }
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
        PNProbeProjectile.Reset();
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNWorldFixtureModule(), new PNProjectileNativeModule() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Host, EveContentCompatibility.Create(hub, docs, "test:object_sync_o3"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile());
        var worlds = new PNWorldRuntime(session, scheduler); var anchor = EditorSceneManager.NewPreviewScene();
        var objects = new List<GameObject>(); PNProjectileManager host = null, client = null; PNObjectSyncService hostSync = null, clientSync = null;
        void Pump(Func<bool> until) { for (int i = 0; i < 1000 && !until(); i++) scheduler.AdvanceFrame(); if (!until()) throw new Exception("Fixture did not drain"); }
        PNProjectileManager NewManager(string name)
        {
            var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, anchor); objects.Add(obj);
            var manager = obj.AddComponent<PNProjectileManager>(); manager.Initialize(session, new PNProjectileProfile()); manager.ManualAdvance = true; return manager;
        }
        try
        {
            var world = worlds.Create(EveId.Parse("fixture:world")); Pump(() => world.State == EveWorldState.Ready);
            var scene = worlds.Get(world).PhysicsScene; double clock = 100d;
            host = NewManager("Host projectiles"); client = NewManager("Client projectiles");
            worlds.BeforeUnload = unloading => { host.ReleaseWorld(unloading); client.ReleaseWorld(unloading); };
            hostSync = new PNObjectSyncService(true, () => clock); clientSync = new PNObjectSyncService(false, () => clock);
            var wire = new Loop { Target = clientSync }; hostSync.Transport = wire; host.ObjectSync = hostSync; client.ObjectSync = clientSync;
            var source = PNCombatSource.System(session.SessionId, new EveEntityId(990401));
            PNProjectileReceipt Fire(string id, Vector3 position, Vector3 direction, double speed = 1, double scale = 1, double life = 1)
                => host.Create(new PNProjectileLaunch(EveId.Parse(id), null, source, PNTargetLayers.Of(PNLayers.Enemy), null, null, speed, scale, life), new PNProjectileSpawn(scene, position, direction));
            // The copy moves first, so a correction made at the end of the real object's tick lands on a copy that is not a tick ahead (on a real network the two tick on their own).
            void Advance(float seconds) { clock += seconds; client.Advance(seconds); host.Advance(seconds); }
            // Both managers number their objects from 1, so PNProbeProjectile.ByHandle cannot tell a real object from its copy: the world's scene lists them.
            IEnumerable<PNProbeProjectile> Running() => scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNProbeProjectile>()).Where(x => x.IsRunning);
            PNProbeProjectile Real() => Running().Single(x => !x.IsReplica);
            PNProbeProjectile Copy() => Running().Single(x => x.IsReplica);

            // 4.7: the object's id seeds its random numbers; the copy of an object has the id of the record, so both draw the same sequence.
            var fired = Fire("test:probe_fast", new Vector3(0, 1, 0), Vector3.right, life: 3);
            var record = wire.All.Single(x => x.Type == PNObjectRecordType.Spawn);
            var real = Real(); var copy = Copy();
            Check(real.ObjectSeed == record.Id && copy.ObjectSeed == record.Id && record.Id > 0, "the real object and its copy are seeded by the id of the spawn record");
            var a = Enumerable.Range(0, 5).Select(_ => real.Random.NextUInt64()).ToArray(); var b = Enumerable.Range(0, 5).Select(_ => copy.Random.NextUInt64()).ToArray();
            Check(a.SequenceEqual(b), "the real object and its copy draw the same random numbers");
            fired.Handle.End();
            var second = Fire("test:probe_fast", new Vector3(0, 1, 5), Vector3.right, life: 3); var seededSecond = Real();
            Check(seededSecond.ObjectSeed == wire.All.Last(x => x.Type == PNObjectRecordType.Spawn).Id && seededSecond.ObjectSeed > record.Id, "a second object gets the next id");
            second.Handle.End();

            // 4.3a: a copy that flies like the real object gets no corrections; one that is off (the real object was pushed) gets one, and then follows.
            wire.All.Clear();
            var steady = Fire("test:probe_fast", new Vector3(0, 1, 20), Vector3.right, life: 3);
            for (int i = 0; i < 20; i++) Advance(.1f);
            Check(wire.All.Count(x => x.Type == PNObjectRecordType.Retarget) == 0 && steady.Handle.IsAlive, "a steady straight object is audited and costs no correction record");
            var steadyReal = Real(); var steadyCopy = Copy();
            Check(Vector3.Distance(steadyReal.Position, steadyCopy.Position) < .05f, "(and its copy is where it is)");
            steadyReal.ApplyCorrection(steadyReal.Position + new Vector3(0, 0, 3), steadyReal.Forward, null);
            Check(Vector3.Distance(steadyReal.Position, steadyCopy.Position) > 2.9f, "(setup) the real object is pushed 3 units off the copy's path");
            for (int i = 0; i < 7; i++) Advance(.1f);
            var corrections = wire.All.Where(x => x.Type == PNObjectRecordType.Retarget).ToArray();
            Check(corrections.Length == 1 && Mathf.Abs(corrections[0].Position.Z - 20f - 3f) < .01f, "the audit sends one correction record for the push (a retarget with the unchanged target)");
            Check(Vector3.Distance(steadyReal.Position, steadyCopy.Position) < .05f, "the correction brings the copy back to the real object");
            // A correction that was made 0.1 s ago (the latency) lands where the object is now: the copy flies the delay, it does not trail by it.
            var late = PNObjectRecord.Retarget(PNObjectKind.Projectile, corrections[0].Id, corrections[0].World, 0, new PNObjectVector(steadyReal.Position.x, steadyReal.Position.y, steadyReal.Position.z), new PNObjectVector(steadyReal.Forward.x, steadyReal.Forward.y, steadyReal.Forward.z), clock - .1d);
            clientSync.Receive(late);
            float expectedLead = steadyReal.Velocity.magnitude * .1f;
            Check(expectedLead > .5f && Mathf.Abs(Vector3.Distance(steadyReal.Position, steadyCopy.Position) - expectedLead) < .05f, "a correction stamped 0.1 s ago puts the copy 0.1 s of flight ahead of the place it names");
            steady.Handle.End();

            // 4.3a: a copy that declares itself deterministic really flies the authority's path (corrections held back so only the replay is measured).
            foreach (var scenario in new[] { ("test:probe", 1d, 1d, 1d), ("test:probe_fast", 1.5d, 2d, 1.2d), ("test:probe_accel", 1d, 1d, 1d), ("test:probe_gravity", 2d, 1d, 1d) })
            {
                wire.All.Clear(); wire.Paused = false;
                var made = Fire(scenario.Item1, new Vector3(0, 1, 40), new Vector3(1, .3f, .2f).normalized, scenario.Item2, scenario.Item3, scenario.Item4);
                var r = Real(); var c = Copy(); wire.Paused = true; float worst = 0f; int steps = 0;
                for (int i = 0; i < 27 && made.Handle.IsAlive; i++) { Advance(1f / 30f); if (!made.Handle.IsAlive) break; worst = Mathf.Max(worst, Vector3.Distance(r.Position, c.Position)); steps++; }
                Check(r.SyncDeterministic && steps >= 6 && worst < .02f, "the copy of " + scenario.Item1 + " replays the authority's path (worst gap " + worst.ToString("0.0000") + " over " + steps + " steps)");
                wire.Paused = false; if (made.Handle.IsAlive) made.Handle.End();
                if (client.ReplicaCount != 0) Advance(.01f);
            }

            // 4.3a: every movement kind in the game is covered. A new one (a curve, a bounce, a boomerang) must be added here with a scenario above before the build passes.
            var covered = new HashSet<string> { "PNDirectionalProjectile", "PNHomingProjectile", "PNTrailHomingProjectile", "PNStarweaverProjectile", "PNStarreaperProjectile", "PNStarforgerBoltProjectile" };
            var unknown = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => { try { return x.GetTypes(); } catch (Exception) { return Type.EmptyTypes; } })
                .Where(x => typeof(PNProjectileInstance).IsAssignableFrom(x) && !x.IsAbstract && x != typeof(PNProjectileInstance) && !covered.Contains(x.Name) && (x.Assembly.GetName().Name == "ProjectN.Game" || x.Assembly.GetName().Name == "ProjectN.Core"))
                .Select(x => x.FullName).ToArray();
            Check(unknown.Length == 0, "every movement kind of Core and Game has a conformance scenario" + (unknown.Length == 0 ? "" : " (missing: " + string.Join(", ", unknown) + ")"));
            // A kind may only claim determinism it was built for: the base class says no, the directional family (the kinds above) says yes.
            Check(typeof(PNProjectileInstance).GetProperty("SyncDeterministic").DeclaringType == typeof(PNProjectileInstance) && typeof(PNDirectionalProjectile).GetProperty("SyncDeterministic").DeclaringType == typeof(PNDirectionalProjectile), "(only the base class and the directional family declare it)");
            return "PASS " + count + " spawned-object O3 checks (seed by id, audit and correction, replay conformance, movement-kind coverage)";
        }
        finally
        {
            try { if (host != null) host.ObjectSync = null; if (client != null) client.ObjectSync = null; } catch (Exception error) { Debug.LogException(error); }
            foreach (var manager in new[] { host, client }) try { manager?.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            hostSync?.Dispose(); clientSync?.Dispose();
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var obj in objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(anchor); PNProbeProjectile.Reset(); }
        }
    }
}
#endif
