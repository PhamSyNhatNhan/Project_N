#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EveEngine;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TextMeshPro))]
public sealed class PNDamagePopup : MonoBehaviour
{
    [Serializable]
    public sealed class ElementStyle
    {
        public string ElementId;
        public string SpriteName;
        public Color Color = Color.white;
    }
    [Min(.01f)] public float Lifetime = .55f;
    [Min(0f)] public float Rise = .45f;
    [Min(0f)] public float GlowIntensity = 7f;
    [Min(0f)] public float CriticalGlowIntensity = 7f;
    [Min(.01f)] public float CriticalScaleMultiplier = 1.125f;
    [Range(0f, .99f)] public float FadeStart = .65f;
    public TMP_SpriteAsset Icons;
    [Range(.1f, 1f)] public float ElementIconScale = .8f;
    public ElementStyle[] Elements = {
        new ElementStyle { ElementId = "project_n:fire", SpriteName = "fire", Color = new Color(1f,.3f,.08f) },
        new ElementStyle { ElementId = "project_n:ice", SpriteName = "ice", Color = new Color(.3f,.8f,1f) },
        new ElementStyle { ElementId = "project_n:lightning", SpriteName = "lightning", Color = new Color(1f,.85f,.15f) }
    };
    public AnimationCurve Scale = new AnimationCurve(new Keyframe(0f, 1.2f), new Keyframe(.2f, 1f), new Keyframe(1f, .85f));
    private TextMeshPro text;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock block;
    private Vector3 origin, baseScale;
    private Color tint;
    private float elapsed;
    private EveTimeScale clock;
    private bool critical;
    private MeshRenderer[] renderers;
    public bool IsCritical => critical;
    public EveId[] DisplayedElements { get; private set; } = Array.Empty<EveId>();
    public string DisplayText => text == null ? string.Empty : text.text;
    public float Progress => Mathf.Clamp01(elapsed / Lifetime);
    public static string FormatDamage(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        // Round like the original popup, without the Int32 limit or Mono's 15-digit formatter.
        return new System.Numerics.BigInteger(Math.Round(value, 0, MidpointRounding.ToEven)).ToString(CultureInfo.InvariantCulture);
    }
    private const int LinearDistinctLimit = 16, MaximumRetainedScratch = 256;
    [ThreadStatic] private static List<EveId> elementScratch;
    // Reusable per-thread list for building an element selection; callers hand it to DistinctSorted before any re-entry.
    internal static List<EveId> ElementScratch()
    {
        var list = elementScratch ?? (elementScratch = new List<EveId>(8));
        list.Clear();
        return list;
    }
    // Same result as Distinct().OrderBy(id => id).ToArray(): first occurrence kept, ascending EveId order, a fresh array per call.
    // Small inputs use a linear scan + insertion sort; larger ones a hash set + List.Sort (equal ids are the only CompareTo ties,
    // and Distinct has already removed them, so the unstable sort cannot change the result). Retained scratch capacity is bounded.
    internal static EveId[] DistinctSorted(List<EveId> values)
    {
        int count = values.Count;
        if (count == 0) return Array.Empty<EveId>();
        if (count <= LinearDistinctLimit)
        {
            int unique = 0;
            for (int i = 0; i < count; i++)
            {
                var value = values[i];
                bool seen = false;
                for (int j = 0; j < unique; j++) if (values[j].Equals(value)) { seen = true; break; }
                if (!seen) values[unique++] = value;
            }
            if (unique < count) values.RemoveRange(unique, count - unique);
            for (int i = 1; i < unique; i++)
            {
                var value = values[i];
                int j = i - 1;
                while (j >= 0 && values[j].CompareTo(value) > 0) { values[j + 1] = values[j]; j--; }
                values[j + 1] = value;
            }
        }
        else
        {
            var seen = new HashSet<EveId>();
            int unique = 0;
            for (int i = 0; i < count; i++)
            {
                var value = values[i];
                if (seen.Add(value)) values[unique++] = value;
            }
            if (unique < count) values.RemoveRange(unique, count - unique);
            values.Sort();
        }
        var result = values.ToArray();
        if (values.Capacity > MaximumRetainedScratch) { values.Clear(); values.TrimExcess(); }
        return result;
    }
    private ElementStyle StyleFor(string elementId)
    {
        var styles = Elements;
        if (styles == null) return null;
        for (int i = 0; i < styles.Length; i++)
        {
            var style = styles[i];
            if (style != null && style.ElementId == elementId) return style;
        }
        return null;
    }
    public void Begin(double damage, Vector3 position, Color color, EveTimeScale timeScale)
        => Begin(damage, position, color, timeScale, false, Array.Empty<EveId>());
    public void Begin(double damage, Vector3 position, Color color, EveTimeScale timeScale, bool isCritical, EveId[] elements)
    {
        using var diagnostic = EveHotPathMetrics.Measure("popup.begin");
        if (!double.IsFinite(damage) || damage < 0f || !float.IsFinite(Lifetime) || Lifetime <= 0f || !float.IsFinite(Rise) || Rise < 0f ||
            !float.IsFinite(GlowIntensity) || GlowIntensity < 0f || !float.IsFinite(CriticalGlowIntensity) || CriticalGlowIntensity < 0f ||
            !float.IsFinite(CriticalScaleMultiplier) || CriticalScaleMultiplier <= 0f || !float.IsFinite(FadeStart) || FadeStart < 0f || FadeStart >= 1f ||
            !float.IsFinite(ElementIconScale) || ElementIconScale < .1f || ElementIconScale > 1f ||
            Scale == null) throw new ArgumentException("Invalid damage popup settings.");
        if (text == null) { text = GetComponent<TextMeshPro>(); meshRenderer = GetComponent<MeshRenderer>(); block = new MaterialPropertyBlock(); baseScale = transform.localScale; }
        origin = position; tint = color; elapsed = 0f; clock = timeScale; critical = isCritical;
        using (EveHotPathMetrics.Measure("popup.elements"))
        {
            var scratch = ElementScratch();
            if (elements != null) for (int i = 0; i < elements.Length; i++) scratch.Add(elements[i]);
            DisplayedElements = DistinctSorted(scratch);
        }
        text.spriteAsset = Icons; text.richText = true;
        var label = new StringBuilder();
        using (EveHotPathMetrics.Measure("popup.label"))
        {
        if (critical) label.Append(Icon("crit", tint)).Append(' ');
        using (EveHotPathMetrics.Measure("popup.format")) label.Append(FormatDamage(damage));
        foreach (var element in DisplayedElements)
        {
            string elementText = element.ToString();
            var style = StyleFor(elementText);
            string spriteName = style?.SpriteName ?? "element";
            string sprite = Icon(spriteName, style?.Color ?? new Color(.75f,.6f,1f));
            // Keep the smaller glyph's centre at its original height; size tags also reduce its advance.
            var glyph = Icons.spriteCharacterTable[Icons.GetSpriteIndexFromName(spriteName)].glyph;
            float centre = (glyph.metrics.horizontalBearingY - glyph.metrics.height * .5f) / Icons.faceInfo.pointSize;
            float offset = (1f - ElementIconScale) * centre / ElementIconScale;
            label.Append(" <size=").Append((ElementIconScale * 100f).ToString("0.###", CultureInfo.InvariantCulture))
                .Append("%><voffset=").Append(offset.ToString("0.#####", CultureInfo.InvariantCulture))
                .Append("em>").Append(sprite).Append("</voffset></size>");
            // Unmapped mod elements remain visible and distinguishable instead of being silently dropped.
            if (style == null) label.Append("<color=#BFAAFF>").Append(elementText).Append("</color>");
        }
        text.text = label.ToString(); text.color = Color.white;
        }
        using (EveHotPathMetrics.Measure("popup.activate")) gameObject.SetActive(true);
        using (EveHotPathMetrics.Measure("popup.mesh")) text.ForceMeshUpdate();
        using (EveHotPathMetrics.Measure("popup.renderers")) renderers = GetComponentsInChildren<MeshRenderer>(true);
        using (EveHotPathMetrics.Measure("popup.spawn-render")) Render(PNViewCamera.Current);
    }
    private string Icon(string name, Color color)
    {
        if (Icons == null || Icons.GetSpriteIndexFromName(name) < 0)
            throw new InvalidOperationException("Popup sprite asset is missing icon: " + name);
        return "<sprite name=\"" + name + "\" color=#" + ColorUtility.ToHtmlStringRGBA(color) + ">";
    }
    public bool Advance(float deltaTime, Camera camera = null)
    {
        using var diagnostic = EveHotPathMetrics.Measure("popup.advance");
        if (!float.IsFinite(deltaTime) || deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        elapsed += deltaTime * (clock != null && clock.isActiveAndEnabled ? clock.Scale : 1f);
        Render(camera == null ? PNViewCamera.Current : camera);
        return elapsed >= Lifetime;
    }
    private void Render(Camera camera)
    {
        using var diagnostic = EveHotPathMetrics.Measure("popup.render");
        float t = Progress;
        var position = origin + Vector3.up * (Rise * t);
        // One transform write for position+rotation when a camera is present; the final pose equals the former two separate writes.
        if (camera != null) transform.SetPositionAndRotation(position, camera.transform.rotation);
        else transform.position = position;
        transform.localScale = baseScale * (critical ? CriticalScaleMultiplier : 1f) * Mathf.Max(0f, Scale.Evaluate(t));
        block.SetColor("_FaceColor", tint);
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FadeStart, 1f, t));
        block.SetFloat("_EmissionIntensity", critical ? CriticalGlowIntensity : GlowIntensity);
        block.SetFloat("_PopupFade", fade);
        using (EveHotPathMetrics.Measure("popup.render-block"))
            foreach (var renderer in renderers) if (renderer != null) renderer.SetPropertyBlock(block);
    }
    public void DetachClock() => clock = null;
}
#endif
