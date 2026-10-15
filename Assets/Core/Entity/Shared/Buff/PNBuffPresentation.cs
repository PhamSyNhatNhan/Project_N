using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

public readonly struct PNBuffNumbers : IEquatable<PNBuffNumbers>
{
    public long? TopLeft { get; }
    public long? TopRight { get; }
    public long? BottomLeft { get; }
    public long? BottomRight { get; }
    public PNBuffNumbers(long? topLeft = null, long? topRight = null, long? bottomLeft = null, long? bottomRight = null)
    { TopLeft = topLeft; TopRight = topRight; BottomLeft = bottomLeft; BottomRight = bottomRight; }
    public bool Equals(PNBuffNumbers other) => TopLeft == other.TopLeft && TopRight == other.TopRight && BottomLeft == other.BottomLeft && BottomRight == other.BottomRight;
    public override bool Equals(object other) => other is PNBuffNumbers numbers && Equals(numbers);
    public override int GetHashCode() => TopLeft.GetHashCode() ^ TopRight.GetHashCode() ^ BottomLeft.GetHashCode() ^ BottomRight.GetHashCode();
}

public enum PNBuffPresentationChangeKind { Added, Updated, Removed, Reset }

public sealed class PNBuffPresentationSnapshot
{
    public PNBuffHandle Handle { get; }
    public long InstanceId { get; }
    public EveId Id { get; }
    public EveId ProfileId { get; }
    public bool ShowInUI { get; }
    public bool Suspended { get; }
    public string IconPath { get; }
    public string Glow { get; }
    public PNBuffNumbers Numbers { get; }
    public long GroupOrder { get; }
    public long Sequence { get; }
    internal PNBuffPresentationSnapshot(PNBuffInstance instance, PNBuffNumbers numbers, long order, long sequence)
    {
        Handle = instance.Handle; InstanceId = Handle.InstanceId; Id = instance.Id; ProfileId = instance.Profile.Id;
        ShowInUI = instance.Profile.ShowInUI; Suspended = instance.Suspended;
        IconPath = instance.Profile.IconPath; Glow = instance.Profile.Glow;
        Numbers = numbers; GroupOrder = order; Sequence = sequence;
    }
    internal bool Matches(PNBuffInstance instance, PNBuffNumbers numbers) => ProfileId == instance.Profile.Id &&
        ShowInUI == instance.Profile.ShowInUI && Suspended == instance.Suspended && IconPath == instance.Profile.IconPath &&
        Glow == instance.Profile.Glow && Numbers.Equals(numbers);
    // `shown` carries replica-projected Numbers (countdown opt-in); null keeps the wire Numbers.
    internal PNBuffPresentationSnapshot(PNBuffWireState state, PNBuffDefinition profile, long order, long sequence, PNBuffNumbers? shown = null)
    {
        InstanceId = state.InstanceId; Id = state.BuffId; ProfileId = state.ProfileId;
        ShowInUI = profile.ShowInUI; Suspended = state.Suspended; IconPath = profile.IconPath; Glow = profile.Glow;
        Numbers = shown ?? state.Numbers; GroupOrder = order; Sequence = sequence;
    }
}

public readonly struct PNBuffPresentationChange
{
    public PNBuffPresentationChangeKind Kind { get; }
    public PNBuffPresentationSnapshot State { get; }
    public long Sequence { get; }
    internal PNBuffPresentationChange(PNBuffPresentationChangeKind kind, PNBuffPresentationSnapshot state, long sequence)
    { Kind = kind; State = state; Sequence = sequence; }
}

