using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EveEngine;
using Newtonsoft.Json.Linq;

public interface PNIBuffPresentationSource
{
    event Action<PNBuffPresentationChange> PresentationChanged;
    IReadOnlyList<PNBuffPresentationSnapshot> PresentationSnapshot();
}

public enum PNBuffWireKind { Added, Updated, Removed, Snapshot, Opened, Closed }
[Flags] internal enum PNBuffDiagnosticReason { Stacks = 1, Numbers = 2, Rate = 4, Clock = 8, Expiration = 16, VisualTime = 32, DynamicData = 64, PayloadVersion = 128, Profile = 256, Suspended = 512, GuaranteedCue = 1024 }
public enum PNBuffReplicaStatus { AwaitingSnapshot, Live, Resyncing, Failed, Closed }
internal enum PNBuffReplicationEndOutcome { Ended, Stale, NotAllowed }
public enum PNBuffReplicationError { ContentMismatch, InvalidPacket, ResyncExhausted, BufferLimit }

public sealed class PNBuffReplicaPolicy
{
    public int MaximumBufferedPackets { get; }
    public int MaximumInstances { get; }
    public int MaximumPayloadBytes { get; }
    public int MaximumResyncAttempts { get; }
    public double ReorderWindow { get; }
    public double RetryInterval { get; }
    public double SnapshotTimeout { get; }
    public PNBuffReplicaPolicy(int maximumBufferedPackets = 256, int maximumInstances = 4096,
        int maximumPayloadBytes = 16384, int maximumResyncAttempts = 3,
        double reorderWindow = .1, double retryInterval = 1, double snapshotTimeout = 5)
    {
        if (maximumBufferedPackets < 1 || maximumInstances < 1 || maximumPayloadBytes < 1 || maximumResyncAttempts < 1) throw new ArgumentOutOfRangeException();
        PNBuffCollection.Seconds(reorderWindow, true); PNBuffCollection.Seconds(retryInterval, false); PNBuffCollection.Seconds(snapshotTimeout, false);
        MaximumBufferedPackets = maximumBufferedPackets; MaximumInstances = maximumInstances; MaximumPayloadBytes = maximumPayloadBytes;
        MaximumResyncAttempts = maximumResyncAttempts; ReorderWindow = reorderWindow; RetryInterval = retryInterval; SnapshotTimeout = snapshotTimeout;
    }
}

// Immutable wire state. Static artwork/configuration is resolved from ProfileId.
public sealed class PNBuffWireState
{
    private readonly JToken dynamicData;
    public long InstanceId { get; }
    public long Revision { get; }
    public EveId BuffId { get; }
    public EveId ProfileId { get; }
    public EveEntityId SourceId { get; }
    public double? Stacks { get; }
    public bool Suspended { get; }
    public PNBuffNumbers Numbers { get; }
    public PNBuffClock Clock { get; }
    public double BuffTime { get; }
    public double AnchorTime { get; }
    public double Rate { get; }
    public double? Expiration { get; }
    public int PayloadVersion { get; }
    public JToken DynamicData => dynamicData?.DeepClone();
    internal bool MatchesDynamic(JToken data, int version) => PayloadVersion == version && JToken.DeepEquals(dynamicData, data);
    // Which Numbers slots changed (bit0 TopLeft .. bit3 BottomRight); recorded above bit 16 beside the reason flags.
    internal long DiagnosticNumberSlots(PNBuffWireState state)
        => (Numbers.TopLeft != state.Numbers.TopLeft ? 1L : 0L) | (Numbers.TopRight != state.Numbers.TopRight ? 2L : 0L) |
           (Numbers.BottomLeft != state.Numbers.BottomLeft ? 4L : 0L) | (Numbers.BottomRight != state.Numbers.BottomRight ? 8L : 0L);
    internal long DiagnosticDifference(PNBuffWireState state, double now)
    {
        PNBuffDiagnosticReason result = 0;
        if (Stacks != state.Stacks) result |= PNBuffDiagnosticReason.Stacks;
        if (!Numbers.Equals(state.Numbers)) result |= PNBuffDiagnosticReason.Numbers;
        if (Rate != state.Rate) result |= PNBuffDiagnosticReason.Rate;
        if (Clock != state.Clock) result |= PNBuffDiagnosticReason.Clock;
        if (Expiration != state.Expiration) result |= PNBuffDiagnosticReason.Expiration;
        if (Math.Abs(VisualTime(now) - state.BuffTime) > 1e-8 * Math.Max(1, Math.Abs(state.BuffTime))) result |= PNBuffDiagnosticReason.VisualTime;
        if (!JToken.DeepEquals(dynamicData, state.dynamicData)) result |= PNBuffDiagnosticReason.DynamicData;
        if (PayloadVersion != state.PayloadVersion) result |= PNBuffDiagnosticReason.PayloadVersion;
        if (ProfileId != state.ProfileId) result |= PNBuffDiagnosticReason.Profile;
        if (Suspended != state.Suspended) result |= PNBuffDiagnosticReason.Suspended;
        return (long)result;
    }
    public PNBuffWireState(long instanceId, long revision, EveId buffId, EveId profileId, EveEntityId sourceId,
        double? stacks, bool suspended, PNBuffNumbers numbers, PNBuffClock clock, double buffTime,
        double anchorTime, double rate, double? expiration, JToken dynamicData = null, int payloadVersion = 1)
    {
        InstanceId = instanceId; Revision = revision; BuffId = buffId; ProfileId = profileId; SourceId = sourceId;
        Stacks = stacks; Suspended = suspended; Numbers = numbers; Clock = clock; BuffTime = buffTime;
        AnchorTime = anchorTime; Rate = rate; Expiration = expiration; this.dynamicData = dynamicData?.DeepClone(); PayloadVersion = payloadVersion;
    }
    public double VisualTime(double authoritativeNow) => BuffTime + Math.Max(0, authoritativeNow - AnchorTime) * (Suspended ? 0 : Rate);
}

public sealed class PNBuffCue
{
    private readonly JToken payload;
    public long Id { get; }
    public long InstanceId { get; }
    public string Name { get; }
    public double At { get; }
    public double MaximumAge { get; }
    public JToken Payload => payload?.DeepClone();
    public PNBuffCue(long id, long instanceId, string name, double at, double maximumAge = 1, JToken payload = null)
    { Id = id; InstanceId = instanceId; Name = name; At = at; MaximumAge = maximumAge; this.payload = payload?.DeepClone(); }
}

public sealed class PNBuffWireMessage
{
    public Guid SessionId { get; }
    public EveEntityId TargetId { get; }
    public long Epoch { get; }
    public long Sequence { get; }
    public PNBuffWireKind Kind { get; }
    public IReadOnlyList<PNBuffWireState> States { get; }
    public long RemovedId { get; }
    public PNBuffEndReason? EndReason { get; }
    public IReadOnlyList<PNBuffCue> Cues { get; }
    public PNBuffWireMessage(Guid sessionId, EveEntityId targetId, long epoch, long sequence, PNBuffWireKind kind,
        IEnumerable<PNBuffWireState> states = null, long removedId = 0, PNBuffEndReason? endReason = null, IEnumerable<PNBuffCue> cues = null)
    {
        SessionId = sessionId; TargetId = targetId; Epoch = epoch; Sequence = sequence; Kind = kind;
        States = Array.AsReadOnly((states ?? Array.Empty<PNBuffWireState>()).ToArray()); RemovedId = removedId; EndReason = endReason;
        Cues = Array.AsReadOnly((cues ?? Array.Empty<PNBuffCue>()).ToArray());
    }
}

