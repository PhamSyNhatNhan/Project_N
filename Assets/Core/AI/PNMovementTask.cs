#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

public enum PNMovementTaskKind { Approach, MaintainRange, Pursue, Flee, Orbit, Patrol, Guard, FollowOffset, Search, CombatPosition }
public enum PNAgentTaskStatus { Running, Holding, Succeeded, Failed, Cancelled }

public sealed class PNMovementTaskSettings
{
    public PNMovementTaskKind Kind { get; }
    public PNLocomotionOptions Locomotion { get; }
    public PNDistanceMetric DistanceMetric { get; }
    public float MinimumRange { get; }
    public float MaximumRange { get; }
    public float ResumeMargin { get; }
    public float Timeout { get; }
    public float RetryDelay { get; }
    public int Retries { get; }
    public float PredictionTime { get; }
    public float PredictionDistance { get; }
    public float OrbitDegrees { get; }
    public float OrbitAngularSpeed { get; }
    public float Dwell { get; }
    public float Radius { get; }
    public float LeashRadius { get; }
    public bool Loop { get; }
    public Vector3 Offset { get; }
    public bool OffsetInHeadingFrame { get; }
    private readonly Vector3[] points;
    internal int PointCount => points.Length;
    internal Vector3 Point(int index) => points[index];
    public PNMovementTaskSettings(PNMovementTaskKind kind, PNLocomotionOptions locomotion, float minimumRange = 5, float maximumRange = 7,
        float resumeMargin = .4f, float timeout = 30, float retryDelay = .5f, int retries = 2, float predictionTime = .5f,
        float predictionDistance = 3, float orbitDegrees = 20, float dwell = .5f, float radius = 3, bool loop = true,
        Vector3? offset = null, Vector3[] patrolPoints = null, float leashRadius = 0, PNDistanceMetric distanceMetric = PNDistanceMetric.CenterXZ,
        float orbitAngularSpeed = 90, bool offsetInHeadingFrame = false)
    {
        if (!Enum.IsDefined(typeof(PNMovementTaskKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(PNDistanceMetric), distanceMetric)) throw new ArgumentOutOfRangeException(nameof(distanceMetric)); DistanceMetric = distanceMetric;
        Locomotion = locomotion ?? throw new ArgumentNullException(nameof(locomotion));
        PNAgentGeometry.Nonnegative(minimumRange, nameof(minimumRange)); PNNavigationSettings.Positive(maximumRange, nameof(maximumRange));
        if (maximumRange < minimumRange || retries < 0 || retries > 16) throw new ArgumentOutOfRangeException();
        PNNavigationSettings.Positive(resumeMargin, nameof(resumeMargin)); PNNavigationSettings.Positive(timeout, nameof(timeout));
        PNNavigationSettings.Positive(retryDelay, nameof(retryDelay)); PNAgentGeometry.Nonnegative(predictionTime, nameof(predictionTime));
        PNAgentGeometry.Nonnegative(predictionDistance, nameof(predictionDistance)); PNNavigationSettings.Positive(orbitDegrees, nameof(orbitDegrees));
        if (orbitDegrees > 45) throw new ArgumentOutOfRangeException(nameof(orbitDegrees));
        PNAgentGeometry.Nonnegative(dwell, nameof(dwell)); PNNavigationSettings.Positive(radius, nameof(radius));
        PNNavigationSettings.Positive(orbitAngularSpeed, nameof(orbitAngularSpeed)); OrbitAngularSpeed = orbitAngularSpeed;
        OffsetInHeadingFrame = offsetInHeadingFrame;
        Offset = offset ?? Vector3.zero; PNNavigationSettings.Position(Offset);
        PNAgentGeometry.Nonnegative(leashRadius, nameof(leashRadius)); LeashRadius = leashRadius;
        points = patrolPoints != null ? (Vector3[])patrolPoints.Clone() : Array.Empty<Vector3>();
        foreach (var p in points) PNNavigationSettings.Position(p);
        Kind = kind; MinimumRange = minimumRange; MaximumRange = maximumRange; ResumeMargin = resumeMargin;
        Timeout = timeout; RetryDelay = retryDelay; Retries = retries; PredictionTime = predictionTime;
        PredictionDistance = predictionDistance; OrbitDegrees = orbitDegrees; Dwell = dwell; Radius = radius; Loop = loop;
    }
}

// Tasks choose goals, never write pose, advance clocks, or execute a Skill.
public sealed class PNMovementTask : IDisposable
{
    private readonly IPNAgentContext agent;
    private readonly PNMovementTaskSettings settings;
    private readonly System.Random random;
    private PNActorHandle target;
    private PNPerceptionRecord observation;
    private Vector3 home, lastKnown, selectedGoal;
    private PNEnemyNavigationRequest walk;
    private PNAgentPointQuery query;
    private float elapsed, retryAt, dwellUntil;
    private int failures, waypoint, orbitSign = 1;
    private bool disposed, holding;
    private Vector2 leaderHeading = Vector2.right;
    public PNAgentTaskStatus Status { get; private set; }
    public string Diagnostic { get; private set; }
    public bool IsTerminal => Status == PNAgentTaskStatus.Succeeded || Status == PNAgentTaskStatus.Failed || Status == PNAgentTaskStatus.Cancelled;
    public PNMovementTask(IPNAgentContext agent, PNMovementTaskSettings settings, Vector3 home, PNActorHandle target = null,
        PNPerceptionRecord observation = null, int randomSeed = 1)
    {
        this.agent = agent ?? throw new ArgumentNullException(nameof(agent)); this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        PNNavigationSettings.Position(home); this.home = lastKnown = home; this.target = target; this.observation = observation; random = new System.Random(randomSeed);
        if (observation != null) lastKnown = observation.LastPosition;
    }
    public void UpdateObservation(PNPerceptionRecord value)
    {
        if (value == null || target == null || !ReferenceEquals(value.Actor.Load, target.Load)) throw new ArgumentException("Observation must describe this task target.");
        observation = value; lastKnown = value.LastPosition;
    }
    public void UpdateLeaderHeading(Vector2 heading)
    {
        PNNavigationSettings.Position(new Vector3(heading.x, 0, heading.y));
        if (heading.sqrMagnitude < .000001f) throw new ArgumentException("Leader heading cannot be zero."); leaderHeading = heading.normalized;
    }
    public PNAgentTaskStatus Tick(float delta)
    {
        PNAgentGeometry.Nonnegative(delta, nameof(delta));
        if (disposed || IsTerminal) return Status;
        if (!agent.IsCurrent) { Cancel(); return Status; }
        if (!agent.CanExecute || delta == 0 || walk?.Status == PNEnemyNavigationStatus.Suspended) return Status;
        elapsed += delta;
        if (elapsed >= settings.Timeout) { Finish(PNAgentTaskStatus.Failed, "Timeout"); return Status; }
        if (elapsed < retryAt || elapsed < dwellUntil) return Status;
        var self = agent.Self.Position;
        if (settings.LeashRadius > 0 && PNAgentGeometry.CenterDistance(self, home) > settings.LeashRadius)
        { Finish(PNAgentTaskStatus.Failed, "LeashExceeded"); return Status; }
        Vector3 goal = home;
        if (target != null)
        {
            if (!target.TryResolve(out _) || !ReferenceEquals(target.World, agent.Self.World)) { Finish(PNAgentTaskStatus.Failed, "TargetRetired"); return Status; }
            goal = observation != null ? observation.LastPosition : target.Position;
        }
        else if (NeedsTarget(settings.Kind)) { Finish(PNAgentTaskStatus.Failed, "MissingTarget"); return Status; }
        float radii = target != null && settings.DistanceMetric == PNDistanceMetric.FootprintGapXZ ? agent.Self.FootprintRadius + target.FootprintRadius : 0;
        float distance = Mathf.Max(0, PNAgentGeometry.CenterDistance(self, goal) - radii);
        bool needsPoint = false, requireSight = false;
        float minimum = 0, maximum = 0;
        switch (settings.Kind)
        {
            case PNMovementTaskKind.Approach:
                if (holding && distance <= settings.Locomotion.StopDistance + settings.ResumeMargin) { Status = PNAgentTaskStatus.Holding; return Status; }
                holding = false; break;
            case PNMovementTaskKind.MaintainRange:
                float margin = holding ? settings.ResumeMargin : 0;
                if (distance >= Mathf.Max(0, settings.MinimumRange - margin) && distance <= settings.MaximumRange + margin)
                { Hold(); return Status; }
                holding = false; needsPoint = true; minimum = settings.MinimumRange; maximum = settings.MaximumRange; break;
            case PNMovementTaskKind.Pursue:
                if (observation == null || !observation.Visible) { Finish(PNAgentTaskStatus.Failed, "ObservationStale"); return Status; }
                if (holding && distance <= settings.Locomotion.StopDistance + settings.ResumeMargin) { Status = PNAgentTaskStatus.Holding; return Status; }
                holding = false;
                goal += Vector3.ClampMagnitude(observation.ObservedVelocity * settings.PredictionTime, settings.PredictionDistance); break;
            case PNMovementTaskKind.Flee:
                if (distance >= settings.MaximumRange - .04f) { Finish(PNAgentTaskStatus.Succeeded); return Status; }
                if (observation != null) goal += Vector3.ClampMagnitude(observation.ObservedVelocity * settings.PredictionTime, settings.PredictionDistance);
                needsPoint = true; minimum = settings.MaximumRange; maximum = settings.MaximumRange + settings.Radius; break;
            case PNMovementTaskKind.Orbit:
                if (walk == null && query == null)
                {
                    var offset = PNAgentGeometry.Planar(self - goal); float angle = Mathf.Atan2(offset.y, offset.x) + orbitSign * settings.OrbitDegrees * Mathf.Deg2Rad;
                    goal += new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * settings.Radius;
                }
                else goal = selectedGoal;
                needsPoint = true; break;
            case PNMovementTaskKind.Patrol:
                if (settings.PointCount > 0) goal = settings.Point(waypoint % settings.PointCount);
                else if (walk == null && query == null)
                { float angle = (float)random.NextDouble() * Mathf.PI * 2; goal = home + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * settings.Radius * (float)random.NextDouble(); }
                else goal = selectedGoal;
                needsPoint = true; break;
            case PNMovementTaskKind.Guard:
                goal = home; if (PNAgentGeometry.CenterDistance(self, home) <= settings.Radius) { Hold(); return Status; } break;
            case PNMovementTaskKind.FollowOffset:
                var heading = settings.OffsetInHeadingFrame ? leaderHeading : Vector2.right;
                goal += new Vector3(heading.x * settings.Offset.x - heading.y * settings.Offset.z, 0, heading.y * settings.Offset.x + heading.x * settings.Offset.z);
                if (PNAgentGeometry.CenterDistance(self, goal) <= settings.Locomotion.StopDistance + (holding ? settings.ResumeMargin : 0)) { Hold(); return Status; }
                holding = false; break;
            case PNMovementTaskKind.Search:
                goal = lastKnown; needsPoint = true;
                if (waypoint > 0) { minimum = 0; maximum = settings.Radius; }
                if (observation?.Visible == true) { Finish(PNAgentTaskStatus.Succeeded); return Status; } break;
            case PNMovementTaskKind.CombatPosition:
                needsPoint = requireSight = true; minimum = settings.MinimumRange; maximum = settings.MaximumRange; break;
        }
        Status = PNAgentTaskStatus.Running;
        if (walk != null)
        {
            if (!walk.IsTerminal)
            { if (!needsPoint) agent.UpdateGoal(walk, goal); return Status; }
            if (walk.Status != PNEnemyNavigationStatus.Arrived) { Retry(walk.Diagnostic ?? walk.Status.ToString()); return Status; }
            walk = null; failures = 0;
            if ((settings.Kind == PNMovementTaskKind.Approach || settings.Kind == PNMovementTaskKind.Pursue) && !settings.Loop) { Finish(PNAgentTaskStatus.Succeeded); return Status; }
            if (settings.Kind == PNMovementTaskKind.Approach || settings.Kind == PNMovementTaskKind.Pursue || settings.Kind == PNMovementTaskKind.FollowOffset) { Hold(); return Status; }
            if (settings.Kind == PNMovementTaskKind.CombatPosition) { Finish(PNAgentTaskStatus.Succeeded); return Status; }
            if (settings.Kind == PNMovementTaskKind.Patrol || settings.Kind == PNMovementTaskKind.Search)
            { waypoint++; dwellUntil = elapsed + settings.Dwell; if (!settings.Loop && waypoint >= Mathf.Max(1, settings.PointCount)) Finish(PNAgentTaskStatus.Succeeded); return Status; }
            if (settings.Kind == PNMovementTaskKind.Orbit) { selectedGoal = default; return Status; }
        }
        if (needsPoint)
        {
            if (query == null)
            {
                selectedGoal = goal;
                bool band = settings.Kind == PNMovementTaskKind.MaintainRange || settings.Kind == PNMovementTaskKind.Flee || settings.Kind == PNMovementTaskKind.CombatPosition;
                query = agent.FindPoint(new PNPointQuerySettings(goal, minimum + (band ? radii : 0), maximum + (band ? radii : 0), requireSight ? target : null,
                    candidates: maximum == 0 ? 1 : 16, preferredDirection: PNAgentGeometry.Planar(self - goal)));
            }
            query.Tick();
            if (!query.IsTerminal) return Status;
            if (query.Status != PNPointQueryStatus.Success) { Retry(query.Diagnostic ?? query.Status.ToString()); return Status; }
            goal = query.Point; query.Dispose(); query = null;
        }
        bool exactPoint = settings.Kind == PNMovementTaskKind.MaintainRange || settings.Kind == PNMovementTaskKind.CombatPosition ||
            settings.Kind == PNMovementTaskKind.Flee || settings.Kind == PNMovementTaskKind.Orbit;
        var locomotion = exactPoint ? settings.Locomotion.WithStopDistance(0) : radii > 0 && (settings.Kind == PNMovementTaskKind.Approach || settings.Kind == PNMovementTaskKind.Pursue)
            ? settings.Locomotion.WithStopDistance(settings.Locomotion.StopDistance + radii) : settings.Locomotion;
        if (settings.Kind == PNMovementTaskKind.Orbit) locomotion = locomotion.WithMaximumSpeed(settings.Radius * settings.OrbitAngularSpeed * Mathf.Deg2Rad);
        walk = agent.GoTo(goal, locomotion); return Status;
    }
    private static bool NeedsTarget(PNMovementTaskKind kind) => kind == PNMovementTaskKind.Approach || kind == PNMovementTaskKind.MaintainRange ||
        kind == PNMovementTaskKind.Pursue || kind == PNMovementTaskKind.Flee || kind == PNMovementTaskKind.Orbit || kind == PNMovementTaskKind.FollowOffset || kind == PNMovementTaskKind.CombatPosition;
    private void Retry(string reason)
    {
        Release(); if (++failures > settings.Retries) { Finish(PNAgentTaskStatus.Failed, reason); return; }
        orbitSign = -orbitSign; retryAt = elapsed + settings.RetryDelay; Diagnostic = reason;
    }
    private void Hold() { Release(); holding = true; Status = PNAgentTaskStatus.Holding; }
    private void Release() { query?.Dispose(); query = null; if (walk != null) agent.Cancel(walk); walk = null; }
    private void Finish(PNAgentTaskStatus result, string reason = null) { Release(); Status = result; Diagnostic = reason; }
    public void Cancel() { if (!IsTerminal) Finish(PNAgentTaskStatus.Cancelled); }
    public void Dispose() { if (disposed) return; Cancel(); disposed = true; }
}
#endif
