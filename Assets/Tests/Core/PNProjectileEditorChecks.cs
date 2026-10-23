#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// A directional projectile that records what happens to it, so the native checks can look at pooling, lifetime and contacts.
public sealed class PNProbeProjectile : PNDirectionalProjectile
{
    public static int Created, NextSerial;
    public static readonly List<int> Disposed = new List<int>();
    public static readonly Dictionary<long, PNProbeProjectile> ByHandle = new Dictionary<long, PNProbeProjectile>();
    public static Action<PNProbeProjectile, IReadOnlyList<PNProjectileContact>> OnTargetsHook;
    public int Serial;
    public PNProjectileEndReason? Ended;
    public PNBuffSource EndedBy;
    public readonly List<PNProjectileContact> Targets = new List<PNProjectileContact>();
    public PNProjectileContact? EnvironmentHit;
    public static void Reset() { Created = 0; NextSerial = 0; Disposed.Clear(); ByHandle.Clear(); OnTargetsHook = null; }
    public override void OnPoolCreated() { Serial = ++NextSerial; Created++; }
    protected override void Initialize(PNProjectileSpawn spawn) { base.Initialize(spawn); Ended = null; EndedBy = null; Targets.Clear(); EnvironmentHit = null; ByHandle[Handle.InstanceId] = this; }
    protected override void OnContacts(PNProjectileContacts contacts)
    {
        if (contacts.HasEnvironment) EnvironmentHit = contacts.Environment;
        base.OnContacts(contacts);
    }
    protected override void OnTargets(IReadOnlyList<PNProjectileContact> targets)
    {
        Targets.AddRange(targets);
        OnTargetsHook?.Invoke(this, targets);
    }
    protected override void OnEnd(PNProjectileEndReason reason) { Ended = reason; EndedBy = DestroyedBy; }
    public override void DisposePooledResources() { if (!Disposed.Contains(Serial) || Serial == 0) Disposed.Add(Serial); }
}
public sealed class PNProjectileNativeModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    {
        var registry = hub.Get<PNProjectileDefinition>();
        var data = JObject.Parse("{'speed':10,'acceleration':0,'speedMin':0,'speedMax':200,'life':{'mode':'Time','value':0.5},'volume':{'size':[0.4,0.4,0.4]},'environmentMask':['Wall'],'hitLimit':1}");
        var probe = new PNProjectileDefinition(EveId.Parse("test:probe"), typeof(PNProbeProjectile), data, PNDirectionalSettings.ValidateDefinition, PNDirectionalSettings.ValidateInput);
        registry.Register(probe);
        PNProjectileDefinition Variant(string name, Action<JObject> change) { var copy = (JObject)data.DeepClone(); change(copy); var value = probe.Derive(EveId.Parse("test:" + name), copy); registry.Register(value); return value; }
        Variant("probe_b", d => { });
        Variant("probe_fast", d => { d["speed"] = 100; d["life"] = JObject.Parse("{'mode':'Time','value':5}"); });
        Variant("probe_distance", d => d["life"] = JObject.Parse("{'mode':'Distance','value':3}"));
        Variant("probe_gravity", d => { d["gravity"] = new JArray(0, -10, 0); d["life"] = JObject.Parse("{'mode':'Time','value':5}"); });
        Variant("probe_accel", d => { d["acceleration"] = 20; d["speedMax"] = 15; d["life"] = JObject.Parse("{'mode':'Time','value':5}"); });
        Variant("probe_sub", d => { d["speed"] = 100; d["subSteps"] = 1; d["life"] = JObject.Parse("{'mode':'Time','value':5}"); });
    }
}
public static class PNProjectileEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        int count = 0; var evidence = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS " + message); count++; }
        bool Near(float a, float b, float tolerance = 1e-3f) => Mathf.Abs(a - b) <= tolerance;
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNWorldFixtureModule(), new PNProjectileNativeModule() }, docs);
        int enemyMask = 1 << 7, wall = LayerMask.NameToLayer("Wall"), boundary = LayerMask.NameToLayer("Boundary");
        Check(wall >= 0 && boundary >= 0 && LayerMask.NameToLayer("Enemy") == 7, "Wall, Boundary and Enemy layers exist");
        PNProbeProjectile.Reset();
        Geometry(Check);
        var preview = EditorSceneManager.NewPreviewScene(); var fixtures = new List<GameObject>(); PNProjectileManager manager = null;
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:projectile"));
            var session = EveRegistrySession.Context; var source = new PNBuffSource(session.SessionId, new EveEntityId(990001));
            GameObject Fixture(string name) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, preview); fixtures.Add(obj); return obj; }
            PNProjectileManager NewManager(int cap, int steps = 4)
            {
                var m = Fixture("Projectile manager").AddComponent<PNProjectileManager>(); m.Initialize(session, new PNProjectileProfile { MaxActive = cap, MaxSubSteps = steps }); return m;
            }
            PNProjectileReceipt Fire(PNProjectileManager m, string id, Vector3 position, Vector3 direction, double speed = 1, double scale = 1, double life = 1, PNStat target = null, int mask = 0)
                => m.Create(new PNProjectileLaunch(EveId.Parse(id), null, source, mask == 0 ? enemyMask : mask, null, null, speed, scale, life), new PNProjectileSpawn(preview, position, direction, target));
            PNProbeProjectile Probe(PNProjectileReceipt receipt) => PNProbeProjectile.ByHandle[receipt.Handle.InstanceId];
            PNStat Dummy(string name, long id, Vector3 position, int layer)
            {
                var obj = Fixture(name); obj.SetActive(false); obj.transform.position = position; obj.layer = layer;
                obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:dummy"), 0);
                var stat = obj.AddComponent<PNEnemyStat>(); var box = obj.AddComponent<BoxCollider>(); box.size = Vector3.one;
                obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); return stat;
            }
            manager = NewManager(8);

            // Create, move, expire by time, and reuse the pooled object.
            var first = Fire(manager, "test:probe", Vector3.zero, Vector3.right);
            Check(first.Status == PNProjectileStatus.Created && first.Handle.IsAlive && manager.ActiveCount == 1 && manager.BorrowedCount == 1 && manager.PooledObjectCount == 1, "a projectile is created, borrowed and counted");
            var one = Probe(first);
            manager.Advance(.1f);
            Check(Near(one.Position.x, 1f) && Near(one.transform.position.x, 1f) && Near(one.Age, .1f) && Near(one.Distance, 1f) && one.Forward == Vector3.right, "a tick moves the projectile along its heading by speed times its own clock");
            var snapshots = manager.Snapshot();
            Check(snapshots.Count == 1 && snapshots[0].Handle.InstanceId == first.Handle.InstanceId && snapshots[0].IsRunning && Near(snapshots[0].Position.x, 1f) && Near(snapshots[0].HalfExtents.x, .2f), "the manager lists running projectiles with position, box and handle");
            for (int i = 0; i < 5; i++) manager.Advance(.1f);
            Check(!first.Handle.IsAlive && one.Ended == PNProjectileEndReason.Expired && manager.ActiveCount == 0 && manager.BorrowedCount == 0 && manager.IdleObjectCount == 1, "a Time projectile expires and its object returns idle to the pool");
            var again = Fire(manager, "test:probe", new Vector3(0, 0, 5), Vector3.right);
            var two = Probe(again);
            Check(again.Status == PNProjectileStatus.Created && ReferenceEquals(two, one) && PNProbeProjectile.Created == 1 && manager.PooledObjectCount == 1 && Near(two.Position.z, 5f) && two.Age == 0 && two.Ended == null && two.Targets.Count == 0, "the next projectile reuses the pooled object with its state reset");
            again.Handle.End();

            // Distance lifetime and launch factors.
            var distance = Fire(manager, "test:probe_distance", Vector3.zero, Vector3.right); var far = Probe(distance);
            for (int i = 0; i < 4; i++) manager.Advance(.1f);
            Check(far.Ended == PNProjectileEndReason.Expired && far.Distance >= 3f && far.Distance < 5f, "a Distance projectile expires after its maximum distance");
            var stretched = Fire(manager, "test:probe_distance", Vector3.zero, Vector3.right, life: 2); var stretchedProbe = Probe(stretched);
            for (int i = 0; i < 4; i++) manager.Advance(.1f);
            Check(stretched.Handle.IsAlive && stretchedProbe.Ended == null, "the lifetime factor scales the Distance limit");
            for (int i = 0; i < 3; i++) manager.Advance(.1f);
            Check(!stretched.Handle.IsAlive && stretchedProbe.Ended == PNProjectileEndReason.Expired, "the scaled Distance projectile still expires");
            var factors = Fire(manager, "test:probe", Vector3.zero, Vector3.right, speed: 2, scale: 3); var factorProbe = Probe(factors);
            manager.Advance(.1f);
            Check(Near(factorProbe.Position.x, 2f) && Near(factorProbe.HalfExtents.x, .6f) && Near(factorProbe.HalfExtents.z, .6f), "speed and scale factors multiply velocity and body box");
            factors.Handle.End();
            var accel = Fire(manager, "test:probe_accel", Vector3.zero, Vector3.right); var accelProbe = Probe(accel);
            for (int i = 0; i < 10; i++) manager.Advance(.1f);
            Check(accelProbe.Velocity.x <= 15.0001f && Near(accelProbe.Velocity.x, 15f, .01f), "acceleration is clamped to speedMax");
            accel.Handle.End();
            var gravity = Fire(manager, "test:probe_gravity", Vector3.zero, Vector3.right); var gravityProbe = Probe(gravity);
            for (int i = 0; i < 10; i++) manager.Advance(.1f);
            Check(gravityProbe.Position.y < -4f && gravityProbe.Position.y > -6f && gravityProbe.Velocity == new Vector3(10, 0, 0) && gravityProbe.External.y < 0 && gravityProbe.Forward.y < 0, "gravity is an external velocity that bends the path and heading");
            gravity.Handle.End();

            // The projectile has its own clock: a freeze stops it, and an owner that disappears is reclaimed even at scale 0.
            var timed = Fire(manager, "test:probe", Vector3.zero, Vector3.right); var timedProbe = Probe(timed);
            var owner = Fixture("clock owner"); timedProbe.Clock.AddOwnedModifier(EveId.Parse("test:freeze"), EveTimeModifierKind.Scale, -1f, owner);
            manager.Advance(.1f); manager.Advance(.1f);
            Check(timedProbe.Clock.Scale == 0f && timedProbe.Position == Vector3.zero && timedProbe.Age == 0f && timed.Handle.IsAlive, "a time-stop modifier on the projectile clock freezes movement and age");
            owner.SetActive(false); manager.Advance(.1f);
            Check(timedProbe.Clock.Scale == 1f && Near(timedProbe.Position.x, 1f), "a modifier whose owner vanished is reclaimed while the clock is frozen");
            timed.Handle.End();

            // Environment: a fast projectile cannot skip a thin wall; Boundary is ignored.
            var thin = Fixture("thin wall"); thin.layer = wall; thin.transform.position = new Vector3(5, 0, 0); thin.AddComponent<BoxCollider>().size = new Vector3(.2f, 4, 4);
            var passing = Fixture("boundary"); passing.layer = boundary; passing.transform.position = new Vector3(5, 0, 20); passing.AddComponent<BoxCollider>().size = new Vector3(.2f, 4, 4);
            Physics.SyncTransforms();
            var fast = Fire(manager, "test:probe_fast", Vector3.zero, Vector3.right); var fastProbe = Probe(fast);
            manager.Advance(.1f);
            Check(fastProbe.Ended == PNProjectileEndReason.Environment && fastProbe.EnvironmentHit.HasValue && Near(fastProbe.EnvironmentHit.Value.Point.x, 4.9f, .05f) && fastProbe.EnvironmentHit.Value.Normal.x < -.9f && fastProbe.EnvironmentHit.Value.Target == null,
                "a projectile ending a tick behind a thin wall stops at the wall with its point and normal");
            var through = Fire(manager, "test:probe_fast", new Vector3(0, 0, 20), Vector3.right); var throughProbe = Probe(through);
            manager.Advance(.1f);
            Check(through.Handle.IsAlive && throughProbe.EnvironmentHit == null && Near(throughProbe.Position.x, 10f), "the Boundary layer does not stop a projectile");
            through.Handle.End();

            // Targets: only the launch layers, only living entities.
            var enemy = Dummy("enemy", 990101, new Vector3(2, 0, 0), 7); var friend = Dummy("friend", 990102, new Vector3(2, 0, 0), 6);
            Physics.SyncTransforms();
            var hit = Fire(manager, "test:probe", Vector3.zero, Vector3.right); var hitProbe = Probe(hit);
            manager.Advance(.1f);
            Check(hitProbe.Targets.Count == 0, "a projectile short of the target has no contact");
            manager.Advance(.1f);
            Check(hitProbe.Targets.Count >= 1 && hitProbe.Targets.All(x => ReferenceEquals(x.Target, enemy)) && hitProbe.Targets[0].Fraction >= 0 && hitProbe.Targets[0].Fraction <= 1 && Near(hitProbe.Targets[0].Point.x, 1.5f, .05f),
                "the oriented box reports the target on its layers, once per sub-step while overlapping, and never the entity on another layer");
            hit.Handle.End();
            enemy.SetCurrent(PNContentIds.Health, 0);
            var late = Fire(manager, "test:probe", new Vector3(1, 0, 0), Vector3.right); var lateProbe = Probe(late);
            manager.Advance(.1f);
            Check(lateProbe.Targets.Count == 0, "a dead entity is not a contact");
            late.Handle.End();

            // Commands issued from inside a hook are queued, not nested.
            var living = Dummy("living", 990103, new Vector3(2, 0, 3), 7); Physics.SyncTransforms();
            PNProjectileReceipt inner = null; PNProjectileStatus? innerWhileBusy = null;
            PNProbeProjectile.OnTargetsHook = (probe, targets) =>
            {
                inner = Fire(manager, "test:probe", new Vector3(0, 0, 9), Vector3.right); innerWhileBusy = inner.Status;
                probe.Finish(PNProjectileEndReason.Hit);
            };
            var reentrant = Fire(manager, "test:probe", new Vector3(0, 0, 3), Vector3.right); var reentrantProbe = Probe(reentrant);
            manager.Advance(.2f);
            PNProbeProjectile.OnTargetsHook = null;
            Check(innerWhileBusy == PNProjectileStatus.Pending && inner.Status == PNProjectileStatus.Created && reentrantProbe.Ended == PNProjectileEndReason.Hit && !reentrant.Handle.IsAlive && inner.Handle.IsAlive,
                "a projectile created and ended inside a hook is queued and completes after the tick");
            inner.Handle.End();

            // Failed and rejected requests leave nothing behind.
            int borrowed = manager.BorrowedCount;
            var unknown = Fire(manager, "test:no_such_projectile", Vector3.zero, Vector3.right);
            var badInput = manager.Create(new PNProjectileLaunch(EveId.Parse("test:probe"), JObject.Parse("{'x':1}"), source, enemyMask), new PNProjectileSpawn(preview, Vector3.zero, Vector3.right));
            var foreign = manager.Create(new PNProjectileLaunch(EveId.Parse("test:probe"), null, new PNBuffSource(Guid.NewGuid(), new EveEntityId(1)), enemyMask), new PNProjectileSpawn(preview, Vector3.zero, Vector3.right));
            var noScene = manager.Create(new PNProjectileLaunch(EveId.Parse("test:probe"), null, source, enemyMask), new PNProjectileSpawn(default(Scene), Vector3.zero, Vector3.right));
            Check(unknown.Status == PNProjectileStatus.Failed && unknown.Error != null && badInput.Status == PNProjectileStatus.Failed && foreign.Status == PNProjectileStatus.Failed && noScene.Status == PNProjectileStatus.Failed && manager.BorrowedCount == borrowed,
                "unknown ids, bad input, another session's source and an invalid scene fail without borrowing");
            Reject(() => new PNProjectileSpawn(preview, Vector3.zero, Vector3.zero), "a zero direction is rejected", Check);
            Reject(() => new PNProjectileSpawn(preview, new Vector3(float.NaN, 0, 0), Vector3.right), "a non-finite position is rejected", Check);

            // Destroy by another system keeps the attribution.
            var doomed = Fire(manager, "test:probe", Vector3.zero, Vector3.right); var doomedProbe = Probe(doomed);
            Check(manager.Destroy(doomed.Handle, PNProjectileEndReason.Destroyed, source) && doomedProbe.Ended == PNProjectileEndReason.Destroyed && ReferenceEquals(doomedProbe.EndedBy, source) && !doomed.Handle.IsAlive && !manager.Destroy(doomed.Handle),
                "Destroy ends a projectile with its reason and attribution, and a second call does nothing");

            // Cap: reject when every object is active; otherwise evict the idle object that has waited longest.
            manager.Shutdown(); PNProbeProjectile.Reset(); manager = NewManager(3);
            var a1 = Fire(manager, "test:probe", Vector3.zero, Vector3.right); var a2 = Fire(manager, "test:probe", new Vector3(0, 0, 1), Vector3.right); var a3 = Fire(manager, "test:probe", new Vector3(0, 0, 2), Vector3.right);
            var refused = Fire(manager, "test:probe", new Vector3(0, 0, 3), Vector3.right);
            Check(new[] { a1, a2, a3 }.All(x => x.Status == PNProjectileStatus.Created) && refused.Status == PNProjectileStatus.Rejected && refused.Error == null && !refused.Handle.IsAlive && manager.PooledObjectCount == 3 && manager.ActiveCount == 3,
                "at the cap with every object active the new projectile is rejected and nothing running is overwritten");
            int serial1 = Probe(a1).Serial, serial2 = Probe(a2).Serial;
            a1.Handle.End(); a2.Handle.End();
            Check(manager.IdleObjectCount == 2 && manager.ActiveCount == 1, "ended projectiles wait idle in the pool");
            var other = Fire(manager, "test:probe_b", new Vector3(0, 0, 4), Vector3.right);
            Check(other.Status == PNProjectileStatus.Created && manager.PooledObjectCount == 3 && manager.IdleObjectCount == 1 && PNProbeProjectile.Disposed.SequenceEqual(new[] { serial1 }),
                "a new object at the cap destroys the oldest idle object of any pool");
            var reused = Fire(manager, "test:probe", new Vector3(0, 0, 5), Vector3.right);
            Check(reused.Status == PNProjectileStatus.Created && ReferenceEquals(Probe(reused), PNProbeProjectile.ByHandle[a2.Handle.InstanceId]) && Probe(reused).Serial == serial2 && manager.PooledObjectCount == 3 && manager.IdleObjectCount == 0,
                "an idle object of the same pool is reused without growing the pool");

            // Shutdown stops everything, destroys every pooled object and refuses new work until re-initialized.
            PNProbeProjectile.Disposed.Clear();
            manager.Shutdown();
            Check(manager.BorrowedCount == 0 && manager.PooledObjectCount == 0 && PNProbeProjectile.Disposed.Count == 3 && Probe(a3).Ended == PNProjectileEndReason.SessionEnded && !a3.Handle.IsAlive,
                "Shutdown ends running projectiles with SessionEnded and disposes every pooled object");
            Check(Fire(manager, "test:probe", Vector3.zero, Vector3.right).Status == PNProjectileStatus.Rejected, "a shut-down manager rejects new projectiles");
            manager.Initialize(session);
            Check(Fire(manager, "test:probe", Vector3.zero, Vector3.right).Status == PNProjectileStatus.Created && manager.MaxActive == PNProjectileProfile.DefaultMaxActive, "re-initializing restores service with the default profile");
            manager.Shutdown();
        }
        finally
        {
            try { if (manager != null) manager.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var obj in fixtures) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(preview); }
        }
        World(hub, docs, Check);
        Check(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "Authored scene and dirty state preserved.");
        string summary = "PASS " + count + " native projectile checks";
        Directory.CreateDirectory(".utmp/ProjectileReview"); File.WriteAllLines(".utmp/ProjectileReview/NativeResults.txt", evidence.Concat(new[] { summary })); return summary;
    }
    private static void Reject(Action action, string label, Action<bool, string> check)
    { try { action(); } catch (Exception) { check(true, label); return; } throw new Exception("Expected rejection: " + label); }
    private static void Geometry(Action<bool, string> check)
    {
        PNProjectileGeometry.Frame(new Vector3(1, 0, 1), out var right, out var up, out var forward);
        check(Mathf.Abs(Vector3.Dot(right, up)) < 1e-5f && Mathf.Abs(Vector3.Dot(up, forward)) < 1e-5f && Mathf.Abs(Vector3.Dot(right, forward)) < 1e-5f && Mathf.Abs(right.magnitude - 1) < 1e-5f && Mathf.Abs(up.magnitude - 1) < 1e-5f, "the projectile frame is orthonormal");
        PNProjectileGeometry.Frame(Vector3.up, out right, out up, out forward);
        check(Mathf.Abs(Vector3.Dot(right, forward)) < 1e-5f && Mathf.Abs(Vector3.Dot(up, forward)) < 1e-5f && !float.IsNaN(right.x), "a vertical heading still has a valid frame");
        var half = new Vector3(.5f, .5f, .5f);
        check(PNProjectileGeometry.ObbOverlapsAabb(Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, half, new Vector3(.8f, 0, 0), new Vector3(.4f, .5f, .5f)), "aligned boxes that overlap are detected");
        check(!PNProjectileGeometry.ObbOverlapsAabb(Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, half, new Vector3(1.2f, 0, 0), new Vector3(.4f, .5f, .5f)), "aligned boxes that are apart are separated");
        PNProjectileGeometry.Frame(new Vector3(1, 0, 1), out right, out up, out forward);
        check(PNProjectileGeometry.ObbOverlapsAabb(Vector3.zero, right, up, forward, half, new Vector3(.9f, 0, 0), new Vector3(.2f, .5f, .5f)), "a box rotated 45 degrees reaches further than its axis-aligned size");
        check(!PNProjectileGeometry.ObbOverlapsAabb(Vector3.zero, right, up, forward, half, new Vector3(.95f, 0, 0), new Vector3(.2f, .5f, .5f)), "a rotated box that just misses is separated");
        check(PNProjectileGeometry.ObbOverlapsAabb(Vector3.zero, right, up, forward, half, new Vector3(0, 0, 0), new Vector3(.1f, .1f, .1f)), "a box inside another overlaps");
        check(PNProjectileGeometry.ClosestPoint(new Vector3(3, 0, 0), Vector3.zero, Vector3.one) == new Vector3(1, 0, 0), "closest point clamps to the box");
        check(Mathf.Abs(PNProjectileGeometry.DistanceToSegment(new Vector3(1, 2, 0), Vector3.zero, new Vector3(4, 0, 0), out float t) - 2f) < 1e-5f && Mathf.Abs(t - .25f) < 1e-5f, "segment distance reports the closest parameter");
    }
    // A world holds a lease while projectiles fly in it; unloading releases them and leaves other worlds alone.
    private static void World(EveRegistryHub hub, EveContentDocument[] docs, Action<bool, string> check)
    {
        PNProbeProjectile.Reset();
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Host, EveContentCompatibility.Create(hub, docs, "test:projectile_world"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile());
        var worlds = new PNWorldRuntime(session, scheduler); var anchor = EditorSceneManager.NewPreviewScene(); GameObject managerObject = null; PNProjectileManager manager = null;
        void Pump(Func<bool> until) { for (int i = 0; i < 1000 && !until(); i++) scheduler.AdvanceFrame(); if (!until()) throw new Exception("Fixture did not drain"); }
        try
        {
            var a = worlds.Create(EveId.Parse("fixture:world")); var b = worlds.Create(EveId.Parse("fixture:world"));
            Pump(() => a.State == EveWorldState.Ready && b.State == EveWorldState.Ready);
            managerObject = new GameObject("Projectile world manager"); SceneManager.MoveGameObjectToScene(managerObject, anchor);
            manager = managerObject.AddComponent<PNProjectileManager>(); manager.Initialize(session, new PNProjectileProfile());
            worlds.BeforeUnload = world => manager.ReleaseWorld(world);
            var sceneA = worlds.Get(a).PhysicsScene; var sceneB = worlds.Get(b).PhysicsScene;
            var source = new PNBuffSource(session.SessionId, new EveEntityId(990201));
            PNProjectileReceipt Fire(Scene scene) => manager.Create(new PNProjectileLaunch(EveId.Parse("test:probe_fast"), null, source, 1 << 7), new PNProjectileSpawn(scene, new Vector3(0, 1, 0), Vector3.right));
            var inA = Fire(sceneA); var inB = Fire(sceneB);
            check(inA.Status == PNProjectileStatus.Created && inB.Status == PNProjectileStatus.Created && PNProbeProjectile.ByHandle[inA.Handle.InstanceId].World == a && PNProbeProjectile.ByHandle[inB.Handle.InstanceId].World == b,
                "projectiles fired in two scenes are bound to their own worlds");
            manager.Advance(.1f);
            var probeA = PNProbeProjectile.ByHandle[inA.Handle.InstanceId]; var probeB = PNProbeProjectile.ByHandle[inB.Handle.InstanceId];
            check(Mathf.Abs(probeA.Position.x - 10f) < 1e-3f && Mathf.Abs(probeB.Position.x - 10f) < 1e-3f, "projectiles in world scenes move through their own physics scenes");
            check(worlds.Unload(a), "a world unloads while a projectile holds its lease because the world release ends it");
            check(!inA.Handle.IsAlive && probeA.Ended == PNProjectileEndReason.SceneUnloaded && inB.Handle.IsAlive && manager.BorrowedCount == 1 && manager.PooledObjectCount == 1, "unloading world A ends its projectile and its pool but leaves world B alone");
            manager.Advance(.1f);
            check(Mathf.Abs(probeB.Position.x - 20f) < 1e-3f, "the other world keeps simulating");
            check(worlds.Unload(b) && !inB.Handle.IsAlive && manager.BorrowedCount == 0 && manager.PooledObjectCount == 0, "unloading the last world releases every projectile object");
        }
        finally
        {
            try { if (manager != null) manager.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { if (managerObject != null) UnityEngine.Object.DestroyImmediate(managerObject); EditorSceneManager.ClosePreviewScene(anchor); }
        }
    }
}
#endif