// A writer belongs to one behavior incarnation, never to a UI document.
public sealed class PNBuffPresentationWriter
{
    private readonly PNBuffCollection collection;
    internal readonly PNBuffInstance Instance;
    internal PNBuffNumbers Numbers;
    internal JToken DynamicData;
    internal int PayloadVersion = 1;
    public void PublishVisual(JToken data, int version = 1)
    {
        if (!IsActive) return;
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        DynamicData = data?.DeepClone(); PayloadVersion = version; collection.VisualPublished();
    }
    public void Cue(string name, bool guaranteed = false, JToken data = null)
    { if (IsActive) { var payload = data?.DeepClone(); collection.Coordinator.Submit(() => { if (IsActive) collection.PublishCue(Instance.Handle, name, guaranteed, payload); }); } }
    private Action<long?> remainingChanged;
    private long? lastSeconds;
    private bool hasSeconds;
    // Opt-in countdown of the effective profile at bind time. A writer belongs to one behavior incarnation, so a
    // profile override creates a new writer with its own descriptor and tracker state.
    private readonly PNBuffCountdown countdown;
    private long? trackedSeconds;
    private bool overflowReported;
    internal bool Active = true;
    internal PNBuffPresentationWriter(PNBuffCollection collection, PNBuffInstance instance)
    { this.collection = collection; Instance = instance; countdown = instance.Profile?.Countdown; }
    // Numbers as replicated: the countdown slot is null while running (replicas derive it from clock state) and
    // carries the host's last tracked value while suspended. Raw Numbers keep what the behavior published.
    internal PNBuffNumbers ReplicatedNumbers => countdown == null ? Numbers : countdown.With(Numbers, Instance.Suspended ? trackedSeconds : (long?)null);
    public bool IsActive => Active && !collection.IsDisposed && !Instance.Ending && !Instance.PendingBehaviorStart && ReferenceEquals(Instance.Behavior.Presentation, this);
    public void Publish(PNBuffNumbers numbers)
    {
        if (!IsActive) return;
        if (Numbers.Equals(numbers)) return;
        Numbers = numbers;
        collection.ReplicationChanged(PNBuffCollection.DiagnosticCause.Numbers);
        collection.PresentationPublished();
    }
    public void WatchRemainingSeconds(Action<long?> changed)
    {
        if (!IsActive) return;
        if (changed == null) throw new ArgumentNullException(nameof(changed));
        // Opt-in profiles are tracked independently of this legacy callback, which is never invoked for them.
        if (countdown != null) return;
        remainingChanged = changed; hasSeconds = false;
    }
    private void RefreshCountdown()
    {
        if (!IsActive || Instance.Suspended || Instance.PendingBehaviorStart) return;
        trackedSeconds = PNBuffCountdownMath.Compute(Instance.Expiration, Instance.Time, out bool overflow);
        if (overflow && !overflowReported)
        { overflowReported = true; collection.Report(new OverflowException("Buff presentation seconds exceed Int64.")); }
    }
    internal void RefreshSeconds()
    {
        if (countdown != null) { RefreshCountdown(); return; }
        if (!IsActive || Instance.Suspended || Instance.PendingBehaviorStart || remainingChanged == null) return;
        long? seconds = null;
        if (Instance.Expiration.HasValue)
        {
            double value = Math.Ceiling(Math.Max(0, Instance.Expiration.Value - Instance.Time));
            if (value >= 9223372036854775808d) { collection.Report(new OverflowException("Buff presentation seconds exceed Int64.")); remainingChanged = null; return; }
            if (value > 0) seconds = (long)value;
        }
        if (hasSeconds && lastSeconds == seconds) return;
        hasSeconds = true; lastSeconds = seconds;
        try { remainingChanged(seconds); } catch (Exception error) { remainingChanged = null; collection.Report(error); }
    }
    internal void Retire() { Active = false; remainingChanged = null; }
}

