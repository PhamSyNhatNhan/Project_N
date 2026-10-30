using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
#if UNITY_5_3_OR_NEWER
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
#endif

// EN: Registers the shaders the game itself draws so their GPU programs are compiled under the transition cover (docs: Docs/ShaderWarmupPlan.md,
//     Assets/Game/Runtime/PNShaderWarmupREADME.md). Mods (PC) register theirs the same way. Presentation only: no gameplay rule depends on it.
// VI: Đăng ký các shader của game để biên dịch chương trình GPU lúc màn hình che còn đang giữ.
public static class PNShaderWarmupContent
{
    private static readonly string[] Names =
    {
        "Project_N/VFX/GlassShard", "Project_N/VFX/GlassGlint", "Project_N/VFX/EveEnergy", "Project_N/VFX/EveBlade", "Project_N/VFX/ProjectileTrail", "Project_N/VFX/BuffElemental",
        "Project_N/VFX/TimeScaleArea", "Project_N/VFX/TimeScaleDust", "Project_N/VFX/ArcSlash", "Project_N/Environment/SpriteGroundDepth", "ProjectN/DamagePopupSDF", "ProjectN/DamagePopupSprite"
    };
    public static void Register(EveRegistryHub hub)
    {
        var registry = hub.GetOrCreate<PNShaderWarmupDefinition>();
        foreach (var name in Names)
        {
            string slug = name.Substring(name.LastIndexOf('/') + 1).ToLowerInvariant();
            var id = EveId.Parse("project_n:shader_" + slug);
            registry.Register(new PNShaderWarmupDefinition(id, name));
            registry.RegisterBase(EveId.Parse("base:shader_" + slug), id, "project_n");
        }
        // The visual puppet shader has a compile-time variant for a baked atlas (PN_BAKED) and two passes (one per renderer), and its ink bake shader runs when the atlas is built.
        foreach (var entry in Keyworded)
        {
            var id = EveId.Parse("project_n:shader_" + entry.slug);
            registry.Register(new PNShaderWarmupDefinition(id, entry.shader, entry.keywords));
            registry.RegisterBase(EveId.Parse("base:shader_" + entry.slug), id, "project_n");
        }
    }
    private static readonly (string slug, string shader, string[] keywords)[] Keyworded =
    {
        ("visualactor", "PN/VisualActor", new string[0]), ("visualactor_baked", "PN/VisualActor", new[] { "PN_BAKED" }), ("visualinkbake", "Hidden/PN/VisualInkBake", new string[0]), ("visualshadebake", "Hidden/PN/VisualShadeBake", new string[0]),
    };
}