public sealed class PNBuffOptionalCueMessage
{
    public Guid SessionId { get; }
    public EveEntityId TargetId { get; }
    public long Epoch { get; }
    public PNBuffCue Cue { get; }
    public PNBuffOptionalCueMessage(Guid sessionId, EveEntityId targetId, long epoch, PNBuffCue cue)
    { SessionId = sessionId; TargetId = targetId; Epoch = epoch; Cue = cue; }
}

public sealed class PNBuffPresenterDefinition : EveIIdentified
{
    private readonly Func<PNBuffPresenterContext, PNBuffPresenter> factory;
    private readonly Action<JToken> validate;
    public EveId Id { get; }
    public EveId ImplementationId { get; }
    public int PayloadVersion { get; }
    public int MaximumPayloadBytes { get; }
    public bool GroupByProfile { get; }
    public PNBuffPresenterDefinition(EveId id, EveId implementationId, Func<PNBuffPresenterContext, PNBuffPresenter> factory,
        Action<JToken> validate, int payloadVersion = 1, int maximumPayloadBytes = 4096, bool groupByProfile = false)
    {
        if (!id.IsValid || !implementationId.IsValid || payloadVersion < 1 || maximumPayloadBytes < 1) throw new ArgumentException("Invalid presenter definition.");
        Id = id; ImplementationId = implementationId; this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        this.validate = validate; PayloadVersion = payloadVersion; MaximumPayloadBytes = maximumPayloadBytes; GroupByProfile = groupByProfile;
    }
    public void Validate(PNBuffWireState state)
    {
        if (state.PayloadVersion != PayloadVersion) throw new NotSupportedException("Unsupported presenter payload version.");
        var payload = state.DynamicData;
        if (System.Text.Encoding.UTF8.GetByteCount(payload?.ToString(Newtonsoft.Json.Formatting.None) ?? "") > MaximumPayloadBytes) throw new FormatException("Presenter payload too large.");
        validate?.Invoke(payload);
    }
    public PNBuffPresenter Create(PNBuffPresenterContext context) => factory(context) ?? throw new InvalidOperationException("Presenter factory returned null.");
}

public sealed class PNBuffPresenterContext
{
    public PNStat Target { get; }
    public PNBuffDefinition Profile { get; }
    public PNBuffResourceScope Resources { get; }
    public PNBuffDetachedEffects DetachedEffects { get; }
    public PNBuffPresenterContext(PNStat target, PNBuffDefinition profile, PNBuffResourceScope resources)
    { Target = target; Profile = profile; Resources = resources; DetachedEffects = EveRegistrySession.IsReady ? PNBuffDetachedEffects.For(EveRegistrySession.Context) : null; }
}

public abstract class PNBuffPresenter : IDisposable
{
    public abstract void Update(IReadOnlyList<PNBuffWireState> states, double authoritativeNow);
    public virtual void Cue(PNBuffCue cue) { }
    public virtual void Removed(PNBuffWireState state, PNBuffEndReason reason) { }
    public virtual void Advance(double authoritativeNow) { }
    public virtual void Dispose() { }
}

