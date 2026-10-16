#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum PNAreaStatus { Pending, Created, Rejected, Failed }
public enum PNAreaEndReason { Requested, Expired, OwnerUnavailable, Disabled, SceneUnloaded, SessionEnded, RuntimeError }
public readonly struct PNAreaHandle
{
    internal readonly PNAreaManager Manager;
    public long InstanceId { get; }
    internal PNAreaHandle(PNAreaManager manager, long id) { Manager = manager; InstanceId = id; }
    public bool IsAlive => Manager != null && Manager.Contains(this);
    public bool End() => Manager != null && Manager.End(this);
}
public sealed class PNAreaReceipt
{
    public PNAreaStatus Status { get; internal set; } = PNAreaStatus.Pending;
    public PNAreaHandle Handle { get; internal set; }
    public Exception Error { get; internal set; }
}
public sealed class PNAreaSnapshot
{
    public PNAreaHandle Handle { get; }
    public EveId Id { get; }
    public EveId ProfileId { get; }
    public bool IsRunning { get; }
    public PNAreaEndReason? EndReason { get; }
    public Vector3 Position { get; }
    internal PNAreaSnapshot(PNAreaHandle handle, EveId id, EveId profileId, PNAreaInstance instance)
    { Handle = handle; Id = id; ProfileId = profileId; IsRunning = instance.IsRunning; EndReason = instance.EndReason; Position = instance.transform.position; }
}
public sealed class PNAreaCreateRequest
{
    private readonly JToken input;
    public EveId AreaId { get; }
    public JToken Input => input?.DeepClone();
    public PNBuffSource Source { get; }
    public PNStat Owner { get; }
    public PNStat Follow { get; }
    public Vector3 Position { get; }
    public Scene Scene { get; }
    public PNAreaCreateRequest(EveId id, JToken input, PNBuffSource source, Scene scene, Vector3 position, PNStat owner = null, PNStat follow = null)
    { AreaId = id; this.input = input?.DeepClone(); Source = source; Scene = scene; Position = position; Owner = owner; Follow = follow; }
}

// Component instances are reused by the existing GameObject pool; manager owns every borrow.
public abstract class PNAreaInstance : MonoBehaviour, EveIPoolable
{
    public PNAreaHandle Handle { get; private set; }
    public bool IsRunning { get; private set; }
    public PNAreaEndReason? EndReason { get; private set; }
    internal bool CleanupFailed { get; set; }
    protected PNAreaManager Manager { get; private set; }
    protected PNAreaDefinition Definition { get; private set; }
    public virtual float RetirementDuration => 0;
    internal void Bind(PNAreaManager manager, PNAreaHandle handle, PNAreaDefinition definition, PNAreaCreateRequest request)
    { Manager = manager; Handle = handle; Definition = definition; EndReason = null; CleanupFailed = false; IsRunning = true; Initialize(request); }
    protected abstract void Initialize(PNAreaCreateRequest request);
    public abstract void Advance(float gameDelta);
    public virtual void AdvanceRetirement(float gameDelta) { }
    protected abstract void Cleanup();
    public bool Finish(PNAreaEndReason reason = PNAreaEndReason.Requested) => Manager != null && Manager.End(Handle, reason);
    internal void Stop(PNAreaEndReason reason)
    {
        if (!IsRunning && !CleanupFailed) return;
        IsRunning = false; EndReason = reason; CleanupFailed = true;
        Cleanup(); CleanupFailed = false;
    }
    public virtual void OnPoolCreated() { }
    public virtual void OnPoolGet(EveEasyPooling pool) { }
    // Explicit destruction hook is required for retained resources in native Edit fixtures too.
    public virtual void DisposePooledResources() { }
    public virtual void OnPoolRelease()
    {
        Manager = null; Definition = null; Handle = default; EndReason = null; IsRunning = false;
        if (CleanupFailed) throw new InvalidOperationException("Faulted area cleanup; existing pool must evict this instance.");
    }
    protected virtual void OnDisable() { if (IsRunning) Finish(PNAreaEndReason.Disabled); }
    protected virtual void OnDestroy() { if (IsRunning) Finish(PNAreaEndReason.Disabled); DisposePooledResources(); }
}

