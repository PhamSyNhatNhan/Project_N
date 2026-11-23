#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Base of every projectile implementation (same shape as PNAreaInstance). The author writes one movement function (Move) and
//     one contact function (OnContacts); everything else has a default. The base integrates position from Velocity + External,
//     splits a tick into sub-steps, finds contacts and keeps no collider on the projectile. Pooled by the manager.
// VI: Lớp nền mọi implementation đạn (khuôn PNAreaInstance). Tác giả viết hàm chuyển động (Move) và hàm va chạm (OnContacts).
public abstract class PNProjectileInstance : MonoBehaviour, EveIPoolable, IPNTimeTarget
{
    // A running projectile is in its world's PNTimeTargets, so any time effect finds it by shape and layer with the other clocks that are not entities.
    EveTimeScale IPNTimeTarget.TimeClock => Clock;
    int IPNTimeTarget.TimeLayer => PNLayers.Projectile;
    Vector3 IPNTimeTarget.TimePosition => position;
    bool IPNTimeTarget.TimeActive => IsRunning && Clock != null;
    private PNTimeTargets.Entry timeEntry;
    private readonly PNProjectileContacts contacts = new PNProjectileContacts();
    private Vector3 position, heading = Vector3.forward;
    private PNProjectileTarget target;
    private bool ending, first;
    // EN: Shooter arming: a new projectile ignores its own shooter until its box has once been outside the shooter's box (or after
    //     ShooterGraceSeconds of its clock); from then on the shooter is an ordinary target when its layer is among the launch layers.
    // VI: Đạn mới bỏ qua người bắn cho tới khi hộp đạn đã ra khỏi hộp người bắn một lần (hoặc sau ShooterGraceSeconds); sau đó người bắn
    //     là mục tiêu bình thường nếu layer của họ nằm trong các layer của lần bắn.
    public const float ShooterGraceSeconds = 2f;
    private bool armed;
    public bool Armed => armed;
    private PNTargetCandidates tickCandidates;
    public PNProjectileHandle Handle { get; private set; }
    public bool IsRunning { get; private set; }
    public EveWorldContext World { get; private set; }
    public Scene Scene { get; private set; }
    public PNProjectileEndReason? EndReason { get; private set; }
    public PNProjectileLaunch Launch { get; private set; }
    // EN: A view-only copy built from a sync record (spawned-object sync): it flies the way the authority flies the object but finds no contacts, deals no damage and ends only when told to
    //     (the record's end, or its own lifetime as a leak guard). Implementations need no code for it; this class and the hooks below do the filtering.
    // VI: Bản hình dựng từ bản ghi đồng bộ: bay như authority nhưng không tìm va chạm, không gây sát thương, chỉ kết thúc khi được báo (hoặc hết tuổi thọ).
    public bool IsReplica => Launch != null && Launch.IsReplica;
    // Seconds this object is expected to live (0 = unknown), for the sync record's leak guard. An implementation that knows it overrides this.
    public virtual float LifeHint => 0f;
    // EN: The id of this object in the sync records (0 = none, offline), and the random numbers seeded from it: the authority's object and its view-only copy draw the same sequence (plan 4.7).
    //     Use this, never System.Random or UnityEngine.Random, for anything that changes the flight.
    // VI: Id cua vat the trong ban ghi dong bo (0 = khong co) va ngau nhien gieo theo id do: ban that va ban hinh ra cung day so. Moi thu doi duong bay phai dung cai nay.
    public long ObjectSeed { get; private set; }
    // Index of this projectile in its volley (see PNProjectileSpawn.CastIndex).
    public int CastIndex { get; private set; }
    // The shooter's guess of a projectile: it hits nothing for real, it reports.
    public bool IsPrediction => Launch != null && Launch.IsPrediction;
    private PNSkillRandom random;
    public PNSkillRandom Random => random ?? (random = new PNSkillRandom(PNSkillRandom.Seed(ObjectSeed, 0)));
    // EN: True when a view-only copy re-flying this implementation from the record reproduces the authority's path (it reads nothing but the record, its own clock and Random). The authority then
    //     audits the copy loosely; false (the default) makes it send position corrections often. Declaring true is an optimisation; PNObjectSyncEditorChecks checks every declaration (plan 4.3a).
    // VI: True khi ban hinh bay lai tu ban ghi tai tao dung duong bay cua authority. Khai true chi la toi uu; check Edit Mode kiem moi khai bao.
    public virtual bool SyncDeterministic => false;
    // Entity id of the current target (0 = none), for sync records.
    public long TargetEntityValue => target.HasValue ? target.Id.Value : 0L;
    public PNProjectileClock Clock { get; private set; }
    // Set when another system destroyed the projectile with a known attribution (for example a skill that clears projectiles).
    public PNCombatSource DestroyedBy { get; internal set; }
    public Vector3 Position => position;
    public Vector3 Forward => heading;
    // Motion velocity set by Move, plus an external velocity (gravity) added by the base; position += (Velocity + External) * dt.
    public Vector3 Velocity { get; protected set; }
    public Vector3 External { get; protected set; }
    // Half extents of the body box in its own frame (x right, y up, z forward), already multiplied by the launch scale factor.
    public Vector3 HalfExtents { get; protected set; } = Vector3.one * .1f;
    public float Age { get; private set; }
    // Counts this projectile's manager ticks (not sub-steps), for work that should happen once per tick.
    protected int TickIndex { get; private set; }
    public float Distance { get; private set; }
    internal bool CleanupFailed { get; set; }
    protected PNProjectileManager Manager { get; private set; }
    protected PNProjectileDefinition Definition { get; private set; }
    // The definition's destroyable flag (true while no definition is bound).
    public bool Destroyable => Definition == null || Definition.Destroyable;
    protected Vector3 Heading { get => heading; set { if (value.sqrMagnitude > 1e-12f) heading = value.normalized; } }

