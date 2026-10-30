using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: JSON of the string registries inside `extensions.project_n`: visualStringShapes, visualStringColors, visualStringStyles, and the `strings` list of an appearance
//     (Docs/VisualInkStringsImplementationGuide.md, P3). Same rules as the other Visual JSON: unknown fields refused, limits refused never clamped, references checked against
//     the pending batch and the registries, everything commits atomically with the batch.
// VI: JSON của dây rối: hình dạng, màu, kiểu và danh sách `strings` của appearance.
public sealed partial class PNBuffContentBatch
{
    private readonly Dictionary<EveId, PNVisualStringShapeDefinition> visualStringShapes = new Dictionary<EveId, PNVisualStringShapeDefinition>();
    private readonly Dictionary<EveId, PNVisualStringColorDefinition> visualStringColors = new Dictionary<EveId, PNVisualStringColorDefinition>();
    private readonly Dictionary<EveId, PNVisualStringStyleDefinition> visualStringStyles = new Dictionary<EveId, PNVisualStringStyleDefinition>();

    private IEnumerable<EveIIdentified> VisualStringDefinitions => visualStringShapes.Values.Cast<EveIIdentified>().Concat(visualStringColors.Values).Concat(visualStringStyles.Values);

    private void CommitVisualStrings()
    {
        foreach (var d in visualStringShapes.Values) hub.Get<PNVisualStringShapeDefinition>().Register(d);
        foreach (var d in visualStringColors.Values) hub.Get<PNVisualStringColorDefinition>().Register(d);
        foreach (var d in visualStringStyles.Values) hub.Get<PNVisualStringStyleDefinition>().Register(d);
    }

    private void ParseVisualStrings(JObject root, EveContentExtensionDocument document)
    {
        foreach (var token in Array(root["visualStringShapes"])) AddStringDefinition(visualStringShapes, ParseStringShape(Object(token), document), hub.Get<PNVisualStringShapeDefinition>());
        foreach (var token in Array(root["visualStringColors"])) AddStringDefinition(visualStringColors, ParseStringColor(Object(token), document), hub.Get<PNVisualStringColorDefinition>());
        foreach (var token in Array(root["visualStringStyles"])) AddStringDefinition(visualStringStyles, ParseStringStyle(Object(token), document), hub.Get<PNVisualStringStyleDefinition>());
    }

    private static void AddStringDefinition<T>(Dictionary<EveId, T> pending, T definition, EveRegistry<T> registry) where T : class, EveIIdentified
    {
        if (pending.ContainsKey(definition.Id) || registry.TryGet(definition.Id, out _)) throw new FormatException("Duplicate visual string definition " + definition.Id);
        if (pending.Count + registry.Snapshot().Count >= PNVisualStringLimits.MaxRegistry) throw new FormatException("Too many visual string definitions of one kind (" + PNVisualStringLimits.MaxRegistry + ").");
        pending.Add(definition.Id, definition);
    }

    private PNVisualStringShapeDefinition ParseStringShape(JObject obj, EveContentExtensionDocument document)
    {
        Fields(obj, "id", "leave", "leaveDistance", "curve", "slack", "sway", "width", "length", "segments");
        var id = Owned(String(obj, "id"), document);
        var leave = obj["leave"] == null ? new[] { 0f, 1f } : Pair(obj["leave"], "string leave"); var sway = obj["sway"] == null ? new[] { 0f, 1f } : Pair(obj["sway"], "string sway"); var width = Pair(obj["width"], "string width");
        float length = 0f;
        if (obj["length"] != null)
        {
            if (obj["length"].Type == JTokenType.String) { if (obj["length"].Value<string>() != "toTop") throw new FormatException("string length must be \"toTop\" or a number of metres."); }
            else length = Num(obj["length"], "string length");
        }
        return new PNVisualStringShapeDefinition(id, leave[0], leave[1], Float(obj, "leaveDistance", 0f), Float(obj, "curve", 0f), Float(obj, "slack", 0f), sway[0], sway[1], width[0], width[1], length, Integer(obj, "segments", 24));
    }

