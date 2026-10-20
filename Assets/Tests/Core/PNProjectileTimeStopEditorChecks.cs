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

// Time-stop areas act on projectiles by placing owned modifiers on each projectile's own clock (P5).
public static class PNProjectileTimeStopEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        int count = 0; var evidence = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS " + message); count++; }
        bool Near(double a, double b, double tolerance = 1e-3) => Math.Abs(a - b) <= tolerance;
        var docs = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNProjectileHitNativeModule() }, docs);
        var preview = EditorSceneManager.NewPreviewScene(); var fixtures = new List<GameObject>(); PNProjectileManager projectiles = null; PNAreaManager areas = null;
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:projectile_timestop"));
            var session = EveRegistrySession.Context; var source = new PNBuffSource(session.SessionId, new EveEntityId(993001)); int enemy = 1 << 7;
            GameObject Fixture(string name) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, preview); fixtures.Add(obj); return obj; }
            projectiles = Fixture("Projectile manager").AddComponent<PNProjectileManager>(); projectiles.Initialize(session, new PNProjectileProfile { MaxActive = 20 });
            areas = Fixture("Area manager").AddComponent<PNAreaManager>(); areas.Initialize(session);
            int projectileLayer = LayerMask.NameToLayer("Projectile");
            Check(projectileLayer == 8, "the Projectile layer exists");
            PNProjectileReceipt Fire(Vector3 position, string id = "test:hit_once") => projectiles.Create(new PNProjectileLaunch(EveId.Parse(id), null, source, enemy), new PNProjectileSpawn(preview, position, Vector3.right));
            PNAreaReceipt Area(Vector3 position, double reduction, int layerMask = 0, string mode = "Exclude", double radius = 3)
                => areas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"),
                    JObject.Parse("{'radius':" + radius + ",'reduction':" + reduction + ",'minimumReduction':0,'duration':20,'dependent':false,'layerMask':" + layerMask + ",'layerMode':'" + mode + "'}"), source, preview, position));
            PNProjectileClock Clock(PNProjectileReceipt receipt) { Check(projectiles.TryGetClock(receipt.Handle, out var clock), "a running projectile exposes its clock"); return clock; }
            Vector3 Where(PNProjectileReceipt receipt) => projectiles.Get(receipt.Handle).Position;
            void Tick(int n, float step = .05f) { for (int i = 0; i < n; i++) { areas.Advance(step); projectiles.Advance(step); } }

            // A full stop: inside the radius the clock scale drops to zero and the projectile freezes.
            var stopped = Fire(Vector3.zero); var outside = Fire(new Vector3(60, 0, 0));
            projectiles.Advance(.2f); // stopped is now at x=2
            var area = Area(new Vector3(8, 0, 0), 1);
            Check(area.Status == PNAreaStatus.Created && Clock(stopped).Scale == 1f, "the area is created and has not yet reached the projectile");
            Tick(9);
            Check(Clock(stopped).Scale == 0f && Clock(outside).Scale == 1f && Clock(stopped).Modifiers.Count == 2, "a projectile inside the radius gets the scale and minimum modifiers; one outside is untouched");
            var frozenAt = Where(stopped); Tick(6);
            Check(Where(stopped) == frozenAt && Where(outside).x > 60.5f, "a time-stopped projectile does not move while one outside the area keeps flying");

            // Leaving the area, the area ending, or the projectile ending releases the modifiers.
            area.Handle.End();
            projectiles.Advance(.1f);
            Check(Clock(stopped).Scale == 1f && Clock(stopped).Modifiers.Count == 0 && Where(stopped).x > frozenAt.x, "ending the area gives the clock back and the projectile resumes");
            var mover = Fire(new Vector3(0, 0, 30)); var movingArea = Area(new Vector3(2, 0, 30), 1, radius: 1.5); Tick(3);
            Check(Clock(mover).Scale == 0f, "a projectile inside a second area is stopped");
            var clockOfMover = Clock(mover); movingArea.Handle.End(); mover.Handle.End(); Tick(2);
            Check(clockOfMover.Modifiers.Count == 0, "no modifier is left on a clock after both the area and the projectile ended");

            // Stacking: two areas add their reductions; removing one leaves the other.
            var stacked = Fire(new Vector3(0, 0, 40)); var half1 = Area(new Vector3(0, 0, 40), .5); var half2 = Area(new Vector3(0, 0, 40), .5); Tick(1);
            Check(Clock(stacked).Scale == 0f && Clock(stacked).Modifiers.Count == 4, "two overlapping areas add their reductions (0.5 + 0.5)");
            half1.Handle.End(); Tick(1);
            Check(Near(Clock(stacked).Scale, .5) && Clock(stacked).Modifiers.Count == 2, "removing one area leaves the other's reduction");
            half2.Handle.End(); stacked.Handle.End(); Tick(1);

            // The area's layer rule treats projectiles as the Projectile layer.
            var guarded = Fire(new Vector3(0, 0, 50)); var excluded = Area(new Vector3(0, 0, 50), 1, 1 << projectileLayer, "Exclude"); Tick(1);
            Check(Clock(guarded).Scale == 1f, "an area that excludes the Projectile layer leaves projectiles alone");
            excluded.Handle.End(); var included = Area(new Vector3(0, 0, 50), 1, 1 << projectileLayer, "Include"); Tick(1);
            Check(Clock(guarded).Scale == 0f, "an area that includes the Projectile layer stops projectiles");
            var onlyEnemies = Area(new Vector3(0, 0, 70), 1, 1 << 7, "Include"); var elsewhere = Fire(new Vector3(0, 0, 70)); Tick(1);
            Check(Clock(elsewhere).Scale == 1f, "an area limited to other layers does not touch projectiles");
            onlyEnemies.Handle.End(); included.Handle.End(); guarded.Handle.End(); elsewhere.Handle.End(); Tick(1);

            // A frozen projectile does not age: it outlives its lifetime while stopped and expires afterwards.
            var aging = Fire(new Vector3(0, 0, 80), "test:hit_short"); var stopper = Area(new Vector3(0, 0, 80), 1); Tick(1);
            Tick(24);
            Check(aging.Handle.IsAlive && Clock(aging).Scale == 0f, "a stopped projectile outlives its lifetime because its age does not advance");
            stopper.Handle.End(); Tick(8);
            Check(!aging.Handle.IsAlive, "once the area ends the projectile ages and expires");
        }
        finally
        {
            try { areas?.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            try { projectiles?.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var obj in fixtures) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(preview); }
        }
        Check(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "Authored scene and dirty state preserved.");
        string summary = "PASS " + count + " native projectile time-stop checks";
        Directory.CreateDirectory(".utmp/ProjectileReview"); File.WriteAllLines(".utmp/ProjectileReview/NativeTimeStopResults.txt", evidence.Concat(new[] { summary })); return summary;
    }
}
#endif
