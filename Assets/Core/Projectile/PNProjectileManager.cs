#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Independent projectile service (same pattern as PNAreaManager, which it does not touch): a command queue that is safe against
//     re-entrancy, numbered handles, world leases, per-(definition, scene) pools under one global cap, cleanup on scene/world/
//     session end. Every tick it advances each projectile with that projectile's own clock.
// VI: Dịch vụ đạn độc lập (cùng khuôn PNAreaManager, không sửa nó): hàng đợi lệnh chống reentrancy, handle đánh số, world lease,
//     pool theo (definition, scene) dưới một trần toàn cục, dọn khi scene/world/session kết thúc.
[DisallowMultipleComponent]
public sealed class PNProjectileManager : MonoBehaviour
{
    private sealed class PoolEntry
    {
        internal GameObject Template; internal PNProjectilePool Pool; internal Scene Scene;
        internal readonly Dictionary<GameObject, PNProjectileInstance> Instances = new Dictionary<GameObject, PNProjectileInstance>();
    }
    private sealed class Entry
    {
        internal long Id; internal EveId DefinitionId; internal GameObject Root; internal PNProjectileInstance Instance; internal PoolEntry Pool; internal Scene Scene;
        internal EveWorldContext World; internal IDisposable Lease; internal bool Removed;
    }
    private static readonly object NoWorld = new object();
    private readonly Dictionary<(PNProjectileDefinition, Scene), PoolEntry> pools = new Dictionary<(PNProjectileDefinition, Scene), PoolEntry>();
    private readonly Dictionary<long, Entry> entries = new Dictionary<long, Entry>();
    private readonly List<Entry> order = new List<Entry>();
    private sealed class TargetState { internal readonly PNProjectileTargets Targets = new PNProjectileTargets(); internal float Since = float.MaxValue; }
    private readonly Dictionary<object, TargetState> targets = new Dictionary<object, TargetState>();
    private readonly HashSet<object> refreshed = new HashSet<object>();
    private readonly Queue<Action> pending = new Queue<Action>();
    private readonly HashSet<PNProjectileInstance> pendingResources = new HashSet<PNProjectileInstance>();
    private long sequence, stamp; private bool busy, closing, compactNeeded;
    public EveSessionContext Session { get; private set; }
    public int MaxActive { get; private set; } = PNProjectileProfile.DefaultMaxActive;
    public int MaxSubSteps { get; private set; } = 4;
    public float TargetRefreshSeconds { get; private set; } = .1f;
    public int ActiveCount => order.Count(x => !x.Removed && x.Instance != null && x.Instance.IsRunning);
    public int BorrowedCount => entries.Count;
    public int PooledObjectCount => pools.Values.Sum(x => x.Pool.CountAll);
    public int IdleObjectCount => pools.Values.Sum(x => x.Pool.CountIdle);
    public static PNProjectileManager Attach(EveRuntime runtime, PNProjectileProfile profile = null)
    {
        if (runtime == null || !runtime.IsReady) throw new InvalidOperationException("Ready runtime required.");
        var manager = runtime.GetComponent<PNProjectileManager>() ?? runtime.gameObject.AddComponent<PNProjectileManager>();
        manager.Initialize(EveRegistrySession.Context, profile); return manager;
    }
    public static PNProjectileManager Resolve(EveSessionContext session, Scene scene)
    {
        var ready = UnityEngine.Object.FindObjectsByType<PNProjectileManager>().Where(x => x.isActiveAndEnabled && ReferenceEquals(x.Session, session)).ToArray();
        return ready.FirstOrDefault(x => x.GetComponent<EveRuntime>() != null)
            ?? scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNProjectileManager>()).FirstOrDefault(x => x.isActiveAndEnabled && ReferenceEquals(x.Session, session))
            ?? throw new InvalidOperationException("Session projectile manager has not been initialized.");
    }
    public void Initialize(EveSessionContext session, PNProjectileProfile profile = null)
    {
        var chosen = profile ?? new PNProjectileProfile(); chosen.Validate();
        MaxActive = chosen.MaxActive; MaxSubSteps = chosen.MaxSubSteps; TargetRefreshSeconds = chosen.TargetRefreshSeconds;
        if (Session == session && !closing) return;
        Shutdown(); Session = session ?? throw new ArgumentNullException(nameof(session)); closing = false;
        SceneManager.sceneUnloaded += SceneUnloaded;
    }
    public bool Contains(PNProjectileHandle handle) => ReferenceEquals(handle.Manager, this) && entries.TryGetValue(handle.InstanceId, out var entry) && entry.Instance != null && entry.Instance.IsRunning;
    public PNProjectileSnapshot Get(PNProjectileHandle handle)
        => ReferenceEquals(handle.Manager, this) && entries.TryGetValue(handle.InstanceId, out var entry) && entry.Instance != null ? new PNProjectileSnapshot(handle, entry.DefinitionId, entry.DefinitionId, entry.Instance) : null;
    // Running projectiles (position, box, heading, world, handle) for systems that scan or clear them; geometry and filtering are the caller's.
    public IReadOnlyList<PNProjectileSnapshot> Snapshot()
        => order.Where(x => !x.Removed && x.Instance != null && x.Instance.IsRunning).Select(x => new PNProjectileSnapshot(new PNProjectileHandle(this, x.Id), x.DefinitionId, x.DefinitionId, x.Instance)).ToArray();

    // Nearest live target of a world within the radius on the given layers, from the candidate list the manager keeps while projectiles
    // fly there (null when none, or when no projectile has flown in that world yet).
    public PNStat FindNearest(EveWorldContext world, Vector3 position, float radius, int layerMask)
        => targets.TryGetValue((object)world ?? NoWorld, out var state) ? state.Targets.FindNearest(position, radius, layerMask) : null;
    // One-shot nearest-target query for launch time (an Operation aiming a volley): rebuilds a scratch candidate list from the current
    // entities, so it works before any projectile has flown in the world and never touches the per-tick lists.
    public PNStat ResolveNearest(EveWorldContext world, Vector3 position, float radius, int layerMask)
    {
        if (Session == null || !Session.IsActive) return null;
        aimScratch.Refresh(Session, world); aimScratch.UpdateBounds();
        return aimScratch.FindNearest(position, radius, layerMask);
    }
    private readonly PNProjectileTargets aimScratch = new PNProjectileTargets();
    // ---- queries for systems that act on running projectiles (area effects, clearing skills); they do their own filtering ----------
    // Allocation-free: appends the handles of running projectiles of `world` whose position is within `radius` of `center`.
    public int QueryRunning(EveWorldContext world, Vector3 center, float radius, List<PNProjectileHandle> results)
    {
        if (results == null) throw new ArgumentNullException(nameof(results));
        float limit = radius * radius; int added = 0;
        for (int i = 0; i < order.Count; i++)
        {
            var entry = order[i];
            if (entry.Removed || !ReferenceEquals(entry.World, world) || entry.Instance == null || !entry.Instance.IsRunning) continue;
            if ((entry.Instance.Position - center).sqrMagnitude > limit) continue;
            results.Add(new PNProjectileHandle(this, entry.Id)); added++;
        }
        return added;
    }
    // The projectile's own clock, for effects that slow or stop it (a modifier with an owner is reclaimed when the owner goes away).
    public bool TryGetClock(PNProjectileHandle handle, out PNProjectileClock clock)
    {
        clock = null;
        if (!ReferenceEquals(handle.Manager, this) || !entries.TryGetValue(handle.InstanceId, out var entry) || entry.Instance == null || !entry.Instance.IsRunning) return false;
        clock = entry.Instance.Clock; return clock != null;
    }
    // The projectile manager of a session in a scene, or null when there is none (no exception, unlike Resolve).
    public static PNProjectileManager Find(EveSessionContext session, Scene scene)
    {
        var ready = UnityEngine.Object.FindObjectsByType<PNProjectileManager>().Where(x => x.isActiveAndEnabled && ReferenceEquals(x.Session, session)).ToArray();
        return ready.FirstOrDefault(x => x.GetComponent<EveRuntime>() != null)
            ?? scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNProjectileManager>()).FirstOrDefault(x => x.isActiveAndEnabled && ReferenceEquals(x.Session, session))
            ?? ready.FirstOrDefault();
    }

    public PNProjectileReceipt Create(PNProjectileLaunch launch, PNProjectileSpawn spawn)
    {
        var receipt = new PNProjectileReceipt();
        Submit(() => CreateNow(launch, spawn, receipt)); return receipt;
    }
    private void CreateNow(PNProjectileLaunch launch, PNProjectileSpawn spawn, PNProjectileReceipt receipt)
    {
        GameObject root = null; PoolEntry pool = null; long id = 0;
        try
        {
            if (closing || !isActiveAndEnabled || Session == null || !Session.IsActive || !Session.HasAuthority) { receipt.Status = PNProjectileStatus.Rejected; return; }
            if (launch == null || spawn == null || launch.Source.SessionId != Session.SessionId || !spawn.Scene.IsValid() || !spawn.Scene.isLoaded)
                throw new ArgumentException("Projectile requires a launch from this session and a loaded scene.");
            var world = PNWorldGameplay.ForScene(Session, spawn.Scene);
            if (world != null && !world.CanExecute) { receipt.Status = PNProjectileStatus.Rejected; return; }
            var definition = Session.Registry.Get<PNProjectileDefinition>().GetEffective(launch.ProjectileId);
            definition.ValidateInput(launch.Input);
            if (!pools.TryGetValue((definition, spawn.Scene), out pool)) pool = AddPool(definition, spawn.Scene);
            if (pool.Pool.HasIdle) root = pool.Pool.TakeIdle();
            else
            {
                if (pools.Values.Sum(x => x.Pool.CountAll) >= MaxActive && !EvictOldestIdle()) { receipt.Status = PNProjectileStatus.Rejected; return; }
                root = pool.Pool.Create();
            }
            SceneManager.MoveGameObjectToScene(root, spawn.Scene);
            root.hideFlags = HideFlags.DontSave; root.transform.position = spawn.Position;
            var instance = root.GetComponent<PNProjectileInstance>();
            id = ++sequence; var handle = new PNProjectileHandle(this, id);
            var entry = new Entry { Id = id, DefinitionId = Session.Registry.Get<PNProjectileDefinition>().ResolveIdentity(launch.ProjectileId), Root = root, Instance = instance, Pool = pool, Scene = spawn.Scene, World = world, Lease = world?.AcquireLease("projectile") };
            entries.Add(id, entry); order.Add(entry);
            instance.Bind(this, handle, definition, launch, spawn, world); PNWorldPresentationRegistration.Register(world, root); root.SetActive(true);
            if (!instance.isActiveAndEnabled) throw new InvalidOperationException("Selected projectile component must be active after initialization.");
            if (!instance.IsRunning) throw new InvalidOperationException("Projectile ended during initialization.");
            receipt.Handle = handle; receipt.Status = PNProjectileStatus.Created;
        }
        catch (Exception error)
        {
            if (id != 0 && entries.TryGetValue(id, out var entry))
            {
                SafeStop(entry, PNProjectileEndReason.RuntimeError);
                if (entry.Instance.CleanupFailed) { receipt.Error = error; receipt.Status = PNProjectileStatus.Failed; return; }
                entries.Remove(id); entry.Removed = true; compactNeeded = true; entry.Lease?.Dispose();
            }
            if (root != null) { try { pool.Pool.Release(root, ++stamp); } catch (Exception cleanup) { Debug.LogException(cleanup, this); } }
            receipt.Error = error; receipt.Status = PNProjectileStatus.Failed;
        }
    }
    private PoolEntry AddPool(PNProjectileDefinition definition, Scene scene)
    {
        var template = definition.CreatePoolTemplate();
        var pool = new PoolEntry { Template = template, Pool = new PNProjectilePool(template), Scene = scene }; pools.Add((definition, scene), pool);
        SceneManager.MoveGameObjectToScene(template, scene);
        var local = pool;
        pool.Pool.Created += item =>
        {
            local.Instances[item] = item.GetComponent<PNProjectileInstance>();
            var storage = item.transform.root.gameObject; storage.hideFlags = HideFlags.DontSave; SceneManager.MoveGameObjectToScene(storage, local.Scene);
        };
        pool.Pool.Destroyed += item =>
        {
            if (!local.Instances.TryGetValue(item, out var retained)) return;
            local.Instances.Remove(item);
            // Unity may have destroyed the native object already; use the retained CLR reference.
            if (!ReferenceEquals(retained, null)) { pendingResources.Add(retained); retained.DisposePooledResources(); pendingResources.Remove(retained); }
        };
        return pool;
    }
    // Destroys the idle object that has waited longest in any pool; false when every object is active.
    private bool EvictOldestIdle()
    {
        PoolEntry oldest = null; long best = long.MaxValue;
        foreach (var pool in pools.Values) if (pool.Pool.HasIdle && pool.Pool.OldestIdleStamp < best) { best = pool.Pool.OldestIdleStamp; oldest = pool; }
        return oldest != null && oldest.Pool.DestroyOldestIdle();
    }
    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public bool Destroy(PNProjectileHandle handle, PNProjectileEndReason reason = PNProjectileEndReason.Destroyed, PNBuffSource source = null) => End(handle, reason, source);
    public bool End(PNProjectileHandle handle, PNProjectileEndReason reason = PNProjectileEndReason.Requested, PNBuffSource source = null)
    {
        if (!Enum.IsDefined(typeof(PNProjectileEndReason), reason)) throw new ArgumentException("Unknown projectile end reason.");
        if (!Contains(handle)) return false;
        Submit(() =>
        {
            if (!entries.TryGetValue(handle.InstanceId, out var entry) || !entry.Instance.IsRunning) return;
            if (source != null) entry.Instance.DestroyedBy = source;
            SafeStop(entry, reason);
            // Unity may be destroying the pool storage during scene-unload OnDisable: return after the scene event or the next tick.
            if (reason != PNProjectileEndReason.Disabled) Return(entry);
        });
        return true;
    }
    private void Submit(Action action)
    {
        pending.Enqueue(action); if (busy) return;
        busy = true;
        try
        {
            int count = 0;
            while (pending.Count != 0)
            {
                if (++count == 10001) { Debug.LogError("Projectile mutation cycle exceeded 10000 commands; shutting down manager.", this); Shutdown(); }
                try { pending.Dequeue()(); } catch (Exception error) { Debug.LogException(error, this); }
            }
        }
        finally { busy = false; }
    }
    public void Advance(float gameDelta)
    {
        if (!Finite(gameDelta) || gameDelta < 0) throw new ArgumentOutOfRangeException(nameof(gameDelta));
        if (closing) return;
        if (Session == null || !Session.IsActive) { Shutdown(); return; }
        Submit(() =>
        {
            if (compactNeeded) { order.RemoveAll(x => x.Removed); compactNeeded = false; }
            refreshed.Clear();
            int count = order.Count;
            for (int i = 0; i < count; i++)
            {
                var entry = order[i];
                if (entry.Removed) continue;
                try
                {
                    if (entry.Instance == null) { Return(entry); continue; }
                    if (entry.World != null && !entry.World.CanExecute) continue;
                    if (!entry.Instance.IsRunning) { Return(entry); continue; }
                    if (!entry.Instance.isActiveAndEnabled) { End(new PNProjectileHandle(this, entry.Id), PNProjectileEndReason.Disabled); continue; }
                    object key = (object)entry.World ?? NoWorld;
                    if (!targets.TryGetValue(key, out var state)) targets.Add(key, state = new TargetState());
                    if (refreshed.Add(key))
                    using (EveHotPathMetrics.Measure("projectile.candidates"))
                    {
                        // Membership at a limited rate, boxes every tick.
                        state.Since += gameDelta;
                        if (state.Since >= TargetRefreshSeconds) { state.Targets.Refresh(Session, entry.World); state.Since = 0f; }
                        state.Targets.UpdateBounds();
                    }
                    var physics = (entry.World != null ? entry.World.PhysicsOwner : entry.Scene).GetPhysicsScene();
                    entry.Instance.Tick(gameDelta, MaxSubSteps, state.Targets, physics);
                }
                catch (Exception error) { Debug.LogException(error, this); End(new PNProjectileHandle(this, entry.Id), PNProjectileEndReason.RuntimeError); }
            }
        });
    }
    private void SafeStop(Entry entry, PNProjectileEndReason reason)
    { try { if (entry.Instance != null) entry.Instance.Stop(reason); } catch (Exception error) { if (entry.Instance != null) entry.Instance.CleanupFailed = true; Debug.LogException(error, this); } }
    private void Return(Entry entry)
    {
        // A failed gameplay cleanup keeps its owner; pool eviction is not a cleanup retry.
        if (!ReferenceEquals(entry.Instance, null) && entry.Instance.CleanupFailed) return;
        if (entry.Removed) return;
        entries.Remove(entry.Id); entry.Removed = true; compactNeeded = true; entry.Lease?.Dispose();
        if (entry.Instance == null && !ReferenceEquals(entry.Instance, null)) entry.Instance.CleanupFailed = true;
        try { entry.Pool.Pool.Release(entry.Root, ++stamp); } catch (Exception error) { Debug.LogException(error, this); }
    }
    private void SceneUnloaded(Scene scene)
    {
        Submit(() =>
        {
            foreach (var entry in entries.Values.Where(x => x.Scene == scene).ToArray()) { SafeStop(entry, PNProjectileEndReason.SceneUnloaded); Return(entry); }
            // Storage is scene-local: rebuild idle pools after any scene teardown.
            foreach (var pair in pools.Where(x => x.Value.Scene == scene).ToArray())
            {
                if (entries.Values.Any(x => ReferenceEquals(x.Pool, pair.Value))) continue;
                pair.Value.Pool.Dispose(); DestroyOwned(pair.Value.Template); pools.Remove(pair.Key);
            }
        });
    }
    public void ReleaseWorld(EveWorldContext world)
    {
        if (busy) throw new InvalidOperationException("Projectile mutation must drain before world teardown.");
        foreach (var entry in entries.Values.Where(x => ReferenceEquals(x.World, world)).ToArray()) { entry.Instance?.Stop(PNProjectileEndReason.SceneUnloaded); Return(entry); }
        foreach (var pair in pools.Where(x => PNWorldGameplay.ForScene(Session, x.Value.Scene) == world).ToArray())
        {
            if (entries.Values.Any(x => ReferenceEquals(x.Pool, pair.Value))) throw new InvalidOperationException("World projectile cleanup remains pending.");
            foreach (var instance in pair.Value.Instances.Values) instance.DisposePooledResources();
            pair.Value.Pool.Dispose(); DestroyOwned(pair.Value.Template); pools.Remove(pair.Key);
        }
        targets.Remove((object)world ?? NoWorld);
    }
    public void Shutdown()
    {
        closing = true; SceneManager.sceneUnloaded -= SceneUnloaded;
        var errors = new List<Exception>();
        foreach (var entry in entries.Values.ToArray())
        {
            try
            {
                if (!ReferenceEquals(entry.Instance, null)) entry.Instance.Stop(PNProjectileEndReason.SessionEnded);
                entries.Remove(entry.Id); entry.Removed = true; entry.Lease?.Dispose();
            }
            catch (Exception error) { errors.Add(error); }
        }
        // Required gameplay failures retain entries/pools/session and block the next play.
        if (errors.Count != 0) throw new AggregateException("Projectile gameplay cleanup failed.", errors);
        order.Clear(); compactNeeded = false; targets.Clear();
        foreach (var resource in pendingResources.ToArray())
        {
            try { resource.DisposePooledResources(); pendingResources.Remove(resource); }
            catch (Exception error) { errors.Add(error); }
        }
        foreach (var pair in pools.ToArray())
        {
            try
            {
                foreach (var instance in pair.Value.Instances.ToArray()) { instance.Value.DisposePooledResources(); pair.Value.Instances.Remove(instance.Key); }
                pair.Value.Pool.Dispose(); DestroyOwned(pair.Value.Template); pools.Remove(pair.Key);
            }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Projectile resource cleanup failed.", errors);
        Session = null;
    }
    private static void DestroyOwned(GameObject obj) { if (obj == null) return; if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    // Tests and benchmarks that drive the manager themselves set this so the frame loop does not advance it a second time.
    public bool ManualAdvance { get; set; }
    private void Update() { if (!ManualAdvance) Advance(Time.deltaTime); }
    private void OnDisable() => Shutdown();
    private void OnDestroy() => Shutdown();
}
#endif
