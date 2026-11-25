#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum PNBlastStatus { Pending, Created, Rejected, Failed }
public enum PNBlastEndReason { Requested, Expired, OwnerUnavailable, Disabled, SceneUnloaded, SessionEnded, RuntimeError }
public readonly struct PNBlastHandle
{
    internal readonly PNBlastManager Manager;
    public long InstanceId { get; }
    internal PNBlastHandle(PNBlastManager manager, long id) { Manager = manager; InstanceId = id; }
    public bool IsAlive => Manager != null && Manager.Contains(this);
    public bool End() => Manager != null && Manager.End(this);
}
public sealed class PNBlastReceipt
{
    public PNBlastStatus Status { get; internal set; } = PNBlastStatus.Pending;
    public PNBlastHandle Handle { get; internal set; }
    public Exception Error { get; internal set; }
}
public sealed class PNBlastSnapshot
{
    public PNBlastHandle Handle { get; }
    public EveId Id { get; }
    public EveId ProfileId { get; }
    public bool IsRunning { get; }
    public PNBlastEndReason? EndReason { get; }
    public Vector3 Position { get; }
    internal PNBlastSnapshot(PNBlastHandle handle, EveId id, EveId profileId, PNBlastInstance instance)
    { Handle = handle; Id = id; ProfileId = profileId; IsRunning = instance.IsRunning; EndReason = instance.EndReason; Position = instance.transform.position; }
}
public sealed class PNBlastCreateRequest
{
    private readonly JToken input;
    public EveId BlastId { get; }
    public JToken Input => input?.DeepClone();
    public PNCombatSource Source { get; }
    public PNStat Owner { get; }
    public PNBlastDependency Dependency { get; }
    public Vector3 Position { get; }
    public Scene Scene { get; }
    // EN: A view-only copy on a machine without authority (plan O5): no combat source and no damage; `owner` (when this machine has it) is only for what is drawn; `scale` is the creator's blast_scale the record carries.
    // VI: Ban hinh chi de xem o may khong co authority: khong co nguon combat, khong sat thuong; owner (neu may nay co) chi de ve; scale la blast_scale cua nguoi tao ma ban ghi mang.
    public bool IsReplica { get; private set; }
    public double ReplicaScale { get; private set; } = 1d;
    // EN: The cast that made the blast (0 = none) and which blast of that cast (the Action ordinal). The authority writes it into the record; a shooter's guess is named by it, so the record can take the guess over.
    // VI: Cast tao ra blast (0 = khong) va la blast thu may cua cast (so thu tu Action). Authority ghi vao ban ghi; ban doan cua nguoi ban mang ten do de ban ghi nhan lai.
    public long CastKey { get; private set; }
    public int CastIndex { get; private set; }
    // The layers the real blast can hurt (the Spawn record's TargetMask): a copy needs them to know whether it can hurt the player of this machine (plan 4.4c).
    public int ReplicaTargets { get; private set; }
    public PNBlastCreateRequest WithReplicaTargets(int mask) { ReplicaTargets = mask > 0 ? mask : 0; return this; }
    public PNBlastCreateRequest WithCast(long castKey, int castIndex) { CastKey = castKey > 0 ? castKey : 0; CastIndex = Math.Max(0, Math.Min(255, castIndex)); return this; }
    public static PNBlastCreateRequest Replica(EveId id, JToken input, Scene scene, Vector3 position, PNStat owner, double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale < 0d) throw new ArgumentException("A blast copy needs a finite scale.", nameof(scale));
        return new PNBlastCreateRequest(id, input, scene, position, owner, scale);
    }
    private PNBlastCreateRequest(EveId id, JToken input, Scene scene, Vector3 position, PNStat owner, double scale)
    { BlastId = id; this.input = input?.DeepClone(); Scene = scene; Position = position; Owner = owner; Dependency = PNBlastDependency.Independent; IsReplica = true; ReplicaScale = scale; }
    public PNBlastCreateRequest(EveId id, JToken input, PNCombatSource source, Scene scene, Vector3 position, PNStat owner = null, PNBlastDependency dependency = PNBlastDependency.Independent)
    { BlastId = id; this.input = input?.DeepClone(); Source = source; Scene = scene; Position = position; Owner = owner; Dependency = dependency; if (!Enum.IsDefined(typeof(PNBlastDependency), dependency) || dependency == PNBlastDependency.Owner && owner == null) throw new ArgumentException("An owner-dependent blast needs its owner."); }
}

