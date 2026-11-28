#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum PNAreaStatus { Pending, Created, Rejected, Failed }
// EN: Requested, Expired, OwnerUnavailable and Replaced are END reasons (the area really finished: `OnEnd`, then its Ending phase); Disabled, SceneUnloaded, SessionEnded and
//     RuntimeError are CANCEL reasons (forced: `OnCancel`, no Ending). The value is the byte of the sync End record, so a reason is only ever appended (Docs/InstanceLifecyclePlan.md 3.2).
// VI: Requested, Expired, OwnerUnavailable, Replaced la ly do END (area ket thuc that: `OnEnd`, roi pha Ending); Disabled, SceneUnloaded, SessionEnded, RuntimeError la CANCEL (cuong buc: `OnCancel`, khong Ending).
public enum PNAreaEndReason { Requested, Expired, OwnerUnavailable, Disabled, SceneUnloaded, SessionEnded, RuntimeError, Replaced }
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
    public PNCombatSource Source { get; }
    public PNStat Owner { get; }
    public PNStat Follow { get; }
    public Vector3 Position { get; }
    public Scene Scene { get; }
    // EN: A view-only copy on a machine without authority (plan O5): no combat source, no Buff and no clock effect; `owner` / `follow` (when this machine has them) are only for what is drawn and where it sits;
    //     `scale` is the creator's area_scale the record carries.
    // VI: Ban hinh chi de xem o may khong co authority: khong nguon combat, khong Buff, khong tac dong dong ho; owner / follow chi de ve va dat cho; scale la area_scale cua nguoi tao.
    public bool IsReplica { get; private set; }
    public double ReplicaScale { get; private set; } = 1d;
    // EN: The cast that made the area (0 = none) and which area of that cast (the Action ordinal). The authority writes it into the record; a shooter's guess is named by it, so the record can take the guess over.
    // VI: Cast tao ra vung (0 = khong) va la vung thu may cua cast (so thu tu Action). Authority ghi vao ban ghi; ban doan cua nguoi ban mang ten do de ban ghi nhan lai.
    public long CastKey { get; private set; }
    public int CastIndex { get; private set; }
    public PNAreaCreateRequest WithCast(long castKey, int castIndex) { CastKey = castKey > 0 ? castKey : 0; CastIndex = Math.Max(0, Math.Min(255, castIndex)); return this; }
    public static PNAreaCreateRequest Replica(EveId id, JToken input, Scene scene, Vector3 position, PNStat owner, PNStat follow, double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale < 0d) throw new ArgumentException("An area copy needs a finite scale.", nameof(scale));
        return new PNAreaCreateRequest(id, input, scene, position, owner, follow, scale);
    }
    private PNAreaCreateRequest(EveId id, JToken input, Scene scene, Vector3 position, PNStat owner, PNStat follow, double scale)
    { AreaId = id; this.input = input?.DeepClone(); Scene = scene; Position = position; Owner = owner; Follow = follow; IsReplica = true; ReplicaScale = scale; }
    public PNAreaCreateRequest(EveId id, JToken input, PNCombatSource source, Scene scene, Vector3 position, PNStat owner = null, PNStat follow = null)
    { AreaId = id; this.input = input?.DeepClone(); Source = source; Scene = scene; Position = position; Owner = owner; Follow = follow; }
}

