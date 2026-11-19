#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        // Spawned-object sync: the id of this object in the records (0 = not synced) and whether it is a view-only copy.
        internal long ObjectId; internal bool Replica;
        // The shooter's guess (plan 4.4a): whether the authority's record has taken it over (Adopted), the heading it was made with and when (unscaled seconds).
        internal bool Predicted, Adopted; internal Vector3 SpawnHeading; internal double CreatedAt;
        // The state the last record gave a copy (position, heading, speed, the object's age and the host clock then): the authority's audit predicts the copy from it.
        internal Vector3 SentPosition, SentHeading; internal float SentSpeed, SentAge; internal double SentAt;
    }
    // EN: How the authority audits a copy (plan 4.3a). The copy of an object that declares SyncDeterministic is checked every AuditSeconds and corrected when the real object is more than AuditDistance
    //     from the straight line the last record implies; any other copy is corrected as soon as FollowSeconds passed and it is more than FollowDistance away. A correction is a Retarget record.
    // VI: Cach authority kiem ban hinh (plan 4.3a): vat khai SyncDeterministic kiem moi AuditSeconds, sua khi lech quá AuditDistance so voi duong thang suy ra tu ban ghi cuoi; vat khac sua thuong xuyen.
    public const double AuditSeconds = .5d, FollowSeconds = .1d;
    public const float AuditDistance = 1f, FollowDistance = .05f;
    // EN: Prediction (plan 4.4a). A hit report names the projectile by the cast key plus ReportStepBase + its index in the volley (steps below it belong to Actions). A guess that no record has taken over
    //     after OrphanSeconds is removed (the authority made fewer projectiles than the guess); an ended guess is remembered for EndedPredictionSeconds so its record, arriving late, is swallowed instead of showing a second one.
    // VI: Doan (plan 4.4a): bao trung goi ten vien bang cast key + ReportStepBase + so thu tu; vien doan khong duoc ban ghi nao nhan sau OrphanSeconds thi go; vien doan da het duoc nho EndedPredictionSeconds de ban ghi toi muon bi nuot.
    public const int ReportStepBase = 192, MaxReportedIndex = 63;
    public const double OrphanSeconds = 1d, EndedPredictionSeconds = 3d;
    // The clock of the guesses (unscaled seconds); a test sets its own.
    public Func<double> PredictionClock { get; set; }
    private double PredictionNow => PredictionClock != null ? PredictionClock() : Time.unscaledTimeAsDouble;
    // Evidence for the shooter's machine: how many guesses a record took over, and how often the record disagreed (a turn of more than a few degrees, another target, other factors).
    public int Adoptions { get; private set; }
    public int AdoptTurns { get; private set; }
    public int AdoptTargetChanges { get; private set; }
    public int AdoptFactorFixes { get; private set; }
    private readonly Dictionary<(long, int), long> predictions = new Dictionary<(long, int), long>();
    private readonly Dictionary<(long, int), double> endedPredictions = new Dictionary<(long, int), double>();
    // The seed of the random numbers of a projectile that belongs to a cast: the same on the shooter's guess, the authority's object and every copy, whatever id the sync gave it.
    internal static long CastSeed(long castKey, int index) => unchecked(castKey * 1000003L + index + 1L);
    private static readonly object NoWorld = new object();
    private readonly Dictionary<(PNProjectileDefinition, Scene), PoolEntry> pools = new Dictionary<(PNProjectileDefinition, Scene), PoolEntry>();
    private readonly Dictionary<long, Entry> entries = new Dictionary<long, Entry>();
    private readonly List<Entry> order = new List<Entry>();
    // Per-world candidate lists, shared with target selection (autotarget, a volley's aim).
    public PNTargetIndex Targets { get; } = new PNTargetIndex();
    private readonly HashSet<object> refreshed = new HashSet<object>();
    private readonly Queue<Action> pending = new Queue<Action>();
    private readonly HashSet<PNProjectileInstance> pendingResources = new HashSet<PNProjectileInstance>();
    private long sequence, stamp; private bool busy, closing, compactNeeded;
    public EveSessionContext Session { get; private set; }
    private PNObjectSyncService objectSync; private ObjectBridge bridge;
    private readonly Dictionary<long, long> objectEntries = new Dictionary<long, long>();
    // EN: The spawned-object sync of this play (stage O2). On the authority every projectile made here is recorded (Spawn, Retarget, End); on a client the manager is the sink that
    //     builds, corrects and ends the view-only copies. Null = no sync (offline, or before the glue sets it). Set it back to null before the session ends.
    // VI: Dich vu dong bo vat the cua phien. O authority moi dan tao ra deu duoc ghi; o client manager la noi dung / chinh / ket thuc ban hinh.
    public PNObjectSyncService ObjectSync
    {
        get => objectSync;
        set
        {
            if (ReferenceEquals(objectSync, value)) return;
            if (objectSync != null) { objectSync.SetSink(PNObjectKind.Projectile, null); objectSync.SetSource(PNObjectKind.Projectile, null); }
            objectSync = value;
            if (value != null) { bridge = bridge ?? new ObjectBridge(this); value.SetSink(PNObjectKind.Projectile, bridge); value.SetSource(PNObjectKind.Projectile, bridge); }
        }
    }
    public int ReplicaCount { get { int count = 0; foreach (var entry in entries.Values) if (entry.Replica) count++; return count; } }
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
        if (Session == session && !closing) { Targets.Initialize(session, TargetRefreshSeconds); return; }
        Shutdown(); Session = session ?? throw new ArgumentNullException(nameof(session)); closing = false;
        Targets.Initialize(session, TargetRefreshSeconds);
        SceneManager.sceneUnloaded += SceneUnloaded;
    }
    public bool Contains(PNProjectileHandle handle) => ReferenceEquals(handle.Manager, this) && entries.TryGetValue(handle.InstanceId, out var entry) && entry.Instance != null && entry.Instance.IsRunning;
    public PNProjectileSnapshot Get(PNProjectileHandle handle)
        => ReferenceEquals(handle.Manager, this) && entries.TryGetValue(handle.InstanceId, out var entry) && entry.Instance != null ? new PNProjectileSnapshot(handle, entry.DefinitionId, entry.DefinitionId, entry.Instance) : null;
    // Running projectiles (position, box, heading, world, handle) for systems that scan or clear them; geometry and filtering are the caller's.
    public IReadOnlyList<PNProjectileSnapshot> Snapshot()
        => order.Where(x => !x.Removed && x.Instance != null && x.Instance.IsRunning).Select(x => new PNProjectileSnapshot(new PNProjectileHandle(this, x.Id), x.DefinitionId, x.DefinitionId, x.Instance)).ToArray();

    // Nearest live target of a world within the radius on the given layers, from the last candidate list of that world (null when none,
    // or when no list was built there yet). Target selection with a rule goes through PNTargetSelector over Targets.Acquire(world).
    public PNStat FindNearest(EveWorldContext world, Vector3 position, float radius, int layerMask)
        => Targets.Peek(world)?.FindNearest(position, radius, layerMask);
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
    // A guess is made only on a machine that is not the authority (the sync service says so when there is one; the session's role otherwise).
    private bool GuessAllowed => objectSync != null ? !objectSync.IsAuthority : Session != null && !Session.HasAuthority;
    private void CreateNow(PNProjectileLaunch launch, PNProjectileSpawn spawn, PNProjectileReceipt receipt, long replicaId = 0)
    {
        bool replica = replicaId != 0;
        GameObject root = null; PoolEntry pool = null; long id = 0;
        try
        {
            if (closing || !isActiveAndEnabled || Session == null || !Session.IsActive || !replica && (launch == null || (launch.IsPrediction ? !GuessAllowed : !Session.HasAuthority))) { receipt.Status = PNProjectileStatus.Rejected; return; }
            if (launch == null || spawn == null || launch.IsReplica != replica || !replica && launch.Source.SessionId != Session.SessionId || !spawn.Scene.IsValid() || !spawn.Scene.isLoaded)
                throw new ArgumentException("Projectile requires a launch from this session and a loaded scene.");
            var world = PNWorldGameplay.ForScene(Session, spawn.Scene);
            if (world != null && !world.CanExecute) { receipt.Status = PNProjectileStatus.Rejected; return; }
            var definition = Session.Registry.Instances().GetEffectiveKind<PNProjectileDefinition>(launch.ProjectileId);
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
            var entry = new Entry { Id = id, DefinitionId = Session.Registry.Instances().ResolveIdentity(launch.ProjectileId), Root = root, Instance = instance, Pool = pool, Scene = spawn.Scene, World = world, Lease = world?.AcquireLease("projectile") };
            entries.Add(id, entry); order.Add(entry);
            if (launch.IsPrediction) { entry.Predicted = true; entry.SpawnHeading = spawn.Direction; entry.CreatedAt = PredictionNow; predictions[(launch.CastKey, spawn.CastIndex)] = id; }
            // The sync id is taken before the implementation starts so Random is seeded by it from the first line of Initialize (a copy takes the record's id).
            long objectId = replica ? replicaId : objectSync != null && objectSync.IsAuthority && !launch.IsPrediction ? objectSync.NextId() : 0L;
            instance.Bind(this, handle, definition, launch, spawn, world, launch.CastKey != 0 ? CastSeed(launch.CastKey, spawn.CastIndex) : objectId); PNWorldPresentationRegistration.Register(world, root); root.SetActive(true);
            if (!instance.isActiveAndEnabled) throw new InvalidOperationException("Selected projectile component must be active after initialization.");
            if (!instance.IsRunning) throw new InvalidOperationException("Projectile ended during initialization.");
            receipt.Handle = handle; receipt.Status = PNProjectileStatus.Created;
            if (replica) { entry.Replica = true; entry.ObjectId = replicaId; objectEntries[replicaId] = id; }
            else if (!launch.IsPrediction) RecordSpawn(entry, launch, spawn, objectId);
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
    // ---- spawned-object sync ----------------------------------------------------------------------------------------------------
    private static PNObjectVector Vec(Vector3 v) => new PNObjectVector(v.x, v.y, v.z);
    private void RecordSpawn(Entry entry, PNProjectileLaunch launch, PNProjectileSpawn spawn, long id)
    {
        if (id == 0 || objectSync == null || !objectSync.IsAuthority || entry.World == null) return;
        try
        {
            var json = launch.Input; string input = json == null || json.Type == JTokenType.Null ? null : json.ToString(Formatting.None);
            if (input != null && input.Length > PNObjectSyncLimits.InputChars) { objectSync.NoteRefused(); return; }
            var record = PNObjectRecord.Spawn(PNObjectKind.Projectile, id, PNObjectWorld.Of(entry.World), entry.DefinitionId, input, Vec(spawn.Position), Vec(spawn.Direction),
                spawn.Target != null ? spawn.Target.EntityId.Value : 0L, launch.SourceId.Value, 0L, launch.CastKey != 0 ? launch.CastKey.ToString(System.Globalization.CultureInfo.InvariantCulture) : null, spawn.CastIndex, launch.SpeedFactor, launch.ScaleFactor, launch.LifetimeFactor, objectSync.Now, 0f, entry.Instance.LifeHint);
            entry.ObjectId = id; objectEntries[id] = entry.Id; MarkSent(entry);
            if (!objectSync.RecordSpawn(record)) { entry.ObjectId = 0; objectEntries.Remove(id); }
        }
        catch (FormatException) { objectSync.NoteRefused(); }
    }
    // What a copy knows after a record: the real object's state now.
    private void MarkSent(Entry entry)
    {
        var instance = entry.Instance; if (instance == null) return;
        entry.SentPosition = instance.Position; entry.SentHeading = instance.Forward; entry.SentSpeed = (instance.Velocity + instance.External).magnitude; entry.SentAge = instance.Age; entry.SentAt = objectSync.Now;
    }
    // After each tick of a real, synced object: sends a correction (a Retarget record with the unchanged target) when the copy, flying straight on from the last record, would be off.
    private void AuditSync(Entry entry)
    {
        var instance = entry.Instance;
        if (objectSync == null || !objectSync.IsAuthority || entry.ObjectId == 0 || entry.Replica || instance == null || !instance.IsRunning) return;
        bool declared = instance.SyncDeterministic; double now = objectSync.Now;
        if (now - entry.SentAt < (declared ? AuditSeconds : FollowSeconds)) return;
        var expected = entry.SentPosition + entry.SentHeading * (entry.SentSpeed * Mathf.Max(0f, instance.Age - entry.SentAge));
        if ((instance.Position - expected).magnitude <= (declared ? AuditDistance : FollowDistance)) { entry.SentAt = now; return; }
        MarkSent(entry);
        try { objectSync.RecordRetarget(PNObjectRecord.Retarget(PNObjectKind.Projectile, entry.ObjectId, PNObjectWorld.Of(entry.World), instance.TargetEntityValue, Vec(instance.Position), Vec(instance.Forward), now)); }
        catch (FormatException) { objectSync.NoteRefused(); }
    }
    internal void NotifyRetarget(PNProjectileInstance instance)
    {
        if (objectSync == null || !objectSync.IsAuthority || !ReferenceEquals(instance.Handle.Manager, this) || !entries.TryGetValue(instance.Handle.InstanceId, out var entry) || entry.ObjectId == 0 || entry.Replica) return;
        MarkSent(entry);
        try { objectSync.RecordRetarget(PNObjectRecord.Retarget(PNObjectKind.Projectile, entry.ObjectId, PNObjectWorld.Of(entry.World), instance.TargetEntityValue, Vec(instance.Position), Vec(instance.Forward), objectSync.Now)); }
        catch (FormatException) { objectSync.NoteRefused(); }
    }
    private void NotifyEnded(Entry entry, PNProjectileEndReason reason)
    {
        if (entry.ObjectId == 0 || objectSync == null) return;
        long id = entry.ObjectId; entry.ObjectId = 0; objectEntries.Remove(id);
        if (entry.Replica) { objectSync.ReplicaEnded(id); return; }
        try { objectSync.RecordEnd(PNObjectRecord.End(PNObjectKind.Projectile, id, PNObjectWorld.Of(entry.World), (byte)reason, Vec(entry.Instance != null ? entry.Instance.Position : default))); }
        catch (FormatException) { objectSync.NoteRefused(); }
    }
    // EN: Builds a view-only copy of a projectile from a Spawn record and lives `elapsed` seconds of its life at once. The copy deals no damage and ends only when told to or at its own lifetime.
    //     Pending while the command queue is busy. Rejected when the world is not here, not running or the cap is reached.
    // VI: Dung ban hinh cua mot vien dan tu ban ghi Spawn va song san `elapsed` giay. Khong gay sat thuong, chi ket thuc khi duoc bao hoac het tuoi tho.
    public PNProjectileReceipt CreateReplica(PNObjectRecord record, double elapsed)
    {
        if (record == null || record.Type != PNObjectRecordType.Spawn || record.Kind != PNObjectKind.Projectile) throw new ArgumentException("A projectile Spawn record is required.", nameof(record));
        var receipt = new PNProjectileReceipt();
        Submit(() => CreateReplicaNow(record, elapsed, receipt)); return receipt;
    }
    private void CreateReplicaNow(PNObjectRecord record, double elapsed, PNProjectileReceipt receipt)
    {
        try
        {
            if (closing || Session == null || !Session.IsActive || objectEntries.ContainsKey(record.Id)) { receipt.Status = PNProjectileStatus.Rejected; return; }
            EveWorldContext world = null;
            foreach (var candidate in Session.Worlds.Snapshot()) if (candidate.InstanceId == record.World.Instance && candidate.Incarnation == record.World.Incarnation) { world = candidate; break; }
            if (world == null || !world.CanExecute || !world.PhysicsOwner.IsValid()) { receipt.Status = PNProjectileStatus.Rejected; return; }
            JToken input = record.Input == null ? null : JToken.Parse(record.Input);
            PNStat target = null;
            if (record.Target != 0 && Session.Entities.TryGet(new EveEntityId(record.Target), out var loader)) target = loader.GetComponent<PNStat>();
            long castKey = 0; if (record.CastKey != null) long.TryParse(record.CastKey, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out castKey);
            // The shooter's own guess of this projectile takes the record over (no second copy); one that already ended swallows it.
            if (castKey != 0 && TryAdopt(record, castKey, target)) { receipt.Status = PNProjectileStatus.Created; return; }
            var launch = PNProjectileLaunch.Replica(record.DefinitionId, input, record.Owner != 0 ? new EveEntityId(record.Owner) : default, record.Speed, record.Scale, record.Lifetime);
            if (castKey != 0) launch = launch.WithCast(castKey, false);
            var spawn = new PNProjectileSpawn(world.PhysicsOwner, new Vector3(record.Position.X, record.Position.Y, record.Position.Z), new Vector3(record.Direction.X, record.Direction.Y, record.Direction.Z), target, record.CastIndex);
            CreateNow(launch, spawn, receipt, record.Id);
            // `elapsed` is the record's age plus the delay since its stamp; the record's position is where the object was at the stamp, so only the delay is flown.
            if (receipt.Status == PNProjectileStatus.Created && entries.TryGetValue(receipt.Handle.InstanceId, out var made) && made.Instance != null)
            {
                made.Instance.SeedProgress(record.Age); double delay = elapsed - record.Age;
                if (delay > 1e-4d) made.Instance.FastForward(delay, MaxSubSteps);
            }
        }
        catch (Exception error) { receipt.Error = error; receipt.Status = PNProjectileStatus.Failed; }
    }
    // EN: The record of a projectile this machine's shooter already guessed. The guess becomes the copy: it takes the record's id (so End and Retarget find it), keeps its place (it is ahead of the real one by
    //     the trip of the input, which is the point of a guess) and is corrected only in space: a heading that left the record's by more than a few degrees is turned by the difference, a different target is set.
    // VI: Ban ghi cua vien ma nguoi ban da doan. Vien doan thanh ban hinh: nhan id cua ban ghi, giu cho cua no va chi sua theo khong gian.
    private bool TryAdopt(PNObjectRecord record, long castKey, PNStat target)
    {
        var key = (castKey, record.CastIndex);
        if (predictions.TryGetValue(key, out var handleId) && entries.TryGetValue(handleId, out var entry) && entry.Instance != null && entry.Instance.IsRunning && !entry.Adopted)
        {
            entry.Adopted = true; entry.Replica = true; entry.ObjectId = record.Id; objectEntries[record.Id] = entry.Id; predictions.Remove(key);
            var instance = entry.Instance; Adoptions++;
            // The guess was made with the factors this machine knew; the authority's are the real ones.
            var made = instance.Launch;
            if (made != null && (Math.Abs(record.Speed - made.SpeedFactor) > 1e-3 * made.SpeedFactor || Math.Abs(record.Scale - made.ScaleFactor) > 1e-3 * made.ScaleFactor || Math.Abs(record.Lifetime - made.LifetimeFactor) > 1e-3 * made.LifetimeFactor)) { instance.ApplyFactors(record.Speed, record.Scale, record.Lifetime); AdoptFactorFixes++; } var real = new Vector3(record.Direction.X, record.Direction.Y, record.Direction.Z);
            if (real.sqrMagnitude > 1e-12f && entry.SpawnHeading.sqrMagnitude > 1e-12f)
            {
                var turn = Quaternion.FromToRotation(entry.SpawnHeading, real.normalized);
                if (Quaternion.Angle(Quaternion.identity, turn) > 3f) { AdoptTurns++; if (target != null && instance.TargetEntityValue != target.EntityId.Value) AdoptTargetChanges++; instance.ApplyCorrection(instance.Position, turn * instance.Forward, target); return true; }
            }
            if (target != null && instance.TargetEntityValue != target.EntityId.Value) { AdoptTargetChanges++; instance.SetTarget(target); }
            return true;
        }
        return endedPredictions.Remove(key);
    }
    // The cast was refused (or taken back): its guessed projectiles go.
    public void DiscardPredicted(long castKey)
    {
        if (castKey == 0) return;
        foreach (var entry in entries.Values.ToArray())
            if (entry.Predicted && !entry.Adopted && entry.Instance != null && entry.Instance.Launch != null && entry.Instance.Launch.CastKey == castKey) End(new PNProjectileHandle(this, entry.Id));
        foreach (var key in endedPredictions.Keys.Where(x => x.Item1 == castKey).ToArray()) endedPredictions.Remove(key);
    }
    // EN: A hit the shooter's machine reported (plan 4.4a): finds the authority's projectile named by the cast key and the index, and lets it take the hit. False when there is none (ended, stale, not this machine's).
    // VI: Cu trung may nguoi ban bao: tim vien that theo cast key + so thu tu va de no nhan. False khi khong co.
    public bool AcceptReportedHit(long castKey, int index, PNStat target, float tolerance)
    {
        if (castKey == 0 || target == null || Session == null || !Session.HasAuthority) return false;
        foreach (var entry in entries.Values)
        {
            var instance = entry.Instance;
            if (entry.Predicted || entry.Replica || instance == null || !instance.IsRunning || instance.Launch == null || instance.Launch.CastKey != castKey || instance.CastIndex != index) continue;
            return instance.TakeReportedHit(target, tolerance);
        }
        return false;
    }
    // The sink and the source the service talks to: copies on a client, live state for the baseline on the authority.
    private sealed class ObjectBridge : IPNObjectSyncSink, IPNObjectSyncSource
    {
        private readonly PNProjectileManager owner;
        public ObjectBridge(PNProjectileManager manager) { owner = manager; }
        public bool Spawned(PNObjectRecord spawn, double elapsed)
        { var receipt = owner.CreateReplica(spawn, elapsed); return receipt.Status == PNProjectileStatus.Created || receipt.Status == PNProjectileStatus.Pending; }
        public void Retargeted(PNObjectRecord retarget)
        {
            if (!owner.objectEntries.TryGetValue(retarget.Id, out var handleId) || !owner.entries.TryGetValue(handleId, out var entry) || entry.Instance == null) return;
            PNStat target = null;
            if (retarget.Target != 0 && owner.Session != null && owner.Session.Entities.TryGet(new EveEntityId(retarget.Target), out var loader)) target = loader.GetComponent<PNStat>();
            // The shooter's own guess is ahead of the real object in time: a record's place would pull it back, so it only follows a change of target.
            if (entry.Adopted) { if (target != null && entry.Instance.TargetEntityValue != target.EntityId.Value) entry.Instance.SetTarget(target); return; }
            entry.Instance.ApplyCorrection(new Vector3(retarget.Position.X, retarget.Position.Y, retarget.Position.Z), new Vector3(retarget.Direction.X, retarget.Direction.Y, retarget.Direction.Z), target);
            // The record's place is where the object was at its stamp: the copy flies the delay since then, or it would trail the real object by the latency every time it is corrected.
            double delay = owner.objectSync == null ? 0d : Math.Min(PNObjectSyncLimits.MaxCatchUpSeconds, Math.Max(0d, owner.objectSync.Now - retarget.Stamp));
            if (delay > 1e-4d) entry.Instance.FastForward(delay, owner.MaxSubSteps);
        }
        public void Ended(PNObjectRecord end)
        {
            if (!owner.objectEntries.TryGetValue(end.Id, out var handleId)) return;
            var reason = Enum.IsDefined(typeof(PNProjectileEndReason), (int)end.Reason) ? (PNProjectileEndReason)end.Reason : PNProjectileEndReason.Requested;
            owner.End(new PNProjectileHandle(owner, handleId), reason);
        }
        public bool TryCapture(long id, out PNObjectVector position, out PNObjectVector direction, out long target, out float age)
        {
            position = default; direction = default; target = 0; age = 0f;
            if (!owner.objectEntries.TryGetValue(id, out var handleId) || !owner.entries.TryGetValue(handleId, out var entry) || entry.Instance == null || !entry.Instance.IsRunning) return false;
            position = Vec(entry.Instance.Position); direction = Vec(entry.Instance.Forward); target = entry.Instance.TargetEntityValue; age = Mathf.Min(entry.Instance.Age, (float)PNObjectSyncLimits.MaxReplicaSeconds); return true;
        }
    }

    private PoolEntry AddPool(PNProjectileDefinition definition, Scene scene)
    {
        var template = definition.CreatePoolTemplate();
        // Clones inherit the layer. Children an implementation adds at run time set their own layer (see the projectile README).
        foreach (var child in template.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = PNLayers.Projectile;
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
    // EN: Gameplay clearing (a shield or clear-screen skill): honours the definition's destroyable flag unless `bypass`, returning false (nothing ends) when it
    //     is blocked. Lifecycle ends (expiry, hit, owner/scene/world/session) go through End and are never blocked.
    // VI: Xoá do gameplay (khiên, skill xoá đạn): tôn trọng cờ destroyable của định nghĩa trừ khi `bypass`, trả false (không kết thúc gì) khi bị chặn.
    //     Kết thúc vòng đời (hết hạn, trúng, owner/scene/world/session) đi qua End và không bao giờ bị chặn.
    public bool Destroy(PNProjectileHandle handle, PNProjectileEndReason reason = PNProjectileEndReason.Destroyed, PNCombatSource source = null, bool bypass = false)
    {
        if (!Contains(handle)) return false;
        if (!bypass && entries.TryGetValue(handle.InstanceId, out var entry) && !entry.Instance.Destroyable) return false;
        return End(handle, reason, source);
    }
    public bool End(PNProjectileHandle handle, PNProjectileEndReason reason = PNProjectileEndReason.Requested, PNCombatSource source = null)
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
        if (objectSync != null) { if (objectSync.IsAuthority) objectSync.Flush(); else objectSync.Tick(); }
        if (endedPredictions.Count != 0) { double now = PredictionNow; foreach (var key in endedPredictions.Where(x => x.Value < now).Select(x => x.Key).ToArray()) endedPredictions.Remove(key); }
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
                    if (entry.Predicted && !entry.Adopted && PredictionNow - entry.CreatedAt > OrphanSeconds) { End(new PNProjectileHandle(this, entry.Id)); continue; }
                    if (entry.World != null && !entry.World.CanExecute) continue;
                    if (!entry.Instance.IsRunning) { Return(entry); continue; }
                    if (!entry.Instance.isActiveAndEnabled) { End(new PNProjectileHandle(this, entry.Id), PNProjectileEndReason.Disabled); continue; }
                    object key = (object)entry.World ?? NoWorld;
                    PNTargetCandidates candidates;
                    if (refreshed.Add(key))
                    using (EveHotPathMetrics.Measure("projectile.candidates"))
                        // Membership at a limited rate, boxes every tick.
                        candidates = Targets.Tick(entry.World, gameDelta);
                    else candidates = Targets.Peek(entry.World);
                    var physics = (entry.World != null ? entry.World.PhysicsOwner : entry.Scene).GetPhysicsScene();
                    entry.Instance.Tick(gameDelta, MaxSubSteps, candidates, physics);
                    if (entry.ObjectId != 0 && !entry.Replica) AuditSync(entry);
                }
                catch (Exception error) { Debug.LogException(error, this); End(new PNProjectileHandle(this, entry.Id), PNProjectileEndReason.RuntimeError); }
            }
        });
    }
    private void SafeStop(Entry entry, PNProjectileEndReason reason)
    {
        try { if (entry.Instance != null) entry.Instance.Stop(reason); } catch (Exception error) { if (entry.Instance != null) entry.Instance.CleanupFailed = true; Debug.LogException(error, this); }
        try { NotifyEnded(entry, reason); } catch (Exception error) { Debug.LogException(error, this); }
        if (entry.Predicted && entry.Instance != null) PredictionEnded(entry);
    }
    private void PredictionEnded(Entry entry)
    {
        var key = (entry.Instance.Launch?.CastKey ?? 0L, entry.Instance.CastIndex);
        if (predictions.TryGetValue(key, out var id) && id == entry.Id) predictions.Remove(key);
        // A guess that ended before its record came is remembered, so the record is taken as already shown.
        if (!entry.Adopted && key.Item1 != 0) endedPredictions[key] = PredictionNow + EndedPredictionSeconds;
    }
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
        foreach (var entry in entries.Values.Where(x => ReferenceEquals(x.World, world)).ToArray()) { entry.Instance?.Stop(PNProjectileEndReason.SceneUnloaded); NotifyEnded(entry, PNProjectileEndReason.SceneUnloaded); Return(entry); }
        objectSync?.ForgetWorld(PNObjectWorld.Of(world));
        foreach (var pair in pools.Where(x => PNWorldGameplay.ForScene(Session, x.Value.Scene) == world).ToArray())
        {
            if (entries.Values.Any(x => ReferenceEquals(x.Pool, pair.Value))) throw new InvalidOperationException("World projectile cleanup remains pending.");
            foreach (var instance in pair.Value.Instances.Values) instance.DisposePooledResources();
            pair.Value.Pool.Dispose(); DestroyOwned(pair.Value.Template); pools.Remove(pair.Key);
        }
        Targets.ReleaseWorld(world);
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
        order.Clear(); compactNeeded = false; Targets.Clear(); objectEntries.Clear(); predictions.Clear(); endedPredictions.Clear();
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