// Component instances are reused by the existing GameObject pool; manager owns every borrow.
public abstract class PNBlastInstance : MonoBehaviour, EveIPoolable, IPNTimeTarget
{
    // EN: Every blast carries a clock (PNBlastClock, minScale 0). A blast that is affected by time (the default) registers in the world's PNTimeTargets while it
    //     runs, so a time effect can slow or stop it; the manager then hands `Advance` the delta scaled by that clock. An implementation that must not be
    //     affected (the time-scale blast itself, so two overlapping zones cannot lengthen each other) overrides this to false. The Ending runs on the same clock.
    // VI: Mỗi vùng có đồng hồ (PNBlastClock, minScale 0). Vùng chịu thời gian (mặc định) đăng ký vào PNTimeTargets của world khi chạy; manager đưa OnPerform
    //     delta đã nhân theo đồng hồ đó. Implementation không muốn chịu (chính vùng thời gian) override false. Pha Ending chạy trên cùng đồng hồ.
    public virtual bool AffectedByTime => true;
    public PNBlastClock Clock { get; private set; }
    EveTimeScale IPNTimeTarget.TimeClock => Clock;
    int IPNTimeTarget.TimeLayer => PNLayers.Blast;
    Vector3 IPNTimeTarget.TimePosition => transform.position;
    bool IPNTimeTarget.TimeActive => (life.IsRunning || life.IsEnding) && Clock != null;
    private PNTimeTargets.Entry timeEntry;
    private PNBlastCreateRequest created; private double scaleFactor = double.NaN;
    // The creator's `blast_scale` (Scale route over a base of 1, read from the source snapshot at creation; 1 = unchanged). What it scales is the implementation's choice.
    // The creator's blast_scale as the sync record carries it.
    public double SyncScale => ScaleFactor;
    // A guess made with another blast_scale than the authority's record carries takes the record's; an implementation that cached its shape overrides OnScaleCorrected.
    internal void CorrectScale(double scale) { scaleFactor = scale; OnScaleCorrected(scale); }
    protected virtual void OnScaleCorrected(double scale) { }
    protected double ScaleFactor
    {
        get
        {
            if (double.IsNaN(scaleFactor)) scaleFactor = created != null && created.IsReplica ? created.ReplicaScale : created?.Source?.Snapshot != null ? created.Source.Snapshot.Calculate(1d, PNContentIds.BlastScale) : 1d;
            return scaleFactor;
        }
    }
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
    public PNBlastHandle Handle { get; private set; }
    // A view-only copy of the authority's blast: draws, deals nothing, ends when the authority says so.
    public bool IsReplica => created != null && created.IsReplica;
    // Seconds of its own life the blast has lived (what a late observer's copy is brought to); 0 when the implementation does not say.
    public virtual float ProgressSeconds => 0f;
    public PNBlastDependency Dependency => created != null ? created.Dependency : PNBlastDependency.Independent;
    protected PNStat Owner => created?.Owner;
    private EveEntityId ownerId;
    // True while an Owner-dependent blast's owner is alive, enabled and still in the blast's world (always true for the other dependencies).
    internal bool DependencyHolds()
    {
        if (Dependency != PNBlastDependency.Owner) return true;
        var o = created.Owner;
        return o != null && o.IsReady && !o.IsDead && o.isActiveAndEnabled && o.EntityId == ownerId && ReferenceEquals(o.GetComponent<EveEntityIdentity>()?.World, World);
    }
    // The combat capture of this blast: the creator's snapshot, `blast_scale` read once at creation, `blast_damage` added to the damage tags (D7).
    protected PNHitCapture CreateCapture(System.Collections.Generic.IEnumerable<EveId> attackTags)
        => new PNHitCapture(created.Source.Snapshot ?? throw new InvalidOperationException("The blast needs a combat snapshot."), ScaleFactor,
            (attackTags ?? Array.Empty<EveId>()).Concat(new[] { PNContentIds.BlastDamage }));
    // The layers a copy may hurt (0 on the authority and for a guess); see PNBlastCreateRequest.ReplicaTargets.
    protected int ReplicaTargetMask => created != null ? created.ReplicaTargets : 0;
    // EN: Running = alive; Ending = it really finished and only plays its own end (not running, handle not alive, not captured for a late joiner). `EndReason` is the first reason that took effect.
    // VI: Running = con song; Ending = da ket thuc that, chi choi phan ket thuc cua no (khong chay, handle khong song, khong vao baseline).
    public bool IsRunning => life.IsRunning;
    public bool IsEnding => life.IsEnding;
    internal bool IsDone => life.Phase == PNInstancePhase.Done;
    public EveWorldContext World { get; private set; }
    public PNBlastEndReason? EndReason => life.Reason.HasValue ? (PNBlastEndReason?)life.Reason.Value : null;
    internal bool CleanupFailed { get; set; }
    protected PNBlastManager Manager { get; private set; }
    protected PNBlastDefinition Definition { get; private set; }
    public virtual void ActorWorldChanging(PNStat actor) { if (Dependency == PNBlastDependency.Owner && ReferenceEquals(created?.Owner, actor)) Finish(PNBlastEndReason.OwnerUnavailable); }
    // Requested, Expired and OwnerUnavailable are END reasons (a real finish: `OnEnd`, then the Ending); Disabled, SceneUnloaded, SessionEnded and RuntimeError are CANCEL (forced: `OnCancel`, no Ending).
    private static readonly PNInstanceReasonTable Reasons = new PNInstanceReasonTable(7, (int)PNBlastEndReason.Requested, (int)PNBlastEndReason.Expired, (int)PNBlastEndReason.OwnerUnavailable);
    internal static bool IsEndReason(PNBlastEndReason reason) => Reasons.IsEnd((int)reason);
    private readonly PNInstanceLifecycle life = new PNInstanceLifecycle(Reasons);
    internal void Bind(PNBlastManager manager, PNBlastHandle handle, PNBlastDefinition definition, PNBlastCreateRequest request)
    { Manager = manager; World=PNWorldGameplay.ForScene(manager.Session,request.Scene);Handle = handle; Definition = definition; CleanupFailed = false; life.Start();
        strikes = 0; HitCount = 0; Seeking = false; struck.Clear();
        Clock = GetComponent<PNBlastClock>(); if (Clock != null) Clock.ClearModifiers(); created = request; scaleFactor = double.NaN; ownerId = request.Owner != null ? request.Owner.EntityId : default;
        OnStart(request);
        if (AffectedByTime && Clock != null) timeEntry = PNTimeTargets.For(World).Register(this);
    }