// Trusted-host input only. The transport authenticates the connection before calling this proxy.
public sealed partial class PNBuffReplica : PNIBuffPresentationSource, IDisposable
{
    private readonly EveRegistryHub registry;
    private readonly PNStat target;
    private readonly bool render;
    private int publicationDepth;
    private readonly Queue<Action> publicationNotices = new Queue<Action>();
    public IDisposable DeferPresentation()
    { publicationDepth++; return new PNBuffRelease(() => { if (--publicationDepth == 0) while (publicationNotices.Count > 0) Observer(publicationNotices.Dequeue()); }); }
    private readonly PNBuffReplicaPolicy policy;
    private readonly Dictionary<long, PNBuffWireState> states = new Dictionary<long, PNBuffWireState>();
    private readonly SortedDictionary<long, PNBuffWireMessage> buffered = new SortedDictionary<long, PNBuffWireMessage>();
    private readonly Dictionary<string, PresenterSlot> presenters = new Dictionary<string, PresenterSlot>();
    private readonly Dictionary<EveId, long> groups = new Dictionary<EveId, long>();
    private readonly HashSet<long> seenCues = new HashSet<long>();
    private readonly Queue<long> cueOrder = new Queue<long>();
    private readonly HashSet<long> retired = new HashSet<long>();
    private readonly Queue<long> retiredOrder = new Queue<long>();
    private double gapAt = -1, lastRequest = double.NegativeInfinity, requestedAt;
    private int attempts;
    private long uiSequence, groupSequence;
    private long lastSnapshotSequence = -1, highestInstance;
    private readonly Queue<Action> incoming = new Queue<Action>();
    private bool dispatching;
    private void Dispatch(Action action)
    {
        if (incoming.Count >= policy.MaximumBufferedPackets) { Fail(PNBuffReplicationError.BufferLimit, "Reentrant Buff input queue overflow."); incoming.Clear(); return; }
        incoming.Enqueue(action); if (dispatching) return; dispatching = true;
        try
        {
            int count = 0;
            while (incoming.Count > 0)
            {
                if (++count > 10000) { Fail(PNBuffReplicationError.BufferLimit, "Recursive Buff input cycle."); incoming.Clear(); break; }
                incoming.Dequeue()();
            }
        }
        finally { dispatching = false; }
    }
    public Guid SessionId { get; }
    public EveEntityId TargetId { get; }
    public long Epoch { get; private set; }
    public long Sequence { get; private set; }
    public PNBuffReplicaStatus Status { get; private set; } = PNBuffReplicaStatus.AwaitingSnapshot;
    private sealed class PresentationObservers
    {internal Action<PNBuffPresentationChange> Changed;internal Action<Exception> PresenterFailure;internal Action<PNBuffCue> Cue;}
    private PresentationObservers observers=new PresentationObservers();
    public event Action<PNBuffPresentationChange> PresentationChanged {add{observers.Changed+=value;}remove{observers.Changed-=value;}}
    public event Action SnapshotRequested;
    public event Action<PNBuffReplicationError, string> Failed;
    public event Action<Exception> PresenterFailed {add{observers.PresenterFailure+=value;}remove{observers.PresenterFailure-=value;}}
    public event Action<PNBuffCue> CuePresented {add{observers.Cue+=value;}remove{observers.Cue-=value;}}
    public int PresenterCount => presenters.Count(x => x.Value.Value != null);
    public void RejectSession(PNBuffReplicationError code, string reason) => Dispatch(() => Fail(code, reason));
    public IReadOnlyList<PNBuffWireState> Snapshot() => Array.AsReadOnly(states.Values.OrderBy(x => x.InstanceId).ToArray());
    private sealed class PresenterSlot
    {
        public PNBuffPresenter Value; public PNBuffResourceScope Resources; public bool Faulted; public int Failures;
    }
    public PNBuffReplica(EveRegistryHub registry, Guid authoritativeSession, EveEntityId targetId,
        PNStat target = null, bool render = true, PNBuffReplicaPolicy policy = null)
    {
        if (registry == null || !registry.IsSealed || authoritativeSession == Guid.Empty || !targetId.IsValid) throw new ArgumentException("Replica needs sealed content and authoritative identity.");
        this.registry = registry; SessionId = authoritativeSession; TargetId = targetId; this.target = target; this.render = render; this.policy = policy ?? new PNBuffReplicaPolicy();
    }
    private void Observer(Action action) { try { action(); } catch (Exception e) { ReportPresenter(e); } }
    private void ReportPresenter(Exception e) { foreach (Action<Exception> f in observers.PresenterFailure?.GetInvocationList() ?? Array.Empty<Delegate>()) try { f(e); } catch { } }
    private void Fail(PNBuffReplicationError code, string message)
    {
        if (Status == PNBuffReplicaStatus.Failed || Status == PNBuffReplicaStatus.Closed) return;
        Status = PNBuffReplicaStatus.Failed; Clear();
        foreach (Action<PNBuffReplicationError, string> f in Failed?.GetInvocationList() ?? Array.Empty<Delegate>()) try { f(code, message); } catch { }
    }
    private void ValidateState(PNBuffWireState s)
    {
        if (s == null || s.InstanceId <= 0 || s.Revision <= 0 || !s.SourceId.IsValid || !s.BuffId.IsValid || !s.ProfileId.IsValid || !Enum.IsDefined(typeof(PNBuffClock), s.Clock)) throw new FormatException("Invalid Buff state identity.");
        if (!registry.Buffs().TryGet(s.BuffId, out _) || !registry.Buffs().TryGet(s.ProfileId, out _)) throw new MissingMemberException("Unknown Buff/profile: content mismatch.");
        PNBuffCollection.Seconds(s.BuffTime, true); PNBuffCollection.Seconds(s.AnchorTime, true); PNBuffCollection.Seconds(s.Rate, true);
        if (s.Expiration.HasValue) PNBuffCollection.Seconds(s.Expiration.Value, true);
        if (s.Stacks.HasValue && (double.IsNaN(s.Stacks.Value) || double.IsInfinity(s.Stacks.Value) || s.Stacks < 0)) throw new FormatException("Invalid Buff stacks.");
        if (s.PayloadVersion < 1 || System.Text.Encoding.UTF8.GetByteCount(s.DynamicData?.ToString(Newtonsoft.Json.Formatting.None) ?? "") > policy.MaximumPayloadBytes) throw new FormatException("Invalid dynamic payload size/version.");
    }
    private void ValidateCue(PNBuffCue cue)
    {
        if (cue == null || cue.Id <= 0 || cue.InstanceId <= 0 || string.IsNullOrWhiteSpace(cue.Name) || cue.Name.Length > 128) throw new FormatException("Invalid cue.");
        PNBuffCollection.Seconds(cue.At, true); PNBuffCollection.Seconds(cue.MaximumAge, true);
        if (System.Text.Encoding.UTF8.GetByteCount(cue.Payload?.ToString(Newtonsoft.Json.Formatting.None) ?? "") > policy.MaximumPayloadBytes) throw new FormatException("Cue payload too large.");
    }
    public void Receive(PNBuffWireMessage m, double now) => Dispatch(() => ReceiveCore(m, now));
    private void ReceiveCore(PNBuffWireMessage m, double now)
    {
        PNBuffCollection.Seconds(now, true);
        if (Status == PNBuffReplicaStatus.Failed || Status == PNBuffReplicaStatus.Closed) return;
        if (m == null) { Request(now); return; }
        if (m.SessionId != SessionId || m.TargetId != TargetId) return;
        if (m.Epoch < Epoch || m.Epoch <= 0) return;
        if (m.Epoch == Epoch && m.Kind != PNBuffWireKind.Snapshot && m.Kind != PNBuffWireKind.Opened && m.Sequence <= Sequence) return;
        try
        {
            if (m.Sequence < 0 || !Enum.IsDefined(typeof(PNBuffWireKind), m.Kind) || m.States.Count > policy.MaximumInstances || m.Cues.Count > policy.MaximumBufferedPackets) throw new FormatException("Invalid envelope.");
            foreach (var s in m.States) ValidateState(s);
            foreach (var c in m.Cues) ValidateCue(c);
            if (m.States.Select(x => x.InstanceId).Distinct().Count() != m.States.Count) throw new FormatException("Duplicate snapshot instance.");
            if (m.Kind == PNBuffWireKind.Added || m.Kind == PNBuffWireKind.Updated) { if (m.States.Count != 1) throw new FormatException("One state required."); }
            else if (m.Kind != PNBuffWireKind.Snapshot && m.States.Count != 0) throw new FormatException("Unexpected state payload.");
            if (m.Kind == PNBuffWireKind.Removed && (m.RemovedId <= 0 || !m.EndReason.HasValue || !Enum.IsDefined(typeof(PNBuffEndReason), m.EndReason.Value))) throw new FormatException("Invalid removal.");
            if (m.Cues.Count > 0 && (m.Kind != PNBuffWireKind.Added && m.Kind != PNBuffWireKind.Updated && m.Kind != PNBuffWireKind.Removed || m.Cues.Any(c => c.InstanceId != (m.Kind == PNBuffWireKind.Removed ? m.RemovedId : m.States[0].InstanceId)))) throw new FormatException("Cue belongs to another event/instance.");
            if (m.Kind == PNBuffWireKind.Snapshot && m.Cues.Count != 0) throw new FormatException("Snapshots cannot replay cues.");
            if (m.Epoch > Epoch)
            {
                if (m.Kind != PNBuffWireKind.Opened && m.Kind != PNBuffWireKind.Snapshot) { Request(now); return; }
                Clear(); buffered.Clear(); retired.Clear(); retiredOrder.Clear(); seenCues.Clear(); cueOrder.Clear(); groups.Clear(); groupSequence = 0;
                Epoch = m.Epoch; Sequence = 0; lastSnapshotSequence = -1; attempts = 0; gapAt = -1; Status = PNBuffReplicaStatus.AwaitingSnapshot;
            }
            if (m.Kind == PNBuffWireKind.Opened) { Request(now); return; }
            if (m.Kind == PNBuffWireKind.Snapshot)
            {
                if (m.Sequence < Sequence || m.Sequence <= lastSnapshotSequence) return;
                if (m.States.Any(s => retired.Contains(s.InstanceId) || !states.ContainsKey(s.InstanceId) && s.InstanceId <= highestInstance)) throw new FormatException("Snapshot reused a retired instance.");
                Reconcile(m.States, now); Sequence = m.Sequence; lastSnapshotSequence = m.Sequence; Status = PNBuffReplicaStatus.Live; gapAt = -1;
                foreach (var key in buffered.Keys.Where(x => x <= Sequence).ToArray()) buffered.Remove(key);
                Drain(now); if (buffered.Count == 0) attempts = 0; return;
            }
            if (m.Sequence <= Sequence) return;
            if (buffered.ContainsKey(m.Sequence)) return;
            if (buffered.Count >= policy.MaximumBufferedPackets) { buffered.Clear(); Request(now); return; }
            buffered.Add(m.Sequence, m);
            if (Status == PNBuffReplicaStatus.Live) Drain(now);
            if (buffered.Count > 0 && gapAt < 0) gapAt = now;
        }
        catch (MissingMemberException e) { Fail(PNBuffReplicationError.ContentMismatch, e.Message); }
        catch (Exception) { Request(now); }
    }
    private void Drain(double now)
    {
        while (Status == PNBuffReplicaStatus.Live && buffered.TryGetValue(Sequence + 1, out var m))
        {
            if (!Apply(m, now)) { Request(now); return; }
            buffered.Remove(m.Sequence); Sequence = m.Sequence;
        }
        if (buffered.Count == 0) gapAt = -1; else if (gapAt < 0) gapAt = now;
    }
    private bool Apply(PNBuffWireMessage m, double now)
    {
        if (m.Kind == PNBuffWireKind.Closed) { Dispose(); return true; }
        if (m.Kind == PNBuffWireKind.Removed)
        {
            if (!states.TryGetValue(m.RemovedId, out var old)) return false;
            DeliverCues(m.Cues, now, false);
            var key = GroupKey(old); Present(() => { if (presenters.TryGetValue(key, out var slot) && !slot.Faulted) InvokeSlot(slot, () => slot.Value?.Removed(old, m.EndReason.Value)); });
            // The Removed event carries the last shown Numbers; forget the countdown cache only after emitting it.
            states.Remove(old.InstanceId); Retired(old.InstanceId); Emit(PNBuffPresentationChangeKind.Removed, old); ForgetShown(old.InstanceId); Rebuild(now, false); return true;
        }
        var s = m.States[0]; bool cleanupGroups = false;
        if (m.Kind == PNBuffWireKind.Added)
        {
            if (states.ContainsKey(s.InstanceId) || s.InstanceId <= highestInstance || retired.Contains(s.InstanceId) || states.Count >= policy.MaximumInstances) return false;
            highestInstance = s.InstanceId;
            states.Add(s.InstanceId, s); StoreShown(s, now); Emit(PNBuffPresentationChangeKind.Added, s);
        }
        else
        {
            if (!states.TryGetValue(s.InstanceId, out var old)) return false;
            if (s.Revision <= old.Revision) return false;
            cleanupGroups = old.BuffId != s.BuffId;
            states[s.InstanceId] = s; if (UiChanged(old, s, now)) Emit(PNBuffPresentationChangeKind.Updated, s);
        }
        Rebuild(now, false, cleanupGroups); DeliverCues(m.Cues, now, false); return true;
    }
    private void Reconcile(IReadOnlyList<PNBuffWireState> incoming, double now)
    {
        var next = incoming.ToDictionary(x => x.InstanceId);
        foreach (var old in states.Values.ToArray()) if (!next.ContainsKey(old.InstanceId)) { states.Remove(old.InstanceId); Retired(old.InstanceId); Emit(PNBuffPresentationChangeKind.Removed, old); ForgetShown(old.InstanceId); }
        foreach (var s in incoming)
        {
            bool existed = states.TryGetValue(s.InstanceId, out var old); states[s.InstanceId] = s;
            bool changed;
            if (existed) changed = UiChanged(old, s, now); else { StoreShown(s, now); changed = true; }
            if (changed) Emit(existed ? PNBuffPresentationChangeKind.Updated : PNBuffPresentationChangeKind.Added, s);
        }
        if (incoming.Count > 0) highestInstance = Math.Max(highestInstance, incoming.Max(s => s.InstanceId));
        Rebuild(now, true);
    }
    // Countdown opt-in: Numbers shown for instances whose effective profile declares a descriptor. The wire slot is
    // null while running; replicas derive it from clock state, so no wire change is needed per second. Entries exist
    // only for opted-in instances, so legacy profiles pay nothing per frame.
    // NextChange is the presentation time at which the displayed integer next changes (+inf when it never will:
    // suspended, no expiration, stopped clock or hidden). Frames before the earliest NextChange do no per-instance work.
    private struct ShownCountdown { public PNBuffCountdown Descriptor; public PNBuffNumbers Numbers; public double NextChange; }
    private double earliestCountdownChange = double.PositiveInfinity;
    private readonly Dictionary<long, ShownCountdown> shown = new Dictionary<long, ShownCountdown>();
    private readonly HashSet<long> countdownOverflowReported = new HashSet<long>();
    // Overflow may recover from clock progression alone. These rare active instances
    // are checked separately, without waking the ordinary countdown scheduling pass.
    private readonly HashSet<long> overflowingCountdowns = new HashSet<long>();
    private readonly List<long> overflowBuffer = new List<long>();
    private readonly List<long> countdownBuffer = new List<long>();
    private PNBuffNumbers ProjectCountdown(PNBuffWireState s, PNBuffCountdown descriptor, double now)
    {
        // Suspended: the host's frozen value travels in the existing Numbers slot, and VisualTime stands still.
        if (s.Suspended) return s.Numbers;
        long? seconds = PNBuffCountdownMath.Compute(s.Expiration, s.VisualTime(now), out bool overflow);
        if (overflow && countdownOverflowReported.Add(s.InstanceId)) ReportPresenter(new OverflowException("Buff presentation seconds exceed Int64."));
        return descriptor.With(s.Numbers, seconds);
    }
    private static double NextCountdownChange(PNBuffWireState s, double now)
    {
        if (s.Suspended || !s.Expiration.HasValue || !(s.Rate > 0)) return double.PositiveInfinity;
        double remaining = s.Expiration.Value - s.VisualTime(now);
        if (remaining <= 0 || remaining >= 9223372036854775808d) return double.PositiveInfinity;
        // Displayed Ceiling(remaining) falls by one when remaining reaches Ceiling(remaining) - 1.
        double boundary = Math.Ceiling(remaining) - 1;
        return s.AnchorTime + (s.Expiration.Value - s.BuffTime - boundary) / s.Rate;
    }
    // Compute the Numbers this state presents at `now` and store them for the instance (or clear for legacy profiles).
    // Callers compare with the previous cached value BEFORE calling this when they need the old presentation.
    private void StoreShown(PNBuffWireState s, double now)
    {
        var descriptor = registry.Buffs().Get(s.ProfileId).Countdown;
        if (descriptor == null) { shown.Remove(s.InstanceId); countdownOverflowReported.Remove(s.InstanceId); overflowingCountdowns.Remove(s.InstanceId); return; }
        double next = NextCountdownChange(s, now);
        shown[s.InstanceId] = new ShownCountdown { Descriptor = descriptor, Numbers = ProjectCountdown(s, descriptor, now), NextChange = next };
        UpdateOverflowMembership(s, now);
        if (next < earliestCountdownChange) earliestCountdownChange = next;
    }
    private void UpdateOverflowMembership(PNBuffWireState s, double now)
    {
        bool overflow = false;
        if (!s.Suspended && s.Rate > 0) PNBuffCountdownMath.Compute(s.Expiration, s.VisualTime(now), out overflow);
        if (overflow) overflowingCountdowns.Add(s.InstanceId); else overflowingCountdowns.Remove(s.InstanceId);
    }
    private void ForgetShown(long instanceId) { shown.Remove(instanceId); countdownOverflowReported.Remove(instanceId); overflowingCountdowns.Remove(instanceId); }
    private PNBuffPresentationSnapshot Ui(PNBuffWireState s)
    {
        if (!groups.TryGetValue(s.BuffId, out var order)) groups.Add(s.BuffId, order = ++groupSequence);
        return new PNBuffPresentationSnapshot(s, registry.Buffs().Get(s.ProfileId), order, uiSequence,
            shown.TryGetValue(s.InstanceId, out var entry) ? entry.Numbers : (PNBuffNumbers?)null);
    }
    // Compares with the presentation currently shown (cache, else the previous wire Numbers) and only then refreshes
    // the cache, so a change is never lost by overwriting the old value before comparing.
    private bool UiChanged(PNBuffWireState old, PNBuffWireState next, double now)
    {
        var before = shown.TryGetValue(old.InstanceId, out var entry) ? entry.Numbers : old.Numbers;
        bool profileChanged = old.ProfileId != next.ProfileId;
        if (profileChanged) countdownOverflowReported.Remove(next.InstanceId);
        StoreShown(next, now);
        var after = shown.TryGetValue(next.InstanceId, out var current) ? current.Numbers : next.Numbers;
        return old.BuffId != next.BuffId || profileChanged || old.Suspended != next.Suspended || !before.Equals(after);
    }
    private void AdvanceCountdowns(double now)
    {
        countdownBuffer.Clear(); countdownBuffer.AddRange(shown.Keys);
        double earliest = double.PositiveInfinity;
        foreach (long id in countdownBuffer)
        {
            if (!states.TryGetValue(id, out var s) || s.Suspended || !shown.TryGetValue(id, out var entry)) continue;
            if (entry.NextChange - 1e-9 > now) { if (entry.NextChange < earliest) earliest = entry.NextChange; continue; }
            var next = ProjectCountdown(s, entry.Descriptor, now);
            bool changed = !entry.Numbers.Equals(next);
            entry.Numbers = next; entry.NextChange = NextCountdownChange(s, now); shown[id] = entry;
            UpdateOverflowMembership(s, now);
            if (entry.NextChange < earliest) earliest = entry.NextChange;
            if (changed) Emit(PNBuffPresentationChangeKind.Updated, s);
        }
        earliestCountdownChange = earliest;
    }
    private void AdvanceOverflowCountdowns(double now)
    {
        overflowBuffer.Clear(); overflowBuffer.AddRange(overflowingCountdowns);
        foreach (long id in overflowBuffer)
        {
            if (!states.TryGetValue(id, out var s) || s.Suspended || !(s.Rate > 0) || !shown.TryGetValue(id, out var entry))
            { overflowingCountdowns.Remove(id); continue; }
            PNBuffCountdownMath.Compute(s.Expiration, s.VisualTime(now), out bool overflow);
            if (overflow) continue;
            overflowingCountdowns.Remove(id);
            var next = ProjectCountdown(s, entry.Descriptor, now);
            bool changed = !entry.Numbers.Equals(next);
            entry.Numbers = next; entry.NextChange = NextCountdownChange(s, now); shown[id] = entry;
            if (entry.NextChange < earliestCountdownChange) earliestCountdownChange = entry.NextChange;
            if (changed) Emit(PNBuffPresentationChangeKind.Updated, s);
        }
    }
    private void Retired(long id)
    { if (!retired.Add(id)) return; retiredOrder.Enqueue(id); while (retiredOrder.Count > policy.MaximumBufferedPackets * 4) retired.Remove(retiredOrder.Dequeue()); }
    private void Emit(PNBuffPresentationChangeKind kind, PNBuffWireState s)
    {
        uiSequence = checked(uiSequence + 1); var change = new PNBuffPresentationChange(kind, Ui(s), uiSequence);
        Action notify = () => { foreach (Action<PNBuffPresentationChange> f in observers.Changed?.GetInvocationList() ?? Array.Empty<Delegate>()) Observer(() => f(change)); };
        Present(notify);
    }
    public IReadOnlyList<PNBuffPresentationSnapshot> PresentationSnapshot() => Array.AsReadOnly(states.Values.Select(Ui).OrderBy(x => x.GroupOrder).ThenBy(x => x.InstanceId).ToArray());
    private string GroupKey(PNBuffWireState s)
    {
        var p = registry.Buffs().Get(s.ProfileId).PresenterId;
        if (!p.IsValid) return "";
        var d = registry.BuffPresenters().GetEffective(p);
        return s.ProfileId + (d.GroupByProfile ? "" : ":" + s.InstanceId);
    }
    private void RetireSlot(PresenterSlot slot)
    {
        var value = slot.Value; slot.Value = null;
        try { value?.Dispose(); } catch (Exception e) { ReportPresenter(e); }
        try { slot.Resources?.Dispose(); } catch (Exception e) { ReportPresenter(e); }
        slot.Resources = null;
    }
    private void InvokeSlot(PresenterSlot slot, Action action)
    {
        if (slot.Faulted) return;
        try { action(); }
        catch (Exception e) { slot.Faulted = true; slot.Failures++; RetireSlot(slot); ReportPresenter(e); }
    }
    private void Rebuild(double now, bool retry, bool cleanupGroups = true)
    {
        if (cleanupGroups) foreach (var id in groups.Keys.Where(id => !states.Values.Any(s => s.BuffId == id)).ToArray()) groups.Remove(id);
        if (!render) return;
        var desired = states.Values.Where(x => !x.Suspended && registry.Buffs().Get(x.ProfileId).PresenterId.IsValid).GroupBy(GroupKey).ToDictionary(x => x.Key, x => x.OrderBy(y => y.InstanceId).ToArray());
        Present(() => RebuildDesired(desired, now, retry));
    }
    private void RebuildDesired(Dictionary<string, PNBuffWireState[]> desired, double now, bool retry)
    {
        foreach (var key in presenters.Keys.Where(x => !desired.ContainsKey(x)).ToArray()) { RetireSlot(presenters[key]); presenters.Remove(key); }
        foreach (var pair in desired)
        {
            if (!presenters.TryGetValue(pair.Key, out var slot)) presenters.Add(pair.Key, slot = new PresenterSlot());
            if (retry && slot.Failures < policy.MaximumResyncAttempts) slot.Faulted = false;
            if (slot.Faulted) continue;
            InvokeSlot(slot, () =>
            {
                var profile = registry.Buffs().Get(pair.Value[0].ProfileId); var definition = registry.BuffPresenters().GetEffective(profile.PresenterId);
                foreach (var state in pair.Value) definition.Validate(state);
                if (slot.Value == null) { slot.Resources = new PNBuffResourceScope(ReportPresenter); slot.Value = definition.Create(new PNBuffPresenterContext(target, profile, slot.Resources)); }
                slot.Value.Update(Array.AsReadOnly(pair.Value), now);
            });
        }
    }
    private void DeliverCues(IEnumerable<PNBuffCue> cues, double now, bool optional)
    {
        foreach (var cue in cues)
        {
            if (!states.TryGetValue(cue.InstanceId, out var state) || state.Suspended || seenCues.Contains(cue.Id) || optional && (now < cue.At || now - cue.At > cue.MaximumAge)) continue;
            seenCues.Add(cue.Id); cueOrder.Enqueue(cue.Id); while (cueOrder.Count > policy.MaximumBufferedPackets * 4) seenCues.Remove(cueOrder.Dequeue());
            var key = GroupKey(state);
            Present(() => {
                if (presenters.TryGetValue(key, out var slot)) InvokeSlot(slot, () => slot.Value?.Cue(cue));
                foreach (Action<PNBuffCue> f in observers.Cue?.GetInvocationList() ?? Array.Empty<Delegate>()) Observer(() => f(cue));
            });
        }
    }
    public void Receive(PNBuffOptionalCueMessage m, double now) => Dispatch(() => ReceiveCore(m, now));
    // Ordered actor stream already checked sequence/session/incarnation/generation.
    // No second gap buffer or per-target recovery is allowed on this path.
    public void ApplyLifecycle(PNBuffWireMessage message, double now)
    {
        PNBuffCollection.Seconds(now, true);
        if (message == null || message.SessionId != SessionId || message.TargetId != TargetId || message.Epoch <= 0 || Status == PNBuffReplicaStatus.Closed || Status == PNBuffReplicaStatus.Failed) throw new FormatException("Invalid actor Buff packet.");
        if (Epoch != 0 && message.Epoch != Epoch) throw new FormatException("Buff generation differs from actor generation.");
        foreach (var state in message.States) ValidateState(state);
        foreach (var cue in message.Cues) ValidateCue(cue);
        if (message.States.Select(x => x.InstanceId).Distinct().Count() != message.States.Count || message.States.Count > policy.MaximumInstances) throw new FormatException("Buff state count.");
        Epoch = message.Epoch; Status = PNBuffReplicaStatus.Live;
        if (message.Kind == PNBuffWireKind.Snapshot) Reconcile(message.States, now);
        else
        {
            if (message.Kind == PNBuffWireKind.Removed && (message.RemovedId <= 0 || !message.EndReason.HasValue) ||
                (message.Kind == PNBuffWireKind.Added || message.Kind == PNBuffWireKind.Updated) && message.States.Count != 1 ||
                message.Kind != PNBuffWireKind.Added && message.Kind != PNBuffWireKind.Updated && message.Kind != PNBuffWireKind.Removed) throw new FormatException("Invalid Buff lifecycle kind.");
            if (!Apply(message, now)) throw new FormatException("Buff delta does not match current actor state.");
        }
        Sequence = message.Sequence; // diagnostic only; outer actor stream owns ordering
    }
    private void ReceiveCore(PNBuffOptionalCueMessage m, double now)
    {
        PNBuffCollection.Seconds(now, true);
        if (Status != PNBuffReplicaStatus.Live || m == null || m.SessionId != SessionId || m.TargetId != TargetId || m.Epoch != Epoch) return;
        try { ValidateCue(m.Cue); DeliverCues(new[] { m.Cue }, now, true); } catch (Exception e) { ReportPresenter(e); }
    }
    private void Request(double now)
    {
        if (Status == PNBuffReplicaStatus.Failed || Status == PNBuffReplicaStatus.Closed) return;
        Status = PNBuffReplicaStatus.Resyncing;
        if (now - lastRequest < policy.RetryInterval) return;
        if (++attempts > policy.MaximumResyncAttempts) { Fail(PNBuffReplicationError.ResyncExhausted, "Buff snapshot recovery failed."); return; }
        lastRequest = requestedAt = now;
        foreach (Action f in SnapshotRequested?.GetInvocationList() ?? Array.Empty<Delegate>()) Observer(f);
    }
    public void Advance(double now) => Dispatch(() => AdvanceCore(now));
    private void AdvanceCore(double now)
    {
        using (EveEngine.EveHotPathMetrics.Measure("buff.replica"))
        {
        PNBuffCollection.Seconds(now, true);
        if (Status == PNBuffReplicaStatus.Live && gapAt >= 0 && now - gapAt >= policy.ReorderWindow) Request(now);
        else if (Status == PNBuffReplicaStatus.Resyncing && now - requestedAt >= policy.SnapshotTimeout) Request(now);
        if (Status != PNBuffReplicaStatus.Live && Status != PNBuffReplicaStatus.Resyncing) return;
        // Replica-derived countdown: UI-only change, no wire revision/sequence, cue or presenter rebuild involved.
        if (overflowingCountdowns.Count > 0) AdvanceOverflowCountdowns(now);
        if (shown.Count > 0 && now >= earliestCountdownChange - 1e-9) AdvanceCountdowns(now);
        foreach (var slot in presenters.Values.ToArray()) InvokeSlot(slot, () => slot.Value?.Advance(now));
    
        }
    }
    private void Clear()
    {
        foreach (var slot in presenters.Values.ToArray()) RetireSlot(slot); presenters.Clear();
        foreach (var state in states.Values.ToArray()) { states.Remove(state.InstanceId); Emit(PNBuffPresentationChangeKind.Removed, state); }
        shown.Clear(); countdownOverflowReported.Clear(); overflowingCountdowns.Clear(); overflowBuffer.Clear(); earliestCountdownChange = double.PositiveInfinity;
        buffered.Clear();
    }
    public void Dispose() => Dispatch(() =>
    {
        if (Status == PNBuffReplicaStatus.Closed) return;
        Status = PNBuffReplicaStatus.Closed; Clear(); groups.Clear(); retired.Clear(); retiredOrder.Clear(); seenCues.Clear(); cueOrder.Clear();
        observers.Changed=null;SnapshotRequested=null;Failed=null;observers.PresenterFailure=null;observers.Cue=null;
    });
}

