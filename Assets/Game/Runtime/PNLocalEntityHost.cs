#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;

// EN: The entity host of an offline play (and of a host without a coop layer): builds through the preparation scheduler, gives the entity a fresh id and the
//     request's character, and retires it through the same scheduler. The coop host registers its own over this one (Docs/SummonPlan.md S6).
// VI: Host entity của chơi offline: sinh qua scheduler chuẩn bị, cấp id mới và nhân vật của request; gỡ qua cùng scheduler.
public sealed class PNLocalEntityHost : IPNEntityHost
{
    private readonly EveSessionContext session;
    private readonly IPNEntityFactory factory;
    private readonly EveEntityPreparationScheduler scheduler;
    public PNLocalEntityHost(EveSessionContext session, IPNEntityFactory factory, EveEntityPreparationScheduler scheduler)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }
    public EveEntityPreparationScheduler.Candidate TrySpawn(PNEntityRequest request, Action<EveEntityLoader> configure = null, Action<EveEntityLoader> published = null, Action<Exception> failed = null)
    {
        if (session.Role == EveSessionRole.Client) throw new InvalidOperationException("A client does not spawn runtime entities.");
        return PNEntitySpawner.TryEnqueue(scheduler, factory, request, loader => { PNEntitySpawner.AssignNew(session, loader, request); configure?.Invoke(loader); }, published, failed);
    }
    public bool Retire(EveEntityLoader loader) => scheduler.TryRetirePublished(loader);
}
#endif