#if UNITY_5_3_OR_NEWER
public static class PNShaderWarmup
{
    private const int PerFrame = 4;
    private const double PreloadBudgetMs = 6;     // milliseconds of visual atlas work per frame (one image is always done, so a very large image can exceed it)
    private readonly struct Item
    {
        public readonly Shader Shader; public readonly PassType Pass; public readonly string[] Keywords;
        public Item(Shader shader, PassType pass, string[] keywords) { Shader = shader; Pass = pass; Keywords = keywords; }
    }
    private static readonly Queue<Item> pending = new Queue<Item>();
    private static readonly HashSet<string> seen = new HashSet<string>();
    private static readonly HashSet<object> scanned = new HashSet<object>();
    private static Guid generation; private static bool begun, prepared; private static int total, done, frames;
    // Recorded graphics states (phase B): pipeline state objects captured from a real run (PNGraphicsStateRecorder), warmed after the shader variants. Only on APIs that have
    // pipeline states (DX12, Vulkan, Metal) and only when a file recorded for this platform and API ships in StreamingAssets/GraphicsStates.
    private static GraphicsStateCollection states; private static JobHandle statesHandle;
    public const int StatesPerFrame = 8;
    public static string StatesFile => Path.Combine(Application.streamingAssetsPath, "GraphicsStates", Application.platform + "_" + SystemInfo.graphicsDeviceType + ".graphicsstate");
    public static bool StatesLoaded => states != null;
    public static int StatesTotal => states?.totalGraphicsStateCount ?? 0;
    public static int StatesCompleted => states == null ? 0 : states.completedWarmupCount;
    private static bool StatesDone => states == null || states.isWarmedUp;
    public static bool IsDone => begun && prepared && pending.Count == 0 && StatesDone && !PNVisualRuntime.PreloadActive;
    public static float Fraction
    {
        get
        {
            if (!begun) return 0f;
            int preload = PNVisualRuntime.PreloadTotal; int all = total + StatesTotal + preload; if (all == 0) return 1f;
            return Mathf.Clamp01((done + Mathf.Min(StatesCompleted, StatesTotal) + PNVisualRuntime.PreloadDone) / (float)all);
        }
    }
    public static int Compiled => done;
    public static double Seconds { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { Clear(); generation = default; }
    private static void Clear() { states = null; statesHandle = default; pending.Clear(); seen.Clear(); scanned.Clear(); begun = prepared = false; total = done = frames = 0; Seconds = 0; }

    // Once per content generation: later sessions that keep the same content find it already done (or already running).
    public static void Begin(EveSessionContext session) => Begin(session.Registry, PNGameBootstrap.Runtime?.Content?.GenerationId ?? default);
    public static void Begin(EveRegistryHub registry, Guid current)
    {
        if (begun && current == generation) return;
        Clear(); generation = current; begun = true;
        try
        {
            foreach (var pair in registry.Get<PNShaderWarmupDefinition>().Snapshot())
            {
                var definition = pair.Value; var shader = Shader.Find(definition.ShaderName);
                if (shader == null || !shader.isSupported) { Debug.LogWarning("Shader warm-up skipped, shader missing or unsupported: " + definition.ShaderName); continue; }
                Enqueue(shader, definition.Keywords.ToArray());
            }
        }
        catch (Exception error) { Debug.LogException(error); }
        try { PNVisualRuntime.BeginPreload(registry); } catch (Exception error) { Debug.LogException(error); }      // the visual atlas (and its ink bake) is built under the cover, a few ms per frame (Pump)
        LoadStates();
    }
    private static void LoadStates()
    {
        try
        {
            string file = StatesFile;
            if (!File.Exists(file)) { Debug.Log("Graphics states: none recorded for " + Application.platform + "/" + SystemInfo.graphicsDeviceType + " (" + file + ")"); return; }
            var loaded = new GraphicsStateCollection();
            if (!loaded.LoadFromFile(file)) { Debug.LogWarning("Graphics states file could not be read: " + file); return; }
            if (loaded.graphicsDeviceType != SystemInfo.graphicsDeviceType || loaded.runtimePlatform != Application.platform) { Debug.LogWarning("Graphics states file was recorded for " + loaded.runtimePlatform + "/" + loaded.graphicsDeviceType + ", skipped."); return; }
            states = loaded; Debug.Log("Graphics states loaded: " + loaded.variantCount + " variants, " + loaded.totalGraphicsStateCount + " states");
        }
        catch (Exception error) { Debug.LogException(error); states = null; }
    }
    // Mods (PC): shaders that arrive with their world are found by what the world draws (see AddWorld); this is the explicit route for any other shader.
    // Which pass types a shader has depends on its LightMode tags (untagged = Normal, SRPDefaultUnlit, UniversalForward/custom = ScriptableRenderPipeline), so each one is tried
    // when the variant is built and the ones the shader lacks are skipped silently.
    private static readonly PassType[] PassTypes = { PassType.Normal, PassType.ScriptableRenderPipelineDefaultUnlit, PassType.ScriptableRenderPipeline };
    public static void Enqueue(Shader shader, string[] keywords)
    {
        if (shader == null) return;
        keywords = keywords ?? Array.Empty<string>();
        if (!seen.Add(shader.name + "|" + string.Join(",", keywords.OrderBy(x => x, StringComparer.Ordinal)))) return;
        foreach (var pass in PassTypes) { pending.Enqueue(new Item(shader, pass, keywords)); total++; }
    }
    // What a world actually draws: every material of every renderer (entities and particle systems included) with its enabled keywords, so the real variants of
    // standard URP shaders and of shaders that arrive in a mod's scene bundle are compiled too. Each world instance is scanned once.
    public static void AddWorld(EveWorldContext world)
    {
        if (!begun || world == null || !scanned.Add(world)) return;
        ScanScene(world.PhysicsOwner);
    }
    public static void ScanScene(UnityEngine.SceneManagement.Scene scene)
    {
        if (!begun || !scene.IsValid()) return;
        try
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null || material.shader == null || !material.shader.isSupported) continue;
                        Enqueue(material.shader, material.shaderKeywords);
                    }
        }
        catch (Exception error) { Debug.LogException(error); }
    }
    // Called every frame by the transition controller: a few variants per frame so the cover keeps animating; true when nothing is left.
    public static bool Pump()
    {
        if (!begun || IsDone) return IsDone;
        double start = Time.realtimeSinceStartupAsDouble;
        if (!prepared)
        {
            prepared = true;
            try { PNLoricaShatter.Warm(); } catch (Exception error) { Debug.LogException(error); }       // materials, environment image and meshes of the lorica break
        }
        if (PNVisualRuntime.PreloadActive) PNVisualRuntime.PreloadStep(PreloadBudgetMs);
        var batch = new ShaderVariantCollection(); int added = 0;
        while (pending.Count > 0 && added < PerFrame)
        {
            var item = pending.Dequeue(); done++;
            try { if (batch.Add(new ShaderVariantCollection.ShaderVariant(item.Shader, item.Pass, item.Keywords))) added++; }
            catch (ArgumentException) { }      // the shader has no such pass or keyword: nothing to compile
        }
        try { if (added > 0) batch.WarmUp(); } catch (Exception error) { Debug.LogException(error); }
        UnityEngine.Object.Destroy(batch);
        if (pending.Count == 0 && states != null && !states.isWarmedUp)
        {
            try { statesHandle = states.WarmUpProgressively(StatesPerFrame, statesHandle); }
            catch (Exception error) { Debug.LogException(error); states = null; }
        }
        Seconds += Time.realtimeSinceStartupAsDouble - start; frames++;
        if (IsDone) Debug.Log("Shader warm-up done: " + done + " variant requests, " + total + " queued, " + frames + " frames, " + Seconds.ToString("0.000") + " s of work, graphics states " + StatesCompleted + "/" + StatesTotal);
        return IsDone;
    }
}
#endif
