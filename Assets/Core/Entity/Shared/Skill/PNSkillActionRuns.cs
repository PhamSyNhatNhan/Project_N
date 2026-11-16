using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

public sealed partial class PNSkillInvocation
{
    private long actionOrdinal;
    internal long NextActionOrdinal() => actionOrdinal == long.MaxValue ? 0 : ++actionOrdinal;
}

public partial class PNSkill
{
    private static readonly ConditionalWeakTable<EveSessionContext, PNActionRunBudget> actionBudgets = new ConditionalWeakTable<EveSessionContext, PNActionRunBudget>();
    private PNActionRunStore actionRuns;
    private Guid actionEpoch, actionWorld;
    private ulong actionIncarnation;
    private long actionGeneration;
    public PNActionRunStore ActionRuns => actionRuns;
    public IReadOnlyList<PNActionRunRecord> CaptureActionRuns(Guid epoch, Guid world, ulong incarnation, long generation, double stamp)
    {
        if (actionRuns != null && (epoch != actionEpoch || world != actionWorld || incarnation != actionIncarnation || generation != actionGeneration)) ReleaseActionRuns();
        return actionRuns?.Snapshot(stamp) ?? Array.Empty<PNActionRunRecord>();
    }
    public void SealActionRuns(double stamp) => actionRuns?.CaptureHolds(stamp);
    public long ActionIdentityRejected { get; private set; }
    public long ActionCapabilityRejected { get; private set; }
    private PNActionExecutionFrame actionFrame;

    internal bool ActionStamp(out double stamp)
    {
        stamp = 0;
        if (!EveRegistrySession.IsReady || !EveRegistrySession.Context.HasAuthority) return false;
        try { stamp = PNTimeService.For(EveRegistrySession.Context).Sample().Local; return true; }
        catch (InvalidOperationException) { return false; } // standalone/custom hosts must attach the explicit timeline
    }
    internal void BeginActionRun(PNSkillActionContext context)
    {
        if (context.Mode != PNSkillMode.Authority || context.Invocation == null || context.Data == null || !ActionStamp(out var stamp)) return;
        if (context.ActionOrdinal == 0 || context.Invocation.CastKey < 0 || context.Invocation.Id <= 0) { ActionIdentityRejected++; return; }
        var definition = context.Registry.Actions().Get(context.Data.Action.Id);
        // Only the audited planar direction payload is supported in this stage.
        if (!definition.ObserverReplay) return;
        if (!definition.DriverProgress || (definition.RequiredContext & ~PNSkillContextRequirements.Direction) != 0 || !context.Direction.HasValue || context.Targets.Count != 0 || context.Position.HasValue) { ActionCapabilityRejected++; return; }
        var session = EveRegistrySession.Context;
        var load = GetComponent<EveEntityLoader>()?.LoadContext;
        if (load == null) return;
        var sample = PNTimeService.For(session).Sample(); var world = load.Identity.World;
        var worldId = world?.InstanceId ?? sample.Epoch; var incarnation = world?.Incarnation ?? 1;
        if (actionRuns == null || actionEpoch != sample.Epoch || actionWorld != worldId || actionIncarnation != incarnation || actionGeneration != load.Generation)
        {
            ReleaseActionRuns(); actionEpoch = sample.Epoch; actionWorld = worldId; actionIncarnation = incarnation; actionGeneration = load.Generation;
            actionRuns = new PNActionRunStore(actionBudgets.GetValue(session, owner =>
            {
                var budget = new PNActionRunBudget();
                owner.RegisterCleanup(() => { budget.Dispose(); actionBudgets.Remove(owner); });
                return budget;
            }));
        }
        var key = new PNActionRunKey(context.Invocation.CastKey != 0 ? context.Invocation.CastKey : -context.Invocation.Id, context.ActionOrdinal);
        if (actionFrame != null) stamp = actionFrame.Stamp;
        var direction = context.Direction.Value;
        if (actionRuns.Admit(key, context.Registry.Skills.ResolveIdentity(context.EntrySkillId).ToString(), context.Registry.Skills.ResolveIdentity(context.SkillId).ToString(), definition.Id.ToString(), direction.x, direction.z, stamp))
        { context.RunStore = actionRuns; context.RunKey = key; context.RunStamp = stamp; }
    }
    internal void EndActionRun(PNSkillActionContext context, bool cancelled)
    {
        if (context.RunStore == null || !ActionStamp(out var stamp)) return;
        if (actionFrame != null) stamp = actionFrame.Stamp;
        context.RunStore.Finish(context.RunKey, Math.Max(stamp, context.RunStamp), cancelled ? PNActionRunEnd.Cancelled : PNActionRunEnd.Completed);
    }
    private void ReleaseActionRuns() { actionRuns?.Dispose(); actionRuns = null; }

    // Driver API: supply gameplay delta already selected by the caller. The frame maps
    // consumed driver seconds to the one unscaled source; it never reads attack stats.
    public PNActionExecutionFrame BeginActionExecutionFrame(float delta)
    {
        if (actionFrame != null) throw new InvalidOperationException("Nested Action execution frame.");
        double stamp = 0, raw = 0;
        ActionStamp(out stamp);
#if UNITY_5_3_OR_NEWER
        raw = Time.unscaledDeltaTime;
#endif
        return actionFrame = new PNActionExecutionFrame(this, delta, raw, stamp);
    }
    public sealed class PNActionExecutionFrame : IDisposable
    {
        private readonly PNSkill skill;
        private readonly double delta, raw, end;
        private double consumed;
        internal double Stamp { get; private set; }
        internal PNActionExecutionFrame(PNSkill skill, double delta, double raw, double end)
        { PNTimeMath.Nonnegative(delta); this.skill = skill; this.delta = delta; this.raw = raw; this.end = end; Stamp = Math.Max(0, end - raw); }
        // Called for every phase, including phases without an Action. Callback receives
        // exactly the old gameplay step. Holds use Hold; no outside timer advances gameplay.
        public void Advance(PNSkillActionContext context, float step, double speed, Action<float> advance)
        {
            PNTimeMath.Nonnegative(step); if (speed <= 0 || double.IsInfinity(speed) || double.IsNaN(speed)) throw new ArgumentException(nameof(speed));
            consumed += step / speed;
            double next = delta > 0 ? Math.Min(end, Math.Max(0, end - raw) + raw * Math.Min(1, consumed / delta)) : end;
            Record(context, next, step, raw > 0 ? delta / raw * speed : 0);
            Stamp = next; advance?.Invoke(step);
        }
        public void Hold(PNSkillActionContext context) { Stamp = end; Record(context, end, 0, 0); }
        private static void Record(PNSkillActionContext context, double stamp, double step, double rate)
        {
            if (context?.RunStore == null) return;
            stamp = Math.Max(stamp, context.RunStamp);
            context.RunProgress += step; context.RunStamp = stamp;
            context.RunStore.Advance(context.RunKey, stamp, step, rate);
        }
        public void Dispose() { if (ReferenceEquals(skill.actionFrame, this)) skill.actionFrame = null; }
    }
}
