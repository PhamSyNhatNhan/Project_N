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

// Native checks of the spawned-object sync for projectiles (Docs/SpawnedObjectSyncPlan.md O2): the authority's manager records what it creates, a second manager builds view-only
// copies from the records, the copies fly like the real ones, deal no damage and end only on a record (or their own lifetime), a late observer is brought up from a baseline, and a
// bad record is dropped without an exception. One process plays both machines: two managers of one session, two services, a loopback that can be paused.
public static class PNObjectSyncEditorChecks
{
    private sealed class Loop : IPNObjectSyncTransport
    {
        public readonly List<PNObjectRecord> Held = new List<PNObjectRecord>(); public readonly List<PNObjectRecord> All = new List<PNObjectRecord>();
        public bool Paused; public PNObjectSyncService Target;
        public void Send(PNObjectRecord record) { All.Add(record); if (Paused) Held.Add(record); else Target?.Receive(record); }
        public void Release() { var pending = Held.ToArray(); Held.Clear(); foreach (var record in pending) Target?.Receive(record); }
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int count = 0; var evidence = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS " + message); count++; }
        bool Near(float a, float b, float tolerance = 1e-3f) => Mathf.Abs(a - b) <= tolerance;
        PNProbeProjectile.Reset();
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNWorldFixtureModule(), new PNProjectileNativeModule() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Host, EveContentCompatibility.Create(hub, docs, "test:object_sync"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile());
        var worlds = new PNWorldRuntime(session, scheduler); var anchor = EditorSceneManager.NewPreviewScene();
        var objects = new List<GameObject>(); PNProjectileManager host = null, client = null, late = null; PNBlastManager blasts = null; PNAreaManager areas = null; GameObject shooter = null;
        PNObjectSyncService hostSync = null, clientSync = null, lateSync = null;
        void Pump(Func<bool> until) { for (int i = 0; i < 1000 && !until(); i++) scheduler.AdvanceFrame(); if (!until()) throw new Exception("Fixture did not drain"); }
        GameObject NewObject(string name) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, anchor); objects.Add(obj); return obj; }
        PNProjectileManager NewManager(string name)
        {
            var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, anchor); objects.Add(obj);
            var manager = obj.AddComponent<PNProjectileManager>(); manager.Initialize(session, new PNProjectileProfile()); manager.ManualAdvance = true; return manager;
        }
        try
        {
            var world = worlds.Create(EveId.Parse("fixture:world")); Pump(() => world.State == EveWorldState.Ready);
            var scene = worlds.Get(world).PhysicsScene; double clock = 100d;
            host = NewManager("Host projectiles"); client = NewManager("Client projectiles"); late = NewManager("Late projectiles");
            worlds.BeforeUnload = unloading => { host.ReleaseWorld(unloading); client.ReleaseWorld(unloading); late.ReleaseWorld(unloading); blasts?.ReleaseWorld(unloading); areas?.ReleaseWorld(unloading); };
            hostSync = new PNObjectSyncService(true, () => clock); clientSync = new PNObjectSyncService(false, () => clock); lateSync = new PNObjectSyncService(false, () => clock);
            var wire = new Loop { Target = clientSync }; hostSync.Transport = wire;
            host.ObjectSync = hostSync; client.ObjectSync = clientSync; late.ObjectSync = lateSync;
            var source = PNCombatSource.System(session.SessionId, new EveEntityId(990301));
            PNProjectileReceipt Fire(string id, Vector3 position, Vector3 direction, double speed = 1, double scale = 1, double life = 1)
                => host.Create(new PNProjectileLaunch(EveId.Parse(id), null, source, PNTargetLayers.Of(PNLayers.Enemy), null, null, speed, scale, life), new PNProjectileSpawn(scene, position, direction));
            void Advance(float seconds) { clock += seconds; host.Advance(seconds); client.Advance(seconds); late.Advance(seconds); }
            PNProjectileSnapshot Only(PNProjectileManager manager) => manager.Snapshot().Single();
            IEnumerable<PNProbeProjectile> Copies() => PNProbeProjectile.ByHandle.Values.Where(x => x.IsRunning && x.IsReplica);

            // Spawn: the authority records what it makes, the client builds a copy with the same factors and the same place.
            var first = Fire("test:probe_fast", new Vector3(0, 1, 0), Vector3.right, speed: 2, scale: 3);
            Check(first.Status == PNProjectileStatus.Created && hostSync.SpawnsSent == 1 && hostSync.LiveCount == 1 && wire.All.Count == 1 && wire.All[0].Type == PNObjectRecordType.Spawn, "the authority records a projectile it creates");
            var record = wire.All[0];
            Check(record.Kind == PNObjectKind.Projectile && record.DefinitionId == EveId.Parse("test:probe_fast") && record.Speed == 2d && record.Scale == 3d && record.Lifetime == 1d && record.Owner == 990301 && record.World.Equals(PNObjectWorld.Of(world)) && record.Input == null && record.Age == 0f && record.LifeHint > 0f, "the spawn record carries definition, factors, owner, world and a life hint but no damage");
            Check(client.ReplicaCount == 1 && clientSync.ReplicaCount == 1 && Only(client).IsRunning && Only(client).World == world, "the client builds a copy in the same world");
            var copy = Copies().Single();
            Check(copy.IsReplica && copy.Launch.Source == null && copy.Launch.SourceId.Value == 990301 && copy.Launch.SpeedFactor == 2d && Near(Only(client).HalfExtents.x, Only(host).HalfExtents.x) && Near(Only(client).HalfExtents.x, .6f), "the copy has no source or damage and the size the authority computed");
            Check(Near(Only(client).Position.x, Only(host).Position.x) && Near(Only(client).Forward.x, 1f), "the copy starts where the real projectile started");
            Advance(.1f);
            Check(Near(Only(host).Position.x, Only(client).Position.x) && Near(Only(client).Position.x, 20f) && Near(Only(client).Position.y, 1f), "the copy flies the same path as the real projectile");

            // Retarget: one correction of position and heading, and no search of its own.
            var drift = PNObjectRecord.Retarget(PNObjectKind.Projectile, record.Id, record.World, 0, new PNObjectVector(5, 1, 2), new PNObjectVector(0, 0, 1), clock);
            clientSync.Receive(drift);
            Check(Near(Only(client).Position.x, 5f) && Near(Only(client).Position.z, 2f) && Near(Only(client).Forward.z, 1f), "a retarget record corrects the copy's position and heading once");
            Advance(.1f);
            Check(Near(Only(client).Position.z, 2f + 20f) && Near(Only(client).Position.x, 5f), "the copy keeps the corrected heading afterwards");
            clientSync.Receive(PNObjectRecord.Retarget(PNObjectKind.Projectile, 424242, record.World, 0, new PNObjectVector(0, 0, 0), new PNObjectVector(0, 0, 1), clock));
            Check(clientSync.Stale == 1 && Near(Only(client).Position.x, 5f), "a retarget of an unknown object is stale and changes nothing");

            // End: the authority ends it, the copy ends with the same reason and its object returns to the pool.
            first.Handle.End();
            Check(!first.Handle.IsAlive && hostSync.EndsSent == 1 && hostSync.LiveCount == 0 && client.ReplicaCount == 0 && client.ActiveCount == 0 && client.IdleObjectCount == 1, "an end record ends the copy and returns its object to the pool");

            // No contacts, no damage: a copy keeps flying through a wall until the record that ends it arrives.
            var wall = new GameObject("Sync wall"); objects.Add(wall); wall.layer = LayerMask.NameToLayer("Wall"); wall.transform.position = new Vector3(3, 1, 0); wall.AddComponent<BoxCollider>().size = new Vector3(1, 4, 4);
            SceneManager.MoveGameObjectToScene(wall, scene); Physics.SyncTransforms();
            var blocked = Fire("test:probe_fast", new Vector3(0, 1, 0), Vector3.right);
            Check(client.ReplicaCount == 1, "(setup) the copy of the projectile that will hit the wall exists");
            wire.Paused = true;
            for (int i = 0; i < 4; i++) Advance(.05f);
            Check(!blocked.Handle.IsAlive && hostSync.EndsSent == 2 && wire.Held.Count == 1 && wire.Held[0].Type == PNObjectRecordType.End && wire.Held[0].Reason == (byte)PNProjectileEndReason.Environment, "the real projectile ends at the wall and its end record is held");
            Check(client.ReplicaCount == 1 && Only(client).Position.x > 3.5f, "the copy has no contacts: it flew through the wall while the end record was held");
            wire.Paused = false; wire.Release();
            Check(client.ReplicaCount == 0 && clientSync.ReplicaCount == 0, "the held end record then ends the copy");

            // Lifetime guard: a copy whose end never arrives ends with its own lifetime and the service forgets it.
            var timed = Fire("test:probe", new Vector3(0, 1, -5), Vector3.right);
            Check(client.ReplicaCount == 1, "(setup) the copy of the short-lived projectile exists");
            wire.Paused = true;
            for (int i = 0; i < 6; i++) Advance(.1f);
            Check(!timed.Handle.IsAlive && client.ReplicaCount == 0 && clientSync.ReplicaCount == 0 && wire.Held.Count == 1, "a copy that outlived its lifetime ends by itself and is forgotten");
            int staleBefore = clientSync.Stale; wire.Paused = false; wire.Release();
            Check(clientSync.Stale == staleBefore + 1, "the late end of a copy that already ended is stale");

            // Baseline: a late observer gets the live objects with their state now and flies with them.
            var a = Fire("test:probe_fast", new Vector3(0, 1, 10), Vector3.right, life: 2); var b = Fire("test:probe_fast", new Vector3(0, 1, 12), Vector3.forward, life: 2);
            Advance(.2f);
            var list = hostSync.Baseline(PNObjectWorld.Of(world));
            Check(list.Count == 2 && list.All(x => x.Type == PNObjectRecordType.Spawn && x.Age > .19f && x.Stamp == clock) && list.Any(x => x.Position.X > 15f), "the baseline lists the live projectiles with their state now");
            lateSync.ApplyBaseline(PNObjectWorld.Of(world), list);
            Check(late.ReplicaCount == 2 && late.Snapshot().Count == 2 && late.Snapshot().OrderBy(x => x.Position.z).First().Position.x > 15f, "a late observer builds the copies at the state of the baseline");
            Advance(.1f);
            var hostPositions = host.Snapshot().OrderBy(x => x.Position.z).Select(x => x.Position).ToArray(); var latePositions = late.Snapshot().OrderBy(x => x.Position.z).Select(x => x.Position).ToArray();
            Check(hostPositions.Length == 2 && latePositions.Length == 2 && Vector3.Distance(hostPositions[0], latePositions[0]) < .05f && Vector3.Distance(hostPositions[1], latePositions[1]) < .05f, "the late copies stay with the real projectiles");
            a.Handle.End(); b.Handle.End();
            Check(clientSync.ReplicaCount == 0 && late.ReplicaCount == 2, "(the late observer is not on the wire: it keeps its copies)");
            lateSync.ApplyBaseline(PNObjectWorld.Of(world), hostSync.Baseline(PNObjectWorld.Of(world)));
            Check(late.ReplicaCount == 0, "a baseline that no longer lists them ends the copies");

            // A bad record is dropped, never thrown out of the manager.
            int dropped = clientSync.Dropped;
            clientSync.Receive(PNObjectRecord.Spawn(PNObjectKind.Projectile, 777, new PNObjectWorld(Guid.NewGuid(), 1), EveId.Parse("test:probe"), null, new PNObjectVector(0, 0, 0), new PNObjectVector(0, 0, 1), 0, 0, 0, null, 0, 1, 1, 1, clock, 0f, 1f));
            clientSync.Receive(PNObjectRecord.Spawn(PNObjectKind.Projectile, 778, PNObjectWorld.Of(world), EveId.Parse("test:no_such_projectile"), null, new PNObjectVector(0, 0, 0), new PNObjectVector(0, 0, 1), 0, 0, 0, null, 0, 1, 1, 1, clock, 0f, 1f));
            clientSync.Receive(PNObjectRecord.Spawn(PNObjectKind.Projectile, 779, PNObjectWorld.Of(world), EveId.Parse("test:probe"), "{not json", new PNObjectVector(0, 0, 0), new PNObjectVector(0, 0, 1), 0, 0, 0, null, 0, 1, 1, 1, clock, 0f, 1f));
            Check(clientSync.Dropped == dropped + 3 && client.ReplicaCount == 0 && clientSync.ReplicaCount == 0, "a record for an unknown world, an unknown definition or with broken input is dropped");

            // Areas and blasts: the authority records them too (the copies of these two kinds are built in O5).
            blasts = NewObject("Sync blasts").AddComponent<PNBlastManager>(); blasts.Initialize(session);
            areas = NewObject("Sync areas").AddComponent<PNAreaManager>(); areas.Initialize(session);
            blasts.ObjectSync = hostSync; areas.ObjectSync = hostSync;
            shooter = NewObject("Sync shooter"); shooter.SetActive(false); shooter.layer = PNLayers.Void; SceneManager.MoveGameObjectToScene(shooter, scene);
            var shooterId = shooter.AddComponent<EveEntityIdentity>(); shooterId.Assign(new EveEntityId(990310), EveId.Parse("project_n:dummy"), 0); shooterId.BindWorld(world, "chunk-1"); shooter.AddComponent<CapsuleCollider>().height = 1; shooter.AddComponent<EveTimeScale>();
            var shooterStat = shooter.AddComponent<PNEnemyStat>(); shooter.AddComponent<EveEntityLoader>().Load(hub); shooter.SetActive(true); shooter.GetComponent<PNBuffManager>().Advance(0);
            int sentBefore = hostSync.SpawnsSent; wire.All.Clear(); wire.Paused = true;
            var blast = blasts.Create(new PNBlastCreateRequest(PNBlastGameplayContent.Cylinder, Newtonsoft.Json.Linq.JObject.Parse("{'delay':.3,'tail':.2,'radius':1,'height':1,'value':{'amount':1},'canCrit':false,'tags':['base:magic']}"),
                PNCombatSource.Capture(shooterStat), scene, new Vector3(4, 0, 6), shooterStat));
            var zone = areas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"),
                Newtonsoft.Json.Linq.JObject.Parse("{'radius':3,'reduction':.5,'minimumReduction':0,'duration':.4,'dependent':false,'targetLayers':['Blast'],'layerMode':'Include'}"), PNCombatSource.Capture(shooterStat), scene, new Vector3(-2, 0, 1)));
            Check(blast.Status == PNBlastStatus.Created && zone.Status == PNAreaStatus.Created && hostSync.SpawnsSent == sentBefore + 2, "the authority records a blast and an area it creates (blast " + blast.Status + " " + blast.Error?.Message + ", area " + zone.Status + " " + zone.Error?.Message + ", spawns " + (hostSync.SpawnsSent - sentBefore) + ", refused " + hostSync.Refused + ", dropped " + hostSync.Dropped + ")");
            var blastRecord = wire.All.Single(x => x.Kind == PNObjectKind.Blast); var areaRecord = wire.All.Single(x => x.Kind == PNObjectKind.Area);
            Check(blastRecord.DefinitionId == PNBlastGameplayContent.Cylinder && Near(blastRecord.Position.X, 4f) && Near(blastRecord.Position.Z, 6f) && blastRecord.Scale == 1d && blastRecord.World.Equals(PNObjectWorld.Of(world)) && blastRecord.Input != null && blastRecord.Input.Length < PNObjectSyncLimits.InputChars,
                "the blast record carries its definition, place, scale, world and input");
            Check(areaRecord.DefinitionId == session.Registry.Instances().ResolveIdentity(EveId.Parse("base:timescale_area")) && Near(areaRecord.Position.X, -2f) && areaRecord.Scale == 1d, "the area record carries its definition, place and scale");
            for (int i = 0; i < 12; i++) { clock += .1d; areas.Advance(.1f); blasts.Advance(.1f); }
            var ends = wire.All.Where(x => x.Type == PNObjectRecordType.End).ToArray();
            Check(ends.Length == 2 && ends.Any(x => x.Kind == PNObjectKind.Blast && x.Id == blastRecord.Id) && ends.Any(x => x.Kind == PNObjectKind.Area && x.Id == areaRecord.Id) && hostSync.LiveCount == 0, "each ends with an End record that names it");
            wire.Held.Clear(); wire.Paused = false;

            shooter.GetComponent<EveEntityLoader>().Unload(); shooter.GetComponent<EveEntityIdentity>().ResetIdentity(); UnityEngine.Object.DestroyImmediate(shooter); shooter = null;

            // A world that unloads takes the copies with it.
            var inWorld = Fire("test:probe_fast", new Vector3(0, 1, 20), Vector3.right, life: 5);
            Check(client.ReplicaCount == 1, "(setup) a copy exists before the world unloads");
            Check(worlds.Unload(world) && client.ReplicaCount == 0 && client.BorrowedCount == 0 && host.BorrowedCount == 0, "unloading the world releases the real projectile and its copy");
            Check(!inWorld.Handle.IsAlive, "the unloaded world's projectile is gone");
        }
        finally
        {
            try { if (host != null) host.ObjectSync = null; if (client != null) client.ObjectSync = null; if (late != null) late.ObjectSync = null; } catch (Exception error) { Debug.LogException(error); }
            foreach (var manager in new[] { host, client, late }) try { manager?.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            try { if (blasts != null) { blasts.ObjectSync = null; blasts.Shutdown(); } if (areas != null) { areas.ObjectSync = null; areas.Shutdown(); } } catch (Exception error) { Debug.LogException(error); }
            if (shooter != null) { try { shooter.GetComponent<EveEntityLoader>()?.Unload(); shooter.GetComponent<EveEntityIdentity>()?.ResetIdentity(); } catch (Exception error) { Debug.LogException(error); } UnityEngine.Object.DestroyImmediate(shooter); }
            hostSync?.Dispose(); clientSync?.Dispose(); lateSync?.Dispose();
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var obj in objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(anchor); }
        }
        return "PASS " + count + " spawned-object sync checks (projectile records, view-only copies, retarget, end, late baseline, bad records, world unload)";
    }
}
#endif