    // ---- hooks ----------------------------------------------------------------------------------------------------------
    // Called once on bind (object still inactive): read the definition payload and the launch factors, set Velocity/HalfExtents.
    protected abstract void Initialize(PNProjectileSpawn spawn);
    // Update Velocity (and optionally Position through SetPosition) for one sub-step of `dt` seconds of this projectile's own clock.
    protected abstract void Move(float dt);
    // Contacts found in the last sub-step: targets sorted by approximate entry order, and an environment hit. Call Finish to end.
    protected abstract void OnContacts(PNProjectileContacts contacts);
    protected virtual void OnEnd(PNProjectileEndReason reason) { }
    // EN: Called once per impact on every machine that shows this projectile (real, guess or copy; see PNImpact). The default does nothing: the look of an impact belongs to the implementation.
    //     A hit of a piercing projectile calls it for each hit of the real object and of its guess; a copy is told of the last impact only (its End record).
    // VI: Goi mot lan moi va cham tren moi may hien vien dan nay. Mac dinh khong lam gi: hinh dang va cham thuoc ve lop cai dat.
    protected virtual void OnImpact(PNImpact impact) { }
    // Impacts reported by this projectile since it was bound (checks and diagnostics).
    public int ImpactCount { get; private set; }
    private Vector3 wallPoint, wallNormal, endPoint, endNormal; private bool hasWall, hasEndHint, recordHint; private long endTargetId;
    private PNImpactOrigin ImpactOrigin => Launch != null && Launch.IsReplica ? PNImpactOrigin.Copy : Launch != null && Launch.IsPrediction ? PNImpactOrigin.Guess : PNImpactOrigin.Real;
    private void RaiseImpact(PNImpactKind kind, Vector3 point, Vector3 normal, PNStat target, long targetId, int hitIndex)
    {
        ImpactCount++; Manager?.NoteImpact(ImpactOrigin, kind); endPoint = point; endNormal = normal; endTargetId = targetId; hasEndHint = true;
        try { OnImpact(new PNImpact(kind, ImpactOrigin, point, normal, heading, target, targetId, hitIndex)); } catch (Exception error) { Debug.LogException(error, this); }
    }
    // The End record's own account of the impact (a copy has no contacts of its own).
    internal void NoteEndHint(Vector3 point, Vector3 normal, long targetId) { endPoint = point; endNormal = normal; endTargetId = targetId; hasEndHint = true; recordHint = true; }
    // What the authority tells copies about the end: where, against what, and which entity (0 = none).
    internal bool TryGetEndImpact(out Vector3 point, out Vector3 normal, out long targetId) { point = endPoint; normal = endNormal; targetId = endTargetId; return hasEndHint; }
    protected virtual void Cleanup() { }
    // Layers treated as walls/ground (0 = none). Targets come from the launch mask.
    protected virtual int EnvironmentMask => 0;
    protected virtual Vector3 Gravity => Vector3.zero;
    protected virtual bool IsExpired => false;
    // An implementation may cap its own sub-step count below the manager cap.
    protected virtual int SubStepLimit => int.MaxValue;
    protected virtual float StepDistance(float dt) => (Velocity + External).magnitude * dt;

