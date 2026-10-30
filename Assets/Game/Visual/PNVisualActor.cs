#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.Rendering;

// EN: The shared run-time state of the Visual system for one content session: the atlas of every PNG the content uses, and the one material all actors share.
// VI: Trạng thái chạy dùng chung của hệ Visual cho một content session: atlas và vật liệu chung.
public static class PNVisualRuntime
{
    // One atlas flavour: `baked` carries the ink marks (shader PN_BAKED), `raw` is the plain image (the shader computes the marks live). Both have the same tile geometry.
    private sealed class Flavor
    {
        public bool Live; public PNVisualAtlas Atlas; public Material Material, NoShadow; public PNVisualAtlas.Builder Builder; public int Total;
        public readonly Dictionary<Texture, Material> Shaded = new Dictionary<Texture, Material>();
    }
    private static Flavor[] flavors = { new Flavor(), new Flavor { Live = true } };
    private static Flavor[] nextFlavors;                                  // a rebuild under way (PNVisualLookApply); the current set keeps drawing until the swap
    private static readonly List<Flavor> retired = new List<Flavor>(); private static readonly List<PNVisualShadeSet> retiredShades = new List<PNVisualShadeSet>();
    private static EveRegistryHub atlasHub, builderHub, rebuildHub;
    private static readonly Dictionary<string, PNVisualShadeSet> shades = new Dictionary<string, PNVisualShadeSet>(StringComparer.Ordinal);
    // Bumped when the atlas set is replaced (Rebuild): an actor compares it and re-attaches.
    public static int Epoch { get; private set; }
    public static bool TryGetShade(string key, out PNVisualShadeSet set) => shades.TryGetValue(key, out set);
    public static void StoreShade(string key, PNVisualShadeSet set) => shades[key] = set;
    private static Flavor Of(bool live) => flavors[live ? 1 : 0];
    private static Flavor FlavorOf(PNVisualAtlas atlas)
    {
        foreach (var f in flavors) if (ReferenceEquals(f.Atlas, atlas)) return f;
        foreach (var f in retired) if (ReferenceEquals(f.Atlas, atlas)) return f;
        if (nextFlavors != null) foreach (var f in nextFlavors) if (ReferenceEquals(f.Atlas, atlas)) return f;
        return Of(!atlas.Baked);
    }
    private static List<KeyValuePair<string, string>> CollectImages(EveRegistryHub hub)
    {
        var images = new List<KeyValuePair<string, string>>();
        foreach (var part in hub.Get<PNVisualPartDefinition>().Snapshot().Values)
        {
            images.Add(new KeyValuePair<string, string>(part.Id.Namespace, part.Image));
            foreach (var variant in part.Variants.Values) images.Add(new KeyValuePair<string, string>(part.Id.Namespace, variant));
        }
        foreach (var motion in hub.Get<PNVisualMotionDefinition>().Snapshot().Values)
            foreach (var frame in motion.Frames) images.Add(new KeyValuePair<string, string>(motion.Id.Namespace, frame));
        return images;
    }
    // Which flavours the current settings need: baked ink -> baked; live key actors -> both; live actors/all -> raw. `BakeInk` false (the old switch, kept for checks) means everything raw.
    private static bool Needs(bool live)
    {
        if (!PNVisualPaperSettings.BakeInk) return live;
        switch (PNVisualPaperSettings.Ink) { case PNInkMode.Baked: return !live; case PNInkMode.LiveKeyActors: return true; default: return live; }
    }
    // The atlas of this content session for one flavour (`live` = the raw atlas). Built on demand (the first actor), or ahead of time in steps by the loading cover (BeginPreload + PreloadStep).
    public static PNVisualAtlas AtlasFor(EveRegistryHub hub, bool live)
    {
        if (hub == null) throw new ArgumentNullException(nameof(hub));
        var f = Of(live);
        if (f.Atlas != null && ReferenceEquals(atlasHub, hub)) return f.Atlas;
        if (f.Builder != null && ReferenceEquals(builderHub, hub))      // a preload is under way: finish it now
        {
            var building = f.Builder; f.Builder = null;
            if (atlasHub != null && !ReferenceEquals(atlasHub, hub)) ReleaseResources();
            using (building) f.Atlas = building.Finish();
            atlasHub = hub; return f.Atlas;
        }
        if (atlasHub != null && !ReferenceEquals(atlasHub, hub)) Release();
        f.Atlas = PNVisualAtlas.Build(CollectImages(hub), !live); atlasHub = hub;
        return f.Atlas;
    }
    // The old single-atlas call: the flavour a non-key actor uses under the current settings.
    public static PNVisualAtlas AtlasFor(EveRegistryHub hub) => AtlasFor(hub, PNVisualPaperSettings.UsesRaw(false));
    // Loading-cover preload: queue the atlas builds (no work yet), then call PreloadStep every frame with a millisecond budget until it returns true.
    public static void BeginPreload(EveRegistryHub hub, IEnumerable<KeyValuePair<EveId, EveId?>> extraShades = null)
    {
        if (hub == null) throw new ArgumentNullException(nameof(hub));
        if (!hub.Snapshot().ContainsKey(typeof(PNVisualPartDefinition))) return;       // this content has no visual registry (a test hub, a headless run): nothing to preload
        bool queued = false; imageTotal = 0;
        foreach (bool live in new[] { false, true })
        {
            var f = Of(live); if (!Needs(live)) continue;
            if (f.Atlas != null && ReferenceEquals(atlasHub, hub)) continue;
            if (f.Builder != null && ReferenceEquals(builderHub, hub)) { queued = true; continue; }
            f.Builder?.Dispose(); f.Builder = new PNVisualAtlas.Builder(CollectImages(hub), !live); f.Total = f.Builder.Total; builderHub = hub; queued = true;
        }
        if (!queued) return;
        shadeTotal = shadeDone = 0; shadeQueue.Clear(); ShadeTimesMs.Clear();
        shadeExtra = extraShades == null ? null : new List<KeyValuePair<EveId, EveId?>>(extraShades); shadeHub = hub;
    }
    private struct ShadeJob { public EveId Appearance; public EveId? Build; public bool Live; }
    private static readonly Queue<ShadeJob> shadeQueue = new Queue<ShadeJob>(); private static int imageTotal, shadeTotal, shadeDone;
    private static List<KeyValuePair<EveId, EveId?>> shadeExtra; private static EveRegistryHub shadeHub;
    // Milliseconds each pre-baked shade took in the last preload (appearance + build), for measurements.
    public static readonly List<double> ShadeTimesMs = new List<double>();
    private static bool AnyBuilder() { foreach (var f in flavors) if (f.Builder != null) return true; return false; }
    public static bool PreloadActive => AnyBuilder() || shadeQueue.Count > 0;
    public static int PreloadTotal { get { int t = shadeTotal; foreach (var f in flavors) t += f.Total; return t; } }
    public static int PreloadDone { get { int d = shadeDone; foreach (var f in flavors) d += f.Builder != null ? f.Builder.Done : f.Total; return d; } }
    private static string ShadeKey(EveId appearance, EveId build, bool live) => appearance + "|" + build + (live ? "|raw" : "|baked");
    public static string ShadeKey(PNVisualAppearanceDefinition appearance, PNVisualBuildDefinition build, bool live) => ShadeKey(appearance.Id, build.Id, live);
    // The (appearance, build) pairs that get pre-baked shade tiles: those the characters of the content spawn (body.visual + body.build), plus any given explicitly; once per atlas flavour that exists. Only in the Baked shadow mode.
    private static void QueueShades(EveRegistryHub hub)
    {
        shadeQueue.Clear(); shadeTotal = shadeDone = 0;
        if (PNVisualPaperSettings.Mode != PNPaperShadowMode.Baked) return;
        var seen = new HashSet<string>(StringComparer.Ordinal); var appearances = hub.Get<PNVisualAppearanceDefinition>();
        void Add(EveId appearance, EveId? build)
        {
            var def = appearances.Get(appearance); var buildId = build ?? def.Build;
            foreach (var f in flavors)
            {
                if (f.Atlas == null) continue; string key = ShadeKey(def.Id, buildId, f.Live);
                if (!seen.Add(key) || shades.ContainsKey(key)) continue;
                shadeQueue.Enqueue(new ShadeJob { Appearance = appearance, Build = build, Live = f.Live });
            }
        }
        if (hub.Snapshot().ContainsKey(typeof(EveCharacterDefinition)))
            foreach (var character in hub.Get<EveCharacterDefinition>().Snapshot().Values)
            {
                PNBodySettings body; try { body = PNBodySettings.Parse(character.Data); } catch (Exception) { continue; }
                if (body.Visual.HasValue) try { Add(body.Visual.Value, body.BuildOverride); } catch (Exception) { }
            }
        if (shadeExtra != null) foreach (var pair in shadeExtra) Add(pair.Key, pair.Value);
        shadeTotal = shadeQueue.Count;
    }
    // One pair, with a real actor: the same code that will run at spawn (rest pose, affine maps, GPU bake), so the cached result is exactly what a spawned actor would compute.
    private static void PreloadOneShade(EveRegistryHub hub)
    {
        var job = shadeQueue.Dequeue(); var clock = System.Diagnostics.Stopwatch.StartNew();
        var go = new GameObject("PN shade preload") { hideFlags = HideFlags.HideAndDontSave };
        try { var actorComponent = go.AddComponent<PNVisualActor>(); actorComponent.Bind(hub.Get<PNVisualAppearanceDefinition>().Get(job.Appearance), hub, Vector3.zero, job.Build, job.Live); actorComponent.PreloadShade(); }
        finally { UnityEngine.Object.DestroyImmediate(go); shadeDone++; ShadeTimesMs.Add(clock.Elapsed.TotalMilliseconds); }
    }
    private static void FinishBuilders()
    {
        var hub = builderHub; if (atlasHub != null && !ReferenceEquals(atlasHub, hub)) ReleaseResources();
        foreach (var f in flavors) if (f.Builder != null) { var done = f.Builder; f.Builder = null; using (done) f.Atlas = done.Finish(); }
        builderHub = null; atlasHub = hub; shadeHub = hub; QueueShades(hub);
    }
    // True when nothing is left to do. A failure is logged and dropped; the first actor then builds (and reports) it the ordinary way.
    public static bool PreloadStep(double budgetMs)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (AnyBuilder())
            {
                foreach (var f in flavors)
                {
                    if (f.Builder == null) continue;
                    double left = Math.Max(0.1, budgetMs - clock.Elapsed.TotalMilliseconds);
                    if (!f.Builder.Step(left)) return false;
                }
                FinishBuilders();
                if (clock.Elapsed.TotalMilliseconds >= budgetMs) return shadeQueue.Count == 0;
            }
            while (shadeQueue.Count > 0)
            {
                PreloadOneShade(shadeHub); if (clock.Elapsed.TotalMilliseconds >= budgetMs) break;       // one pair per step at least; a pair is the unit of work
            }
            return shadeQueue.Count == 0;
        }
        catch (Exception error) { Debug.LogException(error); foreach (var f in flavors) { f.Builder?.Dispose(); f.Builder = null; } builderHub = null; shadeQueue.Clear(); return true; }
    }

    // ---- Rebuild (the Settings Save changed the ink or the shadow look in the Hall) ----
    // BeginRebuild builds the NEW atlas set in steps (RebuildStep, under the loading cover) while the current one keeps drawing; at the swap the new set becomes current, its shade tiles are
    // baked, and only then every actor re-attaches (PNVisualSystem.RebindAll) and the old resources are destroyed, all in one frame.
    public static bool RebuildActive { get; private set; }
    private static int rebuildPhase;                         // 0 atlases, 1 shades
    public static int RebuildTotal { get { int t = shadeTotal; if (nextFlavors != null) foreach (var f in nextFlavors) t += f.Total; return t; } }
    public static int RebuildDone { get { int d = shadeDone; if (nextFlavors != null) foreach (var f in nextFlavors) d += f.Builder != null ? f.Builder.Done : f.Total; return d; } }
    public static void BeginRebuild(EveRegistryHub hub)
    {
        if (hub == null) throw new ArgumentNullException(nameof(hub));
        if (RebuildActive) CancelRebuild();
        if (!hub.Snapshot().ContainsKey(typeof(PNVisualPartDefinition))) return;
        nextFlavors = new[] { new Flavor(), new Flavor { Live = true } }; rebuildHub = hub; rebuildPhase = 0; RebuildActive = true;
        foreach (var f in nextFlavors) if (Needs(f.Live)) { f.Builder = new PNVisualAtlas.Builder(CollectImages(hub), !f.Live); f.Total = f.Builder.Total; }
    }
    private static void CancelRebuild()
    {
        if (nextFlavors != null) foreach (var f in nextFlavors) { f.Builder?.Dispose(); f.Builder = null; DisposeFlavor(f); }
        nextFlavors = null; RebuildActive = false; shadeQueue.Clear();
    }
    // True when the rebuild is finished (the actors already re-attached).
    public static bool RebuildStep(double budgetMs)
    {
        if (!RebuildActive) return true;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (rebuildPhase == 0)
            {
                foreach (var f in nextFlavors)
                {
                    if (f.Builder == null) continue;
                    if (!f.Builder.Step(Math.Max(0.1, budgetMs - clock.Elapsed.TotalMilliseconds))) return false;
                }
                foreach (var f in nextFlavors) if (f.Builder != null) { var done = f.Builder; f.Builder = null; using (done) f.Atlas = done.Finish(); }
                // swap: the old set is retired (still drawing until the rebind), the new one is current
                foreach (var f in flavors) if (f.Atlas != null || f.Material != null) retired.Add(f);
                flavors = nextFlavors; nextFlavors = null; atlasHub = rebuildHub; shadeHub = rebuildHub;
                retiredShades.AddRange(shades.Values); shades.Clear();
                QueueShades(rebuildHub); rebuildPhase = 1;
                if (clock.Elapsed.TotalMilliseconds >= budgetMs) return false;
            }
            while (shadeQueue.Count > 0)
            {
                PreloadOneShade(shadeHub); if (clock.Elapsed.TotalMilliseconds >= budgetMs) return false;
            }
            Epoch++; PNVisualSystem.RebindAll();
            foreach (var set in retiredShades) set.Dispose(); retiredShades.Clear();
            foreach (var f in retired) DisposeFlavor(f); retired.Clear(); PNVisualShade.Release();
            RebuildActive = false; rebuildHub = null; return true;
        }
        catch (Exception error) { Debug.LogException(error); CancelRebuild(); return true; }
    }

    private static void Destroy(UnityEngine.Object o) { if (o == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o); }
    private static void DisposeFlavor(Flavor f)
    {
        foreach (var m in f.Shaded.Values) Destroy(m); f.Shaded.Clear();
        if (f.Atlas != null) { f.Atlas.Dispose(); f.Atlas = null; }
        Destroy(f.Material); f.Material = null; Destroy(f.NoShadow); f.NoShadow = null;
    }
    // `overlapShadow` false returns the twin material with the overlap shadow switched off: an actor on it carries no MaterialPropertyBlock, so the SRP batcher can group it.
    public static Material MaterialFor(PNVisualAtlas forAtlas, bool overlapShadow = true, Texture shadeTexture = null)
    {
        var f = FlavorOf(forAtlas);
        if (f.Material == null)
        {
            var shader = Resources.Load<Shader>("PNVisualActor"); if (shader == null) shader = Shader.Find("PN/VisualActor"); if (shader == null) throw new InvalidOperationException("Shader PN/VisualActor is missing.");
            f.Material = new Material(shader) { name = f.Live ? "PN Visual Actor (live ink)" : "PN Visual Actor", hideFlags = HideFlags.HideAndDontSave, enableInstancing = false };
            if (f.Live) PNInkBake.ApplyLive(f.Material);
        }
        var material = f.Material;
        material.SetTexture("_MainTex", forAtlas.Texture);
        if (forAtlas.Baked) material.EnableKeyword("PN_BAKED"); else material.DisableKeyword("PN_BAKED");   // baked atlas: the shader skips every fixed-to-the-image effect
        if (shadeTexture != null)           // an appearance with pre-baked shadow tiles: its own material (the same shader), no dynamic shadow
        {
            if (!f.Shaded.TryGetValue(shadeTexture, out var shaded) || shaded == null)
            {
                shaded = new Material(MaterialFor(forAtlas, false)) { name = "PN Visual Actor (pre-baked shade)", hideFlags = HideFlags.HideAndDontSave };
                shaded.SetTexture("_ShadeTex", shadeTexture); f.Shaded[shadeTexture] = shaded;
            }
            return shaded;
        }
        if (!overlapShadow)
        {
            if (f.NoShadow == null) f.NoShadow = new Material(material) { name = "PN Visual Actor (no overlap shadow)", hideFlags = HideFlags.HideAndDontSave };
            RefreshVariants();
            return f.NoShadow;
        }
        return material;
    }
    // Copies the shared material's values to the no-shadow twin (call after changing the shared material's properties by code).
    public static void RefreshVariants()
    {
        foreach (var f in flavors) { if (f.NoShadow == null || f.Material == null) continue; f.NoShadow.CopyPropertiesFromMaterial(f.Material); f.NoShadow.SetFloat("_OverlapShadow", 0f); }
    }
    public static void Release()
    {
        CancelRebuild();
        foreach (var f in flavors) { f.Builder?.Dispose(); f.Builder = null; f.Total = 0; }
        builderHub = null; shadeQueue.Clear(); imageTotal = shadeTotal = shadeDone = 0; shadeExtra = null; ShadeTimesMs.Clear();
        ReleaseResources();
    }
    // The atlases, materials and shade tiles (not the preload counters, the queue or builders: a finishing preload replaces the old resources and keeps going).
    private static void ReleaseResources()
    {
        foreach (var set in shades.Values) set.Dispose(); shades.Clear(); PNVisualShade.Release();
        foreach (var set in retiredShades) set.Dispose(); retiredShades.Clear();
        foreach (var f in flavors) DisposeFlavor(f);
        foreach (var f in retired) DisposeFlavor(f); retired.Clear();
        atlasHub = null;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        flavors = new[] { new Flavor(), new Flavor { Live = true } }; nextFlavors = null; retired.Clear(); retiredShades.Clear(); atlasHub = builderHub = rebuildHub = null; shades.Clear(); RebuildActive = false; Epoch = 0;
    }
}