// Component instances are reused by the existing GameObject pool; manager owns every borrow.
public abstract class PNAreaInstance : MonoBehaviour, EveIPoolable, IPNTimeTarget
{
    // EN: Every area carries a clock (PNAreaClock, minScale 0). An area that is affected by time (the default) registers in the world's PNTimeTargets while it
    //     runs, so a time effect can slow or stop it; the manager then hands `Advance` the delta scaled by that clock. An implementation that must not be
    //     affected (the time-scale area itself, so two overlapping zones cannot lengthen each other) overrides this to false. The Ending phase runs on the same clock.
    // VI: Mỗi vùng có đồng hồ (PNAreaClock, minScale 0). Vùng chịu thời gian (mặc định) đăng ký vào PNTimeTargets của world khi chạy; manager đưa OnPerform
    //     delta đã nhân theo đồng hồ đó. Implementation không muốn chịu (chính vùng thời gian) override false. Pha Ending chạy trên cùng đồng hồ.
    public virtual bool AffectedByTime => true;
    public PNAreaClock Clock { get; private set; }
    EveTimeScale IPNTimeTarget.TimeClock => Clock;
    int IPNTimeTarget.TimeLayer => PNLayers.Area;
    Vector3 IPNTimeTarget.TimePosition => transform.position;
    bool IPNTimeTarget.TimeActive => (life.IsRunning || life.IsEnding) && Clock != null;
    // The reasons of an area: which are a real finish (END) and which a forced stop (CANCEL); see PNAreaEndReason.
    private static readonly PNInstanceReasonTable Reasons = new PNInstanceReasonTable(8, (int)PNAreaEndReason.Requested, (int)PNAreaEndReason.Expired, (int)PNAreaEndReason.OwnerUnavailable, (int)PNAreaEndReason.Replaced);
    internal static bool IsEndReason(PNAreaEndReason reason) => Reasons.IsEnd((int)reason);
    private readonly PNInstanceLifecycle life = new PNInstanceLifecycle(Reasons);
    private PNTimeTargets.Entry timeEntry;
    private PNAreaCreateRequest created; private double scaleFactor = double.NaN;
    // Who made it (null for a world's own); what the camera audience resolves an area to.
    public PNStat CreatedBy => created?.Owner;
    // The creator's `area_scale` (Scale route over a base of 1, read from the source snapshot at creation; 1 = unchanged). What it scales is the implementation's choice.
    protected double ScaleFactor
    {
        get
        {
            if (double.IsNaN(scaleFactor)) scaleFactor = created != null && created.IsReplica ? created.ReplicaScale : created?.Source?.Snapshot != null ? created.Source.Snapshot.Calculate(1d, PNContentIds.AreaScale) : 1d;
            return scaleFactor;
        }
    }
    // The creator's area_scale as the sync record carries it.
    public double SyncScale => ScaleFactor;
    // A guess made with another area_scale than the authority's record carries takes the record's (the area draws at the size the authority's area really has).
    internal void CorrectScale(double scale) { scaleFactor = scale; OnScaleCorrected(scale); }
    protected virtual void OnScaleCorrected(double scale) { }
    internal float ScaleDelta(float gameDelta)
    {
        if (!AffectedByTime || Clock == null || Clock.ModifierCount == 0) return gameDelta;
        Clock.Advance(gameDelta);
        return gameDelta * Clock.Scale;
    }
    private void ReleaseTime()
    {
        if (timeEntry != null) { PNTimeTargets.Unregister(timeEntry); timeEntry = null; }
        if (Clock != null) Clock.ClearModifiers();
    }
    public PNAreaHandle Handle { get; private set; }
    // A view-only copy of the authority's area: draws, applies nothing, ends when the authority says so.
    public bool IsReplica => created != null && created.IsReplica;
    // Seconds of its own life the area has lived (what a late observer's copy is brought to); 0 when the implementation does not say.
    public virtual float ProgressSeconds => 0f;
    // A claim of presence from the machine of a player (plan 4.4c V3): the implementation that applies something to what stands inside takes it and answers whether it was valid; the default takes nothing.
    public virtual bool TakeClaimedPresence(PNStat target, bool inside, float tolerance) => false;
    // EN: Running = alive (the area applies what it applies). Ending = it really finished and only plays its own end (`OnEnding`): it is not running, its handle is not alive,
    //     it is not captured for a late joiner and a claim on it is refused. The first reason that took effect is `EndReason`.
    // VI: Running = con song. Ending = da ket thuc that, chi choi phan ket thuc cua no: khong chay, handle khong song, khong vao baseline, claim bi tu choi.
    public bool IsRunning => life.IsRunning;
    public bool IsEnding => life.IsEnding;
    internal bool IsDone => life.Phase == PNInstancePhase.Done;
    public EveWorldContext World { get; private set; }
    public PNAreaEndReason? EndReason => life.Reason.HasValue ? (PNAreaEndReason?)life.Reason.Value : null;
    internal bool CleanupFailed { get; set; }
    protected PNAreaManager Manager { get; private set; }
    protected PNAreaDefinition Definition { get; private set; }
    public virtual void ActorWorldChanging(PNStat actor) { }
    internal void Bind(PNAreaManager manager, PNAreaHandle handle, PNAreaDefinition definition, PNAreaCreateRequest request)
    { Manager = manager; World=PNWorldGameplay.ForScene(manager.Session,request.Scene);Handle = handle; Definition = definition; CleanupFailed = false; life.Start();
        Clock = GetComponent<PNAreaClock>(); if (Clock != null) Clock.ClearModifiers(); created = request; scaleFactor = double.NaN;
        OnStart(request);
        if (AffectedByTime && Clock != null) timeEntry = PNTimeTargets.For(World).Register(this);
    }

    // ---- the hooks of an area (Docs/InstanceLifecyclePlan.md 3.1): Start -> Perform -> End -> Ending -> Cleanup, or Cancel -> Cleanup ----
    // Once when the area is bound, on every machine (the real area, a guess, a copy).
    protected abstract void OnStart(PNAreaCreateRequest request);
    // Every frame while Running, `gameDelta` already scaled by the area's clock.
    protected abstract void OnPerform(float gameDelta);
    // Once, when the area really finishes (an END reason). Read `EndingDuration` after it. A gameplay effect here belongs to the real area (`!IsReplica`).
    protected virtual void OnEnd(PNAreaEndReason reason) { }
    // Seconds of its own clock the Ending lasts; read right after `OnEnd`, so `OnEnd` may decide. Zero or less: no Ending.
    public virtual float EndingDuration => 0f;
    // Every frame of the Ending, `delta` scaled by the area's clock.
    protected virtual void OnEnding(float delta) { }
    // Once, when the area is stopped by force (a CANCEL reason), from Running or from the Ending. `OnEnd` does not run for it.
    protected virtual void OnCancel(PNAreaEndReason reason) { }
    // Last, exactly once, after the End (and its Ending) or the Cancel: release what the area holds.
    protected abstract void Cleanup();
    public bool Finish(PNAreaEndReason reason = PNAreaEndReason.Requested) => Manager != null && Manager.End(Handle, reason);