    protected void SetPosition(Vector3 value) { if (!PNProjectileSpawn.Finite(value)) throw new ArgumentException("Finite position required."); position = value; }
    public bool Finish(PNProjectileEndReason reason = PNProjectileEndReason.Requested)
    {
        if (!IsRunning || ending || Manager == null) return false;
        ending = true; return Manager.End(Handle, reason);
    }
    public bool TryGetTarget(out PNStat result) => target.TryGet(World, out result);
    // Best live target of this projectile's world within the radius (3D, from the box center) on the launch target layers, ranked by
    // `rule` (PNTargetSelector over the manager's candidate list). The shooter is not excluded: it is a candidate when its layer is targeted.
    protected PNStat FindTarget(float radius, PNTargetRule rule)
    {
        if (Manager == null || Launch == null) return null;
        var list = tickCandidates ?? Manager.Targets.Peek(World);
        return PNTargetSelector.Select(list, new PNTargetQuery { Origin = position, Range = radius, LayerMask = Launch.TargetLayers.Mask, Rule = rule });
    }
    // Nearest live target (FindTarget with the Nearest rule).
    protected PNStat FindNearestTarget(float radius) => FindTarget(radius, PNTargetRule.Autotarget(PNTargetMode.Nearest));
    public void SetTarget(PNStat value)
    {
        var changed = target.Id != (value != null ? value.EntityId : default);
        target = new PNProjectileTarget(value);
        if (changed && IsRunning && Manager != null && !IsReplica) Manager.NotifyRetarget(this);
    }
    // A sync record corrects a copy once: position, heading and target (the target is the record's, never searched for by the copy).
    internal void ApplyCorrection(Vector3 newPosition, Vector3 newHeading, PNStat newTarget)
    {
        if (!IsRunning || !PNProjectileSpawn.Finite(newPosition) || !PNProjectileSpawn.Finite(newHeading) || newHeading.sqrMagnitude < 1e-12f) return;
        position = newPosition; heading = newHeading.normalized; target = new PNProjectileTarget(newTarget); OnCorrected(heading);
        transform.SetPositionAndRotation(position, Quaternion.LookRotation(heading));
    }
    // EN: The authority's factors differ from the ones a guess was made with (a projectile_* modifier the shooter's machine did not know): the guess takes the real ones, and the implementation rescales what it derived from them.
    // VI: He so that cua authority khac he so luc doan: vien doan nhan he so that va lop cai dat tinh lai phan suy ra tu chung.
    internal void ApplyFactors(double speed, double scale, double lifetime)
    {
        if (!IsRunning || Launch == null || !(speed > 0d) || !(scale > 0d) || !(lifetime > 0d) || double.IsInfinity(speed + scale + lifetime)) return;
        double rs = speed / Launch.SpeedFactor, rc = scale / Launch.ScaleFactor, rl = lifetime / Launch.LifetimeFactor;
        Launch = Launch.WithFactors(speed, scale, lifetime); OnFactorsCorrected(rs, rc, rl);
    }
    protected virtual void OnFactorsCorrected(double speedRatio, double scaleRatio, double lifetimeRatio) { }
    // The implementation follows a correction: set the direction it flies along (a directional projectile keeps its own).
    protected virtual void OnCorrected(Vector3 newHeading) { }
    // A copy built from a record that is already `age` seconds old: its age and distance start there (so its lifetime ends when the real one does).
    internal void SeedProgress(float seconds) { if (seconds <= 0f) return; Age = seconds; Distance = seconds * (Velocity + External).magnitude; }
    // Lives `seconds` of its own clock at once (a copy built late), in small steps so the motion matches the authority's; finds no contacts.
    internal void FastForward(double seconds, int maxSubSteps)
    {
        float remaining = (float)Math.Min(seconds, PNObjectSyncLimits.MaxReplicaSeconds);
        while (remaining > 1e-4f && IsRunning && !ending) { float step = Mathf.Min(remaining, 1f / 30f); Tick(step, maxSubSteps, null, default); remaining -= step; }
    }

