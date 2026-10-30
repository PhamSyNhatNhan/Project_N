#if UNITY_5_3_OR_NEWER
using UnityEngine;

// EN: Bakes the marks of the ink look that are fixed to an image (grain, wash, dry ink, hatching, ghost trace, broken contour, cut-edge ink) into a copy of that image, once, when the
//     visual atlas is built (clone trial). The copy is larger by `Border` transparent pixels on every side so the marks can leave the silhouette. Runs on the GPU with Hidden/PN/VisualInkBake and
//     reads the result back, so a mod's PNG (the atlas is packed at load, never in the Editor) gets the same look. The same call serves other 2D sprites (world art) later.
// VI: Nướng các nét cố định theo ảnh (hạt, loang, mực khô, gạch xiên, viền) vào bản sao của ảnh, một lần khi dựng atlas. Bản sao lớn hơn `Border` pixel trong suốt mỗi phía.
public static class PNInkBake
{
    public const int Border = 16;                    // = PNVisualActor's geometry gutter: the actor already reads this much outside the source rect
    public const float ReferenceScale = 6.17f;        // art pixels per screen pixel at the reference game scale (1630 px/m art, about 264 px/m on a 1080p screen)
    private static Material material;

    public struct Preset
    {
        public float Paper, PaperScale, Outline, Sketch, Drawn, DryInk, Hatch, HatchCoverage, Pigment, Wobble, Loose, DryFiber;
        public Color Ink;
        // The "Strong Ink" values the user chose (clone README).
        public static Preset Strong => new Preset { Paper = .025f, PaperScale = 3, Outline = .55f, Sketch = .12f, Drawn = 1, DryInk = .9f, Hatch = .95f, HatchCoverage = 1, Pigment = .30f, Wobble = .7f, Loose = .32f, DryFiber = .12f, Ink = new Color(.16f, .12f, .20f, 1) };
    }
    // Tuned by eye against the dynamic shader at game scale (G:\ProjectN_Camera\VisualGpu\Bake\Sweep): the dynamic hatching reads crisper than the analytic softness suggests (and the atlas has no mips), so
    // the baked strokes use a softness of .75 and hatch strength .8 (hat-interior luminance spread 8.8 vs 8.8 of the dynamic Strong shader; see HANDOFF).
    public static Preset Current = WithHatch(Preset.Strong, .8f);
    public static float HatchSoftness = .75f;         // multiplies the stroke softness of the hatching (1 = one screen pixel at the reference scale)
    private static Preset WithHatch(Preset p, float hatch) { p.Hatch = hatch; return p; }

    // Returns a new readable texture of (w + 2 Border) x (h + 2 Border). The source is not modified or destroyed.
    public static Texture2D Bake(Texture2D source, int border = Border)
    {
        if (source == null) throw new System.ArgumentNullException(nameof(source));
        if (material == null)
        {
            var shader = Resources.Load<Shader>("PNInkBake"); if (shader == null) shader = Shader.Find("Hidden/PN/VisualInkBake");
            if (shader == null || !shader.isSupported) throw new System.InvalidOperationException("Shader Hidden/PN/VisualInkBake is missing or unsupported.");
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        int w = source.width, h = source.height, W = w + 2 * border, H = h + 2 * border;
        var pixels = source.GetPixels32(); var padded = new Color32[W * H];     // transparent border
        for (int y = 0; y < h; y++) System.Array.Copy(pixels, y * w, padded, (y + border) * W + border, w);
        var enlarged = new Texture2D(W, H, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        enlarged.SetPixels32(padded); enlarged.Apply(false);
        var p = Current;
        material.SetFloat("_Scale", ReferenceScale); material.SetFloat("_AaScale", HatchSoftness); material.SetFloat("_PaperStrength", p.Paper); material.SetFloat("_PaperScale", p.PaperScale); material.SetFloat("_OutlineWidth", p.Outline);
        material.SetFloat("_SketchStrength", p.Sketch); material.SetFloat("_DrawnAmount", p.Drawn); material.SetFloat("_DryInk", p.DryInk); material.SetFloat("_HatchStrength", p.Hatch);
        material.SetFloat("_HatchCoverage", p.HatchCoverage); material.SetFloat("_PigmentStrength", p.Pigment); material.SetFloat("_LineWobble", p.Wobble);
        material.SetFloat("_LooseOutline", p.Loose); material.SetFloat("_DryFiber", p.DryFiber); material.SetColor("_OutlineColor", p.Ink);
        var rt = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(enlarged, rt, material, 0);
            RenderTexture.active = rt;
            var baked = new Texture2D(W, H, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            baked.ReadPixels(new Rect(0, 0, W, H), 0, 0); baked.Apply(false);
            return baked;
        }
        finally
        {
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt);
            if (Application.isPlaying) Object.Destroy(enlarged); else Object.DestroyImmediate(enlarged);
        }
    }
    // The raw (live ink) atlas uses the same geometry as the baked one: a copy of the source with `border` transparent pixels on every side, no marks. The source is not modified.
    public static Texture2D Pad(Texture2D source, int border = Border)
    {
        int w = source.width, h = source.height, W = w + 2 * border, H = h + 2 * border;
        var pixels = source.GetPixels32(); var padded = new Color32[W * H];
        for (int y = 0; y < h; y++) System.Array.Copy(pixels, y * w, padded, (y + border) * W + border, w);
        var enlarged = new Texture2D(W, H, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        enlarged.SetPixels32(padded); enlarged.Apply(false); return enlarged;
    }
    // The live shader (PN/VisualActor without PN_BAKED) computes the same marks per pixel: this sets its parameters to the Strong preset so a live actor looks like a baked one. The shader's own defaults are NOT the preset.
    public static void ApplyLive(Material m)
    {
        var p = Current;
        m.SetFloat("_PaperStrength", p.Paper); m.SetFloat("_OutlineWidth", p.Outline); m.SetFloat("_ShadowStrength", 0); m.SetFloat("_DrawnAmount", p.Drawn); m.SetFloat("_DryInk", p.DryInk);
        m.SetFloat("_HatchStrength", p.Hatch); m.SetFloat("_HatchCoverage", p.HatchCoverage); m.SetFloat("_PigmentStrength", p.Pigment); m.SetFloat("_LineWobble", p.Wobble);
        m.SetFloat("_LooseOutline", p.Loose); m.SetFloat("_DryFiber", p.DryFiber);
        m.SetVector("_OverlapOffset", new Vector4(2, -3.6f, 0, 0)); m.SetFloat("_OverlapSoftness", .8f);
    }
    public static void Release() { if (material != null) { if (Application.isPlaying) Object.Destroy(material); else Object.DestroyImmediate(material); material = null; } }
}
#endif