    internal void Perform(float gameDelta) { if (life.IsRunning) OnPerform(gameDelta); }
    internal PNInstanceBegin BeginStop(PNAreaEndReason reason) => life.Begin((int)reason);
    internal void RunEnd(PNAreaEndReason reason) { OnEnd(reason); life.ArmEnding(EndingDuration); }
    // A failing OnCancel is reported; the Cleanup that follows still runs and decides whether the area is released.
    internal void RunCancel(PNAreaEndReason reason) { try { OnCancel(reason); } catch (Exception error) { Debug.LogException(error, this); } }
    // One step of the Ending; true when it is over and Cleanup is due.
    internal bool StepEnding(float delta) { if (!life.IsEnding) return false; OnEnding(delta); return life.AdvanceEnding(delta); }
    internal bool CleanupDue => life.CleanupPending;
    internal void RunCleanup()
    {
        if (!life.TakeCleanup() && !CleanupFailed) return;
        CleanupFailed = true; ReleaseTime();
        Cleanup(); CleanupFailed = false;
    }
    // A forced stop that finishes at once (shutdown, world teardown): the Cancel hook if it is not over yet, then Cleanup (also a retry after a failed one).
    internal void CancelNow(PNAreaEndReason reason)
    {
        if (BeginStop(reason) == PNInstanceBegin.RunCancel) RunCancel(reason);
        RunCleanup();
    }
    public virtual void OnPoolCreated() { }
    public virtual void OnPoolGet(EveEasyPooling pool) { }
    // Explicit destruction hook is required for retained resources in native Edit fixtures too.
    public virtual void DisposePooledResources() { }
    public virtual void OnPoolRelease()
    {
        ReleaseTime(); created = null;
        Manager = null;World=null; Definition = null; Handle = default; life.Release();
        if (CleanupFailed) throw new InvalidOperationException("Faulted area cleanup; existing pool must evict this instance.");
    }
    protected virtual void OnDisable() { if (life.IsRunning || life.IsEnding) Finish(PNAreaEndReason.Disabled); }
    protected virtual void OnDestroy() { if (life.IsRunning || life.IsEnding) Finish(PNAreaEndReason.Disabled); DisposePooledResources(); }
}

