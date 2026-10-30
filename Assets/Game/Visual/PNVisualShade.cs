#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;

// EN: Pre-baked overlap shadow (clone trial): the shadow the dynamic shader draws between the parts of a puppet, computed ONCE at the rest pose on the GPU into copies of the receiving
//     parts (the shade texture of one appearance + build). The actor then draws those copies instead of casting shadows every frame; the shadow does not follow the pose (bent cloaks, a mirrored actor).
// VI: Bóng chồng lớp nướng sẵn theo tư thế nghỉ vào bản sao của các mảnh bị che; không đổi theo cử động.
public sealed class PNVisualShadeSet : IDisposable
{
    public Texture2D Texture;
    public Rect[] Uv;                 // per drawable of the actor: the tile (including its border) in Texture, width 0 = this part is not shaded
    public int Tiles;
    public void Dispose() { if (Texture != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(Texture); else UnityEngine.Object.DestroyImmediate(Texture); Texture = null; } }
}

public static class PNVisualShade
{
    public const float ReferencePixelsPerMeter = 1080f / (2f * 2.04f);     // 264.7 screen pixels per metre: the reference game scale of the ink bake
    private static Material material;

    // Affine map between atlas uv and puppet-local metres, from three vertices of a quad (rest pose).
    public struct Affine
    {
        public float M00, M01, M10, M11, Tx, Ty;      // local = M * uv + T
        public static Affine From(Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 pA, Vector2 pB, Vector2 pC)
        {
            Vector2 e1 = uvB - uvA, e2 = uvC - uvA, f1 = pB - pA, f2 = pC - pA;
            float det = e1.x * e2.y - e1.y * e2.x; if (Mathf.Abs(det) < 1e-12f) return default;
            float i00 = e2.y / det, i01 = -e2.x / det, i10 = -e1.y / det, i11 = e1.x / det;           // inverse of [e1 e2]
            var a = new Affine { M00 = f1.x * i00 + f2.x * i10, M01 = f1.x * i01 + f2.x * i11, M10 = f1.y * i00 + f2.y * i10, M11 = f1.y * i01 + f2.y * i11 };
            a.Tx = pA.x - (a.M00 * uvA.x + a.M01 * uvA.y); a.Ty = pA.y - (a.M10 * uvA.x + a.M11 * uvA.y); return a;
        }
        public bool Valid => Mathf.Abs(M00 * M11 - M01 * M10) > 1e-14f;
        public Affine Inverse()
        {
            float det = M00 * M11 - M01 * M10; float i00 = M11 / det, i01 = -M01 / det, i10 = -M10 / det, i11 = M00 / det;
            return new Affine { M00 = i00, M01 = i01, M10 = i10, M11 = i11, Tx = -(i00 * Tx + i01 * Ty), Ty = -(i10 * Tx + i11 * Ty) };
        }
    }
    public sealed class Caster { public Affine UvToLocal; public Rect Bounds; }
    public sealed class Receiver { public int Drawable; public Rect Outer; public Affine UvToLocal; public List<Caster> Casters = new List<Caster>(); }

    // Bakes every receiver (a tile of the atlas texture, border included) and packs the results into one texture. `border` is the atlas tile border in pixels.
    public static PNVisualShadeSet Bake(Texture2D atlasTexture, int drawableCount, List<Receiver> receivers, float strength, Vector2 offsetPx, float softnessPx)
    {
        var set = new PNVisualShadeSet { Uv = new Rect[drawableCount] };
        if (receivers.Count == 0) return set;
        if (material == null)
        {
            var shader = Resources.Load<Shader>("PNShadeBake"); if (shader == null) shader = Shader.Find("Hidden/PN/VisualShadeBake");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Shader Hidden/PN/VisualShadeBake is missing or unsupported.");
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        float s = 1f / ReferencePixelsPerMeter;
        material.SetVector("_Offset", new Vector4(s * offsetPx.x, s * offsetPx.y, s * softnessPx, s * softnessPx)); material.SetFloat("_Strength", strength);
        var tiles = new List<Texture2D>(); var drawables = new List<int>(); var previous = RenderTexture.active;
        try
        {
            foreach (var r in receivers)
            {
                int w = Mathf.Max(1, Mathf.RoundToInt(r.Outer.width * atlasTexture.width)), h = Mathf.Max(1, Mathf.RoundToInt(r.Outer.height * atlasTexture.height));
                material.SetVector("_RecvOuter", new Vector4(r.Outer.xMin, r.Outer.yMin, r.Outer.xMax, r.Outer.yMax));
                material.SetVector("_RecvAx", new Vector4(r.UvToLocal.M00, r.UvToLocal.M01, r.UvToLocal.Tx, 0)); material.SetVector("_RecvAy", new Vector4(r.UvToLocal.M10, r.UvToLocal.M11, r.UvToLocal.Ty, 0));
                int n = Mathf.Min(8, r.Casters.Count); var bx = new Vector4[8]; var by = new Vector4[8]; var bounds = new Vector4[8];
                for (int k = 0; k < n; k++)
                {
                    var inv = r.Casters[k].UvToLocal.Inverse();     // local -> uv
                    bx[k] = new Vector4(inv.M00, inv.M01, inv.Tx, 0); by[k] = new Vector4(inv.M10, inv.M11, inv.Ty, 0);
                    var b = r.Casters[k].Bounds; bounds[k] = new Vector4(b.xMin, b.yMin, b.xMax, b.yMax);
                }
                material.SetFloat("_CasterCount", n); material.SetVectorArray("_CasterBx", bx); material.SetVectorArray("_CasterBy", by); material.SetVectorArray("_CasterBounds", bounds);
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                try
                {
                    Graphics.Blit(atlasTexture, rt, material, 0); RenderTexture.active = rt;
                    var tile = new Texture2D(w, h, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    tile.ReadPixels(new Rect(0, 0, w, h), 0, 0); tile.Apply(false); tiles.Add(tile); drawables.Add(r.Drawable);
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
            }
            var packedTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var rects = packedTexture.PackTextures(tiles.ToArray(), PNVisualAtlas.Padding, PNVisualAtlas.MaxSize, false);
            if (rects == null || rects.Length != tiles.Count) { UnityEngine.Object.DestroyImmediate(packedTexture); throw new InvalidOperationException("Shade tiles do not fit one texture."); }
            for (int i = 0; i < rects.Length; i++) set.Uv[drawables[i]] = rects[i];
            set.Texture = packedTexture; set.Tiles = rects.Length;
            return set;
        }
        finally { RenderTexture.active = previous; foreach (var t in tiles) UnityEngine.Object.DestroyImmediate(t); }
    }
    public static void Release() { if (material != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(material); else UnityEngine.Object.DestroyImmediate(material); material = null; } }
}
#endif
