using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// Independent Apply instances; input remains available across selected behavior overrides.
public static class PNTimeScaleBuffContent
{
    public const string Glow = "#8040f0";   // deep purple, shared by both variants

    public static void Register(EveRegistryHub hub)
    {
        foreach (bool area in new[] { false, true })
        {
            bool isArea = area;
            var definition = new PNBuffDefinition(EveId.Parse("project_n:timescale_reduction" + (area ? "_area" : "")), EveId.Parse("project_n:buff_timescale"),
                data => new PNTimeScaleBuffBehavior(data), new JObject { ["areaOwned"] = area },
                new[] { EveId.Parse("base:debuff"), EveId.Parse("base:timescale") },
                multipleInstances: true, maximumInstances: int.MaxValue, canDispel: !area,
                validate: (data, registry) => { var obj = PNBuffContentBatch.Object(data); PNBuffContentBatch.Fields(obj, "areaOwned"); PNBuffContentBatch.Bool(obj, "areaOwned"); },
                validateInput: input => ValidateInput(input, isArea),
                iconPath: "PNBuffIcons/PNTimescaleReduction" + (area ? "Area" : ""), glow: Glow);
            // Only the duration-owned variant displays seconds; area-owned instances have no expiration and stay legacy.
            hub.Buffs().Register(area ? definition : definition.WithCountdown(PNBuffCountdown.BottomRight));
        }
    }
    public static JObject ValidateInput(JToken input, bool area)
    {
        var obj = PNBuffContentBatch.Object(input);
        PNBuffContentBatch.Fields(obj, area ? new[] { "reduction", "minimumReduction" } : new[] { "reduction", "minimumReduction", "duration" });
        foreach (string name in new[] { "reduction", "minimumReduction" })
        {
            double value = PNBuffContentBatch.Number(obj, name);
            if (value < 0 || value > float.MaxValue) throw new FormatException("Invalid timescale " + name);
        }
        if (!area) PNBuffCollection.Seconds(PNBuffContentBatch.Number(obj, "duration"), false);
        return obj;
    }
}

public class PNTimeScaleBuffBehavior : PNBuffBehavior
{
    private readonly bool area;
    private float reduction, minimumReduction;
    public PNTimeScaleBuffBehavior(JToken data) { area = PNBuffContentBatch.Bool((JObject)data, "areaOwned"); }
    private void Initialize(PNBuffContext context, JToken input)
    {
        var obj = PNTimeScaleBuffContent.ValidateInput(input, area);
        reduction = (float)PNBuffContentBatch.Number(obj, "reduction");
        minimumReduction = (float)PNBuffContentBatch.Number(obj, "minimumReduction");
        context.SetClock(PNBuffClock.Game);
        context.SetDuration(area ? (double?)null : PNBuffContentBatch.Number(obj, "duration"));
        if (!area) Presentation.WatchRemainingSeconds(seconds => Presentation.Publish(new PNBuffNumbers(bottomRight: seconds)));
        OnResume(context);
    }
    public override void OnApply(PNBuffContext context, JToken input) => Initialize(context, input);
    public override void OnBehaviorStarted(PNBuffContext context, bool migrated) => Initialize(context, context.InitialInput);
    public override void OnResume(PNBuffContext context)
    {
#if UNITY_5_3_OR_NEWER
        var target = context.Services.Target;
        var clock = target != null ? target.GetComponent<EveTimeScale>() : null;
        if (clock == null || !clock.isActiveAndEnabled) throw new InvalidOperationException("Timescale Buff requires an active target clock.");
        long scale = clock.AddOwnedModifier(context.Snapshot.ProfileId, EveTimeModifierKind.Scale, -reduction, target);
        context.Resources.Own(new PNBuffRelease(() => { if (clock != null) clock.RemoveModifier(scale); }));
        long minimum = clock.AddOwnedModifier(context.Snapshot.ProfileId, EveTimeModifierKind.MinScaleReduction, minimumReduction, target);
        context.Resources.Own(new PNBuffRelease(() => { if (clock != null) clock.RemoveModifier(minimum); }));
#endif
    }
}