    // ---- the hooks of a blast (Docs/InstanceLifecyclePlan.md 3.1): Start -> Perform -> End -> Ending -> Cleanup, or Cancel -> Cleanup ----
    // Once when the blast is bound, on every machine (the real blast, a guess, a copy).
    protected abstract void OnStart(PNBlastCreateRequest request);
    // Every frame while Running, `gameDelta` already scaled by the blast's clock. The author decides when to `Strike`.
    protected abstract void OnPerform(float gameDelta);
    // Once, when the blast really finishes (an END reason). Read `EndingDuration` after it. A `Strike` here is allowed (an aftershock).
    protected virtual void OnEnd(PNBlastEndReason reason) { }
    // Seconds of its own clock the Ending lasts; read right after `OnEnd`. Zero or less: no Ending.
    public virtual float EndingDuration => 0f;
    // Every frame of the Ending, `delta` scaled by the blast's clock.
    protected virtual void OnEnding(float delta) { }
    // Once, when the blast is stopped by force (a CANCEL reason). `OnEnd` does not run for it, and a `Strike` after it does nothing.
    protected virtual void OnCancel(PNBlastEndReason reason) { }
    // Last, exactly once, after the End (and its Ending) or the Cancel: release what the blast holds.
    protected abstract void Cleanup();
    public bool Finish(PNBlastEndReason reason = PNBlastEndReason.Requested) => Manager != null && Manager.End(Handle, reason);

    // ---- the Strike door (Docs/InstanceLifecyclePlan.md 3.5): the author decides when damage happens, any number of times; the engine does the rest by the role of the machine ----
    private int strikes; private readonly HashSet<EveEntityId> struck = new HashSet<EveEntityId>();
    // The strikes this blast has made so far; the same on every machine because the same logic runs on the same clock.
    public int StrikeCount => strikes;
    // How many targets the real blast hurt directly, over all its strikes (a target claimed by its own machine is not counted here).
    public int HitCount { get; private set; }
    // True while a copy built late is being brought to its age: the strikes that already passed are counted but not claimed.
    internal bool Seeking { get; set; }
    // EN: One strike. Authority: tests every living target of the creator's layers in this world against `Hits` and deals the prepared damage, and opens a claim ticket for the players whose own
    //     machine decides. A copy: tests only the body of this machine's player and claims once. A guess: nothing (the author's own drawing still runs). A target is hit once per strike; strikes may hit it again.
    // VI: Mot cu danh. Authority: xet moi muc tieu song thuoc layer cua nguoi tao trong world nay voi `Hits` roi gay sat thuong da chuan bi, va mo ve claim cho nguoi choi ma may cua ho tu quyet.
    //     Ban sao: chi xet than nguoi choi cua may nay va bao mot lan. Ban doan: khong lam gi. Moi muc tieu trung mot lan moi cu; cu khac trung lai duoc.
    protected void Strike(in PNBlastStrike strike)
    {
        if (!life.IsRunning && !life.IsEnding) return;
        int index = strikes++;
        if (IsReplica) { StrikeCopy(strike, index); return; }
        StrikeReal(strike, index);
    }
    private void StrikeReal(in PNBlastStrike strike, int index)
    {
        if (strike.Capture == null || strike.Prepared == null || strike.Hits == null) throw new ArgumentException("A strike of a real blast needs its capture, prepared damage and shape.");
        struck.Clear();
        var claims = Manager.ClaimBook.Policy; int mask = created.Source.TargetLayers.Mask;
        bool ticket = claims != null && index <= PNHurtClaimLimits.MaxHit;
        if (ticket)
        {
            // The machine of a player decides his own hits (plan 4.4c): the blast does not test him here, it only keeps the damage ready for his claim.
            var cap = strike.Capture; var ready = strike.Prepared; float x = strike.Center.x, y = strike.Center.y, z = strike.Center.z;
            Manager.OpenClaims(this, index, strike.Center, strike.Radius, strike.Height, (stat, key) => stat.ReceiveDamage(cap.Damage(ready).WithHit(key, x, y, z)));
        }
        var entities = World != null ? Manager.Session.Entities.Snapshot(World) : Manager.Session.Entities.Snapshot();
        foreach (var entity in entities)
        {
            var target = entity.GetComponent<PNStat>();
            if (target == null || !ValidTarget(target) || !PNLayers.Contains(mask, target.gameObject.layer) || struck.Contains(target.EntityId)) continue;
            if (ticket && target.gameObject.layer == PNLayers.Player && claims.ClaimsDecide(target)) continue;
            var collider = target.GetComponent<Collider>(); if (collider == null) continue;
            var b = collider.bounds;
            if (b.max.y < strike.Center.y - strike.Height || b.min.y > strike.Center.y + strike.Height || !strike.Hits(b)) continue;
            struck.Add(target.EntityId); HitCount++;
            target.ReceiveDamage(strike.Capture.Damage(strike.Prepared));
        }
    }
    // A copy on the machine of a player: tests only his body against the shape and claims once for this strike (the early phase first, so he reacts at once). Not while the copy is being brought to its age.
    private void StrikeCopy(in PNBlastStrike strike, int index)
    {
        Manager.NoteReplicaDetonated();
        int mask = ReplicaTargetMask;
        if (Seeking || mask == 0 || strike.Hits == null || index > PNHurtClaimLimits.MaxHit) return;
        var sink = Manager.ClaimBook.SinkFor(out var me); if (sink == null || me == null || !me.IsReady || PNTargetCandidates.IsDead(me) || !PNLayers.Contains(mask, me.gameObject.layer)) return;
        var collider = me.GetComponent<Collider>(); if (collider == null) return;
        var b = collider.bounds;
        if (b.max.y < strike.Center.y - strike.Height || b.min.y > strike.Center.y + strike.Height || !strike.Hits(b)) return;
        long id = Manager.ObjectIdOf(this); if (id <= 0) return;
        PNCombatEvents.RaiseEarly(me.EntityId.Value, PNBlastManager.HitName(id, index, me.EntityId.Value), true, b.center.x, b.center.y, b.center.z);
        if (sink.SendHitReport(PNHitReport.TakenFromObject(PNObjectKind.Blast, id, index), null)) Manager.ClaimBook.NoteSent();
    }
    private bool ValidTarget(PNStat s) => s.IsReady && s.gameObject.activeInHierarchy && !PNTargetCandidates.IsDead(s) && PNWorldGameplay.CanExecute(s) && ReferenceEquals(s.GetComponent<EveEntityIdentity>()?.World, World);

