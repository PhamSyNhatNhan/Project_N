using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// Explicit presentation payload, never inferred from mutable Buff tags.
public static class PNBuffElementVisualSettings
{
    public static void Validate(JToken token)
    {
        if (token == null) return;
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "kind", "slot", "color", "size");
        string kind = PNBuffContentBatch.String(obj, "kind");
        if (kind != "Arrow" && kind != "Fire" && kind != "Ice" && kind != "Lightning") throw new FormatException("Unknown Buff visual kind.");
        int slot = PNBuffContentBatch.Integer(obj, "slot");
        if (slot < -16 || slot > 16) throw new FormatException("Buff visual slot out of range.");
        double size = PNBuffContentBatch.Number(obj, "size");
        if (size <= 0 || size > 2) throw new FormatException("Buff visual size out of range.");
        if (!(obj["color"] is JArray color) || color.Count != 3) throw new FormatException("Buff visual color requires RGB.");
        foreach (var value in color)
        {
            var wrapper = new JObject { ["v"] = value.DeepClone() };
            double channel = PNBuffContentBatch.Number(wrapper, "v");
            if (channel < 0 || channel > 1) throw new FormatException("Buff visual RGB must be 0..1.");
        }
    }
    public static PNBuffElementVisualLease Acquire(PNBuffContext context, JObject data)
    {
        if (data["visual"] == null || context.Services.Target == null) return null;
#if UNITY_5_3_OR_NEWER
        return context.Resources.Own(PNBuffElementVisualLease.Create(context.Services.Target, context.Snapshot.ProfileId, (JObject)data["visual"], PNBuffBuiltinSettings.Clock(data)));
#else
        return null; // Standalone smoke cannot establish rendering.
#endif
    }
}

public sealed class PNBuffVulnerabilityBehavior : PNBuffTimedBehavior
{
    public PNBuffVulnerabilityBehavior(JToken data) : base(data) { }
    public static void Validate(JToken token, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "duration", "clock", "tagId", "value", "group", "calculationId", "contributionId", "visual");
        PNBuffCollection.Seconds(PNBuffContentBatch.Number(obj, "duration"), false); PNBuffBuiltinSettings.Clock(obj);
        var tag = EveId.Parse(PNBuffContentBatch.String(obj, "tagId")); hub.Tags().Get(tag);
        int group = PNBuffContentBatch.Integer(obj, "group");
        if (group < 0) throw new FormatException("Vulnerability group must be nonnegative.");
        PNBuffBuiltinSettings.RequireAmount(PNBuffContentBatch.Number(obj, "value"));
        PNBuffElementVisualSettings.Validate(obj["visual"]);
        PNStatModifierData.Validate(obj, hub, "tagId");
    }
    public override void OnApply(PNBuffContext context, JToken input) { base.OnApply(context, input); OnResume(context); }
    public override bool OnReapply(PNBuffContext context, PNBuffApplyRequest request)
    { context.SetDuration(PNBuffContentBatch.Number(Data, "duration")); return true; }
    public override void OnResume(PNBuffContext context)
    {
        context.ModifyStats(new[] { PNStatModifierData.Parse(Data, 1d, "tagId") });
        Presentation.PublishVisual(null);
    }
}
