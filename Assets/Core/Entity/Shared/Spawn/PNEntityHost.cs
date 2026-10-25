#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;

// EN: Who spawns and removes entities for the machine that has authority in a session (Docs/SummonPlan.md D11). Code in Game (a summon Operation, an entity
//     lifetime) cannot see the coop layer, so it asks the host registered for its session: offline it builds and retires through the preparation scheduler;
//     in a coop host the network manager registers a host that also creates the replication shell and tears it down on retire. A client has none.
// VI: Ai sinh và gỡ entity cho máy có quyền trong một session. Code Game không thấy lớp coop nên hỏi host đã đăng ký cho session.
public interface IPNEntityHost
{
    // Starts spawning a runtime entity (host or offline only). `configure` runs while the object is inactive, after the factory's Configure and after the entity
    // has its identity; `published` runs after the entity is published. Returns null when the preparation queue is full or closed (nothing was started).
    EveEntityPreparationScheduler.Candidate TrySpawn(PNEntityRequest request, Action<EveEntityLoader> configure = null, Action<EveEntityLoader> published = null, Action<Exception> failed = null);
    // Removes a published entity; false when it is already being removed or was not built through this host's scheduler.
    bool Retire(EveEntityLoader loader);
}

public static class PNEntityHosts
{
    private static readonly Dictionary<Guid, IPNEntityHost> hosts = new Dictionary<Guid, IPNEntityHost>();
    public static void Set(EveSessionContext session, IPNEntityHost host)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (host == null) throw new ArgumentNullException(nameof(host));
        hosts[session.SessionId] = host;
    }
    public static void Remove(EveSessionContext session) { if (session != null) hosts.Remove(session.SessionId); }
    // Null when none is registered (a fixture that builds its own factory and scheduler).
    public static IPNEntityHost Find(EveSessionContext session) => session != null && hosts.TryGetValue(session.SessionId, out var host) ? host : null;
    public static IPNEntityHost For(EveSessionContext session)
        => Find(session) ?? throw new InvalidOperationException("This session has no entity host: only the machine with authority can spawn runtime entities.");
}
#endif
