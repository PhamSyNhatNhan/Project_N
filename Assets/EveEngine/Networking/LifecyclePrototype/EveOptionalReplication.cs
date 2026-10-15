using System;

namespace EveEngine.Experimental.Lifecycle
{
    public enum OptionalAdmission { Admitted, NotLive, Budget, Reserve, Commitment }

    // Explicit per-connection policy. Null on the legacy constructor preserves the old path.
    public sealed class OptionalReplicationPolicy
    {
        public int MaximumQueuedMessages { get; }
        public int MaximumQueuedBytes { get; }
        public int ReserveMessages { get; }
        public int ReserveQueuedBytes { get; }
        public int ReserveAcceptedBytes { get; }
        public OptionalReplicationPolicy(int maximumQueuedMessages, int maximumQueuedBytes,
            int reserveMessages, int reserveQueuedBytes, int reserveAcceptedBytes)
        {
            if (maximumQueuedMessages < 0 || maximumQueuedBytes < 0 || reserveMessages < 0 ||
                reserveQueuedBytes < 0 || reserveAcceptedBytes < 0) throw new ArgumentOutOfRangeException();
            MaximumQueuedMessages=maximumQueuedMessages; MaximumQueuedBytes=maximumQueuedBytes;
            ReserveMessages=reserveMessages; ReserveQueuedBytes=reserveQueuedBytes; ReserveAcceptedBytes=reserveAcceptedBytes;
        }
        internal void Validate(ModelPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if ((long)MaximumQueuedMessages + ReserveMessages > policy.MaximumQueuedMessages ||
                (long)MaximumQueuedBytes + ReserveQueuedBytes > policy.MaximumQueuedBytes || ReserveAcceptedBytes > policy.MaximumAcceptedBytes)
                throw new ArgumentException("Optional policy exceeds connection capacities.");
        }
    }
}
