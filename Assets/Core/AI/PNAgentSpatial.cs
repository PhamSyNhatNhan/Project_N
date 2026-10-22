#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum PNPointQueryStatus { Pending, Success, NoCandidate, Cancelled }
public sealed class PNPointQuerySettings
{
    public Vector3 Center { get; }
    public float MinimumRadius { get; }
    public float MaximumRadius { get; }
    public float ProjectionTolerance { get; }
    public int Candidates { get; }
    public PNActorHandle SightTarget { get; }
    public Vector2 PreferredDirection { get; }
    public Func<Vector3, float> Score { get; }
    public PNPointQuerySettings(Vector3 center, float minimumRadius, float maximumRadius, PNActorHandle sightTarget = null, int candidates = 16, float projectionTolerance = .5f,
        Vector2? preferredDirection = null, Func<Vector3, float> score = null)
    {
        PNNavigationSettings.Position(center); PNAgentGeometry.Nonnegative(minimumRadius, nameof(minimumRadius));
        PNAgentGeometry.Nonnegative(maximumRadius, nameof(maximumRadius)); PNNavigationSettings.Positive(projectionTolerance, nameof(projectionTolerance));
        if (maximumRadius < minimumRadius || candidates < 1 || candidates > 64) throw new ArgumentOutOfRangeException();
        Center = center; MinimumRadius = minimumRadius; MaximumRadius = maximumRadius; SightTarget = sightTarget;
        Candidates = candidates; ProjectionTolerance = projectionTolerance;
        PreferredDirection = preferredDirection ?? Vector2.right;
        PNNavigationSettings.Position(new Vector3(PreferredDirection.x, 0, PreferredDirection.y)); Score = score;
    }
}

public static class PNAgentSpatial
{
    public static bool HasSight(PNStat source, Vector3 start, PNStat target, Vector3 end, RaycastHit[] buffer, int mask = ~(1 << 13))
        => HasSight(source, start, target, end, ref buffer, mask);
    public static bool HasSight(PNStat source, Vector3 start, PNStat target, Vector3 end, ref RaycastHit[] buffer, int mask = ~(1 << 13))
    {
        PNNavigationSettings.Position(start); PNNavigationSettings.Position(end);
        if (source == null || target == null || !PNWorldGameplay.CanTarget(source, target) || buffer == null || buffer.Length == 0) return false;
        var direction = end - start; if (direction.sqrMagnitude < .000001f) return true;
        int count = PNAgentPhysicsQuery.Raycast(source.gameObject.scene.GetPhysicsScene(), start, direction.normalized, ref buffer, direction.magnitude, mask);
        if (count == buffer.Length) return false;
        for (int i = 0; i < count; i++) if (buffer[i].collider != null && buffer[i].collider.GetComponentInParent<PNStat>() == null) return false;
        return true;
    }
}

