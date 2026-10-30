#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// checks for: the overlap-shadow setting (off / key actors / actors), the view test with margin + hysteresis, lazy pose while out of view, and the caster cap policy.
public static class PNVisualCullShadowCheck
{
    private const string Output = "G:/ProjectN_Camera/VisualGpu/Cull";
    private static int checks;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("Cull/shadow: " + message); checks++; }

    public static void Batch()
    {
        Directory.CreateDirectory(Output);
        try { var r = Run(); File.WriteAllText(Output + "/result.txt", r); UnityEngine.Debug.Log(r); EditorApplication.Exit(0); }
        catch (Exception e) { File.WriteAllText(Output + "/result.txt", e.ToString()); UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static string Run()
    {
        Directory.CreateDirectory(Output);
        checks = 0; var log = new System.Text.StringBuilder();
        // ---- the pure caster plan ----
        {
            var keepAll = PNVisualActor.PlanCasters(new[] { 14, 14, 1, 1 }, new long[] { 900, 800, 100, 50 }, new[] { 1, 2, 3, 4 }, 128, out int s0, out int full0);
            Require(full0 == 60 && s0 == 1 && Array.TrueForAll(keepAll, k => k), "under the cap every strip casts, nothing changes (60 triangles)");
            var segs = new int[10]; var area = new long[10]; var lay = new int[10]; for (int i = 0; i < 10; i++) { segs[i] = 14; area[i] = 1000 - i; lay[i] = i; }
            var coarse = PNVisualActor.PlanCasters(segs, area, lay, 128, out int s1, out int full1);
            Require(full1 == 280 && s1 == 4 && Array.TrueForAll(coarse, k => k), "over the cap bent parts are coarsened first (280 -> stride 4, 70 triangles), no part dropped");
            int n = 70; var sq = new int[n]; var sa = new long[n]; var sl = new int[n]; for (int i = 0; i < n; i++) { sq[i] = 1; sa[i] = 100 + (i * 37) % 70; sl[i] = i % 5; }
            var drop = PNVisualActor.PlanCasters(sq, sa, sl, 128, out int s2, out int full2);
            int kept = 0; foreach (var k in drop) if (k) kept++;
            Require(full2 == 140 && s2 == 1 && kept == 64, "plain quads cannot coarsen, so the 6 smallest stop casting (64 x 2 = 128)");
            long minKept = long.MaxValue, maxDropped = long.MinValue; for (int i = 0; i < n; i++) { if (drop[i]) minKept = Math.Min(minKept, sa[i]); else maxDropped = Math.Max(maxDropped, sa[i]); }
            Require(minKept >= maxDropped, "only the smallest parts are dropped");
            var again = PNVisualActor.PlanCasters(sq, sa, sl, 128, out _, out _); bool same = true; for (int i = 0; i < n; i++) same &= again[i] == drop[i];
            Require(same, "the plan is deterministic");
        }
        // ---- actors ----
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        PNVisualSystem.Prune(); PNVisualPaperSettings.Mode = PNPaperShadowMode.Actors;
        string root = Application.dataPath + "/StreamingAssets/BaseContent"; PNVisualPacks.Set("project_n", root);
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() },
            new[] { new EveContentDocument("visual", File.ReadAllText(root + "/visual/humanoid.json"), "project_n"), new EveContentDocument("star", File.ReadAllText(root + "/visual/starweaver.json"), "project_n") });
        var scene = EditorSceneManager.NewPreviewScene(); RenderTexture target = null; Texture2D image = null; var previous = RenderTexture.active;
        try
        {
            var cg = new GameObject("Cull camera"); SceneManager.MoveGameObjectToScene(cg, scene);
            var camera = cg.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene; camera.orthographic = true; camera.orthographicSize = .68f;
            camera.aspect = 768f / 1024f; camera.transform.position = new Vector3(.02f, .48f, -5); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.78f, .77f, .72f, 0);
            cg.AddComponent<UniversalAdditionalCameraData>().SetRenderer(1);
            target = new RenderTexture(768, 1024, 24); target.Create(); camera.targetTexture = target; image = new Texture2D(768, 1024, TextureFormat.RGBA32, false);
            var appearance = hub.Get<PNVisualAppearanceDefinition>().Get(EveId.Parse("project_n:starweaver"));
            PNVisualActor Make(string name, Vector3 at)
            {
                var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); go.transform.position = at;
                var a = go.AddComponent<PNVisualActor>(); a.Bind(appearance, hub, Vector3.zero);
                var f = go.GetComponent<PNCameraFacingTarget>(); f.ViewCamera = camera; f.Align(); a.Play("idle", true); return a;
            }
            var actor = Make("Starweaver", Vector3.zero); actor.Tick(.4f);
            Require(actor.CasterTriangles == 92 && !actor.CasterCoarsened && actor.CastersDropped == 0, "Starweaver keeps its 92 caster triangles (under the cap, unchanged)");
            Color32[] Capture(string name) { camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 768, 1024), 0, 0); image.Apply(); File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG()); return image.GetPixels32(); }
            int Diff(Color32[] a, Color32[] b) { int c = 0; for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 8) c++; return c; }

            // ---- setting ----
            var shared = actor.Renderer.sharedMaterial; Require(actor.ShadowActive && shared.GetFloat("_OverlapShadow") > 0, "default mode Actors: the shadow material is used");
            var withShadow = Capture("actors_mode");
            PNVisualPaperSettings.Mode = PNPaperShadowMode.Off; actor.Tick(0);
            Require(!actor.ShadowActive && actor.Renderer.sharedMaterial.GetFloat("_OverlapShadow") == 0f && !actor.Renderer.HasPropertyBlock(), "Off: twin material, no property block (batchable)");
            var off = Capture("off_mode"); Require(Diff(withShadow, off) > 100, "Off removes the shadow from the rendered frame");
            int alphaChanged = 0; for (int i = 0; i < off.Length; i++) if (off[i].a != withShadow[i].a) alphaChanged++;
            Require(alphaChanged == 0, "the shadow never changes the silhouette (alpha identical)");
            PNVisualPaperSettings.Mode = PNPaperShadowMode.KeyActors; actor.Tick(0);
            Require(!actor.ShadowActive, "KeyActors: a normal actor has no shadow");
            actor.KeyActor = true; actor.RefreshShadowMode(); Require(actor.ShadowActive, "KeyActors: a key actor (player/boss) has the shadow");
            Require(Diff(withShadow, Capture("key_actor_mode")) < 10, "a key actor renders the same as in Actors mode");
            actor.KeyActor = false; PNVisualPaperSettings.Mode = PNPaperShadowMode.All; actor.Tick(0); Require(actor.ShadowActive, "All: shadow on");
            PNVisualPaperSettings.Mode = PNPaperShadowMode.Actors; actor.Tick(0);

            // ---- view test: margin + hysteresis ----
            Matrix4x4 vp = camera.projectionMatrix * camera.worldToCameraMatrix; actor.TryGetWorldBounds(out var b0);
            float halfW = camera.orthographicSize * camera.aspect, camX = camera.transform.position.x;
            void PlaceMinX(PNVisualActor a, float minX) { a.TryGetWorldBounds(out var b); a.transform.position += new Vector3(minX - b.min.x, 0, 0); }
            float enter = camX + halfW * (1 + 2 * PNVisualPaperSettings.ViewMargin), exit = camX + halfW * (1 + 2 * (PNVisualPaperSettings.ViewMargin + PNVisualPaperSettings.ExitExtra));
            var edge = Make("Edge", Vector3.zero); edge.Tick(.2f);
            PlaceMinX(edge, camX); Require(edge.UpdateInView(vp), "in view");
            PlaceMinX(edge, enter - halfW * .02f); Require(edge.UpdateInView(vp), "just inside the margin: still prepared");
            PlaceMinX(edge, (enter + exit) * .5f); Require(edge.UpdateInView(vp), "between enter and exit: an actor that was in view stays prepared (hysteresis)");
            PlaceMinX(edge, exit + halfW * .1f); Require(!edge.UpdateInView(vp), "well outside the margin: released");
            PlaceMinX(edge, (enter + exit) * .5f); Require(!edge.UpdateInView(vp), "between enter and exit: an actor that was released stays released (no flicker)");
            PlaceMinX(edge, enter - halfW * .05f); Require(edge.UpdateInView(vp), "inside the enter margin: prepared again");
            edge.transform.position = new Vector3(0, 5, 0); Require(!edge.UpdateInView(vp), "far above: released");

            // ---- lazy pose: out of view only advances; sockets are evaluated on demand ----
            var seen = Make("Seen", Vector3.zero); var hidden = Make("Hidden", Vector3.zero);
            for (int i = 0; i < 30; i++) { seen.Tick(.033f, true); hidden.Tick(.033f, false); }
            seen.TryGetSocketLocal("head_top", out var hs); hidden.TryGetSocketLocal("head_top", out var hh);
            Require(Vector3.Distance(hs, hh) < 1e-5f, "an actor ticked out of view still reports the same socket (evaluated on demand)");
            Require(Mathf.Abs(seen.Animator.BoneAffine[4] - hidden.Animator.BoneAffine[4]) < 1e-5f, "bone pose agrees after re-entry");

            // ---- CPU: out-of-view ticks are cheaper; no allocation ----
            var crowd = new PNVisualActor[96]; for (int i = 0; i < crowd.Length; i++) { crowd[i] = Make("C" + i, new Vector3(i * .01f, 0, 0)); crowd[i].Tick(.1f + i * .01f); }
            double Time(bool write) { var sw = Stopwatch.StartNew(); for (int f = 0; f < 200; f++) foreach (var a in crowd) a.Tick(.016f, write); return sw.Elapsed.TotalMilliseconds / 200; }
            Time(true); double inViewMs = Time(true), outMs = Time(false);
            log.AppendLine("96 actors, CPU per frame: in view " + inViewMs.ToString("F3") + " ms, out of view " + outMs.ToString("F3") + " ms");
            Require(outMs < inViewMs * .6, "an out-of-view actor costs clearly less CPU than one in view");
            vp = camera.projectionMatrix * camera.worldToCameraMatrix;
            for (int i = 0; i < 100; i++) foreach (var a in crowd) { a.UpdateInView(vp); a.Tick(.016f, true); }
            long gc = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 300; i++) foreach (var a in crowd) { a.UpdateInView(vp); a.Tick(.016f, i % 2 == 0); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - gc;
            log.AppendLine("allocated over 300 frames x 96 actors (view test + tick): " + allocated + " bytes");
            Require(allocated <= 4096, "view test + tick allocate nothing per frame");
            foreach (var a in crowd) UnityEngine.Object.DestroyImmediate(a.gameObject);
        }
        finally
        {
            RenderTexture.active = previous; if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            PNVisualPaperSettings.Mode = PNPaperShadowMode.Actors;
            EditorSceneManager.ClosePreviewScene(scene); PNVisualRuntime.Release(); PNVisualPacks.Clear();
        }
        Require(PNVisualSystem.Count == 0 && original == SceneManager.GetActiveScene() && original.isDirty == dirty, "preview cleaned up, authored scene unchanged");
        return "PASS " + checks + " cull/shadow checks\n" + log;
    }
}
#endif
