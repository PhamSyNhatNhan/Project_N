#if UNITY_EDITOR
using System;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/ObservedViewPlan.md "lượt A": PNVisualBody.PlayActionAt / PNVisualAnimator.Drive on REAL rigs (Starkeeper: Time clock states; Starforger: Phase clock states), Edit Mode, preview scene.
// What it shows: a driven body takes its time from the run's age only (no integration, no slow-clock drift), a body that seeks straight to an age shows the same pose as one stepped to it,
// Release hands the clock back with no jump. What it does NOT show: the look of the pose (no frame), real Play Mode timing, monsters' Actions (none use Drive yet).
public static class PNVisualDriveChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Editor required");
        var scene = EditorSceneManager.NewPreviewScene(); bool started = false; int n = 0; GameObject a = null, b = null, f = null;
        void Check(bool ok, string what) { if (!ok) throw new Exception("Visual drive check " + n + ": " + what); n++; }
        bool Near(double x, double y, double eps = 1e-4) => Math.Abs(x - y) <= eps;
        try
        {
            var docs = new PNGameContentSource(Application.streamingAssetsPath + "/BaseContent", "unused", Array.Empty<string>()).ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
            EveRegistrySession.Begin(); started = true; EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:visual-drive"));
            PNVisualPacks.Set("project_n", Application.streamingAssetsPath + "/BaseContent");
            GameObject Spawn(string prefabPath, string characterId, int id)
            {
                var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene);
                go.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse(characterId), 0); go.GetComponent<EveEntityLoader>().Load(hub); go.SetActive(true);
                Check(go.GetComponent<PNVisualBody>().Begin(hub.Characters.Get(EveId.Parse(characterId)), hub), characterId + " has a puppet");
                return go;
            }
            float[] Pose(PNVisualActor actor) { var an = actor.Animator; an.Evaluate(); return (float[])an.BoneAffine.Clone(); }
            bool Same(float[] x, float[] y) { if (x.Length != y.Length) return false; for (int i = 0; i < x.Length; i++) if (Math.Abs(x[i] - y[i]) > 1e-4f) return false; return true; }

            // ================= Time clock: Starkeeper star_point (3.45 s, not looping, blendIn .12) =================
            a = Spawn("Assets/Game/Entity/Enemy/Starkeeper/Prefab/Starkeeper.prefab", "project_n:starkeeper", 978301);
            b = Spawn("Assets/Game/Entity/Enemy/Starkeeper/Prefab/Starkeeper.prefab", "project_n:starkeeper", 978302);
            var bodyA = a.GetComponent<PNVisualBody>(); var bodyB = b.GetComponent<PNVisualBody>(); var actA = bodyA.Actor; var actB = bodyB.Actor;
            actA.Tick(.02f); actB.Tick(.02f);
            const double dur = 3.45;
            Check(bodyA.PlayActionAt("star_point", .5, dur, true) && actA.IsDriven && actA.Animator.BaseState == "star_point", "a Time clock state is placed by the run's age and the body is driven");
            double scale = actA.Animator.BaseTime / .5; Check(scale > 0, "the motion has a speed scale");
            Check(Near(actA.Animator.BaseTime, .5 * scale), "BaseTime = age x speed scale");
            for (int i = 0; i < 6; i++) actA.Tick(.25f);
            Check(Near(actA.Animator.BaseTime, .5 * scale), "a driven body does not integrate its own clock (1.5 s of ticks changed nothing)");
            actA.SetRate(.25f); for (int i = 0; i < 4; i++) actA.Tick(.25f);
            Check(Near(actA.Animator.BaseTime, .5 * scale), "a slowed rate does not move a driven body either (no double counting with a slow area)");
            actA.SetRate(1f);

            Check(bodyA.PlayActionAt("star_point", 1.2, dur, false) && Near(actA.Animator.BaseTime, 1.2 * scale), "a later age moves on without restarting");
            Check(Near(actA.Animator.BaseTime, Math.Min(1.2 * scale, actA.Animator.BaseTime)) && actA.Animator.BaseState == "star_point", "the state stayed the same");
            Check(bodyA.PlayActionAt("star_point", .2, dur, true) && Near(actA.Animator.BaseTime, .2 * scale), "seeking back restarts and places the state at the earlier age");
            bodyA.PlayActionAt("star_point", dur + 5, dur, false);
            Check(actA.Animator.BaseFinished && Near(actA.Animator.BaseTime, dur), "an age beyond the motion stops at its end (finished, not wrapped)");
            Check(bodyA.PlayActionAt("star_point", -1, dur, false) && Near(actA.Animator.BaseTime, 0), "a negative age is the start");

            // host (stepped) vs client (seeks straight to the age): same pose
            bodyA.PlayActionAt("star_point", 0, dur, true);
            for (double age = 0; age <= 2.0001; age += .05) { bodyA.PlayActionAt("star_point", age, dur, false); actA.Tick(.05f, false); }
            bodyB.PlayActionAt("star_point", 2.0, dur, true);
            for (int i = 0; i < 4; i++) { actA.Tick(.1f); actB.Tick(.1f); }     // past blendIn (.12 s) for both
            Check(Near(actA.Animator.BaseTime, actB.Animator.BaseTime), "stepped and seeked bodies are at the same time");
            Check(Same(Pose(actA), Pose(actB)), "stepped and seeked bodies show the same pose (host vs late joiner)");
            bodyB.PlayActionAt("star_point", .3, dur, false); actB.Tick(.1f);
            Check(!Same(Pose(actA), Pose(actB)), "different ages give different poses (the pose check is not vacuous)");

            // release: the clock is handed back where the run left it, no jump
            bodyB.PlayActionAt("star_point", 1.0, dur, true); actB.Tick(.3f);
            float held = actB.Animator.BaseTime; var poseHeld = Pose(actB);
            bodyB.EndAction();
            Check(!actB.IsDriven && !bodyB.Holding, "EndAction releases the body and the hold");
            Check(Near(actB.Animator.BaseTime, held) && Same(Pose(actB), poseHeld), "the release itself does not change the pose or the time");
            actB.Tick(.05f);
            Check(Near(actB.Animator.BaseTime, held + .05 * scale, 1e-3), "after release the body integrates again from the held time (no jump)");

            // dead / broken bodies refuse
            Check(bodyA.PlayActionAt("no_such_state", 0, dur, true) == false, "an unknown state is refused");

            // ================= Phase clock: Starforger crouch / slam (1 s phase, no blend) =================
            f = Spawn("Assets/Game/Entity/Enemy/Starforger/Prefab/Starforger.prefab", "project_n:starforger", 978303);
            var bodyF = f.GetComponent<PNVisualBody>(); var actF = bodyF.Actor; actF.Tick(.02f);
            Check(bodyF.PlayActionAt("slam", .4, 2.0, true), "a Phase clock state starts driven"); actF.Tick(.02f);
            Check(Near(actF.Animator.BaseTime, .2 * 1.0), "a Phase clock state takes age / duration (0.4 / 2.0 = 0.2 of its 1 s) once the actor ticks (the phase reaches BaseTime in Advance, not in Drive)");
            for (int i = 0; i < 5; i++) actF.Tick(.3f);
            Check(Near(actF.Animator.BaseTime, .2), "a driven Phase clock body does not move on its own");
            var poseA = Pose(actF);
            bodyF.PlayActionAt("slam", 1.6, 2.0, false); actF.Tick(.02f);
            Check(Near(actF.Animator.BaseTime, .8) && !Same(Pose(actF), poseA), "a later age moves the pose");
            bodyF.PlayActionAt("slam", 9.0, 2.0, false); actF.Tick(.02f);
            Check(Near(actF.Animator.BaseTime, 1.0), "an age beyond the duration holds the last pose");
            bodyF.PlayActionAt("slam", .5, 0, false); actF.Tick(.02f);
            Check(Near(actF.Animator.BaseTime, 0), "KNOWN LIMIT: a duration of 0 puts a Phase clock state at its start (an Action of unknown length must supply a duration)");
            float phaseHeld = actF.Animator.BaseTime; bodyF.EndAction(); actF.Tick(.1f);
            Check(Near(actF.Animator.BaseTime, phaseHeld), "a released Phase clock body keeps the phase it had (it has no clock of its own)");

            // the rigs' action states carry no animator events (only walk/run have 'step'), so Drive skipping animator-time events costs the monsters nothing
            foreach (var id in new[] { "star_sweep", "star_point" })
            {
                var motion = hub.Get<PNVisualMotionDefinition>().Get(EveId.Parse("project_n:starkeeper_" + id));
                Check(motion.Events.Count == 0, "Starkeeper " + id + " has no animator events");
            }
            foreach (var id in new[] { "crouch", "air", "slam", "cast_star" })
            {
                var motion = hub.Get<PNVisualMotionDefinition>().Get(EveId.Parse("project_n:starforger_" + id));
                Check(motion.Events.Count == 0, "Starforger " + id + " has no animator events");
            }
            return "PASS " + n + " visual drive checks on real Starkeeper (Time clock) and Starforger (Phase clock) rigs; no rendered frame, no Action uses Drive yet";
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally { foreach (var o in new[] { a, b, f }) if (o != null) UnityEngine.Object.DestroyImmediate(o); EditorSceneManager.ClosePreviewScene(scene); PNVisualRuntime.Release(); PNVisualPacks.Clear(); }
        }
    }

    // Rendered frames of a driven monster in a preview scene (Edit Mode): the host-style body (stepped by age every tick) against a late joiner (seeks straight to the age), at three ages.
    // Writes PNGs to `folder` and returns the share of pixels that differ between the two bodies at each age. Needs no Play Mode; it does NOT use the Player's camera, shaders' lighting or the
    // real network path, so it is a pose check, not proof of the final look.
    public static string RunFrames(string folder)
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Editor required");
        System.IO.Directory.CreateDirectory(folder);
        var scene = EditorSceneManager.NewPreviewScene(); bool started = false; GameObject a = null, b = null, cam = null; RenderTexture rt = null; Texture2D tex = null; var report = new System.Text.StringBuilder();
        try
        {
            var docs = new PNGameContentSource(Application.streamingAssetsPath + "/BaseContent", "unused", Array.Empty<string>()).ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
            EveRegistrySession.Begin(); started = true; EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:visual-drive-frames"));
            PNVisualPacks.Set("project_n", Application.streamingAssetsPath + "/BaseContent");
            GameObject Spawn(int id, Vector3 where)
            {
                var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starkeeper/Prefab/Starkeeper.prefab")); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.position = where;
                go.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:starkeeper"), 0); go.GetComponent<EveEntityLoader>().Load(hub); go.SetActive(true);
                if (!go.GetComponent<PNVisualBody>().Begin(hub.Characters.Get(EveId.Parse("project_n:starkeeper")), hub)) throw new Exception("no puppet");
                return go;
            }
            a = Spawn(978311, new Vector3(0, 0, 0)); b = Spawn(978312, new Vector3(20, 0, 0));
            var bodyA = a.GetComponent<PNVisualBody>(); var bodyB = b.GetComponent<PNVisualBody>(); var actA = bodyA.Actor; var actB = bodyB.Actor;
            cam = new GameObject("Drive capture camera"); SceneManager.MoveGameObjectToScene(cam, scene);
            var camera = cam.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene; camera.orthographic = true; camera.orthographicSize = .9f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.5f, .5f, .55f); camera.cullingMask = ~0;
            rt = new RenderTexture(360, 360, 24); rt.Create(); tex = new Texture2D(360, 360, TextureFormat.RGB24, false);
            Color32[] Shot(GameObject target, string label)
            {
                camera.transform.position = target.transform.position + new Vector3(0, -.15f, -6f); camera.transform.rotation = Quaternion.identity;
                var previous = RenderTexture.active;
                try
                {
                    camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 360, 360), 0, 0); tex.Apply();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, label + ".png"), tex.EncodeToPNG());
                    return tex.GetPixels32();
                }
                finally { camera.targetTexture = null; RenderTexture.active = previous; }
            }
            int Painted(Color32[] px) { int c = 0; foreach (var p in px) if (Math.Abs(p.r - 127) + Math.Abs(p.g - 127) + Math.Abs(p.b - 140) > 30) c++; return c; }
            double Diff(Color32[] x, Color32[] y) { int c = 0; for (int i = 0; i < x.Length; i++) if (Math.Abs(x[i].r - y[i].r) + Math.Abs(x[i].g - y[i].g) + Math.Abs(x[i].b - y[i].b) > 24) c++; return c / (double)x.Length; }
            actA.Tick(.02f); actB.Tick(.02f);
            const double dur = 3.45; double stepped = 0; var painted = new System.Collections.Generic.List<int>(); var shotsA = new System.Collections.Generic.List<Color32[]>();
            bodyA.PlayActionAt("star_point", 0, dur, true);
            foreach (var age in new[] { .3, 1.5, 2.8 })
            {
                for (; stepped <= age + 1e-6; stepped += 1.0 / 30) { bodyA.PlayActionAt("star_point", stepped, dur, false); actA.Tick(1f / 30); }
                bodyA.PlayActionAt("star_point", age, dur, false); actA.Tick(.01f);
                bodyB.PlayActionAt("star_point", age, dur, true); for (int i = 0; i < 6; i++) actB.Tick(.05f);   // past the blend
                var pa = Shot(a, "starkeeper_star_point_host_" + age.ToString("0.0")); var pb = Shot(b, "starkeeper_star_point_joiner_" + age.ToString("0.0"));
                painted.Add(Painted(pa)); shotsA.Add(pa);
                report.Append(" age " + age.ToString("0.0") + ": painted " + Painted(pa) + "/" + Painted(pb) + ", host-vs-joiner diff " + (Diff(pa, pb) * 100).ToString("0.00") + "%;");
                if (Painted(pa) < 200 || Painted(pb) < 200) throw new Exception("the puppet is not in the picture at age " + age + report);
                if (Diff(pa, pb) > .01) throw new Exception("host and late joiner differ by more than 1% of the pixels at age " + age + report);
            }
            if (Diff(shotsA[0], shotsA[2]) < .005) throw new Exception("the pose did not change between age 0.3 and 2.8" + report);
            return "PASS frames in " + folder + report;
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally
            {
                foreach (var o in new[] { a, b, cam }) if (o != null) UnityEngine.Object.DestroyImmediate(o);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex); if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
                EditorSceneManager.ClosePreviewScene(scene); PNVisualRuntime.Release(); PNVisualPacks.Clear();
            }
        }
    }
}
#endif
