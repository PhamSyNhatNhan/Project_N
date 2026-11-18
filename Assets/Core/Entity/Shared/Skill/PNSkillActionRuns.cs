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
    private static readonly ConditionalWeakTable<EveSessionContext,PNActionPublicationBudget> publicationBudgets=new ConditionalWeakTable<EveSessionContext,PNActionPublicationBudget>();
    private PNActionPublication actionPublication;
    private PNActionPublicationRecovery publicationRecovery;
    public PNActionPublicationRecovery ActionPublicationRecovery => publicationRecovery;
    public PNActionRecoveryState RecoverActionPublication(double stamp, double originalDeadline)
    {
        if(!ActionPublicationEnabled || publicationRecovery==null) return PNActionRecoveryState.Failed;
        if(actionFrame!=null) throw new InvalidOperationException("Recover after the execution frame is disposed.");
        var old=actionPublication;
        var result=publicationRecovery.Recover(ref actionPublication,stamp,originalDeadline);
        if(old!=null&&old.Disposed)publications.Remove(old);
        if(result==PNActionRecoveryState.Recovered)publications.Add(actionPublication);
        return result;
    }
    public PNActionPublication ActionPublication => actionPublication;
    // Engine-owned opt-in shadow. No character or Action network branches.
    private static bool publicationEnabled;
    private static readonly HashSet<PNActionPublication> publications=new HashSet<PNActionPublication>();
    public static bool ActionPublicationEnabled { get=>publicationEnabled; set {publicationEnabled=value;if(!value){foreach(var source in publications)source.Dispose();publications.Clear();}} }
    public long ActionPublicationErrors { get; private set; }
    #if UNITY_5_3_OR_NEWER
    private static readonly Unity.Profiling.ProfilerMarker publicationMarker=new Unity.Profiling.ProfilerMarker("PN.ActionPublication");
