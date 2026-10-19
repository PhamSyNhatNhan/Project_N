#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Step 4.2 (lots A1+A2) checks: popup element selection/label parity against the original LINQ behavior,
// the native popup suite, the render capture and the Player builds. Writes only under .utmp/Step4b.
public static class PNStep4bBatchChecks
{
    private const string Folder = "F:/unity/Project_N/.utmp/Step4b/Native";
    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        string host = null;
        try
        {
            var scene = SceneManager.GetActiveScene(); bool dirty = scene.isDirty;
            if (Application.isBatchMode && string.IsNullOrEmpty(scene.path) && !dirty)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Tests/Generated")) AssetDatabase.CreateFolder("Assets/Tests", "Generated");
                host = "Assets/Tests/Generated/Step4bBatch_" + Guid.NewGuid().ToString("N") + ".unity";
                if (!EditorSceneManager.SaveScene(scene, host)) throw new Exception("Cannot save owned empty batch scene.");
            }
            EveHotPathMetrics.Enabled = EveHotPathMetrics.CountOnly = false;
            File.WriteAllText(Folder + "/Parity.txt", Parity());
            File.WriteAllText(Folder + "/Native.txt", PNDamagePopupEditorChecks.Run());
            File.WriteAllText(Folder + "/RenderAfter.txt", PNDamagePopupRenderChecks.Run());
            File.Copy("F:/unity/Project_N/.utmp/PNDamagePopupGlow.png", Folder + "/RenderAfter.png", true);
            if (scene != SceneManager.GetActiveScene() || scene.isDirty != dirty) throw new Exception("Checks changed authored scene state.");
            File.WriteAllText(Folder + "/BatchResult.txt", "PASS parity, native popup checks, render capture and requested builds");
            if (host != null) AssetDatabase.DeleteAsset(host);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            File.WriteAllText(Folder + "/BatchFailure.txt", e.ToString());
            if (host != null) AssetDatabase.DeleteAsset(host);
            if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
        }
    }

    private static string Parity()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var ids = new[]
        {
            PNContentIds.Fire, PNContentIds.Ice, PNContentIds.Lightning, EveId.Parse("mod:wind"), EveId.Parse("mod:a"),
            EveId.Parse("z:b"), EveId.Parse("mod:wind2"), EveId.Parse("project_n:physical")
        };
        var rng = new System.Random(20261013);
        // Selection helper against the original Distinct().OrderBy().ToArray().
        EveId[] previous = null;
        for (int n = 0; n < 3000; n++)
        {
            int length = rng.Next(0, 11);
            var source = new EveId[length];
            for (int i = 0; i < length; i++) source[i] = ids[rng.Next(ids.Length)];
            var expected = source.Distinct().OrderBy(id => id).ToArray();
            var scratch = PNDamagePopup.ElementScratch(); scratch.AddRange(source);
            var actual = PNDamagePopup.DistinctSorted(scratch);
            Check(actual.SequenceEqual(expected), "DistinctSorted differs from Distinct/OrderBy for input " + string.Join(",", source));
            if (previous != null && actual.Length != 0) Check(!ReferenceEquals(previous, actual), "DistinctSorted reused a result array.");
            if (actual.Length != 0) previous = actual;
        }
        // Large inputs (hash-set path) and bounded retained scratch capacity.
        var many = Enumerable.Range(0, 300).Select(i => EveId.Parse("mod:e" + i)).ToArray();
        for (int n = 0; n < 200; n++)
        {
            int length = n < 5 ? 17 + n : rng.Next(17, 1200);
            var source = new EveId[length];
            for (int i = 0; i < length; i++) source[i] = rng.Next(4) == 0 ? ids[rng.Next(ids.Length)] : many[rng.Next(many.Length)];
            var expected = source.Distinct().OrderBy(id => id).ToArray();
            var scratch = PNDamagePopup.ElementScratch(); scratch.AddRange(source);
            var actual = PNDamagePopup.DistinctSorted(scratch);
            Check(actual.SequenceEqual(expected), "Large DistinctSorted differs from Distinct/OrderBy.");
            Check(PNDamagePopup.ElementScratch().Capacity <= 256, "Large input left an oversized retained scratch list.");
        }
        // Begin label/element parity (duplicate styles, null styles, null Elements, unmapped elements, crit).
        var prefab = Resources.Load<GameObject>("PNUI/PNDamagePopup");
        if (prefab == null) throw new Exception("Resources prefab PNUI/PNDamagePopup not found.");
        var root = UnityEngine.Object.Instantiate(prefab);
        try
        {
            var popup = root.GetComponent<PNDamagePopup>();
            if (popup == null) throw new Exception("Prefab has no PNDamagePopup; components=" + string.Join(",", root.GetComponents<Component>().Select(c => c == null ? "<missing>" : c.GetType().Name)));
            var original = popup.Elements;
            var variants = new List<PNDamagePopup.ElementStyle[]>
            {
                original,
                new[]
                {
                    null,
                    new PNDamagePopup.ElementStyle { ElementId = "project_n:fire", SpriteName = "fire", Color = Color.red },
                    new PNDamagePopup.ElementStyle { ElementId = "project_n:fire", SpriteName = "ice", Color = Color.blue },
                    new PNDamagePopup.ElementStyle { ElementId = "mod:wind", SpriteName = "lightning", Color = Color.green }
                },
                null,
                Array.Empty<PNDamagePopup.ElementStyle>()
            };
            double[] damages = { 0d, 1.25d, 37.5d, 199999984d, 2147483648.75d };
            foreach (var styles in variants)
            {
                popup.Elements = styles;
                for (int n = 0; n < 200; n++)
                {
                    int length = rng.Next(0, 7);
                    var elements = new EveId[length];
                    for (int i = 0; i < length; i++) elements[i] = ids[rng.Next(ids.Length)];
                    double damage = damages[rng.Next(damages.Length)]; bool critical = rng.Next(2) == 1;
                    var tint = new Color(rng.Next(256) / 255f, rng.Next(256) / 255f, rng.Next(256) / 255f, 1f);
                    popup.Begin(damage, Vector3.zero, tint, null, critical, elements);
                    string expectedText = Reference(popup, damage, tint, critical, elements, out var expectedElements);
                    Check(popup.DisplayText == expectedText, "Begin label differs from the original builder.");
                    Check(popup.DisplayedElements.SequenceEqual(expectedElements), "Begin displayed elements differ from the original selection.");
                }
                popup.Begin(5d, Vector3.zero, Color.white, null, false, null);
                Check(popup.DisplayedElements.Length == 0 && popup.DisplayText == "5", "Null element list changed behavior.");
            }
            popup.Elements = original;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        return "PASS " + checks + " step4b parity checks: element selection (3000 small + 200 large seeded lists, bounded scratch) and popup label/elements (800 seeded Begin calls over 4 style tables).";
    }

    // Independent copy of the pre-4.2 Begin label/selection code.
    private static string Reference(PNDamagePopup popup, double damage, Color tint, bool critical, EveId[] elements, out EveId[] displayed)
    {
        displayed = (elements ?? Array.Empty<EveId>()).Distinct().OrderBy(id => id).ToArray();
        string Icon(string name, Color color) => "<sprite name=\"" + name + "\" color=#" + ColorUtility.ToHtmlStringRGBA(color) + ">";
        var icons = popup.Icons;
        var label = new StringBuilder();
        if (critical) label.Append(Icon("crit", tint)).Append(' ');
        label.Append(PNDamagePopup.FormatDamage(damage));
        foreach (var element in displayed)
        {
            var style = popup.Elements?.FirstOrDefault(s => s != null && s.ElementId == element.ToString());
            string spriteName = style?.SpriteName ?? "element";
            string sprite = Icon(spriteName, style?.Color ?? new Color(.75f, .6f, 1f));
            var glyph = icons.spriteCharacterTable[icons.GetSpriteIndexFromName(spriteName)].glyph;
            float centre = (glyph.metrics.horizontalBearingY - glyph.metrics.height * .5f) / icons.faceInfo.pointSize;
            float offset = (1f - popup.ElementIconScale) * centre / popup.ElementIconScale;
            label.Append(" <size=").Append((popup.ElementIconScale * 100f).ToString("0.###", CultureInfo.InvariantCulture))
                .Append("%><voffset=").Append(offset.ToString("0.#####", CultureInfo.InvariantCulture))
                .Append("em>").Append(sprite).Append("</voffset></size>");
            if (style == null) label.Append("<color=#BFAAFF>").Append(element.ToString()).Append("</color>");
        }
        return label.ToString();
    }
}
#endif
