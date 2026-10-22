#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Pathfinding;
using UnityEngine;

// A* details remain internal; callers own world leases and receive copied positions only.
public sealed class PNWorldNavigation : IDisposable
{
    private static readonly Dictionary<EveSessionContext, PNWorldNavigation> sessions = new Dictionary<EveSessionContext, PNWorldNavigation>();
    private readonly Dictionary<EveWorldContext, PNWorldNavigationLease> worlds = new Dictionary<EveWorldContext, PNWorldNavigationLease>();
    private readonly EveSessionContext session;
    private GameObject root;
    internal AstarPath Astar { get; private set; }
    private bool disposed;
    private int startsFrame = -1, starts;
    private List<EveEntityLoadContext> probeWaiters;
    private List<PNEnemyNavigationRequest> movementWaiters;
    private Dictionary<EveEntityLoadContext, int> probeLastSeen;
    public int GraphCount => worlds.Values.Sum(x => x.Graphs.Count);
    public int PendingRequests => worlds.Values.Sum(x => x.Pending);
    public int PathStartsPerFrame { get; set; } = 4;
    private PNWorldNavigation(EveSessionContext owner)
    {
        session = owner;
        if (AstarPath.Version.ToString() != "5.3.3") throw new InvalidOperationException("Navigation caches require the installed A*5.3.3 backend.");
        if (AstarPath.active != null) throw new InvalidOperationException("Another AstarPath already owns this process.");
        root = new GameObject("Project N Navigation"); root.SetActive(false); root.AddComponent<PNRoomResource>();
        try
        {
            Astar = root.AddComponent<AstarPath>(); Astar.scanOnStartup = false;
            Astar.threadCount = ThreadCount.One; Astar.logPathResults = PathLog.None;
            root.SetActive(true); UnityEngine.Object.DontDestroyOnLoad(root);
            session.RegisterCleanup(Dispose, afterEntities: true);
        }
        catch { UnityEngine.Object.DestroyImmediate(root); throw; }
    }
    public static PNWorldNavigation ForSession(EveSessionContext owner)
    {
        if (owner == null || !owner.IsActive || !owner.HasAuthority) throw new InvalidOperationException("Navigation requires an active authoritative play.");
        if (!sessions.TryGetValue(owner, out var value)) { value = new PNWorldNavigation(owner); sessions.Add(owner, value); }
        return value;
    }
    public static PNWorldNavigationLease Find(EveWorldContext world)
        => world != null && sessions.TryGetValue(world.Session, out var owner) && owner.worlds.TryGetValue(world, out var lease) && lease.IsCurrent ? lease : null;
    public static void Prepare(PNWorldBuildContext build)
    {
        var declarations = build.Slots.SelectMany(s => s.Roots).Where(x => x != null)
            .SelectMany(x => x.GetComponentsInChildren<PNWorldNavigationSource>(true)).ToArray();
        if (declarations.Length == 0) return;
        if (declarations.Length != 1) throw new InvalidOperationException("One navigation declaration per world is required.");
        var source = declarations[0]; source.Validate();
        if (build.World.Session.HasAuthority) ForSession(build.World.Session).Load(build.World, source.Cache, source.ExpectedFingerprint);
    }
    public PNWorldNavigationLease Load(EveWorldContext world, PNNavigationCache cache, string expectedFingerprint)
    {
        if (disposed || !session.IsActive || !ReferenceEquals(world?.Session, session) || world.State != EveWorldState.Loading || worlds.ContainsKey(world))
            throw new InvalidOperationException("Load navigation once during world preparation.");
        if (cache == null) throw new ArgumentNullException(nameof(cache)); cache.Validate();
        if (expectedFingerprint != cache.Fingerprint) throw new InvalidOperationException("Navigation fingerprint differs from the world declaration.");
        // The first release supports exactly one baked graph per profile/world.
        if (GraphCount >= 32) throw new InvalidOperationException("A*5.3.3 GraphMask capacity exceeded (32 graphs).");
        var lease = new PNWorldNavigationLease(this, world, cache);
        worlds.Add(world, lease); world.RegisterCleanup(lease.Dispose);
        try
        {
            var before = new HashSet<NavGraph>(Astar.data.graphs.Where(g => g != null));
            try { Astar.data.DeserializeGraphsAdditive(cache.CopyGraphData()); }
            finally { lease.Graphs.AddRange(Astar.data.graphs.Where(g => g != null && !before.Contains(g))); }
            if (lease.Graphs.Count != 1 || !(lease.Graphs[0] is RecastGraph) || lease.Graphs[0].graphIndex >= 32)
                throw new InvalidOperationException("Expected one baked Recast graph with a representable mask.");
            foreach (var graph in lease.Graphs)
                graph.GetNodes(node => node.GetConnections(other =>
                { if (other.GraphIndex != node.GraphIndex) throw new InvalidOperationException("Navigation graph connects outside its world."); }));
            lease.Publish(); return lease;
        }
        catch { lease.Retire(); throw; }
    }
    private static bool IsWaiting(PNEnemyNavigationRequest request)
        => request != null && !request.IsTerminal && request.NeedsPath && request.Owner != null &&
            ReferenceEquals(request.Owner.NavigationRequest, request) && request.Owner.isActiveAndEnabled && request.Owner.IsReady &&
            request.Owner.CanMove && request.Owner.CanWriteMotion && !request.Owner.HasActiveMotion && request.Owner.NavigationSuspensions == 0 &&
            request.Owner.MovementTimeScale > 0 && PNWorldGameplay.CanExecute(request.Owner) &&
            (request.OwnerIsCurrent == null || request.OwnerIsCurrent());
    internal bool TakeStartBudget(PNEnemyNavigationRequest request)
    {
        if (disposed || !session.IsActive || !IsWaiting(request)) return false;
        if (PathStartsPerFrame <= 0) throw new InvalidOperationException("Path start budget must be positive.");
        if (startsFrame != Time.frameCount) { startsFrame = Time.frameCount; starts = 0; }
        movementWaiters ??= new List<PNEnemyNavigationRequest>();
        for (int i = movementWaiters.Count - 1; i >= 0; i--) if (!IsWaiting(movementWaiters[i])) movementWaiters.RemoveAt(i);
        if (!movementWaiters.Contains(request)) movementWaiters.Add(request);
        PruneProbeWaiters();
        if (probeWaiters?.Count > 0 && starts >= PathStartsPerFrame - 1) return false;
        if (starts >= PathStartsPerFrame || !ReferenceEquals(movementWaiters[0], request)) return false;
        movementWaiters.RemoveAt(0); starts++; return true;
    }
    private void PruneProbeWaiters()
    {
        if (probeWaiters == null) return;
        for (int i = probeWaiters.Count - 1; i >= 0; i--)
        { var actor = probeWaiters[i]; if (!actor.CanUse || Time.frameCount - probeLastSeen[actor] > 2) { probeWaiters.RemoveAt(i); probeLastSeen.Remove(actor); } }
    }
    internal bool TakeProbeBudget(EveEntityLoadContext actor)
    {
        if (disposed || !session.IsActive || !actor.CanExecute || !ReferenceEquals(actor.Session, session)) return false;
        if (PathStartsPerFrame <= 0) throw new InvalidOperationException("Path start budget must be positive.");
        if (startsFrame != Time.frameCount) { startsFrame = Time.frameCount; starts = 0; }
        probeWaiters ??= new List<EveEntityLoadContext>(); probeLastSeen ??= new Dictionary<EveEntityLoadContext, int>();
        PruneProbeWaiters();
        if (!probeLastSeen.ContainsKey(actor)) probeWaiters.Add(actor);
        probeLastSeen[actor] = Time.frameCount;
        if (!ReferenceEquals(probeWaiters[0], actor) || starts >= PathStartsPerFrame) return false;
        probeWaiters.RemoveAt(0); probeLastSeen.Remove(actor); starts++; return true;
    }
    internal void Removed(PNWorldNavigationLease lease)
    {
        worlds.Remove(lease.World);
        movementWaiters?.RemoveAll(request => !IsWaiting(request));
        PruneProbeWaiters();
    }
    public void Dispose()
    {
        if (disposed) return;
        var failures = new List<Exception>();
        foreach (var lease in worlds.Values.ToArray()) try { lease.Dispose(); } catch (Exception e) { failures.Add(e); }
        if (failures.Count > 0) throw new AggregateException("Navigation cleanup remains owned.", failures);
        if (root != null) UnityEngine.Object.DestroyImmediate(root);
        disposed = true; sessions.Remove(session); root = null; Astar = null;
        movementWaiters?.Clear(); probeWaiters?.Clear(); probeLastSeen?.Clear();
    }
}

