using System;
using System.Collections.Generic;
using System.Linq;

namespace EveEngine
{
    // EN: Tracks live entities, rejects duplicate IDs and allocates host IDs within one session.
    // VI: Theo dõi entity sống, từ chối ID trùng và cấp ID host trong một phiên.
    public class EveEntityDirectory
    {
        private readonly EveSessionContext session;
        private readonly Dictionary<(EveEntityId, long), EveEntityLoader> entities = new Dictionary<(EveEntityId, long), EveEntityLoader>();
        private readonly Dictionary<EveEntityId, long> current = new Dictionary<EveEntityId, long>();
        private long lastHostId;
        public virtual int Count => entities.Count;
        // Includes preparing/retiring presences whose cleanup still owns the ID.
        public bool IsReserved(EveEntityId id)=>current.ContainsKey(id);
        // Read-only, deterministic snapshot of ready entities owned by this session.
        public virtual IReadOnlyList<EveEntityLoader> Snapshot()
            => Array.AsReadOnly(entities.OrderBy(pair => pair.Key.Item1.Value).Where(pair => current.TryGetValue(pair.Key.Item1, out var gen) && gen == pair.Key.Item2 && pair.Value.IsReady)
                .Select(pair => pair.Value).ToArray());
        internal EveEntityDirectory(EveSessionContext session) { this.session = session; }
        public virtual EveEntityId AllocateHostId()
        {
            if (!session.IsActive || session.Role != EveSessionRole.Host)
                throw new InvalidOperationException("Only an active host can allocate network IDs.");
            return new EveEntityId(checked(++lastHostId));
        }
        internal void Register(EveEntityIdentity identity, EveEntityLoader loader)
        {
            if (!session.IsActive) throw new InvalidOperationException("Session has ended.");
            var key = (identity.Id, identity.Generation);
            if (entities.ContainsKey(key)) throw new InvalidOperationException("Duplicate entity presence.");
            if (current.TryGetValue(identity.Id, out var highest) && identity.Generation <= highest)
            {
                if (session.Role != EveSessionRole.Offline) throw new InvalidOperationException("Generation must increase.");
                identity.AdvanceGeneration(checked(highest + 1)); key = (identity.Id, identity.Generation);
            }
            identity.BindSession(session);
            if (identity.Id.IsHostAssigned) lastHostId = Math.Max(lastHostId, identity.Id.Value);
            entities.Add(key, loader); current[identity.Id] = identity.Generation;
        }
        internal void Remove(EveEntityIdentity identity, EveEntityLoader loader)
        {
            var removed = entities.Where(x => ReferenceEquals(x.Value, loader)).Select(x => x.Key).ToArray();
            foreach (var key in removed) entities.Remove(key);
            foreach (var id in removed.Select(x => x.Item1).Distinct()) if (!entities.Keys.Any(x => x.Item1 == id)) current.Remove(id);
        }
        public virtual bool TryGet(EveEntityId id, out EveEntityLoader loader)
        {
            if (current.TryGetValue(id, out var gen) && entities.TryGetValue((id, gen), out loader) && loader.IsReady) return true;
            loader = null;
            return false;
        }
        public bool TryGet(EveEntityId id, long generation, out EveEntityLoader loader)
        {
            if (current.TryGetValue(id, out var gen) && gen == generation && entities.TryGetValue((id, generation), out loader) && loader.IsReady) return true;
            loader = null; return false;
        }
        // Exact already-published presence may remain usable while a replacement prepares.
        // This never resolves another generation and never exposes an uncommitted candidate.
        public bool TryGetPublishedPresence(EveEntityId id, long generation, out EveEntityLoader loader)
        {
            if (entities.TryGetValue((id, generation), out loader) && loader.IsReady) return true;
            loader = null; return false;
        }
        internal IReadOnlyList<EveEntityLoader> Tracked() => entities.Values.Distinct().ToArray();
        internal void Publish(EveEntityLoader loader)
        {
            var context = loader.LoadContext;
            foreach (var old in entities.Where(x => x.Key.Item1 == context.Identity.Id && x.Key.Item2 < context.Generation).Select(x => x.Value).ToArray()) old.InvalidateAndHide();
        }
        internal void Clear()
        {
            var errors = new List<Exception>();
            // EN: Remove only successful cleanups; failed loaders remain reachable for retry.
            // VI: Chỉ xóa các lần dọn thành công; giữ loader lỗi để có thể thử lại.
            foreach (var pair in entities.ToArray())
                try
                {
                    pair.Value.Unload();
                    entities.Remove(pair.Key);
                }
                catch (Exception ex) { errors.Add(ex); }
            if (errors.Count != 0) throw new AggregateException("Entity cleanup failed.", errors);
        }
    }
}
