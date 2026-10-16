using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using EveEngine.Experimental.Lifecycle;

namespace EveEngine
{

    // Authenticated ordered bytes in, prepared Unity actors/presentation out. Transport remains separate.
    public sealed class EveActorClientConnection : IDisposable
    {
        private sealed class Binding
        {
            internal ActorState State;
            internal EveEntityLoader Loader;
            internal PreparationToken Token;
            internal readonly List<EveIReplicaStage> Stages = new List<EveIReplicaStage>();
            internal readonly Queue<EveActorData> Pending = new Queue<EveActorData>();
        }
        private readonly EveSessionContext session;
        private readonly Guid hostSession;
        private readonly long incarnation;
        private readonly ModelPolicy policy;
        private readonly Func<double> clock;
        private readonly Func<EveEntitySpawnMessage, EveEntityLoader> createInactive;
        private readonly Action<EveEntityLoader> returnObject;
        private readonly Dictionary<PresenceKey, Binding> bindings = new Dictionary<PresenceKey, Binding>();
        private readonly Dictionary<PresenceKey, ActorState> spawnStates = new Dictionary<PresenceKey, ActorState>();
        private readonly Dictionary<PresenceKey, Queue<EveActorData>> earlyUpdates = new Dictionary<PresenceKey, Queue<EveActorData>>();
        private bool closed;
        private long candidateEndSequence = -1;
        public ClientModel Protocol { get; }
        public Exception LastError { get; private set; }
        public EveActorClientConnection(EveSessionContext session, Guid hostSession, long incarnation, ModelPolicy policy, Func<double> monotonicMs,
            Func<EveEntitySpawnMessage, EveEntityLoader> createInactive, Action<EveEntityLoader> returnObject, EveContentCompatibility hostContent)
        {
            if (session == null || !session.IsActive || session.Role != EveSessionRole.Client || session.Cleanup == null) throw new InvalidOperationException("Client with explicit cleanup policy required.");
            session.Compatibility.Validate(hostContent);
            this.session = session; this.hostSession = hostSession; this.incarnation = incarnation; this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
            clock = monotonicMs ?? throw new ArgumentNullException(nameof(monotonicMs)); this.createInactive = createInactive ?? throw new ArgumentNullException(nameof(createInactive)); this.returnObject = returnObject ?? throw new ArgumentNullException(nameof(returnObject));
            Protocol = new ClientModel(hostSession, incarnation, policy, clock());
        }
        public void Receive(byte[] wire)
        {
            if (closed || !session.IsActive) return;
            try
            {
                var packet = ModelCodec.Decode(wire, policy); long before = Protocol.Sequence;
                Protocol.Receive(wire, clock());
                if (Protocol.Phase == ClientPhase.Closed) { Fail(Protocol.Failure ?? ErrorCode.InvalidEntityLifecycle); return; }
                if (packet.Session != hostSession || packet.Incarnation != incarnation || packet.Sequence <= before || Protocol.Phase != ClientPhase.Live || packet.Kind < MessageKind.Spawn || packet.Kind == MessageKind.IntentResult) return;
                if (packet.Kind == MessageKind.Spawn)
                {
                    RetireExcept(packet.Key.EntityId, packet.Key);
                    spawnStates[packet.Key] = new ActorState(packet.Key, false, packet.Payload);
                    // Creation/load is scheduled in AdvanceFrame, never blocks Receive.
                }
                else if (packet.Kind == MessageKind.Despawn || packet.Kind == MessageKind.Dormant) { RetireExcept(packet.Key.EntityId, null); spawnStates.Remove(packet.Key); earlyUpdates.Remove(packet.Key); }
                else if (packet.Kind == MessageKind.Updated)
                {
                    var data = EveActorData.Decode(packet.Payload, policy.MaximumPayloadBytes);
                    if (bindings.TryGetValue(packet.Key, out var binding)) { if (binding.Loader.IsReady) ApplyDelta(binding, data); else binding.Pending.Enqueue(data); }
                    else { if (!earlyUpdates.TryGetValue(packet.Key, out var queue)) earlyUpdates.Add(packet.Key, queue = new Queue<EveActorData>()); queue.Enqueue(data); }
                }
            }
            catch (MissingMemberException error) { LastError = error; Fail(ErrorCode.ContentMismatch); }
            catch (Exception error) { LastError = error; Fail(ErrorCode.SnapshotInvalid); }
        }
        private void RetireExcept(long entityId, PresenceKey? keep)
        {
            foreach (var key in spawnStates.Keys.Where(x => x.EntityId == entityId && (!keep.HasValue || !x.Equals(keep.Value))).ToArray()) { spawnStates.Remove(key); earlyUpdates.Remove(key); }
            foreach (var key in bindings.Keys.Where(x => x.EntityId == entityId && (!keep.HasValue || !x.Equals(keep.Value))).ToArray()) Retire(key);
        }
        private void Retire(PresenceKey key)
        {
            var binding = bindings[key]; foreach (var stage in binding.Stages) stage.Dispose(); binding.Stages.Clear();
            session.Cleanup.Retire(binding.Loader); bindings.Remove(key);
        }
        private static Dictionary<EveId, EveIEntityReplicationPart> Parts(EveEntityLoader loader)
            => loader.GetComponents<MonoBehaviour>().OfType<EveIEntityReplicationPart>().ToDictionary(x => x.ReplicationId);
        private void Stage(Binding binding)
        {
            foreach (var stage in binding.Stages) stage.Dispose(); binding.Stages.Clear();
            var data = EveActorData.Decode(binding.State.Data, policy.MaximumPayloadBytes); var parts = Parts(binding.Loader);
            if (!parts.Keys.OrderBy(x => x).SequenceEqual(data.Parts.Keys.OrderBy(x => x))) throw new MissingMemberException("Actor subsystem set differs from content.");
            foreach (var part in data.Parts) binding.Stages.Add(parts[part.Key].StageReplication(part.Value, hostSession, binding.State.Key.Generation));
        }
        private void ApplyDelta(Binding binding, EveActorData data)
        {
            if (data.Delta == null) throw new FormatException("Actor update lacks subsystem delta.");
            var identity = binding.Loader.GetComponent<EveEntityIdentity>();
            if (data.Character != identity.CharacterId.ToString() || data.Owner != identity.OwnerId) throw new FormatException("Actor descriptor changed without a new generation.");
            var parts = Parts(binding.Loader);
            if (!parts.TryGetValue(data.DeltaPart, out var part)) throw new MissingMemberException("Unknown subsystem.");
            part.ApplyReplicationDelta(data.Delta, hostSession, binding.State.Key.Generation);
        }
        private Binding Create(ActorState state)
        {
            var data = EveActorData.Decode(state.Data, policy.MaximumPayloadBytes);
            session.Registry.Characters.Get(EveId.Parse(data.Character));
            var token = Protocol.BeginPreparation(state.Key);
            var loader = createInactive(new EveEntitySpawnMessage(hostSession, state.Key.EntityId, data.Character, data.Owner, state.Key.Generation));
            if (loader == null || loader.gameObject.activeInHierarchy || loader.State != EveEntityLoadState.Idle) throw new InvalidOperationException("Factory must return fresh inactive loader.");
            try
            {
                var identity = loader.GetComponent<EveEntityIdentity>(); if (identity == null) throw new InvalidOperationException("Identity required.");
                identity.Assign(new EveEntityId(state.Key.EntityId), EveId.Parse(data.Character), data.Owner, state.Key.Generation);
                data.ApplyPose(loader);
                loader.BeginPrepare(session.Registry);
                if (!session.Cleanup.Track(loader, () => returnObject(loader))) throw new InvalidOperationException("Cleanup backpressure changed while creating actor.");
                var binding = new Binding { State = state, Loader = loader, Token = token };
                bindings.Add(state.Key, binding); return binding;
            }
            catch { loader.Unload(); returnObject(loader); throw; }
        }
        public void AdvanceFrame(EveEntityPreparationBudget budget)
        {
            if (budget == null) throw new ArgumentNullException(nameof(budget));
            session.Cleanup.AdvanceFrame();
            if (closed || !session.IsActive) return;
            Protocol.Tick(clock()); if (Protocol.Phase == ClientPhase.Closed) { Fail(Protocol.Failure ?? ErrorCode.RecoveryExhausted); return; }
            try
            {
                foreach (var key in bindings.Keys.Where(x => !bindings[x].Loader.IsReady && !Protocol.HasPreparedPresence(x) && !Protocol.IsCurrentPreparation(bindings[x].Token)).ToArray()) Retire(key);
                bool snapshot = Protocol.Phase == ClientPhase.Preparing;
                if (!snapshot && Protocol.Phase != ClientPhase.Live) return;
                var desired = (snapshot ? Protocol.CandidateSnapshot() : Protocol.Snapshot()).Where(x => !x.IsDormant).ToArray();
                var keep = new HashSet<PresenceKey>(desired.Select(x => x.Key));
                // Keep old published view until global commit; evict only superseded uncommitted preparations now.
                foreach (var key in bindings.Keys.Where(x => !keep.Contains(x) && !bindings[x].Loader.IsReady).ToArray()) Retire(key);
                if (snapshot && candidateEndSequence != Protocol.Sequence)
                {
                    spawnStates.Clear(); earlyUpdates.Clear();
                    foreach (var state in desired) if (bindings.TryGetValue(state.Key, out var retained)) { retained.State = state; retained.Pending.Clear(); if (retained.Loader.IsReady || retained.Loader.State == EveEntityLoadState.Prepared) Stage(retained); }
                    candidateEndSequence = Protocol.Sequence;
                }
                int created = 0, steps = 0; double started = clock();
                foreach (var state in desired)
                {
                    if (!bindings.TryGetValue(state.Key, out var binding))
                    {
                        if (created >= budget.EntitiesPerFrame || !session.Cleanup.CanBeginPreparation) continue;
                        binding = Create(!snapshot && spawnStates.TryGetValue(state.Key, out var initial) ? initial : state); created++;
                        if (!snapshot && earlyUpdates.TryGetValue(state.Key, out var pending)) { while (pending.Count > 0) binding.Pending.Enqueue(pending.Dequeue()); earlyUpdates.Remove(state.Key); spawnStates.Remove(state.Key); }
                    }
                    while (binding.Loader.State == EveEntityLoadState.Preparing && steps < budget.StepsPerFrame && (steps == 0 || clock() - started < budget.MillisecondsPerFrame)) { binding.Loader.PrepareNext(); steps++; }
                    if (binding.Loader.State == EveEntityLoadState.Prepared && binding.Stages.Count == 0)
                    {
                        Stage(binding);
                        if (!Protocol.HasPreparedPresence(state.Key) && !Protocol.CompletePreparation(binding.Token, true, clock())) { Retire(state.Key); continue; }
                        if (!snapshot)
                        {
                            EveEntityLoader.CommitBatch(new[] { binding.Loader }, () => { foreach (var stage in binding.Stages) stage.Commit(); });
                            binding.Loader.gameObject.SetActive(true); PublishStages(binding);
                            while (binding.Pending.Count > 0) ApplyDelta(binding, binding.Pending.Dequeue());
                        }
                    }
                }
                if (snapshot && Protocol.NeedsPreparation().Count == 0 && desired.All(x => bindings.ContainsKey(x.Key)))
                {
                    var selected = desired.Select(x => bindings[x.Key]).ToArray();
                    EveEntityLoader.CommitBatch(selected.Where(x => x.Loader.State == EveEntityLoadState.Prepared).Select(x => x.Loader), () =>
                    { foreach (var binding in selected) { EveActorData.Decode(binding.State.Data, policy.MaximumPayloadBytes).ApplyPose(binding.Loader); foreach (var stage in binding.Stages) stage.Commit(); } if (!Protocol.Commit(true, false, clock())) throw new InvalidOperationException("Model snapshot commit refused."); foreach (var key in bindings.Keys.Where(x => !keep.Contains(x)).ToArray()) Retire(key); }, session);
                    foreach (var binding in selected) { binding.Loader.gameObject.SetActive(true); PublishStages(binding); }
                }
            }
            catch (MissingMemberException error) { LastError = error; Fail(ErrorCode.ContentMismatch); }
            catch (Exception error) { LastError = error; Fail(ErrorCode.EntityPrepareFailed); }
        }
        public AppliedAck? TakeAppliedAck() => Protocol.TakeAppliedAck();
        private static void PublishStages(Binding binding)
        {
            foreach (var stage in binding.Stages) try { stage.Published(); } catch (Exception error) { Debug.LogException(error, binding.Loader); }
            binding.Stages.Clear();
        }
        private void Fail(ErrorCode code)
        { if (closed) return; Protocol.Close(code); session.FailConnection(((ushort)code).ToString(), code.ToString()); Dispose(); }
        public void Dispose()
        {
            if (closed) return; closed = true; Protocol.Close();
            session.NotifyStopping(); // one client session belongs to one host connection
            foreach (var key in bindings.Keys.ToArray()) Retire(key);
            spawnStates.Clear(); earlyUpdates.Clear();
        }
    }
}
