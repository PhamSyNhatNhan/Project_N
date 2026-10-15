using System;
using System.Collections.Generic;
using System.Linq;

namespace EveEngine.Experimental.Lifecycle
{
    // One connection only; no session-global shutdown or player-disconnect decision.
    public sealed class HostModel
    {
        private readonly ModelPolicy policy;
        private readonly Guid session;
        private readonly long incarnation;
        private readonly Queue<Envelope> outgoing = new Queue<Envelope>(), controls = new Queue<Envelope>(), held = new Queue<Envelope>();
        private Envelope[] pending;
        private byte[][] pendingBytes;
        private int pendingControlCount, queuedBytes, heldBytes;
        private double now, started, cycleStarted, roundStarted, endAcceptedAt;
        private long controlQueuedBytes;
        public long ControlQueuedBytes => controlQueuedBytes;
        public OptionalReplicationPolicy OptionalPolicy { get; }
        public int OptionalQueuedMessages { get; private set; }
        public long OptionalQueuedBytes { get; private set; }
        public long OptionalOffered { get; private set; }
        public long OptionalAdmitted { get; private set; }
        public long OptionalDroppedNotLive { get; private set; }
        public long OptionalDroppedBudget { get; private set; }
        public long OptionalDroppedReserve { get; private set; }
        public long OptionalDroppedCommitment { get; private set; }
        public int OptionalHighMessages { get; private set; }
        public long OptionalHighBytes { get; private set; }
        public long RecoveryStarts { get; private set; }
        public long QueueOverflows { get; private set; }
        public long RequiredEnqueued { get; private set; }
        public long RequiredAccepted { get; private set; }
        private int rounds;
        private bool submitting;
        private long snapshotCounter;
        public HostPhase Phase { get; private set; } = HostPhase.Handshake;
        public ErrorCode? Failure { get; private set; }
        public long SentSequence { get; private set; }
        public long SnapshotId { get; private set; }
        public long EndSequence { get; private set; }
        public long AcceptedBytes { get; private set; }
        public int Round => rounds;
        public int BufferedMessages => outgoing.Count(x => x.Kind >= MessageKind.Spawn) + held.Count;
        public int BufferedBytes => queuedBytes + heldBytes;
        public int ControlCount => controls.Count;
        public int IgnoredSnapshotRequests { get; private set; }
        public int RejectedIntents { get; private set; }
        public HostModel(Guid session, long incarnation, ModelPolicy policy, double nowMs)
            : this(session, incarnation, policy, nowMs, null) { }
        public HostModel(Guid session, long incarnation, ModelPolicy policy, double nowMs, OptionalReplicationPolicy optionalPolicy)
        {
            optionalPolicy?.Validate(policy);
            OptionalPolicy = optionalPolicy;
            if (session == Guid.Empty || incarnation <= 0) throw new ArgumentException();
            this.session = session; this.incarnation = incarnation; this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
            AdvanceClock(nowMs); started = nowMs;
        }
        private void AdvanceClock(double time)
        { if (submitting) throw new InvalidOperationException("Reentrant transport callback."); if (double.IsNaN(time) || double.IsInfinity(time) || time < now) throw new ArgumentOutOfRangeException(nameof(time)); now = time; }
        public void Admit(bool contentMatches, bool policyMatches, double nowMs)
        {
            AdvanceClock(nowMs); if (Phase != HostPhase.Handshake) throw new InvalidOperationException();
            if (!contentMatches) { Close(ErrorCode.ContentMismatch); return; }
            if (!policyMatches) { Close(ErrorCode.PolicyMismatch); return; }
            BeginRecovery();
        }
        public void RequestSnapshot(double nowMs, long failedSnapshotId = 0)
        {
            AdvanceClock(nowMs);
            if (Phase == HostPhase.Closed || Phase == HostPhase.Handshake) return;
            // A request about a partially received current snapshot is not an ack failure.
            if (Phase == HostPhase.WaitCapture || Phase == HostPhase.PreparingSnapshot || Phase == HostPhase.SendingSnapshot || Phase == HostPhase.AwaitApplied)
            { IgnoredSnapshotRequests++; return; }
            BeginRecovery();
        }
        private void BeginRecovery()
        {
            bool existing = Phase != HostPhase.Live && Phase != HostPhase.Handshake;
            if (!existing) { rounds = 0; cycleStarted = now; }
            if (rounds >= policy.MaximumRounds || now - cycleStarted >= policy.CycleMs) { Close(ErrorCode.RecoveryExhausted); return; }
            rounds++; roundStarted = now;
            RecoveryStarts++;
            outgoing.Clear(); held.Clear(); queuedBytes = heldBytes = 0;
            OptionalQueuedMessages = 0; OptionalQueuedBytes = 0;
            // Controls are still queued (pending was only a view); accepted bytes cannot be recalled.
            pending = null; pendingBytes = null; EndSequence = 0;
            Phase = HostPhase.WaitCapture;
        }
        public void Capture(IEnumerable<ActorState> actors, double nowMs)
        {
            AdvanceClock(nowMs); if (Phase != HostPhase.WaitCapture && Phase != HostPhase.PreparingSnapshot) throw new InvalidOperationException();
            byte[] body;
            try { body = ModelCodec.EncodeActors(actors, policy); }
            catch (FormatException) { Close(ErrorCode.SnapshotTooLarge); return; }
            SnapshotId = checked(++snapshotCounter);
            int count = checked((int)(((long)body.Length + policy.ChunkBytes - 1) / policy.ChunkBytes));
            if (count > policy.MaximumChunks) { Close(ErrorCode.SnapshotTooLarge); return; }
            outgoing.Enqueue(Packet(MessageKind.SnapshotStart, ModelCodec.Hash(body), snapshot: SnapshotId, index: body.Length, count: count));
            for (int i = 0; i < count; i++)
            {
                var slice = new byte[Math.Min(policy.ChunkBytes, body.Length - i * policy.ChunkBytes)];
                Buffer.BlockCopy(body, i * policy.ChunkBytes, slice, 0, slice.Length);
                outgoing.Enqueue(Packet(MessageKind.SnapshotChunk, slice, snapshot: SnapshotId, index: i, count: count));
            }
            outgoing.Enqueue(Packet(MessageKind.SnapshotEnd, Array.Empty<byte>(), snapshot: SnapshotId, count: count));
            Phase = HostPhase.SendingSnapshot;
        }
        public void BeginSnapshotCapture(double nowMs)
        { AdvanceClock(nowMs); if (Phase != HostPhase.WaitCapture) throw new InvalidOperationException(); Phase = HostPhase.PreparingSnapshot; }
        private Envelope Packet(MessageKind kind, byte[] data, PresenceKey key = default, long snapshot = 0, int index = 0, int count = 0)
            => new Envelope(session, incarnation, 0, kind, data, key, snapshot, index, count);
        public void Delta(MessageKind kind, PresenceKey key, byte[] payload, double nowMs)
            => DeltaCore(kind, key, payload, nowMs, false);
        internal void DeltaOwned(MessageKind kind, ActorState state, double nowMs)
            => DeltaCore(kind, state.Key, state.Bytes, nowMs, true);
        private void DeltaCore(MessageKind kind, PresenceKey key, byte[] payload, double nowMs, bool owned)
        {
            AdvanceClock(nowMs);
            if (kind < MessageKind.Spawn || kind == MessageKind.IntentResult) throw new ArgumentException("Gameplay message required.");
            if (Phase == HostPhase.Closed || Phase == HostPhase.Handshake || Phase == HostPhase.WaitCapture) return;
            var packet = new Envelope(session, incarnation, 0, kind, payload, key, 0, 0, 0, owned);
            if (packet.Bytes.Length > policy.MaximumPayloadBytes) throw new ArgumentException("Payload cap.");
            if (BufferedMessages + 1 > policy.MaximumQueuedMessages || (long)BufferedBytes + packet.WireBytes > policy.MaximumQueuedBytes)
            { QueueOverflows++; BeginRecovery(); return; }
            RequiredEnqueued++;
            if (Phase == HostPhase.Live) { outgoing.Enqueue(packet); queuedBytes += packet.WireBytes; }
            else { held.Enqueue(packet); heldBytes += packet.WireBytes; }
        }
        // Only a cheap phase gate; exact-size reservation remains in TryDeltaOptionalCore.
        internal bool RejectOptionalBeforeCapture(double nowMs)
        {
            AdvanceClock(nowMs);
            if (OptionalPolicy == null) throw new InvalidOperationException("Optional policy required.");
            if (Phase == HostPhase.Live) return false;
            OptionalOffered++; OptionalDroppedNotLive++; return true;
        }
        public OptionalAdmission TryDeltaOptional(MessageKind kind, PresenceKey key, byte[] payload, double nowMs)
            => TryDeltaOptionalCore(kind, key, payload, nowMs, false);
        internal OptionalAdmission TryDeltaOptionalOwned(MessageKind kind, ActorState state, double nowMs)
            => TryDeltaOptionalCore(kind, state.Key, state.Bytes, nowMs, true);
        private OptionalAdmission TryDeltaOptionalCore(MessageKind kind, PresenceKey key, byte[] payload, double nowMs, bool owned)
        {
            AdvanceClock(nowMs);
            if (OptionalPolicy == null) throw new InvalidOperationException("Optional policy required.");
            if (kind != MessageKind.Updated || payload == null || payload.Length > policy.MaximumPayloadBytes)
                throw new ArgumentException("Optional traffic must be a bounded Updated payload.");
            OptionalOffered++;
            if (Phase != HostPhase.Live) { OptionalDroppedNotLive++; return OptionalAdmission.NotLive; }
            long wire = (long)Envelope.HeaderBytes + payload.Length;
            var p = OptionalPolicy;
            if ((long)OptionalQueuedMessages + 1 > p.MaximumQueuedMessages || OptionalQueuedBytes + wire > p.MaximumQueuedBytes)
            { OptionalDroppedBudget++; return OptionalAdmission.Budget; }
            if ((long)BufferedMessages + 1 > policy.MaximumQueuedMessages - p.ReserveMessages ||
                (long)BufferedBytes + wire > policy.MaximumQueuedBytes - p.ReserveQueuedBytes)
            { OptionalDroppedReserve++; return OptionalAdmission.Reserve; }
            // pending entries are still in their queues: never add pendingBytes a second time.
            if (AcceptedBytes + BufferedBytes + controlQueuedBytes + wire > (long)policy.MaximumAcceptedBytes - p.ReserveAcceptedBytes)
            { OptionalDroppedCommitment++; return OptionalAdmission.Commitment; }
            var packet = new Envelope(session, incarnation, 0, kind, payload, key, 0, 0, 0, owned) { IsOptional = true };
            outgoing.Enqueue(packet); queuedBytes = checked(queuedBytes + packet.WireBytes);
            OptionalQueuedMessages++; OptionalQueuedBytes += wire; OptionalAdmitted++;
            OptionalHighMessages = Math.Max(OptionalHighMessages, OptionalQueuedMessages);
            OptionalHighBytes = Math.Max(OptionalHighBytes, OptionalQueuedBytes);
            return OptionalAdmission.Admitted;
        }
        public void IntentResult(byte[] payload, double nowMs)
        {
            AdvanceClock(nowMs); if (Phase == HostPhase.Closed || Phase == HostPhase.Handshake) return;
            if (payload == null || payload.Length > policy.MaximumPayloadBytes) throw new ArgumentException("Control payload cap.");
            if (controls.Count >= policy.MaximumControlMessages) { Close(ErrorCode.InboundRateExceeded); return; }
            var packet = Packet(MessageKind.IntentResult, payload);
            controls.Enqueue(packet); controlQueuedBytes = checked(controlQueuedBytes + packet.WireBytes);
        }
        public bool AcceptIntent(double nowMs)
        { AdvanceClock(nowMs); if (Phase == HostPhase.Live || Phase == HostPhase.Drain) return true; RejectedIntents++; return false; }

