#if UNITY_EDITOR
using System;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEditor;

// EN: Native checks of the shader warm-up (docs: Docs/ShaderWarmupPlan.md): real shaders found, work spread over frames, once per content generation, scene scan of real materials.
public static class PNShaderWarmupEditorChecks
{
    public static string Run()
    {
        int checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); checks++; }
        var hub = new EveRegistryHub(); PNShaderWarmupContent.Register(hub);
        var registry = hub.GetOrCreate<PNShaderWarmupDefinition>();
        int found = registry.Snapshot().Values.Count(x => Shader.Find(x.ShaderName) != null);
        Check(found == registry.Count, "every registered shader exists in the project (found " + found + " of " + registry.Count + ")");
        var generation = Guid.NewGuid();
        PNShaderWarmup.Begin(hub, generation);
        Check(!PNShaderWarmup.IsDone && PNShaderWarmup.Fraction == 0f, "warm-up starts not done at fraction 0");
        int frames = 0; float last = 0f; bool monotonic = true;
        while (!PNShaderWarmup.Pump() && frames++ < 1000) { monotonic &= PNShaderWarmup.Fraction >= last; last = PNShaderWarmup.Fraction; }
        int compiled = PNShaderWarmup.Compiled;
        Check(PNShaderWarmup.IsDone && PNShaderWarmup.Fraction == 1f, "warm-up finishes with fraction 1");
        Check(monotonic && frames >= 2, "work is spread over several frames, a few variants each (" + compiled + " variants, " + frames + " frames)");
        Check(compiled >= registry.Count * 3, "each registered shader queued all three pass types (" + compiled + " variants for " + registry.Count + " shaders)");
        PNShaderWarmup.Begin(hub, generation);
        Check(PNShaderWarmup.IsDone && PNShaderWarmup.Compiled == compiled, "the same content generation is not warmed twice");
        PNShaderWarmup.Begin(hub, Guid.NewGuid());
        Check(!PNShaderWarmup.IsDone && PNShaderWarmup.Compiled == 0, "a new content generation starts over");
        while (!PNShaderWarmup.Pump()) { }
        // Scene scan: a real URP Lit material with its keywords.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.EnableKeyword("_EMISSION"); cube.GetComponent<Renderer>().sharedMaterial = material;
        int before = PNShaderWarmup.Compiled;
        PNShaderWarmup.ScanScene(scene);
        Check(!PNShaderWarmup.IsDone, "a scene with new materials adds work after the registry part was done");
        int pumps = 0; while (!PNShaderWarmup.Pump() && pumps++ < 1000) { }
        Check(PNShaderWarmup.IsDone && PNShaderWarmup.Compiled > before, "scanned materials are warmed (" + (PNShaderWarmup.Compiled - before) + " variants)");
        int again = PNShaderWarmup.Compiled; PNShaderWarmup.ScanScene(scene); while (!PNShaderWarmup.Pump()) { }
        Check(PNShaderWarmup.Compiled == again, "an already known variant is not queued twice");
        UnityEngine.Object.DestroyImmediate(cube); UnityEngine.Object.DestroyImmediate(material);
        // Phase B: the installed recording and the merge tool.
        string installed = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "GraphicsStates", "WindowsPlayer_Direct3D12.graphicsstate");
        Check(System.IO.File.Exists(installed), "a Windows DX12 graphics-state recording is installed in StreamingAssets/GraphicsStates");
        var recording = new UnityEngine.Rendering.GraphicsStateCollection();
        Check(recording.LoadFromFile(installed) && recording.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12 && recording.runtimePlatform == RuntimePlatform.WindowsPlayer && recording.variantCount > 0 && recording.totalGraphicsStateCount > 0, "the recording loads and is for WindowsPlayer/Direct3D12 (" + recording.variantCount + " variants, " + recording.totalGraphicsStateCount + " states)");
        string merged = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PNGscMerge-" + Guid.NewGuid().ToString("N") + ".graphicsstate");
        PNGraphicsStateTools.Merge(new[] { installed, installed }, merged);
        var mergedCollection = new UnityEngine.Rendering.GraphicsStateCollection();
        Check(mergedCollection.LoadFromFile(merged) && mergedCollection.variantCount == recording.variantCount && mergedCollection.totalGraphicsStateCount == recording.totalGraphicsStateCount, "merging a recording with itself keeps the same variants and states");
        System.IO.File.Delete(merged);
        bool rejected = false; try { PNGraphicsStateTools.Merge(new[] { installed, installed + ".missing" }, merged); } catch (InvalidOperationException) { rejected = true; }
        bool empty = false; try { PNGraphicsStateTools.Merge(new string[0], merged); } catch (ArgumentException) { empty = true; }
        Check(rejected && empty && !System.IO.File.Exists(merged), "merge rejects an unreadable input and an empty list without writing");
        string seconds = PNShaderWarmup.Seconds.ToString("0.000");
        PNShaderWarmup.Begin(hub, Guid.NewGuid());
        return "PASS " + checks + " native shader warm-up checks; " + compiled + " registry variants over " + frames + " pumps, " + seconds + " s of the scan run";
    }
}
#endif