// EN: One rig puppet drawn as ONE mesh (a quad per visible part, sorted by layer) on the shared atlas. It owns a PNVisualAnimator (pure pose maths), writes the vertices each
//     tick without allocating, and exposes sockets and the build's capsule. It never touches gameplay: the entity feeds it a state, a phase, a facing and effects.
//     Camera facing is a PNCameraFacingTarget on the same object, not done here.
// VI: Một con rối rig vẽ bằng MỘT mesh trên atlas chung. Không đụng gameplay: entity chỉ gửi trạng thái, pha, hướng và hiệu ứng.
[ExecuteAlways]                                   // OnDestroy (mesh and registration cleanup) must also run in Edit Mode: previews and checks create actors there
[DisallowMultipleComponent]
public sealed class PNVisualActor : MonoBehaviour
{
    private struct Drawable
    {
        public int Slot, Bone; public float AtX, AtY; public int Layer; public PNVisualPartDefinition Part; public string Namespace;
        public Rect Uv; public Vector2Int Size; public PNVisualTint Tint; public bool Blush;
        public Dictionary<string, KeyValuePair<Rect, Vector2Int>> Variants;
        public int Segments, BendBone, VertexStart; public bool AxisX;   // Segments 1 = a plain quad; otherwise the strips of PNVisualBend driven by the bone's bend/sway
    }
    private PNVisualAnimator animator; private PNVisualResolvedRig rig; private PNVisualAppearanceDefinition appearance; private PNVisualBuildDefinition build;
    // Transparent geometry gutter gives the paper edge/shadow room outside the source image.
    // UV2 bounds stop offset samples from reading a neighbour in the shared atlas.
    private const float PaperPadding = 16f;
    private Drawable[] drawables; private Mesh mesh; private Vector3[] vertices; private Vector2[] uv0, uv1; private Vector4[] uvBounds; private Color32[] colors;
    private Color32[] tintColors; private MeshRenderer meshRenderer; private EveTimeScale clock; private PNCameraFacingTarget facing;
    private float flash, fade = 1f; private bool dirty = true; private float feetY;
    // Clone trial: projected caster triangles, restricted to major overlapping paper parts.
    private const int MaxPaperCasters=128;
    private readonly Vector4[] casterA=new Vector4[MaxPaperCasters],casterB=new Vector4[MaxPaperCasters],casterC=new Vector4[MaxPaperCasters],casterBounds=new Vector4[MaxPaperCasters],casterMeta=new Vector4[MaxPaperCasters];
    private MaterialPropertyBlock paperBlock;private int[] paperIndices;private Vector2[] paperLayer;
    // Caster plan, computed once at Bind: which drawables cast and with how many strips (see BuildCasterPlan). Over the cap it coarsens bent parts, then drops the smallest parts.
    private int[] casterDrawable, casterStride; private PNVisualAtlas atlasInUse; private bool shadowOn, poseDirty, inView = true; private int settingsVersion = -1;
    private static readonly HashSet<string> casterWarned = new HashSet<string>();
    private Vector3[] restVertices; private Vector2[] restUv; private PNVisualShadeSet shade; private bool shadeOn;      // rest pose snapshot (only for an appearance with casters) and the pre-baked shade tiles
    public bool ShadeActive => shadeOn;
    public int ShadeTiles => shade?.Tiles ?? 0;
    public int CasterTriangles { get; private set; }
    public bool CasterCoarsened { get; private set; }
    public int CastersDropped { get; private set; }
    // The player and bosses; the PNPaperShadowMode.KeyActors setting gives the overlap shadow to these only. PNVisualBody sets it for a player; bosses set it when they get a visual.
    public bool KeyActor { get => keyActor; set { if (keyActor == value) return; keyActor = value; Rebind(); } }
    private bool keyActor; private EveRegistryHub hubUsed; private bool? forcedLive; private int epochUsed;
    // The atlas flavour this actor wants under the current settings: raw (live ink) or baked.
    private bool WantsRaw => forcedLive ?? PNVisualPaperSettings.UsesRaw(KeyActor);
    public bool LiveInk => atlasInUse != null && !atlasInUse.Baked;
    public int AtlasEpoch => epochUsed;

