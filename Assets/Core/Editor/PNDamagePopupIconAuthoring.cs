#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;

public static class PNDamagePopupIconAuthoring
{
    private const string Folder = "Assets/Resources/PNUI";
    public static string Apply()
    {
        PNDamagePopupAuthoring.CreateDefaults();
        var icons = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(Folder + "/PNDamagePopupIcons.asset");
        if (icons == null)
        {
            const int size = 64;
            WriteAtlas();
            icons = ScriptableObject.CreateInstance<TMP_SpriteAsset>(); icons.name = "PNDamagePopupIcons";
            var serializedIcons = new SerializedObject(icons); serializedIcons.FindProperty("m_Version").stringValue = "1.1.0";
            serializedIcons.ApplyModifiedPropertiesWithoutUndo();
            icons.spriteSheet = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/PNDamagePopupIcons.png");
            icons.faceInfo = new FaceInfo { pointSize = 64, scale = 1, ascentLine = 56, descentLine = -8, lineHeight = 64 };
            string[] names = { "crit", "fire", "ice", "lightning", "element" };
            for (int i = 0; i < names.Length; i++)
            {
                var glyph = new TMP_SpriteGlyph((uint)i, new GlyphMetrics(64, 64, 0, 56, 68), new GlyphRect(i * size, 0, size, size), 1, 0);
                icons.spriteGlyphTable.Add(glyph);
                icons.spriteCharacterTable.Add(new TMP_SpriteCharacter(0xE000u + (uint)i, icons, glyph) { name = names[i] });
            }
            var shader = Shader.Find("ProjectN/DamagePopupSprite");
            if (shader == null) throw new System.InvalidOperationException("Popup sprite shader did not compile.");
            var material = new Material(shader) { name = "PNDamagePopupIconsHDR" }; material.mainTexture = icons.spriteSheet;
            AssetDatabase.CreateAsset(material, Folder + "/PNDamagePopupIcons.mat"); icons.material = material;
            icons.UpdateLookupTables(); AssetDatabase.CreateAsset(icons, Folder + "/PNDamagePopupIcons.asset");
        }
        var root = PrefabUtility.LoadPrefabContents(Folder + "/PNDamagePopup.prefab");
        try
        {
            root.transform.localScale = Vector3.one * .4f;
            var popup = root.GetComponent<PNDamagePopup>(); popup.Icons = icons;
            popup.CriticalScaleMultiplier = 1.125f; popup.FadeStart = .65f;
            root.GetComponent<TextMeshPro>().spriteAsset = icons;
            root.GetComponent<TextMeshPro>().textWrappingMode = TextWrappingModes.NoWrap;
            PrefabUtility.SaveAsPrefabAsset(root, Folder + "/PNDamagePopup.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        return "Popup scale .4 / critical .45; TMP outline icons and HDR sprite material authored.";
    }
    public static string ApplyOutlineStyle()
    {
        WriteAtlas(); // Replace pixels in place, preserving texture/Sprite Asset/material GUIDs.
        var root = PrefabUtility.LoadPrefabContents(Folder + "/PNDamagePopup.prefab");
        try
        {
            root.GetComponent<PNDamagePopup>().GlowIntensity = 7f;
            PrefabUtility.SaveAsPrefabAsset(root, Folder + "/PNDamagePopup.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        return "Four-point outlined crit, outlined elements, normal HDR7 / critical HDR7.";
    }
    private static void WriteAtlas()
    {
        const int size = 64;
        var atlas = new Texture2D(size * 5, size, TextureFormat.RGBA32, false);
        for (int icon = 0; icon < 5; icon++) for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float coverage = 0f;
            for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++)
            {
                var p = new Vector2((x + (sx + .5f) / 4f) / size * 2f - 1f, (y + (sy + .5f) / 4f) / size * 2f - 1f);
                if (Inside(icon, p)) coverage += 1f / 16f;
            }
            atlas.SetPixel(icon * size + x, y, new Color(1f, 1f, 1f, coverage));
        }
        atlas.Apply(); File.WriteAllBytes(Folder + "/PNDamagePopupIcons.png", atlas.EncodeToPNG());
        Object.DestroyImmediate(atlas); AssetDatabase.ImportAsset(Folder + "/PNDamagePopupIcons.png");
        var importer = (TextureImporter)AssetImporter.GetAtPath(Folder + "/PNDamagePopupIcons.png");
        importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp; importer.SaveAndReimport();
    }
    private static bool Inside(int icon, Vector2 p)
    {
        if (icon == 0)
            return Outline(p, new[] {new Vector2(0,.85f),new Vector2(.17f,.17f),new Vector2(.85f,0),new Vector2(.17f,-.17f),
                new Vector2(0,-.85f),new Vector2(-.17f,-.17f),new Vector2(-.85f,0),new Vector2(-.17f,.17f)}, .045f);
        if (icon == 1) return Outline(p, new[] { new Vector2(0,.85f),new Vector2(.16f,.25f),new Vector2(.36f,.55f),new Vector2(.57f,0),
            new Vector2(.52f,-.35f),new Vector2(.3f,-.65f),new Vector2(-.25f,-.68f),new Vector2(-.54f,-.3f),
            new Vector2(-.4f,.15f),new Vector2(-.15f,.48f),new Vector2(-.17f,.1f) }, .045f);
        if (icon == 2)
        {
            for (int arm = 0; arm < 6; arm++)
            {
                float angle = arm * Mathf.PI / 3f; var d = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)); var side = new Vector2(-d.y, d.x);
                if (Segment(p, Vector2.zero, d * .78f) < .045f || Segment(p,d * .42f,d * .63f + side * .2f) < .035f ||
                    Segment(p,d * .42f,d * .63f - side * .2f) < .035f) return true;
            }
            return false;
        }
        if (icon == 3) return Outline(p,new[] { new Vector2(.1f,.9f),new Vector2(-.6f,-.1f),new Vector2(-.13f,-.1f),
            new Vector2(-.3f,-.9f),new Vector2(.6f,.22f),new Vector2(.15f,.22f) }, .045f);
        float diamond = Mathf.Abs(p.x) + Mathf.Abs(p.y); return diamond > .48f && diamond < .72f;
    }
    private static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        var delta = b - a; return (p - a - delta * Mathf.Clamp01(Vector2.Dot(p - a, delta) / delta.sqrMagnitude)).magnitude;
    }
    private static bool Outline(Vector2 p, Vector2[] points, float width)
    {
        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            if (Segment(p, points[j], points[i]) < width) return true;
        return false;
    }
}
#endif