    internal void Bind(PNProjectileManager manager, PNProjectileHandle handle, PNProjectileDefinition definition, PNProjectileLaunch launch, PNProjectileSpawn spawn, EveWorldContext world, long objectSeed = 0)
    {
        ObjectSeed = objectSeed; random = null; CastIndex = spawn.CastIndex;
        Manager = manager; Handle = handle; Definition = definition; Launch = launch; World = world; Scene = spawn.Scene; Clock = GetComponent<PNProjectileClock>();
        EndReason = null; CleanupFailed = false; ending = false; first = true; armed = false; Age = 0; Distance = 0; TickIndex = 0; HitCount = 0; ImpactCount = 0; hasWall = hasEndHint = recordHint = false; endTargetId = 0; hitContext = null; admission = null; Velocity = default; External = default; DestroyedBy = null;
        position = spawn.Position; heading = spawn.Direction; target = new PNProjectileTarget(spawn.Target); contacts.Clear();
        transform.position = position; transform.rotation = Quaternion.LookRotation(heading);
        IsRunning = true; timeEntry = PNTimeTargets.For(world).Register(this); Initialize(spawn);
    }
    internal void Stop(PNProjectileEndReason reason)
    {
        if (!IsRunning && !CleanupFailed) return;
        IsRunning = false; EndReason = reason; CleanupFailed = true;
        if (timeEntry != null) { PNTimeTargets.Unregister(timeEntry); timeEntry = null; }
        EmitEndImpact(reason);
        OnEnd(reason); Cleanup(); CleanupFailed = false;
    }
    // The impact an end stands for, once: a wall, a hit nobody reported yet (a copy, or a guess the authority's End closes), the end of its life, or being cleared. A real hit already raised its own.
    private void EmitEndImpact(PNProjectileEndReason reason)
    {
        var at = position; var normal = -heading; long id = 0;
        switch (reason)
        {
            case PNProjectileEndReason.Environment:
                if (recordHint) { at = endPoint; normal = endNormal; }
                else if (hasWall) { at = wallPoint; normal = wallNormal; }
                RaiseImpact(PNImpactKind.Wall, at, normal, null, 0, -1); break;
            case PNProjectileEndReason.Hit:
                if (HitCount > 0) break;
                if (hasEndHint) { at = endPoint; normal = endNormal; id = endTargetId; }
                RaiseImpact(PNImpactKind.Target, at, normal, Resolve(id), id, 0); break;
            case PNProjectileEndReason.Expired: RaiseImpact(PNImpactKind.Expired, at, default, null, 0, -1); break;
            case PNProjectileEndReason.Destroyed: RaiseImpact(PNImpactKind.Cancelled, at, default, null, 0, -1); break;
            default: hasEndHint = false; break;
        }
    }
    private PNStat Resolve(long id)
    {
        if (id == 0 || Manager == null || Manager.Session == null || !Manager.Session.Entities.TryGet(new EveEntityId(id), out var loader) || loader == null) return null;
        return loader.GetComponent<PNStat>();
    }

