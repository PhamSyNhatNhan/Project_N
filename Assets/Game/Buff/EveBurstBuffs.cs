using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// Eve-specific registered configuration; lifetime is owned by Burst/repeat-chain logic.
public static class EveBurstBuffContent
{
    public static readonly EveId BlazingAssault = EveId.Parse("project_n:eve_blazing_assault");
    public static readonly EveId RisingMomentum = EveId.Parse("project_n:eve_rising_momentum");
    public static void Register(EveRegistryHub hub)
    {
        hub.Buffs().Register(new PNBuffDefinition(BlazingAssault, EveId.Parse("project_n:buff_" + BlazingAssault.Path), data => new EveBlazingAssaultBehavior(data),
            JObject.Parse("{'clock':'Game','contribution':{'readMode':'Live','skillIds':['base:eve_attack_entry'],'categoryIds':['base:attack'],'effects':[{'trigger':'OnHit','kind':'Operation','id':'base:apply_buff','data':{'buffId':'base:burn','input':{'stacks':1}},'target':'HitTarget','hitScope':'PerTarget'}]}}"),
            new[] { EveId.Parse("base:buff") }, scope: PNBuffInstanceScope.Source, validate: ValidateBlazing,
            validateReferences: (data, context) => PNBuffPayloadReferences.Contribution(data["contribution"], context)));
        hub.Buffs().Register(new PNBuffDefinition(RisingMomentum, EveId.Parse("project_n:buff_" + RisingMomentum.Path), data => new EveRisingMomentumBehavior(data),
            JObject.Parse("{'clock':'Game','maximumStacks':2147483647,'modifiers':[{'statId':'base:attack_power','value':0.1,'group':2},{'statId':'base:attack_speed','value':10,'group':0}]}"),
            new[] { EveId.Parse("base:buff") }, scope: PNBuffInstanceScope.Source, validate: ValidateMomentum,
            validateReferences: (data, context) => { foreach (var entry in (JArray)data["modifiers"]) PNBuffPayloadReferences.Modifier(entry, context); }));
    }
    private static void ValidateBlazing(JToken payload, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(payload); PNBuffContentBatch.Fields(obj, "clock", "contribution"); PNBuffBuiltinSettings.Clock(obj);
        PNBuffContentBatch.Object(obj["contribution"]);
        if (hub.IsSealed) new PNSkillContribution(obj["contribution"], hub);
    }
    private static void ValidateMomentum(JToken payload, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(payload); PNBuffContentBatch.Fields(obj, "clock", "maximumStacks", "modifiers"); PNBuffBuiltinSettings.Clock(obj);
        if (PNBuffContentBatch.Integer(obj, "maximumStacks") < 1) throw new FormatException("Momentum cap must be positive.");
        if (!(obj["modifiers"] is JArray entries) || entries.Count == 0) throw new FormatException("Momentum modifiers required.");
        foreach (var token in entries)
        {
            var entry = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(entry, "statId", "value", "group", "calculationId", "contributionId");
            PNStatModifierData.Validate(entry, hub);
            var id = EveId.Parse(PNBuffContentBatch.String(entry, "statId"));
            if (hub.IsSealed && hub.Stats.Get(id).Kind != EveStatKind.Number) throw new FormatException("Momentum requires number stats.");
            PNBuffBuiltinSettings.RequireAmount(PNBuffContentBatch.Number(entry, "value"));
            if (PNBuffContentBatch.Integer(entry, "group") < 0) throw new FormatException("Momentum group must be nonnegative.");
        }
    }
}

public sealed class EveBlazingAssaultBehavior : PNBuffBehavior
{
    private readonly JObject data;
    public EveBlazingAssaultBehavior(JToken payload) { data = (JObject)payload.DeepClone(); }
    public override void OnApply(PNBuffContext context, JToken input)
    { context.SetClock(PNBuffBuiltinSettings.Clock(data)); context.SetDuration(null); OnResume(context); }
    public override bool OnReapply(PNBuffContext context, PNBuffApplyRequest request) => true;
    public override void OnResume(PNBuffContext context) => context.ModifySkill(new PNSkillContribution(data["contribution"], context.Registry));
    public override void OnBehaviorStarted(PNBuffContext context, bool migrated) => OnApply(context, null);
}

