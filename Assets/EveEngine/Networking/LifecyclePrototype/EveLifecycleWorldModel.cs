using System;
using System.Collections.Generic;
using System.Linq;

namespace EveEngine.Experimental.Lifecycle
{
    // Pure authoritative admission model, shared by connections. Not the runtime directory.
    public sealed class WorldModel
    {
        private readonly ModelPolicy policy;
        private readonly Dictionary<long, ActorState> actors = new Dictionary<long, ActorState>();
        private readonly Dictionary<long, long> generations = new Dictionary<long, long>();
        private readonly HashSet<PresenceKey> reserved = new HashSet<PresenceKey>();
        private readonly Dictionary<long, string> retentionOwners = new Dictionary<long, string>();
        public long LastAllocatedId { get; private set; }
        public int IdentityCount => actors.Count + reserved.Count(x => !actors.ContainsKey(x.EntityId));
        public int DormantCount => retentionOwners.Count;
        public WorldModel(ModelPolicy policy) { this.policy = policy ?? throw new ArgumentNullException(nameof(policy)); }
        public PresenceKey ReserveNew()
        {
            if (IdentityCount >= policy.MaximumActors) throw new InvalidOperationException("World identity capacity.");
            var key = new PresenceKey(checked(LastAllocatedId + 1), 1); LastAllocatedId = key.EntityId;
            reserved.Add(key); generations[key.EntityId] = 1; return key;
        }
        public PresenceKey ReserveGeneration(long entityId)
        {
            if (!actors.ContainsKey(entityId)) throw new InvalidOperationException("No retained identity.");
            var key = new PresenceKey(entityId, checked(generations[entityId] + 1)); generations[entityId] = key.Generation;
            reserved.RemoveWhere(x => x.EntityId == entityId); reserved.Add(key); return key;
        }
        public void Cancel(PresenceKey key)
        { if (reserved.Remove(key) && !actors.ContainsKey(key.EntityId)) generations.Remove(key.EntityId); }
        public bool Publish(PresenceKey key, byte[] payload)
        {
            if (!reserved.Contains(key)) throw new InvalidOperationException("Presence not reserved.");
            var state = new ActorState(key, false, payload);
            if (!Representable(state)) return false; // caller cancels/retries after resolving state bound
            actors[key.EntityId] = state; reserved.Remove(key); retentionOwners.Remove(key.EntityId); return true;
        }
        public bool Update(PresenceKey key, byte[] payload)
        {
            if (!actors.TryGetValue(key.EntityId, out var current) || !current.Key.Equals(key)) throw new InvalidOperationException("Stale presence.");
            var state = new ActorState(key, current.IsDormant, payload);
            if (!Representable(state)) return false;
            actors[key.EntityId] = state; return true;
        }
        private bool Representable(ActorState state)
        {
            try { ModelCodec.EncodeActors(actors.Values.Where(x => x.Key.EntityId != state.Key.EntityId).Concat(new[] { state }), policy); return true; }
            catch (FormatException) { return false; }
        }
        public void RetainDormant(PresenceKey key, string owner)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Game mode retention owner required.");
            if (!actors.TryGetValue(key.EntityId, out var actor) || !actor.Key.Equals(key)) throw new InvalidOperationException("Stale presence.");
            actors[key.EntityId] = new ActorState(key, true, actor.Bytes); retentionOwners[key.EntityId] = owner;
        }
        public bool EndRetention(PresenceKey key, string owner)
        {
            if (!retentionOwners.TryGetValue(key.EntityId, out var expected) || expected != owner) return false;
            return Despawn(key);
        }
        public bool Despawn(PresenceKey key)
        {
            if (!actors.TryGetValue(key.EntityId, out var actor) || !actor.Key.Equals(key)) return false;
            actors.Remove(key.EntityId); generations.Remove(key.EntityId); retentionOwners.Remove(key.EntityId); reserved.RemoveWhere(x => x.EntityId == key.EntityId); return true;
        }
        public IReadOnlyList<ActorState> Snapshot() => Array.AsReadOnly(actors.Values.OrderBy(x => x.Key.EntityId).ToArray());
        public void EndSession() { actors.Clear(); generations.Clear(); retentionOwners.Clear(); reserved.Clear(); }
    }
}
