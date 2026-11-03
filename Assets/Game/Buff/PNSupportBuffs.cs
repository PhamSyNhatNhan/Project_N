using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Two Buffs for support mobs (the Starchanter's heal channel and speed boost).
//       block_hits  : blocks the next N damaging hits on its owner (every hit counts 1, damage over time included). Runs through PNStat.BeforeDamage, so a blocked hit applies nothing
//                     (no HP, Lorica, iframe, popup or event). Ends with StacksDepleted when the last hit is spent, or Expired when the time runs out. One instance per target: applying
//                     it again resets hits and time. Apply input `{duration, hits}` overrides the registered payload.
//       speed_boost : +value to the owner's move_speed for `duration` seconds. Every application is a new instance (up to 8), the modifiers add. Plain stat template, no code of its own.
// VI: Hai Buff cho quái hỗ trợ. block_hits chặn N nhát tiếp theo (mỗi nhát 1, kể cả DOT) qua PNStat.BeforeDamage; speed_boost cộng move_speed, mỗi lần áp là một instance mới.
public static class PNSupportBuffContent
{
    public static readonly EveId BlockHits = EveId.Parse("project_n:block_hits"), SpeedBoost = EveId.Parse("project_n:speed_boost");
    // Glow: a pale holy gold for the guard (kept apart from the lightning vulnerability's saturated gold), a fresh green for speed.
    public const string BlockGlow = "#f2d98a", SpeedGlow = "#6fdc8c";
    public static void Register(EveRegistryHub hub)
    {
        var registry = hub.Buffs(); var buff = new[] { EveId.Parse("project_n:buff") };
        registry.Register(new PNBuffDefinition(BlockHits, BlockHits, data => new PNBuffBlockHitsBehavior(data), JObject.Parse("{'duration':5.5,'clock':'Entity','hits':2}"), buff,
            iconPath: "PNBuffIcons/PNBlockHits", glow: BlockGlow, validate: ValidateBlock, validateInput: ValidateInput).WithCountdown(PNBuffCountdown.BottomRight));
        registry.Register(new PNBuffDefinition(SpeedBoost, EveId.Parse("project_n:buff_stat_template"), data => new PNBuffStatBehavior(data),
            JObject.Parse("{'duration':5,'clock':'Entity','statId':'base:move_speed','value':0.2,'group':0,'maximumStacks':1}"), buff, multipleInstances: true, maximumInstances: 8,
            iconPath: "PNBuffIcons/PNSpeedBoost", glow: SpeedGlow, validate: (data, content) => PNBuffBuiltinSettings.Validate("buff_stat_template", data, content),
            validateReferences: (data, context) => PNBuffPayloadReferences.Template("buff_stat_template", data, context)).WithCountdown(PNBuffCountdown.BottomRight));
    }
    public static void ValidateBlock(JToken data, EveRegistryHub content)
    {
        var obj = PNBuffContentBatch.Object(data); PNBuffContentBatch.Fields(obj, "duration", "clock", "hits");
        PNBuffCollection.Seconds(PNBuffContentBatch.Number(obj, "duration"), false); PNBuffBuiltinSettings.Clock(obj);
        int hits = PNBuffContentBatch.Integer(obj, "hits"); if (hits < 1 || hits > 1000) throw new FormatException("block_hits hits must be 1..1000.");
    }
    // Apply input: nothing, or `{duration, hits}`.
    public static void ValidateInput(JToken input)
    {
        if (input == null || input.Type == JTokenType.Null) return;
        var obj = PNBuffContentBatch.Object(input); PNBuffContentBatch.Fields(obj, "duration", "hits");
        if (obj["duration"] != null) PNBuffCollection.Seconds(PNBuffContentBatch.Number(obj, "duration"), false);
        if (obj["hits"] != null) { int hits = PNBuffContentBatch.Integer(obj, "hits"); if (hits < 1 || hits > 1000) throw new FormatException("block_hits hits must be 1..1000."); }
    }
}

public sealed class PNBuffBlockHitsBehavior : PNBuffTimedBehavior
{
    public override bool EventDrivenReplication => true;
    private const string Deplete = "deplete";
    private PNBuffContext owner; private PNStat target; private int remaining; private bool spent;
    public PNBuffBlockHitsBehavior(JToken data) : base(data) { }
    public override double? StackCount => remaining;                      // the hits left, shown top right
    private int Hits(JToken input) => input is JObject obj && obj["hits"] != null ? PNBuffContentBatch.Integer(obj, "hits") : PNBuffContentBatch.Integer(Data, "hits");
    private static void ApplyInputDuration(PNBuffContext context, JToken input)
    { if (input is JObject obj && obj["duration"] != null) context.SetDuration(PNBuffContentBatch.Number(obj, "duration")); }
    public override void OnApply(PNBuffContext context, JToken input)
    {
        base.OnApply(context, input); ApplyInputDuration(context, input);
        owner = context; target = context.Services.Target; remaining = Hits(input); spent = false;
        if (target != null) target.BeforeDamage += OnHit;
        PublishNumbers();
    }
    // The base re-reads the registered duration when the behavior starts, so the input duration is applied again afterwards.
    public override void OnBehaviorStarted(PNBuffContext context, bool migrated)
    { base.OnBehaviorStarted(context, migrated); if (!migrated) ApplyInputDuration(context, context.InitialInput); }
    public override bool OnReapply(PNBuffContext context, PNBuffApplyRequest request)
    {
        context.SetDuration(PNBuffContentBatch.Number(Data, "duration")); ApplyInputDuration(context, request.Input);
        if (spent) context.CancelSchedule(Deplete);
        remaining = Hits(request.Input); spent = false; PublishNumbers(); return true;
    }
    // Spends one hit and blocks it. The last hit ends the Buff on the next clock step (a Buff cannot end itself from inside another entity's damage call).
    private void OnHit(PNDamageGate gate)
    {
        if (gate.Blocked || spent || remaining <= 0 || owner == null || owner.IsEnding || owner.IsSuspended) return;
        remaining--; gate.Block();
        if (remaining <= 0) { spent = true; owner.Schedule(Deplete, .001); }
        PublishNumbers();
    }
    public override void OnScheduled(PNBuffContext context, string key)
    { if (key == Deplete) context.End(PNBuffEndReason.StacksDepleted); }
    public override void OnCleanup(PNBuffContext context)
    { if (target != null) target.BeforeDamage -= OnHit; target = null; owner = null; }
}