[DisallowMultipleComponent]
public sealed class PNAreaManager : MonoBehaviour
{
    private sealed class PoolEntry
    {
        internal GameObject Template; internal EveEasyPooling Pool; internal Scene Scene;
        internal readonly Dictionary<GameObject, PNAreaInstance> Instances = new Dictionary<GameObject, PNAreaInstance>();
    }
    private sealed class Entry { internal EveId Id, ProfileId; internal GameObject Root; internal PNAreaInstance Instance; internal PoolEntry Pool; internal Scene Scene; internal float Retirement; }
    private readonly Dictionary<(PNAreaDefinition, Scene), PoolEntry> pools = new Dictionary<(PNAreaDefinition, Scene), PoolEntry>();
    private readonly Dictionary<long, Entry> entries = new Dictionary<long, Entry>();
    private readonly Queue<Action> pending = new Queue<Action>();
    private readonly HashSet<PNAreaInstance> pendingResources = new HashSet<PNAreaInstance>();
    private long sequence; private bool busy, closing;
    public EveSessionContext Session { get; private set; }
    public int ActiveCount => entries.Values.Count(x => x.Instance != null && x.Instance.IsRunning);
    public int BorrowedCount => entries.Count;
    public int PooledObjectCount => pools.Values.Sum(x => x.Pool.CountAll);
    public static PNAreaManager Attach(EveRuntime runtime)
    {
        if (runtime == null || !runtime.IsReady) throw new InvalidOperationException("Ready runtime required.");
        var manager = runtime.GetComponent<PNAreaManager>() ?? runtime.gameObject.AddComponent<PNAreaManager>();
        manager.Initialize(EveRegistrySession.Context); return manager;
    }
    public static PNAreaManager Resolve(EveSessionContext session, Scene scene)
    {
        var ready = UnityEngine.Object.FindObjectsByType<PNAreaManager>().Where(x => x.isActiveAndEnabled && ReferenceEquals(x.Session, session)).ToArray();
        return ready.FirstOrDefault(x => x.GetComponent<EveRuntime>() != null)
            ?? scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNAreaManager>()).FirstOrDefault(x => x.isActiveAndEnabled && ReferenceEquals(x.Session, session))
            ?? throw new InvalidOperationException("Session area manager has not been initialized.");
    }
    public void Initialize(EveSessionContext session)
    {
        if (Session == session && !closing) return;
        Shutdown(); Session = session ?? throw new ArgumentNullException(nameof(session)); closing = false;
        SceneManager.sceneUnloaded += SceneUnloaded;
    }
    public bool Contains(PNAreaHandle handle) => ReferenceEquals(handle.Manager, this) && entries.TryGetValue(handle.InstanceId, out var entry) && entry.Instance != null && entry.Instance.IsRunning;
    public PNAreaSnapshot Get(PNAreaHandle handle)
        => ReferenceEquals(handle.Manager, this) && entries.TryGetValue(handle.InstanceId, out var entry) && entry.Instance != null
            ? new PNAreaSnapshot(handle, entry.Id, entry.ProfileId, entry.Instance) : null;
    public IReadOnlyList<PNAreaSnapshot> Snapshot() => entries.Where(x => x.Value.Instance != null)
        .Select(x => new PNAreaSnapshot(new PNAreaHandle(this, x.Key), x.Value.Id, x.Value.ProfileId, x.Value.Instance)).ToArray();
    public PNAreaReceipt Create(PNAreaCreateRequest request)
    {
        var receipt = new PNAreaReceipt();
        Submit(() => CreateNow(request, receipt)); return receipt;
    }
    private void CreateNow(PNAreaCreateRequest request, PNAreaReceipt receipt)
    {
        GameObject root = null; PoolEntry pool = null; long id = 0;
        try
        {
            if (closing || !isActiveAndEnabled || Session == null || !Session.IsActive || !Session.HasAuthority) { receipt.Status = PNAreaStatus.Rejected; return; }
            if (request == null || request.Source == null || request.Source.SessionId != Session.SessionId || !request.Scene.IsValid() || !request.Scene.isLoaded)
                throw new ArgumentException("Area requires valid source/session and loaded scene.");
            var p = request.Position;
            if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z)) throw new ArgumentException("Finite area position required.");
            var definition = Session.Registry.Get<PNAreaDefinition>().GetEffective(request.AreaId);
            definition.ValidateInput(request.Input);
            if (!pools.TryGetValue((definition, request.Scene), out pool))
            {
                var template = definition.CreatePoolTemplate();
                pool = new PoolEntry { Template = template, Pool = new EveEasyPooling(template), Scene = request.Scene }; pools.Add((definition, request.Scene), pool);
                SceneManager.MoveGameObjectToScene(template, request.Scene);
                var localPool = pool;
                pool.Pool.Created += item => {
                    localPool.Instances[item] = item.GetComponent<PNAreaInstance>();
                    var storage = item.transform.root.gameObject;
                    storage.hideFlags = HideFlags.DontSave;
                    SceneManager.MoveGameObjectToScene(storage, localPool.Scene);
                };
                pool.Pool.Destroyed += item => {
                    if (!localPool.Instances.TryGetValue(item, out var retained)) return;
                    localPool.Instances.Remove(item);
                    // Unity may have destroyed the native object already; use the retained CLR reference.
                    if (!ReferenceEquals(retained, null))
                    {
                        pendingResources.Add(retained);
                        retained.DisposePooledResources();
                        pendingResources.Remove(retained);
                    }
                };
            }
            root = pool.Pool.Get(); SceneManager.MoveGameObjectToScene(root, request.Scene);
            root.hideFlags = HideFlags.DontSave; root.transform.position = p;
            var instance = root.GetComponent<PNAreaInstance>();
            id = ++sequence; var handle = new PNAreaHandle(this, id);
            entries.Add(id, new Entry { Id = Session.Registry.Get<PNAreaDefinition>().ResolveIdentity(request.AreaId), ProfileId = definition.Id, Root = root, Instance = instance, Pool = pool, Scene = request.Scene });
            instance.Bind(this, handle, definition, request); root.SetActive(true);
            if (!instance.isActiveAndEnabled) throw new InvalidOperationException("Selected area component must be active after initialization.");
            // Immediate admission occurs after activation, when owned clock effects may be acquired.
            instance.Advance(0);
            if (!instance.IsRunning) throw new InvalidOperationException("Area ended during initialization.");
            receipt.Handle = handle; receipt.Status = PNAreaStatus.Created;
        }
        catch (Exception error)
        {
            if (id != 0 && entries.TryGetValue(id, out var entry))
            {
                SafeStop(entry, PNAreaEndReason.RuntimeError);
                if (entry.Instance.CleanupFailed) { receipt.Error = error; receipt.Status = PNAreaStatus.Failed; return; }
                entries.Remove(id);
            }
            if (root != null) { try { pool.Pool.Release(root); } catch (Exception cleanup) { Debug.LogException(cleanup, this); } }
            receipt.Error = error; receipt.Status = PNAreaStatus.Failed;
        }
    }
    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public bool End(PNAreaHandle handle, PNAreaEndReason reason = PNAreaEndReason.Requested)
    {
        if (!Enum.IsDefined(typeof(PNAreaEndReason), reason)) throw new ArgumentException("Unknown area end reason.");
        if (!Contains(handle)) return false;
        Submit(() => {
            if (!entries.TryGetValue(handle.InstanceId, out var entry) || !entry.Instance.IsRunning) return;
            SafeStop(entry, reason);
            bool immediate = reason == PNAreaEndReason.Disabled || reason == PNAreaEndReason.SceneUnloaded || reason == PNAreaEndReason.SessionEnded || reason == PNAreaEndReason.RuntimeError;
            entry.Retirement = immediate ? 0 : entry.Instance.RetirementDuration;
            // Unity may be destroying the pool storage during scene-unload OnDisable.
            // Gameplay cleanup is immediate; return after sceneUnloaded or next manager update.
            if (entry.Retirement <= 0 && reason != PNAreaEndReason.Disabled) Return(handle.InstanceId, entry);
        }); return true;
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
                if (++count == 10001) { Debug.LogError("Area mutation cycle exceeded 10000 commands; shutting down manager.", this); Shutdown(); }
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
        Submit(() => {
            foreach (var pair in entries.ToArray())
            {
                var entry = pair.Value;
                try
                {
                    if (entry.Instance == null) { Return(pair.Key, entry); continue; }
                    if (entry.Instance.IsRunning && !entry.Instance.isActiveAndEnabled) { End(new PNAreaHandle(this, pair.Key), PNAreaEndReason.Disabled); continue; }
                    if (entry.Instance.IsRunning) entry.Instance.Advance(gameDelta);
                    else { entry.Instance.AdvanceRetirement(gameDelta); entry.Retirement -= gameDelta; if (entry.Retirement <= 0) Return(pair.Key, entry); }
                }
                catch (Exception error) { Debug.LogException(error, this); End(new PNAreaHandle(this, pair.Key), PNAreaEndReason.RuntimeError); }
            }
        });
    }
    private void SafeStop(Entry entry, PNAreaEndReason reason)
    { try { if (entry.Instance != null) entry.Instance.Stop(reason); } catch (Exception error) { if (entry.Instance != null) entry.Instance.CleanupFailed = true; Debug.LogException(error, this); } }
    private void Return(long id, Entry entry)
    {
        // A failed gameplay cleanup keeps its owner; pool eviction is not a cleanup retry.
        if (!ReferenceEquals(entry.Instance, null) && entry.Instance.CleanupFailed) return;
        entries.Remove(id);
        if (entry.Instance == null && !ReferenceEquals(entry.Instance, null)) entry.Instance.CleanupFailed = true;
        try { entry.Pool.Pool.Release(entry.Root); } catch (Exception error) { Debug.LogException(error, this); }
    }
    private void SceneUnloaded(Scene scene)
    {
        Submit(() => {
            foreach (var pair in entries.Where(x => x.Value.Scene == scene).ToArray()) { SafeStop(pair.Value, PNAreaEndReason.SceneUnloaded); Return(pair.Key, pair.Value); }
            // Storage is scene-local: rebuild idle pools after any scene teardown.
            foreach (var pair in pools.Where(x => x.Value.Scene == scene).ToArray())
            {
                if (entries.Values.Any(x => ReferenceEquals(x.Pool, pair.Value))) continue;
                pair.Value.Pool.Dispose(); DestroyOwned(pair.Value.Template); pools.Remove(pair.Key);
            }
        });
    }
    public void Shutdown()
    {
        closing = true; SceneManager.sceneUnloaded -= SceneUnloaded;
        var errors = new List<Exception>();
        foreach (var pair in entries.ToArray())
        {
            try
            {
                if (!ReferenceEquals(pair.Value.Instance, null)) pair.Value.Instance.Stop(PNAreaEndReason.SessionEnded);
                entries.Remove(pair.Key);
            }
            catch (Exception error) { errors.Add(error); }
        }
        // Required gameplay failures retain entries/pools/session and block the next play.
        if (errors.Count != 0) throw new AggregateException("Area gameplay cleanup failed.", errors);
        foreach (var resource in pendingResources.ToArray())
        {
            try { resource.DisposePooledResources(); pendingResources.Remove(resource); }
            catch (Exception error) { errors.Add(error); }
        }
        foreach (var pair in pools.ToArray())
        {
            try
            {
                foreach (var instance in pair.Value.Instances.ToArray())
                {
                    instance.Value.DisposePooledResources();
                    pair.Value.Instances.Remove(instance.Key);
                }
                pair.Value.Pool.Dispose(); DestroyOwned(pair.Value.Template); pools.Remove(pair.Key);
            }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Area resource cleanup failed.", errors);
        Session = null;
    }
    private static void DestroyOwned(GameObject obj) { if (obj == null) return; if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    private void Update() => Advance(Time.deltaTime);
    private void OnDisable() => Shutdown();
    private void OnDestroy() => Shutdown();
}
#endif
