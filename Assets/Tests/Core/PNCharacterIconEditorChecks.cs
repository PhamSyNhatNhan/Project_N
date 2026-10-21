#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

// Native checks for the character-button icon: the layers load from Resources, bake into white lines on transparent with a halo, and the lobby
// icon carries more ink than the hall one (the moon). When `previewDirectory` is given, both bakes are written there on the pause-surface grey.
public static class PNCharacterIconEditorChecks
{
    public static string Run() => Run(null);
    public static string Run(string previewDirectory)
    {
        int checks = 0; void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        long[] ink = new long[2];
        for (int mode = 0; mode < 2; mode++)
        {
            bool lobby = mode == 1;
            var texture = PNCharacterIconArt.Build(lobby);
            Check(texture != null && texture.width == 512 && texture.height == 512, "the icon bakes to a 512 px texture");
            var pixels = texture.GetPixels32(); bool white = true; int opaque = 0, clear = 0;
            foreach (var p in pixels) { if (p.a > 0 && (p.r != 255 || p.g != 255 || p.b != 255)) white = false; if (p.a > 200) opaque++; if (p.a == 0) clear++; ink[mode] += p.a; }
            Check(white, "every visible pixel is white");
            Check(opaque > 500 && clear > 1000, "the icon has ink and a clear margin (opaque " + opaque + ", clear " + clear + " of " + pixels.Length + ")");
            if (!string.IsNullOrEmpty(previewDirectory))
            {
                Directory.CreateDirectory(previewDirectory);
                var flat = new Texture2D(512, 512, TextureFormat.RGBA32, false); var grey = new Color32[pixels.Length];
                for (int i = 0; i < pixels.Length; i++) { float a = pixels[i].a / 255f; grey[i] = new Color32((byte)(184 + 71 * a), (byte)(192 + 63 * a), (byte)(196 + 59 * a), 255); }
                flat.SetPixels32(grey); flat.Apply(); File.WriteAllBytes(Path.Combine(previewDirectory, lobby ? "lobby.png" : "hall.png"), flat.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(flat);
            }
            UnityEngine.Object.DestroyImmediate(texture);
        }
        Check(ink[1] > ink[0], "the lobby icon (with the moon) has more ink than the hall icon");
        return "PNCharacterIconEditorChecks PASS (" + checks + " checks)";
    }
}
#endif
