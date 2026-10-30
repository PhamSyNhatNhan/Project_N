#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using EveEngine;
using UnityEditor;
using UnityEngine;

//: the pre-baked shade computed under the loading cover (a real temporary actor per appearance + build), and what that saves at spawn. Measures the cost.
public static class PNVisualShadePreloadCheck
{
    private const string Output = "G:/ProjectN_Camera/VisualGpu/ShadePreload";
    private static int checks;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("ShadePreload: " + message); checks++; }

    public static void Batch()
    {
        Directory.CreateDirectory(Output);
        try { var r = Run(); File.WriteAllText(Output + "/result.txt", r); UnityEngine.Debug.Log(r); EditorApplication.Exit(0); }
        catch (Exception e) { File.WriteAllText(Output + "/result.txt", e.ToString()); UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static string Run()
    {
        Directory.CreateDirectory(Output);
        var sb = new StringBuilder(); checks = 0;
        sb.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " | " + SystemInfo.graphicsDeviceType);
        var starweaver = EveId.Parse("project_n:starweaver"); var shortBuild = EveId.Parse("project_n:build_short"); const string key = "project_n:starweaver|project_n:build_short|baked";
        var extra = new[] { new KeyValuePair<EveId, EveId?>(starweaver, shortBuild) };
        PNVisualPaperSettings.BakeInk = true; PNVisualPaperSettings.Mode = PNPaperShadowMode.Baked;

        // ---- warm-up registration of the shade bake shader ----
        var hub = PNVisualBakeCheck.Hub();
        Require(hub.Get<PNShaderWarmupDefinition>().Snapshot().Values.Any(x => x.ShaderName == "Hidden/PN/VisualShadeBake"), "the shade bake shader is registered in the warm-up");
        Require(Shader.Find("Hidden/PN/VisualShadeBake") != null && Shader.Find("Hidden/PN/VisualShadeBake").isSupported, "the shade bake shader is found and supported");

        // ---- lazy: no preload, the first actor pays for the shade ----
        PNVisualRuntime.Release(); PNVisualBakeCheck.Hub();
        var swAtlas = Stopwatch.StartNew(); PNVisualRuntime.AtlasFor(hub); double atlasMs = swAtlas.Elapsed.TotalMilliseconds;
        double lazyBind; { var sw = Stopwatch.StartNew(); var go = new GameObject("lazy"); var a = go.AddComponent<PNVisualActor>(); a.Bind(hub.Get<PNVisualAppearanceDefinition>().Get(starweaver), hub, Vector3.zero, shortBuild); lazyBind = sw.Elapsed.TotalMilliseconds; Require(a.ShadeActive && a.ShadeTiles > 0, "lazy: the first actor computes its shade (" + a.ShadeTiles + " tiles)"); UnityEngine.Object.DestroyImmediate(go); }
        double lazyBind2; { var sw = Stopwatch.StartNew(); var go = new GameObject("lazy2"); var a = go.AddComponent<PNVisualActor>(); a.Bind(hub.Get<PNVisualAppearanceDefinition>().Get(starweaver), hub, Vector3.zero, shortBuild); lazyBind2 = sw.Elapsed.TotalMilliseconds; UnityEngine.Object.DestroyImmediate(go); }
        sb.AppendLine("lazy: atlas build " + atlasMs.ToString("F0") + " ms; first Bind of the Starweaver (short) " + lazyBind.ToString("F0") + " ms; second Bind (shade cached) " + lazyBind2.ToString("F0") + " ms");

        // ---- preload: atlas + shade under the cover, in steps ----
        PNVisualRuntime.Release(); PNVisualBakeCheck.Hub();
        PNVisualRuntime.BeginPreload(hub, extra);
        Require(PNVisualRuntime.PreloadActive && PNVisualRuntime.PreloadDone == 0, "BeginPreload queues the work and does none");
        int frames = 0, shadeSteps = 0; double worst = 0, sum = 0; int lastDone = 0; bool monotonic = true; int shadesBefore;
        while (true)
        {
            bool shading = PNVisualRuntime.PreloadDone >= PNVisualRuntime.PreloadTotal - 1;
            var sw = Stopwatch.StartNew(); bool done = PNVisualRuntime.PreloadStep(6); double ms = sw.Elapsed.TotalMilliseconds; frames++; worst = Math.Max(worst, ms); sum += ms;
            monotonic &= PNVisualRuntime.PreloadDone >= lastDone; lastDone = PNVisualRuntime.PreloadDone; if (done) break; if (frames > 1000) throw new Exception("preload never finished");
        }
        shadesBefore = PNVisualRuntime.ShadeTimesMs.Count;
        sb.AppendLine("preload: " + PNVisualRuntime.PreloadTotal + " units (images + shade pairs) in " + frames + " steps, " + sum.ToString("F0") + " ms total, longest step " + worst.ToString("F0") + " ms; shade pairs computed: " + shadesBefore + " (" + string.Join(", ", PNVisualRuntime.ShadeTimesMs.Select(x => x.ToString("F0") + " ms")) + ")");
        Require(monotonic && !PNVisualRuntime.PreloadActive, "the preload progresses monotonically and finishes");
        bool hasShade = PNVisualRuntime.TryGetShade(key, out var cached); Require(shadesBefore == 1 && hasShade && cached.Tiles > 0, "the shade of the Starweaver (short) is cached after the preload");
        Require(PNVisualSystem.Count == 0 && Resources.FindObjectsOfTypeAll<PNVisualActor>().Length == 0 && true, "the temporary actors left nothing behind");
        double warmBind; { var sw = Stopwatch.StartNew(); var go = new GameObject("warm"); var a = go.AddComponent<PNVisualActor>(); a.Bind(hub.Get<PNVisualAppearanceDefinition>().Get(starweaver), hub, Vector3.zero, shortBuild); warmBind = sw.Elapsed.TotalMilliseconds; Require(a.ShadeActive && a.ShadeTiles == cached.Tiles, "a spawned actor uses the preloaded shade"); UnityEngine.Object.DestroyImmediate(go); }
        sb.AppendLine("after the preload: Bind of the Starweaver (short) " + warmBind.ToString("F0") + " ms (was " + lazyBind.ToString("F0") + " ms for the first lazy Bind)");
        Require(warmBind < lazyBind, "a spawn after the preload is cheaper than the first lazy spawn");

        // ---- not in the Baked mode: nothing is queued ----
        PNVisualRuntime.Release(); PNVisualBakeCheck.Hub(); PNVisualPaperSettings.Mode = PNPaperShadowMode.Actors;
        PNVisualRuntime.BeginPreload(hub, extra); int guard = 0; while (!PNVisualRuntime.PreloadStep(6) && guard++ < 1000) { }
        Require(PNVisualRuntime.ShadeTimesMs.Count == 0 && !PNVisualRuntime.TryGetShade(key, out _), "the dynamic modes preload no shade");
        PNVisualPaperSettings.Mode = PNPaperShadowMode.Actors; PNVisualRuntime.Release(); PNVisualPacks.Clear();
        return "PASS " + checks + " shade preload checks\n" + sb;
    }
}
#endif