    // ---- strings (Docs/VisualInkStringsPlan.md): what the stars hang this puppet by. Drawn by PNVisualStringSystem, one mesh for every puppet. Presentation only. ----
    private PNVisualStringSet strings;
    public PNVisualStringSet Strings => strings;
    public bool HasStrings => strings != null && strings.HasStrings;
    public bool FacingLeft => animator != null && animator.FacingLeft;
    private PNVisualStringSet EnsureStrings() => strings ?? (strings = new PNVisualStringSet(hubUsed, appearance.Id.ToString(), null));
    public int AddString(PNVisualStringEntry entry, float lowerSeconds = 0f) => animator == null ? -1 : EnsureStrings().Add(entry, lowerSeconds);
    public void RemoveString(int handle, bool cut = true) => strings?.Remove(handle, cut);
    public void SetString(int handle, PNVisualStringEntry entry, float blendSeconds = 0f) => strings?.Set(handle, entry, blendSeconds);
    public void SetStringColor(int handle, EveId color, float blendSeconds = 0f) => strings?.SetColor(handle, color, blendSeconds);
    public void SetStringShape(int handle, EveId shape, float blendSeconds = 0f) => strings?.SetShape(handle, shape, blendSeconds);
    public void SetStringsColor(EveId color, float blendSeconds = 0f) => strings?.SetAllColors(color, blendSeconds);
    public void CutStrings() => strings?.CutAll();
    public void LowerStrings(float seconds = 0f) => strings?.LowerAll(seconds);
    public void ShowStrings() => strings?.ShowAll();
    public void HideStrings() => strings?.HideAll();
    internal void PulseStringEffects()=>strings?.Hit();
    // Re-attaches to the atlas the settings want now (a rebuilt atlas set after Save, or a KeyActor flip under "live: player and bosses"). Pose, animator, effects, facing and the mesh stay;
    // only the uv rects, the material and the shade tiles are redone.
    public void Rebind()
    {
        if (animator == null || hubUsed == null || drawables == null) return;
        var next = PNVisualRuntime.AtlasFor(hubUsed, WantsRaw);
        if (ReferenceEquals(next, atlasInUse) && epochUsed == PNVisualRuntime.Epoch) return;
        var oldUv = new Rect[drawables.Length]; for (int i = 0; i < oldUv.Length; i++) oldUv[i] = drawables[i].Uv;
        BuildDrawables(hubUsed, next);
        if (restUv != null)           // same art, same tile geometry in both flavours: the rest-pose uv moves with its rect
            for (int i = 0; i < drawables.Length && i < oldUv.Length; i++)
            {
                var o = oldUv[i]; var n = drawables[i].Uv; int count = drawables[i].Segments == 1 ? 4 : 2 * (drawables[i].Segments + 1);
                for (int k = 0; k < count; k++)
                {
                    var u = restUv[drawables[i].VertexStart + k];
                    restUv[drawables[i].VertexStart + k] = new Vector2(n.x + (u.x - o.x) / o.width * n.width, n.y + (u.y - o.y) / o.height * n.height);
                }
            }
        atlasInUse = next; epochUsed = PNVisualRuntime.Epoch; shade = null; shadeOn = false; dirty = true;
        ApplyShadowMode();
        if (vertices != null) { EnsurePose(); WriteMesh(); }
    }
    public bool ShadowActive => shadowOn;
    public bool InView => inView;
    private readonly float[] rowX = new float[PNVisualLimits.MaxSegments + 1], rowY = new float[PNVisualLimits.MaxSegments + 1], rowNx = new float[PNVisualLimits.MaxSegments + 1], rowNy = new float[PNVisualLimits.MaxSegments + 1];

