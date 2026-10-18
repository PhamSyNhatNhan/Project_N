using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

internal sealed class PNBuffSchedule
{
    internal string Key;
    internal double At;
    internal double? Period;
    internal long Order;
}
public sealed class PNBuffInstance
{
    public PNBuffHandle Handle { get; internal set; }
    public EveId Id { get; internal set; }
    public PNBuffSource Source { get; internal set; }
    internal PNBuffDefinition Original, Profile;
    internal PNBuffBehavior Behavior;
    internal PNBuffResourceScope Resources;
    internal PNBuffContext Context;
    internal readonly HashSet<EveId> Tags = new HashSet<EveId>();
    internal readonly Dictionary<string, PNBuffSchedule> Events = new Dictionary<string, PNBuffSchedule>();
    internal double Time;
    internal double? Expiration;
    internal PNBuffClock Clock;
    internal JToken InitialInput;
    internal bool Ending, Suspended;
    internal long EventOrder;
    internal long StatSourceSequence;
    internal long Revision;
    internal bool PreparingTransition, PendingBehaviorStart, PendingMigrated;
    internal bool CleanupCalled, CleanupSucceeded;
    internal Exception Failure;
}

public sealed class PNBuffContext
{
    private readonly PNBuffInstance instance;
    public PNBuffCollection Collection { get; }
    public PNBuffHandle Handle => instance.Handle;
    public PNBuffSource Source => instance.Source;
    public JToken InitialInput => instance.InitialInput?.DeepClone();
    public PNBuffServices Services => Collection.Services;
    public PNBuffResourceScope Resources => instance.Resources;
    public EveRegistryHub Registry => Collection.Registry;
    public double Time => instance.Time;
    public bool IsEnding => instance.Ending;
    public bool IsSuspended => instance.Suspended;
    public void EndingCue(string name, JToken payload = null)
    { if (!IsEnding) throw new InvalidOperationException("Ending cue requires OnEnding."); Collection.PublishCue(Handle, name, true, payload); }
    public PNBuffSnapshot Snapshot => new PNBuffSnapshot(instance);
    internal PNBuffContext(PNBuffCollection collection, PNBuffInstance instance) { Collection = collection; this.instance = instance; }
    private void RequireActive() { if (instance.Ending || instance.PreparingTransition || !Handle.IsAlive) throw new InvalidOperationException("Buff behavior is inactive or preparing a migration."); }
    private void RequireEffects() { RequireActive(); if (instance.Suspended) throw new InvalidOperationException("Suspended Buff cannot grant effects."); }
    public void SetClock(PNBuffClock clock)
    { RequireActive(); if (!Enum.IsDefined(typeof(PNBuffClock), clock)) throw new ArgumentException("Unknown clock."); if (instance.Clock != clock) { instance.Revision++; Collection.ReplicationChanged(PNBuffCollection.DiagnosticCause.Clock); } instance.Clock = clock; }
    public void SetDuration(double? seconds)
    {
        RequireActive(); if (seconds.HasValue) PNBuffCollection.Seconds(seconds.Value, false);
        Collection.ReplicationChanged(PNBuffCollection.DiagnosticCause.Expiration); instance.Expiration = seconds.HasValue ? PNBuffCollection.Sum(Time, seconds.Value) : (double?)null;
    }
    public void Schedule(string key, double delay, double? period = null)
    {
        RequireActive(); if (string.IsNullOrEmpty(key)) throw new ArgumentException("Schedule key required.");
        PNBuffCollection.Seconds(delay, false); if (period.HasValue) PNBuffCollection.Seconds(period.Value, false);
        instance.Events[key] = new PNBuffSchedule { Key = key, At = PNBuffCollection.Sum(Time, delay), Period = period, Order = ++instance.EventOrder };
        if (instance.Events[key].At <= Time) { instance.Events.Remove(key); throw new ArgumentException("Schedule delay is below current clock precision."); }
    }
    public void CancelSchedule(string key) { RequireActive(); instance.Events.Remove(key); }
    public PNBuffReceipt End(PNBuffEndReason reason = PNBuffEndReason.Expired) => Collection.Remove(Handle, reason, true);
    public IReadOnlyList<PNBuffContribution> Contributions(Type contract) => Collection.Contributions(instance, contract);
    // Receiver explicitly chooses addition. Other implementations may use their own reduction.
    public double Additive(Type contract, string field, double baseValue)
    {
        double result = baseValue;
        foreach (var value in Contributions(contract).OfType<PNBuffNumberContribution>().Where(x => x.Field == field)) result += value.Value;
        if (double.IsNaN(result) || double.IsInfinity(result)) throw new OverflowException("Buff contribution overflow."); return result;
    }
    public PNBuffContributionLease Contribute(PNBuffContribution contribution, EveId? matchingId = null,
        PNBuffSelection selection = PNBuffSelection.CurrentAndFuture, PNBuffHandle? exact = null)
    { RequireEffects(); return Resources.Own(Collection.Contribute("buff:" + Handle.InstanceId, contribution, matchingId, selection, exact)); }
    public PNBuffImmunityLease Immune(IEnumerable<EveId> ids = null, IEnumerable<EveId> tags = null, bool purge = false)
    { RequireEffects(); return Resources.Own(Collection.Immune(ids, tags, purge)); }
    public PNControlLease Lock(IEnumerable<EveId> permissions, IEnumerable<EveId> skills = null)
    {
        RequireEffects(); if (Services.Controls == null) throw new InvalidOperationException("Buff target has no controls.");
        return Resources.Own(Services.Controls.Acquire("buff:" + Handle.InstanceId, permissions, skills));
    }
    public bool TryInterrupt(EveId reason) { RequireEffects(); return Services.Interrupt?.Invoke(reason) ?? false; }
    public PNSkillContributionLease ModifySkill(PNSkillContribution contribution)
    {
        RequireEffects(); var skill = Services.Target != null ? Services.Target.GetComponent<PNSkill>() : null;
        if (skill == null || !skill.IsReady || skill.Effects == null) throw new InvalidOperationException("Target has no loaded Skill effect state.");
        return Resources.Own(skill.Effects.Contribute("buff:" + Handle.InstanceId, contribution));
    }
    public IDisposable ModifyStats(IEnumerable<EveStatModifier> modifiers)
    {
        RequireEffects(); if (Services.Target == null) throw new InvalidOperationException("Buff target has no stats.");
        var target = Services.Target;
        var sequence = checked(++instance.StatSourceSequence);
        var key = EveId.Parse("project_n:buff_source_" + Handle.InstanceId.ToString("D20", System.Globalization.CultureInfo.InvariantCulture)
            + "_" + sequence.ToString("D20", System.Globalization.CultureInfo.InvariantCulture));
        target.SetBuffSource(key, modifiers);
        var scope = Resources; PNBuffRelease release = null;
        release = new PNBuffRelease(() => { try { if (target != null && target.IsReady) target.RemoveSource(key); } finally { scope.Forget(release); } });
        return scope.Own(release);
    }
    public PNDamageResult Damage(PNDamage packet)
    { if (Services.Target == null) throw new InvalidOperationException("No combat receiver."); return Services.Target.ReceiveDamage(packet); }
    public PNHealResult Heal(PNHeal packet)
    { if (Services.Target == null) throw new InvalidOperationException("No heal receiver."); return Services.Target.ReceiveHeal(packet); }
}

