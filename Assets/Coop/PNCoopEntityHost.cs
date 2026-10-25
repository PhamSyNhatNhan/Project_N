using System;
using System.Linq;
using EveEngine;

namespace ProjectN.Coop
{
    // EN: The entity host of a coop host (Docs/SummonPlan.md S6): a runtime entity (a summon) is enrolled like any host entity, so it gets its replication shell, and
    //     it is removed through its binding so the shell goes away on every client. Registered over the offline host when the play starts (PNRoomPlay).
    // VI: Host entity của host coop: entity sinh lúc chạy (summon) được ghi danh như entity host khác nên có shell; gỡ qua binding để shell biến mất trên mọi client.
    public sealed partial class PNNetworkManager
    {
        private sealed class CoopEntityHost : IPNEntityHost
        {
            private readonly PNNetworkManager manager;
            public CoopEntityHost(PNNetworkManager manager) { this.manager = manager ?? throw new ArgumentNullException(nameof(manager)); }
            public EveEntityPreparationScheduler.Candidate TrySpawn(PNEntityRequest request, Action<EveEntityLoader> configure = null, Action<EveEntityLoader> published = null, Action<Exception> failed = null)
            {
                if (!manager.IsPlaying || manager.Scheduler == null || !manager.Scheduler.HasPendingCapacity) return null;
                return manager.EnrollHost(request, null, configure, published, failed);
            }
            public bool Retire(EveEntityLoader loader)
            {
                var binding = manager.bindings.Values.Concat(manager.awaitingSpawn).FirstOrDefault(x => !x.Retiring && x.Candidate?.Loader == loader);
                if (binding != null) { manager.RetireBinding(binding); return true; }
                return manager.Scheduler != null && manager.Scheduler.TryRetirePublished(loader);
            }
        }
    }
}