    private PNVisualStringColorDefinition ParseStringColor(JObject obj, EveContentExtensionDocument document)
    {
        Fields(obj, "id", "core", "halo", "knot", "alpha", "fadeTop");
        var id = Owned(String(obj, "id"), document);
        PNVisualColor Colour(string field) => PNVisualColor.Parse(obj[field] != null && obj[field].Type == JTokenType.String ? obj[field].Value<string>() : throw new FormatException("string colour needs " + field + " as #RRGGBB."));
        return new PNVisualStringColorDefinition(id, Colour("core"), Colour("halo"), Colour("knot"), Float(obj, "alpha", 1f), Float(obj, "fadeTop", 0f));
    }

    private PNVisualStringStyleDefinition ParseStringStyle(JObject obj, EveContentExtensionDocument document)
    {
        Fields(obj, "id", "extends", "shape", "color", "lean", "entrance");
        var id = Owned(String(obj, "id"), document);
        EveId? Ref<T>(string field, EveRegistry<T> registry) where T : class, EveIIdentified
        {
            if (obj[field] == null || obj[field].Type == JTokenType.Null) return null;
            var value = EveId.Parse(String(obj, field)); Reference(value, document, registry); return value;
        }
        var style = new PNVisualStringStyleDefinition(id, Ref("extends", hub.Get<PNVisualStringStyleDefinition>()), Ref("shape", hub.Get<PNVisualStringShapeDefinition>()), Ref("color", hub.Get<PNVisualStringColorDefinition>()),
            obj["lean"] == null ? (float?)null : Float(obj, "lean"), obj["entrance"] == null ? null : String(obj, "entrance"));
        checks.Add(() =>
        {
            if (style.Extends.HasValue && FindVisual(visualStringStyles, style.Extends.Value) == null) throw new FormatException("string style " + id + " extends unknown style " + style.Extends.Value);
            if (style.Shape.HasValue && FindVisual(visualStringShapes, style.Shape.Value) == null) throw new FormatException("string style " + id + " uses unknown shape " + style.Shape.Value);
            if (style.Color.HasValue && FindVisual(visualStringColors, style.Color.Value) == null) throw new FormatException("string style " + id + " uses unknown colour " + style.Color.Value);
            ResolveStyleChain(style.Id);
        });
        return style;
    }

    private void ResolveStyleChain(EveId styleId)      // depth and cycle (the resolver reports both)
    {
        PNVisualStringResolver.Resolve(new PNVisualStringEntry("x", styleId, null, null), id => FindVisual(visualStringStyles, id),
            id => new PNVisualStringShapeDefinition(id, 0, 1, 0, 0, 0, 0, 1, .01f, .01f, 0, PNVisualStringLimits.MinSegments), id => new PNVisualStringColorDefinition(id, default, default, default, 1, 0));
    }

    private PNVisualStringEntry ParseStringEntry(JToken token, EveContentExtensionDocument document)
    {
        var obj = Object(token); Fields(obj, "socket", "style", "shape", "color", "offset", "lean");
        EveId? Ref<T>(string field, EveRegistry<T> registry) where T : class, EveIIdentified
        {
            if (obj[field] == null || obj[field].Type == JTokenType.Null) return null;
            var value = EveId.Parse(String(obj, field)); Reference(value, document, registry); return value;
        }
        var offset = obj["offset"] == null ? new float[2] : Pair(obj["offset"], "string offset");
        return new PNVisualStringEntry(String(obj, "socket"), Ref("style", hub.Get<PNVisualStringStyleDefinition>()), Ref("shape", hub.Get<PNVisualStringShapeDefinition>()), Ref("color", hub.Get<PNVisualStringColorDefinition>()),
            offset[0], offset[1], obj["lean"] == null ? (float?)null : Float(obj, "lean"));
    }

    private void CheckStringEntries(PNVisualAppearanceDefinition appearance, PNVisualResolvedRig rig)
    {
        foreach (var entry in appearance.Strings)
        {
            if (rig.SocketIndex(entry.Socket) < 0) throw new FormatException("appearance " + appearance.Id + " ties a string to unknown socket " + entry.Socket);
            PNVisualStringResolver.Resolve(entry, id => FindVisual(visualStringStyles, id), id => FindVisual(visualStringShapes, id), id => FindVisual(visualStringColors, id));
        }
    }
}
