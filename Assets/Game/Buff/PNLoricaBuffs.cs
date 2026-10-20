using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The two Buffs of the enemy lorica mechanic (docs: Docs/EnemyLoricaDesign.md). Both are the enemy's own, not dispellable through the
//     ordinary dispel (CanDispel false; a boss removes `lorica_break` on purpose with bypass_remove_buff), run on the entity clock, and take
//     their duration from the apply input (`{duration}`, the enemy's lorica settings) with the registered payload as the default.
//       lorica_break    : the enemy cannot move, jump, use skills or voluntary motion, and its running cast is interrupted. When it ends
//                         (expired or bypassed) lorica refills and lorica_recovery starts.
//       lorica_recovery : holds a lorica lock lease while it lasts: lorica cannot go down, hits still share.
// VI: Hai Buff của cơ chế lorica ở enemy. Không gỡ được bằng dispel thường; thời lượng lấy từ input khi áp (cấu hình của enemy).
public static class PNLoricaBuffContent
{
    public static void Register(EveRegistryHub hub)
    {
        var registry = hub.Buffs();
        var neutral = new[] { EveId.Parse("project_n:neutral") };
        registry.Register(new PNBuffDefinition(PNContentIds.LoricaBreak, PNContentIds.LoricaBreak, data => new PNLoricaBreakBehavior(data),
            JObject.Parse("{'duration':7,'clock':'Entity','permissionIds':['base:move','base:jump','base:skill_all','base:motion_voluntary'],'interrupt':true}"),
            neutral, canDispel: false,
            validate: (data, content) => PNBuffBuiltinSettings.Validate("buff_control_template", data, content), validateInput: ValidateInput,
            validateReferences: (data, context) => PNBuffPayloadReferences.Template("buff_control_template", data, context)).WithCountdown(PNBuffCountdown.BottomRight));
        registry.Register(new PNBuffDefinition(PNContentIds.LoricaRecovery, PNContentIds.LoricaRecovery, data => new PNLoricaRecoveryBehavior(data),
            JObject.Parse("{'duration':5,'clock':'Entity'}"), neutral, canDispel: false,
            validate: (data, content) =>
            {
                var obj = PNBuffContentBatch.Object(data); PNBuffContentBatch.Fields(obj, "duration", "clock");
                PNBuffCollection.Seconds(PNBuffContentBatch.Number(obj, "duration"), false); PNBuffBuiltinSettings.Clock(obj);
            }, validateInput: ValidateInput).WithCountdown(PNBuffCountdown.BottomRight));
    }
    // Apply input: nothing, or `{duration}` in seconds (> 0).
    public static void ValidateInput(JToken input)
    {
        if (input == null || input.Type == JTokenType.Null) return;
        var obj = PNBuffContentBatch.Object(input); PNBuffContentBatch.Fields(obj, "duration");
        if (obj["duration"] != null) PNBuffCollection.Seconds(PNBuffContentBatch.Number(obj, "duration"), false);
    }
}

public abstract class PNLoricaTimedBehavior : PNBuffTimedBehavior
{
    public override bool EventDrivenReplication => true;
    protected PNLoricaTimedBehavior(JToken data) : base(data) { }
    protected static void ApplyInputDuration(PNBuffContext context, JToken input)
    {
        if (input is JObject obj && obj["duration"] != null) context.SetDuration(PNBuffContentBatch.Number(obj, "duration"));
    }
    // The base re-reads the registered duration when the behavior starts, so the input duration is applied again afterwards.
    public override void OnBehaviorStarted(PNBuffContext context, bool migrated)
    {
        base.OnBehaviorStarted(context, migrated);
        if (!migrated) ApplyInputDuration(context, context.InitialInput);
    }
    public override bool OnReapply(PNBuffContext context, PNBuffApplyRequest request)
    { ApplyInputDuration(context, request.Input); return true; }
}

public sealed class PNLoricaBreakBehavior : PNLoricaTimedBehavior
{
    public PNLoricaBreakBehavior(JToken data) : base(data) { }
    public override void OnApply(PNBuffContext context, JToken input)
    {
        base.OnApply(context, input); ApplyInputDuration(context, input); OnResume(context);
        if (PNBuffContentBatch.Bool(Data, "interrupt")) context.TryInterrupt(context.Snapshot.Id);
    }
    public override void OnResume(PNBuffContext context) => context.Lock(PNBuffBuiltinSettings.Ids(Data, "permissionIds"));
    public override void OnEnding(PNBuffContext context, PNBuffEndReason reason, PNBuffSnapshot finalState)
    {
        // Only a break that ran its course (or was removed on purpose) refills; death, unload and session end leave the stat alone.
        if (reason == PNBuffEndReason.Death || reason == PNBuffEndReason.Unload || reason == PNBuffEndReason.SessionEnded || reason == PNBuffEndReason.RuntimeError) return;
        (context.Services.Target as PNEnemyStat)?.FinishLoricaBreak();
    }
}

public sealed class PNLoricaRecoveryBehavior : PNLoricaTimedBehavior
{
    private IDisposable lease; private PNBuffResourceScope leaseScope;
    public PNLoricaRecoveryBehavior(JToken data) : base(data) { }
    public override void OnApply(PNBuffContext context, JToken input) { base.OnApply(context, input); ApplyInputDuration(context, input); OnResume(context); }
    public override void OnResume(PNBuffContext context)
    {
        if (lease != null && ReferenceEquals(leaseScope, context.Resources) && !leaseScope.IsDisposed) return;
        var stat = context.Services.Target as PNEnemyStat;
        if (stat == null) throw new InvalidOperationException("Lorica recovery needs an enemy target.");
        lease = context.Resources.Own(stat.LockLorica("buff:" + context.Handle.InstanceId)); leaseScope = context.Resources;
    }
    public override void OnCleanup(PNBuffContext context) { lease?.Dispose(); lease = null; leaseScope = null; }
}