    internal void Perform(float gameDelta) { if (life.IsRunning) OnPerform(gameDelta); }
    internal PNInstanceBegin BeginStop(PNBlastEndReason reason) => life.Begin((int)reason);
    internal void RunEnd(PNBlastEndReason reason) { OnEnd(reason); life.ArmEnding(EndingDuration); }
    // A failing OnCancel is reported; the Cleanup that follows still runs and decides whether the blast is released.
    internal void RunCancel(PNBlastEndReason reason) { try { OnCancel(reason); } catch (Exception error) { Debug.LogException(error, this); } }
    // One step of the Ending; true when it is over and Cleanup is due.
    internal bool StepEnding(float delta) { if (!life.IsEnding) return false; OnEnding(delta); return life.AdvanceEnding(delta); }
    internal bool CleanupDue => life.CleanupPending;
    internal void RunCleanup()
    {
        if (!life.TakeCleanup() && !CleanupFailed) return;
        CleanupFailed = true; ReleaseTime();
        Cleanup(); CleanupFailed = false;
    }
    // A forced stop that finishes at once (shutdown): the Cancel hook if it is not over yet, then Cleanup (also a retry after a failed one).
    internal void CancelNow(PNBlastEndReason reason)
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
        ReleaseTime(); created = null; struck.Clear();
        Manager = null;World=null; Definition = null; Handle = default; life.Release();
        if (CleanupFailed) throw new InvalidOperationException("Faulted blast cleanup; existing pool must evict this instance.");
    }
    protected virtual void OnDisable() { if (life.IsRunning || life.IsEnding) Finish(PNBlastEndReason.Disabled); }
    protected virtual void OnDestroy() { if (life.IsRunning || life.IsEnding) Finish(PNBlastEndReason.Disabled); DisposePooledResources(); }
}

// What one `Strike` needs from the author: where and how big (the claim reach of a player's own machine uses `Center`/`Radius`/`Height`; `Height` is the half extent up and down), the footprint test,
// and the prepared damage taken from the capture (a copy has neither capture nor damage: pass null).
public readonly struct PNBlastStrike
{
    public readonly Vector3 Center; public readonly float Radius, Height; public readonly Func<Bounds, bool> Hits; public readonly PNHitCapture Capture; public readonly PNPreparedDamage Prepared;
    public PNBlastStrike(Vector3 center, float radius, float height, Func<Bounds, bool> hits, PNHitCapture capture, PNPreparedDamage prepared)
    { Center = center; Radius = radius; Height = height; Hits = hits; Capture = capture; Prepared = prepared; }
}

