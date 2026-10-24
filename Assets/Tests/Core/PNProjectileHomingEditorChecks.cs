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

// Homing (P3): steering toward a target, reacquiring or keeping the heading when it is lost, nearest-target search and candidate refresh.
public static class PNProjectileHomingEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        int count = 0; var evidence = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS " + message); count++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNProjectileHitNativeModule() }, docs);
        var preview = EditorSceneManager.NewPreviewScene(); var fixtures = new List<GameObject>(); PNProjectileManager manager = null;
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:projectile_homing"));
            var session = EveRegistrySession.Context; int enemy = 1 << 7; long nextId = 992000;
            GameObject Fixture(string name) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, preview); fixtures.Add(obj); return obj; }
            PNProjectileManager NewManager(float refresh = .1f)
            {
                var m = Fixture("Projectile manager").AddComponent<PNProjectileManager>(); m.Initialize(session, new PNProjectileProfile { MaxActive = 30, TargetRefreshSeconds = refresh }); return m;
            }
            PNStat Dummy(Vector3 position, int layer = 7)
            {
                var obj = Fixture("target " + nextId); obj.SetActive(false); obj.transform.position = position; obj.layer = layer;
                obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(nextId++), EveId.Parse("project_n:dummy"), 0);
                var stat = obj.AddComponent<PNEnemyStat>(); obj.AddComponent<BoxCollider>().size = Vector3.one;
                obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); obj.GetComponent<PNBuffManager>().Advance(0); return stat;
            }
            var shooterObject = Fixture("shooter"); shooterObject.SetActive(false);
            shooterObject.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(nextId++), EveId.Parse("project_n:eve"), 0);
            shooterObject.AddComponent<EveCharacterStat>(); shooterObject.AddComponent<PNAbilityManager>(); shooterObject.AddComponent<PNProjectileSkillProbe>();
            shooterObject.AddComponent<EveEntityLoader>().Load(hub); shooterObject.SetActive(true);
            var source = PNCombatSource.Capture(shooterObject.GetComponent<PNStat>());
            var prepared = source.Snapshot.PrepareDamage(100, false, 0d, PNContentIds.True);
            PNProjectileReceipt Fire(PNProjectileManager m, string id, Vector3 position, Vector3 direction, PNStat target = null)
                => m.Create(new PNProjectileLaunch(EveId.Parse(id), null, source, PNTargetLayers.Of(PNLayers.Enemy), prepared), new PNProjectileSpawn(preview, position, direction, target));
            double Hp(PNStat stat) => stat.GetCurrent(PNContentIds.Health);
            void Advance(PNProjectileManager m, int ticks, float step = .05f) { for (int i = 0; i < ticks; i++) m.Advance(step); }
            manager = NewManager();

            // Steering: an off-axis target is reached by a homing projectile and missed by a straight one.
            var off = Dummy(new Vector3(6, 0, 3)); Physics.SyncTransforms(); double hpOff = Hp(off);
            Fire(manager, "test:hit_once", Vector3.zero, Vector3.right); Advance(manager, 40);
            Check(Hp(off) == hpOff, "a straight projectile misses a target 3 units off its line");
            var steered = Fire(manager, "test:homing_basic", Vector3.zero, Vector3.right, off); Advance(manager, 40);
            Check(hpOff - Hp(off) == 100d && !steered.Handle.IsAlive, "a homing projectile with a target turns to it and hits it once");

            // homeAfter: straight until the delay, then it turns.
            var late = Fire(manager, "test:homing_late", Vector3.zero, Vector3.right, off); var lateSnapshot = manager.Snapshot().First(x => x.Handle.InstanceId == late.Handle.InstanceId);
            Advance(manager, 8);
            var early = manager.Get(late.Handle);
            Check(Mathf.Abs(early.Position.z) < 1e-4f && early.Position.x > 3.5f, "before homeAfter the projectile flies straight");
            Advance(manager, 6);
            Check(manager.Get(late.Handle) == null || !late.Handle.IsAlive || manager.Get(late.Handle).Position.z > .3f, "after homeAfter the projectile turns toward the target");
            if (late.Handle.IsAlive) late.Handle.End();

            // Reacquire: no initial target, the nearest of two is chosen; a lost target is replaced.
            double hpOffBefore = Hp(off);
            var far = Dummy(new Vector3(6, 0, -4)); Physics.SyncTransforms(); double hpFar = Hp(far); hpOffBefore = Hp(off);
            manager.Advance(.2f); // lets the candidate list see the new targets
            var seeker = Fire(manager, "test:homing_basic", Vector3.zero, Vector3.right); Advance(manager, 60);
            Check(Hp(off) < hpOffBefore && Hp(far) == hpFar && !seeker.Handle.IsAlive, "without a target a homing projectile finds the nearest one and hits it, not the farther one");
            hpOffBefore = Hp(off); hpFar = Hp(far); var third = Dummy(new Vector3(6, 0, 2.5f)); Physics.SyncTransforms(); double hpThird = Hp(third);
            var lost = Fire(manager, "test:homing_basic", Vector3.zero, Vector3.right, third); Advance(manager, 2);
            third.SetCurrent(PNContentIds.Health, 0);
            Advance(manager, 60);
            Check(Hp(off) < hpOffBefore && !lost.Handle.IsAlive, "when its target dies a Reacquire projectile picks the nearest remaining one");
            hpOffBefore = Hp(off); hpFar = Hp(far);
            var keeper = Fire(manager, "test:homing_keep", Vector3.zero, Vector3.right, far);
            Advance(manager, 2);
            far.SetCurrent(PNContentIds.Health, 0); double hpFarDead = Hp(far); manager.Advance(.0f);
            var keepProbe = manager.Get(keeper.Handle).Forward;
            Advance(manager, 40);
            Check(Hp(off) == hpOffBefore && Hp(far) == hpFarDead && (!keeper.Handle.IsAlive || manager.Get(keeper.Handle).Position.z < -0.01f), "a KeepHeading projectile flies on in its last direction instead of picking a new target");
            if (keeper.Handle.IsAlive) keeper.Handle.End();

            // Nearest-target search over the manager's candidate list.
            var hold = Fire(manager, "test:hit_still", Vector3.zero, Vector3.right); // keeps the world list alive
            var near = Dummy(new Vector3(0, 0, 40)); var farther = Dummy(new Vector3(0, 0, 45)); var wrongLayer = Dummy(new Vector3(0, 0, 41), 6); Physics.SyncTransforms();
            manager.Advance(.2f);
            Check(ReferenceEquals(manager.FindNearest(null, new Vector3(0, 0, 38), 20f, enemy), near), "FindNearest returns the closest live target on the requested layers");
            Check(manager.FindNearest(null, new Vector3(0, 0, 38), 1f, enemy) == null && ReferenceEquals(manager.FindNearest(null, new Vector3(0, 0, 38), 5f, 1 << 6), wrongLayer), "FindNearest honors the radius and the layer mask");
            near.SetCurrent(PNContentIds.Health, 0); manager.Advance(.0f);
            Check(ReferenceEquals(manager.FindNearest(null, new Vector3(0, 0, 38), 20f, enemy), farther), "a dead target is skipped immediately, without waiting for a membership refresh");
            hold.Handle.End();

            // Candidate membership refreshes at a limited rate while boxes are read every tick.
            manager.Shutdown(); manager = NewManager(.5f);
            var anchor = Dummy(new Vector3(0, 0, 60)); Physics.SyncTransforms();
            var watcher = Fire(manager, "test:hit_still", new Vector3(0, 0, 55), Vector3.right);
            manager.Advance(.05f);
            var latecomer = Dummy(new Vector3(0, 0, 61)); Physics.SyncTransforms();
            manager.Advance(.05f);
            Check(ReferenceEquals(manager.FindNearest(null, new Vector3(0, 0, 61), 3f, enemy), anchor), "a new entity is not yet a candidate before the refresh period passes");
            manager.Advance(.5f);
            Check(ReferenceEquals(manager.FindNearest(null, new Vector3(0, 0, 61), 3f, enemy), latecomer), "after the refresh period the new entity becomes a candidate");
            anchor.transform.position = new Vector3(0, 0, 80); Physics.SyncTransforms(); manager.Advance(.05f);
            Check(manager.FindNearest(null, new Vector3(0, 0, 60), .5f, enemy) == null && ReferenceEquals(manager.FindNearest(null, new Vector3(0, 0, 80), 2f, enemy), anchor), "a moved entity is found at its new position on the next tick");
            watcher.Handle.End();
        }
        finally
        {
            try { if (manager != null) manager.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var obj in fixtures) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(preview); }
        }
        Check(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "Authored scene and dirty state preserved.");
        string summary = "PASS " + count + " native projectile homing checks";
        Directory.CreateDirectory(".utmp/ProjectileReview"); File.WriteAllLines(".utmp/ProjectileReview/NativeHomingResults.txt", evidence.Concat(new[] { summary })); return summary;
    }
}
#endif
