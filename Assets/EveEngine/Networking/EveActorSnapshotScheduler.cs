using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine.Experimental.Lifecycle;

namespace EveEngine
{
    public sealed class EveFrozenActorSnapshot
    {
        internal readonly Queue<Action> Work;
        internal readonly Func<IReadOnlyList<ActorState>> Complete;
        public EveFrozenActorSnapshot(IEnumerable<Action> serializationSteps, Func<IReadOnlyList<ActorState>> complete)
        { Work = new Queue<Action>(serializationSteps ?? throw new ArgumentNullException(nameof(serializationSteps))); Complete = complete ?? throw new ArgumentNullException(nameof(complete)); }
    }
    public sealed class EveActorSnapshotScheduler
    {
        private sealed class Request { internal Action Cut; internal Action<IReadOnlyList<ActorState>> Done; internal Action<Exception> Failed; }
        private sealed class Job
        { internal string Scope, QueryScope; internal Func<EveFrozenActorSnapshot> Freeze; internal EveFrozenActorSnapshot Frozen; internal long ReadyFrame; internal readonly List<Request> Requests = new List<Request>(); }
        private readonly int capacity;
        private readonly EveEntityPreparationBudget budget;
        private readonly Func<double> clock;
        private readonly Dictionary<string, Job> pending = new Dictionary<string, Job>();
        private readonly Dictionary<string, Job> waitingScopes = new Dictionary<string, Job>();
        private readonly Queue<Job> waiting = new Queue<Job>(), serializing = new Queue<Job>();
        private long frame, nextJobId;
        public int PendingJobs => pending.Count;
        public int Captures { get; private set; }
        public EveActorSnapshotScheduler(int maximumJobs, EveEntityPreparationBudget serializationBudget, Func<double> monotonicMs)
        { if (maximumJobs < 1) throw new ArgumentOutOfRangeException(); capacity = maximumJobs; budget = serializationBudget ?? throw new ArgumentNullException(nameof(serializationBudget)); clock = monotonicMs ?? throw new ArgumentNullException(nameof(monotonicMs)); }
        internal void RequestCapture(string scope, Func<EveFrozenActorSnapshot> freeze, Action cut, Action<IReadOnlyList<ActorState>> done, Action<Exception> failed)
        {
            // Share only captures that have not crossed their cut yet.
            if (!waitingScopes.TryGetValue(scope, out var job))
            {
                if (pending.Count >= capacity) throw new InvalidOperationException("Snapshot work capacity.");
                job = new Job { Scope = scope + ":" + checked(++nextJobId), QueryScope = scope, Freeze = freeze };
                pending.Add(job.Scope, job); waitingScopes.Add(scope, job); waiting.Enqueue(job);
            }
            job.Requests.Add(new Request { Cut = cut, Done = done, Failed = failed });
        }
        public void AdvanceFrame()
        {
            frame = checked(frame + 1);
            if (waiting.Count > 0)
            {
                var job = waiting.Dequeue();
                waitingScopes.Remove(job.QueryScope);
                try { job.Frozen = job.Freeze(); Captures++; foreach (var request in job.Requests) request.Cut(); job.ReadyFrame = frame + 1; serializing.Enqueue(job); }
                catch (Exception error) { Fail(job, error); }
                return; // Capture freeze and serialization never consume the same host frame budget.
            }
            int steps = 0; double started = clock(); int visits = serializing.Count;
            while (serializing.Count > 0 && steps < budget.StepsPerFrame && (steps == 0 || clock() - started < budget.MillisecondsPerFrame))
            {
                var job = serializing.Dequeue(); if (job.ReadyFrame > frame) { serializing.Enqueue(job); if (--visits <= 0) break; continue; }
                try
                {
                    if (job.Frozen.Work.Count > 0) { job.Frozen.Work.Dequeue()(); steps++; }
                    if (job.Frozen.Work.Count == 0) { var snapshot = job.Frozen.Complete(); foreach (var request in job.Requests) request.Done(snapshot); pending.Remove(job.Scope); }
                    else serializing.Enqueue(job);
                }
                catch (Exception error) { Fail(job, error); }
                visits = serializing.Count;
            }
        }
        private void Fail(Job job, Exception error) { pending.Remove(job.Scope); foreach (var request in job.Requests) request.Failed(error); }
    }
    public interface EveIFrozenEntityReplicationPart
    { Func<byte[]> FreezeReplication(); }
}