public sealed class PNWorldNavigationLease : IDisposable
{
    private readonly PNWorldNavigation owner;
    private bool published, retiring, disposed;
    internal readonly List<NavGraph> Graphs = new List<NavGraph>();
    internal int Pending;
    private HashSet<EveEntityLoadContext> probeActors;
    public EveWorldContext World { get; }
    public Guid Generation { get; } = Guid.NewGuid();
    public string ProfileId { get; }
    public float Radius { get; }
    public float Height { get; }
    public bool IsCurrent => published && !retiring && !disposed && World.Session.IsActive &&
        (World.State == EveWorldState.Loading || World.State == EveWorldState.Ready || World.State == EveWorldState.Paused);
    internal PNWorldNavigationLease(PNWorldNavigation owner, EveWorldContext world, PNNavigationCache cache)
    { this.owner = owner; World = world; ProfileId = cache.ProfileId; Radius = cache.Radius; Height = cache.Height; }
    internal void Publish() => published = true;
    internal void Retire() { retiring = true; published = false; }
    public bool TryProject(Vector3 point, float tolerance, out Vector3 projected)
    {
        PNNavigationSettings.Position(point); PNAgentGeometry.Nonnegative(tolerance, nameof(tolerance)); projected = default;
        if (!IsCurrent || !World.CanExecute) return false;
        var constraint = NNConstraint.Walkable; constraint.graphMask = GraphMask.FromGraph(Graphs[0]);
        var nearest = owner.Astar.GetNearest(point, constraint);
        if (nearest.node == null || nearest.node.GraphIndex != Graphs[0].graphIndex || Vector3.Distance(nearest.position, point) > tolerance) return false;
        projected = nearest.position; return true;
    }
    internal bool StartProbe(EveEntityLoadContext actor, Vector3 start, Vector3 goal, float tolerance, Action<bool> completed)
    {
        if (!IsCurrent || !World.CanExecute || probeActors?.Contains(actor) == true || !owner.TakeProbeBudget(actor)) return false;
        if (!TryProject(start, tolerance, out _) || !TryProject(goal, tolerance, out _)) { completed(false); return true; }
        (probeActors ??= new HashSet<EveEntityLoadContext>()).Add(actor);
        var request = ABPath.Construct(start, goal, path =>
        {
            try { completed(IsCurrent && !path.error && path.CompleteState == PathCompleteState.Complete && path.path != null &&
                path.path.All(n => n.GraphIndex == Graphs[0].graphIndex) && ReachesEndpoints(path, start, goal, tolerance)); }
            finally { Pending--; probeActors.Remove(actor); }
        });
        request.nnConstraint.graphMask = GraphMask.FromGraph(Graphs[0]); Pending++;
        try { AstarPath.StartPath(request); return true; } catch { Pending--; probeActors.Remove(actor); throw; }
    }
    internal bool Start(PNEnemyNavigationRequest movement, Seeker seeker, Vector3 start, Vector3 goal, float tolerance, Action<Vector3[], string> completed)
    {
        if (!IsCurrent || !World.CanExecute || !owner.TakeStartBudget(movement)) return false;
        var constraint = NNConstraint.Walkable; constraint.graphMask = GraphMask.FromGraph(Graphs[0]);
        var a = owner.Astar.GetNearest(start, constraint); var b = owner.Astar.GetNearest(goal, constraint);
        if (a.node == null || b.node == null || Vector3.Distance(a.position, start) > tolerance || Vector3.Distance(b.position, goal) > tolerance)
        { completed(null, "Start or goal is outside the navigation profile."); return true; }
        Pending++;
        var request = ABPath.Construct(start, goal, null);
        try
        {
            seeker.StartPath(request, null, GraphMask.FromGraph(Graphs[0]));
            // Direct Path callback also runs when Seeker drops a replaced/destroyed request.
            // Append AFTER Seeker's callback so its Funnel modifier has already run.
            request.callback += path =>
            {
                try
                {
                    if (!IsCurrent) { completed(null, "World navigation retired."); return; }
                    if (path.error || path.CompleteState != PathCompleteState.Complete || path.path == null ||
                        path.path.Any(n => n.GraphIndex != Graphs[0].graphIndex) || path.vectorPath == null || path.vectorPath.Count < 2 ||
                        !ReachesEndpoints(path, start, goal, tolerance))
                    { completed(null, string.IsNullOrEmpty(path.errorLog) ? "No complete path reaching the requested endpoints in this world." : path.errorLog); return; }
                    // Copy before the Seeker releases its pooled Path, never retain vectorPath.
                    completed(path.vectorPath.ToArray(), null);
                }
                finally { Pending--; }
            };
            return true;
        }
        catch { Pending--; throw; }
    }
    // A* may clamp an end node to the start component and still report Complete.
    // A complete path in this contract must reach the requested endpoints as well.
    private static bool ReachesEndpoints(Path path, Vector3 start, Vector3 goal, float tolerance)
        // A raw probe's vectorPath contains triangle centres until Funnel runs.
        // ABPath keeps the actual closest points used for its endpoint nodes.
        => path is ABPath ab && Vector3.Distance(ab.startPoint, start) <= tolerance &&
            Vector3.Distance(ab.endPoint, goal) <= tolerance;
    public void Dispose()
    {
        if (disposed) return; Retire();
        if (Pending != 0) throw new EveWorldCleanupPendingException("Navigation is waiting for path callbacks.");
        foreach (var graph in Graphs.ToArray()) { if (owner.Astar != null) owner.Astar.data.RemoveGraph(graph); Graphs.Remove(graph); }
        disposed = true; owner.Removed(this);
    }
}
#endif
