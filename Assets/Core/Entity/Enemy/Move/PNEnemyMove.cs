using EveEngine;
using UnityEngine;
#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
#endif

// EN: Extension point for this game's 3D enemy movement; AI behavior belongs to each enemy.
// VI: Điểm kế thừa cho di chuyển 3D của kẻ địch; AI do từng loại địch triển khai.
[DisallowMultipleComponent]
public class PNEnemyMove : PNMove
{
#if UNITY_5_3_OR_NEWER
    [SerializeField] private PNNavigationSettings navigation = new PNNavigationSettings();
    private Pathfinding.Seeker seeker;
    private Guid generation;
    private long nextRequest;
    private PNEnemyNavigationRequest current;
    private PNWorldNavigationLease navigationWorld;
    private bool workerPending;
    private readonly HashSet<Guid> suspensions = new HashSet<Guid>();
    private RaycastHit[] obstacles = new RaycastHit[32];
    private List<PNEnemyLookIntent> looks;
    private long nextLook;
    public Vector2 LookDirection { get; private set; } = Vector2.right;
    public PNEnemyNavigationRequest NavigationRequest => current;
    public int NavigationSuspensions => suspensions.Count;
    public bool HasPendingNavigationPath => workerPending;
    public bool NavigationReady => PNWorldNavigation.Find(GetComponent<EveEntityIdentity>()?.World) != null;
    public void ConfigureNavigation(PNNavigationSettings value)
    {
        if (loadContext != null) throw new InvalidOperationException("Configure navigation before loading.");
        value?.Validate(); navigation = value?.Copy() ?? throw new ArgumentNullException(nameof(value));
    }
    public override void Load(EveEntityLoadContext context)
    {
        navigation.Validate(); generation = Guid.NewGuid(); base.Load(context); base.CanJump = false;
        seeker = GetComponent<Pathfinding.Seeker>() ?? gameObject.AddComponent<Pathfinding.Seeker>();
        if (GetComponent<Pathfinding.FunnelModifier>() == null) gameObject.AddComponent<Pathfinding.FunnelModifier>();
        PoseRelocated += Relocated;
    }
    public PNEnemyNavigationRequest GoTo(Vector3 goal, float stopDistance, Func<bool> ownerIsCurrent = null)
        => GoToCore(goal, stopDistance, null, ownerIsCurrent);
    public PNEnemyNavigationRequest GoTo(Vector3 goal, PNLocomotionOptions options, Func<bool> ownerIsCurrent = null)
    { if (options == null) throw new ArgumentNullException(nameof(options)); return GoToCore(goal, options.StopDistance, options, ownerIsCurrent); }
    private PNEnemyNavigationRequest GoToCore(Vector3 goal, float stopDistance, PNLocomotionOptions options, Func<bool> ownerIsCurrent)
    {
        PNNavigationSettings.Position(goal);
        PNAgentGeometry.Nonnegative(stopDistance, nameof(stopDistance));
        if (!IsReady || !isActiveAndEnabled || !loadContext.CanExecute || !loadContext.Session.HasAuthority || NetworkRole != PNMoveNetworkRole.Local)
            throw new InvalidOperationException("Navigation requires a Ready authoritative enemy.");
        var world = PNWorldNavigation.Find(GetComponent<EveEntityIdentity>()?.World);
        if (world == null || world.ProfileId != navigation.ProfileId) throw new InvalidOperationException("Enemy navigation profile is not prepared for this world.");
        var capsule = GetComponent<CapsuleCollider>();
        if (capsule != null && (Mathf.Max(capsule.bounds.extents.x, capsule.bounds.extents.z) > world.Radius + .001f || capsule.bounds.size.y > world.Height + .001f))
            throw new InvalidOperationException("Enemy body exceeds the baked navigation profile.");
        if (ownerIsCurrent != null && !ownerIsCurrent()) throw new InvalidOperationException("Stale navigation owner.");
        var previous = current;
        var request = new PNEnemyNavigationRequest(this, checked(++nextRequest), generation, goal, stopDistance, options, ownerIsCurrent);
        request.FollowHeading = LastMoveDirection;
        current = request; navigationWorld = world; request.PreviousBody = Body.position; request.RepathElapsed = navigation.RepathInterval;
        previous?.Finish(PNEnemyNavigationStatus.Cancelled, PNEnemyNavigationEndReason.Replaced); return request;
    }
    public bool UpdateGoal(PNEnemyNavigationRequest request, Vector3 goal)
    {
        PNNavigationSettings.Position(goal); if (!Owns(request)) return false;
        // Compare to the path's sampled goal, not last frame's goal: small continuous
        // target updates must accumulate until they cross the repath threshold.
        if ((goal - request.SubmittedGoal).sqrMagnitude >= navigation.GoalThreshold * navigation.GoalThreshold) request.NeedsPath = true;
        request.Goal = goal; return true;
    }
    public bool CancelNavigation(PNEnemyNavigationRequest request, PNEnemyNavigationEndReason reason = PNEnemyNavigationEndReason.OwnerCancelled)
    {
        if (request == null || !ReferenceEquals(request, current) || request.LoadGeneration != generation || request.IsTerminal) return false;
        current = null; SetMoveDirection(Vector2.zero); request.Finish(PNEnemyNavigationStatus.Cancelled, reason); return true;
    }
    public IDisposable SuspendNavigation()
    {
        var token = Guid.NewGuid(); suspensions.Add(token); SetMoveDirection(Vector2.zero);
        if (CanWriteMotion && !HasActiveMotion && Mode == EveMoveMode.None) base.ApplyVelocity(Vector2.zero);
        return new NavigationSuspension(this, token, generation);
    }
    private sealed class NavigationSuspension : IDisposable
    {
        private PNEnemyMove owner; private readonly Guid token, generation;
        public NavigationSuspension(PNEnemyMove owner, Guid token, Guid generation) { this.owner = owner; this.token = token; this.generation = generation; }
        public void Dispose()
        {
            var value = owner; owner = null;
            if (value == null || value.generation != generation || !value.suspensions.Remove(token)) return;
            if (value.suspensions.Count == 0) value.Relocated();
        }
    }
    private bool Owns(PNEnemyNavigationRequest request) => request != null && ReferenceEquals(request, current) &&
        request.LoadGeneration == generation && !request.IsTerminal && (request.OwnerIsCurrent == null || request.OwnerIsCurrent());
    public override bool TryJump() => false;
    public override bool TryFaceSkill(Vector2 direction)
    { bool applied = base.TryFaceSkill(direction); if (applied && direction.sqrMagnitude > 0) LookDirection = direction.normalized; return applied; }
    public override void FaceDirection(Vector2 direction)
    { base.FaceDirection(direction); if (CanFlip && direction.sqrMagnitude > 0) LookDirection = direction.normalized; }
    public PNEnemyLookIntent LookAt(Vector3 point, PNEnemyLookPriority priority = PNEnemyLookPriority.Task, Func<bool> ownerIsCurrent = null)
    {
        PNNavigationSettings.Position(point);
        if (!Enum.IsDefined(typeof(PNEnemyLookPriority), priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        if (loadContext == null || !loadContext.CanExecute || !loadContext.Session.HasAuthority || !isActiveAndEnabled ||
            !IsReady || (ownerIsCurrent != null && !ownerIsCurrent())) throw new InvalidOperationException("Look requires an active authoritative owner.");
        var intent = new PNEnemyLookIntent(this, generation, checked(++nextLook), point, priority, ownerIsCurrent);
        (looks ??= new List<PNEnemyLookIntent>()).Add(intent); return intent;
    }
    internal bool UpdateLook(PNEnemyLookIntent intent, Vector3 point)
    {
        if (intent.Generation != generation || looks == null || !looks.Contains(intent) ||
            (intent.IsCurrent != null && !intent.IsCurrent()) || loadContext?.CanExecute != true) return false;
        intent.Point = point; return true;
    }
    internal void ReleaseLook(PNEnemyLookIntent intent) { looks?.Remove(intent); }
    private void ApplyLook()
    {
        if (loadContext?.CanExecute != true || !CanWriteMotion) return;
        if (looks == null || looks.Count == 0) { if (MoveDirection.sqrMagnitude > 0) LookDirection = MoveDirection.normalized; return; }
        PNEnemyLookIntent best = null;
        for (int i = looks.Count - 1; i >= 0; i--)
        {
            var item = looks[i];
            if (item.Generation != generation || (item.IsCurrent != null && !item.IsCurrent())) { looks.RemoveAt(i); continue; }
            if (best == null || item.Priority > best.Priority || item.Priority == best.Priority && item.Sequence > best.Sequence) best = item;
        }
        if (best == null) { if (MoveDirection.sqrMagnitude > 0) LookDirection = MoveDirection.normalized; return; }
        var direction = PNAgentGeometry.Planar(best.Point - Body.position);
        if (direction.sqrMagnitude < .000001f) return;
        if (best.Priority == PNEnemyLookPriority.Skill)
        { if (TryFaceSkill(direction)) LookDirection = direction.normalized; }
        else if (CanFlip) { FaceDirection(direction); LookDirection = direction.normalized; }
    }
    private void Relocated()
    {
        if (current == null || current.IsTerminal) return;
        current.Route = null; current.NeedsPath = true; current.RouteGeneration = Guid.NewGuid();
        current.FollowSpeed = current.ExpectedProgress = 0; current.FollowHeading = LastMoveDirection;
        current.ProgressElapsed = current.ProgressDistance = 0; current.PreviousBody = Body != null ? Body.position : transform.position;
    }
    protected override void FixedUpdate() { FollowPath(); base.FixedUpdate(); ApplyLook(); }
    private void FollowPath()
    {
        var request = current; if (request == null || request.IsTerminal) return;
        var world = GetComponent<EveEntityIdentity>()?.World;
        if (!Owns(request) || navigationWorld == null || !navigationWorld.IsCurrent || !ReferenceEquals(world, navigationWorld.World))
        { CancelCurrent(PNEnemyNavigationEndReason.WorldChanged); return; }
        if (loadContext == null || !loadContext.CanUse || !loadContext.Session.HasAuthority || !isActiveAndEnabled) { CancelCurrent(PNEnemyNavigationEndReason.Unloaded); return; }
        var stat = GetComponent<PNStat>();
        if (stat != null && stat.IsDead) { CancelCurrent(PNEnemyNavigationEndReason.Died); return; }
        if (!IsReady || !CanMove || !CanWriteMotion || HasActiveMotion || suspensions.Count != 0 || Mode != EveMoveMode.None || CurrentMoveSpeed <= 0)
        {
            SetMoveDirection(Vector2.zero); request.Status = PNEnemyNavigationStatus.Suspended;
            request.FollowSpeed = request.ExpectedProgress = 0;
            request.PreviousBody = Body.position; request.ProgressElapsed = request.ProgressDistance = 0; return;
        }
        if (request.Status == PNEnemyNavigationStatus.Suspended) Relocated();
        float delta = Time.fixedDeltaTime * MovementTimeScale; request.RepathElapsed += Time.fixedDeltaTime;
        if (request.NeedsPath && !workerPending && request.RepathElapsed >= navigation.RepathInterval)
        {
            workerPending = request.Pending = true; request.SubmittedGoal = request.Goal; request.RouteGeneration = Guid.NewGuid();
            var routeGeneration = request.RouteGeneration; var lease = navigationWorld;
            bool started = lease.Start(request, seeker, Body.position, request.Goal, navigation.EndpointTolerance, (route, error) =>
            {
                workerPending = request.Pending = false;
                if (!Owns(request) || !ReferenceEquals(navigationWorld, lease) || !lease.IsCurrent || request.RouteGeneration != routeGeneration) return;
                if (error != null) { SetMoveDirection(Vector2.zero); request.Finish(PNEnemyNavigationStatus.NoPath, diagnostic: error); return; }
                request.Route = route; request.Corner = 1;
                request.NeedsPath = (request.Goal - request.SubmittedGoal).sqrMagnitude >= navigation.GoalThreshold * navigation.GoalThreshold;
                request.Status = PNEnemyNavigationStatus.Following;
            });
            if (started) { request.PathAttempts++; request.RepathElapsed = 0; request.NeedsPath = false; } else workerPending = request.Pending = false;
        }
        if (request.IsTerminal) return;
        var position = Body.position; var planarGoal = new Vector2(request.Goal.x - position.x, request.Goal.z - position.z);
        if (request.Route != null && request.Corner == request.Route.Length - 1 && !request.NeedsPath && planarGoal.magnitude <= request.StopDistance + navigation.ArrivalEpsilon)
        { SetMoveDirection(Vector2.zero); request.Finish(PNEnemyNavigationStatus.Arrived); return; }
        if (request.Route == null) { SetMoveDirection(Vector2.zero); request.Status = PNEnemyNavigationStatus.PendingPath; return; }
        while (request.Corner < request.Route.Length - 1 && PlanarDistance(position, request.Route[request.Corner]) <= navigation.CornerTolerance) request.Corner++;
        var corner = request.Route[request.Corner]; var direction = new Vector2(corner.x - position.x, corner.z - position.z);
        float distance = direction.magnitude;
        float travel = request.Corner == request.Route.Length - 1 ? Mathf.Min(distance, Mathf.Max(0, planarGoal.magnitude - request.StopDistance)) : distance;
        var unit = distance > .0001f ? direction / distance : Vector2.zero; float step = CurrentMoveSpeed * delta;
        float speed = CurrentMoveSpeed; bool turning = false;
        if (request.Options != null && !request.Options.IsImmediate)
        {
            var options = request.Options;
            float remaining = distance;
            for (int i = request.Corner + 1; i < request.Route.Length; i++) remaining += PlanarDistance(request.Route[i - 1], request.Route[i]);
            remaining = Mathf.Max(0, remaining - request.StopDistance);
            float maximum = CurrentMoveSpeed * options.SpeedFraction; float desired = maximum;
            if (options.MaximumSpeedEnabled) desired = maximum = Mathf.Min(maximum, options.MaximumSpeed);
            if (options.SlowDistance > 0) desired *= Mathf.Clamp01(remaining / options.SlowDistance);
            if (options.BrakingEnabled) desired = Mathf.Min(desired, Mathf.Sqrt(2 * options.Braking * remaining));
            if (options.TurnLimitEnabled && request.FollowHeading.sqrMagnitude > 0 && unit.sqrMagnitude > 0)
            {
                float angle = Vector2.SignedAngle(request.FollowHeading, unit);
                float turn = Mathf.Clamp(angle, -options.TurnRate * delta, options.TurnRate * delta) * Mathf.Deg2Rad;
                var old = request.FollowHeading;
                unit = new Vector2(old.x * Mathf.Cos(turn) - old.y * Mathf.Sin(turn), old.x * Mathf.Sin(turn) + old.y * Mathf.Cos(turn)).normalized;
                turning = Vector2.Angle(unit, direction) > 75;
                if (turning) desired = 0;
                else
                {
                    // A turn-limited agent must reduce its turning radius near a corner/goal.
                    // Otherwise constant-speed steering can circle a nearby destination forever.
                    float alignment = Mathf.Max(0, Vector2.Dot(unit, direction.normalized));
                    desired = Mathf.Min(desired * alignment, distance * options.TurnRate * Mathf.Deg2Rad * .8f);
                }
            }
            request.FollowHeading = unit;
            request.FollowSpeed = Mathf.Min(request.FollowSpeed, maximum);
            bool rising = desired > request.FollowSpeed;
            bool limited = rising ? options.AccelerationEnabled : options.BrakingEnabled;
            speed = limited ? Mathf.MoveTowards(request.FollowSpeed, desired, (rising ? options.Acceleration : options.Braking) * delta) : desired;
            if (turning) speed = 0;
            request.FollowSpeed = speed; step = speed * delta;
        }
        if (Blocked(unit, Mathf.Min(step, travel) + navigation.CollisionSkin))
        { SetMoveDirection(Vector2.zero); request.FollowSpeed = 0; request.NeedsPath = true; }
        else SetMoveDirection(unit * (CurrentMoveSpeed * delta > 0 ? Mathf.Min(step, travel) / (CurrentMoveSpeed * delta) : 0));
        request.ExpectedProgress += Mathf.Min(step, travel);
        if (turning || request.Options?.SpeedFraction == 0) request.ProgressElapsed = request.ProgressDistance = request.ExpectedProgress = 0;
        request.ProgressDistance += PlanarDistance(position, request.PreviousBody); request.ProgressElapsed += delta; request.PreviousBody = position;
        if (request.ProgressElapsed >= navigation.StuckWindow)
        {
            if (request.ProgressDistance < Mathf.Min(navigation.MinimumProgress, request.ExpectedProgress * .25f))
            {
                if (request.Retries++ >= navigation.StuckRetries) { SetMoveDirection(Vector2.zero); request.Finish(PNEnemyNavigationStatus.Stuck); }
                else Relocated();
            }
            request.ProgressElapsed = request.ProgressDistance = request.ExpectedProgress = 0;
        }
    }
    private bool Blocked(Vector2 direction, float distance)
    {
        var capsule = GetComponent<CapsuleCollider>(); if (capsule == null || distance <= 0 || direction == Vector2.zero) return false;
        var bounds = capsule.bounds; float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * .95f;
        float half = Mathf.Max(0, bounds.extents.y - radius - navigation.CollisionSkin);
        int count = PNAgentPhysicsQuery.CapsuleCast(gameObject.scene.GetPhysicsScene(), bounds.center + Vector3.up * half, bounds.center - Vector3.up * half,
            radius, new Vector3(direction.x, 0, direction.y), ref obstacles, distance, ~(1 << 13));
        if (count == obstacles.Length) return true;
        for (int i = 0; i < count; i++)
            if (obstacles[i].collider != null && !obstacles[i].collider.transform.IsChildOf(transform) && obstacles[i].collider.GetComponentInParent<PNStat>() == null &&
                Vector3.Dot(new Vector3(direction.x, 0, direction.y), obstacles[i].normal) < -.001f) return true;
        return false;
    }
    private static float PlanarDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    private void CancelCurrent(PNEnemyNavigationEndReason reason)
    { var request = current; current = null; SetMoveDirection(Vector2.zero); request?.Finish(PNEnemyNavigationStatus.Cancelled, reason); }
    protected override void OnDisable()
    { CancelCurrent(PNEnemyNavigationEndReason.Disabled); suspensions.Clear(); looks?.Clear(); base.OnDisable(); }
    public override void Unload()
    {
        CancelCurrent(PNEnemyNavigationEndReason.Unloaded); suspensions.Clear(); PoseRelocated -= Relocated;
        generation = Guid.NewGuid(); navigationWorld = null; base.Unload();
        looks = null; LookDirection = Vector2.right;
    }
#endif
}
