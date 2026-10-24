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
public abstract class PNProjectileInstance : MonoBehaviour, EveIPoolable
{
    private readonly PNProjectileContacts contacts = new PNProjectileContacts();
    private Vector3 position, heading = Vector3.forward;
    private PNProjectileTarget target;
    private bool ending, first;
    public PNProjectileHandle Handle { get; private set; }
    public bool IsRunning { get; private set; }
    public EveWorldContext World { get; private set; }
    public Scene Scene { get; private set; }
    public PNProjectileEndReason? EndReason { get; private set; }
    public PNProjectileLaunch Launch { get; private set; }
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
    // Nearest live target of this projectile's world within the radius on the launch target layers (from the manager candidate list).
    protected PNStat FindNearestTarget(float radius) => Manager == null ? null : Manager.FindNearest(World, position, radius, Launch.TargetMask);
    public void SetTarget(PNStat value) => target = new PNProjectileTarget(value);

    internal void Bind(PNProjectileManager manager, PNProjectileHandle handle, PNProjectileDefinition definition, PNProjectileLaunch launch, PNProjectileSpawn spawn, EveWorldContext world)
    {
        Manager = manager; Handle = handle; Definition = definition; Launch = launch; World = world; Scene = spawn.Scene; Clock = GetComponent<PNProjectileClock>();
        EndReason = null; CleanupFailed = false; ending = false; first = true; Age = 0; Distance = 0; TickIndex = 0; HitCount = 0; hitContext = null; admission = null; Velocity = default; External = default; DestroyedBy = null;
        position = spawn.Position; heading = spawn.Direction; target = new PNProjectileTarget(spawn.Target); contacts.Clear();
        transform.position = position; transform.rotation = Quaternion.LookRotation(heading);
        IsRunning = true; Initialize(spawn);
    }
    internal void Stop(PNProjectileEndReason reason)
    {
        if (!IsRunning && !CleanupFailed) return;
        IsRunning = false; EndReason = reason; CleanupFailed = true;
        OnEnd(reason); Cleanup(); CleanupFailed = false;
    }

    // One manager tick: advance the clock (always, so modifiers whose owner vanished are reclaimed even at scale 0), then move.
    internal void Tick(float gameDelta, int maxSubSteps, PNProjectileTargets candidates, PhysicsScene physics)
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
        for (int i = 0; i < steps && IsRunning && !ending; i++) SubStep(step, candidates, physics);
        if (IsRunning && !ending && IsExpired) Finish(PNProjectileEndReason.Expired);
        transform.SetPositionAndRotation(position, Quaternion.LookRotation(heading));
    }
    private void SubStep(float step, PNProjectileTargets candidates, PhysicsScene physics)
    {
        var from = position;
        using (EveHotPathMetrics.Measure("projectile.move")) Move(step);
        External += Gravity * step;
        var total = Velocity + External;
        if (total.sqrMagnitude > 1e-10f) heading = total.normalized;
        position += total * step; Age += step; Distance += (position - from).magnitude;
        contacts.Clear();
        Detect(from, position, candidates, physics);
        if (!contacts.IsEmpty) OnContacts(contacts);
        first = false;
    }
    private void Detect(Vector3 from, Vector3 to, PNProjectileTargets candidates, PhysicsScene physics)
    {
        var segment = to - from; float length = segment.magnitude, segment2 = length * length;
        int environment = EnvironmentMask; Vector3 right = default, up = default, forward = default; bool framed = false;
        if (environment != 0 && length > 1e-5f && physics.IsValid())
        {
            using (EveHotPathMetrics.Measure("projectile.env-cast"))
            {
                PNProjectileGeometry.Frame(heading, out right, out up, out forward); framed = true;
                if (physics.BoxCast(from, HalfExtents, segment / length, out var hit, Quaternion.LookRotation(forward, up), length, environment, QueryTriggerInteraction.Ignore))
                { contacts.HasEnvironment = true; contacts.Environment = new PNProjectileContact(null, hit.point, hit.normal, Mathf.Clamp01(hit.distance / length)); }
            }
        }
        if (candidates == null || candidates.Count == 0) return;
        using (EveHotPathMetrics.Measure("projectile.targets"))
        {
            int mask = Launch.TargetMask; float radius = HalfExtents.magnitude; var items = candidates.RawItems;
            float sx = segment.x, sy = segment.y, sz = segment.z;
            // Only candidates near the step (spatial hash on XZ), then a scalar coarse test: the segment must pass within the two bounding radii.
            int n = candidates.Query(Mathf.Min(from.x, to.x), Mathf.Min(from.z, to.z), Mathf.Max(from.x, to.x), Mathf.Max(from.z, to.z), radius, out var indices);
            for (int k = 0; k < n; k++)
            {
                ref var candidate = ref items[indices[k]];
                if (((mask >> candidate.Layer) & 1) == 0) continue;
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
        }
        if (contacts.targets.Count > 1) contacts.targets.Sort((x, y) => x.Fraction.CompareTo(y.Fraction));
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
        if (target == null || !IsRunning || ending || Manager == null) return null;
        if (!target.IsReady || !target.gameObject.activeInHierarchy || PNProjectileTargets.IsDead(target) || !PNWorldGameplay.CanExecute(target) ||
            !ReferenceEquals(target.GetComponent<EveEntityIdentity>()?.World, World)) return null;
        // EN: A Player/Enemy projectile passes through targets that are not hostile to its captured team (not counted as a hit); Neutral keeps the layer-mask rule.
        // VI: Đạn phe Player/Enemy xuyên qua mục tiêu không thù địch với phe đã chụp (không tính là một lần trúng); Neutral giữ luật theo layer mask.
        if (Launch.Team != PNCombatTeam.Neutral && !target.IsHostileToTeam(Launch.Team)) return null;
        var result = new PNProjectileHitResult { Target = target, HitIndex = HitCount };
        HitCount++;
        if (Launch.Damage != null)
        {
            var snapshot = Launch.Source.Snapshot ?? throw new InvalidOperationException("Projectile damage needs a source with a combat snapshot.");
            result.Damage = target.ReceiveDamage(snapshot.CreateDamage(Launch.Damage));
        }
        if (Launch.HitPlan != null)
        {
            if (hitContext == null) { hitContext = new PNProjectileHitContext(Launch.HitPlan.EntrySkillId, Launch.Source, World, Manager.Session.Registry); admission = new PNSkillHitAdmission(); }
            result.Effects = Launch.HitPlan.Execute(hitContext, target, contact.Point, heading, result.HitIndex, admission);
        }
        return result;
    }

    // ---- pooling (the pool calls these) -----------------------------------------------------------------------------------
    public virtual void OnPoolCreated() { }
    public virtual void OnPoolGet(EveEasyPooling pool) { }
    // Explicit destruction hook so retained resources are released in native Edit fixtures too. Safe to call repeatedly.
    public virtual void DisposePooledResources() { }
    public virtual void OnPoolRelease()
    {
        Manager = null; World = null; Definition = null; Launch = null; Handle = default; EndReason = null; IsRunning = false; ending = false; DestroyedBy = null; target = default; hitContext = null; admission = null;
        if (CleanupFailed) throw new InvalidOperationException("Faulted projectile cleanup; the pool must discard this instance.");
    }
    protected virtual void OnDisable() { if (IsRunning) Finish(PNProjectileEndReason.Disabled); }
    protected virtual void OnDestroy() { if (IsRunning) Finish(PNProjectileEndReason.Disabled); DisposePooledResources(); }
}
#endif
