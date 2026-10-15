using System;
using System.Collections.Generic;

namespace EveEngine.Experimental.Lifecycle
{
    // Recent closed identities only. Never evict generation floors of current/candidate actors.
    internal sealed class DiagnosticHistory
    {
        private readonly int capacity;
        private readonly Queue<long> order = new Queue<long>();
        private readonly HashSet<long> ids = new HashSet<long>();
        public long Evictions { get; private set; }
        public int Count => ids.Count;
        public DiagnosticHistory(int capacity) { if (capacity < 1) throw new ArgumentOutOfRangeException(); this.capacity = capacity; }
        public bool Contains(long id) => ids.Contains(id);
        public void Remember(long id)
        {
            if (ids.Contains(id)) return;
            if (ids.Count == capacity) { ids.Remove(order.Dequeue()); if (Evictions < long.MaxValue) Evictions++; }
            ids.Add(id); order.Enqueue(id);
        }
        // Reset contents at authoritative snapshot commit; cumulative diagnostics remain available.
        public void Reset() { ids.Clear(); order.Clear(); }
    }
}