public sealed partial class PNBuffCollection
{
    private readonly Dictionary<PNBuffHandle, PNBuffPresentationSnapshot> presentation = new Dictionary<PNBuffHandle, PNBuffPresentationSnapshot>();
    private readonly Dictionary<PNBuffHandle, PNBuffPresentationWriter> writers = new Dictionary<PNBuffHandle, PNBuffPresentationWriter>();
    private readonly Dictionary<EveId, long> presentationGroups = new Dictionary<EveId, long>();
    private long presentationSequence, nextPresentationGroup;
    private bool syncingPresentation;
    private bool presentationQueued;
    // SyncPresentation scratch (S1). Used only while `syncingPresentation` is true, so reentrant syncs never share it;
    // cleared in finally so no handle/writer reference is retained between calls.
    private readonly List<KeyValuePair<PNBuffHandle, PNBuffPresentationWriter>> writerScratch = new List<KeyValuePair<PNBuffHandle, PNBuffPresentationWriter>>();
    private readonly List<KeyValuePair<PNBuffHandle, PNBuffPresentationSnapshot>> presentationScratch = new List<KeyValuePair<PNBuffHandle, PNBuffPresentationSnapshot>>();
    private readonly List<EveId> groupScratch = new List<EveId>();
    private readonly HashSet<EveId> liveGroupScratch = new HashSet<EveId>();
    public event Action<PNBuffPresentationChange> PresentationChanged;
    public IReadOnlyList<PNBuffPresentationSnapshot> PresentationSnapshot() => Array.AsReadOnly(presentation.Values
        .OrderBy(x => x.GroupOrder).ThenBy(x => x.Handle.InstanceId).ToArray());
    private void BindPresentation(PNBuffInstance instance)
    {
        if (writers.TryGetValue(instance.Handle, out var old)) old.Retire();
        var writer = new PNBuffPresentationWriter(this, instance);
        instance.Behavior.Presentation = writer; writers[instance.Handle] = writer;
    }
    internal void PresentationPublished()
    {
        if (presentationQueued) return;
        presentationQueued = true;
        Coordinator.Submit(() => { presentationQueued = false; SyncPresentation(); });
    }
    private void EmitPresentation(PNBuffPresentationChangeKind kind, PNBuffPresentationSnapshot state)
    {
        var change = new PNBuffPresentationChange(kind, state, ++presentationSequence);
        foreach (Action<PNBuffPresentationChange> listener in PresentationChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { listener(change); } catch (Exception error) { Report(error); }
    }
    private void SyncPresentation()
    {
        using (EveEngine.EveHotPathMetrics.Measure("buff.presentation"))
        {
        if (syncingPresentation) return;
        syncingPresentation = true;
        try
        {
            // Same enumeration order and lazy Contains checks as the former ToArray() copies, without per-call arrays.
            writerScratch.AddRange(writers);
            for (int i = 0; i < writerScratch.Count; i++)
            { var pair = writerScratch[i]; if (!Contains(pair.Key)) { pair.Value.Retire(); writers.Remove(pair.Key); } }
            presentationScratch.AddRange(presentation);
            for (int i = 0; i < presentationScratch.Count; i++)
            {
                var pair = presentationScratch[i];
                if (!Contains(pair.Key)) { presentation.Remove(pair.Key); EmitPresentation(PNBuffPresentationChangeKind.Removed, pair.Value); }
            }
            if (presentationGroups.Count > 0)
            {
                // One liveness pass at the moment of the former per-group scans (no callbacks run between).
                foreach (var value in instances.Values) if (!value.Ending) liveGroupScratch.Add(value.Id);
                groupScratch.AddRange(presentationGroups.Keys);
                for (int i = 0; i < groupScratch.Count; i++) if (!liveGroupScratch.Contains(groupScratch[i])) presentationGroups.Remove(groupScratch[i]);
            }
            foreach (var instance in OrderedInstanceSnapshot(false))
            {
                if (!Contains(instance.Handle) || !writers.TryGetValue(instance.Handle, out var writer)) continue;
                writer.RefreshSeconds();
                if (!presentationGroups.TryGetValue(instance.Id, out var order)) presentationGroups.Add(instance.Id, order = ++nextPresentationGroup);
                bool existed = presentation.TryGetValue(instance.Handle, out var previous);
                if (existed && previous.Matches(instance, writer.Numbers)) continue;
                var state = new PNBuffPresentationSnapshot(instance, writer.Numbers, order, presentationSequence + 1);
                presentation[instance.Handle] = state;
                EmitPresentation(existed ? PNBuffPresentationChangeKind.Updated : PNBuffPresentationChangeKind.Added, state);
            }
        }
        finally
        {
            syncingPresentation = false;
            writerScratch.Clear(); presentationScratch.Clear(); groupScratch.Clear(); liveGroupScratch.Clear();
        }
    
        }
    }
    private void RefreshPresentationSeconds()
    {
        // Invoked inside the coordinator: callback mutations are queued, so enumeration
        // remains stable and all changed numeric writers coalesce into one sync.
        foreach (var writer in writers.Values) writer.RefreshSeconds();
    }
    private void ClearPresentation()
    {
        foreach (var writer in writers.Values) writer.Retire(); writers.Clear(); presentation.Clear(); presentationGroups.Clear();
        EmitPresentation(PNBuffPresentationChangeKind.Reset, null); PresentationChanged = null;
    }
}
