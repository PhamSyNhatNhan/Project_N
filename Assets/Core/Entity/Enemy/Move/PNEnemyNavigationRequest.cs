#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

public enum PNEnemyNavigationStatus { PendingPath, Following, Suspended, Arrived, NoPath, Stuck, Cancelled }
public enum PNEnemyNavigationEndReason { None, Replaced, OwnerCancelled, Disabled, Unloaded, Died, WorldChanged }
public sealed class PNEnemyNavigationRequest
{
    internal readonly PNEnemyMove Owner;
    internal readonly Func<bool> OwnerIsCurrent;
    internal readonly Guid LoadGeneration;
    internal Guid RouteGeneration;
    internal Vector3[] Route;
    internal int Corner, Retries;
    internal float RepathElapsed, ProgressElapsed, ProgressDistance;
    internal float FollowSpeed, ExpectedProgress;
    internal Vector2 FollowHeading;
    internal Vector3 PreviousBody, SubmittedGoal;
    internal bool Pending, NeedsPath = true;
    public long Id { get; }
    public int PathAttempts { get; internal set; }
    public Vector3 Goal { get; internal set; }
    public PNLocomotionOptions Options { get; }
    public float StopDistance { get; }
    public PNEnemyNavigationStatus Status { get; internal set; } = PNEnemyNavigationStatus.PendingPath;
    public PNEnemyNavigationEndReason EndReason { get; internal set; }
    public string Diagnostic { get; internal set; }
    public bool IsTerminal => Status == PNEnemyNavigationStatus.Arrived || Status == PNEnemyNavigationStatus.NoPath ||
        Status == PNEnemyNavigationStatus.Stuck || Status == PNEnemyNavigationStatus.Cancelled;
    public event Action<PNEnemyNavigationRequest> Completed;
    internal PNEnemyNavigationRequest(PNEnemyMove owner, long id, Guid generation, Vector3 goal, float stopDistance, PNLocomotionOptions options, Func<bool> current)
    { Owner = owner; Id = id; LoadGeneration = generation; Goal = SubmittedGoal = goal; StopDistance = stopDistance; Options = options; OwnerIsCurrent = current; }
    internal void Finish(PNEnemyNavigationStatus state, PNEnemyNavigationEndReason reason = PNEnemyNavigationEndReason.None, string diagnostic = null)
    {
        if (IsTerminal) return; Status = state; EndReason = reason; Diagnostic = diagnostic; Route = null;
        var listeners = Completed; Completed = null;
        foreach (Action<PNEnemyNavigationRequest> listener in listeners?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { listener(this); } catch (Exception e) { Debug.LogException(e); }
    }
}
#endif