    // One manager tick: advance the clock (always, so modifiers whose owner vanished are reclaimed even at scale 0), then move.
    internal void Tick(float gameDelta, int maxSubSteps, PNTargetCandidates candidates, PhysicsScene physics)
    {
        if (!IsRunning || ending) return;
        // An untouched clock (no modifier) is exactly scale 1 and has nothing to expire, so the common case skips it.
        TickIndex++;
        bool clocked = Clock.ModifierCount > 0;
        if (clocked) using (EveHotPathMetrics.Measure("projectile.clock")) Clock.Advance(gameDelta);
        float dt = clocked ? gameDelta * Clock.Scale : gameDelta;
        if (dt <= 0f) return;
        float smallest = Mathf.Max(Mathf.Min(HalfExtents.x, Mathf.Min(HalfExtents.y, HalfExtents.z)), 1e-3f);
        int steps = Mathf.Clamp(Mathf.CeilToInt(StepDistance(dt) / smallest), 1, Mathf.Max(1, Mathf.Min(maxSubSteps, SubStepLimit)));
        float step = dt / steps;
        tickCandidates = candidates;
        try { for (int i = 0; i < steps && IsRunning && !ending; i++) SubStep(step, candidates, physics); }
        finally { tickCandidates = null; }
        if (IsRunning && !ending && IsExpired) Finish(PNProjectileEndReason.Expired);
        transform.SetPositionAndRotation(position, Quaternion.LookRotation(heading));
    }
    private void SubStep(float step, PNTargetCandidates candidates, PhysicsScene physics)
    {
        var from = position;
        using (EveHotPathMetrics.Measure("projectile.move")) Move(step);
        External += Gravity * step;
        var total = Velocity + External;
        if (total.sqrMagnitude > 1e-10f) heading = total.normalized;
        position += total * step; Age += step; Distance += (position - from).magnitude;
        contacts.Clear();
        if (!IsReplica)
        {
            Detect(from, position, candidates, physics);
            if (contacts.HasEnvironment) { hasWall = true; wallPoint = contacts.Environment.Point; wallNormal = contacts.Environment.Normal; }
            if (!contacts.IsEmpty) OnContacts(contacts);
        }
        else ClaimDetect(from, position, candidates, physics);
        first = false;
    }
    private void Detect(Vector3 from, Vector3 to, PNTargetCandidates candidates, PhysicsScene physics, long only = 0)
    {
        var segment = to - from; float length = segment.magnitude, segment2 = length * length;
        int environment = only != 0 ? 0 : EnvironmentMask; Vector3 right = default, up = default, forward = default; bool framed = false;
        if (environment != 0 && length > 1e-5f && physics.IsValid())
        {
            using (EveHotPathMetrics.Measure("projectile.env-cast"))
            {
                PNProjectileGeometry.Frame(heading, out right, out up, out forward); framed = true;
                if (physics.BoxCast(from, HalfExtents, segment / length, out var hit, Quaternion.LookRotation(forward, up), length, environment, QueryTriggerInteraction.Ignore))
                { contacts.HasEnvironment = true; contacts.Environment = new PNProjectileContact(null, hit.point, hit.normal, Mathf.Clamp01(hit.distance / length)); }
            }
        }
        // The authority's projectile of a predicting shooter leaves target contacts to the shooter's reports (plan 4.4a); walls above stay its own.
        if (Launch.ShooterDecides && only == 0) { armed = true; return; }
        if (candidates == null || candidates.Count == 0) { armed = true; return; }
        using (EveHotPathMetrics.Measure("projectile.targets"))
        {
            int mask = Launch.TargetLayers.Mask; float radius = HalfExtents.magnitude; var items = candidates.RawItems;
            // The shooter is skipped for the whole step while unarmed; it becomes a target only in steps that start armed.
            bool skipShooter = !armed && only == 0; long shooter = Launch.SourceId.Value; var claimPolicy = only == 0 ? Manager?.HurtClaimPolicy : null;
            float sx = segment.x, sy = segment.y, sz = segment.z;
            // Only candidates near the step (spatial hash on XZ), then a scalar coarse test: the segment must pass within the two bounding radii.
            int n = candidates.Query(Mathf.Min(from.x, to.x), Mathf.Min(from.z, to.z), Mathf.Max(from.x, to.x), Mathf.Max(from.z, to.z), radius, out var indices);
            for (int k = 0; k < n; k++)
            {
                ref var candidate = ref items[indices[k]];
                if (((mask >> candidate.Layer) & 1) == 0) continue;
                if (only != 0 && candidate.EntityId != only) continue;
                if (skipShooter && candidate.EntityId == shooter) continue;
                // A player whose machine decides his own hits (plan 4.4c) is left to the claims; the policy takes the decision back when that machine falls silent.
                if (claimPolicy != null && candidate.Stat != null && candidate.Layer == PNLayers.Player && claimPolicy.ClaimsDecide(candidate.Stat)) continue;
                float rx = candidate.Center.x - from.x, ry = candidate.Center.y - from.y, rz = candidate.Center.z - from.z;
                float fraction = segment2 < 1e-12f ? 0f : (rx * sx + ry * sy + rz * sz) / segment2;
                fraction = fraction < 0f ? 0f : fraction > 1f ? 1f : fraction;
                float dx = rx - sx * fraction, dy = ry - sy * fraction, dz = rz - sz * fraction, reach = radius + candidate.Radius;
                if (dx * dx + dy * dy + dz * dz > reach * reach) continue;
                if (!framed) { PNProjectileGeometry.Frame(heading, out right, out up, out forward); framed = true; }
                // Sample the end of the step (and the start on the first step, for a projectile spawned inside a target).
                Vector3 at = to;
                bool overlap = PNProjectileGeometry.ObbOverlapsAabb(to, right, up, forward, HalfExtents, candidate.Center, candidate.Extents);
                if (!overlap && first) { at = from; overlap = PNProjectileGeometry.ObbOverlapsAabb(from, right, up, forward, HalfExtents, candidate.Center, candidate.Extents); }
                if (!overlap) continue;
                // Contact point: where the target box is nearest to the step's start (its entry side); a projectile that began inside it
                // reports the sampled center and a normal against its heading.
                var point = PNProjectileGeometry.ClosestPoint(from, candidate.Center, candidate.Extents);
                var normal = from - point;
                if (normal.sqrMagnitude < 1e-10f) { point = at; normal = -forward; } else normal = normal.normalized;
                contacts.targets.Add(new PNProjectileContact(candidate.Stat, point, normal, fraction));
            }
            if (skipShooter && only == 0) armed = Age >= ShooterGraceSeconds || !InsideShooter(to, heading, candidates);
        }
        if (contacts.targets.Count > 1) contacts.targets.Sort((x, y) => x.Fraction.CompareTo(y.Fraction));
    }