// Explicitly ticked by its owning task. Pending backend callbacks always drain the world lease.
public sealed class PNAgentPointQuery : IDisposable
{
    private readonly IPNAgentContext agent;
    private readonly PNPointQuerySettings settings;
    private readonly PNWorldNavigationLease lease;
    private RaycastHit[] sight = new RaycastHit[32];
    private Collider[] clearance = new Collider[32];
    private int candidate;
    private int probeIndex;
    private bool sorted;
    private readonly List<(Vector3 Point, float Score)> points = new List<(Vector3, float)>();
    private bool pending, ticking;
    public PNPointQueryStatus Status { get; private set; }
    public Vector3 Point { get; private set; }
    public string Diagnostic { get; private set; }
    public bool IsTerminal => Status != PNPointQueryStatus.Pending;
    internal PNAgentPointQuery(IPNAgentContext agent, PNPointQuerySettings settings)
    { this.agent = agent; this.settings = settings ?? throw new ArgumentNullException(nameof(settings)); lease = PNWorldNavigation.Find(agent.Self.World) ?? throw new InvalidOperationException("No navigation lease."); }
    public void Tick()
    {
        if (ticking) throw new InvalidOperationException("Point query tick cannot reenter.");
        ticking = true; try { TickCore(); } finally { ticking = false; }
    }
    private void TickCore()
    {
        if (IsTerminal) return;
        if (!agent.IsCurrent || !lease.IsCurrent) { Dispose(); return; }
        if (!agent.CanExecute || pending) return;
        if (!agent.Self.TryResolve(out var actor)) { Dispose(); return; }
        for (int work = 0; work < 4 && candidate < settings.Candidates; work++)
        {
            int index = candidate;
            float angle = Mathf.Atan2(settings.PreferredDirection.y, settings.PreferredDirection.x) + index * 2.399963f;
            float radius = Mathf.Lerp(settings.MinimumRadius, settings.MaximumRadius, settings.Candidates == 1 ? .5f : (float)index / (settings.Candidates - 1));
            var raw = settings.Center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
            if (!lease.TryProject(raw, settings.ProjectionTolerance, out var point)) { candidate++; Diagnostic = "OutsideProfile"; continue; }
            float projectedRadius = PNAgentGeometry.CenterDistance(settings.Center, point);
            if (projectedRadius < settings.MinimumRadius - .001f || projectedRadius > settings.MaximumRadius + .001f) { candidate++; continue; }
            var body = actor.GetComponent<CapsuleCollider>();
            if (body == null) throw new InvalidOperationException("Point clearance requires an upright body capsule.");
            float r = PNAgentGeometry.FootprintRadius(body); float height = body.bounds.size.y;
            var center = point + (body.bounds.center - PNActorHandle.Pose(actor));
            float half = Mathf.Max(0, height * .5f - r - .03f);
            int count = PNAgentPhysicsQuery.OverlapCapsule(actor.gameObject.scene.GetPhysicsScene(), center + Vector3.up * half, center - Vector3.up * half, r, ref clearance, ~(1 << 13));
            bool blocked = count == clearance.Length;
            for (int i = 0; i < count; i++) if (clearance[i] != null && clearance[i].GetComponentInParent<PNStat>() == null) blocked = true;
            if (blocked) { candidate++; Diagnostic = "NoClearance"; continue; }
            if (settings.SightTarget != null && (!settings.SightTarget.TryResolve(out var target) ||
                !PNAgentSpatial.HasSight(actor, point + Vector3.up * .6f, target, settings.SightTarget.Position + Vector3.up * .6f, ref sight)))
            { candidate++; Diagnostic = "NoSight"; continue; }
            float score = settings.Score != null ? settings.Score(point) : -PNAgentGeometry.CenterDistance(agent.Self.Position, point);
            if (float.IsNaN(score) || float.IsInfinity(score)) throw new InvalidOperationException("Point score must be finite.");
            points.Add((point, score)); candidate++;
        }
        if (candidate < settings.Candidates) return;
        if (!sorted)
        {
            points.Sort((a, b) => { int result = b.Score.CompareTo(a.Score); if (result != 0) return result;
                result = a.Point.x.CompareTo(b.Point.x); if (result != 0) return result; result = a.Point.z.CompareTo(b.Point.z); return result != 0 ? result : a.Point.y.CompareTo(b.Point.y); });
            sorted = true;
        }
        if (probeIndex < points.Count)
        {
            var point = points[probeIndex].Point; pending = true;
            bool started = lease.StartProbe(agent.Self.Load, agent.Self.Position, point, settings.ProjectionTolerance, valid =>
            {
                pending = false;
                if (IsTerminal) return;
                if (!agent.IsCurrent || !lease.IsCurrent) { Dispose(); return; }
                if (valid) { Point = point; Status = PNPointQueryStatus.Success; Diagnostic = null; }
                else Diagnostic = "NoFullPath";
            });
            if (!started) { pending = false; return; }
            probeIndex++; return;
        }
        if (!pending) { Status = PNPointQueryStatus.NoCandidate; Diagnostic ??= "NoCandidate"; }
    }
    public void Dispose() { if (!IsTerminal) Status = PNPointQueryStatus.Cancelled; }
}
#endif
