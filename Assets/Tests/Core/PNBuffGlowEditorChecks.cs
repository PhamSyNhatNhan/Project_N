#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Edit-mode checks of the Buff glow bake (PNBuffGlowArt) on the real PNBurn icon, plus a composited preview of the card as the UI draws it
// (glow tinted, 30 px icon on top) at real size and magnified. The preview shows the baked pixels, not a UI Toolkit panel frame.
public static class PNBuffGlowEditorChecks
{
    private const string Icon = "PNBuffIcons/PNBurn";
    private static readonly string Folder = "G:/ProjectN_Camera/BuffGlow";

    public static string Run()
    {
        var log = new List<string>(); int fails = 0;
        void Check(bool ok, string text) { log.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) fails++; }

        var sprite = Resources.Load<Sprite>(Icon);
        Check(sprite != null, "PNBurn loads as a sprite from Resources");
        if (sprite == null) return Finish(log, fails);
        Check(Mathf.RoundToInt(sprite.rect.width) == 120 && Mathf.RoundToInt(sprite.rect.height) == 120, "the source icon is 120 x 120");
        var glow = PNBuffGlowArt.Build(sprite);
        Check(glow != null, "the glow bakes from the icon");
        if (glow == null) return Finish(log, fails);
        try
        {
            Check(glow.width == 180 && glow.height == 180, "glow texture is the icon plus a quarter on each side (180 px), got " + glow.width + "x" + glow.height);
            var px = glow.GetPixels32();
            Check(px[0].a == 0 && px[glow.width - 1].a == 0 && px[px.Length - 1].a == 0, "the corners are clear (the glow fades out inside the margin)");
            Check(px[90 * 180 + 90].a > 200, "the glow is strong at the icon's centre, alpha " + px[90 * 180 + 90].a);
            int edgeMax = 0; for (int i = 0; i < 180; i++) edgeMax = Mathf.Max(edgeMax, px[i].a, px[(179) * 180 + i].a, px[i * 180].a, px[i * 180 + 179].a);
            Check(edgeMax < 40, "the texture border is nearly clear so the glow is not cut, max alpha " + edgeMax);
            Check(px[90 * 180 + 90].r == 255 && px[90 * 180 + 90].g == 255 && px[90 * 180 + 90].b == 255, "the baked glow is white (the tint is applied per card)");
            Check(PNBuffGlowArt.Tint("red") == (Color)new Color32(255, 64, 48, 255) && PNBuffGlowArt.Tint("") == Color.white && PNBuffGlowArt.Tint("#zz") == Color.white, "Tint: red preset, empty and unreadable fall back to white");
            Directory.CreateDirectory(Folder);
            Preview(sprite, glow, new[] { "red", "aqua", "", "#ffcc33" }, 1, Path.Combine(Folder, "real.png"));
            Preview(sprite, glow, new[] { "red", "aqua", "", "#ffcc33" }, 6, Path.Combine(Folder, "zoom.png"));
            log.Add("preview written to " + Folder);
            // Every stock icon with the glow its registration gives it.
            var stock = new[] { ("PNBurn", "red"), ("PNFrostbite", "blue"), ("PNElectrocution", "gold"), ("PNFireVulnerability", "red"), ("PNIceVulnerability", "blue"), ("PNLightningVulnerability", "gold"), ("PNTimescaleReduction", PNTimeScaleBuffContent.Glow), ("PNTimescaleReductionArea", PNTimeScaleBuffContent.Glow) };
            var sprites = new List<Sprite>(); var baked = new List<Texture2D>(); var tints = new List<string>();
            try
            {
                foreach (var (name, tint) in stock)
                {
                    var s = Resources.Load<Sprite>("PNBuffIcons/" + name); Check(s != null && Mathf.RoundToInt(s.rect.width) == 120 && Mathf.RoundToInt(s.rect.height) == 120, name + " loads at 120 x 120");
                    if (s == null) continue;
                    var t = PNBuffGlowArt.Build(s); Check(t != null && t.width == 180, name + " bakes a 180 px glow");
                    if (t == null) continue;
                    sprites.Add(s); baked.Add(t); tints.Add(tint);
                }
                PreviewSet(sprites, baked, tints, 3, Path.Combine(Folder, "stock.png"));
            }
            finally { foreach (var t in baked) UnityEngine.Object.DestroyImmediate(t); }
        }
        finally { UnityEngine.Object.DestroyImmediate(glow); }
        return Finish(log, fails);
    }

    private static string Finish(List<string> log, int fails)
    {
        string text = string.Join("\n", log) + "\n" + (fails == 0 ? "ALL PASS " + log.FindAll(x => x.StartsWith("PASS")).Count : fails + " FAIL");
        try { Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "results.txt"), text); } catch (Exception) { }
        return text;
    }

    private static Color32[] Pixels(Sprite sprite)
    {
        var rt = RenderTexture.GetTemporary(120, 120, 0, RenderTextureFormat.ARGB32); var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = rt; GL.Clear(false, true, Color.clear); Graphics.Blit(sprite.texture, rt);
            var read = new Texture2D(120, 120, TextureFormat.RGBA32, false); read.ReadPixels(new Rect(0, 0, 120, 120), 0, 0); read.Apply(false);
            var result = read.GetPixels32(); UnityEngine.Object.DestroyImmediate(read); return result;
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
    }

    private static float Alpha(Color32[] p, int w, int h, float u, float v)
    {
        float x = u * w - .5f, y = v * h - .5f; int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y); float fx = x - x0, fy = y - y0; float sum = 0;
        for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++)
        {
            int xx = x0 + i, yy = y0 + j; float weight = (i == 0 ? 1 - fx : fx) * (j == 0 ? 1 - fy : fy);
            if (xx >= 0 && yy >= 0 && xx < w && yy < h) sum += p[yy * w + xx].a / 255f * weight;
        }
        return sum;
    }

    private static void PreviewSet(List<Sprite> sprites, List<Texture2D> glows, List<string> tints, int scale, string path)
    {
        int cell = 60 * scale, rows = 2, width = cell * sprites.Count, height = cell * rows;
        var bgs = new[] { new Color(.18f, .23f, .29f), new Color(.72f, .75f, .77f) };
        var output = new Texture2D(width, height, TextureFormat.RGBA32, false); var colours = new Color32[width * height];
        for (int col = 0; col < sprites.Count; col++)
        {
            var icon = Pixels(sprites[col]); var glowPixels = glows[col].GetPixels32(); var tint = PNBuffGlowArt.Tint(tints[col]);
            for (int row = 0; row < rows; row++) for (int y = 0; y < cell; y++) for (int x = 0; x < cell; x++)
            {
                float u = (x + .5f) / scale - 15f, v = (y + .5f) / scale - 15f;
                float g = Alpha(glowPixels, glows[col].width, glows[col].height, (u + 7.5f) / 45f, (v + 7.5f) / 45f) * tint.a, a = Alpha(icon, 120, 120, u / 30f, v / 30f);
                var c = Color.Lerp(bgs[row], new Color(tint.r, tint.g, tint.b), g); c = Color.Lerp(c, Color.white, a);
                colours[(row * cell + y) * width + col * cell + x] = c;
            }
        }
        output.SetPixels32(colours); output.Apply(false); File.WriteAllBytes(path, output.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(output);
    }

    // One row per background (the dark HUD slate, the light menu grey), one cell per tint; a card is 30 px, its glow 45 px.
    private static void Preview(Sprite sprite, Texture2D glow, string[] tints, int scale, string path)
    {
        var icon = Pixels(sprite); var glowPixels = glow.GetPixels32();
        int cell = 60 * scale, rows = 2, width = cell * tints.Length, height = cell * rows;
        var bgs = new[] { new Color(.18f, .23f, .29f), new Color(.72f, .75f, .77f) };
        var output = new Texture2D(width, height, TextureFormat.RGBA32, false); var colours = new Color32[width * height];
        for (int row = 0; row < rows; row++) for (int col = 0; col < tints.Length; col++)
        {
            var tint = PNBuffGlowArt.Tint(tints[col]);
            for (int y = 0; y < cell; y++) for (int x = 0; x < cell; x++)
            {
                float u = (x + .5f) / scale - 15f, v = (y + .5f) / scale - 15f;     // card pixels, origin at the card corner (y up)
                float g = Alpha(glowPixels, glow.width, glow.height, (u + 7.5f) / 45f, (v + 7.5f) / 45f) * tint.a;
                float a = Alpha(icon, 120, 120, u / 30f, v / 30f);
                var c = bgs[row]; c = Color.Lerp(c, new Color(tint.r, tint.g, tint.b), g); c = Color.Lerp(c, Color.white, a);
                colours[(row * cell + y) * width + col * cell + x] = c;
            }
        }
        output.SetPixels32(colours); output.Apply(false); File.WriteAllBytes(path, output.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(output);
    }
}
#endif
