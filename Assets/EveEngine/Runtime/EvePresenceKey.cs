using System;
namespace EveEngine.Experimental.Lifecycle
{
    public readonly struct PresenceKey : IEquatable<PresenceKey>
    {
        public long EntityId { get; }
        public long Generation { get; }
        public PresenceKey(long entityId, long generation)
        {
            if (entityId <= 0 || generation <= 0) throw new ArgumentOutOfRangeException();
            EntityId = entityId; Generation = generation;
        }
        public bool Equals(PresenceKey other) => EntityId == other.EntityId && Generation == other.Generation;
        public override bool Equals(object obj) => obj is PresenceKey other && Equals(other);
        public override int GetHashCode() => EntityId.GetHashCode() * 397 ^ Generation.GetHashCode();
        public override string ToString() => EntityId + ":" + Generation;
    }
}
