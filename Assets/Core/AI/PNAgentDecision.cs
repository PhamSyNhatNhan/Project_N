#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;

public sealed class PNUtilityCandidate
{
    public string Id { get; }
    public int Priority { get; }
    public double Bias { get; }
    internal readonly Func<bool> Eligible;
    internal readonly Func<float> Score;
    public PNUtilityCandidate(string id, Func<bool> eligible, Func<float> score, double bias = 1, int priority = 0)
    {
        if (string.IsNullOrWhiteSpace(id) || double.IsNaN(bias) || double.IsInfinity(bias) || bias < 0 || bias > 1000) throw new ArgumentException("A stable candidate identity and finite bias in [0,1000] are required.");
        Id = id; Eligible = eligible ?? throw new ArgumentNullException(nameof(eligible)); Score = score ?? throw new ArgumentNullException(nameof(score)); Bias = bias; Priority = priority;
    }
    internal double Evaluate()
    {
        float score = Score(); if (float.IsNaN(score) || float.IsInfinity(score) || score < 0 || score > 1) throw new InvalidOperationException("Utility scores must be finite in [0,1].");
        return score * Bias;
    }
}

public sealed class PNUtilitySelector
{
    private readonly PNUtilityCandidate[] candidates;
    private readonly float interval, minimumHold;
    private readonly double margin;
    private float time, nextDecision, selectedAt;
    private PNUtilityCandidate current;
    public string CurrentId => current?.Id;
    public int Evaluations { get; private set; }
    public PNUtilitySelector(IEnumerable<PNUtilityCandidate> candidates, float interval = .25f, float minimumHold = .5f, double switchMargin = .1)
    {
        this.candidates = candidates?.ToArray() ?? throw new ArgumentNullException(nameof(candidates));
        if (this.candidates.Length > 64 || this.candidates.Any(c => c == null) || this.candidates.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != this.candidates.Length) throw new ArgumentException("At most 64 unique candidates.");
        PNNavigationSettings.Positive(interval, nameof(interval)); PNAgentGeometry.Nonnegative(minimumHold, nameof(minimumHold));
        if (double.IsNaN(switchMargin) || double.IsInfinity(switchMargin) || switchMargin < 0) throw new ArgumentOutOfRangeException(nameof(switchMargin));
        this.interval = interval; this.minimumHold = minimumHold; margin = switchMargin;
    }
    public string Tick(float delta)
    {
        PNAgentGeometry.Nonnegative(delta, nameof(delta)); if (delta == 0) return CurrentId;
        time += delta;
        bool valid = current != null && current.Eligible();
        if (valid && time < nextDecision) return current.Id;
        nextDecision = time + interval; Evaluations++;
        PNUtilityCandidate best = null; double bestScore = -1, currentScore = -1;
        foreach (var candidate in candidates)
        {
            if (!candidate.Eligible()) continue; double score = candidate.Evaluate();
            if (ReferenceEquals(candidate, current)) currentScore = score;
            if (score > bestScore || score == bestScore && (best == null || candidate.Priority > best.Priority || candidate.Priority == best.Priority && string.CompareOrdinal(candidate.Id, best.Id) < 0))
            { best = candidate; bestScore = score; }
        }
        if (valid && best != current && (time - selectedAt < minimumHold || bestScore < currentScore + margin)) return current.Id;
        if (!ReferenceEquals(current, best)) { current = best; selectedAt = time; }
        return CurrentId;
    }
}

