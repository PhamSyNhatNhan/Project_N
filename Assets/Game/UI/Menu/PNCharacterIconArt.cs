#if UNITY_5_3_OR_NEWER
using UnityEngine;

// The character-button icon, built once from the artist's layers in Resources/UI/Icons/Charator_Icon (2048 px, black lines): head, eyes, two stars
// and, for the lobby, the moon with its star. They are cropped to the drawing, turned into white lines on transparent, thickened by 0.2 px at
// 56 px, given the soft halo of the pause star and baked into one 512 px texture (PNIconBake holds the pixel maths).
internal static class PNCharacterIconArt
{
    private const string Folder = "UI/Icons/Charator_Icon/";
    private const int Work = 1024, Size = 512;
    private static readonly Rect Crop = new Rect(250f / 2048, (2048f - 148 - 1600) / 2048, 1600f / 2048, 1600f / 2048); // the ink spans x 316-1784, y 252-1644; a margin keeps the halo whole

    // The layers that make the icon; the moon is the lobby mark.
    internal static string[] Layers(bool lobby)
    {
        return lobby ? new[] { "UI_Icon_Charator", "UI_Icon_Charator_Eye", "UI_Icon_Charator_Star", "UI_Icon_Charator_Moon_Star" }
                     : new[] { "UI_Icon_Charator", "UI_Icon_Charator_Eye", "UI_Icon_Charator_Star" };
    }

    // Null when a layer is missing (the caller then shows a plain button).
    internal static Texture2D Build(bool lobby)
    {
        var rt = RenderTexture.GetTemporary(Work, Work, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active; float[] sum = null;
        try
        {
            foreach (var name in Layers(lobby))
            {
                var source = Resources.Load<Texture2D>(Folder + name);
                if (source == null) { Debug.LogWarning("PNCharacterIconArt: missing " + Folder + name); return null; }
                Graphics.Blit(source, rt, Crop.size, Crop.position);
                RenderTexture.active = rt;
                var read = new Texture2D(Work, Work, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, Work, Work), 0, 0); read.Apply(false);
                var pixels = read.GetPixels32();
                if (Application.isPlaying) Object.Destroy(read); else Object.DestroyImmediate(read);
                var plane = new float[pixels.Length];
                for (int i = 0; i < pixels.Length; i++) plane[i] = PNIconBake.LineAlpha(pixels[i].r / 255f, pixels[i].g / 255f, pixels[i].b / 255f, pixels[i].a / 255f);
                if (sum == null) sum = plane; else PNIconBake.Union(sum, plane);
            }
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
        if (sum == null) return null;
        var lines = PNIconBake.Dilate(PNIconBake.Downsample2(sum, Work, Work), Size, Size, 1);
        var alpha = PNIconBake.WithHalo(lines, PNIconBake.Blur(lines, Size, Size, 10), .4f);
        var colours = new Color32[alpha.Length];
        for (int i = 0; i < colours.Length; i++) colours[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha[i]) * 255f));
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = lobby ? "Character icon (lobby)" : "Character icon", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        texture.SetPixels32(colours); texture.Apply(false, false);
        return texture;
    }
}
#endif