#endif
    private void Publish(Action action) {
#if UNITY_5_3_OR_NEWER
        using(publicationMarker.Auto())
#endif
        try { action(); } catch (Exception) { ActionPublicationErrors++; if(actionPublication!=null){publications.Remove(actionPublication);actionPublication.Dispose();} } }
    private void TrackAuthority(Action action) { try { action(); } catch(Exception) { publicationRecovery?.Invalidate();ActionPublicationErrors++; } }
    private Guid actionEpoch, actionWorld;
    private ulong actionIncarnation;
    private long actionGeneration;
    public PNActionRunStore ActionRuns => actionRuns;
    public IReadOnlyList<PNActionRunRecord> CaptureActionRuns(Guid epoch, Guid world, ulong incarnation, long generation, double stamp)
    {
        if (actionRuns != null && (epoch != actionEpoch || world != actionWorld || incarnation != actionIncarnation || generation != actionGeneration)) ReleaseActionRuns();
        return actionRuns?.Snapshot(stamp) ?? Array.Empty<PNActionRunRecord>();
    }
    public void SealActionRuns(double stamp) { actionRuns?.CaptureHolds(stamp); publicationRecovery?.Seal(stamp); if(ActionPublicationEnabled && actionPublication!=null)Publish(()=>actionPublication.Seal(stamp)); }
    public long ActionIdentityRejected { get; private set; }
    public long ActionCapabilityRejected { get; private set; }
    private PNActionExecutionFrame actionFrame;
    private double previousActionFrameEnd;
    private double currentActionFrameStart;
    private int previousActionFrameNumber = -2;

    internal bool ActionStamp(out double stamp)
    {
        stamp = 0;
        if (!EveRegistrySession.IsReady || !EveRegistrySession.Context.HasAuthority) return false;
        try { stamp = PNTimeService.For(EveRegistrySession.Context).Sample().Local; return true; }
        catch (InvalidOperationException) { return false; } // standalone/custom hosts must attach the explicit timeline
    }
    private PNActionPublicationBudget PublicationBudget(EveSessionContext session)
        =>publicationBudgets.GetValue(session,owner=>{var budget=new PNActionPublicationBudget();owner.RegisterCleanup(()=>{foreach(var source in budget.Sources)publications.Remove(source);budget.Dispose();publicationBudgets.Remove(owner);});return budget;});
    private void EnsureActionScope(EveSessionContext session,Guid epoch,Guid world,ulong incarnation,long generation)
    {
        if (actionRuns == null || actionEpoch != epoch || actionWorld != world || actionIncarnation != incarnation || actionGeneration != generation)
        {
            ReleaseActionRuns(); actionEpoch = epoch; actionWorld = world; actionIncarnation = incarnation; actionGeneration = generation;
            actionRuns = new PNActionRunStore(actionBudgets.GetValue(session, owner =>
            {
                var budget = new PNActionRunBudget();
                owner.RegisterCleanup(() => { budget.Dispose(); actionBudgets.Remove(owner); });
                return budget;
            }));
        }
    }
    public PNActionRecoveryState PrepareActionPublication(double stamp,double originalDeadline)
    {
        if(!ActionPublicationEnabled||!EveRegistrySession.IsReady||!EveRegistrySession.Context.HasAuthority)return PNActionRecoveryState.Failed;
        if(actionFrame!=null)throw new InvalidOperationException("Prepare after drivers");
        var session=EveRegistrySession.Context;var load=GetComponent<EveEntityLoader>()?.LoadContext;
        if(load==null)return PNActionRecoveryState.Pending;
        var sample=PNTimeService.For(session).Sample();var world=load.Identity.World;
        EnsureActionScope(session,sample.Epoch,world?.InstanceId??sample.Epoch,world?.Incarnation??1,load.Generation);
        if(publicationRecovery==null)publicationRecovery=new PNActionPublicationRecovery(PublicationBudget(session));
        return RecoverActionPublication(stamp,originalDeadline);
    }
    internal void BeginActionRun(PNSkillActionContext context, bool commitPresentation = false)
    {
        if (context.Mode != PNSkillMode.Authority || context.Invocation == null || context.Data == null || !ActionStamp(out var stamp)) return;
        if (context.ActionOrdinal == 0 || context.Invocation.CastKey < 0 || context.Invocation.Id <= 0) { ActionIdentityRejected++; return; }
        var definition = context.Registry.Actions().Get(context.Data.Action.Id);
        // Only the audited planar direction payload is supported in this stage.
        if (!definition.ObserverReplay) return;
        if (!definition.DriverProgress || (definition.RequiredContext & ~PNSkillContextRequirements.Direction) != 0 || !context.Direction.HasValue || context.Targets.Count != 0 || context.Position.HasValue) { ActionCapabilityRejected++; return; }
        if (definition.PresentationScaleTag.HasValue && !commitPresentation)
        {
            context.PendingRunStart = actionFrame?.Stamp ?? stamp;
            return;
        }
        if (commitPresentation && !context.PendingRunStart.HasValue) return;
        var session = EveRegistrySession.Context;
        var load = GetComponent<EveEntityLoader>()?.LoadContext;
        if (load == null) return;
        var sample = PNTimeService.For(session).Sample(); var world = load.Identity.World;
        var worldId = world?.InstanceId ?? sample.Epoch; var incarnation = world?.Incarnation ?? 1;
        EnsureActionScope(session,sample.Epoch,worldId,incarnation,load.Generation);
        var key = new PNActionRunKey(context.Invocation.CastKey != 0 ? context.Invocation.CastKey : -context.Invocation.Id, context.ActionOrdinal);
        if (actionFrame != null) stamp = actionFrame.Stamp;
        if (context.PendingRunStart.HasValue)
        {
            stamp = context.PendingRunStart.Value;
            context.PendingRunStart = null;
        }
        var direction = context.Direction.Value;
        var shared=PublicationBudget(session);
        if(publicationRecovery==null)publicationRecovery=new PNActionPublicationRecovery(shared);
        var entry=context.Registry.Skills.ResolveIdentity(context.EntrySkillId).ToString();
        var data=context.Registry.Skills.ResolveIdentity(context.SkillId).ToString();
        var action=definition.Id.ToString();
        TrackAuthority(()=>publicationRecovery.Begin(key,entry,data,action,direction.x,direction.z,stamp,context.PresentationScale));
        // Initial source only at the start of a complete census. Mid-life enable
        // requires the explicit safe-cut recovery API, never an empty new source.
        if(ActionPublicationEnabled && actionPublication==null && publicationRecovery.Complete && publicationRecovery.Count==1) {actionPublication=new PNActionPublication(sessionBudget:shared);if(actionPublication.Complete)publications.Add(actionPublication);}
        if(ActionPublicationEnabled && actionPublication!=null) Publish(()=>actionPublication.Begin(key,entry,data,action,direction.x,direction.z,stamp,context.PresentationScale));
        actionRuns.Admit(key, context.Registry.Skills.ResolveIdentity(context.EntrySkillId).ToString(), context.Registry.Skills.ResolveIdentity(context.SkillId).ToString(), definition.Id.ToString(), direction.x, direction.z, stamp);
        // Keep observational driver/finish boundaries even for refused runs. Store
        // mutation still requires admission; this does not publish refused rows.
        context.RunStore = actionRuns; context.RunKey = key; context.RunStamp = stamp;
    }
    internal void EndActionRun(PNSkillActionContext context, bool cancelled)
    {
        context.PendingRunStart = null;
        if (context.RunStore == null || !ReferenceEquals(context.RunStore,actionRuns)) return;
        publicationRecovery?.End(context.RunKey);
        if(!ActionStamp(out var stamp))return;
        if (actionFrame != null) stamp = actionFrame.Stamp;
        if(ActionPublicationEnabled && actionPublication!=null)Publish(()=>actionPublication.End(context.RunKey,Math.Max(stamp,context.RunStamp),cancelled?PNActionRunEnd.Cancelled:PNActionRunEnd.Completed));
        context.RunStore.Finish(context.RunKey, Math.Max(stamp, context.RunStamp), cancelled ? PNActionRunEnd.Cancelled : PNActionRunEnd.Completed);
    }
    private void ReleaseActionRuns() { previousActionFrameNumber=-2;currentActionFrameStart=previousActionFrameEnd=0;if(actionPublication!=null)publications.Remove(actionPublication);actionPublication?.Dispose();actionPublication=null; publicationRecovery?.Dispose();publicationRecovery=null; actionRuns?.Dispose(); actionRuns = null; }

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
        double begin = Math.Max(0, stamp - raw);
