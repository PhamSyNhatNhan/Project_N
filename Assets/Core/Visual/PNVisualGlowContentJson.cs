using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: JSON of the glow registry inside `extensions.project_n`: visualGlows. Same rules as the other Visual JSON: unknown fields refused, limits refused never clamped, the batch
//     commits atomically. Every glow is also registered as a look of the same ID (so its path follows the look rule: lowercase, digits, underscores, at most 24).
// VI: JSON cua glow (visualGlows). Moi glow cung dang ky thanh mot look cung ID.
public sealed partial class PNBuffContentBatch
{
    private readonly Dictionary<EveId, PNVisualGlowDefinition> visualGlows = new Dictionary<EveId, PNVisualGlowDefinition>();
    private readonly Dictionary<EveId, PNLookDefinition> visualGlowLooks = new Dictionary<EveId, PNLookDefinition>();

    private IEnumerable<EveIIdentified> VisualGlowDefinitions => visualGlows.Values.Cast<EveIIdentified>().Concat(visualGlowLooks.Values);

    private void CommitVisualGlows()
    {
        foreach (var d in visualGlows.Values) hub.GetOrCreate<PNVisualGlowDefinition>().Register(d);
        foreach (var d in visualGlowLooks.Values) hub.GetOrCreate<PNLookDefinition>().Register(d);
    }

    private void ParseVisualGlows(JObject root, EveContentExtensionDocument document)
    {
        foreach (var token in Array(root["visualGlows"]))
        {
            var glow = ParseGlow(Object(token), document);
            var glows = hub.GetOrCreate<PNVisualGlowDefinition>();
            if (visualGlows.ContainsKey(glow.Id) || glows.TryGet(glow.Id, out _) || hub.GetOrCreate<PNLookDefinition>().TryGet(glow.Id, out _)) throw new FormatException("Duplicate glow or look " + glow.Id);
            if (visualGlows.Count + glows.Snapshot().Count >= PNVisualGlowDefinition.MaxRegistry) throw new FormatException("Too many glows (" + PNVisualGlowDefinition.MaxRegistry + ").");
            PNLookDefinition look;
            try { look = new PNLookDefinition(glow.Id, build => PNGlowLook.Create(glow, build)); }
            catch (ArgumentException error) { throw new FormatException("glow " + glow.Id + ": " + error.Message); }
            visualGlows.Add(glow.Id, glow); visualGlowLooks.Add(glow.Id, look);
        }
    }

    private PNVisualGlowDefinition ParseGlow(JObject obj, EveContentExtensionDocument document)
    {
        Fields(obj, "id", "parts", "key", "color", "core", "tolerance", "amount", "spill", "spread", "priority", "rise", "release", "pulse", "warning");
        var id = Owned(String(obj, "id"), document);
        PNVisualColor Colour(string field) => PNVisualColor.Parse(String(obj, field));
        var parts = obj["parts"] == null ? null : Names(obj["parts"]).ToArray();
        float depth = 0f, rate = 0f, seconds = 0f; EveId? state = null;
        if (obj["pulse"] != null)
        {
            var pulse = Object(obj["pulse"]); Fields(pulse, "depth", "rate");
            depth = Float(pulse, "depth"); rate = Float(pulse, "rate");
        }
        if (obj["warning"] != null)
        {
            var warning = Object(obj["warning"]); Fields(warning, "state", "seconds");
            state = EveId.Parse(String(warning, "state")); seconds = Float(warning, "seconds");
        }
        var color = Colour("color");
        return new PNVisualGlowDefinition(id, parts, obj["key"] == null ? (PNVisualColor?)null : Colour("key"), color, obj["core"] == null ? color : Colour("core"),
            Float(obj, "tolerance", .5f), Float(obj, "amount", 1f), Float(obj, "spill", 0f), Float(obj, "spread", 1f), Integer(obj, "priority", 0),
            Float(obj, "rise", 0f), Float(obj, "release", 0f), depth, rate, state, seconds);
    }
}
