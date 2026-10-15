using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EveEngine;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PNBuffManager : MonoBehaviour, EveIEntityLoadStep, EveIEntityLoadPreparation, EveIEntityCommitStep, EveIEntityReplicationPart, EveIFrozenEntityReplicationPart, EveIReplicationDeltaClassifier
{
    private static readonly ConditionalWeakTable<EveSessionContext, PNBuffMutationCoordinator> coordinators = new ConditionalWeakTable<EveSessionContext, PNBuffMutationCoordinator>();
    private PNBuffCollection collection;
    private EveSessionContext session;
    private PNStat target;
    private EveTimeScale clock;
    private bool deathObserved;
    private EveEntityLoadContext context;
    private IDisposable publication;
    public EveId ReplicationId => EveId.Parse("project_n:buff_presentation");
    public bool CanCaptureReplication => IsReady && Publisher != null && !collection.Coordinator.IsMutating;
    public event Action<byte[]> ReplicationDelta;
    public EveReplicationTraffic ClassifyReplicationDelta(byte[] payload)
    {
        if (payload == null || payload.Length < 10 || payload.Length > 1048585 || payload[8] > 1)
            throw new FormatException("Invalid Buff replication prefix.");
        return payload[8] == 1 ? EveReplicationTraffic.Optional : EveReplicationTraffic.Required;
    }
    private byte[] EncodeReplication(PNBuffWireMessage message)
        => EncodeReplication(message, presentationTime);
    private static byte[] EncodeReplication(PNBuffWireMessage message, double time)
    {
        var body = PNBuffWireCodec.Encode(message); var bytes = new byte[body.Length + 9];
        Buffer.BlockCopy(BitConverter.GetBytes(time), 0, bytes, 0, 8); bytes[8] = 0; Buffer.BlockCopy(body, 0, bytes, 9, body.Length); return bytes;
    }
    private static (double, PNBuffWireMessage) DecodeReplication(byte[] payload)
    {
        if (payload == null || payload.Length < 10 || payload.Length > 1048585 || payload[8] != 0) throw new FormatException("Buff actor payload size/kind.");
        double now = BitConverter.ToDouble(payload, 0); PNBuffCollection.Seconds(now, true);
        var body = new byte[payload.Length - 9]; Buffer.BlockCopy(payload, 9, body, 0, body.Length); return (now, PNBuffWireCodec.Decode<PNBuffWireMessage>(body));
    }
    public byte[] CaptureReplication() { if (Publisher == null) throw new InvalidOperationException("Host Buff publisher required."); return EncodeReplication(Publisher.Snapshot()); }
    public Func<byte[]> FreezeReplication()
    { if (!CanCaptureReplication) throw new InvalidOperationException("Buff capture requires drained mutation boundary."); var snapshot = Publisher.Snapshot(); double time = presentationTime; return () => EncodeReplication(snapshot, time); }
    private sealed class ReplicaStage : EveIReplicaStage
    {
        private readonly PNBuffManager owner; private readonly PNBuffReplica candidate; private readonly double now; private readonly PNBuffWireMessage packet; private readonly bool replacing;
        private IDisposable deferred; private bool committed, published;
        public ReplicaStage(PNBuffManager owner, PNBuffReplica candidate, double now, PNBuffWireMessage packet, bool replacing)
        { this.owner = owner; this.candidate = candidate; this.now = now; this.packet = packet; this.replacing = replacing; }
        public void Commit()
        { deferred = candidate.DeferPresentation(); candidate.ApplyLifecycle(packet, now); owner.replica = candidate; owner.presentationTime = now; committed = true; }
        public void Published() { published = true; deferred?.Dispose(); deferred = null; }
        public void Dispose()
        { if (!published && (replacing || committed)) { candidate.Dispose(); } deferred = null; }
    }
    public EveIReplicaStage StageReplication(byte[] payload, Guid authoritativeSession, long generation)
    {
        if (session == null || session.HasAuthority || !context.IsValid) throw new InvalidOperationException("Initialized client Buff module required.");
        var decoded = DecodeReplication(payload); var packet = decoded.Item2;
        if (packet.SessionId != authoritativeSession || packet.TargetId != target.EntityId || packet.Epoch != generation || packet.Kind != PNBuffWireKind.Snapshot) throw new FormatException("Buff snapshot identity.");
        // Validate selected profile/state without constructing presenters or changing the retained replica.
        using (var probe = new PNBuffReplica(session.Registry, authoritativeSession, target.EntityId, render: false)) probe.ApplyLifecycle(packet, decoded.Item1);
        bool replacing = replica == null; var candidate = replica ?? new PNBuffReplica(session.Registry, authoritativeSession, target.EntityId, target, RenderPresentation);
        return new ReplicaStage(this, candidate, decoded.Item1, packet, replacing);
    }
    public void ApplyReplicationDelta(byte[] payload, Guid authoritativeSession, long generation)
    {
        if (!IsReady || session.HasAuthority || replica == null) throw new InvalidOperationException("Published client presenter required.");
        if (payload != null && payload.Length >= 10 && payload.Length <= 1048585 && payload[8] == 1)
        {
            double time = BitConverter.ToDouble(payload, 0); PNBuffCollection.Seconds(time, true);
            var body = new byte[payload.Length - 9]; Buffer.BlockCopy(payload, 9, body, 0, body.Length); var cue = PNBuffWireCodec.Decode<PNBuffOptionalCueMessage>(body);
            if (cue.SessionId != authoritativeSession || cue.TargetId != target.EntityId || cue.Epoch != generation) throw new FormatException("Optional Buff cue identity.");
            presentationTime = time; replica.Receive(cue, time); return;
        }
        var decoded = DecodeReplication(payload); var packet = decoded.Item2;
        if (packet.SessionId != authoritativeSession || packet.Epoch != generation) throw new FormatException("Buff actor identity.");
        presentationTime = decoded.Item1; replica.ApplyLifecycle(packet, presentationTime);
    }
    public Exception RuntimeFailure { get; private set; }
    private PNBuffReplica replica;
    private double presentationTime;
    private bool advancingCollection;
    public PNBuffPublisher Publisher { get; private set; }
    public PNIBuffPresentationSource Presentation => IsReady ? replica : null;
    public PNBuffReplica Replica => replica;
    public Func<double> AuthoritativePresentationTime { get; set; }
    public bool RenderPresentation { get; set; } = true;
    public PNBuffCollection Buffs => IsReady ? collection : throw new InvalidOperationException("Buff manager is not loaded.");
    public bool IsReady => collection != null && !collection.IsDisposed && context != null && context.CanUse;
    public int LoadOrder => 10;
    public IEnumerable<Type> Dependencies => new[] { typeof(PNStat), typeof(PNControlManager) };
    public void PrepareEntityLoad() { if (GetComponent<PNControlManager>() == null) gameObject.AddComponent<PNControlManager>(); }
    public void Load(EveEntityLoadContext context)
    {
        if (collection != null) throw new InvalidOperationException("Buff manager already loaded.");
        this.context = context;
        target = GetComponent<PNStat>(); clock = GetComponent<EveTimeScale>(); session = context.Session; deathObserved = false; RuntimeFailure = null;
        var coordinator = coordinators.GetValue(session, _ => new PNBuffMutationCoordinator());
        var skill = GetComponent<PNSkill>();
        collection = new PNBuffCollection(context.Registry, session.SessionId, coordinator,
            new PNBuffServices(target, GetComponent<PNControlManager>().State, reason => skill != null && skill.IsReady && skill.TryInterrupt(reason)),
            () => context.CanMutate);
        collection.Failed += Report; target.Died += OnDied;
        presentationTime = 0;
    }
    public void CommitEntity(EveEntityLoadContext context)
    {
        if (session.HasAuthority)
        {
            Publisher = new PNBuffPublisher(collection, target.EntityId, context.Generation, () => Math.Max(0, presentationTime - (advancingCollection ? collection.RemainingGameDelta : 0)));
            replica = new PNBuffReplica(context.Registry, session.SessionId, target.EntityId, target, RenderPresentation);
            publication = replica.DeferPresentation();
            replica.PresenterFailed += Report;
            Publisher.Message += LocalMessage; Publisher.OptionalCue += LocalCue;
            replica.ApplyLifecycle(Publisher.Snapshot(), presentationTime);
        }
    }
    public void EntityPublished(EveEntityLoadContext context)
    { publication?.Dispose(); publication = null; if (session.HasAuthority && !isActiveAndEnabled) collection.Suspend(true); }
    private void LocalMessage(PNBuffWireMessage message)
    {
        if (!context.CanUse || message.Kind == PNBuffWireKind.Closed) return;
        using (EveHotPathMetrics.Measure("buff.local-presentation")) replica?.ApplyLifecycle(message, presentationTime);
        var listeners = ReplicationDelta; if (listeners == null) return;
        using (EveHotPathMetrics.Measure("buff.encode"))
        {
            byte[] data; using (EveHotPathMetrics.Measure("buff.codec")) data = EncodeReplication(message);
            if (EveHotPathMetrics.Active) { EveHotPathMetrics.Increment("wire.buff.messages." + message.Kind); EveHotPathMetrics.Increment("wire.buff.body-bytes", data.Length - 9); }
            using (EveHotPathMetrics.Measure("fanout.observers")) foreach (Action<byte[]> observer in listeners.GetInvocationList()) try { observer(data); } catch (Exception error) { Report(error); }
        }
    }
    private void LocalCue(PNBuffOptionalCueMessage message)
    {
        if (!context.CanUse) return; using (EveHotPathMetrics.Measure("buff.local-cue-presentation")) replica?.Receive(message, presentationTime);
        var listeners = ReplicationDelta; if (listeners == null) return;
        byte[] bytes;
        using (EveHotPathMetrics.Measure("cue.codec"))
        { var body = PNBuffWireCodec.Encode(message); bytes = new byte[body.Length + 9]; Buffer.BlockCopy(BitConverter.GetBytes(presentationTime), 0, bytes, 0, 8); bytes[8] = 1; Buffer.BlockCopy(body, 0, bytes, 9, body.Length); }
        if (EveHotPathMetrics.Active) { EveHotPathMetrics.Increment("wire.cue.messages"); EveHotPathMetrics.Increment("wire.cue.body-bytes", bytes.Length - 9); }
        using (EveHotPathMetrics.Measure("fanout.cue-observers")) foreach (Action<byte[]> observer in listeners.GetInvocationList()) try { observer(bytes); } catch (Exception error) { Report(error); }
    }
    // Called only after the network adapter authenticates the host/session and maps this target.
    public void BindClientPresentation(Guid authoritativeSession, EveEntityId targetId, PNBuffReplicaPolicy policy = null)
    {
        if (!IsReady || session.HasAuthority || targetId != target.EntityId) throw new InvalidOperationException("Bind requires a loaded client target.");
        replica?.Dispose(); replica = new PNBuffReplica(session.Registry, authoritativeSession, targetId, target, RenderPresentation, policy); replica.PresenterFailed += Report;
        replica.Failed += (code, message) => session.FailConnection(code.ToString(), message);
    }
    private void Report(Exception error) => Debug.LogException(error, this);
    private void OnDied(PNDamageResult result) { if (IsReady && session.HasAuthority && !deathObserved) { deathObserved = true; collection.TargetDied(); } }
    public void Advance(double delta)
    {
        PNBuffCollection.Seconds(delta, true);
        if (!IsReady || !session.HasAuthority) return;
        var scale = clock != null && clock.isActiveAndEnabled ? clock.Scale : 1;
        var raw = clock != null && clock.isActiveAndEnabled ? Math.Max(0, clock.RawScale) : 1;
        Publisher?.CaptureDiagnostic(presentationTime, scale, raw, "Advance-pre");
        presentationTime = PNBuffCollection.Sum(presentationTime, delta);
        collection.Suspend(!isActiveAndEnabled);
        if (!isActiveAndEnabled) return;
        if (target.IsDead && !deathObserved) { deathObserved = true; collection.TargetDied(); }
        else if (!target.IsDead) deathObserved = false;
        advancingCollection = true;
        try { collection.Advance(delta, clock != null && clock.isActiveAndEnabled ? clock.Scale : 1, clock != null && clock.isActiveAndEnabled ? Math.Max(0, clock.RawScale) : 1); }
        finally { advancingCollection = false; }
        Publisher?.CaptureDiagnostic(presentationTime, clock != null && clock.isActiveAndEnabled ? clock.Scale : 1, clock != null && clock.isActiveAndEnabled ? Math.Max(0, clock.RawScale) : 1, "Advance-post");
        replica?.Advance(AuthoritativePresentationTime?.Invoke() ?? presentationTime);
    }
    private void Update() => AdvanceAutomatically(Time.deltaTime);
    // Same fault boundary for Unity Update and explicit/headless runtime schedulers.
    public void AdvanceAutomatically(double delta)
    {
        PNBuffCollection.Seconds(delta, true);
        if (RuntimeFailure != null) return;
        if (!IsReady) return;
        try
        {
            if (session != null && !session.HasAuthority) presentationTime = PNBuffCollection.Sum(presentationTime, delta);
            Advance(delta);
            if (session != null && !session.HasAuthority) replica?.Advance(AuthoritativePresentationTime?.Invoke() ?? presentationTime);
        }
        catch (Exception error)
        {
            RuntimeFailure = error;
            Report(error);
            try { collection?.Dispose(PNBuffEndReason.RuntimeError); } catch (Exception cleanup) { Report(cleanup); }
            try { Publisher?.Dispose(); Publisher = null; replica?.Dispose(); } catch (Exception cleanup) { Report(cleanup); }
        }
    }
    private void OnDisable() { if (IsReady && session.HasAuthority) collection.Suspend(true); }
    private void OnEnable() { if (IsReady && session.HasAuthority) collection.Suspend(false); }
    public void Unload()
    {
        if (target != null) target.Died -= OnDied;
        collection?.Dispose(session != null && !session.IsActive ? PNBuffEndReason.SessionEnded : PNBuffEndReason.Unload);
        Publisher?.Dispose(); Publisher = null; replica?.Dispose(); replica = null;
        collection = null; target = null; clock = null; session = null;
        context = null; publication = null;
        ReplicationDelta = null;
    }
    private void OnDestroy() => Unload();
}