#if UNITY_5_3_OR_NEWER
        // Adjacent frames share the exact source boundary. Float unscaledDeltaTime
        // is not an exact difference of double source stamps.
        // Parallel Actions execute separate frames in the same Unity frame. They
        // all share its exact start; recomputing start from the float delta for
        // the second Action manufactured sub-nanosecond hold/resume boundaries.
        if (previousActionFrameNumber == Time.frameCount)
            begin = currentActionFrameStart;
        else
        {
            if (previousActionFrameNumber == Time.frameCount - 1 && stamp >= previousActionFrameEnd)
                begin = previousActionFrameEnd;
            currentActionFrameStart = begin;
            previousActionFrameNumber = Time.frameCount;
            previousActionFrameEnd = stamp;
        }
#endif
        return actionFrame = new PNActionExecutionFrame(this, delta, raw, stamp, begin);
    }
    public sealed class PNActionExecutionFrame : IDisposable
    {
        private readonly PNSkill skill;
        private readonly double delta, raw, end, start;
        private double consumed;
        internal double Stamp { get; private set; }
        internal PNActionExecutionFrame(PNSkill skill, double delta, double raw, double end)
            : this(skill, delta, raw, end, Math.Max(0, end - raw)) { }
        internal PNActionExecutionFrame(PNSkill skill, double delta, double raw, double end, double start)
        { PNTimeMath.Nonnegative(delta); this.skill = skill; this.delta = delta; this.raw = raw; this.end = end; this.start = start; Stamp = start; }
        // Called for every phase, including phases without an Action. Callback receives
        // exactly the old gameplay step. Holds use Hold; no outside timer advances gameplay.
        public void Advance(PNSkillActionContext context, float step, double speed, Action<float> advance)
        {
            PNTimeMath.Nonnegative(step); if (speed <= 0 || double.IsInfinity(speed) || double.IsNaN(speed)) throw new ArgumentException(nameof(speed));
            double begin = Stamp;
            // Exhaustion is expressed in the same float step vocabulary as the
            // gameplay caller; only the timestamp mapping consumes the exact tail.
            bool exhausts = delta > consumed && step == (float)((delta - consumed) * speed);
            consumed = exhausts ? delta : consumed + step / speed;
            double next = delta > 0 ? Math.Min(end, start + (end - start) * Math.Min(1, consumed / delta)) : end;
            Record(context, begin, next, step, raw > 0 ? delta / raw * speed : 0);
            Stamp = next; advance?.Invoke(step);
        }
        public void Hold(PNSkillActionContext context) { var begin = Stamp; Stamp = end; Record(context, begin, end, 0, 0); }
        private static void Record(PNSkillActionContext context, double begin, double stamp, double step, double rate)
        {
            if (context?.RunStore == null || !ReferenceEquals(context.RunStore,context.Skill?.actionRuns)) return;
            stamp = Math.Max(stamp, context.RunStamp);
            context.RunProgress += step; context.RunStamp = stamp;
            if(context.Skill?.publicationRecovery!=null)context.Skill.TrackAuthority(()=>context.Skill.publicationRecovery.Advance(context.RunKey,stamp,step,rate));
            if(ActionPublicationEnabled && context.Skill?.actionPublication!=null)context.Skill.Publish(()=>context.Skill.actionPublication.Advance(context.RunKey,Math.Min(begin,stamp),stamp,step,rate));
            context.RunStore.AdvanceInterval(context.RunKey, Math.Min(begin, stamp), stamp, step, rate);
        }
        public void Dispose() { if (ReferenceEquals(skill.actionFrame, this)) skill.actionFrame = null; }
    }
}
