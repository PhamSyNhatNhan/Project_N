#if UNITY_5_3_OR_NEWER
using UnityEngine;

// The soft glow behind a Buff icon: the icon's own alpha blurred, baked once per icon into a white texture (padded by a quarter of the icon
// on each side so the glow is not cut) and tinted per Buff by the card with the colour of the definition's `glow`. PNIconBake holds the maths.
internal static class PNBuffGlowArt
{
    internal const float Padding = .25f;      // glow margin as a fraction of the icon size, on each side
    private const float Strength = 1.7f;

    // Null when the sprite is missing. The caller owns the texture.
    internal static Texture2D Build(Sprite sprite)
    {
        if (sprite == null || sprite.texture == null) return null;
        var rect = sprite.rect; int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
        if (w < 4 || h < 4) return null;
        var source = sprite.texture;
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active; Color32[] pixels;
        try
        {
            var scale = new Vector2(rect.width / source.width, rect.height / source.height);
            var offset = new Vector2(rect.x / source.width, rect.y / source.height);
            RenderTexture.active = rt; GL.Clear(false, true, Color.clear);
            Graphics.Blit(source, rt, scale, offset);
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0); read.Apply(false);
            pixels = read.GetPixels32();
            if (Application.isPlaying) Object.Destroy(read); else Object.DestroyImmediate(read);
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
        int padX = Mathf.RoundToInt(w * Padding), padY = Mathf.RoundToInt(h * Padding), width = w + 2 * padX, height = h + 2 * padY;
        var plane = new float[width * height];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) plane[(y + padY) * width + x + padX] = pixels[y * w + x].a / 255f;
        var blurred = PNIconBake.Blur(plane, width, height, Mathf.Max(1, w / 12));
        var colours = new Color32[blurred.Length];
        for (int i = 0; i < colours.Length; i++) colours[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(blurred[i] * Strength) * 255f));
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Buff glow " + sprite.name, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        texture.SetPixels32(colours); texture.Apply(false, false);
        return texture;
    }

    // The tint for a definition's glow string; white for empty or (never after validation) unreadable text.
    internal static Color Tint(string glow)
    {
        if (!PNBuffGlow.TryParse(glow, out byte r, out byte g, out byte b, out byte a)) return Color.white;
        return new Color32(r, g, b, a);
    }
}
#endif