[DisallowMultipleComponent]
public sealed class PNBlastManager : MonoBehaviour, IPNHitReportReceiver
{
    private sealed class PoolEntry
    {
        internal GameObject Template; internal EveEasyPooling Pool; internal Scene Scene;
        internal readonly Dictionary<GameObject, PNBlastInstance> Instances = new Dictionary<GameObject, PNBlastInstance>();
    }
    private sealed class Entry { internal EveId Id, ProfileId; internal GameObject Root; internal PNBlastInstance Instance; internal PoolEntry Pool; internal Scene Scene; internal bool Replica;internal EveWorldContext World;internal IDisposable Lease; internal bool Guess, Adopted; internal double CreatedAt; internal (long, int) GuessKey; }
    private readonly Dictionary<(PNBlastDefinition, Scene), PoolEntry> pools = new Dictionary<(PNBlastDefinition, Scene), PoolEntry>();
    private readonly Dictionary<long, Entry> entries = new Dictionary<long, Entry>();
    private readonly Queue<Action> pending = new Queue<Action>();
    private readonly HashSet<PNBlastInstance> pendingResources = new HashSet<PNBlastInstance>();
    private long sequence; private bool busy, closing;
    public EveSessionContext Session { get; private set; }
    public int ActiveCount => entries.Values.Count(x => x.Instance != null && x.Instance.IsRunning);
    public int BorrowedCount => entries.Count;
    public int PooledObjectCount => pools.Values.Sum(x => x.Pool.CountAll);
    public static PNBlastManager Attach(EveRuntime runtime)
    {
        if (runtime == null || !runtime.IsReady) throw new InvalidOperationException("Ready runtime required.");
        var manager = runtime.GetComponent<PNBlastManager>() ?? runtime.gameObject.AddComponent<PNBlastManager>();
        manager.Initialize(EveRegistrySession.Context); return manager;
    }
    public static PNBlastManager Resolve(EveSessionContext session, Scene scene)
    {
        var ready = UnityEngine.Object.FindObjectsByType<PNBlastManager>().Where(x => x.isActiveAndEnabled && ReferenceEquals(x.Session, session)).ToArray();
        return ready.FirstOrDefault(x => x.GetComponent<EveRuntime>() != null)
            ?? scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNBlastManager>()).FirstOrDefault(x => x.isActiveAndEnabled && ReferenceEquals(x.Session, session))
            ?? throw new InvalidOperationException("Session blast manager has not been initialized.");
    }
    public void Initialize(EveSessionContext session)
    {
        if (Session == session && !closing) return;
        Shutdown(); Session = session ?? throw new ArgumentNullException(nameof(session)); closing = false;
        SceneManager.sceneUnloaded += SceneUnloaded;
    }
    public bool Contains(PNBlastHandle handle) => ReferenceEquals(handle.Manager, this) && entries.TryGetValue(handle.InstanceId, out var entry) && entry.Instance != null && entry.Instance.IsRunning;
    public PNBlastSnapshot Get(PNBlastHandle handle)
        => ReferenceEquals(handle.Manager, this) && entries.TryGetValue(handle.InstanceId, out var entry) && entry.Instance != null
            ? new PNBlastSnapshot(handle, entry.Id, entry.ProfileId, entry.Instance) : null;
    public IReadOnlyList<PNBlastSnapshot> Snapshot() => entries.Where(x => x.Value.Instance != null)
        .Select(x => new PNBlastSnapshot(new PNBlastHandle(this, x.Key), x.Value.Id, x.Value.ProfileId, x.Value.Instance)).ToArray();
    public PNBlastReceipt Create(PNBlastCreateRequest request)
    {
        var receipt = new PNBlastReceipt();
        Submit(() => CreateNow(request, receipt)); return receipt;
    }
    private void CreateNow(PNBlastCreateRequest request, PNBlastReceipt receipt, long replicaId = 0, bool guess = false)
    {
        GameObject root = null; PoolEntry pool = null; long id = 0; bool replica = replicaId != 0 || guess;
        try
        {
            if (closing || !isActiveAndEnabled || Session == null || !Session.IsActive || !replica && !Session.HasAuthority || replica && (request == null || !request.IsReplica || replicaId != 0 && objectEntries.ContainsKey(replicaId) || guess && (request.CastKey <= 0 || predictions.ContainsKey((request.CastKey, request.CastIndex))))) { receipt.Status = PNBlastStatus.Rejected; return; }
            if (request == null || !replica && (request.Source == null || request.Source.SessionId != Session.SessionId) || !request.Scene.IsValid() || !request.Scene.isLoaded)
                throw new ArgumentException("Blast requires valid source/session and loaded scene.");
            var p = request.Position;
            var world=PNWorldGameplay.ForScene(Session,request.Scene);
            if(world!=null&&!world.CanExecute||!replica&&request.Owner!=null&&(!PNWorldGameplay.CanExecute(request.Owner)||!ReferenceEquals(request.Owner.GetComponent<EveEntityIdentity>()?.World,world)))
                throw new InvalidOperationException("Blast world/owner is unavailable or crosses worlds.");
            if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z)) throw new ArgumentException("Finite blast position required.");
            var definition = Session.Registry.Instances().GetEffectiveKind<PNBlastDefinition>(request.BlastId);
            definition.ValidateInput(request.Input);
            if (!pools.TryGetValue((definition, request.Scene), out pool))
            {
                var template = definition.CreatePoolTemplate();
                pool = new PoolEntry { Template = template, Pool = new EveEasyPooling(template), Scene = request.Scene }; pools.Add((definition, request.Scene), pool);
                SceneManager.MoveGameObjectToScene(template, request.Scene);
                var localPool = pool;
                pool.Pool.Created += item => {
                    localPool.Instances[item] = item.GetComponent<PNBlastInstance>();
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
            var instance = root.GetComponent<PNBlastInstance>();
            id = ++sequence; var handle = new PNBlastHandle(this, id);
            entries.Add(id, new Entry { Id = Session.Registry.Instances().ResolveIdentity(request.BlastId), ProfileId = definition.Id, Root = root, Instance = instance, Pool = pool, Scene = request.Scene,World=world,Lease=world?.AcquireLease("blast") });
            instance.Bind(this, handle, definition, request);PNWorldPresentationRegistration.Register(world,root);root.SetActive(true);
            if (!instance.isActiveAndEnabled) throw new InvalidOperationException("Selected blast component must be active after initialization.");
            // Immediate admission occurs after activation, when owned clock effects may be acquired.
            instance.Perform(0);
            PNWorldPresentationRegistration.Register(world,root);
            if (!instance.IsRunning) throw new InvalidOperationException("Blast ended during initialization.");
            receipt.Handle = handle; receipt.Status = PNBlastStatus.Created;
            if (guess) { var made = entries[id]; made.Replica = true; made.Guess = true; made.CreatedAt = PredictionNow; made.GuessKey = (request.CastKey, request.CastIndex); predictions[made.GuessKey] = id; }
            else if (replica) { entries[id].Replica = true; objectEntries[replicaId] = id; objectIds[id] = replicaId; }
            else RecordSpawn(id, entries[id], request);
        }
        catch (Exception error)
        {
            if (id != 0 && entries.TryGetValue(id, out var entry))
            {
                Stop(id, entry, PNBlastEndReason.RuntimeError);
                if (entry.Instance.CleanupFailed) { receipt.Error = error; receipt.Status = PNBlastStatus.Failed; return; }
                entries.Remove(id);entry.Lease?.Dispose();
            }
            if (root != null) { try { pool.Pool.Release(root); } catch (Exception cleanup) { Debug.LogException(cleanup, this); } }
            receipt.Error = error; receipt.Status = PNBlastStatus.Failed;
        }
    }
    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public bool End(PNBlastHandle handle, PNBlastEndReason reason = PNBlastEndReason.Requested)
    {
        if (!Enum.IsDefined(typeof(PNBlastEndReason), reason)) throw new ArgumentException("Unknown blast end reason.");
        // A running blast takes any reason; one in its Ending takes only a forced stop (a second real finish is dropped); a finished one takes nothing.
        if (!ReferenceEquals(handle.Manager, this) || !entries.TryGetValue(handle.InstanceId, out var probe) || probe.Instance == null || probe.Instance.IsDone
            || probe.Instance.IsEnding && PNBlastInstance.IsEndReason(reason)) return false;
        Submit(() => {
            if (!entries.TryGetValue(handle.InstanceId, out var entry)) return;
            if (!Stop(handle.InstanceId, entry, reason)) return;
            // Unity may be destroying the pool storage during scene-unload OnDisable.
            // Gameplay cleanup is immediate; return after sceneUnloaded or next manager update.
            if (entry.Instance.IsDone && reason != PNBlastEndReason.Disabled) Return(handle.InstanceId, entry);
        }); return true;
    }
    // Gives one reason to a blast: an END starts its Ending (`OnEnd`; a fault there becomes a RuntimeError cancel), a CANCEL stops it now (`OnCancel`); the Cleanup follows when due.
    // The sync End record carries the reason that really took effect. Returns false when nothing happened. `strict`: a failing Cleanup is thrown, not logged.
    private bool Stop(long id, Entry entry, PNBlastEndReason reason, bool strict = false)
    {
        var instance = entry.Instance;
        if (ReferenceEquals(instance, null)) { NotifyEnded(id, entry, reason); return false; }
        var begin = instance.BeginStop(reason);
        if (begin == PNInstanceBegin.Ignored && !instance.CleanupFailed) return false;
        if (begin == PNInstanceBegin.RunEnd)
        {
            try { instance.RunEnd(reason); }
            catch (Exception error)
            {
                Debug.LogException(error, this);
                reason = PNBlastEndReason.RuntimeError; begin = instance.BeginStop(reason);
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
                if (++count == 10001) { Debug.LogError("Blast mutation cycle exceeded 10000 commands; shutting down manager.", this); Shutdown(); }
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
        claimBook.PruneTickets();
        Submit(() => {
            foreach (var pair in entries.ToArray())
            {
                var entry = pair.Value;
                try
                {
                    if (entry.Instance == null) { Return(pair.Key, entry); continue; }
                    if(entry.World!=null&&!entry.World.CanExecute)continue;
                    var instance = entry.Instance;
                    if ((instance.IsRunning || instance.IsEnding) && !instance.isActiveAndEnabled) { End(new PNBlastHandle(this, pair.Key), PNBlastEndReason.Disabled); continue; }
                    if (instance.IsRunning && !instance.DependencyHolds()) { End(new PNBlastHandle(this, pair.Key), PNBlastEndReason.OwnerUnavailable); continue; }
                    if (entry.Guess && !entry.Adopted && instance.IsRunning && PredictionNow - entry.CreatedAt > OrphanSeconds) { End(new PNBlastHandle(this, pair.Key), PNBlastEndReason.Requested); continue; }
                    if (instance.IsRunning) instance.Perform(instance.ScaleDelta(gameDelta));
                    else if (instance.IsEnding)
                    {
                        // The Ending runs on the blast's own clock; when it is over the Cleanup runs (once) and the blast goes back to the pool.
                        if (instance.StepEnding(instance.ScaleDelta(gameDelta))) { instance.RunCleanup(); Return(pair.Key, entry); }
                    }
                    else Return(pair.Key, entry);
                }
                catch (Exception error) { Debug.LogException(error, this); End(new PNBlastHandle(this, pair.Key), PNBlastEndReason.RuntimeError); }
            }
        });
    }
    // ---- spawned-object sync (stage O2: the authority records what it creates; the view-only copy of an area/blast is built in O5) ----------------------------------------
    private PNObjectSyncService objectSync;
    private readonly Dictionary<long, long> objectIds = new Dictionary<long, long>();
    // EN: The spawned-object sync of this play. On the authority every blast made here is recorded (Spawn at creation, End when it stops). Null = no sync.
    // VI: Dich vu dong bo vat the cua phien. O authority moi blast tao ra deu duoc ghi (Spawn luc tao, End luc dung).
    public PNObjectSyncService ObjectSync
    {
        get => objectSync;
        set
        {
            if (ReferenceEquals(objectSync, value)) return;
            if (objectSync != null) { objectSync.SetSink(PNObjectKind.Blast, null); objectSync.SetSource(PNObjectKind.Blast, null); }
            objectSync = value;
            if (value != null) { bridge = bridge ?? new ObjectBridge(this); value.SetSink(PNObjectKind.Blast, bridge); value.SetSource(PNObjectKind.Blast, bridge); }
        }
    }
    private ObjectBridge bridge;
    // ---- hit claims (plan 4.4c V2, way B) ----
    private PNHurtClaimBook claimBook = new PNHurtClaimBook();
    public PNHurtClaimBook ClaimBook { get => claimBook; set => claimBook = value ?? new PNHurtClaimBook(); }
    // The sync id of a blast of this manager (a copy's or the authority's); 0 when it has none.
    public long ObjectIdOf(PNBlastInstance instance) => instance != null && objectIds.TryGetValue(instance.Handle.InstanceId, out var id) ? id : 0L;
    // EN: The authority's blast has detonated: for PNHurtClaimLimits.ClaimGraceSeconds a player whose machine decides his own hits may claim it (the claim travels a trip after the copy's detonation). `apply` deals the blast's own prepared damage.
    // VI: Blast cua authority vua no: trong ClaimGraceSeconds nguoi choi ma may tu quyet duoc bao trung (lenh di mat mot chuyen sau luc ban hinh no). apply gay sat thuong da chuan bi cua chinh blast.
    public void OpenClaims(PNBlastInstance instance, int strike, Vector3 center, float radius, float height, Action<PNStat, string> apply)
    {
        long objectId = ObjectIdOf(instance); if (objectId <= 0 || claimBook.Policy == null) return;
        claimBook.OpenTicket(PNClaimTicketKey.Blast(objectId, strike), center, radius, height, 1f, stat => { apply(stat, HitName(objectId, strike, stat.EntityId.Value)); return true; }, () => PredictionNow);
    }
    // The name of a hit of one strike of a blast: object, strike index and the victim, the same on the authority and on the machine that claims it.
    public static string HitName(long objectId, int strike, long victim)
        => "o" + objectId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":s" + strike.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + PNHurtClaimLimits.ClaimKeyIndex(victim);
    // EN: A claim from the machine of `target`: the strike (`report.Detail`) of the blast named by `objectId` hurts him once when the policy says his machine decides, the claim comes within the grace after that strike and he is near the blast.
    public bool AcceptHitReport(in PNHitReport report, PNStat victim, float tolerance)
        => report.Role == PNHitReportRole.Taken && !report.ByCast && report.Kind == PNObjectKind.Blast && AcceptTaken(report.ObjectId, report.Detail, victim, tolerance);
    private bool AcceptTaken(long objectId, int strike, PNStat target, float tolerance)
    {
        if (objectId <= 0 || strike < 0 || strike > PNHurtClaimLimits.MaxHit || target == null || Session == null || !Session.HasAuthority) return false;
        return claimBook.AcceptTicket(PNClaimTicketKey.Blast(objectId, strike), target, tolerance);
    }
    private readonly Dictionary<long, long> objectEntries = new Dictionary<long, long>();
    // ---- the shooter's guess (plan O5, own book of the blast manager): the player's machine draws the blast at the cast, named by (cast key, index); the authority's Spawn record with that name takes it over ----
    // A guess nobody takes over within OrphanSeconds (the authority refused the cast) is removed; a refused or taken-back cast removes it at once (DiscardPredicted). It draws only: no damage, ever.
    // The guess starts when the cast does, the authority's blast one trip later, so the shooter sees the detonation a trip before the damage (kept on purpose, measured on Player).
    public const double OrphanSeconds = 1d;
    private readonly Dictionary<(long, int), long> predictions = new Dictionary<(long, int), long>();
    public Func<double> PredictionClock { get; set; }
    private double PredictionNow => PredictionClock != null ? PredictionClock() : Time.unscaledTimeAsDouble;
    public int Adoptions { get; private set; }
    public int AdoptScaleFixes { get; private set; }
    public int GuessCount { get { int count = 0; foreach (var entry in entries.Values) if (entry.Guess && !entry.Adopted && entry.Instance != null && entry.Instance.IsRunning) count++; return count; } }
    // EN: Draws the shooter's guess of a blast. `request` is a view-only request (PNBlastCreateRequest.Replica) with the cast key; nothing is recorded or sent.
    // VI: Ve ban doan blast cua nguoi ban. request la yeu cau chi-de-xem (Replica) co cast key; khong ghi, khong gui.
    public PNBlastReceipt CreateGuess(PNBlastCreateRequest request)
    {
        if (request == null || !request.IsReplica || request.CastKey <= 0) throw new ArgumentException("A guess needs a view-only request with a cast key.", nameof(request));
        var receipt = new PNBlastReceipt();
        Submit(() => CreateNow(request, receipt, 0, true)); return receipt;
    }
    // The cast was refused (or taken back): the blasts it guessed go now. A blast the authority's record already took over is the real one's copy and stays.
    public void DiscardPredicted(long castKey)
    {
        if (castKey <= 0) return;
        foreach (var pair in predictions.ToArray())
            if (pair.Key.Item1 == castKey && entries.TryGetValue(pair.Value, out var entry) && entry.Guess && !entry.Adopted) End(new PNBlastHandle(this, pair.Value));
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
    public int ReplicasDetonated { get; private set; }
    // A copy played its detonation (draw only, no damage); counted for tests and the Player scenario.
    public void NoteReplicaDetonated() => ReplicasDetonated++;
    // EN: Builds a view-only copy of a blast from a Spawn record and brings it to `elapsed` seconds of its own life. It deals no damage and ends when told to or at its own end.
    // VI: Dung ban hinh cua mot blast tu ban ghi Spawn va dua no toi `elapsed` giay cua doi no. Khong gay sat thuong.
    public PNBlastReceipt CreateReplica(PNObjectRecord record, double elapsed)
    {
        if (record == null || record.Type != PNObjectRecordType.Spawn || record.Kind != PNObjectKind.Blast) throw new ArgumentException("A blast Spawn record is required.", nameof(record));
        var receipt = new PNBlastReceipt();
        Submit(() => CreateReplicaNow(record, elapsed, receipt)); return receipt;
    }
    private void CreateReplicaNow(PNObjectRecord record, double elapsed, PNBlastReceipt receipt)
    {
        try
        {
            if (closing || Session == null || !Session.IsActive || objectEntries.ContainsKey(record.Id)) { receipt.Status = PNBlastStatus.Rejected; return; }
            if (TryAdopt(record)) { receipt.Status = PNBlastStatus.Created; return; }
            EveWorldContext world = null;
            foreach (var candidate in Session.Worlds.Snapshot()) if (candidate.InstanceId == record.World.Instance && candidate.Incarnation == record.World.Incarnation) { world = candidate; break; }
            if (world == null || !world.CanExecute || !world.PhysicsOwner.IsValid()) { receipt.Status = PNBlastStatus.Rejected; return; }
            JToken input = record.Input == null ? null : JToken.Parse(record.Input);
            PNStat owner = null;
            if (record.Owner != 0 && Session.Entities.TryGet(new EveEntityId(record.Owner), out var loader) && loader != null) owner = loader.GetComponent<PNStat>();
            var request = PNBlastCreateRequest.Replica(record.DefinitionId, input, world.PhysicsOwner, new Vector3(record.Position.X, record.Position.Y, record.Position.Z), owner, record.Scale).WithReplicaTargets(record.TargetMask);
            CreateNow(request, receipt, record.Id);
            if (receipt.Status == PNBlastStatus.Created && entries.TryGetValue(receipt.Handle.InstanceId, out var made) && made.Instance != null && elapsed > 1e-4d)
            {
                // The strikes that already passed are counted and drawn but not claimed (the authority's tickets are gone by now).
                made.Instance.Seeking = true;
                try { made.Instance.Perform((float)Math.Min(elapsed, PNObjectSyncLimits.MaxReplicaSeconds)); } finally { made.Instance.Seeking = false; }
            }
        }
        catch (Exception error) { receipt.Error = error; receipt.Status = PNBlastStatus.Failed; }
    }
    private sealed class ObjectBridge : IPNObjectSyncSink, IPNObjectSyncSource
    {
        private readonly PNBlastManager owner;
        public ObjectBridge(PNBlastManager manager) { owner = manager; }
        public bool Spawned(PNObjectRecord spawn, double elapsed) { var receipt = owner.CreateReplica(spawn, elapsed); return receipt.Status == PNBlastStatus.Created || receipt.Status == PNBlastStatus.Pending; }
        public void Retargeted(PNObjectRecord retarget) { }
        public void Ended(PNObjectRecord end)
        {
            if (!owner.objectEntries.TryGetValue(end.Id, out var handleId)) return;
            var reason = Enum.IsDefined(typeof(PNBlastEndReason), (int)end.Reason) ? (PNBlastEndReason)end.Reason : PNBlastEndReason.Requested;
            owner.End(new PNBlastHandle(owner, handleId), reason);
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
    private void RecordSpawn(long id, Entry entry, PNBlastCreateRequest request)
    {
        if (objectSync == null || !objectSync.IsAuthority || entry.World == null) return;
        try
        {
            var json = request.Input; string input = json == null || json.Type == JTokenType.Null ? null : json.ToString(Formatting.None);
            if (input != null && input.Length > PNObjectSyncLimits.InputChars) { objectSync.NoteRefused(); return; }
            var at = request.Position; long objectId = objectSync.NextId();
            var record = PNObjectRecord.Spawn(PNObjectKind.Blast, objectId, PNObjectWorld.Of(entry.World), entry.Id, input, new PNObjectVector(at.x, at.y, at.z), new PNObjectVector(0, 0, 1), 0,
                request.Owner != null ? request.Owner.EntityId.Value : 0L, 0L, request.CastKey > 0 ? request.CastKey.ToString(System.Globalization.CultureInfo.InvariantCulture) : null, request.CastIndex, 1d, entry.Instance.SyncScale, 1d, objectSync.Now, 0f, 0f, request.Source != null ? request.Source.TargetLayers.Mask : 0);
            objectIds[id] = objectId;
            if (!objectSync.RecordSpawn(record)) objectIds.Remove(id);
        }
        catch (FormatException) { objectSync.NoteRefused(); }
    }
    private void NotifyEnded(long id, Entry entry, PNBlastEndReason reason)
    {
        if (objectSync == null || !objectIds.TryGetValue(id, out var objectId)) return;
        objectIds.Remove(id);
        if (entry.Replica) { objectEntries.Remove(objectId); objectSync.ReplicaEnded(objectId); return; }
        var at = entry.Instance != null ? entry.Instance.transform.position : default;
        try { objectSync.RecordEnd(PNObjectRecord.End(PNObjectKind.Blast, objectId, PNObjectWorld.Of(entry.World), (byte)reason, new PNObjectVector(at.x, at.y, at.z))); }
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
            foreach (var pair in entries.Where(x => x.Value.Scene == scene).ToArray()) { Stop(pair.Key, pair.Value, PNBlastEndReason.SceneUnloaded); Return(pair.Key, pair.Value); }
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
        if(busy)throw new InvalidOperationException("Blast mutations must drain before actor transfer.");
        foreach(var entry in entries.Values.ToArray())if(entry.Instance!=null&&entry.Instance.IsRunning)entry.Instance.ActorWorldChanging(actor);
    }
    public void ReleaseWorld(EveWorldContext world)
    {
        if(busy)throw new InvalidOperationException("Blast mutation must drain before world teardown.");
        foreach(var pair in entries.Where(x=>ReferenceEquals(x.Value.World,world)).ToArray())
        { Stop(pair.Key,pair.Value,PNBlastEndReason.SceneUnloaded,true);Return(pair.Key,pair.Value); }
        foreach(var pair in pools.Where(x=>PNWorldGameplay.ForScene(Session,x.Value.Scene)==world).ToArray())
        {
            if(entries.Values.Any(x=>ReferenceEquals(x.Pool,pair.Value)))throw new InvalidOperationException("World blast cleanup remains pending.");
            foreach(var instance in pair.Value.Instances.Values)instance.DisposePooledResources();
            pair.Value.Pool.Dispose();DestroyOwned(pair.Value.Template);pools.Remove(pair.Key);
        }
    }
    public void Shutdown()
    {
        closing = true; SceneManager.sceneUnloaded -= SceneUnloaded; objectEntries.Clear(); objectIds.Clear(); predictions.Clear();
        var errors = new List<Exception>();
        foreach (var pair in entries.ToArray())
        {
            try
            {
                if (!ReferenceEquals(pair.Value.Instance, null)) pair.Value.Instance.CancelNow(PNBlastEndReason.SessionEnded);
                entries.Remove(pair.Key);pair.Value.Lease?.Dispose();
            }
            catch (Exception error) { errors.Add(error); }
        }
        // Required gameplay failures retain entries/pools/session and block the next play.
        if (errors.Count != 0) throw new AggregateException("Blast gameplay cleanup failed.", errors);
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
        if (errors.Count != 0) throw new AggregateException("Blast resource cleanup failed.", errors);
        Session = null;
    }
    private static void DestroyOwned(GameObject obj) { if (obj == null) return; if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    private void Update() => Advance(Time.deltaTime);
    private void OnDisable() => Shutdown();
    private void OnDestroy() => Shutdown();
}
#endif
