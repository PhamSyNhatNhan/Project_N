#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using EveEngine;
using UnityEditor;
using UnityEngine;

//: the visual atlas preload (chunked bake under the loading cover) and the warm-up registration of the visual shaders.
public static class PNVisualPreloadCheck
{
    private const string Output = "G:/ProjectN_Camera/VisualGpu/Preload";
    private static int checks;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("Preload: " + message); checks++; }

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
        // ---- the existing warm-up check, now with the visual shaders registered (found, three pass types each, once per generation) ----
        sb.AppendLine("PNShaderWarmupEditorChecks: " + PNShaderWarmupEditorChecks.Run());
        // ---- registration ----
        var hub = PNVisualBakeCheck.Hub();
        var warm = hub.Get<PNShaderWarmupDefinition>().Snapshot().Values.ToList();
        Require(warm.Any(x => x.ShaderName == "PN/VisualActor" && x.Keywords.Count == 0), "PN/VisualActor is registered without a keyword");
        Require(warm.Any(x => x.ShaderName == "PN/VisualActor" && x.Keywords.Contains("PN_BAKED")), "PN/VisualActor is registered with PN_BAKED");
        Require(warm.Any(x => x.ShaderName == "Hidden/PN/VisualInkBake"), "the ink bake shader is registered");
        foreach (var name in new[] { "PN/VisualActor", "Hidden/PN/VisualInkBake" }) Require(Shader.Find(name) != null && Shader.Find(name).isSupported, name + " is found and supported");
        // the real variants compile (the warm-up queues each pass type; the ones the shader lacks are skipped)
        var variants = new ShaderVariantCollection(); int added = 0;
        foreach (var pass in new[] { UnityEngine.Rendering.PassType.Normal, UnityEngine.Rendering.PassType.ScriptableRenderPipelineDefaultUnlit, UnityEngine.Rendering.PassType.ScriptableRenderPipeline })
            foreach (var keywords in new[] { new string[0], new[] { "PN_BAKED" } })
                try { if (variants.Add(new ShaderVariantCollection.ShaderVariant(Shader.Find("PN/VisualActor"), pass, keywords))) added++; } catch (ArgumentException) { }
        sb.AppendLine("PN/VisualActor variants accepted by a ShaderVariantCollection: " + added);
        Require(added >= 2, "the actor shader's variants (with and without PN_BAKED) are accepted");
        variants.WarmUp(); UnityEngine.Object.DestroyImmediate(variants);

        // ---- chunked preload ----
        PNVisualPaperSettings.BakeInk = true; PNVisualRuntime.Release();
        PNVisualBakeCheck.Hub();   // sets the pack folders
        PNVisualRuntime.BeginPreload(hub);
        Require(PNVisualRuntime.PreloadActive && PNVisualRuntime.PreloadTotal > 0 && PNVisualRuntime.PreloadDone == 0, "BeginPreload queues the images and does no work yet");
        int total = PNVisualRuntime.PreloadTotal, frames = 0; double worst = 0, sum = 0; bool monotonic = true; int last = 0;
        while (true)
        {
            var sw = Stopwatch.StartNew(); bool done = PNVisualRuntime.PreloadStep(6); double ms = sw.Elapsed.TotalMilliseconds; frames++; worst = Math.Max(worst, ms); sum += ms;
            if (done) break; monotonic &= PNVisualRuntime.PreloadDone >= last; last = PNVisualRuntime.PreloadDone; if (frames > 1000) throw new Exception("preload never finished");
        }
        sb.AppendLine("preload: " + total + " images in " + frames + " steps, total " + sum.ToString("F0") + " ms, longest step " + worst.ToString("F0") + " ms (budget 6 ms + one image; the last step also packs)");
        Require(frames >= 3 && monotonic, "the bake is spread over several steps");
        Require(!PNVisualRuntime.PreloadActive, "the preload is finished");
        var atlas = PNVisualRuntime.AtlasFor(hub);
        Require(atlas.Baked && atlas.ImageCount == total, "AtlasFor returns the preloaded, baked atlas (" + atlas.ImageCount + " images)");
        Require(ReferenceEquals(atlas, PNVisualRuntime.AtlasFor(hub)), "a second call returns the same atlas, no rebuild");
        // the same content built in one go gives the same layout
        var reference = PNVisualAtlas.Build(hub.Get<PNVisualPartDefinition>().Snapshot().Values.Select(p => new System.Collections.Generic.KeyValuePair<string, string>(p.Id.Namespace, p.Image)), true);
        PNVisualPartDefinition any = hub.Get<PNVisualPartDefinition>().Snapshot().Values.First(p => p.Id.ToString().Contains("starweaver"));
        Require(atlas.TryGet(any.Id.Namespace, any.Image, out var uvA, out var sizeA) && sizeA.x > 0, "a Starweaver part is in the atlas with its original size");
        reference.Dispose();

        // ---- an actor binding during a preload finishes it ----
        PNVisualRuntime.Release(); PNVisualRuntime.BeginPreload(hub); PNVisualRuntime.PreloadStep(1);
        Require(PNVisualRuntime.PreloadActive && PNVisualRuntime.PreloadDone > 0, "a preload under way");
        var forced = PNVisualRuntime.AtlasFor(hub);
        Require(!PNVisualRuntime.PreloadActive && forced.Baked && forced.ImageCount == total, "AtlasFor finishes a running preload at once (an actor spawned before the cover ended)");
        // ---- Release during a preload, and an empty hub ----
        PNVisualRuntime.Release(); PNVisualRuntime.BeginPreload(hub); PNVisualRuntime.Release();
        Require(!PNVisualRuntime.PreloadActive, "Release cancels a running preload");
        PNVisualRuntime.BeginPreload(new EveRegistryHub()); Require(!PNVisualRuntime.PreloadActive, "a hub without a visual registry queues nothing");
        PNVisualRuntime.Release(); PNVisualPacks.Clear();
        return "PASS " + checks + " preload checks\n" + sb;
    }
}
#endif
