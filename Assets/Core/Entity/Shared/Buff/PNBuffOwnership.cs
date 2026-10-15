using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

public sealed class PNBuffResourceScope : IDisposable
{
    private readonly List<IDisposable> resources = new List<IDisposable>();
    private readonly Action<Exception> report;
    public bool IsDisposed { get; private set; }
    public bool CleanupFailed { get; private set; }
    public PNBuffResourceScope(Action<Exception> report = null) { this.report = report; }
    public T Own<T>(T resource) where T : IDisposable
    {
        if (resource == null) throw new ArgumentNullException(nameof(resource));
        if (IsDisposed) { resource.Dispose(); throw new ObjectDisposedException(nameof(PNBuffResourceScope)); }
        resources.Add(resource); return resource;
    }
    public void Forget(IDisposable resource) => resources.Remove(resource);
    public void Dispose()
    {
        if (IsDisposed) return; IsDisposed = true;
        var retiring = resources.ToArray(); resources.Clear();
        for (int i = retiring.Length - 1; i >= 0; i--) try { retiring[i].Dispose(); } catch (Exception e) { CleanupFailed = true; report?.Invoke(e); }
    }
}

public sealed class PNBuffRelease : IDisposable
{
    private Action release;
    public PNBuffRelease(Action release) { this.release = release ?? throw new ArgumentNullException(nameof(release)); }
    public void Dispose() { var action = release; release = null; action?.Invoke(); }
}

// Contract types identify independently owned contributions. Receivers define their arithmetic.
public sealed class PNBuffStackLimitContract { }
public sealed class PNBuffValueContract { }
public sealed class PNBuffInstanceLimitContract { }
public abstract class PNBuffContribution
{
    public abstract Type Contract { get; }
}
public sealed class PNBuffNumberContribution : PNBuffContribution
{
    public override Type Contract { get; }
    public string Field { get; }
    public double Value { get; }
    public PNBuffNumberContribution(Type contract, string field, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
        Contract = contract ?? throw new ArgumentNullException(nameof(contract)); Field = field ?? throw new ArgumentNullException(nameof(field)); Value = value;
    }
}
public sealed class PNBuffTagContribution : PNBuffContribution
{
    public override Type Contract => typeof(PNBuffTagContribution);
    public IReadOnlyList<EveId> Add { get; }
    public IReadOnlyList<EveId> Suppress { get; }
    public PNBuffTagContribution(IEnumerable<EveId> add = null, IEnumerable<EveId> suppress = null)
    { Add = Array.AsReadOnly((add ?? Array.Empty<EveId>()).Distinct().ToArray()); Suppress = Array.AsReadOnly((suppress ?? Array.Empty<EveId>()).Distinct().ToArray()); }
}
public sealed class PNBuffOverrideContribution : PNBuffContribution
{
    public override Type Contract => typeof(PNBuffOverrideContribution);
    public EveId ProfileId { get; }
    public int Priority { get; }
    public PNBuffOverrideContribution(EveId profileId, int priority)
    { if (!profileId.IsValid) throw new ArgumentException("Profile ID required."); ProfileId = profileId; Priority = priority; }
}

public sealed class PNBuffContributionLease : IDisposable
{
    private readonly PNBuffCollection collection;
    internal readonly HashSet<long> Targets = new HashSet<long>();
    internal readonly EveId? MatchId;
    internal readonly PNBuffSelection Selection;
    internal readonly long? Exact;
    public long Id { get; }
    public string Owner { get; }
    public PNBuffContribution Contribution { get; }
    public bool IsActive { get; internal set; } = true;
    internal PNBuffContributionLease(PNBuffCollection collection, long id, string owner, PNBuffContribution contribution,
        EveId? matchId, PNBuffSelection selection, long? exact)
    { this.collection = collection; Id = id; Owner = owner; Contribution = contribution; MatchId = matchId; Selection = selection; Exact = exact; }
    public void Dispose() { if (!IsActive) return; IsActive = false; collection.Release(this); }
}

public sealed class PNBuffImmunityLease : IDisposable
{
    private readonly PNBuffCollection collection;
    internal readonly HashSet<EveId> Ids;
    internal readonly HashSet<EveId> Tags;
    internal readonly bool Purge;
    public long Id { get; }
    public bool IsActive { get; internal set; } = true;
    internal PNBuffImmunityLease(PNBuffCollection collection, long id, IEnumerable<EveId> ids, IEnumerable<EveId> tags, bool purge)
    { this.collection = collection; Id = id; Ids = new HashSet<EveId>(ids); Tags = new HashSet<EveId>(tags); Purge = purge; }
    public void Dispose() { if (!IsActive) return; IsActive = false; collection.Release(this); }
}

// Session-wide serial queue. Callback-created mutations run at the next boundary, not recursively.
public sealed class PNBuffMutationCoordinator
{
    private readonly Queue<(Action execute, Action<Exception> abort)> queue = new Queue<(Action, Action<Exception>)>();
    private bool running;
    public bool IsExecuting => running;
    public bool IsFaulted { get; private set; }
    public bool IsMutating => running || queue.Count != 0;
    public event Action<Exception> Failed;
    public void Submit(Action command, Action<Exception> abort = null)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        if (IsFaulted) { var error = new InvalidOperationException("Buff coordinator is faulted; create a new runtime/session."); abort?.Invoke(error); return; }
        queue.Enqueue((command, abort));
        if (running) return; running = true;
        try
        {
            int count = 0;
            while (queue.Count > 0)
            {
                if (++count > 10000)
                {
                    IsFaulted = true; var error = new InvalidOperationException("Buff mutation cycle exceeded 10000 commands at one boundary; coordinator failed and owned collections are retired.");
                    while (queue.Count > 0) { var pending = queue.Dequeue(); try { pending.abort?.Invoke(error); } catch { } }
                    Report(error); break;
                }
                var next = queue.Dequeue(); try { next.execute(); } catch (Exception e) { next.abort?.Invoke(e); Report(e); }
            }
        }
        catch (Exception e) { Report(e); }
        finally { running = false; }
    }
    private void Report(Exception e)
    { foreach (Action<Exception> listener in Failed?.GetInvocationList() ?? Array.Empty<Delegate>()) try { listener(e); } catch { } }
}