        // callback must atomically accept the whole batch; no mutation/reentrancy while called.
        public bool TrySend(int maximumBatchMessages, Func<IReadOnlyList<byte[]>, bool> accept, double nowMs)
        {
            using (EveHotPathMetrics.Measure("host.trysend"))
            {
            AdvanceClock(nowMs); if (maximumBatchMessages < 1 || accept == null) throw new ArgumentException();
            if (Phase == HostPhase.Closed || Phase == HostPhase.Handshake || Phase == HostPhase.WaitCapture && controls.Count == 0) return false;
            if (Phase == HostPhase.Drain && outgoing.Count == 0) MoveHeld();
            if (pending == null)
            {
                using (EveHotPathMetrics.Measure("host.trysend.build"))
                {
                var list = new List<Envelope>(); long bytes = 0;
                // Controls may interleave chunks and may be sent while awaiting Applied.
                IEnumerable<Envelope> candidates = controls.Concat(Phase == HostPhase.AwaitApplied ? Enumerable.Empty<Envelope>() : outgoing);
                foreach (var packet in candidates)
                {
                    if (list.Count == maximumBatchMessages || AcceptedBytes + bytes + packet.WireBytes > policy.MaximumAcceptedBytes) break;
                    list.Add(packet.WithSequence(checked(SentSequence + list.Count + 1))); bytes += packet.WireBytes;
                }
                if (list.Count == 0) return false;
                pendingControlCount = Math.Min(controls.Count, list.Count); pending = list.ToArray();
                }
                using (EveHotPathMetrics.Measure("host.trysend.encode")) pendingBytes = pending.Select(ModelCodec.Encode).ToArray();
            }
            // Rejected retries have byte-for-byte identical sequence/payload. Caller receives copies.
            bool accepted;
            submitting = true;
            try
            {
                IReadOnlyList<byte[]> copies;
                using (EveHotPathMetrics.Measure("host.trysend.public-copy")) copies = Array.AsReadOnly(pendingBytes.Select(x => (byte[])x.Clone()).ToArray());
                using (EveHotPathMetrics.Measure("host.trysend.accept")) accepted = accept(copies);
            }
            finally { submitting = false; }
            if (!accepted) return false;
            for (int i = 0; i < pending.Length; i++)
            {
                var sent = i < pendingControlCount ? controls.Dequeue() : outgoing.Dequeue();
                if (Phase == HostPhase.Live || Phase == HostPhase.Drain) if (sent.Kind != MessageKind.IntentResult) queuedBytes -= sent.WireBytes;
                if (sent.Kind == MessageKind.IntentResult) controlQueuedBytes -= sent.WireBytes;
                if (sent.IsOptional) { OptionalQueuedMessages--; OptionalQueuedBytes -= sent.WireBytes; }
                else if (sent.Kind >= MessageKind.Spawn && sent.Kind != MessageKind.IntentResult) RequiredAccepted++;
                SentSequence = pending[i].Sequence; AcceptedBytes = checked(AcceptedBytes + pendingBytes[i].Length);
                if (sent.Kind == MessageKind.SnapshotEnd)
                { EndSequence = SentSequence; endAcceptedAt = now; Phase = HostPhase.AwaitApplied; }
            }
            pending = null; pendingBytes = null;
            if (Phase == HostPhase.Drain && outgoing.Count == 0) { MoveHeld(); if (outgoing.Count == 0) Phase = HostPhase.Live; }
            return true;
            }
        }
        // Adapter reports bytes no longer retained by its reliable pipeline; not merely EndSend success.
        public void ReleaseAccepted(int bytes)
        { if (submitting) throw new InvalidOperationException("Reentrant transport callback."); if (bytes < 0 || bytes > AcceptedBytes) throw new ArgumentOutOfRangeException(); AcceptedBytes -= bytes; }
        public void Applied(long connectionIncarnation, long snapshotId, long endSequence, double nowMs)
        {
            AdvanceClock(nowMs);
            if (Phase != HostPhase.AwaitApplied || connectionIncarnation != incarnation || snapshotId != SnapshotId || endSequence != EndSequence) return;
            Phase = HostPhase.Drain; MoveHeld(); if (outgoing.Count == 0) Phase = HostPhase.Live;
        }
        private void MoveHeld()
        { while (held.Count > 0) outgoing.Enqueue(held.Dequeue()); queuedBytes += heldBytes; heldBytes = 0; }
        public void Tick(double nowMs)
        {
            AdvanceClock(nowMs); if (Phase == HostPhase.Closed) return;
            if (Phase == HostPhase.Handshake) { if (now - started >= policy.HandshakeMs) Close(ErrorCode.HandshakeTimeout); return; }
            if (Phase == HostPhase.Live) return;
            if (now - cycleStarted >= policy.CycleMs) { Close(ErrorCode.RecoveryExhausted); return; }
            if (now - roundStarted >= policy.RoundMs || Phase == HostPhase.AwaitApplied && now - endAcceptedAt >= policy.AppliedMs) BeginRecovery();
        }
        public void Close(ErrorCode? error = null)
        { if (submitting) throw new InvalidOperationException("Reentrant transport callback."); Phase = HostPhase.Closed; Failure = error; outgoing.Clear(); held.Clear(); controls.Clear(); pending = null; pendingBytes = null; queuedBytes = heldBytes = 0; AcceptedBytes = 0; controlQueuedBytes = 0; OptionalQueuedMessages = 0; OptionalQueuedBytes = 0; }
    }
}
