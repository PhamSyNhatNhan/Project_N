using System;
using System.Collections.Generic;
using System.Linq;

namespace EveEngine.Experimental.Lifecycle
{
    public enum CleanupLeaseState { Active, Queued, RetryWaiting, Quarantined, Released }
    public enum CleanupStopState { Running, Stopping, Blocked, Stopped }

    public sealed class CleanupPolicy
    {
        public int MaximumResident { get; }
        public int MaximumBacklog { get; }
        public int PausePreparationAtBacklog { get; }
        public int MaximumStepsPerFrame { get; }
        public int MaximumStepsPerLease { get; }
        public double BudgetMs { get; }
        public int MaximumAttempts { get; }
        public double RetryIntervalMs { get; }
        public CleanupPolicy(int resident, int backlog, int pauseAt, int stepsPerFrame, int stepsPerLease, double budgetMs, int attempts, double retryIntervalMs)
        {
            if (resident < 1 || backlog < resident || pauseAt < 1 || pauseAt > backlog || stepsPerFrame < 1 || stepsPerLease < 1 || stepsPerLease == int.MaxValue || attempts < 1 ||
                double.IsNaN(budgetMs) || double.IsInfinity(budgetMs) || budgetMs <= 0 ||
                double.IsNaN(retryIntervalMs) || double.IsInfinity(retryIntervalMs) || retryIntervalMs <= 0) throw new ArgumentOutOfRangeException();
            MaximumResident = resident; MaximumBacklog = backlog; PausePreparationAtBacklog = pauseAt;
            MaximumStepsPerFrame = stepsPerFrame; MaximumStepsPerLease = stepsPerLease;
            BudgetMs = budgetMs; MaximumAttempts = attempts; RetryIntervalMs = retryIntervalMs;
        }
    }
    public sealed class CleanupStep
    {
        public string Name { get; }
        internal Action Work { get; }
        public CleanupStep(string name, Action work)
        { if (string.IsNullOrWhiteSpace(name) || name.Length > 128) throw new ArgumentException("Bounded step name required."); Name = name; Work = work ?? throw new ArgumentNullException(nameof(work)); }
    }
    public sealed class CleanupLease
    {
        public long LeaseId { get; }
        public PresenceKey Key { get; }
        public CleanupLeaseState State { get; internal set; } = CleanupLeaseState.Active;
        public bool IsVisible { get; internal set; } = true;
        public bool CanRunGameplay { get; internal set; } = true;
        public int Attempts { get; internal set; }
        public string FailedStep { get; internal set; }
        public Exception LastError { get; internal set; }
        internal CleanupLease(long id, PresenceKey key) { LeaseId = id; Key = key; }
    }

