#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;

// EN: Roles of an entity as the rest of the game needs to ask about them (Docs/EntitySpawnPlan.md).
// VI: Hỏi vai trò của một entity theo id.
public static class PNEntityRoles
{
    // A role whose recipe is the player's. An empty id (an authored scene entity) has no role.
    public static bool IsPlayer(EveRegistryHub registry, string entity)
        => !string.IsNullOrEmpty(entity) && registry.Instances().GetEffectiveKind<PNEntityDefinition>(EveId.Parse(entity)).IsPlayer;

    // The same question for an entity that exists: its role was applied when it was built (an authored scene entity has no role and is not a player).
    // The stat class says nothing about it; a summon, NPC or prop may use any stat.
    public static bool IsPlayer(UnityEngine.Component entity) => entity != null && entity.GetComponent<PNEntityRole>()?.IsPlayer == true;
}

// EN: The one way to start spawning an entity: Get -> Configure -> (prepare, commit) -> Published through the preparation scheduler, bound to its
//     world. Callers add their own steps through the callbacks. By default the factory's Published runs first, then `published`; a caller that must
//     run its own step before it (the coop client) passes factoryPublished:false and calls factory.Published itself.
// VI: Lối vào duy nhất để bắt đầu sinh một entity qua scheduler chuẩn bị, gắn với world của nó.
public static class PNEntitySpawner
{
    // For a host or offline spawn of a runtime entity whose prefab does not name its character (an enemy): gives it a fresh entity id and the request's
    // character. Restores, the coop client and world transfers assign their own identity.
    public static void AssignNew(EveSessionContext session, EveEntityLoader loader, PNEntityRequest request)
    {
        if (session.Role == EveSessionRole.Client) throw new InvalidOperationException("A client receives its entity ids from the host.");
        EveEntityId id;
        do { id = session.Role == EveSessionRole.Host ? session.Entities.AllocateHostId() : session.Entities.AllocateOfflineId(); } while (session.Entities.IsReserved(id));
        loader.GetComponent<EveEntityIdentity>().Assign(id, EveId.Parse(request.Character), request.Owner);
    }

    public static EveEntityPreparationScheduler.Candidate Enqueue(EveEntityPreparationScheduler scheduler, IPNEntityFactory factory, PNEntityRequest request,
        Action<EveEntityLoader> configure = null, Action<EveEntityLoader> published = null, Action<Exception> failed = null, Action<EveEntityLoader> beforePublish = null,
        Func<EveEntityLoader, bool> preparePublication = null, Func<bool> canBegin = null, bool factoryPublished = true)
    {
        if (scheduler == null) throw new ArgumentNullException(nameof(scheduler));
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (request == null) throw new ArgumentNullException(nameof(request));
        var candidate = scheduler.Enqueue(() => factory.Get(request), loader => { factory.Configure(loader, request); configure?.Invoke(loader); }, factory.Return,
            published == null && !factoryPublished ? (Action<EveEntityLoader>)null : loader => { if (factoryPublished) factory.Published(loader); published?.Invoke(loader); },
            failed, beforePublish, preparePublication, canBegin);
        if (request.World != null) scheduler.AssociateWorld(candidate, request.World);
        return candidate;
    }

    // Enqueue that answers null instead of throwing when the preparation queue is full or closed, for spawns that may be refused (summons).
    public static EveEntityPreparationScheduler.Candidate TryEnqueue(EveEntityPreparationScheduler scheduler, IPNEntityFactory factory, PNEntityRequest request,
        Action<EveEntityLoader> configure = null, Action<EveEntityLoader> published = null, Action<Exception> failed = null, Action<EveEntityLoader> beforePublish = null,
        Func<EveEntityLoader, bool> preparePublication = null, Func<bool> canBegin = null, bool factoryPublished = true)
    {
        if (scheduler == null) throw new ArgumentNullException(nameof(scheduler));
        if (!scheduler.HasPendingCapacity) return null;
        return Enqueue(scheduler, factory, request, configure, published, failed, beforePublish, preparePublication, canBegin, factoryPublished);
    }
}
#endif
