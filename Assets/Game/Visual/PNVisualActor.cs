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
    private static PNVisualAtlas atlas; private static Material material; private static EveRegistryHub atlasHub;
    public static PNVisualAtlas AtlasFor(EveRegistryHub hub)
    {
        if (hub == null) throw new ArgumentNullException(nameof(hub));
        if (atlas != null && ReferenceEquals(atlasHub, hub)) return atlas;
        Release();
        var images = new List<KeyValuePair<string, string>>();
        foreach (var part in hub.Get<PNVisualPartDefinition>().Snapshot().Values)
        {
            images.Add(new KeyValuePair<string, string>(part.Id.Namespace, part.Image));
            foreach (var variant in part.Variants.Values) images.Add(new KeyValuePair<string, string>(part.Id.Namespace, variant));
        }
        foreach (var motion in hub.Get<PNVisualMotionDefinition>().Snapshot().Values)
            foreach (var frame in motion.Frames) images.Add(new KeyValuePair<string, string>(motion.Id.Namespace, frame));
        atlas = PNVisualAtlas.Build(images); atlasHub = hub;
        return atlas;
    }
    public static Material MaterialFor(PNVisualAtlas forAtlas)
    {
        if (material == null)
        {
            var shader = Resources.Load<Shader>("PNVisualActor"); if (shader == null) shader = Shader.Find("PN/VisualActor"); if (shader == null) throw new InvalidOperationException("Shader PN/VisualActor is missing.");
            material = new Material(shader) { name = "PN Visual Actor", hideFlags = HideFlags.HideAndDontSave, enableInstancing = false };
        }
        material.SetTexture("_MainTex", forAtlas.Texture);
        return material;
    }
    public static void Release()
    {
        if (atlas != null) { atlas.Dispose(); atlas = null; }
        if (material != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(material); else UnityEngine.Object.DestroyImmediate(material); material = null; }
        atlasHub = null;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void Reset() { atlas = null; material = null; atlasHub = null; }
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
    private Drawable[] drawables; private Mesh mesh; private Vector3[] vertices; private Vector2[] uv0, uv1; private Color32[] colors;
    private Color32[] tintColors; private MeshRenderer meshRenderer; private EveTimeScale clock; private PNCameraFacingTarget facing;
    private float flash, fade = 1f; private bool dirty = true; private float feetY;
    private readonly float[] rowX = new float[PNVisualLimits.MaxSegments + 1], rowY = new float[PNVisualLimits.MaxSegments + 1], rowNx = new float[PNVisualLimits.MaxSegments + 1], rowNy = new float[PNVisualLimits.MaxSegments + 1];

    public PNVisualAnimator Animator => animator;
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
    public void Bind(PNVisualAppearanceDefinition definition, EveRegistryHub hub, Vector3 feetLocal, EveId? buildOverride = null)
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
        var atlas = PNVisualRuntime.AtlasFor(hub);
        BuildDrawables(hub, atlas);
        tintColors = new Color32[6];
        for (int i = 0; i < tintColors.Length; i++) tintColors[i] = new Color32(255, 255, 255, 255);
        foreach (var pair in appearance.Colors) { int t = TintIndex(pair.Key); if (t > 0) tintColors[t] = ToColor32(pair.Value); }
        BuildMesh(atlas);
        feetY = feetLocal.y;
        var facingTarget = GetComponent<PNCameraFacingTarget>(); if (facingTarget == null) facingTarget = gameObject.AddComponent<PNCameraFacingTarget>();
        facingTarget.FootOffset = 0f; facingTarget.FootAnchor = feetLocal; facing = facingTarget;
        clock = GetComponentInParent<EveTimeScale>();
        PNVisualSystem.Register(this);
        animator.Evaluate(); WriteMesh();
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
        vertices = new Vector3[vertexCount]; uv0 = new Vector2[vertexCount]; uv1 = new Vector2[vertexCount]; colors = new Color32[vertexCount];
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
        mesh.vertices = vertices; mesh.triangles = tris; mesh.uv = uv0; mesh.colors32 = colors;
        mesh.bounds = new Bounds(new Vector3(0f, CapsuleHeight * .5f, 0f), new Vector3(CapsuleHeight * 2f, CapsuleHeight * 2.5f, .1f));   // fixed: no per-frame bounds work
        var filter = GetComponent<MeshFilter>(); if (filter == null) filter = gameObject.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
        meshRenderer = GetComponent<MeshRenderer>(); if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = PNVisualRuntime.MaterialFor(atlas);
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
        animator.Advance(dt); animator.Evaluate();
        if (writeMesh) WriteMesh();
    }
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
            Rect uv = d.Uv; Vector2Int size = d.Size;
            if (d.Slot >= 0 && d.Variants != null) { var name = a.SlotVariant[d.Slot]; if (name.Length > 0 && d.Variants.TryGetValue(name, out var variant)) { uv = variant.Key; size = variant.Value; } }
            float x0 = -d.Part.PivotX, y0 = -d.Part.PivotY, x1 = size.x - d.Part.PivotX, y1 = size.y - d.Part.PivotY;
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
                    float t = (float)k / d.Segments, lx = rowX[k] + rowNx[k] * lowAcross, ly = rowY[k] + rowNy[k] * lowAcross, hx = rowX[k] + rowNx[k] * highAcross, hy = rowY[k] + rowNy[k] * highAcross;
                    int row = v + 2 * k;
                    vertices[row] = new Vector3(ta * lx + tc * ly + ttx, tb * lx + td * ly + tty, 0f);
                    vertices[row + 1] = new Vector3(ta * hx + tc * hy + ttx, tb * hx + td * hy + tty, 0f);
                    if (d.AxisX) { float u = uv.x + t * uv.width; uv0[row] = new Vector2(u, uv.yMax); uv0[row + 1] = new Vector2(u, uv.y); }
                    else { float w = uv.yMax - t * uv.height; uv0[row] = new Vector2(uv.x, w); uv0[row + 1] = new Vector2(uv.xMax, w); }
                }
            }
            var tint = tintColors[(int)d.Tint]; float alpha = d.Slot >= 0 ? a.SlotAlpha[d.Slot] : 1f; if (d.Blush) alpha *= appearance.BlushAlpha;
            var color = new Color32(tint.r, tint.g, tint.b, (byte)Mathf.Clamp(Mathf.RoundToInt(tint.a * alpha), 0, 255));
            var effect = new Vector2(flash, fade);
            for (int k = 0; k < count; k++) { colors[v + k] = color; uv1[v + k] = effect; }
        }
        mesh.SetVertices(vertices); mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.SetColors(colors);
        dirty = false;
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
        if (actors.Count == 0 && runner != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(runner); else UnityEngine.Object.DestroyImmediate(runner); runner = null; }
    }
    // Drops entries whose object was destroyed without OnDestroy reaching Unregister (a safety net, and for tests after an aborted run).
    public static int Prune() { int before = actors.Count; actors.RemoveAll(x => x == null); return before - actors.Count; }
    public static void Tick()
    {
        for (int i = actors.Count - 1; i >= 0; i--)
        {
            if (i >= actors.Count) continue; var actor = actors[i];
            if (actor == null) { actors.RemoveAt(i); continue; }
            try { actor.Tick(actor.ClockDelta(), actor.Visible); }
            catch (Exception error) { Debug.LogException(error); }
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void Reset() { actors.Clear(); runner = null; }
    private sealed class PNVisualRunner : MonoBehaviour { private void Update() => PNVisualSystem.Tick(); }
}
#endif