    // ---- hit claims (plan 4.4c) ----
    // A copy tests only the player of this machine: the machine of a player decides the hits on him. The first contact raises his early phase and sends the claim; the real object's hit event answers it (or the early phase is cancelled).
    private System.Collections.Generic.HashSet<long> claimedHere;
    private long claimTarget; // set while the authority applies a claimed hit: names the hit by the victim (PNHurtClaimLimits.ClaimKeyIndex)
    private void ClaimDetect(Vector3 from, Vector3 to, PNTargetCandidates candidates, PhysicsScene physics)
    {
        var manager = Manager; if (manager == null || Launch == null || Launch.TargetLayers.IsEmpty || candidates == null || candidates.Count == 0) return;
        var sink = manager.ClaimSinkFor(out var me); if (sink == null || me == null || !me.IsReady || PNTargetCandidates.IsDead(me)) return;
        if (((Launch.TargetLayers.Mask >> me.gameObject.layer) & 1) == 0 || (claimedHere != null && claimedHere.Contains(me.EntityId.Value))) return;
        contacts.Clear(); Detect(from, to, candidates, physics, me.EntityId.Value);
        if (contacts.targets.Count == 0) return;
        var contact = contacts.targets[0]; contacts.Clear();
        long id = manager.SyncIdOf(this); if (id <= 0) return;
        if (claimedHere == null) claimedHere = new System.Collections.Generic.HashSet<long>(); claimedHere.Add(me.EntityId.Value);
        int hit = Mathf.Min(claimedHere.Count - 1, PNHurtClaimLimits.MaxHit);
        var key = HitKey(PNHurtClaimLimits.ClaimKeyIndex(me.EntityId.Value)); if (key != null) PNCombatEvents.RaiseEarly(me.EntityId.Value, key, true, contact.Point.x, contact.Point.y, contact.Point.z);
        if (sink.SendClaim(PNObjectKind.Projectile, id, hit)) manager.NoteClaimSent();
    }
    // A claim the authority took from the machine of `target`: runs the implementation's own contact rule for it (hit limit, Finish). `tolerance` is the slack the claim's trip gets over the object's reach.
    internal bool TakeClaimedHit(PNStat target, float tolerance)
    {
        if (!IsRunning || ending || Manager == null || Launch == null || Launch.IsReplica || Launch.IsPrediction || target == null) return false;
        if (((Launch.TargetLayers.Mask >> target.gameObject.layer) & 1) == 0) return false;
        float reach = HalfExtents.magnitude + Mathf.Max(0f, tolerance) + (Velocity + External).magnitude * ReportLagSeconds + 1f;
        if ((target.transform.position - position).magnitude > reach) return false;
        int before = HitCount; contacts.Clear(); claimTarget = target.EntityId.Value;
        contacts.targets.Add(new PNProjectileContact(target, target.transform.position, -heading, 0f));
        try { OnContacts(contacts); } finally { contacts.Clear(); claimTarget = 0; }
        return HitCount > before;
    }