public enum PNGoalStatus { Inactive, Running, Succeeded, Failed, Cancelled }
public abstract class PNAgentGoal : IDisposable
{
    [ThreadStatic] private static int tickDepth;
    [ThreadStatic] private static int tickFuel;
    private readonly List<IDisposable> resources = new List<IDisposable>();
    private bool disposed, ticking;
    internal PNAgentGoal Parent;
    public PNGoalStatus Status { get; private set; }
    public bool IsTerminal => Status == PNGoalStatus.Succeeded || Status == PNGoalStatus.Failed || Status == PNGoalStatus.Cancelled;
    public virtual int Depth => 1;
    public virtual int Nodes => 1;
    protected T Own<T>(T resource) where T : IDisposable { if (disposed) throw new ObjectDisposedException(GetType().Name); resources.Add(resource); return resource; }
    protected virtual void Enter() { }
    protected abstract PNGoalStatus Execute(float delta);
    public PNGoalStatus Tick(float delta)
    {
        PNAgentGeometry.Nonnegative(delta, nameof(delta)); if (IsTerminal || disposed) return Status;
        if (ticking) throw new InvalidOperationException("A goal cannot reenter its tick.");
        if (tickDepth >= 8) throw new InvalidOperationException("Dynamic goal nesting exceeds depth8.");
        if (tickDepth == 0) tickFuel = 16;
        if (tickFuel-- <= 0) return PNGoalStatus.Running;
        tickDepth++; ticking = true;
        try
        {
            if (Status == PNGoalStatus.Inactive) { Status = PNGoalStatus.Running; Enter(); }
            var next = Execute(delta);
            if (IsTerminal || disposed) return Status;
            if (next == PNGoalStatus.Inactive || !Enum.IsDefined(typeof(PNGoalStatus), next)) throw new InvalidOperationException("Invalid goal transition.");
            Status = next; if (IsTerminal) Release(); return Status;
        }
        finally { ticking = false; tickDepth--; }
    }
    private void Release()
    {
        var failures = new List<Exception>();
        for (int i = resources.Count - 1; i >= 0; i--) try { resources[i].Dispose(); resources.RemoveAt(i); } catch (Exception e) { failures.Add(e); }
        if (failures.Count != 0) throw new AggregateException("Goal resources remain owned for retry.", failures);
    }
    public void Cancel() { if (!IsTerminal) Status = PNGoalStatus.Cancelled; Release(); }
    public void Dispose() { if (disposed) return; Cancel(); disposed = true; }
}
public sealed class PNDelegateGoal : PNAgentGoal
{
    private readonly Func<float, PNGoalStatus> tick;
    public PNDelegateGoal(Func<float, PNGoalStatus> tick, IDisposable resource = null)
    { this.tick = tick ?? throw new ArgumentNullException(nameof(tick)); if (resource != null) Own(resource); }
    protected override PNGoalStatus Execute(float delta) => tick(delta);
}
public sealed class PNMovementGoal : PNAgentGoal
{
    private readonly PNMovementTask task;
    public PNMovementGoal(PNMovementTask task) { this.task = Own(task ?? throw new ArgumentNullException(nameof(task))); }
    protected override PNGoalStatus Execute(float delta)
    {
        switch (task.Tick(delta))
        { case PNAgentTaskStatus.Succeeded: return PNGoalStatus.Succeeded; case PNAgentTaskStatus.Failed: return PNGoalStatus.Failed; case PNAgentTaskStatus.Cancelled: return PNGoalStatus.Cancelled; default: return PNGoalStatus.Running; }
    }
}
public sealed class PNCompositeGoal : PNAgentGoal
{
    private readonly PNAgentGoal[] children;
    private readonly bool fallback;
    private int index;
    public override int Depth { get; }
    public override int Nodes { get; }
    public PNCompositeGoal(IEnumerable<PNAgentGoal> children, bool fallback = false)
    {
        this.children = children?.ToArray() ?? throw new ArgumentNullException(nameof(children)); this.fallback = fallback;
        if (this.children.Length > 64 || this.children.Any(c => c == null || c.Parent != null || c.Status != PNGoalStatus.Inactive) || this.children.Distinct().Count() != this.children.Length) throw new ArgumentException("At most 64 distinct unowned inactive children.");
        Depth = this.children.Length == 0 ? 1 : 1 + this.children.Max(c => c.Depth);
        Nodes = 1 + this.children.Sum(c => c.Nodes);
        if (Depth > 8 || Nodes > 256) throw new ArgumentException("Goal tree exceeds depth8/nodes256.");
        foreach (var child in this.children) { child.Parent = this; Own(child); }
    }
    protected override PNGoalStatus Execute(float delta)
    {
        for (int transitions = 0; transitions < 16 && index < children.Length; transitions++)
        {
            var state = children[index].Tick(delta); delta = 0;
            if (state == PNGoalStatus.Running) return state;
            if (state == PNGoalStatus.Cancelled) return state;
            if (fallback && state == PNGoalStatus.Succeeded || !fallback && state == PNGoalStatus.Failed) return state;
            children[index].Dispose(); index++;
        }
        return index == children.Length ? (fallback ? PNGoalStatus.Failed : PNGoalStatus.Succeeded) : PNGoalStatus.Running;
    }
}
public sealed class PNRetryGoal : PNAgentGoal
{
    private readonly Func<PNAgentGoal> create;
    private readonly int retries;
    private readonly float backoff;
    private PNAgentGoal current;
    private int attempts;
    private float time, retryAt;
    public PNRetryGoal(Func<PNAgentGoal> create, int retries = 2, float backoff = .5f)
    {
        this.create = create ?? throw new ArgumentNullException(nameof(create));
        if (retries < 0 || retries > 16) throw new ArgumentOutOfRangeException(nameof(retries));
        PNNavigationSettings.Positive(backoff, nameof(backoff)); this.retries = retries; this.backoff = backoff;
    }
    protected override PNGoalStatus Execute(float delta)
    {
        time += delta; if (time < retryAt) return PNGoalStatus.Running;
        if (current == null)
        {
            var child = create() ?? throw new InvalidOperationException("Retry factory returned no goal.");
            if (child.Parent != null || child.Status != PNGoalStatus.Inactive) throw new InvalidOperationException("Retry factory must return a fresh unowned goal.");
            current = Own(child); current.Parent = this;
            if (current.Depth >= 8 || current.Nodes >= 256) throw new InvalidOperationException("Retry child exceeds tree budget.");
            attempts++;
        }
        var result = current.Tick(delta);
        if (result != PNGoalStatus.Failed) return result;
        current.Dispose(); current = null;
        if (attempts > retries) return PNGoalStatus.Failed;
        retryAt = time + backoff; return PNGoalStatus.Running;
    }
}
#endif
