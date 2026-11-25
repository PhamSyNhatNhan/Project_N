using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: `project_n:gura_untouchable` (Docs/GuraCharacter.md 5.1): blocks EVERY hit on its owner for as long as it lasts, through `PNStat.BeforeDamage` (a blocked hit applies nothing: no HP, Lorica, iframe, popup or event).
//     It is not iframe (which still takes 30%) and it does not stop enemies from targeting the owner. Payload `{duration, clock}`; apply input `{duration}` overrides the registered duration (a skill sets the length of its own
//     immune phase). Ends by time, by removal (the skill takes it back when its phase ends or is cancelled) or when the owner unloads. One instance per target: applying again resets the time.
// VI: Buff bat tu theo thoi gian: chan moi don danh vao chu qua BeforeDamage; khong phai iframe, quai van nham duoc. Input `{duration}` ghi de thoi luong.
public sealed class PNUntouchableBuffBehavior : PNBuffTimedBehavior
{
    public static readonly EveId Id = EveId.Parse("project_n:gura_untouchable");
    public override bool EventDrivenReplication => true;
    private PNStat target; private PNBuffContext owner;
    public PNUntouchableBuffBehavior(JToken data) : base(data) { }
    private static void ApplyInputDuration(PNBuffContext context, JToken input)
    { if (input is JObject obj && obj["duration"] != null) context.SetDuration(PNBuffContentBatch.Number(obj, "duration")); }
    public override void OnApply(PNBuffContext context, JToken input)
    {
        base.OnApply(context, input); ApplyInputDuration(context, input);
        owner = context; target = context.Services.Target;
        if (target != null) target.BeforeDamage += OnHit;
        PublishNumbers();
    }
    public override void OnBehaviorStarted(PNBuffContext context, bool migrated)
    { base.OnBehaviorStarted(context, migrated); if (!migrated) ApplyInputDuration(context, context.InitialInput); }
    public override bool OnReapply(PNBuffContext context, PNBuffApplyRequest request)
    { context.SetDuration(PNBuffContentBatch.Number(Data, "duration")); ApplyInputDuration(context, request.Input); PublishNumbers(); return true; }
    private void OnHit(PNDamageGate gate)
    { if (gate.Blocked || owner == null || owner.IsEnding || owner.IsSuspended) return; gate.Block(); }
    public override void OnCleanup(PNBuffContext context)
    { if (target != null) target.BeforeDamage -= OnHit; target = null; owner = null; }

    public static void Register(EveRegistryHub hub)
    {
        hub.Buffs().Register(new PNBuffDefinition(Id, Id, data => new PNUntouchableBuffBehavior(data), JObject.Parse("{'duration':1,'clock':'Game'}"), new[] { EveId.Parse("project_n:buff") },
            validate: ValidatePayload, validateInput: ValidateInput).WithCountdown(PNBuffCountdown.BottomRight));
    }
    public static void ValidatePayload(JToken data, EveRegistryHub content)
    {
        var obj = PNBuffContentBatch.Object(data); PNBuffContentBatch.Fields(obj, "duration", "clock");
        PNBuffCollection.Seconds(PNBuffContentBatch.Number(obj, "duration"), false); PNBuffBuiltinSettings.Clock(obj);
    }
    public static void ValidateInput(JToken input)
    {
        if (input == null || input.Type == JTokenType.Null) return;
        var obj = PNBuffContentBatch.Object(input); PNBuffContentBatch.Fields(obj, "duration");
        if (obj["duration"] != null) PNBuffCollection.Seconds(PNBuffContentBatch.Number(obj, "duration"), false);
    }
}