    // Reading the animator evaluates a pose that was only advanced while the actor was out of view, so sockets and bones are never stale.
    public PNVisualAnimator Animator { get { EnsurePose(); return animator; } }
    private void EnsurePose() { if (poseDirty && animator != null) { animator.Evaluate(); poseDirty = false; } }
    public PNVisualAppearanceDefinition Appearance => appearance;
    public int QuadCount => drawables == null ? 0 : drawables.Length;
    public Mesh Mesh => mesh;
    public MeshRenderer Renderer => meshRenderer;
    // The capsule of this body in metres: the rig's own size times the build's factors.
    public float CapsuleHeight { get; private set; }
    public float CapsuleRadius { get; private set; }
    public float ShadowScale { get; private set; } = 1f;
    public bool Visible => meshRenderer != null && meshRenderer.isVisible;

    // Builds the puppet. `feetLocal` is where the feet are in the parent's space (the mesh origin sits there); `buildOverride` replaces the appearance's build.
    public void Bind(PNVisualAppearanceDefinition definition, EveRegistryHub hub, Vector3 feetLocal, EveId? buildOverride = null, bool? forceLive = null)
    {
        if (animator != null) throw new InvalidOperationException("This Visual actor is already bound.");
        appearance = definition ?? throw new ArgumentNullException(nameof(definition)); if (hub == null) throw new ArgumentNullException(nameof(hub));
        var rigs = hub.Get<PNVisualRigDefinition>(); var rigDef = rigs.Get(appearance.Rig); rig = rigDef.Resolve(id => rigs.Get(id));
        build = hub.Get<PNVisualBuildDefinition>().Get(buildOverride ?? appearance.Build);
        var set = hub.Get<PNVisualMotionSetDefinition>().Get(appearance.MotionSet);
        var motions = hub.Get<PNVisualMotionDefinition>();
        animator = new PNVisualAnimator(rig, set, id => motions.Get(id), build);
        if (appearance.Expression.IsValid) animator.SetExpression(hub.Get<PNVisualExpressionDefinition>().Get(appearance.Expression));
        CapsuleHeight = rigDef.CapsuleHeight * build.CapsuleHeight; CapsuleRadius = rigDef.CapsuleRadius * build.CapsuleRadius; ShadowScale = build.Shadow;
        hubUsed = hub; forcedLive = forceLive;
        var atlas = PNVisualRuntime.AtlasFor(hub, WantsRaw); epochUsed = PNVisualRuntime.Epoch;
        BuildDrawables(hub, atlas);
        tintColors = new Color32[6];
        for (int i = 0; i < tintColors.Length; i++) tintColors[i] = new Color32(255, 255, 255, 255);
        foreach (var pair in appearance.Colors) { int t = TintIndex(pair.Key); if (t > 0) tintColors[t] = ToColor32(pair.Value); }
        BuildMesh(atlas); atlasInUse = atlas; BuildCasterPlan(); ApplyShadowMode();
        feetY = feetLocal.y;
        var facingTarget = GetComponent<PNCameraFacingTarget>(); if (facingTarget == null) facingTarget = gameObject.AddComponent<PNCameraFacingTarget>();
        facingTarget.FootOffset = 0f; facingTarget.FootAnchor = feetLocal; facing = facingTarget;
        clock = GetComponentInParent<EveTimeScale>();
        if (appearance.Strings.Count > 0) strings = new PNVisualStringSet(hub, appearance.Id.ToString(), appearance.Strings);
        PNVisualSystem.Register(this);
        animator.Evaluate(); WriteMesh();
        if (casterDrawable != null && casterDrawable.Length > 0) { restVertices = (Vector3[])vertices.Clone(); restUv = (Vector2[])uv0.Clone(); ApplyShadowMode(); }     // the rest pose: the pre-baked shadow is computed from it
    }