public sealed class EveRisingMomentumBehavior : PNBuffBehavior
{
    private readonly JObject data;
    private int stacks = 1;
    private IDisposable modifier;
    public EveRisingMomentumBehavior(JToken payload) { data = (JObject)payload.DeepClone(); }
    public override double? StackCount => stacks;
    private int Maximum => PNBuffContentBatch.Integer(data, "maximumStacks");
    private void ApplyModifiers(PNBuffContext context)
    {
        var values = PNBuffContentBatch.Array(data["modifiers"]).Select(token =>
        {
            var item = PNBuffContentBatch.Object(token);
            return PNStatModifierData.Parse(item, stacks);
        }).ToArray();
        modifier?.Dispose(); modifier = context.ModifyStats(values);
        Presentation?.Publish(new PNBuffNumbers(topRight: stacks));
    }
    public override void OnApply(PNBuffContext context, JToken input)
    { context.SetClock(PNBuffBuiltinSettings.Clock(data)); context.SetDuration(null); stacks = 1; ApplyModifiers(context); }
    public override bool OnReapply(PNBuffContext context, PNBuffApplyRequest request)
    { stacks = (int)Math.Min(Maximum, (long)stacks + 1); ApplyModifiers(context); return true; }
    public override void OnResume(PNBuffContext context) => ApplyModifiers(context);
    public override void OnCleanup(PNBuffContext context) { modifier?.Dispose(); modifier = null; }
    public override void OnBehaviorStarted(PNBuffContext context, bool migrated) => OnApply(context, null);
}

// Exact owned receipt, including cleanup requested while Apply is still Pending.
internal sealed class EveOwnedBuff : IDisposable
{
    private readonly PNBuffCollection collection;
    private readonly EveId id;
    private readonly PNBuffReceipt receipt;
    private bool disposed;
    public EveOwnedBuff(PNSkillActionContext context, EveId id)
    {
        this.id = id; collection = context.Skill.GetComponent<PNBuffManager>().Buffs; receipt = Apply(context);
    }
    private PNBuffReceipt Apply(PNSkillActionContext context)
    {
        var target = context.Skill.GetComponent<PNStat>();
        var execution = new PNSkillActionContext(context.Skill, context.SkillId, context.Data, context.Registry, new[] { target }, context.Direction, context.Invocation, false, context.Position);
        using (var operation = context.Registry.Operations().Get(EveId.Parse("base:apply_buff")).Create())
        {
            if (!(operation is PNApplyBuffOperation apply)) throw new InvalidOperationException("Eve Buff grant requires apply receipt contract.");
            apply.Execute(execution, new JObject { ["buffId"] = id.ToString() });
            if (apply.Results.Count != 1) throw new InvalidOperationException("Eve Buff grant must report one target receipt.");
            return apply.Results[0];
        }
    }
    public PNBuffSnapshot Snapshot => !disposed && !collection.IsDisposed && receipt.Handle.IsAlive ? collection.Get(receipt.Handle) : null;
    public bool IsPending => !disposed && receipt.Status == PNBuffStatus.Pending;
    public void Reapply(PNSkillActionContext context) { if (!disposed && !collection.IsDisposed) Apply(context); }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        collection.Coordinator.Submit(() =>
        {
            if (!collection.IsDisposed && receipt.Handle.IsAlive && EveRegistrySession.IsReady && EveRegistrySession.Context.HasAuthority && EveRegistrySession.Context.SessionId == collection.SessionId)
                collection.Remove(receipt.Handle, PNBuffEndReason.Bypass, true);
        });
    }
}