public sealed class PNBuffServices
{
    public PNStat Target { get; }
    public PNControlState Controls { get; }
    public Func<EveId, bool> Interrupt { get; }
    public PNBuffServices(PNStat target = null, PNControlState controls = null, Func<EveId, bool> interrupt = null)
    { Target = target; Controls = controls; Interrupt = interrupt; }
}

public sealed partial class PNBuffCollection : IDisposable, PNIBuffPresentationSource
{
    private readonly Dictionary<long, PNBuffInstance> instances = new Dictionary<long, PNBuffInstance>();
    // Ordering index only: predicates are evaluated at each snapshot boundary.
    private readonly List<PNBuffInstance> orderedInstances = new List<PNBuffInstance>();
    private readonly List<PNBuffContributionLease> contributions = new List<PNBuffContributionLease>();
    private readonly List<PNBuffImmunityLease> immunities = new List<PNBuffImmunityLease>();
    private readonly Func<bool> canMutate;
    private long nextId, nextLease;
    private bool advancing;
    internal double RemainingGameDelta { get; private set; }
    private double replicationGameTime;
    internal double ReplicationGameTime => replicationGameTime - RemainingGameDelta;
    internal long ReplicationRevision { get; private set; }
    [Flags] internal enum DiagnosticCause { None = 0, Unknown = 1, Visual = 2, Numbers = 4, Clock = 8, Expiration = 16, ScheduledStacks = 32 }
    private DiagnosticCause pendingDiagnosticCause;
    internal DiagnosticCause CurrentDiagnosticCause { get; private set; }
    internal void ReplicationChanged(DiagnosticCause cause = DiagnosticCause.Unknown)
    { ReplicationRevision = checked(ReplicationRevision + 1); if (EveHotPathMetrics.Active) pendingDiagnosticCause |= cause; }
    internal bool HasOnlyEventDrivenReplication
    { get { foreach (var value in instances.Values) if (!value.Ending && value.Behavior != null && !value.Behavior.EventDrivenReplication) return false; return true; } }
    private struct AdvanceEntry { internal PNBuffInstance Value; internal long Revision; internal double Start, Delta; }
    private readonly List<AdvanceEntry> advanceFrame = new List<AdvanceEntry>();
    private bool suspended;
    public bool IsDisposed { get; private set; }
    public EveRegistryHub Registry { get; }
    public Guid SessionId { get; }
    public PNBuffServices Services { get; }
    public PNBuffMutationCoordinator Coordinator { get; }
    public event Action Changed;
    public event Action<Exception> Failed;
    internal event Action<PNBuffHandle, PNBuffEndReason> Ended;
    internal event Action<PNBuffHandle, string, bool, JToken> VisualCue;
    internal PNBuffInstance[] ReplicationInstances() => OrderedInstanceSnapshot(true);
    private PNBuffInstance[] OrderedInstanceSnapshot(bool requireBehavior)
    {
        int count = 0;
        for (int i = 0; i < orderedInstances.Count; i++)
        {
            var value = orderedInstances[i];
            if (!value.Ending && (!requireBehavior || value.Behavior != null)) count++;
        }
        var snapshot = new PNBuffInstance[count];
        int next = 0;
        for (int i = 0; i < orderedInstances.Count; i++)
        {
            var value = orderedInstances[i];
            if (!value.Ending && (!requireBehavior || value.Behavior != null)) snapshot[next++] = value;
        }
        return snapshot;
    }
    private int OrderedInstanceIndex(long id)
    {
        int low = 0, high = orderedInstances.Count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            long current = orderedInstances[middle].Handle.InstanceId;
            if (current == id) return middle;
            if (current < id) low = middle + 1; else high = middle - 1;
        }
        throw new InvalidOperationException("Buff membership ordering index is inconsistent.");
    }
    internal void VisualPublished()
    { if (!EveHotPathMetrics.Active) { Coordinator.Submit(Notify); return; } Coordinator.Submit(() => { pendingDiagnosticCause |= DiagnosticCause.Visual; Notify(); }); }
    internal void PublishCue(PNBuffHandle handle, string name, bool guaranteed, JToken data)
    { if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Cue name required."); foreach (Action<PNBuffHandle, string, bool, JToken> f in VisualCue?.GetInvocationList() ?? Array.Empty<Delegate>()) try { f(handle, name, guaranteed, data); } catch (Exception e) { Report(e); } }
    public PNBuffCollection(EveRegistryHub registry, Guid sessionId, PNBuffMutationCoordinator coordinator = null,
        PNBuffServices services = null, Func<bool> canMutate = null)
    {
        if (registry == null || !registry.IsSealed) throw new ArgumentException("Sealed registry required.");
        if (sessionId == Guid.Empty) throw new ArgumentException("Session ID required.");
        Registry = registry; SessionId = sessionId; Coordinator = coordinator ?? new PNBuffMutationCoordinator();
        Services = services ?? new PNBuffServices(); this.canMutate = canMutate ?? (() => true);
        Coordinator.Failed += CoordinatorFailed;
    }
    private void RequireMutation() { if (IsDisposed) throw new ObjectDisposedException(nameof(PNBuffCollection)); if (!canMutate()) throw new InvalidOperationException("Buff mutation requires active authority."); }
    public bool Contains(PNBuffHandle handle) => !IsDisposed && ReferenceEquals(handle.Collection, this) && instances.TryGetValue(handle.InstanceId, out var value) && !value.Ending;
    public IReadOnlyList<PNBuffSnapshot> Snapshot() => Array.AsReadOnly(instances.Values.Where(x => !x.Ending).OrderBy(x => x.Handle.InstanceId).Select(x => new PNBuffSnapshot(x)).ToArray());
    public PNBuffSnapshot Get(PNBuffHandle handle) => Contains(handle) ? new PNBuffSnapshot(instances[handle.InstanceId]) : null;
    public PNBuffReceipt Apply(PNBuffApplyRequest request)
    {
        RequireMutation(); if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.Source.SessionId != SessionId) throw new ArgumentException("Buff source belongs to another session.");
        var profile = Registry.Buffs().GetEffective(request.BuffId); profile.ValidateInput(request.Input);
        var id = Registry.Buffs().ResolveIdentity(request.BuffId); var receipt = new PNBuffReceipt();
        Coordinator.Submit(() => Execute(receipt, () => ApplyCore(id, profile, request, receipt)), error => Abort(receipt, error)); return receipt;
    }
    private void ApplyCore(EveId id, PNBuffDefinition profile, PNBuffApplyRequest request, PNBuffReceipt receipt)
    {
        RequireMutation();
        if (suspended || Services.Target != null && Services.Target.IsReady && Services.Target.IsDead)
        { receipt.Status = PNBuffStatus.Rejected; receipt.Message = suspended ? "Target suspended" : "Target dead"; return; }
        var group = instances.Values.Where(x => !x.Ending && x.Id == id &&
            (profile.Scope == PNBuffInstanceScope.Target || x.Source.EntityId == request.Source.EntityId)).OrderBy(x => x.Handle.InstanceId).ToArray();
        // Candidate identity is reserved before mutation; future scopes participate before immunity/effects.
        var candidate = new PNBuffInstance { Handle = new PNBuffHandle(this, checked(++nextId)), Id = id, Original = profile, Profile = profile, Source = request.Source, InitialInput = request.Input, Clock = PNBuffClock.Game };
        var future = contributions.Where(x => x.IsActive && x.Exact == null && x.Selection != PNBuffSelection.Current && (!x.MatchId.HasValue || x.MatchId == id)).ToArray();
        foreach (var lease in future) lease.Targets.Add(candidate.Handle.InstanceId);
        try
        {
            candidate.Profile = SelectProfile(candidate); SetTags(candidate);
            if (Blocked(candidate, false)) { receipt.Status = PNBuffStatus.Rejected; receipt.Message = "Immune"; return; }
            int maximum = GroupLimit(profile, id);
            if (!profile.MultipleInstances && group.Length > 0 || group.Length >= maximum)
            {
                var current = group[0];
                current.Profile.ValidateInput(request.Input);
                if (Blocked(current, false)) { receipt.Status = PNBuffStatus.Rejected; receipt.Message = "Existing Buff is immune"; return; }
                bool accepted = false;
                Invoke(current, () => accepted = !profile.MultipleInstances ? current.Behavior.OnReapply(current.Context, request) : current.Behavior.OnFull(current.Context, request));
                receipt.Handle = current.Handle; receipt.Status = accepted && !current.Ending ? PNBuffStatus.Reapplied : PNBuffStatus.Rejected; Notify(); return;
            }
            candidate.Behavior = candidate.Profile.Create(); candidate.Resources = NewResources(); candidate.Context = new PNBuffContext(this, candidate);
            BindPresentation(candidate);
            var admissionReceiver = ReferenceEquals(candidate.Profile, candidate.Original) ? candidate.Behavior : candidate.Original.Create();
            foreach (var lease in future)
            {
                var contribution = lease.Contribution;
                if (!(contribution is PNBuffTagContribution) && !(contribution is PNBuffOverrideContribution) && contribution.Contract != typeof(PNBuffInstanceLimitContract) &&
                    !admissionReceiver.Supports(contribution.Contract) && !candidate.Behavior.Supports(contribution.Contract))
                    throw new ArgumentException("Future Buff does not support contract " + contribution.Contract.Name);
            }
            foreach (var contract in future.Select(x => x.Contribution.Contract).Distinct())
            {
                var proposed = Array.AsReadOnly(future.Where(x => x.Contribution.Contract == contract).Select(x => x.Contribution).ToArray());
                if (candidate.Behavior.Supports(contract)) candidate.Behavior.ValidateContributions(candidate.Context, contract, proposed);
                else if (admissionReceiver.Supports(contract)) admissionReceiver.ValidateContributions(candidate.Context, contract, proposed);
            }
            System.Diagnostics.Debug.Assert(orderedInstances.Count == 0 || orderedInstances[orderedInstances.Count - 1].Handle.InstanceId < candidate.Handle.InstanceId);
            // Reserve before Dictionary.Add: index insertion cannot allocate after membership commits.
            if (orderedInstances.Count == orderedInstances.Capacity)
                orderedInstances.Capacity = orderedInstances.Count == 0 ? 4 : checked(orderedInstances.Count * 2);
            instances.Add(candidate.Handle.InstanceId, candidate);
            orderedInstances.Add(candidate);
            receipt.Handle = candidate.Handle;
            Invoke(candidate, () => { candidate.Behavior.OnApply(candidate.Context, request.Input); if (!candidate.Ending) candidate.Behavior.OnContributionsChanged(candidate.Context); });
            receipt.Status = candidate.Ending ? PNBuffStatus.Failed : PNBuffStatus.Applied;
            if (candidate.Ending) { receipt.Error = candidate.Failure; receipt.Message = candidate.Failure?.Message; }
            Notify();
        }
        finally { if (!instances.ContainsKey(candidate.Handle.InstanceId)) foreach (var lease in future) lease.Targets.Remove(candidate.Handle.InstanceId); }
    }
    public PNBuffReceipt Trigger(PNBuffHandle handle) => MutateBehavior(handle, null);
    public PNBuffReceipt ChangeStacks(PNBuffHandle handle, int delta) => MutateBehavior(handle, delta);
    private PNBuffReceipt MutateBehavior(PNBuffHandle handle, int? delta)
    {
        RequireMutation(); var receipt = new PNBuffReceipt();
        Coordinator.Submit(() => Execute(receipt, () =>
        {
            RequireMutation();
            if (!Contains(handle)) { receipt.Status = PNBuffStatus.Rejected; receipt.Message = "Stale handle"; return; }
            var value = instances[handle.InstanceId];
            if (value.Suspended || Services.Target != null && Services.Target.IsReady && Services.Target.IsDead ||
                (delta.HasValue ? !value.Behavior.CanChangeStacks : !value.Behavior.CanTrigger))
            { receipt.Status = PNBuffStatus.Rejected; receipt.Message = "Behavior unavailable or unsupported"; return; }
            receipt.Handle = handle;
            Invoke(value, () => { if (delta.HasValue) value.Behavior.ChangeStacks(value.Context, delta.Value); else value.Behavior.Trigger(value.Context); });
            if (value.Failure != null) { receipt.Status = PNBuffStatus.Failed; receipt.Error = value.Failure; }
            else receipt.Status = delta.HasValue ? PNBuffStatus.StacksChanged : PNBuffStatus.Triggered;
            Notify();
        }), error => Abort(receipt, error)); return receipt;
    }
    public PNBuffReceipt Remove(PNBuffHandle handle, PNBuffEndReason reason = PNBuffEndReason.Dispelled, bool bypass = false)
    {
        RequireMutation(); if (!Enum.IsDefined(typeof(PNBuffEndReason), reason)) throw new ArgumentException("Unknown ending reason.");
        var receipt = new PNBuffReceipt();
        Coordinator.Submit(() => Execute(receipt, () =>
        {
            RequireMutation();
            if (!Contains(handle)) { receipt.Status = PNBuffStatus.Rejected; receipt.Message = "Stale handle"; return; }
            var value = instances[handle.InstanceId];
            if (!bypass && !value.Profile.CanDispel) { receipt.Status = PNBuffStatus.Rejected; receipt.Message = "Cannot dispel"; return; }
            EndCore(value, reason); receipt.Handle = handle; receipt.Status = PNBuffStatus.Removed;
        }), error => Abort(receipt, error)); return receipt;
    }
    public void RemoveMatching(Func<PNBuffSnapshot, bool> predicate, PNBuffEndReason reason, bool bypass = false)
    { RequireMutation(); if (predicate == null) throw new ArgumentNullException(nameof(predicate)); foreach (var value in Snapshot().Where(predicate)) Remove(value.Handle, reason, bypass); }
    // Exact resource-owner release is lifecycle work, even while gameplay is held.
    // It cannot apply/trigger a Buff or select instances by ID.
    public PNBuffReceipt ReleaseOwned(PNBuffHandle handle)
    {
        void RequireCleanup()
        {
            var context=Services.Target?.GetComponent<EveEntityLoader>()?.LoadContext;
            if(IsDisposed||!canMutate()&&(context==null||!context.IsValid||!context.Session.HasAuthority))throw new InvalidOperationException("Buff resource release requires its authoritative lifetime.");
        }
        RequireCleanup();var receipt=new PNBuffReceipt();
        Coordinator.Submit(()=>Execute(receipt,()=>
        {
            RequireCleanup();if(!Contains(handle)){receipt.Status=PNBuffStatus.Rejected;return;}
            EndCore(instances[handle.InstanceId],PNBuffEndReason.Bypass);receipt.Handle=handle;receipt.Status=PNBuffStatus.Removed;
        }),error=>Abort(receipt,error));return receipt;
    }
    private PNBuffResourceScope NewResources() => new PNBuffResourceScope(Report);
    private void EndCore(PNBuffInstance value, PNBuffEndReason reason)
    {
        if (value.Ending) return; value.Ending = true;
        try { var final = new PNBuffSnapshot(value); value.Behavior?.OnEnding(value.Context, reason, final); } catch (Exception e) { Report(e); }
        finally
        {
            Cleanup(value); value.Events.Clear();
            int orderedIndex = OrderedInstanceIndex(value.Handle.InstanceId);
            instances.Remove(value.Handle.InstanceId); orderedInstances.RemoveAt(orderedIndex);
            foreach (var lease in contributions) lease.Targets.Remove(value.Handle.InstanceId);
            foreach (Action<PNBuffHandle, PNBuffEndReason> f in Ended?.GetInvocationList() ?? Array.Empty<Delegate>()) try { f(value.Handle, reason); } catch (Exception e) { Report(e); }
            Notify();
        }
    }
    private bool Cleanup(PNBuffInstance value)
    {
        if (value.CleanupCalled) return value.CleanupSucceeded;
        value.CleanupCalled = true; value.CleanupSucceeded = true;
        try { value.Behavior?.OnCleanup(value.Context); } catch (Exception e) { value.CleanupSucceeded = false; Report(e); }
        finally { value.Resources?.Dispose(); if (value.Resources != null && value.Resources.CleanupFailed) value.CleanupSucceeded = false; }
        return value.CleanupSucceeded;
    }
    // D5 (RulesVersion 71): ends one instance whose StackCount failed again on its publisher retry. Mirrors the Invoke failure
    // path (Failure + EndCore(RuntimeError)); the publisher already reported the exception. Revalidated when the command runs.
    internal void EndAfterReplicationFailure(long instanceId, PNBuffBehavior behavior, Exception error, Action<PNBuffReplicationEndOutcome> completed)
    {
        if (IsDisposed || !canMutate()) { completed(PNBuffReplicationEndOutcome.NotAllowed); return; }
        Coordinator.Submit(() =>
        {
            var outcome = PNBuffReplicationEndOutcome.Stale;
            try
            {
                if (IsDisposed || !canMutate()) outcome = PNBuffReplicationEndOutcome.NotAllowed;
                else if (instances.TryGetValue(instanceId, out var value) && !value.Ending && ReferenceEquals(value.Behavior, behavior))
                { value.Failure = error; outcome = PNBuffReplicationEndOutcome.Ended; EndCore(value, PNBuffEndReason.RuntimeError); }
            }
            finally { completed(outcome); }
        }, abort => completed(PNBuffReplicationEndOutcome.NotAllowed));
    }
    private void Invoke(PNBuffInstance value, Action action)
    { if (value.Ending) return; try { action(); } catch (Exception e) { value.Failure = e; Report(e); EndCore(value, PNBuffEndReason.RuntimeError); } }
    private void Execute(PNBuffReceipt receipt, Action action)
    { try { action(); } catch (Exception e) { receipt.Error = e; receipt.Message = e.Message; receipt.Status = PNBuffStatus.Failed; Report(e); } }
    private void Notify()
    {
        ReplicationChanged(DiagnosticCause.None);
        bool diagnostic = EveHotPathMetrics.Active;
        var prior = CurrentDiagnosticCause;
        if (diagnostic) { CurrentDiagnosticCause = pendingDiagnosticCause == DiagnosticCause.None ? DiagnosticCause.Unknown : pendingDiagnosticCause; pendingDiagnosticCause = DiagnosticCause.None; }
        try { SyncPresentation(); foreach (Action listener in Changed?.GetInvocationList() ?? Array.Empty<Delegate>()) try { listener(); } catch (Exception e) { if (diagnostic) pendingDiagnosticCause |= CurrentDiagnosticCause; Report(e); } }
        catch { if (diagnostic) pendingDiagnosticCause |= CurrentDiagnosticCause; throw; }
        finally { if (diagnostic) CurrentDiagnosticCause = prior; }
    }
    internal void Report(Exception error) { foreach (Action<Exception> listener in Failed?.GetInvocationList() ?? Array.Empty<Delegate>()) try { listener(error); } catch { } }
    private void Abort(PNBuffReceipt receipt, Exception error) { receipt.Status = PNBuffStatus.Failed; receipt.Error = error; receipt.Message = error.Message; }
    private void CoordinatorFailed(Exception error) { Report(error); if (Coordinator.IsFaulted) Dispose(PNBuffEndReason.RuntimeError); }

    public PNBuffContributionLease Contribute(string owner, PNBuffContribution contribution, EveId? matchingId = null,
        PNBuffSelection selection = PNBuffSelection.CurrentAndFuture, PNBuffHandle? exact = null)
    {
        RequireMutation(); if (contribution == null || string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Contribution and owner required.");
        if (!Enum.IsDefined(typeof(PNBuffSelection), selection)) throw new ArgumentException("Unknown selection.");
        var id = matchingId.HasValue ? Registry.Buffs().ResolveIdentity(matchingId.Value) : (EveId?)null;
        if (id.HasValue) Registry.Buffs().Get(id.Value);
        if (exact.HasValue && (!Contains(exact.Value) || matchingId.HasValue || selection != PNBuffSelection.Current)) throw new ArgumentException("Exact contribution requires live local handle and Current selection.");
        ValidateContribution(contribution);
        if (contribution.Contract == typeof(PNBuffInstanceLimitContract) && (exact.HasValue || selection != PNBuffSelection.CurrentAndFuture))
            throw new ArgumentException("Instance limits are group contracts; use a group scope with CurrentAndFuture.");
        var targets = instances.Values.Where(x => !x.Ending && (exact.HasValue ? x.Handle.Equals(exact.Value) : (!id.HasValue || id == x.Id))).ToArray();
        if (selection != PNBuffSelection.Future) foreach (var target in targets) ValidateGrant(target, contribution);
        var lease = new PNBuffContributionLease(this, ++nextLease, owner, contribution, id, selection, exact?.InstanceId);
        Coordinator.Submit(() =>
        {
            if (!lease.IsActive || IsDisposed) { lease.IsActive = false; return; }
            try
            {
                RequireMutation();
                var current = instances.Values.Where(x => !x.Ending && (lease.Exact.HasValue ? x.Handle.InstanceId == lease.Exact : !id.HasValue || x.Id == id)).ToArray();
                if (selection != PNBuffSelection.Future) foreach (var target in current) ValidateGrant(target, contribution);
                contributions.Add(lease);
                if (selection != PNBuffSelection.Future) foreach (var target in current) lease.Targets.Add(target.Handle.InstanceId);
                // Validate group arithmetic before changing any behavior or ending excess instances.
                foreach (var target in instances.Values) GroupLimit(target.Original, target.Id);
                RecomputeAll();
            }
            catch (Exception e) { lease.IsActive = false; contributions.Remove(lease); Report(e); }
        }, _ => lease.IsActive = false); return lease;
    }
    private void ValidateContribution(PNBuffContribution contribution)
    {
        if (contribution is PNBuffTagContribution tags) foreach (var id in tags.Add.Concat(tags.Suppress)) Registry.BuffTags().Get(id);
        if (contribution is PNBuffOverrideContribution replacement) Registry.Buffs().GetEffective(replacement.ProfileId).Validate(Registry);
        if (contribution is PNBuffNumberContribution number && number.Contract == typeof(PNBuffInstanceLimitContract) &&
            (number.Field != "maximum" || number.Value != Math.Truncate(number.Value))) throw new ArgumentException("Instance limit contribution must be integral maximum.");
    }
    private void ValidateContract(PNBuffInstance instance, PNBuffContribution contribution)
    {
        if (contribution is PNBuffTagContribution || contribution is PNBuffOverrideContribution || contribution.Contract == typeof(PNBuffInstanceLimitContract)) return;
        if (!instance.Behavior.Supports(contribution.Contract)) throw new ArgumentException("Buff does not support contract " + contribution.Contract.Name);
    }
    private void ValidateGrant(PNBuffInstance instance, PNBuffContribution contribution)
    {
        ValidateContract(instance, contribution);
        instance.Behavior.ValidateContributions(instance.Context, contribution.Contract,
            Array.AsReadOnly(Contributions(instance, contribution.Contract).Concat(new[] { contribution }).ToArray()));
    }
    internal IReadOnlyList<PNBuffContribution> Contributions(PNBuffInstance instance, Type contract)
        => Array.AsReadOnly(contributions.Where(x => x.IsActive && x.Targets.Contains(instance.Handle.InstanceId) && x.Contribution.Contract == contract &&
            (contract == typeof(PNBuffTagContribution) || contract == typeof(PNBuffOverrideContribution) || instance.Behavior != null && instance.Behavior.Supports(contract))).Select(x => x.Contribution).ToArray());
    private PNBuffDefinition SelectProfile(PNBuffInstance instance)
    {
        var winner = contributions.Where(x => x.IsActive && x.Targets.Contains(instance.Handle.InstanceId) && x.Contribution is PNBuffOverrideContribution)
            .OrderByDescending(x => ((PNBuffOverrideContribution)x.Contribution).Priority).ThenByDescending(x => x.Id).FirstOrDefault();
        return winner == null ? instance.Original : Registry.Buffs().GetEffective(((PNBuffOverrideContribution)winner.Contribution).ProfileId);
    }
    private void SetTags(PNBuffInstance instance)
    {
        instance.Tags.Clear(); foreach (var id in instance.Profile.Tags) instance.Tags.Add(Registry.BuffTags().ResolveIdentity(id));
        var modifiers = Contributions(instance, typeof(PNBuffTagContribution)).Cast<PNBuffTagContribution>().ToArray();
        foreach (var id in modifiers.SelectMany(x => x.Add)) instance.Tags.Add(Registry.BuffTags().ResolveIdentity(id));
        foreach (var id in modifiers.SelectMany(x => x.Suppress)) instance.Tags.Remove(Registry.BuffTags().ResolveIdentity(id));
    }
    private void RecomputeAll()
    {
        if (IsDisposed) return;
        foreach (var instance in instances.Values.ToArray())
        {
            if (instance.Ending) continue;
            var profile = SelectProfile(instance); var previousProfile = instance.Profile; instance.Profile = profile; SetTags(instance);
            if (Blocked(instance, true)) { EndCore(instance, PNBuffEndReason.ImmunityRemoved); continue; }
            if (!ReferenceEquals(previousProfile, profile))
            {
                // Block-only immunity retains existing behavior, but never starts a newly immune profile.
                if (Blocked(instance, false)) { instance.Profile = previousProfile; SetTags(instance); continue; }
                var previous = instance.Behavior;
                PNBuffBehavior replacement;
                try { replacement = profile.Create(); } catch (Exception e) { instance.Profile = previousProfile; SetTags(instance); Report(e); continue; }
                Invoke(instance, () =>
                {
                    bool migrated;
                    instance.PreparingTransition = true;
                    try { migrated = replacement.TryMigrateFrom(previous, instance.Context); } finally { instance.PreparingTransition = false; }
                    if (!Cleanup(instance)) throw new InvalidOperationException("Buff behavior transition cleanup failed.");
                    instance.Resources = NewResources(); instance.CleanupCalled = false;
                    if (!migrated) { instance.Time = 0; instance.Expiration = null; instance.Events.Clear(); instance.Clock = PNBuffClock.Game; }
                    instance.Behavior = replacement;
                    BindPresentation(instance);
                    instance.Revision++;
                    if (!instance.Suspended) replacement.OnBehaviorStarted(instance.Context, migrated);
                    else { instance.PendingBehaviorStart = true; instance.PendingMigrated = migrated; }
                });
            }
            if (!instance.Ending) Invoke(instance, () => instance.Behavior.OnContributionsChanged(instance.Context));
        }
        EnforceGroupLimits(); Notify();
    }
    private int GroupLimit(PNBuffDefinition profile, EveId id)
    {
        double value = profile.MaximumInstances;
        foreach (var lease in contributions.Where(x => x.IsActive && x.Exact == null && (!x.MatchId.HasValue || x.MatchId == id)))
            if (lease.Contribution is PNBuffNumberContribution number && number.Contract == typeof(PNBuffInstanceLimitContract)) value += number.Value;
        if (value > int.MaxValue || value < 1 || double.IsInfinity(value)) throw new InvalidOperationException("Effective instance limit must remain positive integral.");
        return profile.MultipleInstances ? (int)value : 1;
    }
    private void EnforceGroupLimits()
    {
        foreach (var group in instances.Values.Where(x => !x.Ending).GroupBy(x => new { x.Id, Source = x.Original.Scope == PNBuffInstanceScope.Source ? x.Source.EntityId : default(EveEntityId) }).ToArray())
        {
            var ordered = group.OrderBy(x => x.Handle.InstanceId).ToArray();
            int maximum = GroupLimit(ordered[0].Original, ordered[0].Id);
            foreach (var value in ordered.Skip(maximum)) EndCore(value, PNBuffEndReason.LimitReduced);
        }
    }
    internal void Release(PNBuffContributionLease lease)
    { Coordinator.Submit(() => { contributions.Remove(lease); if (!IsDisposed) RecomputeAll(); }); }
    public PNBuffImmunityLease Immune(IEnumerable<EveId> ids = null, IEnumerable<EveId> tags = null, bool purge = false)
    {
        RequireMutation(); var resolvedIds = (ids ?? Array.Empty<EveId>()).Select(x => { Registry.Buffs().Get(x); return Registry.Buffs().ResolveIdentity(x); }).ToArray();
        var resolvedTags = (tags ?? Array.Empty<EveId>()).Select(x => { Registry.BuffTags().Get(x); return Registry.BuffTags().ResolveIdentity(x); }).ToArray();
        if (resolvedIds.Length + resolvedTags.Length == 0) throw new ArgumentException("Immunity needs IDs or tags.");
        var lease = new PNBuffImmunityLease(this, ++nextLease, resolvedIds, resolvedTags, purge);
        Coordinator.Submit(() =>
        {
            if (!IsDisposed && lease.IsActive)
            { try { RequireMutation(); immunities.Add(lease); RecomputeAll(); } catch (Exception e) { lease.IsActive = false; immunities.Remove(lease); Report(e); } }
            else lease.IsActive = false;
        }, _ => lease.IsActive = false); return lease;
    }
    internal void Release(PNBuffImmunityLease lease) { Coordinator.Submit(() => { immunities.Remove(lease); if (!IsDisposed) RecomputeAll(); }); }
    private bool Blocked(PNBuffInstance value, bool purgeOnly) => immunities.Any(x => x.IsActive && (!purgeOnly || x.Purge) && (x.Ids.Contains(value.Id) || x.Tags.Overlaps(value.Tags)));

    public void Suspend(bool suspend)
    {
        RequireMutation(); if (suspended == suspend && !Coordinator.IsExecuting) return; Coordinator.Submit(() =>
        {
            if (IsDisposed) return;
            if (suspended == suspend) return;
            RequireMutation(); suspended = suspend;
            foreach (var value in instances.Values.ToArray())
            {
                if (value.Ending || value.Suspended == suspend) continue; value.Suspended = suspend; value.Revision++;
                if (suspend) { Invoke(value, () => value.Behavior.OnSuspend(value.Context)); value.Resources.Dispose(); }
                else
                {
                    value.Resources = NewResources();
                    Invoke(value, () =>
                    {
                        if (value.PendingBehaviorStart) { value.PendingBehaviorStart = false; value.Behavior.OnBehaviorStarted(value.Context, value.PendingMigrated); }
                        else value.Behavior.OnResume(value.Context);
                    });
                    if (!value.Ending) Invoke(value, () => value.Behavior.OnContributionsChanged(value.Context));
                }
            }
            Notify();
        });
    }
    public void TargetDied()
    { RequireMutation(); Coordinator.Submit(() => { foreach (var value in instances.Values.ToArray()) Invoke(value, () => value.Behavior.OnTargetDeath(value.Context)); }); }
    public void Advance(double gameDelta, double entityScale = 1, double rawScale = 1)
    {
        using (EveEngine.EveHotPathMetrics.Measure("buff.collection"))
        {
        RequireMutation(); Seconds(gameDelta, true); Seconds(entityScale, true); Seconds(rawScale, true);
        if (advancing || Coordinator.IsExecuting) throw new InvalidOperationException("Recursive/callback Buff clock advance.");
        replicationGameTime = Sum(replicationGameTime, gameDelta);
        if (instances.Count == 0) return;
        advancing = true; RemainingGameDelta = gameDelta;
        try
        {
            var frame = advanceFrame; frame.Clear();
            foreach (var value in instances.Values) if (!value.Ending && !value.Suspended)
                frame.Add(new AdvanceEntry { Value = value, Revision = value.Revision, Start = value.Time, Delta = Product(gameDelta, value.Clock == PNBuffClock.Game ? 1 : value.Clock == PNBuffClock.Entity ? entityScale : rawScale) });
            frame.Sort((a, b) => a.Value.Handle.InstanceId.CompareTo(b.Value.Handle.InstanceId));
            double fraction = 0;
            while (fraction <= 1)
            {
                PNBuffInstance next = null; PNBuffSchedule scheduled = null; double nextFraction = double.PositiveInfinity; bool expiry = false;
                foreach (var entry in frame)
                {
                    var value = entry.Value; if (value.Ending || value.Suspended || value.Revision != entry.Revision || entry.Delta == 0 || !Contains(value.Handle)) continue;
                    foreach (var ev in value.Events.Values)
                    {
                        double at = (ev.At - entry.Start) / entry.Delta;
                        if (at <= 1 && at >= fraction && (at < nextFraction || at == nextFraction && (expiry || ReferenceEquals(next, value) && scheduled != null && ev.Order < scheduled.Order))) { next = value; scheduled = ev; expiry = false; nextFraction = at; }
                    }
                    if (value.Expiration.HasValue)
                    {
                        double at = (value.Expiration.Value - entry.Start) / entry.Delta;
                        if (at <= 1 && at >= fraction && at < nextFraction) { next = value; scheduled = null; expiry = true; nextFraction = at; }
                    }
                }
                if (next == null) break;
                if (nextFraction != fraction)
                    foreach (var entry in frame) if (!entry.Value.Ending && !entry.Value.Suspended && entry.Value.Revision == entry.Revision) entry.Value.Time = Sum(entry.Start, Product(entry.Delta, nextFraction));
                fraction = nextFraction; RemainingGameDelta = Product(gameDelta, 1 - fraction);
                var current = next; var evCurrent = scheduled; bool isExpiry = expiry;
                Coordinator.Submit(() =>
                {
                    if (!Contains(current.Handle) || current.Suspended) return;
                    if (isExpiry) EndCore(current, PNBuffEndReason.Expired);
                    else
                    {
                        Invoke(current, () =>
                        {
                            if (evCurrent.Period.HasValue)
                            { double at = Sum(evCurrent.At, evCurrent.Period.Value); if (at <= evCurrent.At) throw new InvalidOperationException("Tick period is below clock precision."); evCurrent.At = at; }
                            else current.Events.Remove(evCurrent.Key);
                            var previousStacks = current.Behavior.StackCount;
                            current.Behavior.OnScheduled(current.Context, evCurrent.Key);
                            if (!Nullable.Equals(previousStacks, current.Behavior.StackCount)) ReplicationChanged(DiagnosticCause.ScheduledStacks);
                        });
                    }
                });
            }
            foreach (var entry in frame) if (Contains(entry.Value.Handle) && !entry.Value.Suspended && entry.Value.Revision == entry.Revision) entry.Value.Time = Sum(entry.Start, entry.Delta);
            RemainingGameDelta = 0;
            Coordinator.Submit(RefreshPresentationSeconds);
        }
        finally { advancing = false; RemainingGameDelta = 0; advanceFrame.Clear(); }
    
        }
    }
    public static void Seconds(double value, bool zero) { if (double.IsNaN(value) || double.IsInfinity(value) || (zero ? value < 0 : value <= 0)) throw new ArgumentOutOfRangeException(nameof(value)); }
    internal static double Sum(double a, double b) { var value = a + b; if (double.IsInfinity(value)) throw new OverflowException("Buff time overflow."); return value; }
    private static double Product(double a, double b) { var value = a * b; if (double.IsInfinity(value)) throw new OverflowException("Buff clock overflow."); return value; }
    public void Dispose() => Dispose(PNBuffEndReason.Unload);
    public void Dispose(PNBuffEndReason reason)
    {
        if (IsDisposed) return; IsDisposed = true;
        Action cleanup = () =>
        {
            foreach (var value in instances.Values.ToArray()) try { EndCore(value, reason); } catch (Exception error) { Report(error); }
            foreach (var lease in contributions) lease.IsActive = false; contributions.Clear();
            foreach (var lease in immunities) lease.IsActive = false; immunities.Clear(); Changed = null; Failed = null;
            ClearPresentation();
            Coordinator.Failed -= CoordinatorFailed;
        };
        if (Coordinator.IsFaulted) cleanup(); else Coordinator.Submit(cleanup);
    }
}
