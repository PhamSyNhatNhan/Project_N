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

// A projectile that only writes down the impacts it is told of (Docs/SpawnedObjectSyncPlan.md 4.4b): the engine decides nothing about how an impact looks.
public sealed class PNImpactProbeProjectile : PNDirectionalProjectile
{
    public static readonly List<(PNImpact Impact, bool Replica, long Cast)> Log = new List<(PNImpact, bool, long)>();
    protected override void OnImpact(PNImpact impact) { Log.Add((impact, IsReplica, Launch != null ? Launch.CastKey : 0L)); base.OnImpact(impact); }
}

// Native checks of the impact API: OnImpact is told once per impact on every machine that shows the projectile (the real one, the shooter's guess, a copy), with the point, the normal, the heading and the target.
public static class PNProjectileImpactEditorChecks
{
    private sealed class Loop : IPNObjectSyncTransport
    {
        public readonly List<PNObjectRecord> All = new List<PNObjectRecord>(); public PNObjectSyncService Target;
        public void Send(PNObjectRecord record) { All.Add(record); Target?.Receive(record); }
    }
    private sealed class Sink : IPNHitReportSink
    {
        public Action<long, PNStat, int> Relay;
        public PNStat Who; public PNStat Claimant => Who;
        public bool SendHitReport(in PNHitReport report, IReadOnlyList<EveEntityId> victims) { foreach (var victim in victims) Relay?.Invoke(report.CastKey, Find(victim.Value), report.Step); return true; }
        // The test entities live in a preview scene, which FindObjectsByType does not see: look in the shooter's scene.
        private PNStat Find(long id) => Who == null ? null : Who.gameObject.scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNStat>()).FirstOrDefault(x => x.EntityId.Value == id);
    }
    private sealed class ImpactModule : EveIContentModule
    {
        public static readonly EveId Shot = EveId.Parse("test:impact_shot"), Short = EveId.Parse("test:impact_short"), Pierce = EveId.Parse("test:impact_pierce");
        private static void Add(EveRegistryHub hub, EveId id, string payload)
            => hub.Instances().Register(new PNProjectileDefinition(id, typeof(PNImpactProbeProjectile), JObject.Parse(payload), PNDirectionalSettings.ValidateDefinition, PNDirectionalSettings.ValidateInput));
        public void Register(EveRegistryHub hub)
        {
            Add(hub, Shot, "{'speed':10,'acceleration':0,'speedMin':0,'speedMax':200,'life':{'mode':'Time','value':5},'volume':{'size':[0.4,0.4,0.4]},'environmentMask':['Wall'],'hitLimit':1}");
            Add(hub, Short, "{'speed':10,'acceleration':0,'speedMin':0,'speedMax':200,'life':{'mode':'Time','value':.3},'volume':{'size':[0.4,0.4,0.4]},'environmentMask':['Wall'],'hitLimit':1}");
            Add(hub, Pierce, "{'speed':10,'acceleration':0,'speedMin':0,'speedMax':200,'life':{'mode':'Time','value':5},'volume':{'size':[0.4,0.4,0.4]},'environmentMask':['Wall'],'hitLimit':2}");
        }
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception("Impact check " + count + ": " + message); count++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNWorldFixtureModule(), new ImpactModule() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Host, EveContentCompatibility.Create(hub, docs, "test:projectile_impact"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile());
        var worlds = new PNWorldRuntime(session, scheduler); var anchor = EditorSceneManager.NewPreviewScene();
        var objects = new List<GameObject>(); PNProjectileManager host = null, client = null; PNObjectSyncService hostSync = null, clientSync = null;
        PNImpactProbeProjectile.Log.Clear();
        void Pump(Func<bool> until) { for (int i = 0; i < 1000 && !until(); i++) scheduler.AdvanceFrame(); if (!until()) throw new Exception("Fixture did not drain"); }
        GameObject NewObject(string name) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, anchor); objects.Add(obj); return obj; }
        PNProjectileManager NewManager(string name)
        {
            var manager = NewObject(name).AddComponent<PNProjectileManager>(); manager.Initialize(session, new PNProjectileProfile()); manager.ManualAdvance = true; return manager;
        }
        try
        {
            var world = worlds.Create(EveId.Parse("fixture:world")); Pump(() => world.State == EveWorldState.Ready);
            var scene = worlds.Get(world).PhysicsScene; double clock = 100d; long nextId = 990900;
            host = NewManager("Impact host projectiles"); client = NewManager("Impact client projectiles");
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
            var shooter = Entity(new Vector3(0, 1, 60), PNLayers.Void, "Impact shooter");
            var source = PNCombatSource.Capture(shooter); var layers = PNTargetLayers.Of(PNLayers.Enemy);
            var prepared = source.Snapshot.PrepareDamage(10, false, 0d, PNContentIds.True);
            PNProjectileLaunch Real(EveId id, long cast, bool shooterDecides = false) => new PNProjectileLaunch(id, null, source, layers, prepared).WithCast(cast, shooterDecides);
            PNProjectileLaunch Guess(EveId id, long cast, Sink sink = null) { if (sink != null) { sink.Who = shooter; client.RegisterClaimSink(sink); } return new PNProjectileLaunch(id, null, source, layers).AsPrediction(cast); }
            PNProjectileSpawn Spawn(Vector3 position, Vector3 direction) => new PNProjectileSpawn(scene, position, direction);
            void Advance(float seconds) { clock += seconds; client.Advance(seconds); host.Advance(seconds); }
            void Settle(int steps = 40) { for (int i = 0; i < steps; i++) Advance(.05f); }
            var wall = NewObject("Impact wall"); SceneManager.MoveGameObjectToScene(wall, scene); wall.layer = LayerMask.NameToLayer("Wall");
            wall.transform.position = new Vector3(20, 1, 30); wall.AddComponent<BoxCollider>().size = new Vector3(1, 6, 6); Physics.SyncTransforms();
            Advance(.05f);
            List<(PNImpact Impact, bool Replica, long Cast)> For(long cast, bool replica) => PNImpactProbeProjectile.Log.Where(x => x.Cast == cast && x.Replica == replica).ToList();

            // Hit: the real projectile and its copy are each told once, with the point, a normal against the heading, the heading and the entity.
            var enemy = Entity(new Vector3(3, 1, 10), PNLayers.Enemy, "Impact enemy");
            Advance(.05f); host.Create(Real(ImpactModule.Shot, 8001), Spawn(new Vector3(0, 1, 10), Vector3.right)); Settle();
            var hostHit = For(8001, false); var copyHit = For(8001, true);
            Check(hostHit.Count == 1 && copyHit.Count == 1, "a hit is told once on the real projectile and once on its copy (" + hostHit.Count + ", " + copyHit.Count + ")");
            Check(hostHit[0].Impact.Kind == PNImpactKind.Target && hostHit[0].Impact.Origin == PNImpactOrigin.Real && hostHit[0].Impact.TargetId == enemy.EntityId.Value && hostHit[0].Impact.HitIndex == 0, "the real impact is a Target impact with the hit entity and index 0");
            Check(hostHit[0].Impact.Target == enemy && hostHit[0].Impact.Normal.x < -.5f && hostHit[0].Impact.Heading.x > .9f, "(it carries the live entity, a normal against the heading and the heading)");
            Check(copyHit[0].Impact.Kind == PNImpactKind.Target && copyHit[0].Impact.Origin == PNImpactOrigin.Copy && copyHit[0].Impact.TargetId == enemy.EntityId.Value && copyHit[0].Impact.Target == enemy, "the copy is told the same kind, the entity and that it is a copy");
            Check(Vector3.Distance(copyHit[0].Impact.Point, hostHit[0].Impact.Point) < .01f && Vector3.Distance(copyHit[0].Impact.Normal, hostHit[0].Impact.Normal) < .01f, "(at the point and with the normal the authority had: the End record carries them)");

            // Wall: a wall impact on both, with the wall's normal and a point on its face.
            host.Create(Real(ImpactModule.Shot, 8002), Spawn(new Vector3(10, 1, 30), Vector3.right)); Settle();
            var hostWall = For(8002, false); var copyWall = For(8002, true);
            Check(hostWall.Count == 1 && copyWall.Count == 1 && hostWall[0].Impact.Kind == PNImpactKind.Wall && copyWall[0].Impact.Kind == PNImpactKind.Wall, "a wall is one Wall impact on each machine");
            Check(hostWall[0].Impact.TargetId == 0 && hostWall[0].Impact.Normal.x < -.9f && Mathf.Abs(hostWall[0].Impact.Point.x - 19.5f) < .3f, "the real wall impact sits on the wall face with its normal (" + hostWall[0].Impact.Point + ", " + hostWall[0].Impact.Normal + ")");
            Check(Vector3.Distance(copyWall[0].Impact.Point, hostWall[0].Impact.Point) < .01f && copyWall[0].Impact.Normal.x < -.9f, "the copy's wall impact is at the authority's point with the wall's normal");

            // Expiry and clearing: Expired at the end of the life, Cancelled when another system clears it, nothing when it is only asked to stop (a teardown is not an impact).
            host.Create(Real(ImpactModule.Short, 8003), Spawn(new Vector3(0, 1, 40), Vector3.right)); Settle();
            Check(For(8003, false).Count == 1 && For(8003, false)[0].Impact.Kind == PNImpactKind.Expired && For(8003, true).Count == 1 && For(8003, true)[0].Impact.Kind == PNImpactKind.Expired, "the end of a life is one Expired impact on each machine");
            var cleared = host.Create(Real(ImpactModule.Shot, 8004), Spawn(new Vector3(0, 1, 45), Vector3.right)); Advance(.1f);
            host.Destroy(cleared.Handle); Settle(4);
            Check(For(8004, false).Count == 1 && For(8004, false)[0].Impact.Kind == PNImpactKind.Cancelled && For(8004, true).Count == 1 && For(8004, true)[0].Impact.Kind == PNImpactKind.Cancelled, "a projectile cleared by gameplay is one Cancelled impact on each machine");
            var stopped = host.Create(Real(ImpactModule.Shot, 8005), Spawn(new Vector3(0, 1, 47), Vector3.right)); Advance(.1f);
            stopped.Handle.End(); Settle(4);
            Check(PNImpactProbeProjectile.Log.All(x => x.Cast != 8005), "a plain stop (teardown) is not an impact");

            // Piercing: each hit of the real projectile is told; the copy knows only the last one (its End record).
            Entity(new Vector3(9, 1, 12), PNLayers.Enemy, "Impact first pierced enemy");
            var second = Entity(new Vector3(13, 1, 12), PNLayers.Enemy, "Impact second enemy"); Advance(.05f);
            host.Create(Real(ImpactModule.Pierce, 8006), Spawn(new Vector3(0, 1, 12), Vector3.right)); Settle();
            var pierceHost = For(8006, false);
            Check(pierceHost.Count == 2 && pierceHost[0].Impact.HitIndex == 0 && pierceHost[1].Impact.HitIndex == 1 && pierceHost[1].Impact.TargetId == second.EntityId.Value, "a piercing projectile tells each hit on the real object, in order (cast key and index name the hits)");
            Check(For(8006, true).Count == 2 && For(8006, true)[0].Impact.HitIndex == 0 && For(8006, true)[1].Impact.HitIndex == 1 && For(8006, true)[1].Impact.TargetId == second.EntityId.Value, "(its copy is told each hit too, by the combat event that carries the hit key, and the End record adds nothing)");

            // The shooter's guess: it tells its own hit at once (Guess), and the record that takes it over adds nothing.
            var sink = new Sink(); var shooterEnemy = Entity(new Vector3(3, 1, 20), PNLayers.Enemy, "Impact guess enemy"); Advance(.05f);
            sink.Relay = (cast, target, step) => host.AcceptHitReport(PNHitReport.DealtByCast(cast, step), target, .5f);
            client.Create(Guess(ImpactModule.Shot, 8007, sink), Spawn(new Vector3(0, 1, 20), Vector3.right));
            host.Create(Real(ImpactModule.Shot, 8007, true), Spawn(new Vector3(0, 1, 20), Vector3.right)); Settle();
            var guessHit = PNImpactProbeProjectile.Log.Where(x => x.Cast == 8007 && x.Impact.Origin == PNImpactOrigin.Guess).ToList();
            Check(guessHit.Count == 1 && guessHit[0].Impact.Kind == PNImpactKind.Target && guessHit[0].Impact.TargetId == shooterEnemy.EntityId.Value, "the guess is told its own hit once");
            Check(PNImpactProbeProjectile.Log.Count(x => x.Cast == 8007 && x.Impact.Origin == PNImpactOrigin.Copy) == 0, "(and the record that took it over adds no impact on that machine)");

            client.UnregisterClaimSink(sink);     // the later guesses of this test report nothing
            // A guess that saw no hit but whose real projectile did: the End record tells it (once), from the host's account.
            var missEnemy = Entity(new Vector3(3, 1, 25), PNLayers.Enemy, "Impact missed enemy"); Advance(.05f);
            var offset = Guess(ImpactModule.Shot, 8008, null);
            client.Create(offset, Spawn(new Vector3(0, 6, 25), Vector3.right));
            host.Create(Real(ImpactModule.Shot, 8008), Spawn(new Vector3(0, 1, 25), Vector3.right)); Settle();
            var late = PNImpactProbeProjectile.Log.Where(x => x.Cast == 8008 && x.Impact.Origin == PNImpactOrigin.Guess).ToList();
            Check(late.Count == 1 && late[0].Impact.Kind == PNImpactKind.Target && late[0].Impact.TargetId == missEnemy.EntityId.Value, "a guess that saw no hit is told the host's hit once by the record");

            // A record that does not carry the account (an old End) leaves the impact with the copy's own place and no normal.
            var record = PNObjectRecord.End(PNObjectKind.Projectile, 5, new PNObjectWorld(Guid.NewGuid(), 1), 2, new PNObjectVector(1, 2, 3));
            Check(record.Direction.SqrLength == 0f && record.Target == 0 && record.EstimatedBytes == 52, "an End record without an impact account has no normal and no target");
            return "PASS " + count + " projectile impact checks (hit, wall, expiry, clear, teardown, pierce, guess, record account)";
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