    // True while this projectile's box at `at` overlaps its shooter's box (false when the shooter is no candidate: dead, unloaded, no box).
    private bool InsideShooter(Vector3 at, Vector3 direction, PNTargetCandidates candidates)
    {
        long shooter = Launch.SourceId.Value; var items = candidates.RawItems;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (items[i].EntityId != shooter) continue;
            PNProjectileGeometry.Frame(direction, out var right, out var up, out var forward);
            return PNProjectileGeometry.ObbOverlapsAabb(at, right, up, forward, HalfExtents, items[i].Center, items[i].Extents);
        }
        return false;
    }

    // EN: What names this projectile's hits across machines: the cast key and index of a cast projectile, else "o" + its sync id (an enemy's or a summon's). A hit key is this prefix + ":" + the hit index.
    // VI: Ten cua cac cu trung cua vien nay giua cac may: cast key + so thu tu (vien cua cast), khong thi "o" + id dong bo. Khoa cu trung = tien to + ":" + so cu trung.
    internal string HitPrefix => Launch == null ? null : Launch.CastKey != 0 ? Launch.CastKey.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + CastIndex : ObjectSeed != 0 ? "o" + ObjectSeed.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    private string HitKey(int hitIndex) { var prefix = HitPrefix; return prefix == null ? null : prefix + ":" + hitIndex; }
    // A copy is told of a hit of the real projectile by the combat event that carries its key (a piercing projectile's middle hits, which its End record cannot name).
    internal void TellRemoteHit(PNCombatEvent combatEvent, int hitIndex)
    {
        bool claimed = hitIndex >= PNHurtClaimLimits.ClaimKeyBase;
        if (!IsRunning || ending || !IsReplica || (!claimed && hitIndex < HitCount)) return;
        if (!claimed) HitCount = hitIndex + 1;
        RaiseImpact(PNImpactKind.Target, combatEvent.HasPoint ? new Vector3(combatEvent.PointX, combatEvent.PointY, combatEvent.PointZ) : position, combatEvent.HasNormal ? new Vector3(combatEvent.NormalX, combatEvent.NormalY, combatEvent.NormalZ) : -heading, Resolve(combatEvent.TargetId), combatEvent.TargetId, hitIndex);
    }
    // ---- hits -------------------------------------------------------------------------------------------------------------
    private PNProjectileHitContext hitContext;
    private PNSkillHitAdmission admission;
    // Number of hits applied by this projectile so far; also the hit index for PerHit effects.
    public int HitCount { get; private set; }
    // EN: Apply one hit: damage from the launch's prepared template (a fresh crit roll per hit), then the frozen OnHit effects with this
    //     projectile's own admission. Returns null (and applies nothing) when the target is no longer valid. A built-in
    //     implementation always calls this; an implementation that wants to carry buffs must as well.
    // VI: Áp một lần trúng: sát thương từ mẫu tính sẵn (tung crit lại mỗi hit), rồi effect OnHit đã đóng băng với admission riêng của viên.
    protected PNProjectileHitResult ApplyHit(PNProjectileContact contact)
    {
        var target = contact.Target;
        if (target == null || !IsRunning || ending || Manager == null || IsReplica) return null;
        if (!target.IsReady || !target.gameObject.activeInHierarchy || PNTargetCandidates.IsDead(target) || !PNWorldGameplay.CanExecute(target) ||
            !ReferenceEquals(target.GetComponent<EveEntityIdentity>()?.World, World)) return null;
        var result = new PNProjectileHitResult { Target = target, HitIndex = HitCount };
        HitCount++;
        // A guess deals nothing: it tells the authority which target it sees hit, under the projectile's name (cast key + step of its index).
        if (Launch.IsPrediction)
        {
            // The early phase first: the answer can only come after the report goes out.
            if (Launch.CastKey != 0) PNCombatEvents.RaiseEarly(target.EntityId.Value, HitKey(result.HitIndex), true, contact.Point.x, contact.Point.y, contact.Point.z);
            Launch.ReportSink?.ReportHit(Launch.CastKey, target, PNProjectileManager.ReportStepBase + CastIndex);
            RaiseImpact(PNImpactKind.Target, contact.Point, contact.Normal, target, target.EntityId.Value, result.HitIndex);
            return result;
        }
        if (Launch.Damage != null)
        {
            var snapshot = Launch.Source.Snapshot ?? throw new InvalidOperationException("Projectile damage needs a source with a combat snapshot.");
            // The hit is named by cast key, index in the volley and hit index (the shooter's guess names it the same), so the combat event can be matched to the guess's early phase.
            result.Damage = target.ReceiveDamage(snapshot.CreateDamage(Launch.Damage).WithHit(HitKey(claimTarget == target.EntityId.Value ? PNHurtClaimLimits.ClaimKeyIndex(claimTarget) : result.HitIndex), contact.Point.x, contact.Point.y, contact.Point.z, contact.Normal.x, contact.Normal.y, contact.Normal.z));
        }
        if (Launch.HitPlan != null)
        {
            if (hitContext == null) { hitContext = new PNProjectileHitContext(Launch.HitPlan.EntrySkillId, Launch.Source, World, Manager.Session.Registry); admission = new PNSkillHitAdmission(); }
            result.Effects = Launch.HitPlan.Execute(hitContext, target, contact.Point, heading, result.HitIndex, admission);
        }
        RaiseImpact(PNImpactKind.Target, contact.Point, contact.Normal, target, target.EntityId.Value, result.HitIndex);
        return result;
    }

    // EN: A hit the shooter's machine reported for this (authority) projectile: accepted when the target is on a layer the launch targets and near the projectile (its box, `tolerance`, and the flight of the lag
    //     the report took), then the implementation's own contact rule runs for it (hit limit, cooldown, Finish). False when nothing was applied.
    // VI: Cu trung may nguoi ban bao len cho vien that nay: nhan khi muc tieu dung layer va o gan vien (hop, do noi, quang bay trong thoi gian tre), roi quy tac cua implementation chay nhu va cham thuong.
    public const float ReportLagSeconds = .5f;
    internal bool TakeReportedHit(PNStat target, float tolerance)
    {
        if (!IsRunning || ending || Manager == null || Launch == null || !Launch.ShooterDecides || target == null) return false;
        if (((Launch.TargetLayers.Mask >> target.gameObject.layer) & 1) == 0) return false;
        float reach = HalfExtents.magnitude + Mathf.Max(0f, tolerance) + (Velocity + External).magnitude * ReportLagSeconds + 1f;
        if ((target.transform.position - position).magnitude > reach) return false;
        int before = HitCount; contacts.Clear();
        contacts.targets.Add(new PNProjectileContact(target, target.transform.position, -heading, 0f));
        try { OnContacts(contacts); } finally { contacts.Clear(); }
        return HitCount > before;
    }

    // ---- pooling (the pool calls these) -----------------------------------------------------------------------------------
    public virtual void OnPoolCreated() { }
    public virtual void OnPoolGet(EveEasyPooling pool) { }
    // Explicit destruction hook so retained resources are released in native Edit fixtures too. Safe to call repeatedly.
    public virtual void DisposePooledResources() { }
    public virtual void OnPoolRelease()
    {
        if (timeEntry != null) { PNTimeTargets.Unregister(timeEntry); timeEntry = null; }
        claimedHere?.Clear(); Manager = null; World = null; Definition = null; Launch = null; Handle = default; EndReason = null; IsRunning = false; ending = false; DestroyedBy = null; target = default; hitContext = null; admission = null;
        if (CleanupFailed) throw new InvalidOperationException("Faulted projectile cleanup; the pool must discard this instance.");
    }
    protected virtual void OnDisable() { if (IsRunning) Finish(PNProjectileEndReason.Disabled); }
    protected virtual void OnDestroy() { if (IsRunning) Finish(PNProjectileEndReason.Disabled); DisposePooledResources(); }
}
#endif