// IDs are process-monotonic and never reused during an authoritative session.
public sealed class PNBuffPublisher : IDisposable
{
    private static long nextIdentity, nextEpoch;
    private static long Allocate(ref long counter)
    { while (true) { long old = Interlocked.Read(ref counter); long next = checked(old + 1); if (Interlocked.CompareExchange(ref counter, next, old) == old) return next; } }
    private readonly PNBuffCollection collection;
    private readonly EveEntityId target;
    private readonly Func<double> currentTime;
    private readonly Dictionary<long, long> identities = new Dictionary<long, long>();
    private readonly Dictionary<long, PNBuffWireState> previous = new Dictionary<long, PNBuffWireState>();
    private readonly Dictionary<long, List<PNBuffCue>> endingCues = new Dictionary<long, List<PNBuffCue>>();
    private long sequence, cueId;
    private double now, entityScale = 1, rawScale = 1, lastAnchor = -1;
    private double lastObservedGameTime;
    private long lastObservedRevision = -1;
    // D5 (RulesVersion 71): an exception escaping a StackCount call made by the scan counts as a failure of that behavior
    // incarnation. The scan isolates it (other instances are still published), the next outermost Capture retries it once,
    // and a failed retry ends that instance with RuntimeError through the coordinator. Other exceptions propagate as before.
    private sealed class GetterFailure : Exception { internal GetterFailure(Exception inner) : base(inner.Message, inner) { } }
    private sealed class FailureEntry { internal PNBuffBehavior Behavior; internal bool Ending, Submitted; internal Exception Error; }
    private readonly Dictionary<long, FailureEntry> failures = new Dictionary<long, FailureEntry>();
    // Keyed by InstanceId with the behavior reference as value; always compared with ReferenceEquals (never Equals/GetHashCode of
    // a behavior): an override keeps the InstanceId but starts a new incarnation.
    private readonly Dictionary<long, PNBuffBehavior> eligibleRetry = new Dictionary<long, PNBuffBehavior>(), retriedInOuter = new Dictionary<long, PNBuffBehavior>();
    private bool IsEligible(long local, PNBuffBehavior behavior) => eligibleRetry.TryGetValue(local, out var b) && ReferenceEquals(b, behavior);
    private bool WasRetried(long local, PNBuffBehavior behavior) => retriedInOuter.TryGetValue(local, out var b) && ReferenceEquals(b, behavior);
    private int captureDepth;
    public bool IsDisposed { get; private set; }
    public long Epoch { get; }
    public event Action<PNBuffWireMessage> Message;
    public event Action<PNBuffOptionalCueMessage> OptionalCue;
    public PNBuffPublisher(PNBuffCollection collection, EveEntityId target, long generation = 0, Func<double> authoritativeTime = null)
    { Epoch = generation == 0 ? Allocate(ref nextEpoch) : generation > 0 ? generation : throw new ArgumentOutOfRangeException(nameof(generation)); this.collection = collection; this.target = target; currentTime = authoritativeTime; collection.Changed += Changed; collection.Ended += Ended; collection.VisualCue += Cue; }
    private void Emit(PNBuffWireMessage message)
    { using (EveHotPathMetrics.Measure("buff.message")) { foreach (Action<PNBuffWireMessage> f in Message?.GetInvocationList() ?? Array.Empty<Delegate>()) try { f(message); } catch (Exception e) { collection.Report(e); } } }
    private PNBuffWireMessage Packet(PNBuffWireKind kind, IEnumerable<PNBuffWireState> states = null, long removed = 0, PNBuffEndReason? reason = null, IEnumerable<PNBuffCue> cues = null)
        => new PNBuffWireMessage(collection.SessionId, target, Epoch, checked(++sequence), kind, states, removed, reason, cues);
    private void Changed() => CaptureDiagnostic(currentTime?.Invoke() ?? now, entityScale, rawScale, "Changed");
    public void Capture(double authoritativeNow, double scale = 1, double raw = 1)
        => CaptureDiagnostic(authoritativeNow, scale, raw, "External");
    internal void CaptureDiagnostic(double authoritativeNow, double scale, double raw, string origin)
    {
        bool outer = captureDepth == 0;
        if (outer && failures.Count > 0)
        {
            // Retry eligibility is fixed at the start of the outermost Capture: a failure recorded later waits for the next one.
            eligibleRetry.Clear(); retriedInOuter.Clear();
            foreach (var pair in failures) if (!pair.Value.Ending) eligibleRetry[pair.Key] = pair.Value.Behavior;
        }
        captureDepth++;
        try
        {
            CaptureCore(authoritativeNow, scale, raw, origin, outer);
            if (outer) SubmitDeferredEnds();
        }
        finally
        {
            captureDepth--;
            if (captureDepth == 0) { eligibleRetry.Clear(); retriedInOuter.Clear(); }
        }
    }
    private void CaptureCore(double authoritativeNow, double scale, double raw, string origin, bool outer)
    {
        using (EveEngine.EveHotPathMetrics.Measure("buff.publisher"))
        {
        if (IsDisposed) throw new ObjectDisposedException(nameof(PNBuffPublisher));
        bool diagnostic = EveHotPathMetrics.Active;
        long beforeSequence = sequence;
        if (diagnostic)
        {
            EveHotPathMetrics.Increment("capture.calls." + origin);
            if (origin == "Changed") EveHotPathMetrics.Record(new EveHotPathMetrics.Event { Kind = "Capture", Origin = origin, Session = collection.SessionId, Target = target.Value, Epoch = Epoch, Sequence = sequence, Causes = (long)collection.CurrentDiagnosticCause });
        }
        double gameTime = collection.ReplicationGameTime;
        bool aligned = Math.Abs((authoritativeNow - now) - (gameTime - lastObservedGameTime)) <= 1e-8 * Math.Max(1, Math.Abs(gameTime));
        bool unchanged = lastObservedRevision == collection.ReplicationRevision && entityScale == scale && rawScale == raw;
        now = authoritativeNow; entityScale = scale; rawScale = raw; lastObservedGameTime = gameTime;
        if (unchanged && aligned && collection.HasOnlyEventDrivenReplication)
        {
            if (outer && eligibleRetry.Count > 0) { RetryPass(lastAnchor < 0, scale, raw, diagnostic, origin); return; }
            if (diagnostic) { EveHotPathMetrics.Increment("capture.skip." + origin); EveHotPathMetrics.Increment("capture.no-message." + origin); }
            return;
        }
        lastObservedRevision = collection.ReplicationRevision;
        bool anchor = lastAnchor < 0;
        PNBuffInstance[] captured;
        using (EveHotPathMetrics.Measure("publisher.scan.snapshot")) captured = collection.ReplicationInstances();
        if (diagnostic) { EveHotPathMetrics.Increment("capture.scan." + origin); EveHotPathMetrics.Increment("capture.snapshot-instances." + origin, captured.Length); }
        foreach (var instance in captured)
        {
            if (IsDisposed) break;
            EvaluateAndPublish(instance, anchor, scale, raw, diagnostic, origin);
        }
        if (anchor && !IsDisposed) lastAnchor = now;
        if (diagnostic && sequence == beforeSequence) EveHotPathMetrics.Increment("capture.no-message." + origin);
    
        }
    }
    // Re-evaluates only the incarnations that were Pending when the outermost Capture started (the gate would skip the scan).
    private void RetryPass(bool anchor, double scale, double raw, bool diagnostic, string origin)
    {
        foreach (var instance in collection.ReplicationInstances())
        {
            if (IsDisposed) break;
            long local = instance.Handle.InstanceId;
            if (IsEligible(local, instance.Behavior) && !WasRetried(local, instance.Behavior)) EvaluateAndPublish(instance, anchor, scale, raw, diagnostic, origin);
        }
    }
    private void EvaluateAndPublish(PNBuffInstance instance, bool anchor, double scale, double raw, bool diagnostic, string origin)
    {
        var local = instance.Handle.InstanceId;
        if (!identities.TryGetValue(local, out var id)) identities.Add(local, id = Allocate(ref nextIdentity));
        // Role is fixed here and decides the outcome; the entry state observed after the evaluation never does.
        var behavior = instance.Behavior;
        bool retry = false;
        if (failures.TryGetValue(local, out var entry))
        {
            if (!ReferenceEquals(entry.Behavior, behavior)) { failures.Remove(local); entry = null; }   // new incarnation: Healthy
            else if (entry.Ending || !IsEligible(local, behavior) || WasRetried(local, behavior)) return;   // not visited
            else { retry = true; retriedInOuter[local] = behavior; }   // consumed before the getter runs (re-entry sees it)
        }
        previous.TryGetValue(local, out var old);
        PNBuffWireState state;
        try { state = Evaluate(instance, old, id, anchor, scale, raw, diagnostic, origin); }
        catch (GetterFailure failure)
        {
            var error = failure.InnerException;
            failures.TryGetValue(local, out var current);
            bool same = current != null && ReferenceEquals(current.Behavior, behavior);
            if (retry) { if (same && !current.Ending) { current.Ending = true; current.Error = error; } }
            else if (current == null) failures[local] = new FailureEntry { Behavior = behavior };
            // Recorded before Report: a Failed listener may call Capture.
            collection.Report(error);
            return;
        }
        if (retry && failures.TryGetValue(local, out var recovered) && ReferenceEquals(recovered.Behavior, behavior) && !recovered.Ending) failures.Remove(local);
        if (state == null) return;
        if (diagnostic) { EveHotPathMetrics.Increment("capture.emitted." + origin); RecordState(old, state, origin); }
        previous[local] = state; Emit(Packet(old == null ? PNBuffWireKind.Added : PNBuffWireKind.Updated, new[] { state }));
    }
    // Baseline compare/state construction; returns null when nothing changed. Publishes nothing.
    private PNBuffWireState Evaluate(PNBuffInstance instance, PNBuffWireState old, long id, bool anchor, double scale, double raw, bool diagnostic, string origin)
    {
        using (EveHotPathMetrics.Measure("publisher.scan.compare-state"))
        {
        if (diagnostic) EveHotPathMetrics.Increment("capture.inspected." + origin);
        var writer = instance.Behavior.Presentation;
        double rate = instance.Suspended ? 0 : instance.Clock == PNBuffClock.Game ? 1 : instance.Clock == PNBuffClock.Entity ? scale : raw;
        var data = writer.DynamicData;
        if (old != null && !anchor && old.ProfileId == instance.Profile.Id && old.Stacks == StackCount(instance) && old.Suspended == instance.Suspended &&
            old.Numbers.Equals(writer.ReplicatedNumbers) && old.Rate == rate && old.Clock == instance.Clock && old.Expiration == instance.Expiration &&
            Math.Abs(old.VisualTime(now) - instance.Time) <= 1e-8 * Math.Max(1, Math.Abs(instance.Time)) && old.MatchesDynamic(data, writer.PayloadVersion)) return null;
        return new PNBuffWireState(id, old == null ? 1 : checked(old.Revision + 1), instance.Id, instance.Profile.Id, instance.Source.EntityId,
            StackCount(instance), instance.Suspended, writer.ReplicatedNumbers, instance.Clock, instance.Time, now, rate, instance.Expiration, data, writer.PayloadVersion);
        }
    }
    private static double? StackCount(PNBuffInstance instance)
    { try { return instance.Behavior.StackCount; } catch (Exception error) { throw new GetterFailure(error); } }
    // Outermost Capture only, after the scan. Submitted is set before Submit because an idle coordinator runs synchronously.
    private void SubmitDeferredEnds()
    {
        if (failures.Count == 0) return;
        foreach (var pair in failures.ToArray())
        {
            var entry = pair.Value; long local = pair.Key;
            if (!entry.Ending || entry.Submitted || IsDisposed) continue;
            entry.Submitted = true;
            collection.EndAfterReplicationFailure(local, entry.Behavior, entry.Error, outcome =>
            {
                if (!failures.TryGetValue(local, out var current) || !ReferenceEquals(current, entry)) return;
                if (outcome == PNBuffReplicationEndOutcome.Stale) failures.Remove(local);
                else if (outcome == PNBuffReplicationEndOutcome.NotAllowed) entry.Submitted = false;   // next outermost Capture
            });
        }
    }
    private void RecordState(PNBuffWireState old, PNBuffWireState state, string origin, long reasons = 0)
    {
        using (EveHotPathMetrics.Measure("diagnostic.state-diff"))
        EveHotPathMetrics.Record(new EveHotPathMetrics.Event { Kind = old == null ? "Added" : "Updated", Origin = origin,
            Session = collection.SessionId, Target = target.Value, Epoch = Epoch, Instance = state.InstanceId,
            Sequence = sequence + 1, Revision = state.Revision, Reasons = reasons | (old == null ? 0 : old.DiagnosticDifference(state, now) | (old.DiagnosticNumberSlots(state) << 16)), Causes = origin == "Changed" ? (long)collection.CurrentDiagnosticCause : 0 });
    }
    private void Ended(PNBuffHandle handle, PNBuffEndReason reason)
    {
        failures.Remove(handle.InstanceId);
        if (!previous.TryGetValue(handle.InstanceId, out var old)) return;
        previous.Remove(handle.InstanceId); endingCues.TryGetValue(handle.InstanceId, out var cues); endingCues.Remove(handle.InstanceId);
        if (EveHotPathMetrics.Active) EveHotPathMetrics.Record(new EveHotPathMetrics.Event { Kind = "Removed", Session = collection.SessionId, Target = target.Value, Epoch = Epoch, Instance = old.InstanceId, Sequence = sequence + 1 });
        Emit(Packet(PNBuffWireKind.Removed, removed: old.InstanceId, reason: reason, cues: cues));
    }
    private void Cue(PNBuffHandle handle, string name, bool guaranteed, JToken data)
    {
        CaptureDiagnostic(currentTime?.Invoke() ?? now, entityScale, rawScale, "Cue");
        if (!previous.TryGetValue(handle.InstanceId, out var state)) return;
        var cue = new PNBuffCue(checked(++cueId), state.InstanceId, name, now, payload: data);
        if (EveHotPathMetrics.Active) EveHotPathMetrics.Record(new EveHotPathMetrics.Event { Kind = guaranteed ? "GuaranteedCue" : "OptionalCue", Session = collection.SessionId, Target = target.Value, Epoch = Epoch, Instance = state.InstanceId, Sequence = sequence });
        if (guaranteed && !collection.Contains(handle))
        { if (!endingCues.TryGetValue(handle.InstanceId, out var cues)) endingCues.Add(handle.InstanceId, cues = new List<PNBuffCue>()); cues.Add(cue); }
        else if (guaranteed)
        {
            var updated = new PNBuffWireState(state.InstanceId, checked(state.Revision + 1), state.BuffId, state.ProfileId, state.SourceId, state.Stacks, state.Suspended, state.Numbers, state.Clock, state.BuffTime, state.AnchorTime, state.Rate, state.Expiration, state.DynamicData, state.PayloadVersion);
            if (EveHotPathMetrics.Active) RecordState(state, updated, "GuaranteedCue", (long)PNBuffDiagnosticReason.GuaranteedCue);
            previous[handle.InstanceId] = updated; Emit(Packet(PNBuffWireKind.Updated, new[] { updated }, cues: new[] { cue }));
        }
        else foreach (Action<PNBuffOptionalCueMessage> f in OptionalCue?.GetInvocationList() ?? Array.Empty<Delegate>()) try { f(new PNBuffOptionalCueMessage(collection.SessionId, target, Epoch, cue)); } catch (Exception e) { collection.Report(e); }
    }
    public PNBuffWireMessage Snapshot()
    { if (IsDisposed) throw new ObjectDisposedException(nameof(PNBuffPublisher)); return new PNBuffWireMessage(collection.SessionId, target, Epoch, sequence, PNBuffWireKind.Snapshot, previous.Values); }
    public void Dispose()
    { if (IsDisposed) return; IsDisposed = true; collection.Changed -= Changed; collection.Ended -= Ended; collection.VisualCue -= Cue; Emit(Packet(PNBuffWireKind.Closed)); Message = null; OptionalCue = null; previous.Clear(); identities.Clear(); endingCues.Clear(); failures.Clear(); }
}
