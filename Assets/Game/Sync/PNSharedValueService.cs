#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The Game glue of the shared value store (Docs/SkillNetworkFrameworkPlan.md 4.7, G5). One per play: it makes the session's store and a store for each world instance that has world keys, found by
//     the session through PNSharedValues. It runs the same way on the host, offline and on a client (the host writes, a client reads what arrives); a client's store of a world is made when the world
//     becomes ready, and the values the host already sent wait in the sync service until it is. A world's store is made on first use too, because a checkpoint restore fills it before the world is announced.
//     When a world unloads its store goes with it: two worlds alive at once (a room and a side world, two copies of one map) never share a value.
// VI: Phần Game của kho giá trị dùng chung: kho của phiên và kho của từng world instance có khóa world; chạy giống nhau ở host, offline và client. World dỡ thì kho đi theo.
public sealed class PNSharedValueService : IDisposable
{
    private readonly EveSessionContext session;
    private readonly PNWorldRuntime worlds;
    private readonly PNSyncService sync;
    private readonly Dictionary<EveWorldContext, PNSharedStore> stores = new Dictionary<EveWorldContext, PNSharedStore>();
    private readonly bool anyWorldKeys;
    private bool disposed;
    public PNSharedValues Values { get; }
    public PNSharedStore Session { get; }
    public int WorldStores => stores.Count;

    public PNSharedValueService(EveSessionContext session, PNWorldRuntime worlds, PNSyncService sync)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.worlds = worlds ?? throw new ArgumentNullException(nameof(worlds)); this.sync = sync ?? throw new ArgumentNullException(nameof(sync));
        anyWorldKeys = session.Registry.SharedValues().Snapshot().Values.Any(x => x.Scope == PNSharedScope.World);
        Session = PNSharedStore.ForSession(sync);
        Values = new PNSharedValues(session.Registry, Session, StoreOf);
        PNSharedValues.Attach(session, Values);
        worlds.WorldReady += OnReady; worlds.WorldUnloading += OnUnloading;
    }

    private PNSharedStore StoreOf(EveWorldContext world)
    {
        if (disposed || world == null || !anyWorldKeys || !ReferenceEquals(world.Session, session)) return null;
        if (stores.TryGetValue(world, out var found)) return found;
        if (world.State == EveWorldState.Unloading || world.State == EveWorldState.Failed || world.State == EveWorldState.Disposed) return null;
        var store = PNSharedStore.ForWorld(sync, world);
        stores.Add(world, store);
        return store;
    }

    private void OnReady(EveWorldContext world) { StoreOf(world); }

    private void OnUnloading(EveWorldContext world)
    {
        if (!stores.TryGetValue(world, out var store)) return;
        stores.Remove(world); store.Dispose();
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        worlds.WorldReady -= OnReady; worlds.WorldUnloading -= OnUnloading;
        PNSharedValues.Detach(session);
        foreach (var store in stores.Values) store.Dispose();
        stores.Clear(); Session.Dispose();
    }
}

// EN: What a world keeps in its checkpoint of the shared store: the values of the world keys that declare `persist` (an object id -> value). It is not named by a world policy: a world whose content declares
//     any persistent world key gets it added to the list of world state providers (WorldProviders), the same on capture, restore, export and import, so no world has to remember it.
// VI: Phần checkpoint của world cho kho dùng chung: giá trị các khóa world có `persist`. Không do policy của world khai báo; WorldProviders tự thêm khi nội dung có khóa world cần lưu.
public sealed class PNSharedWorldStateProvider : IPNWorldStateProvider
{
    public static readonly EveId Id = EveId.Parse("project_n:shared_world_state");
    public const int Schema = 1;

    public static IReadOnlyList<EveId> WorldProviders(EveRegistryHub hub, PNWorldPolicy policy)
    {
        var declared = policy.WorldStateProviders;
        if (declared.Contains(Id) || !hub.SharedValues().Snapshot().Values.Any(x => x.Scope == PNSharedScope.World && x.Persist)) return declared;
        return declared.Concat(new[] { Id }).ToArray();
    }

    private static PNSharedStore Store(PNWorldStateContext context) => PNSharedValues.Find(context.Session)?.WorldStore(context.World);

    public JToken Capture(PNWorldStateContext context) => Store(context)?.CapturePersisted() ?? new JObject();

    public void Validate(PNWorldStateContext context, JToken state)
        => PNSharedStore.ValidatePersisted(context.Session.Registry, PNSharedScope.World, state as JObject ?? throw new FormatException("Shared world state is an object."));

    public void Restore(PNWorldStateContext context, JToken state)
    {
        Validate(context, state);
        var store = Store(context) ?? throw new InvalidOperationException("The world has no shared value store to restore into.");
        store.RestorePersisted((JObject)state);
    }

    public void Dispose() { }
}
#endif
