using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EveEngine.Experimental.Lifecycle
{
    public readonly struct AppliedAck
    {
        public long Incarnation { get; }
        public long SnapshotId { get; }
        public long EndSequence { get; }
        public AppliedAck(long incarnation, long snapshotId, long endSequence)
        { Incarnation = incarnation; SnapshotId = snapshotId; EndSequence = endSequence; }
    }

    // Pure state only. Prepared keys model leases; no Unity objects or Buff behavior are created.
    public sealed class ClientModel
    {
        private readonly ModelPolicy policy;
        private readonly Guid session;
        private readonly long incarnation;
        private Dictionary<long, ActorState> current = new Dictionary<long, ActorState>();
        private readonly Dictionary<long, long> observed = new Dictionary<long, long>();
        private readonly DiagnosticHistory history;
        private readonly Dictionary<PresenceKey, PreparationToken> loads = new Dictionary<PresenceKey, PreparationToken>();
        private readonly HashSet<PresenceKey> prepared = new HashSet<PresenceKey>();
        private readonly HashSet<PresenceKey> ready = new HashSet<PresenceKey>();
        private readonly Queue<byte[]> results = new Queue<byte[]>();
        private readonly Dictionary<PresenceKey, Queue<Envelope>> waiting = new Dictionary<PresenceKey, Queue<Envelope>>();
        private readonly Dictionary<PresenceKey, double> loadStarted = new Dictionary<PresenceKey, double>();
        private ActorState[] candidate;
        private MemoryStream assembly;
        private byte[] expectedHash;
        private int expectedBytes, expectedChunks, nextChunk, bufferedBytes, bufferedCount;
        private long snapshotId, endSequence, nextLoadId;
        private double now, phaseStarted, recoveryStarted;
        private int recoveryRounds;
        private AppliedAck? ack;
        public ClientPhase Phase { get; private set; } = ClientPhase.AwaitSnapshot;
        public ErrorCode? Failure { get; private set; }
        public ErrorCode? RecoveryReason { get; private set; }
        public long Sequence { get; private set; }
        public int DuplicateMessages { get; private set; }
        public int StaleConnectionMessages { get; private set; }
        public int ControlResults { get; private set; }
        public int GameplayApplied { get; private set; }
        public int PreparationCancelled { get; private set; }
        public int SnapshotRequests { get; private set; }
        public bool SnapshotRequested { get; private set; }
        public bool CanSendIntent => Phase == ClientPhase.Live && !ack.HasValue;
        public int PreparedCount => prepared.Count;
        public int BufferedMessages => bufferedCount;
        public int CurrentCount => current.Count;
        public int GenerationFloorCount => observed.Count;
        public int HistoryCount => history.Count;
        public long HistoryEvictions => history.Evictions;
        public int PendingLoadCount => loads.Count;
        public ClientModel(Guid session, long incarnation, ModelPolicy policy, double nowMs)
        {
            if (session == Guid.Empty || incarnation <= 0) throw new ArgumentException();
            this.session = session; this.incarnation = incarnation; this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
            history = new DiagnosticHistory(policy.DiagnosticHistoryCapacity);
            Clock(nowMs); phaseStarted = now;
        }
        private void Clock(double time)
        { if (double.IsNaN(time) || double.IsInfinity(time) || time < now) throw new ArgumentOutOfRangeException(nameof(time)); now = time; }
        public IReadOnlyList<ActorState> Snapshot() => Array.AsReadOnly(current.Values.OrderBy(x => x.Key.EntityId).ToArray());
        public IReadOnlyList<ActorState> CandidateSnapshot() => Array.AsReadOnly(candidate?.ToArray() ?? Array.Empty<ActorState>());
        public IReadOnlyList<PresenceKey> NeedsPreparation()
        {
            var keys = Phase == ClientPhase.Preparing ? candidate.Where(x => !x.IsDormant).Select(x => x.Key) : waiting.Keys;
            return Array.AsReadOnly(keys.Where(x => !prepared.Contains(x) && !IsReady(x)).ToArray());
        }
        public bool IsReady(PresenceKey key) => ready.Contains(key);
        public bool HasPreparedPresence(PresenceKey key) => prepared.Contains(key);
        public bool IsCurrentPreparation(PreparationToken token) => loads.TryGetValue(token.Key, out var currentLoad) && currentLoad.Equals(token);
        public PreparationToken BeginPreparation(PresenceKey key)
        {
            if ((Phase != ClientPhase.Live && Phase != ClientPhase.Preparing) || !NeedsPreparation().Contains(key))
                throw new InvalidOperationException("Presence does not need preparation.");
            if (loads.TryGetValue(key, out var existing)) return existing;
            var token = new PreparationToken(incarnation, checked(nextLoadId + 1), key);
            nextLoadId = token.LoadId; loads.Add(key, token); return token;
        }
        public byte[] TakeIntentResult() => results.Count == 0 ? null : results.Dequeue();
        public void Receive(byte[] wire, double nowMs)
        {
            Clock(nowMs); if (Phase == ClientPhase.Closed) return;
            Envelope p;
            try { p = ModelCodec.Decode(wire, policy); }
            catch (FormatException) { Recover(ErrorCode.SnapshotInvalid); return; }
            if (p.Session != session || p.Incarnation != incarnation) { StaleConnectionMessages++; return; }
            if (p.Sequence <= Sequence) { DuplicateMessages++; return; }
            if (p.Sequence != Sequence + 1)
            {
                // Consume a missing position only in recovery; reliable stream cannot repair this gap.
                Sequence = p.Sequence; Recover(ErrorCode.SequenceGap);
                if (Phase == ClientPhase.Closed || p.Kind != MessageKind.SnapshotStart && p.Kind != MessageKind.IntentResult) return;
            }
            else Sequence = p.Sequence;
            if (p.Kind == MessageKind.IntentResult)
            {
                if (results.Count >= policy.MaximumControlMessages) { Close(ErrorCode.QueueLimitExceeded); return; }
                results.Enqueue(p.Payload); ControlResults++; return;
            }
            if (p.Kind == MessageKind.SnapshotStart) { Start(p); return; }
            if (Phase == ClientPhase.Recovering) return; // consume and discard gameplay until Start
            if (Phase == ClientPhase.Assembling)
            {
                if (p.Kind == MessageKind.SnapshotChunk) Chunk(p);
                else if (p.Kind == MessageKind.SnapshotEnd) End(p);
                else Recover(ErrorCode.SnapshotInvalid);
                return;
            }
            if (Phase != ClientPhase.Live) { Recover(ErrorCode.InvalidEntityLifecycle); return; }
            Apply(p);
        }
        private void Start(Envelope p)
        {
            if (p.SnapshotId <= 0 || p.SnapshotId <= snapshotId) { Recover(ErrorCode.SnapshotInvalid); return; }
            if (p.Index < 4 || p.Index > policy.MaximumSnapshotBytes || p.Count < 1 || p.Count > policy.MaximumChunks ||
                (long)p.Count * policy.ChunkBytes < p.Index || p.Bytes.Length != 32)
            { Recover(ErrorCode.SnapshotInvalid); return; }
            DisposeAssembly(); candidate = null; ack = null;
            snapshotId = p.SnapshotId; expectedBytes = p.Index; expectedChunks = p.Count; nextChunk = 0;
            expectedHash = p.Payload; assembly = new MemoryStream();
            Phase = ClientPhase.Assembling; phaseStarted = now; SnapshotRequested = false;
        }
        private void Chunk(Envelope p)
        {
            if (p.SnapshotId != snapshotId || p.Index != nextChunk || p.Count != expectedChunks || p.Bytes.Length == 0 ||
                p.Bytes.Length > policy.ChunkBytes || assembly.Length + p.Bytes.Length > expectedBytes)
            { Recover(ErrorCode.SnapshotInvalid); return; }
            assembly.Write(p.Bytes, 0, p.Bytes.Length); nextChunk++;
        }
        private void End(Envelope p)
        {
            if (p.SnapshotId != snapshotId || p.Count != expectedChunks || p.Bytes.Length != 0 || nextChunk != expectedChunks || assembly.Length != expectedBytes)
            { Recover(ErrorCode.SnapshotInvalid); return; }
            byte[] body = assembly.ToArray(); DisposeAssembly();
            if (!ModelCodec.Hash(body).SequenceEqual(expectedHash)) { Recover(ErrorCode.SnapshotInvalid); return; }
            try
            {
                var next = ModelCodec.DecodeActors(body, policy).ToArray();
                foreach (var actor in next)
                    if (history.Contains(actor.Key.EntityId) || observed.TryGetValue(actor.Key.EntityId, out long gen) && actor.Key.Generation < gen)
                        throw new FormatException("Generation/terminal identity.");
                foreach (var actor in next) observed[actor.Key.EntityId] = actor.Key.Generation;
                // S2 reuses keys prepared for S1. Evict unmatched candidate leases before new preparation.
                var keep = new HashSet<PresenceKey>(next.Where(x => !x.IsDormant).Select(x => x.Key));
                foreach (var key in prepared.Where(x => !keep.Contains(x)).ToArray()) { prepared.Remove(key); PreparationCancelled++; }
                foreach (var key in loads.Keys.Where(x => !keep.Contains(x)).ToArray()) { loads.Remove(key); PreparationCancelled++; }
                candidate = next; ClearWaiting(keep); endSequence = p.Sequence;
                PruneFloors(new HashSet<long>(current.Keys.Concat(next.Select(x => x.Key.EntityId))));
                Phase = ClientPhase.Preparing; phaseStarted = now;
            }
            catch (FormatException) { Recover(ErrorCode.SnapshotInvalid); }
        }
        public bool CompletePreparation(PreparationToken token, bool succeeded, double nowMs)
        {
            Clock(nowMs);
            var key = token.Key;
            if (Phase == ClientPhase.Closed || token.Incarnation != incarnation || !loads.TryGetValue(key, out var currentLoad) || !currentLoad.Equals(token)) return false;
            loads.Remove(key); // one completion only, independent of history eviction
            if (!succeeded) { Close(ErrorCode.EntityPrepareFailed); return false; }
            prepared.Add(key);
            if (Phase == ClientPhase.Live)
            {
                var queue = waiting[key]; waiting.Remove(key); loadStarted.Remove(key); prepared.Remove(key);
                ready.Add(key);
                while (queue.Count > 0)
                {
                    var p = queue.Dequeue(); bufferedCount--; bufferedBytes -= p.WireBytes; Apply(p);
                    if (Phase != ClientPhase.Live) break;
                }
            }
            return true;
        }
        public bool Commit(bool hooksSucceeded, bool hardOrRepeatedOverrun, double nowMs)
        {
            Clock(nowMs); if (Phase != ClientPhase.Preparing) throw new InvalidOperationException();
            if (NeedsPreparation().Count > 0) return false;
            if (!hooksSucceeded || hardOrRepeatedOverrun) { Close(ErrorCode.EntityCommitFailed); return false; }
            var next = candidate.ToDictionary(x => x.Key.EntityId); // one publication, no halfway observable map
            observed.Clear(); foreach (var actor in candidate) observed[actor.Key.EntityId] = actor.Key.Generation;
            history.Reset(); loads.Clear();
            current = next; candidate = null; prepared.Clear();
            ready.Clear(); foreach (var actor in current.Values.Where(x => !x.IsDormant)) ready.Add(actor.Key);
            ack = new AppliedAck(incarnation, snapshotId, endSequence); Phase = ClientPhase.Live;
            SnapshotRequested = false; recoveryRounds = 0; RecoveryReason = null;
            return true;
        }
        // Ack must be queued before enabling new intents. No direct gameplay dispatch from this method.
        public AppliedAck? TakeAppliedAck() { var result = ack; ack = null; return result; }
        private void Apply(Envelope p)
        {
            if (p.Kind == MessageKind.World) { GameplayApplied++; return; }
            var key = p.Key;
            if (key.EntityId <= 0 || key.Generation <= 0) { Recover(ErrorCode.InvalidEntityLifecycle); return; }
            if (history.Contains(key.EntityId)) { Recover(ErrorCode.InvalidEntityLifecycle); return; }
            if (observed.TryGetValue(key.EntityId, out long highest) && key.Generation < highest)
            { Recover(ErrorCode.InvalidEntityLifecycle); return; }
            if (p.Kind == MessageKind.Spawn)
            {
                if (observed.TryGetValue(key.EntityId, out highest) && key.Generation <= highest || current.Count >= policy.MaximumActors && !current.ContainsKey(key.EntityId))
                { Recover(ErrorCode.InvalidEntityLifecycle); return; }
                foreach (var old in waiting.Keys.Where(x => x.EntityId == key.EntityId).ToArray()) CancelLoad(old);
                ready.RemoveWhere(x => x.EntityId == key.EntityId);
                observed[key.EntityId] = key.Generation; current[key.EntityId] = new ActorState(key, false, p.Bytes);
                waiting[key] = new Queue<Envelope>(); loadStarted[key] = now; GameplayApplied++; return;
            }
            if (!current.TryGetValue(key.EntityId, out var actor) || !actor.Key.Equals(key)) { Recover(ErrorCode.InvalidEntityLifecycle); return; }
            if (p.Kind == MessageKind.Despawn)
            {
                CancelLoad(key); ready.Remove(key); current.Remove(key.EntityId); observed.Remove(key.EntityId); history.Remember(key.EntityId); GameplayApplied++; return;
            }
            if (p.Kind == MessageKind.Dormant)
            {
                CancelLoad(key); ready.Remove(key); current[key.EntityId] = new ActorState(key, true, p.Bytes); GameplayApplied++; return;
            }
            if (p.Kind != MessageKind.Updated || actor.IsDormant) { Recover(ErrorCode.InvalidEntityLifecycle); return; }
            if (waiting.TryGetValue(key, out var queue))
            {
                if (bufferedCount >= policy.MaximumQueuedMessages || (long)bufferedBytes + p.WireBytes > policy.MaximumQueuedBytes)
                { Recover(ErrorCode.QueueLimitExceeded); return; }
                queue.Enqueue(p); bufferedCount++; bufferedBytes += p.WireBytes; return;
            }
            current[key.EntityId] = new ActorState(key, false, p.Bytes); GameplayApplied++;
        }
        private void CancelLoad(PresenceKey key)
        {
            if (waiting.TryGetValue(key, out var queue))
            { bufferedCount -= queue.Count; bufferedBytes -= queue.Sum(x => x.WireBytes); waiting.Remove(key); loadStarted.Remove(key); PreparationCancelled++; }
            prepared.Remove(key);
            loads.Remove(key);
        }
        private void ClearWaiting(HashSet<PresenceKey> keep = null)
        {
            foreach (var key in waiting.Keys.ToArray())
            {
                if (keep != null && keep.Contains(key))
                { var queue = waiting[key]; bufferedCount -= queue.Count; bufferedBytes -= queue.Sum(x => x.WireBytes); waiting.Remove(key); loadStarted.Remove(key); }
                else CancelLoad(key);
            }
        }
        private void PruneFloors(HashSet<long> keep)
        { foreach (long id in observed.Keys.Where(x => !keep.Contains(x)).ToArray()) { observed.Remove(id); history.Remember(id); } }
        private void Recover(ErrorCode reason)
        {
            if (Phase == ClientPhase.Closed) return;
            if (Phase == ClientPhase.Recovering) return;
            if (recoveryRounds == 0) recoveryStarted = now;
            if (++recoveryRounds > policy.MaximumRounds || now - recoveryStarted >= policy.CycleMs) { Close(ErrorCode.RecoveryExhausted); return; }
            DisposeAssembly(); candidate = null; ClearWaiting(); ack = null;
            loads.Clear(); // canceled attempts cannot complete into a later recovery candidate
            Phase = ClientPhase.Recovering; phaseStarted = now; RecoveryReason = reason;
            SnapshotRequested = true; SnapshotRequests++;
        }
        public void Tick(double nowMs)
        {
            Clock(nowMs); if (Phase == ClientPhase.Closed) return;
            if (recoveryRounds > 0 && now - recoveryStarted >= policy.CycleMs) { Close(ErrorCode.RecoveryExhausted); return; }
            if (Phase == ClientPhase.Assembling && now - phaseStarted >= policy.AssemblyMs) Recover(ErrorCode.SnapshotTimeout);
            else if (Phase == ClientPhase.Preparing && now - phaseStarted >= policy.PreparationMs) Recover(ErrorCode.EntityPreparationTimeout);
            else if (Phase == ClientPhase.AwaitSnapshot && now - phaseStarted >= policy.HandshakeMs ||
                Phase == ClientPhase.Recovering && now - phaseStarted >= policy.RoundMs) Close(ErrorCode.RecoveryExhausted);
            else if (Phase == ClientPhase.Live && loadStarted.Values.Any(x => now - x >= policy.PreparationMs)) Recover(ErrorCode.EntityPreparationTimeout);
        }
        private void DisposeAssembly() { assembly?.Dispose(); assembly = null; }
        public void Close(ErrorCode? error = null)
        {
            Phase = ClientPhase.Closed; Failure = error; DisposeAssembly(); candidate = null; ClearWaiting();
            PreparationCancelled += prepared.Count; prepared.Clear(); ready.Clear(); results.Clear(); current.Clear(); observed.Clear(); history.Reset(); loads.Clear(); ack = null; SnapshotRequested = false;
        }
    }
}