    private static int TintIndex(string name)
    {
        switch (name) { case "skin": return (int)PNVisualTint.Skin; case "eye": return (int)PNVisualTint.Eye; case "hair": return (int)PNVisualTint.Hair; case "dye1": return (int)PNVisualTint.Dye1; case "dye2": return (int)PNVisualTint.Dye2; default: return 0; }
    }
    private static Color32 ToColor32(PNVisualColor c) => new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(c.R) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(c.G) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(c.B) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(c.A) * 255f));

    private void BuildDrawables(EveRegistryHub hub, PNVisualAtlas atlas)
    {
        var parts = hub.Get<PNVisualPartDefinition>(); var list = new List<Drawable>(); var hidden = new HashSet<string>(appearance.Hide);
        Drawable Make(PNVisualPartDefinition part)
        {
            if (!atlas.TryGet(part.Id.Namespace, part.Image, out var uv, out var size)) throw new InvalidOperationException("Visual image is not in the atlas: " + part.Image);
            var d = new Drawable { Part = part, Namespace = part.Id.Namespace, Uv = uv, Size = size, Tint = part.Tint, Segments = part.Segments, AxisX = part.Axis == PNVisualBendAxis.X, BendBone = -1 };
            if (part.Variants.Count > 0)
            {
                d.Variants = new Dictionary<string, KeyValuePair<Rect, Vector2Int>>(StringComparer.Ordinal);
                foreach (var v in part.Variants) if (atlas.TryGet(part.Id.Namespace, v.Value, out var vuv, out var vsize)) d.Variants[v.Key] = new KeyValuePair<Rect, Vector2Int>(vuv, vsize);
            }
            return d;
        }
        for (int slot = 0; slot < rig.Slots.Count; slot++)
        {
            var s = rig.Slots[slot];
            if (hidden.Contains(s.Id) || !appearance.Slots.TryGetValue(s.Id, out var partId)) continue;
            var d = Make(parts.Get(partId)); d.Slot = slot; d.Bone = -1; d.BendBone = rig.BoneIndex(s.Bone); d.Layer = s.Layer; d.Blush = s.Id.StartsWith("blush", StringComparison.Ordinal); list.Add(d);
        }
        foreach (var free in appearance.Free)
        {
            var d = Make(parts.Get(free.Part)); d.Slot = -1; d.Bone = rig.BoneIndex(free.Bone); d.BendBone = d.Bone; d.AtX = free.X; d.AtY = free.Y; d.Layer = free.Layer; list.Add(d);
        }
        // stable sort by layer: the vertex order IS the draw order, so no sorting group is needed
        drawables = list.Select((d, i) => new { d, i }).OrderBy(x => x.d.Layer).ThenBy(x => x.i).Select(x => x.d).ToArray();
        int vertexCount = 0;
        for (int i = 0; i < drawables.Length; i++) { drawables[i].VertexStart = vertexCount; vertexCount += drawables[i].Segments == 1 ? 4 : 2 * (drawables[i].Segments + 1); }
    }

    private void BuildMesh(PNVisualAtlas atlas)
    {
        int n = drawables.Length; int vertexCount = n == 0 ? 0 : drawables[n - 1].VertexStart + (drawables[n - 1].Segments == 1 ? 4 : 2 * (drawables[n - 1].Segments + 1));
        mesh = new Mesh { name = "PN Visual Actor", hideFlags = HideFlags.HideAndDontSave }; mesh.MarkDynamic();
        vertices = new Vector3[vertexCount]; uv0 = new Vector2[vertexCount]; uv1 = new Vector2[vertexCount]; uvBounds = new Vector4[vertexCount]; colors = new Color32[vertexCount];
        int triangleCount = 0; for (int i = 0; i < n; i++) triangleCount += drawables[i].Segments * 6;
        var tris = new int[triangleCount]; int t = 0;
        for (int i = 0; i < n; i++)
        {
            int v = drawables[i].VertexStart;
            if (drawables[i].Segments == 1) { tris[t++] = v; tris[t++] = v + 1; tris[t++] = v + 2; tris[t++] = v; tris[t++] = v + 2; tris[t++] = v + 3; continue; }
            // strips: row k holds (A, B) at v + 2k, v + 2k + 1; A is the low side across the axis, B the high side; the order matches the plain quad's winding
            for (int k = 0; k < drawables[i].Segments; k++)
            {
                int a0 = v + 2 * k, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                if (drawables[i].AxisX) { tris[t++] = b0; tris[t++] = b1; tris[t++] = a1; tris[t++] = b0; tris[t++] = a1; tris[t++] = a0; }
                else { tris[t++] = a1; tris[t++] = b1; tris[t++] = b0; tris[t++] = a1; tris[t++] = b0; tris[t++] = a0; }
            }
        }
        paperIndices=tris;paperLayer=new Vector2[vertexCount];paperBlock=new MaterialPropertyBlock();
        mesh.vertices = vertices; mesh.triangles = tris; mesh.uv = uv0; mesh.colors32 = colors;
        mesh.bounds = new Bounds(new Vector3(0f, CapsuleHeight * .5f, 0f), new Vector3(CapsuleHeight * 2f, CapsuleHeight * 2.5f, .1f));   // fixed: no per-frame bounds work
        var filter = GetComponent<MeshFilter>(); if (filter == null) filter = gameObject.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
        meshRenderer = GetComponent<MeshRenderer>(); if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = PNVisualRuntime.MaterialFor(atlas, true);
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off; meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off; meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off; meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    // ---- gameplay-facing API ----
    public bool Play(string state, bool restart = false) => animator != null && animator.Play(state, restart);
    public void SetPhase(float phase) => animator?.SetPhase(phase);
    public void SetFacingLeft(bool left) { if (animator != null) animator.FacingLeft = left; }
    public void SetExpression(PNVisualExpressionDefinition expression) => animator?.SetExpression(expression);
    public void SetRate(float rate) { if (animator != null) animator.RateMultiplier = rate; }
    // Effects for the WHOLE actor: flash towards white (0..1) and fade (alpha multiplier 0..1).
    public void SetFlash(float value) { flash = Mathf.Clamp01(value); dirty = true; }
    public void SetFade(float value) { fade = Mathf.Clamp01(value); dirty = true; }

    // Socket position in the actor's local space (metres), or false when the rig has no such socket.
    public bool TryGetSocketLocal(string id, out Vector3 position)
    {
        position = default; if (animator == null) return false;
        int i = rig.SocketIndex(id); if (i < 0) return false;
        EnsurePose();
        position = new Vector3(animator.SocketPosition[i * 2], animator.SocketPosition[i * 2 + 1], 0f); return true;
    }
    public bool TryGetSocketWorld(string id, out Vector3 position)
    {
        if (!TryGetSocketLocal(id, out var local)) { position = default; return false; }
        position = transform.TransformPoint(local); return true;
    }

    // One frame: advance the pose (entity clock seconds), evaluate it and, when the mesh is visible (or `force`), write the vertices.
    public void Tick(float dt, bool writeMesh = true)
    {
        if (animator == null) return;
        if (settingsVersion != PNVisualPaperSettings.Version) ApplyShadowMode();
        strings?.FeedPosition(transform.position,dt);
        strings?.Advance(dt);                                  // the strings lower and fall away on the same clock
        animator.Advance(dt);                                  // time and blends always advance, so motions keep their meaning while the actor is out of view
        if (writeMesh) { animator.Evaluate(); poseDirty = false; WriteMesh(); }
        else poseDirty = true;                                 // evaluated on demand (EnsurePose) or on re-entry
    }

    // The overlap-shadow setting applied to this actor: shadow on = the shared material plus a casters block; off = the twin material, no block (batchable).
    private void ApplyShadowMode()
    {
        settingsVersion = PNVisualPaperSettings.Version; var mode = PNVisualPaperSettings.Mode;
        bool dynamicShadow = PNVisualPaperSettings.Wants(mode, KeyActor) && CasterTriangles > 0;
        bool bakedWanted = mode == PNPaperShadowMode.Baked && CasterTriangles > 0;
        if (bakedWanted && shade == null && restVertices != null && atlasInUse != null) shade = BuildShade();
        shadeOn = bakedWanted && shade != null && shade.Tiles > 0; shadowOn = dynamicShadow;
        if (meshRenderer == null || atlasInUse == null) return;
        var material = PNVisualRuntime.MaterialFor(atlasInUse, dynamicShadow, shadeOn ? shade.Texture : null);
        if (!ReferenceEquals(meshRenderer.sharedMaterial, material)) meshRenderer.sharedMaterial = material;
        if (!dynamicShadow) meshRenderer.SetPropertyBlock(null);
        else if (casterDrawable != null) WritePaperCasters();       // switched on mid-life: the block was cleared, so write it now instead of waiting for the next mesh write
        dirty = true;
        if (writtenOnce) { EnsurePose(); WriteMesh(); }               // the pre-baked tiles change the uv of the receiving parts
    }
    private bool writtenOnce => vertices != null && restVertices != null;

    // Once per appearance + build (cached): the overlap shadow of the rest pose, computed on the GPU into copies of the receiving parts.
    private PNVisualShadeSet BuildShade()
    {
        string key = PNVisualRuntime.ShadeKey(appearance, build, !atlasInUse.Baked);
        if (PNVisualRuntime.TryGetShade(key, out var cached)) return cached;
        PNVisualShade.Affine AffineOf(int di)
        {
            var d = drawables[di]; int v = d.VertexStart; int ia = v, ib = v + 1, ic = d.Segments == 1 ? v + 3 : v + 2 * d.Segments;
            return PNVisualShade.Affine.From(restUv[ia], restUv[ib], restUv[ic], restVertices[ia], restVertices[ib], restVertices[ic]);
        }
        void Extent(int di, out Rect uvRect, out Vector2 lo, out Vector2 hi)
        {
            var d = drawables[di]; int count = d.Segments == 1 ? 4 : 2 * (d.Segments + 1); lo = new Vector2(float.MaxValue, float.MaxValue); hi = new Vector2(float.MinValue, float.MinValue);
            Vector2 uvLo = lo, uvHi = hi;
            for (int k = 0; k < count; k++)
            {
                var p = restVertices[d.VertexStart + k]; var u = restUv[d.VertexStart + k];
                lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); uvLo = Vector2.Min(uvLo, u); uvHi = Vector2.Max(uvHi, u);
            }
            uvRect = Rect.MinMaxRect(uvLo.x, uvLo.y, uvHi.x, uvHi.y);
        }
        var casters = new List<(int index, PNVisualShade.Caster caster, Vector2 lo, Vector2 hi)>();
        foreach (var ci in casterDrawable)
        {
            Extent(ci, out var rect, out var lo, out var hi); var affine = AffineOf(ci); if (!affine.Valid) continue;
            casters.Add((ci, new PNVisualShade.Caster { UvToLocal = affine, Bounds = rect }, lo, hi));
        }
        var receivers = new List<PNVisualShade.Receiver>();
        for (int i = 0; i < drawables.Length; i++)
        {
            Extent(i, out var outer, out var lo, out var hi); var affine = AffineOf(i); if (!affine.Valid) continue;
            var receiver = new PNVisualShade.Receiver { Drawable = i, Outer = outer, UvToLocal = affine };
            foreach (var c in casters)
                if (c.index != i && drawables[c.index].Layer > drawables[i].Layer && c.lo.x < hi.x && c.hi.x > lo.x && c.lo.y < hi.y && c.hi.y > lo.y) receiver.Casters.Add(c.caster);
            if (receiver.Casters.Count > 0) receivers.Add(receiver);
        }
        var shared = PNVisualRuntime.MaterialFor(atlasInUse, true);
        var offset = shared.GetVector("_OverlapOffset");
        var set = PNVisualShade.Bake(atlasInUse.Texture, drawables.Length, receivers, shared.GetFloat("_OverlapShadow"), new Vector2(offset.x, offset.y), shared.GetFloat("_OverlapSoftness"));
        PNVisualRuntime.StoreShade(key, set); return set;
    }

    // Loading-cover preload: compute this appearance + build's pre-baked shade tiles now (cached), if it has casters and the atlas is baked.
    public void PreloadShade() { if (shade == null && restVertices != null && atlasInUse != null && CasterTriangles > 0) shade = BuildShade(); }
    // Call after changing KeyActor (the setting may now include or exclude this actor).
    public void RefreshShadowMode() => ApplyShadowMode();

    // World bounds of the puppet: what the view test uses (not only the pivot).
    public bool TryGetWorldBounds(out Bounds bounds)
    {
        bounds = default; if (meshRenderer == null) return false;
        bounds = meshRenderer.bounds;
        if (strings != null && strings.Any) bounds.Encapsulate(bounds.center + transform.up * StringReach);   // the strings run up to the top of the view: the puppet keeps updating while its strings are on screen
        return true;
    }
    // Hysteresis view test against a precomputed view-projection matrix; the actor turns on inside the margin and off only ExitExtra further out.
    public bool UpdateInView(in Matrix4x4 viewProjection)
    {
        if (!TryGetWorldBounds(out var b)) return inView = true;
        inView = PNVisualSystem.Overlaps(viewProjection, b, PNVisualPaperSettings.ViewMargin + (inView ? PNVisualPaperSettings.ExitExtra : 0f));
        return inView;
    }
    private const float StringReach = 20f;
    public float ClockDelta() => clock != null ? clock.DeltaTime : Time.deltaTime;

    private void WriteMesh()
    {
        var a = animator;
        for (int i = 0; i < drawables.Length; i++)
        {
            ref var d = ref drawables[i];
            float ta, tb, tc, td, ttx, tty;
            if (d.Slot >= 0)
            {
                int o = d.Slot * 6; ta = a.SlotAffine[o]; tb = a.SlotAffine[o + 1]; tc = a.SlotAffine[o + 2]; td = a.SlotAffine[o + 3]; ttx = a.SlotAffine[o + 4]; tty = a.SlotAffine[o + 5];
            }
            else
            {
                int o = d.Bone * 6; float sx = a.BoneScale[d.Bone * 2], sy = a.BoneScale[d.Bone * 2 + 1];
                float ba = a.BoneAffine[o], bb = a.BoneAffine[o + 1], bc = a.BoneAffine[o + 2], bd = a.BoneAffine[o + 3], btx = a.BoneAffine[o + 4], bty = a.BoneAffine[o + 5];
                float ax = d.AtX * sx, ay = d.AtY * sy;
                ta = ba * sx; tb = bb * sx; tc = bc * sy; td = bd * sy; ttx = ba * ax + bc * ay + btx; tty = bb * ax + bd * ay + bty;
            }
            Rect uv = d.Uv; Vector2Int size = d.Size; bool variantUsed = false;
            if (d.Slot >= 0 && d.Variants != null) { var name = a.SlotVariant[d.Slot]; if (name.Length > 0 && d.Variants.TryGetValue(name, out var variant)) { uv = variant.Key; size = variant.Value; variantUsed = true; } }
            bool shaded = shadeOn && !variantUsed && shade.Uv[i].width > 0f;       // this part is drawn from its pre-baked shaded copy
            Rect sourceUv;
            if (shaded)
            {
                uv = shade.Uv[i]; float kx = size.x / (size.x + 2f * PaperPadding), ky = size.y / (size.y + 2f * PaperPadding);
                sourceUv = new Rect(uv.x + uv.width * (1f - kx) * .5f, uv.y + uv.height * (1f - ky) * .5f, uv.width * kx, uv.height * ky);
            }
            else
            {
                sourceUv = uv; float padU = uv.width * PaperPadding / size.x, padV = uv.height * PaperPadding / size.y;
                uv = Rect.MinMaxRect(uv.xMin - padU, uv.yMin - padV, uv.xMax + padU, uv.yMax + padV);
            }
            float x0 = -d.Part.PivotX - PaperPadding, y0 = -d.Part.PivotY - PaperPadding, x1 = size.x - d.Part.PivotX + PaperPadding, y1 = size.y - d.Part.PivotY + PaperPadding;
            int v = d.VertexStart, count;
            if (d.Segments == 1)
            {
                count = 4;
                vertices[v] = new Vector3(ta * x0 + tc * y1 + ttx, tb * x0 + td * y1 + tty, 0f);
                vertices[v + 1] = new Vector3(ta * x1 + tc * y1 + ttx, tb * x1 + td * y1 + tty, 0f);
                vertices[v + 2] = new Vector3(ta * x1 + tc * y0 + ttx, tb * x1 + td * y0 + tty, 0f);
                vertices[v + 3] = new Vector3(ta * x0 + tc * y0 + ttx, tb * x0 + td * y0 + tty, 0f);
                uv0[v] = new Vector2(uv.x, uv.y); uv0[v + 1] = new Vector2(uv.xMax, uv.y); uv0[v + 2] = new Vector2(uv.xMax, uv.yMax); uv0[v + 3] = new Vector2(uv.x, uv.yMax);
            }
            else
            {
                count = 2 * (d.Segments + 1);
                float bend = d.BendBone >= 0 ? a.BoneBend[d.BendBone * 2] : 0f, sway = d.BendBone >= 0 ? a.BoneBend[d.BendBone * 2 + 1] : 0f;
                float length = d.AxisX ? size.x : size.y, pivotAlong = d.AxisX ? d.Part.PivotX : d.Part.PivotY, lowAcross = d.AxisX ? y0 : x0, highAcross = d.AxisX ? y1 : x1;
                PNVisualBend.Rows(d.Segments, length, pivotAlong, d.AxisX, bend, sway, rowX, rowY, rowNx, rowNy);
                for (int k = 0; k <= d.Segments; k++)
                {
                    float endPad = k == 0 ? -PaperPadding : k == d.Segments ? PaperPadding : 0f;
                    float cx = rowX[k] + (d.AxisX ? rowNy[k] : -rowNy[k]) * endPad;
                    float cy = rowY[k] + (d.AxisX ? -rowNx[k] : rowNx[k]) * endPad;
                    float t = (float)k / d.Segments, lx = cx + rowNx[k] * lowAcross, ly = cy + rowNy[k] * lowAcross, hx = cx + rowNx[k] * highAcross, hy = cy + rowNy[k] * highAcross;
                    int row = v + 2 * k;
                    vertices[row] = new Vector3(ta * lx + tc * ly + ttx, tb * lx + td * ly + tty, 0f);
                    vertices[row + 1] = new Vector3(ta * hx + tc * hy + ttx, tb * hx + td * hy + tty, 0f);
                    if (d.AxisX) { float u = sourceUv.x + t * sourceUv.width + endPad * sourceUv.width / size.x; uv0[row] = new Vector2(u, uv.yMax); uv0[row + 1] = new Vector2(u, uv.y); }
                    else { float w = sourceUv.yMax - t * sourceUv.height - endPad * sourceUv.height / size.y; uv0[row] = new Vector2(uv.x, w); uv0[row + 1] = new Vector2(uv.xMax, w); }
                }
            }
            var tint = tintColors[(int)d.Tint]; float alpha = d.Slot >= 0 ? a.SlotAlpha[d.Slot] : 1f; if (d.Blush) alpha *= appearance.BlushAlpha;
            var color = new Color32(tint.r, tint.g, tint.b, (byte)Mathf.Clamp(Mathf.RoundToInt(tint.a * alpha), 0, 255));
            var effect = new Vector2(flash, fade);
            var bounds = new Vector4(uv.xMin, uv.yMin, uv.xMax, uv.yMax);   // both flavours carry the transparent border (baked: with the marks), so samples may read all of it
            for (int k = 0; k < count; k++) { colors[v + k] = color; uv1[v + k] = effect; uvBounds[v + k] = bounds; paperLayer[v+k]=new Vector2(d.Layer,shaded?1f:0f); }
        }
        mesh.SetVertices(vertices); mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.SetUVs(2, uvBounds); mesh.SetColors(colors);
        mesh.SetUVs(3,paperLayer); if (shadowOn) WritePaperCasters();
        dirty = false;
    }

    // Casting parts (the large overlapping ones; names are the Starweaver's slots for now, a data flag later).
    private bool Casts(int i)
    {
        string slot = drawables[i].Slot >= 0 ? rig.Slots[drawables[i].Slot].Id : "";
        return slot == "hat" || slot == "hatBack" || slot == "mask" || slot == "cloakL" || slot == "cloakR" || slot == "cloakBack" || slot == "collar";
    }
    private static int Strips(int segments, int stride) => segments == 1 ? 1 : (segments + stride - 1) / stride;

    // Once per appearance, never per frame. Within MaxPaperCasters triangles every strip casts (exactly the old behaviour). Over it, never an error:
    // first bent parts cast with coarser strips (stride 2, 4, ... strips per caster quad), then the smallest parts (by art area, then layer, then order) stop casting.
    private void BuildCasterPlan()
    {
        var all = new List<int>(); for (int i = 0; i < drawables.Length; i++) if (Casts(i)) all.Add(i);
        var segs = new int[all.Count]; var area = new long[all.Count]; var layers = new int[all.Count];
        for (int k = 0; k < all.Count; k++) { var d = drawables[all[k]]; segs[k] = d.Segments; area[k] = (long)d.Size.x * d.Size.y; layers[k] = d.Layer; }
        var keep = PlanCasters(segs, area, layers, MaxPaperCasters, out int stride, out int fullTriangles);
        CasterCoarsened = stride > 1; var kept = new List<int>(); for (int k = 0; k < all.Count; k++) if (keep[k]) kept.Add(all[k]);
        CastersDropped = all.Count - kept.Count;
        casterDrawable = kept.ToArray(); casterStride = new int[casterDrawable.Length]; CasterTriangles = 0;
        for (int k = 0; k < casterDrawable.Length; k++) { casterStride[k] = stride; CasterTriangles += Strips(drawables[casterDrawable[k]].Segments, stride) * 2; }
        if ((CasterCoarsened || CastersDropped > 0) && casterWarned.Add(appearance.Id.ToString()))
            Debug.LogWarning("Paper shadow of '" + appearance.Id + "' exceeds " + MaxPaperCasters + " caster triangles (" + fullTriangles + "): " + (CasterCoarsened ? "bent parts use coarser strips (x" + stride + ")" : "") + (CastersDropped > 0 ? ", " + CastersDropped + " smallest part(s) do not cast" : "") + ".");
    }

    // Pure plan: one entry per casting part (its strip count, art area, layer). Returns which parts keep casting; `stride` = strips of a bent part merged into one caster quad.
    // Order of reduction: coarsen bent parts (stride 2, 4, ...), then drop the smallest parts (largest area kept; ties: higher layer kept, then earlier order). Deterministic.
    public static bool[] PlanCasters(int[] segments, long[] area, int[] layer, int cap, out int stride, out int fullTriangles)
    {
        int n = segments.Length; var keep = new bool[n]; for (int i = 0; i < n; i++) keep[i] = true;
        int Total(int s) { int t = 0; for (int i = 0; i < n; i++) if (keep[i]) t += Strips(segments[i], s) * 2; return t; }
        fullTriangles = Total(1); stride = 1; int maxSeg = 1; for (int i = 0; i < n; i++) maxSeg = Mathf.Max(maxSeg, segments[i]);
        while (Total(stride) > cap && stride < maxSeg) stride *= 2;
        if (Total(stride) > cap)
        {
            var order = new List<int>(); for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((x, y) => { int c = area[y].CompareTo(area[x]); if (c != 0) return c; c = layer[y].CompareTo(layer[x]); return c != 0 ? c : x.CompareTo(y); });   // best first
            for (int k = order.Count - 1; k >= 0 && Total(stride) > cap; k--) keep[order[k]] = false;
        }
        return keep;
    }

    private void AddCaster(ref int count, int a, int b, int c, int layer)
    {
        casterA[count]=new Vector4(vertices[a].x,vertices[a].y,uv0[a].x,uv0[a].y);
        casterB[count]=new Vector4(vertices[b].x,vertices[b].y,uv0[b].x,uv0[b].y);
        casterC[count]=new Vector4(vertices[c].x,vertices[c].y,uv0[c].x,uv0[c].y);
        casterBounds[count]=uvBounds[a];casterMeta[count]=new Vector4(layer,colors[a].a/255f,0,0);count++;
    }

    private void WritePaperCasters()
    {
        int count=0;
        for(int k=0;k<casterDrawable.Length;k++)
        {
            ref var d=ref drawables[casterDrawable[k]]; int v=d.VertexStart;
            if(d.Segments==1){AddCaster(ref count,v,v+1,v+2,d.Layer);AddCaster(ref count,v,v+2,v+3,d.Layer);continue;}
            int stride=casterStride[k];
            for(int r0=0;r0<d.Segments;r0+=stride)
            {
                int r1=Mathf.Min(d.Segments,r0+stride),a0=v+2*r0,b0=a0+1,a1=v+2*r1,b1=a1+1;
                if(d.AxisX){AddCaster(ref count,b0,b1,a1,d.Layer);AddCaster(ref count,b0,a1,a0,d.Layer);}
                else{AddCaster(ref count,a1,b1,b0,d.Layer);AddCaster(ref count,a1,b0,a0,d.Layer);}
            }
        }
        meshRenderer.GetPropertyBlock(paperBlock);
        paperBlock.SetFloat("_PaperCasterCount",count);
        paperBlock.SetVectorArray("_PaperCasterA",casterA);paperBlock.SetVectorArray("_PaperCasterB",casterB);paperBlock.SetVectorArray("_PaperCasterC",casterC);
        paperBlock.SetVectorArray("_PaperCasterBounds",casterBounds);paperBlock.SetVectorArray("_PaperCasterMeta",casterMeta);
        meshRenderer.SetPropertyBlock(paperBlock);
    }

    private void OnDestroy()
    {
        PNVisualSystem.Unregister(this);
        if (mesh != null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); mesh = null; }
    }
}