[DisallowMultipleComponent]
public sealed class PNAreaManager : MonoBehaviour, IPNHitReportReceiver
{
    private sealed class PoolEntry
    {
        internal GameObject Template; internal EveEasyPooling Pool; internal Scene Scene;
        internal readonly Dictionary<GameObject, PNAreaInstance> Instances = new Dictionary<GameObject, PNAreaInstance>();
    }
    // A trait of a real (authority) area as it was resolved at creation: the effective definition and the payload this area gave it. Null `Traits` = no trait or not the authority's area.
    private struct TraitRun { internal PNAreaTraitDefinition Definition; internal JToken Payload; }
    private sealed class Entry { internal TraitRun[] Traits; internal PNStat Owner; internal EveId Id, ProfileId; internal GameObject Root; internal PNAreaInstance Instance; internal PoolEntry Pool; internal Scene Scene; internal bool Replica;internal EveWorldContext World;internal IDisposable Lease; internal bool Guess, Adopted; internal double CreatedAt; internal (long, int) GuessKey; }
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
    private void CreateNow(PNAreaCreateRequest request, PNAreaReceipt receipt, long replicaId = 0, bool guess = false)
    {
        bool replica = replicaId != 0 || guess;
        GameObject root = null; PoolEntry pool = null; long id = 0;
        try
        {
            if (closing || !isActiveAndEnabled || Session == null || !Session.IsActive || !replica && !Session.HasAuthority || replica && (request == null || !request.IsReplica || replicaId != 0 && objectEntries.ContainsKey(replicaId) || guess && (request.CastKey <= 0 || predictions.ContainsKey((request.CastKey, request.CastIndex)) || RecordCopyExists((request.CastKey, request.CastIndex))))) { receipt.Status = PNAreaStatus.Rejected; return; }
            if (request == null || !replica && (request.Source == null || request.Source.SessionId != Session.SessionId) || !request.Scene.IsValid() || !request.Scene.isLoaded)
                throw new ArgumentException("Area requires valid source/session and loaded scene.");
            var p = request.Position;
            var world=PNWorldGameplay.ForScene(Session,request.Scene);
            if(world!=null&&!world.CanExecute||!replica&&(request.Owner!=null&&(!PNWorldGameplay.CanExecute(request.Owner)||!ReferenceEquals(request.Owner.GetComponent<EveEntityIdentity>()?.World,world))||request.Follow!=null&&(!PNWorldGameplay.CanExecute(request.Follow)||!ReferenceEquals(request.Follow.GetComponent<EveEntityIdentity>()?.World,world))))
                throw new InvalidOperationException("Area world/owner/follow is unavailable or crosses worlds.");
            if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z)) throw new ArgumentException("Finite area position required.");
            var definition = Session.Registry.Instances().GetEffectiveKind<PNAreaDefinition>(request.AreaId);
            definition.ValidateInput(request.Input);
            // The traits of a real area are resolved before anything is borrowed: an unknown trait fails the creation (a copy or a guess never runs traits).
            TraitRun[] traitRuns = null;
            if (!replica && definition.Traits.Count != 0)
            {
                var traitRegistry = Session.Registry.AreaTraits(); traitRuns = new TraitRun[definition.Traits.Count];
                for (int i = 0; i < traitRuns.Length; i++) traitRuns[i] = new TraitRun { Definition = traitRegistry.GetEffective(definition.Traits[i].Id), Payload = definition.Traits[i].Payload };
            }
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
            entries.Add(id, new Entry { Traits = traitRuns, Owner = request.Owner, Id =Session.Registry.Instances().ResolveIdentity(request.AreaId), ProfileId = definition.Id, Root = root, Instance = instance, Pool = pool, Scene = request.Scene,World=world,Lease=world?.AcquireLease("area") });
            instance.Bind(this, handle, definition, request);PNWorldPresentationRegistration.Register(world,root);root.SetActive(true);
            if (!instance.isActiveAndEnabled) throw new InvalidOperationException("Selected area component must be active after initialization.");
            // Immediate admission occurs after activation, when owned clock effects may be acquired.
            instance.Perform(0);
            PNWorldPresentationRegistration.Register(world,root);
            if (!instance.IsRunning) throw new InvalidOperationException("Area ended during initialization.");
            receipt.Handle = handle; receipt.Status = PNAreaStatus.Created;
            if (guess) { var made = entries[id]; made.Replica = true; made.Guess = true; made.CreatedAt = PredictionNow; made.GuessKey = (request.CastKey, request.CastIndex); predictions[made.GuessKey] = id; }
            else if (replica) { entries[id].Replica = true; objectEntries[replicaId] = id; objectIds[id] = replicaId; }
            else { RecordSpawn(id, entries[id], request); RunTraitsCreated(id, entries[id]); }
        }
        catch (Exception error)
        {
            if (id != 0 && entries.TryGetValue(id, out var entry))
            {
                Stop(id, entry, PNAreaEndReason.RuntimeError);
                if (entry.Instance.CleanupFailed) { receipt.Error = error; receipt.Status = PNAreaStatus.Failed; return; }
                entries.Remove(id);entry.Lease?.Dispose();
            }
            if (root != null) { try { pool.Pool.Release(root); } catch (Exception cleanup) { Debug.LogException(cleanup, this); } }
            receipt.Error = error; receipt.Status = PNAreaStatus.Failed;
        }
    }
    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public bool End(PNAreaHandle handle, PNAreaEndReason reason = PNAreaEndReason.Requested)
    {
        if (!Enum.IsDefined(typeof(PNAreaEndReason), reason)) throw new ArgumentException("Unknown area end reason.");
        // A running area takes any reason; one in its Ending takes only a forced stop (a second real finish is dropped); a finished one takes nothing.
        if (!ReferenceEquals(handle.Manager, this) || !entries.TryGetValue(handle.InstanceId, out var probe) || probe.Instance == null || probe.Instance.IsDone
            || probe.Instance.IsEnding && PNAreaInstance.IsEndReason(reason)) return false;
        Submit(() => {
            if (!entries.TryGetValue(handle.InstanceId, out var entry)) return;
            if (!Stop(handle.InstanceId, entry, reason)) return;
            // Unity may be destroying the pool storage during scene-unload OnDisable.
            // Gameplay cleanup is immediate; return after sceneUnloaded or next manager update.
            if (entry.Instance.IsDone && reason != PNAreaEndReason.Disabled) Return(handle.InstanceId, entry);
        }); return true;
    }
    // ---- area traits (Docs/InstanceLifecyclePlan.md 3.4): hooks run for real (authority) areas only, in declaration order, inside the manager's queue ----
    private PNAreaTraitContext TraitContext(long id, Entry entry, in TraitRun run)
        => new PNAreaTraitContext(this, new PNAreaHandle(this, id), entry.Id, run.Definition.Id, entry.World, entry.Instance != null ? entry.Instance.transform.position : default, entry.Owner, run.Payload);
    // After the area was made and recorded. A fault in a trait is reported and does not undo the area or stop the next trait.
    private void RunTraitsCreated(long id, Entry entry)
    {
        if (entry.Traits == null) return;
        foreach (var run in entry.Traits)
        {
            try { run.Definition.Behavior.OnCreated(TraitContext(id, entry, run)); } catch (Exception error) { Debug.LogException(error, this); }
        }
    }
    // After the area's own OnEnd, for a real finish only.
    private void RunTraitsEnded(long id, Entry entry, PNAreaEndReason reason)
    {
        if (entry.Traits == null || entry.Replica) return;
        foreach (var run in entry.Traits)
        {
            try { run.Definition.Behavior.OnEnded(TraitContext(id, entry, run), reason); } catch (Exception error) { Debug.LogException(error, this); }
        }
    }
    // The running real areas of `world` that carry the trait (an area in its Ending is not running).
    internal int CollectWithTrait(EveWorldContext world, EveId traitId, List<PNAreaHandle> into)
    {
        int count = 0;
        foreach (var pair in entries)
        {
            var entry = pair.Value;
            if (entry.Traits == null || entry.Replica || !ReferenceEquals(entry.World, world) || entry.Instance == null || !entry.Instance.IsRunning) continue;
            foreach (var run in entry.Traits)
                if (run.Definition.Id == traitId) { into.Add(new PNAreaHandle(this, pair.Key)); count++; break; }
        }
        return count;
    }
    // Gives one reason to an area: an END starts its Ending (`OnEnd`; a fault there becomes a RuntimeError cancel), a CANCEL stops it now (`OnCancel`); the Cleanup follows when due.
    // The sync End record carries the reason that really took effect. Returns false when nothing happened (already finished, a repeat). `strict`: a failing Cleanup is thrown, not logged.
    private bool Stop(long id, Entry entry, PNAreaEndReason reason, bool strict = false)
    {
        var instance = entry.Instance;
        if (ReferenceEquals(instance, null)) { NotifyEnded(id, entry, reason); return false; }
        var begin = instance.BeginStop(reason);
        if (begin == PNInstanceBegin.Ignored && !instance.CleanupFailed) return false;
        if (begin == PNInstanceBegin.RunEnd)
        {
            try { instance.RunEnd(reason); RunTraitsEnded(id, entry, reason); }
            catch (Exception error)
            {
                Debug.LogException(error, this);
                reason = PNAreaEndReason.RuntimeError; begin = instance.BeginStop(reason);
            }
        }
        if (begin == PNInstanceBegin.RunCancel) instance.RunCancel(reason);
        Exception cleanupError = null;
        if (instance.CleanupDue || instance.CleanupFailed)
        {
            try { instance.RunCleanup(); } catch (Exception error) { instance.CleanupFailed = true; cleanupError = error; }
        }
        if (begin != PNInstanceBegin.Ignored)
        {
            try { NotifyEnded(id, entry, reason); } catch (Exception error) { Debug.LogException(error, this); }
        }
        if (cleanupError != null)
        {
            if (strict) throw cleanupError;
            Debug.LogException(cleanupError, this);
        }
        return begin != PNInstanceBegin.Ignored;
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
        if (objectSync != null) { if (objectSync.IsAuthority) objectSync.Flush(); else objectSync.Tick(); }
        Submit(() => {
            foreach (var pair in entries.ToArray())
            {
                var entry = pair.Value;
                try
                {
                    if (entry.Instance == null) { Return(pair.Key, entry); continue; }
                    if(entry.World!=null&&!entry.World.CanExecute)continue;
                    var instance = entry.Instance;
                    if ((instance.IsRunning || instance.IsEnding) && !instance.isActiveAndEnabled) { End(new PNAreaHandle(this, pair.Key), PNAreaEndReason.Disabled); continue; }
                    if (entry.Guess && !entry.Adopted && instance.IsRunning && PredictionNow - entry.CreatedAt > OrphanSeconds) { End(new PNAreaHandle(this, pair.Key), PNAreaEndReason.Requested); continue; }
                    if (instance.IsRunning) instance.Perform(instance.ScaleDelta(gameDelta));
                    else if (instance.IsEnding)
                    {
                        // The Ending runs on the area's own clock; when it is over the Cleanup runs (once) and the area goes back to the pool.
                        if (instance.StepEnding(instance.ScaleDelta(gameDelta))) { instance.RunCleanup(); Return(pair.Key, entry); }
                    }
                    else Return(pair.Key, entry);
                }
                catch (Exception error) { Debug.LogException(error, this); End(new PNAreaHandle(this, pair.Key), PNAreaEndReason.RuntimeError); }
            }
        });
    }
    // ---- spawned-object sync (stage O2: the authority records what it creates; the view-only copy of an area/blast is built in O5) ----------------------------------------
    private PNObjectSyncService objectSync;
    private readonly Dictionary<long, long> objectIds = new Dictionary<long, long>();
    // EN: The spawned-object sync of this play. On the authority every area made here is recorded (Spawn at creation, End when it stops). Null = no sync.
    // VI: Dich vu dong bo vat the cua phien. O authority moi area tao ra deu duoc ghi (Spawn luc tao, End luc dung).
    public PNObjectSyncService ObjectSync
    {
        get => objectSync;
        set
        {
            if (ReferenceEquals(objectSync, value)) return;
            if (objectSync != null) { objectSync.SetSink(PNObjectKind.Area, null); objectSync.SetSource(PNObjectKind.Area, null); }
            objectSync = value;
            if (value != null) { bridge = bridge ?? new ObjectBridge(this); value.SetSink(PNObjectKind.Area, bridge); value.SetSource(PNObjectKind.Area, bridge); }
        }
    }
    private ObjectBridge bridge;
    // ---- hit claims (plan 4.4c V3, way B): who stands inside a field is decided by the machine of a player on another machine than the authority ----
    private PNHurtClaimBook claimBook = new PNHurtClaimBook();
    public PNHurtClaimBook ClaimBook { get => claimBook; set => claimBook = value ?? new PNHurtClaimBook(); }
    // The sync id of an area of this manager (a copy's or the authority's); 0 when it has none.
    public long ObjectIdOf(PNAreaInstance instance) => instance != null && objectIds.TryGetValue(instance.Handle.InstanceId, out var id) ? id : 0L;
    // EN: A claim from the machine of `target`: "I am inside / outside this area" (`inside`). Taken when the policy says his machine decides and the real area runs; the area keeps his presence until it is withdrawn or the policy gives him back.
    // VI: Lenh bao tu may cua `target`: "toi dang o trong / ngoai vung nay". Nhan khi chinh sach cho may do quyet va vung that dang chay; vung giu trang thai cho toi khi rut lai hoac chinh sach tra nguoi choi ve.
    public bool AcceptHitReport(in PNHitReport report, PNStat victim, float tolerance)
        => report.Role == PNHitReportRole.Taken && !report.ByCast && report.Kind == PNObjectKind.Area && AcceptTaken(report.ObjectId, victim, tolerance, report.Detail != 0);
    private bool AcceptTaken(long objectId, PNStat target, float tolerance, bool inside)
    {
        if (objectId <= 0 || target == null || Session == null || !Session.HasAuthority) return false;
        var policy = claimBook.Policy;
        if (policy == null || !policy.ClaimsDecide(target)) return claimBook.Refuse("policy");
        long entryId = 0; foreach (var pair in objectIds) if (pair.Value == objectId) { entryId = pair.Key; break; }
        if (entryId == 0 || !entries.TryGetValue(entryId, out var entry) || entry.Replica || entry.Instance == null || !entry.Instance.IsRunning) return claimBook.Refuse("gone");
        if (!entry.Instance.TakeClaimedPresence(target, inside, tolerance)) return claimBook.Refuse("rule");
        claimBook.NoteAccepted(); return true;
    }
    private readonly Dictionary<long, long> objectEntries = new Dictionary<long, long>();
    // ---- the shooter's guess (plan O5 step 4): the player's own machine draws the area when it casts, named by (cast key, index); the authority's Spawn record with that name takes it over instead of making a second one ----
    // A guess nobody takes over within OrphanSeconds (the authority refused the cast) is removed. A guess draws only: no Buff, no clock effect.
    public const double OrphanSeconds = 1d;
    private readonly Dictionary<(long, int), long> predictions = new Dictionary<(long, int), long>();
    public Func<double> PredictionClock { get; set; }
    private double PredictionNow => PredictionClock != null ? PredictionClock() : Time.unscaledTimeAsDouble;
    public int Adoptions { get; private set; }
    public int AdoptScaleFixes { get; private set; }
    public int GuessCount { get { int count = 0; foreach (var entry in entries.Values) if (entry.Guess && !entry.Adopted && entry.Instance != null && entry.Instance.IsRunning) count++; return count; } }
    // EN: Draws the shooter's guess of an area. `request` is a view-only request (PNAreaCreateRequest.Replica) with the cast key; nothing is recorded or sent.
    // VI: Ve ban doan cua nguoi ban. request la yeu cau chi-de-xem (Replica) co cast key; khong ghi, khong gui.
    public PNAreaReceipt CreateGuess(PNAreaCreateRequest request)
    {
        if (request == null || !request.IsReplica || request.CastKey <= 0) throw new ArgumentException("A guess needs a view-only request with a cast key.", nameof(request));
        var receipt = new PNAreaReceipt();
        Submit(() => CreateNow(request, receipt, 0, true)); return receipt;
    }
    // The cast was refused (or taken back): the areas it guessed go now, not after OrphanSeconds. An area the authority's record already took over is the real one's copy and stays.
    public void DiscardPredicted(long castKey)
    {
        if (castKey <= 0) return;
        foreach (var pair in predictions.ToArray())
            if (pair.Key.Item1 == castKey && entries.TryGetValue(pair.Value, out var entry) && entry.Guess && !entry.Adopted) End(new PNAreaHandle(this, pair.Value));
    }
    // EN: The record can reach a machine before its own player's predicted cast does (latency under a frame); the copy made from it is then the one to show, so the late guess is refused instead of drawing a second one.
    // VI: Ban ghi co the toi may truoc khi cu danh du doan cua chinh nguoi choi xong (tre it hon mot khung); ban sao tu ban ghi la cai de hien, nen ban doan toi sau bi tu choi thay vi ve them mot cai.
    private readonly Dictionary<(long, int), double> recordCasts = new Dictionary<(long, int), double>();
    // A guess is late for a record made within this many seconds (the copy may even have ended by now: it is not drawn again).
    private const double RecordCastWindow = 3d;
    private bool RecordCopyExists((long, int) key) => recordCasts.TryGetValue(key, out var at) && PredictionNow - at <= RecordCastWindow;
    private void RememberRecordCast(PNObjectRecord record, PNAreaReceipt receipt)
    {
        if (receipt.Status != PNAreaStatus.Created || string.IsNullOrEmpty(record.CastKey) || !long.TryParse(record.CastKey, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var castKey) || castKey <= 0) return;
        if (!entries.TryGetValue(receipt.Handle.InstanceId, out var entry) || entry.Guess) return;
        if (recordCasts.Count >= 64) { var old = new List<(long, int)>(); foreach (var pair in recordCasts) if (PredictionNow - pair.Value > RecordCastWindow) old.Add(pair.Key); foreach (var k in old) recordCasts.Remove(k); }
        recordCasts[(castKey, record.CastIndex)] = PredictionNow;
    }
    private bool TryAdopt(PNObjectRecord record)
    {
        if (string.IsNullOrEmpty(record.CastKey) || !long.TryParse(record.CastKey, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var castKey)) return false;
        var key = (castKey, record.CastIndex);
        if (!predictions.TryGetValue(key, out var handleId) || !entries.TryGetValue(handleId, out var entry) || entry.Instance == null || !entry.Instance.IsRunning || entry.Adopted) return false;
        entry.Adopted = true; predictions.Remove(key); objectEntries[record.Id] = handleId; objectIds[handleId] = record.Id; Adoptions++;
        if (!double.IsNaN(record.Scale) && !double.IsInfinity(record.Scale) && record.Scale > 0d && Math.Abs(record.Scale - entry.Instance.SyncScale) > 1e-3 * Math.Max(1d, record.Scale)) { entry.Instance.CorrectScale(record.Scale); AdoptScaleFixes++; }
        return true;
    }
    public int ReplicaCount { get { int count = 0; foreach (var entry in entries.Values) if (entry.Replica) count++; return count; } }
    // EN: Builds a view-only copy of an area from a Spawn record and brings it to `elapsed` seconds of its own life. It applies no Buff and no clock effect and ends when told to or at its own end.
    // VI: Dung ban hinh cua mot vung tu ban ghi Spawn va dua no toi `elapsed` giay cua doi no. Khong ap Buff / dong ho.
    public PNAreaReceipt CreateReplica(PNObjectRecord record, double elapsed)
    {
        if (record == null || record.Type != PNObjectRecordType.Spawn || record.Kind != PNObjectKind.Area) throw new ArgumentException("An area Spawn record is required.", nameof(record));
        var receipt = new PNAreaReceipt();
        Submit(() => CreateReplicaNow(record, elapsed, receipt)); return receipt;
    }
    private void CreateReplicaNow(PNObjectRecord record, double elapsed, PNAreaReceipt receipt)
    {
        try
        {
            if (closing || Session == null || !Session.IsActive || objectEntries.ContainsKey(record.Id)) { receipt.Status = PNAreaStatus.Rejected; return; }
            if (TryAdopt(record)) { receipt.Status = PNAreaStatus.Created; return; }
            EveWorldContext world = null;
            foreach (var candidate in Session.Worlds.Snapshot()) if (candidate.InstanceId == record.World.Instance && candidate.Incarnation == record.World.Incarnation) { world = candidate; break; }
            if (world == null || !world.CanExecute || !world.PhysicsOwner.IsValid()) { receipt.Status = PNAreaStatus.Rejected; return; }
            JToken input = record.Input == null ? null : JToken.Parse(record.Input);
            PNStat owner = null, follow = null;
            if (record.Owner != 0 && Session.Entities.TryGet(new EveEntityId(record.Owner), out var ownerLoader) && ownerLoader != null) owner = ownerLoader.GetComponent<PNStat>();
            if (record.Follow != 0 && Session.Entities.TryGet(new EveEntityId(record.Follow), out var followLoader) && followLoader != null) follow = followLoader.GetComponent<PNStat>();
            var request = PNAreaCreateRequest.Replica(record.DefinitionId, input, world.PhysicsOwner, new Vector3(record.Position.X, record.Position.Y, record.Position.Z), owner, follow, record.Scale);
            CreateNow(request, receipt, record.Id);
            RememberRecordCast(record, receipt);
            if (receipt.Status == PNAreaStatus.Created && entries.TryGetValue(receipt.Handle.InstanceId, out var made) && made.Instance != null && elapsed > 1e-4d)
                made.Instance.Perform((float)Math.Min(elapsed, PNObjectSyncLimits.MaxReplicaSeconds));
        }
        catch (Exception error) { receipt.Error = error; receipt.Status = PNAreaStatus.Failed; }
    }
    private sealed class ObjectBridge : IPNObjectSyncSink, IPNObjectSyncSource
    {
        private readonly PNAreaManager owner;
        public ObjectBridge(PNAreaManager manager) { owner = manager; }
        public bool Spawned(PNObjectRecord spawn, double elapsed) { var receipt = owner.CreateReplica(spawn, elapsed); return receipt.Status == PNAreaStatus.Created || receipt.Status == PNAreaStatus.Pending; }
        public void Retargeted(PNObjectRecord retarget) { }
        public void Ended(PNObjectRecord end)
        {
            if (!owner.objectEntries.TryGetValue(end.Id, out var handleId)) return;
            var reason = Enum.IsDefined(typeof(PNAreaEndReason), (int)end.Reason) ? (PNAreaEndReason)end.Reason : PNAreaEndReason.Requested;
            owner.End(new PNAreaHandle(owner, handleId), reason);
        }
        public bool TryCapture(long id, out PNObjectVector position, out PNObjectVector direction, out long target, out float age)
        {
            position = default; direction = new PNObjectVector(0, 0, 1); target = 0; age = 0f;
            foreach (var pair in owner.entries)
            {
                if (pair.Value.Instance == null || !pair.Value.Instance.IsRunning || !owner.objectIds.TryGetValue(pair.Key, out var objectId) || objectId != id) continue;
                var at = pair.Value.Instance.transform.position; position = new PNObjectVector(at.x, at.y, at.z);
                age = Mathf.Min(pair.Value.Instance.ProgressSeconds, (float)PNObjectSyncLimits.MaxReplicaSeconds); return true;
            }
            return false;
        }
    }
    private void RecordSpawn(long id, Entry entry, PNAreaCreateRequest request)
    {
        if (objectSync == null || !objectSync.IsAuthority || entry.World == null) return;
        try
        {
            var json = request.Input; string input = json == null || json.Type == JTokenType.Null ? null : json.ToString(Formatting.None);
            if (input != null && input.Length > PNObjectSyncLimits.InputChars) { objectSync.NoteRefused(); return; }
            var at = request.Position; long objectId = objectSync.NextId();
            var record = PNObjectRecord.Spawn(PNObjectKind.Area, objectId, PNObjectWorld.Of(entry.World), entry.Id, input, new PNObjectVector(at.x, at.y, at.z), new PNObjectVector(0, 0, 1), 0,
                request.Owner != null ? request.Owner.EntityId.Value : 0L, request.Follow != null ? request.Follow.EntityId.Value : 0L, request.CastKey > 0 ? request.CastKey.ToString(System.Globalization.CultureInfo.InvariantCulture) : null, request.CastIndex, 1d, entry.Instance.SyncScale, 1d, objectSync.Now, 0f, 0f);
            objectIds[id] = objectId;
            if (!objectSync.RecordSpawn(record)) objectIds.Remove(id);
        }
        catch (FormatException) { objectSync.NoteRefused(); }
    }
    private void NotifyEnded(long id, Entry entry, PNAreaEndReason reason)
    {
        if (objectSync == null || !objectIds.TryGetValue(id, out var objectId)) return;
        objectIds.Remove(id);
        if (entry.Replica) { objectEntries.Remove(objectId); objectSync.ReplicaEnded(objectId); return; }
        var at = entry.Instance != null ? entry.Instance.transform.position : default;
        try { objectSync.RecordEnd(PNObjectRecord.End(PNObjectKind.Area, objectId, PNObjectWorld.Of(entry.World), (byte)reason, new PNObjectVector(at.x, at.y, at.z))); }
        catch (FormatException) { objectSync.NoteRefused(); }
    }
    private void Return(long id, Entry entry)
    {
        // A failed gameplay cleanup keeps its owner; pool eviction is not a cleanup retry.
        if (!ReferenceEquals(entry.Instance, null) && entry.Instance.CleanupFailed) return;
        entries.Remove(id);entry.Lease?.Dispose();
        if (entry.Guess && predictions.TryGetValue(entry.GuessKey, out var guessId) && guessId == id) predictions.Remove(entry.GuessKey);
        if (entry.Instance == null && !ReferenceEquals(entry.Instance, null)) entry.Instance.CleanupFailed = true;
        try { entry.Pool.Pool.Release(entry.Root); } catch (Exception error) { Debug.LogException(error, this); }
    }
    private void SceneUnloaded(Scene scene)
    {
        Submit(() => {
            foreach (var pair in entries.Where(x => x.Value.Scene == scene).ToArray()) { Stop(pair.Key, pair.Value, PNAreaEndReason.SceneUnloaded); Return(pair.Key, pair.Value); }
            // Storage is scene-local: rebuild idle pools after any scene teardown.
            foreach (var pair in pools.Where(x => x.Value.Scene == scene).ToArray())
            {
                if (entries.Values.Any(x => ReferenceEquals(x.Pool, pair.Value))) continue;
                pair.Value.Pool.Dispose(); DestroyOwned(pair.Value.Template); pools.Remove(pair.Key);
            }
        });
    }
    public void ActorWorldChanging(PNStat actor)
    {
        if(actor==null)return;
        if(busy)throw new InvalidOperationException("Area mutations must drain before actor transfer.");
        foreach(var entry in entries.Values.ToArray())if(entry.Instance!=null&&entry.Instance.IsRunning)entry.Instance.ActorWorldChanging(actor);
    }
    public void ReleaseWorld(EveWorldContext world)
    {
        if(busy)throw new InvalidOperationException("Area mutation must drain before world teardown.");
        foreach(var pair in entries.Where(x=>ReferenceEquals(x.Value.World,world)).ToArray())
        { Stop(pair.Key,pair.Value,PNAreaEndReason.SceneUnloaded,true);Return(pair.Key,pair.Value); }
        foreach(var pair in pools.Where(x=>PNWorldGameplay.ForScene(Session,x.Value.Scene)==world).ToArray())
        {
            if(entries.Values.Any(x=>ReferenceEquals(x.Pool,pair.Value)))throw new InvalidOperationException("World area cleanup remains pending.");
            foreach(var instance in pair.Value.Instances.Values)instance.DisposePooledResources();
            pair.Value.Pool.Dispose();DestroyOwned(pair.Value.Template);pools.Remove(pair.Key);
        }
    }
    public void Shutdown()
    {
        closing = true; SceneManager.sceneUnloaded -= SceneUnloaded; objectEntries.Clear(); objectIds.Clear(); predictions.Clear(); recordCasts.Clear();
        var errors = new List<Exception>();
        foreach (var pair in entries.ToArray())
        {
            try
            {
                if (!ReferenceEquals(pair.Value.Instance, null)) pair.Value.Instance.CancelNow(PNAreaEndReason.SessionEnded);
                entries.Remove(pair.Key);pair.Value.Lease?.Dispose();
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