    // Pure scheduler, explicitly pumped by its owner even with no active gameplay session.
    // Flags model immediate hiding/invalidation; Unity renderer/pool integration is a later step.
    public sealed class CleanupModel
    {
        private sealed class Job
        {
            public CleanupLease Lease;
            public CleanupStep[] Steps;
            public bool[] Done;
            public int Cursor;
            public double NotBefore;
        }
        private readonly CleanupPolicy policy;
        private readonly Func<double> clock;
        private readonly Dictionary<long, Job> resident = new Dictionary<long, Job>();
        private readonly Queue<Job> runnable = new Queue<Job>();
        private long nextLeaseId;
        private double lastTime;
        private bool pumping;
        public CleanupStopState StopState { get; private set; } = CleanupStopState.Running;
        public int ResidentCount => resident.Count;
        public int BacklogCount => resident.Values.Count(x => x.Lease.State != CleanupLeaseState.Active);
        public int QuarantineCount => resident.Values.Count(x => x.Lease.State == CleanupLeaseState.Quarantined);
        public bool CanBeginPreparation => StopState == CleanupStopState.Running && ResidentCount < policy.MaximumResident && BacklogCount < policy.PausePreparationAtBacklog;
        public int FailureCount { get; private set; }
        public double LastFrameMs { get; private set; }
        public CleanupModel(CleanupPolicy policy, Func<double> monotonicMilliseconds)
        { this.policy = policy ?? throw new ArgumentNullException(nameof(policy)); clock = monotonicMilliseconds ?? throw new ArgumentNullException(nameof(monotonicMilliseconds)); ReadClock(); }
        private double ReadClock()
        {
            double value = clock();
            if (double.IsNaN(value) || double.IsInfinity(value) || value < lastTime) throw new InvalidOperationException("Monotonic real clock required.");
            lastTime = value; return value;
        }
        private void Guard() { if (pumping) throw new InvalidOperationException("Reentrant cleanup owner operation."); }
        public bool TryAcquire(PresenceKey key, IEnumerable<CleanupStep> cleanupInExecutionOrder, Action returnToPool, out CleanupLease lease)
        {
            Guard(); lease = null;
            if (key.EntityId <= 0 || returnToPool == null || cleanupInExecutionOrder == null) throw new ArgumentException();
            var steps = cleanupInExecutionOrder.Take(policy.MaximumStepsPerLease + 1).ToArray();
            if (steps.Length == 0 || steps.Length > policy.MaximumStepsPerLease || steps.Any(x => x == null)) throw new ArgumentException("Cleanup step capacity.");
            if (!CanBeginPreparation) return false;
            long id = checked(nextLeaseId + 1);
            var result = new CleanupLease(id, key);
            var all = steps.Concat(new[] { new CleanupStep("return-to-pool", returnToPool) }).ToArray();
            resident.Add(id, new Job { Lease = result, Steps = all, Done = new bool[all.Length] });
            nextLeaseId = id; lease = result; return true;
        }
        public void Retire(CleanupLease lease)
        {
            Guard(); var job = Owned(lease);
            if (lease.State != CleanupLeaseState.Active) return;
            // No external callback or heavy Unload on the invalidation/hide path.
            lease.IsVisible = false; lease.CanRunGameplay = false; lease.State = CleanupLeaseState.Queued;
            runnable.Enqueue(job);
            if (BacklogCount > policy.MaximumBacklog) throw new InvalidOperationException("Resident/backlog invariant violated.");
        }
        private Job Owned(CleanupLease lease)
        {
            if (lease == null || !resident.TryGetValue(lease.LeaseId, out var job) || !ReferenceEquals(job.Lease, lease)) throw new ArgumentException("Lease not owned by this scheduler.");
            return job;
        }
        public void BeginStop()
        {
            Guard(); if (StopState == CleanupStopState.Stopped) return;
            StopState = CleanupStopState.Stopping;
            foreach (var job in resident.Values.ToArray()) if (job.Lease.State == CleanupLeaseState.Active) Retire(job.Lease);
            UpdateStop();
        }
        public void RetryCleanup(CleanupLease lease)
        {
            Guard(); var job = Owned(lease);
            if (lease.State != CleanupLeaseState.Quarantined) throw new InvalidOperationException("Explicit retry is for exhausted quarantine only.");
            lease.Attempts = 0; lease.State = CleanupLeaseState.Queued; job.Cursor = 0; job.NotBefore = ReadClock(); runnable.Enqueue(job);
            if (StopState == CleanupStopState.Blocked) StopState = CleanupStopState.Stopping;
        }
        public int AdvanceFrame(double availableFrameBudgetMs)
        {
            Guard();
            if (double.IsNaN(availableFrameBudgetMs) || double.IsInfinity(availableFrameBudgetMs) || availableFrameBudgetMs < 0) throw new ArgumentOutOfRangeException();
            double start = ReadClock(), budget = Math.Min(policy.BudgetMs, availableFrameBudgetMs); int executed = 0;
            pumping = true;
            try
            {
                while (executed < policy.MaximumStepsPerFrame)
                {
                    // Budget controls starting additional steps. First runnable step always progresses.
                    if (executed > 0 && ReadClock() - start >= budget) break;
                    Job job = SelectRunnable(); if (job == null) break;
                    var step = job.Steps[job.Cursor];
                    try { step.Work(); job.Done[job.Cursor] = true; }
                    catch (Exception error)
                    { job.Lease.LastError = error; job.Lease.FailedStep = step.Name; FailureCount++; }
                    job.Cursor++; executed++;
                    if (job.Cursor == job.Steps.Length)
                    {
                        if (job.Done.All(x => x))
                        { job.Lease.State = CleanupLeaseState.Released; job.Lease.LastError = null; job.Lease.FailedStep = null; resident.Remove(job.Lease.LeaseId); }
                        else EndAttempt(job);
                    }
                    else runnable.Enqueue(job);
                }
            }
            finally { try { LastFrameMs = ReadClock() - start; } finally { pumping = false; UpdateStop(); } }
            return executed;
        }
        private Job SelectRunnable()
        {
            int visits = runnable.Count;
            while (visits-- > 0)
            {
                var job = runnable.Dequeue();
                if (ReadClock() < job.NotBefore) { runnable.Enqueue(job); continue; }
                while (job.Cursor < job.Steps.Length && job.Done[job.Cursor]) job.Cursor++;
                // Pool return cannot execute if any preceding cleanup remains failed.
                if (job.Cursor == job.Steps.Length - 1 && job.Done.Take(job.Cursor).Any(x => !x)) { EndAttempt(job); continue; }
                job.Lease.State = CleanupLeaseState.Queued; return job;
            }
            return null;
        }
        private void EndAttempt(Job job)
        {
            job.Lease.Attempts++;
            if (job.Lease.Attempts >= policy.MaximumAttempts) job.Lease.State = CleanupLeaseState.Quarantined;
            else
            { job.Lease.State = CleanupLeaseState.RetryWaiting; job.Cursor = 0; job.NotBefore = ReadClock() + policy.RetryIntervalMs; runnable.Enqueue(job); }
        }
        private void UpdateStop()
        {
            if (StopState == CleanupStopState.Running) return;
            StopState = ResidentCount == 0 ? CleanupStopState.Stopped : runnable.Count == 0 ? CleanupStopState.Blocked : CleanupStopState.Stopping;
        }
    }
}