// EN: One loop for every Visual actor: it advances each by its own entity clock and writes the meshes that are on screen. Hidden runner, like the camera-facing one.
// VI: Một vòng lặp cho mọi Visual actor; mesh nằm ngoài màn hình thì chỉ tính tư thế (để socket vẫn đúng), không ghi đỉnh.
public static class PNVisualSystem
{
    private static readonly List<PNVisualActor> actors = new List<PNVisualActor>();
    private static GameObject runner;
    public static int Count => actors.Count;
    public static IReadOnlyList<PNVisualActor> Actors => actors;
    public static void Register(PNVisualActor actor)
    {
        if (actor == null) throw new ArgumentNullException(nameof(actor));
        if (actors.Contains(actor)) return; actors.Add(actor);
        if (runner == null && Application.isPlaying) { runner = new GameObject("PNVisualRunner") { hideFlags = HideFlags.HideAndDontSave }; UnityEngine.Object.DontDestroyOnLoad(runner); runner.AddComponent<PNVisualRunner>(); }
    }
    public static void Unregister(PNVisualActor actor)
    {
        if (ReferenceEquals(actor, null)) return;            // not `actor == null`: while it is being destroyed that is already true and it would stay registered
        actors.Remove(actor);
        if (actors.Count == 0) PNVisualStringSystem.Release();
        if (actors.Count == 0 && runner != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(runner); else UnityEngine.Object.DestroyImmediate(runner); runner = null; }
    }
    // After an atlas rebuild: every actor re-attaches to the new set, in the same frame.
    public static void RebindAll()
    {
        for (int i = actors.Count - 1; i >= 0; i--)
        {
            if (i >= actors.Count) continue; var actor = actors[i];
            if (actor == null) { actors.RemoveAt(i); continue; }
            try { actor.Rebind(); } catch (Exception error) { Debug.LogException(error); }
        }
    }
    // Drops entries whose object was destroyed without OnDestroy reaching Unregister (a safety net, and for tests after an aborted run).
    public static int Prune() { int before = actors.Count; actors.RemoveAll(x => x == null); return before - actors.Count; }
    public static void Tick()
    {
        // One view-projection per frame; each actor tests its own bounds (puppet + strings) against the view plus a margin, with hysteresis.
        // No camera (nothing to test against): fall back to the renderer's own visibility flag.
        var camera = PNCameraFacing.ResolveCamera(null, Application.isPlaying); bool haveView = camera != null;
        Matrix4x4 viewProjection = haveView ? camera.projectionMatrix * camera.worldToCameraMatrix : default;
        for (int i = actors.Count - 1; i >= 0; i--)
        {
            if (i >= actors.Count) continue; var actor = actors[i];
            if (actor == null) { actors.RemoveAt(i); continue; }
            try { actor.Tick(actor.ClockDelta(), haveView ? actor.UpdateInView(viewProjection) : actor.Visible); }
            catch (Exception error) { Debug.LogException(error); }
        }
        try { PNVisualStringSystem.Draw(); } catch (Exception error) { Debug.LogException(error); }      // every string of every puppet in view: one mesh, one draw
    }
    // True when the box intersects the view enlarged by `margin` (a fraction of the view on each side). Clip-space outcode test: false only when all 8 corners are outside one plane.
    public static bool Overlaps(in Matrix4x4 vp, in Bounds b, float margin)
    {
        float limit = 1f + 2f * margin; var c = b.center; var e = b.extents;
        int outL = 0, outR = 0, outB = 0, outT = 0, outN = 0, outF = 0;
        for (int k = 0; k < 8; k++)
        {
            float x = c.x + ((k & 1) == 0 ? -e.x : e.x), y = c.y + ((k & 2) == 0 ? -e.y : e.y), z = c.z + ((k & 4) == 0 ? -e.z : e.z);
            float cx = vp.m00 * x + vp.m01 * y + vp.m02 * z + vp.m03, cy = vp.m10 * x + vp.m11 * y + vp.m12 * z + vp.m13;
            float cz = vp.m20 * x + vp.m21 * y + vp.m22 * z + vp.m23, cw = vp.m30 * x + vp.m31 * y + vp.m32 * z + vp.m33;
            if (cx < -limit * cw) outL++; if (cx > limit * cw) outR++; if (cy < -limit * cw) outB++; if (cy > limit * cw) outT++;
            if (cz < -cw) outN++; if (cz > cw) outF++;
        }
        return outL < 8 && outR < 8 && outB < 8 && outT < 8 && outN < 8 && outF < 8;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void Reset() { actors.Clear(); runner = null; }
    private sealed class PNVisualRunner : MonoBehaviour { private void Update() => PNVisualSystem.Tick(); }
}
#endif
