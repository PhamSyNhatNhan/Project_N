#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

//: baked ink atlas vs the dynamic shader. Frames, load-time/memory numbers, and a GPU timing table (dynamic vs baked, overlap shadow off/on).
public static class PNVisualBakeCheck
{
    private const string Output = "G:/ProjectN_Camera/VisualGpu/Bake";
    private const int W = 1920, H = 1080;
    private static int checks;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("Bake: " + message); checks++; }

    public static void Batch()
    {
        Directory.CreateDirectory(Output);
        try { var r = Run(); File.WriteAllText(Output + "/result.txt", r); UnityEngine.Debug.Log(r); EditorApplication.Exit(0); }
        catch (Exception e) { File.WriteAllText(Output + "/result.txt", e.ToString()); UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // Look tuning: one frame per (stroke softness, hatch strength) at game scale, plus the dynamic reference, into Bake/Sweep.
    public static void Sweep()
    {
        Directory.CreateDirectory(Output + "/Sweep");
        try
        {
            using (var rig = new Rig(false, 1, W, H)) { rig.Camera.orthographicSize = 2.04f; PNVisualPaperSettings.Mode = PNPaperShadowMode.Off; rig.Actors[0].Tick(0); Shot(rig, "Sweep/dynamic", new Rect(0, 0, W, H)); }
            foreach (var aa in new[] { .2f, .35f, .5f, .75f }) foreach (var hatch in new[] { 1f, .95f, .8f })
            {
                var preset = PNInkBake.Preset.Strong; preset.Hatch = hatch; PNInkBake.Current = preset; PNInkBake.HatchSoftness = aa;
                using (var rig = new Rig(true, 1, W, H)) { rig.Camera.orthographicSize = 2.04f; PNVisualPaperSettings.Mode = PNPaperShadowMode.Off; rig.Actors[0].Tick(0); Shot(rig, "Sweep/b" + aa + "_h" + hatch.ToString("F2"), new Rect(0, 0, W, H)); }
            }
            PNInkBake.Current = PNInkBake.Preset.Strong; PNInkBake.HatchSoftness = 1; PNVisualPaperSettings.BakeInk = true;
            File.WriteAllText(Output + "/Sweep/done.txt", "ok"); EditorApplication.Exit(0);
        }
        catch (Exception e) { File.WriteAllText(Output + "/Sweep/done.txt", e.ToString()); UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
    }

    internal static EveRegistryHub Hub()
    {
        string root = Application.dataPath + "/StreamingAssets/BaseContent"; PNVisualPacks.Set("project_n", root);
        return new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() },
            new[] { new EveContentDocument("visual", File.ReadAllText(root + "/visual/humanoid.json"), "project_n"), new EveContentDocument("star", File.ReadAllText(root + "/visual/starweaver.json"), "project_n") });
    }
    internal static void StrongDynamic(Material m)
    {
        m.SetFloat("_PaperStrength", .025f); m.SetFloat("_OutlineWidth", .55f); m.SetFloat("_ShadowStrength", 0); m.SetFloat("_DrawnAmount", 1); m.SetFloat("_DryInk", .9f);
        m.SetFloat("_HatchStrength", .95f); m.SetFloat("_HatchCoverage", 1); m.SetFloat("_PigmentStrength", .30f); m.SetFloat("_LineWobble", .7f); m.SetFloat("_LooseOutline", .32f); m.SetFloat("_DryFiber", .12f);
        m.SetVector("_OverlapOffset", new Vector4(2, -3.6f, 0, 0)); m.SetFloat("_OverlapSoftness", .8f);
    }

    internal sealed class Rig : IDisposable
    {
        public Scene Scene; public Camera Camera; public RenderTexture Target; public List<PNVisualActor> Actors = new List<PNVisualActor>(); public EveRegistryHub Hub; public double LoadMs;
        public Rig(bool bake, int count, int rw, int rh)
        {
            PNVisualPaperSettings.Mode = PNPaperShadowMode.Actors;   // the dynamic preset below is set on the SHARED material, which only an actor in a shadow mode uses
            PNVisualPaperSettings.BakeInk = bake; PNVisualRuntime.Release(); Hub = PNVisualBakeCheck.Hub();
            Scene = EditorSceneManager.NewPreviewScene();
            var cg = new GameObject("Bake camera"); SceneManager.MoveGameObjectToScene(cg, Scene);
            Camera = cg.AddComponent<Camera>(); Camera.enabled = false; Camera.scene = Scene; Camera.orthographic = true; Camera.aspect = (float)rw / rh;
            Camera.transform.position = new Vector3(0, 0, -5); Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = new Color(.05f, .06f, .10f, 1);
            cg.AddComponent<UniversalAdditionalCameraData>().SetRenderer(1);
            Target = new RenderTexture(rw, rh, 24); Target.Create(); Camera.targetTexture = Target;
            var sw = Stopwatch.StartNew(); var atlas = PNVisualRuntime.AtlasFor(Hub); LoadMs = sw.Elapsed.TotalMilliseconds;
            var appearance = Hub.Get<PNVisualAppearanceDefinition>().Get(EveId.Parse("project_n:starweaver"));
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Starweaver " + i); SceneManager.MoveGameObjectToScene(go, Scene);
                var a = go.AddComponent<PNVisualActor>(); a.Bind(appearance, Hub, Vector3.zero);
                var f = go.GetComponent<PNCameraFacingTarget>(); f.ViewCamera = Camera; f.Align(); a.Play("idle", true); a.Tick(.4f + i * .013f); Actors.Add(a);
            }
            if (!bake) { StrongDynamic(Actors[0].Renderer.sharedMaterial); PNVisualRuntime.RefreshVariants(); }
        }
        public void Dispose()
        {
            foreach (var a in Actors) if (a != null) UnityEngine.Object.DestroyImmediate(a.gameObject);
            if (Target != null) { Target.Release(); UnityEngine.Object.DestroyImmediate(Target); }
            EditorSceneManager.ClosePreviewScene(Scene); PNVisualRuntime.Release(); PNVisualPacks.Clear();
        }
    }

    private static Texture2D Shot(Rig rig, string name, Rect region)
    {
        rig.Camera.Render(); var prev = RenderTexture.active; RenderTexture.active = rig.Target;
        var t = new Texture2D((int)region.width, (int)region.height, TextureFormat.RGBA32, false); t.ReadPixels(region, 0, 0); t.Apply(); RenderTexture.active = prev;
        File.WriteAllBytes(Output + "/" + name + ".png", t.EncodeToPNG()); return t;
    }
    private static double Mad(Texture2D a, Texture2D b)
    {
        var pa = a.GetPixels32(); var pb = b.GetPixels32(); double sum = 0; int n = 0;
        for (int i = 0; i < pa.Length; i++) { int d = Math.Abs(pa[i].r - pb[i].r) + Math.Abs(pa[i].g - pb[i].g) + Math.Abs(pa[i].b - pb[i].b); if (d > 0 || pa[i].r > 20) { sum += d / 3.0; n++; } }
        return n == 0 ? 0 : sum / n;
    }

    public static string Run()
    {
        Directory.CreateDirectory(Output);
        checks = 0; var sb = new StringBuilder(); var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty; PNVisualSystem.Prune();
        sb.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " | " + SystemInfo.graphicsDeviceType);
        Texture2D dynClose, bakClose, dynGame, bakGame;
        // ---- frames + load numbers ----
        double dynLoad, bakLoad; int dynW, dynH, bakW, bakH, images; long dynBytes, bakBytes;
        using (var rig = new Rig(false, 1, 768, 1024))
        {
            var atlas = PNVisualRuntime.AtlasFor(rig.Hub); dynLoad = rig.LoadMs; dynW = atlas.Texture.width; dynH = atlas.Texture.height; images = atlas.ImageCount; dynBytes = (long)dynW * dynH * 4;
            Require(!atlas.Baked, "dynamic atlas is not baked");
            rig.Camera.orthographicSize = .68f; rig.Camera.transform.position = new Vector3(.02f, .48f, -5); PNVisualPaperSettings.Mode = PNPaperShadowMode.Off; rig.Actors[0].Tick(0);
            dynClose = Shot(rig, "dynamic_close", new Rect(0, 0, 768, 1024));
        }
        using (var rig = new Rig(true, 1, 768, 1024))
        {
            var atlas = PNVisualRuntime.AtlasFor(rig.Hub); bakLoad = rig.LoadMs; bakW = atlas.Texture.width; bakH = atlas.Texture.height; bakBytes = (long)bakW * bakH * 4;
            Require(atlas.Baked && atlas.ImageCount == images, "baked atlas holds the same images");
            Require(rig.Actors[0].Renderer.sharedMaterial.IsKeywordEnabled("PN_BAKED"), "material has PN_BAKED");
            rig.Camera.orthographicSize = .68f; rig.Camera.transform.position = new Vector3(.02f, .48f, -5); PNVisualPaperSettings.Mode = PNPaperShadowMode.Off; rig.Actors[0].Tick(0);
            bakClose = Shot(rig, "baked_close", new Rect(0, 0, 768, 1024));
        }
        sb.AppendLine("images " + images + " | atlas dynamic " + dynW + "x" + dynH + " (" + dynBytes / 1048576.0 + " MB) | baked " + bakW + "x" + bakH + " (" + bakBytes / 1048576.0 + " MB)");
        sb.AppendLine("atlas build (load) ms: dynamic " + dynLoad.ToString("F0") + " | baked " + bakLoad.ToString("F0") + " (Editor batch, includes PNG decode + pack)");
        double madClose = Mad(dynClose, bakClose); sb.AppendLine("close-up mean abs diff dynamic vs baked (per covered pixel, 0-255): " + madClose.ToString("F1"));
        Require(bakW <= PNVisualAtlas.MaxSize && bakH <= PNVisualAtlas.MaxSize, "baked atlas fits one page");
        using (var rig = new Rig(false, 1, W, H))
        {
            rig.Camera.orthographicSize = 2.04f; PNVisualPaperSettings.Mode = PNPaperShadowMode.Off; rig.Actors[0].Tick(0); dynGame = Shot(rig, "dynamic_game", new Rect(0, 0, 1920, 1080));
        }
        using (var rig = new Rig(true, 1, W, H))
        {
            rig.Camera.orthographicSize = 2.04f; PNVisualPaperSettings.Mode = PNPaperShadowMode.Off; rig.Actors[0].Tick(0); bakGame = Shot(rig, "baked_game", new Rect(0, 0, 1920, 1080));
        }
        sb.AppendLine("game-scale crop mean abs diff: " + Mad(dynGame, bakGame).ToString("F1"));

        // ---- GPU timing: dynamic vs baked, shadow off/on ----
        var table = new List<string>();
        foreach (var order in (Environment.GetEnvironmentVariable("PN_BAKE_SKIP_GPU") == "1" ? new bool[][] { } : new[] { new[] { false, true }, new[] { true, false } }))
            foreach (var bake in order)
            {
                int count = 112;
                using (var rig = new Rig(bake, count, W, H))
                {
                    var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    double[] Time(int renders)
                    {
                        rig.Camera.Render(); RenderTexture.active = rig.Target; one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                        var sw = Stopwatch.StartNew(); for (int r = 0; r < renders; r++) rig.Camera.Render();
                        double submit = sw.Elapsed.TotalMilliseconds; RenderTexture.active = rig.Target; one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                        return new[] { submit / renders, sw.Elapsed.TotalMilliseconds / renders };
                    }
                    double Median(List<double> l) { l.Sort(); return l[l.Count / 2]; }
                    foreach (var scene in new[] { (n: 16, o: 2.04f, sp: .9f, name: "16 actors game scale"), (n: 112, o: 2.04f, sp: .5f, name: "112 actors game scale"), (n: 1, o: .42f, sp: 0f, name: "1 actor close-up") })
                        foreach (var shadow in new[] { false, true })
                        {
                            rig.Camera.orthographicSize = scene.o; float halfW = scene.o * W / H; int cols = Math.Max(1, Mathf.FloorToInt(2 * halfW / (scene.sp == 0 ? 1 : scene.sp))), rows = Math.Max(1, Mathf.FloorToInt(2 * scene.o / (scene.sp == 0 ? 1 : scene.sp)));
                            PNVisualPaperSettings.Mode = shadow ? PNPaperShadowMode.Actors : PNPaperShadowMode.Off;
                            for (int i = 0; i < rig.Actors.Count; i++)
                            {
                                var a = rig.Actors[i]; bool on = i < scene.n; a.gameObject.SetActive(on); if (!on) continue;
                                int cx = i % cols, cy = (i / cols) % rows;
                                a.transform.position = scene.n == 1 ? Vector3.zero : new Vector3(-halfW + scene.sp * (cx + .5f), scene.o - scene.sp * (cy + .5f) - scene.sp * .5f, 0); a.Tick(0);
                            }
                            var sub = new List<double>(); var tot = new List<double>(); Time(4); int renders = scene.n >= 56 ? 20 : 40;
                            for (int rep = 0; rep < 7; rep++) { var t = Time(renders); sub.Add(t[0]); tot.Add(t[1]); }
                            table.Add(scene.name + " | " + (bake ? "BAKED " : "dynamic") + " | shadow " + (shadow ? "on " : "off") + " | submit " + Median(sub).ToString("F3") + " | total " + Median(tot).ToString("F3"));
                        }
                }
            }
        table.Sort(); sb.AppendLine(); sb.AppendLine("GPU (ms/frame, 1080p, GTX 1650 Ti): scene | atlas | overlap shadow | CPU submit | total to GPU done (each row appears twice: forward and reversed order)");
        foreach (var line in table) sb.AppendLine("  " + line);
        PNVisualPaperSettings.BakeInk = true; PNVisualPaperSettings.Mode = PNPaperShadowMode.Actors;
        Require(PNVisualSystem.Count == 0 && original == SceneManager.GetActiveScene() && original.isDirty == dirty, "preview cleaned up, authored scene unchanged");
        return "PASS " + checks + " bake checks\n" + sb;
    }
}
#endif
