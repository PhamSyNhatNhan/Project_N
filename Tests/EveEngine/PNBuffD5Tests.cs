using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EveEngine;
using static EveSessionSmokeTests;

// D5 fix checks (RulesVersion 71, plan .utmp/Step6/D5/FixPlan.md v3.1). The former baseline record ("no retry after a getter
// failure", RulesVersion 70) is deliberately replaced: an exception escaping a StackCount call made by the publisher scan is
// isolated per behavior incarnation, the next outermost Capture retries it once, and a failed retry ends that instance with
// RuntimeError. Exceptions outside that call keep their previous propagation.
public static class PNBuffD5Tests
{
    private sealed class Probe : PNBuffBehavior
    {
        internal readonly bool Polled;
        internal int Live = 1, Reads, Thrown, ThrowRemaining;
        internal bool ThrowAlways, ThrowEventDriven, OnEndingCalled;
        internal Action OnRead;   // one-shot hook run inside the getter before it returns or throws
        internal Probe(bool polled) { Polled = polled; }
        public override bool EventDrivenReplication { get { if (ThrowEventDriven) throw new InvalidOperationException("D5 event-driven fixture"); return !Polled; } }
        public override bool CanChangeStacks => true;
        public override double? StackCount
        {
            get
            {
                Reads++;
                var hook = OnRead; OnRead = null; hook?.Invoke();
                if (ThrowAlways || ThrowRemaining > 0) { if (ThrowRemaining > 0) ThrowRemaining--; Thrown++; throw new InvalidOperationException("D5 getter fixture"); }
                return Live;
            }
        }
        public override void ChangeStacks(PNBuffContext context, int delta) { Live += delta; }
        public override void OnEnding(PNBuffContext context, PNBuffEndReason reason, PNBuffSnapshot finalState) { OnEndingCalled = true; }
    }

    private sealed class Rig : IDisposable
    {
        internal PNBuffCollection Collection;
        internal PNBuffPublisher Publisher;
        internal readonly List<PNBuffWireMessage> Messages = new List<PNBuffWireMessage>();
        internal readonly List<Exception> Failures = new List<Exception>();
        internal readonly Dictionary<string, Probe> Probes = new Dictionary<string, Probe>();
        internal readonly Dictionary<string, PNBuffHandle> Handles = new Dictionary<string, PNBuffHandle>();
        internal readonly Dictionary<string, EveId> Ids = new Dictionary<string, EveId>();
        internal Action<PNBuffWireMessage> OnMessage;   // one-shot
        internal Action<Exception> OnFailed;            // one-shot
        internal bool AllowMutation = true;
        internal int AltThrowRemaining;   // first N StackCount reads of the next "alt" behavior throw
        internal double Now;
        internal readonly EveId Alternate = EveId.Parse("test:d5_alt");

        // order entries: "name:q" (quiet, event-driven) or "name:p" (polled). Applied in this order.
        internal Rig(string seed, params string[] order)
        {
            var hub = new EveRegistryHub(); new EveBuiltInContentModule().Register(hub); new PNContentModule().Register(hub);
            foreach (var entry in order)
            {
                var name = entry.Split(':')[0]; bool polled = entry.Split(':')[1] == "p";
                var id = EveId.Parse("test:d5_" + name); Ids[name] = id; var key = name;
                hub.Buffs().Register(new PNBuffDefinition(id, EveId.Parse("test:d5_" + name + "_impl"), _ => Probes[key] = new Probe(polled), null));
            }
            hub.Buffs().Register(new PNBuffDefinition(Alternate, EveId.Parse("test:d5_alt_impl"), _ => Probes["alt"] = new Probe(false) { ThrowRemaining = AltThrowRemaining }, null));
            hub.Seal();
            var session = Guid.Parse(seed);
            Collection = new PNBuffCollection(hub, session, canMutate: () => AllowMutation);
            var source = new PNBuffSource(session, new EveEntityId(1));
            Publisher = new PNBuffPublisher(Collection, new EveEntityId(2), 3, () => Now);
            Publisher.Message += m => { Messages.Add(m); var hook = OnMessage; OnMessage = null; hook?.Invoke(m); };
            Collection.Failed += e => { Failures.Add(e); var hook = OnFailed; OnFailed = null; hook?.Invoke(e); };
            foreach (var entry in order)
            {
                var name = entry.Split(':')[0];
                var receipt = Collection.Apply(new PNBuffApplyRequest(Ids[name], source));
                Check(receipt.Status == PNBuffStatus.Applied, "D5 fixture applies " + name);
                Handles[name] = receipt.Handle;
            }
        }
        public void Dispose() { Publisher.Dispose(); Collection.Dispose(); }
        internal PNBuffReceipt Change(string name, int delta) => Collection.ChangeStacks(Handles[name], delta);
        internal void Capture() => Publisher.Capture(Now, 1, 1);
        internal int Published(string name)
        {
            var state = Publisher.Snapshot().States.FirstOrDefault(x => x.BuffId == Ids[name]);
            return state == null || !state.Stacks.HasValue ? -1 : (int)state.Stacks.Value;
        }
        internal List<PNBuffWireMessage> Since(int mark) => Messages.Skip(mark).ToList();
        internal int Updates(int mark, string name) => Since(mark).Count(m => m.Kind == PNBuffWireKind.Updated && m.States.Any(s => s.BuffId == Ids[name]));
        // Failure entries of the publisher: (exists, ending).
        internal (bool Exists, bool Ending) Entry(string name)
        {
            var field = typeof(PNBuffPublisher).GetField("failures", BindingFlags.NonPublic | BindingFlags.Instance);
            var map = (System.Collections.IDictionary)field.GetValue(Publisher);
            var key = Handles[name].InstanceId;
            if (!map.Contains(key)) return (false, false);
            var entry = map[key];
            return (true, (bool)entry.GetType().GetField("Ending", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(entry));
        }
        internal int FailureEntries()
        {
            var field = typeof(PNBuffPublisher).GetField("failures", BindingFlags.NonPublic | BindingFlags.Instance);
            return ((System.Collections.IDictionary)field.GetValue(Publisher)).Count;
        }
    }

    private static string Seed(string suffix) => "52345678-1234-1234-1234-1234" + suffix;

    public static void Run()
    {
        HealthyReadCounts();
        SameScanIsolation();
        TransientRecoveryPublishes();
        TransientRecoveryUnchanged();
        PersistentFailureEndsInstance();
        DeferredEndAfterScanWhenIdle();
        DeferredEndQueuedInsideNotify();
        DeferredEndNotAllowedThenRetried();
        DeferredEndRevalidates();
        OrdinaryNestedFailureOuterSucceeds();
        OrdinaryNestedFailureOuterFails();
        RetryGuardSkipsNestedScan();
        RecoveredIsOrdinaryInNestedCapture();
        HealthyReentryHasNoGuard();
        FailedListenerCaptureDoesNotEscalate();
        OverrideWhilePendingIsHealthy();
        EventDrivenFailureStillPropagates();
        OverrideInsideOuterNewIncarnationWaits();
        RetrySuccessThenOrdinaryFailureInOneOuter();
        OutOfScopeExceptionAfterEndingDefersEnd();
    }

    // Healthy path keeps the baseline short-circuit read counts: unchanged = 1 read (compare), changed = 2 (compare + state).
    private static void HealthyReadCounts()
    {
        using (var rig = new Rig(Seed("0001abcd"), "a:q", "b:q", "c:q"))
        {
            int a = rig.Probes["a"].Reads, c = rig.Probes["c"].Reads;
            rig.Change("c", +1);
            Check(rig.Probes["a"].Reads - a == 1 && rig.Probes["c"].Reads - c == 2 && rig.Published("c") == 2 && rig.Failures.Count == 0,
                "D5 healthy scan keeps baseline StackCount read counts (unchanged 1, changed 2) and publishes the change");
        }
    }

    // A fails for the first time while C changes: C is published in the same scan; A becomes Pending; the original exception is reported.
    private static void SameScanIsolation()
    {
        using (var rig = new Rig(Seed("0002abcd"), "a:q", "b:q", "c:q"))
        {
            rig.Probes["a"].ThrowRemaining = 1;
            int mark = rig.Messages.Count;
            var receipt = rig.Change("c", +1);
            var entry = rig.Entry("a");
            Check(receipt.Status == PNBuffStatus.StacksChanged && rig.Updates(mark, "c") == 1 && rig.Published("c") == 2,
                "D5 instances after a failing getter are published in the same scan");
            Check(entry.Exists && !entry.Ending && rig.Failures.Count == 1 && rig.Failures[0] is InvalidOperationException && rig.Failures[0].Message == "D5 getter fixture",
                "D5 a first getter failure makes the incarnation Pending and reports the original exception");
        }
    }

    // A has an unpublished change and fails once; the next outermost Capture (no mutation) retries it once and publishes it.
    private static void TransientRecoveryPublishes()
    {
        using (var rig = new Rig(Seed("0003abcd"), "a:q", "b:q", "c:q"))
        {
            rig.Probes["a"].ThrowRemaining = 1;
            rig.Change("a", +1);
            Check(rig.Published("a") == 1 && rig.Entry("a").Exists, "D5 failed change stays unpublished and Pending");
            int mark = rig.Messages.Count;
            rig.Capture();
            Check(rig.Updates(mark, "a") == 1 && rig.Published("a") == 2 && !rig.Entry("a").Exists && rig.Failures.Count == 1,
                "D5 transient failure: the retry at the next outermost Capture publishes the pending change and clears the entry");
            int reads = rig.Probes["a"].Reads; mark = rig.Messages.Count;
            rig.Capture();
            Check(rig.Probes["a"].Reads == reads && rig.Messages.Count == mark, "D5 after recovery a no-mutation Capture does not evaluate again");
        }
    }

    // Original D5 shape: A unchanged fails once while C changes; the retry clears A without spurious messages.
    private static void TransientRecoveryUnchanged()
    {
        using (var rig = new Rig(Seed("0004abcd"), "a:q", "b:q", "c:q"))
        {
            rig.Probes["a"].ThrowRemaining = 1;
            rig.Change("c", +1);
            int reads = rig.Probes["a"].Reads, mark = rig.Messages.Count;
            rig.Capture();
            Check(rig.Probes["a"].Reads == reads + 1 && rig.Messages.Count == mark && !rig.Entry("a").Exists && rig.Published("a") == 1 && rig.Published("c") == 2,
                "D5 retry of an unchanged instance reads once, clears the entry and emits nothing for healthy instances");
        }
    }

    // A keeps failing: the retry fails and exactly one End(RuntimeError) is submitted; other instances and the collection stay alive.
    private static void PersistentFailureEndsInstance()
    {
        using (var rig = new Rig(Seed("0005abcd"), "a:q", "b:q", "c:q"))
        {
            var probe = rig.Probes["a"];
            probe.ThrowAlways = true;
            rig.Change("a", +1);
            Check(rig.Entry("a").Exists && rig.Collection.Contains(rig.Handles["a"]), "D5 first failure keeps the instance alive (Pending)");
            int mark = rig.Messages.Count;
            rig.Capture();
            var removed = rig.Since(mark).Where(m => m.Kind == PNBuffWireKind.Removed).ToList();
            Check(removed.Count == 1 && removed[0].EndReason == PNBuffEndReason.RuntimeError && !rig.Collection.Contains(rig.Handles["a"]),
                "D5 a failed retry ends the instance once with RuntimeError");
            Check(!rig.Collection.IsDisposed && !rig.Publisher.IsDisposed && rig.Collection.Contains(rig.Handles["b"]) && rig.Collection.Contains(rig.Handles["c"]) && rig.FailureEntries() == 0,
                "D5 ending an instance keeps the collection, the publisher and the other instances");
            // Reports: first failure, failed retry, and the PNBuffSnapshot read inside EndCore (OnEnding skipped, documented).
            Check(rig.Failures.Count == 3 && !probe.OnEndingCalled, "D5 a persistently failing getter is reported a bounded number of times; OnEnding is skipped");
            mark = rig.Messages.Count; rig.Capture(); rig.Capture();
            Check(rig.Messages.Count == mark && rig.Failures.Count == 3, "D5 no further reports or ends after the instance ended");
        }
    }

    // Idle coordinator (direct External Capture): the end runs after the scan, so C's Updated precedes A's Removed.
    private static void DeferredEndAfterScanWhenIdle()
    {
        using (var rig = new Rig(Seed("0006abcd"), "a:q", "b:q", "c:q", "d:p"))
        {
            rig.Probes["a"].ThrowAlways = true;
            rig.Change("a", +1);   // first failure: Pending
            int mark = rig.Messages.Count;
            rig.Probes["c"].Live += 1;   // an unpublished change seen by the next scan (the polled neighbour keeps it scanning)
            rig.Capture();
            var since = rig.Since(mark);
            int updatedC = since.FindIndex(m => m.Kind == PNBuffWireKind.Updated && m.States.Any(s => s.BuffId == rig.Ids["c"]));
            int removedA = since.FindIndex(m => m.Kind == PNBuffWireKind.Removed);
            Check(updatedC >= 0 && removedA > updatedC, "D5 the end of a failed retry is deferred until after the scan (no mid-scan membership change)");
        }
    }

    // Inside Notify (mutation command running): the end is queued after the current command.
    private static void DeferredEndQueuedInsideNotify()
    {
        using (var rig = new Rig(Seed("0007abcd"), "a:q", "b:q", "c:q"))
        {
            rig.Probes["a"].ThrowAlways = true;
            rig.Change("a", +1);   // first failure (Pending)
            int mark = rig.Messages.Count;
            var receipt = rig.Change("c", +1);   // Changed capture inside Notify retries A and queues the end
            var since = rig.Since(mark);
            int updatedC = since.FindIndex(m => m.Kind == PNBuffWireKind.Updated && m.States.Any(s => s.BuffId == rig.Ids["c"]));
            int removedA = since.FindIndex(m => m.Kind == PNBuffWireKind.Removed);
            Check(receipt.Status == PNBuffStatus.StacksChanged && updatedC >= 0 && removedA > updatedC && !rig.Collection.Contains(rig.Handles["a"]),
                "D5 inside Notify the end is queued after the current command and completes before the mutation returns");
        }
    }

    // Mutation not allowed: the end is not executed, the entry stays Ending and is resubmitted at a later outermost Capture.
    private static void DeferredEndNotAllowedThenRetried()
    {
        using (var rig = new Rig(Seed("0008abcd"), "a:q", "b:q", "c:q"))
        {
            rig.Probes["a"].ThrowAlways = true;
            rig.Change("a", +1);
            rig.AllowMutation = false;
            rig.Capture();
            Check(rig.Collection.Contains(rig.Handles["a"]) && rig.Entry("a").Ending, "D5 end refused while mutation is not allowed; entry kept Ending");
            int failures = rig.Failures.Count;
            rig.Capture();
            Check(rig.Collection.Contains(rig.Handles["a"]) && rig.Failures.Count == failures, "D5 an Ending incarnation is not evaluated again");
            rig.AllowMutation = true;
            rig.Capture();
            Check(!rig.Collection.Contains(rig.Handles["a"]) && rig.FailureEntries() == 0, "D5 refused end is resubmitted at a later outermost Capture");
        }
    }

    // The internal end command revalidates the incarnation when it runs.
    private static void DeferredEndRevalidates()
    {
        using (var rig = new Rig(Seed("0009abcd"), "a:q", "b:q", "c:q"))
        {
            PNBuffReplicationEndOutcome outcome = PNBuffReplicationEndOutcome.Ended;
            rig.Collection.EndAfterReplicationFailure(rig.Handles["a"].InstanceId, new Probe(false), new InvalidOperationException("stale"), o => outcome = o);
            Check(outcome == PNBuffReplicationEndOutcome.Stale && rig.Collection.Contains(rig.Handles["a"]), "D5 end command refuses a stale incarnation");
            rig.Collection.EndAfterReplicationFailure(987654, rig.Probes["a"], new InvalidOperationException("gone"), o => outcome = o);
            Check(outcome == PNBuffReplicationEndOutcome.Stale, "D5 end command refuses a missing instance");
        }
    }

    // ORDINARY outer evaluation whose getter calls a nested Capture in which the same incarnation fails; the outer read succeeds.
    private static void OrdinaryNestedFailureOuterSucceeds()
    {
        using (var rig = new Rig(Seed("0010abcd"), "a:q", "b:q", "c:q", "d:p"))
        {
            var a = rig.Probes["a"];
            a.OnRead = () => { a.ThrowRemaining = 1; rig.Capture(); };   // nested scan (gate open: polled) consumes the throw
            rig.Capture();
            Check(rig.Entry("a").Exists && !rig.Entry("a").Ending && rig.Collection.Contains(rig.Handles["a"]),
                "D5 an outer ORDINARY success keeps the Pending recorded by the nested failure (no clear, no escalation)");
            rig.Capture();
            Check(!rig.Entry("a").Exists, "D5 that Pending is retried and cleared at the next outermost Capture");
        }
    }

    // Same, but the outer read fails too: still Pending, no Ending in that outer attempt.
    private static void OrdinaryNestedFailureOuterFails()
    {
        using (var rig = new Rig(Seed("0011abcd"), "a:q", "b:q", "c:q", "d:p"))
        {
            var a = rig.Probes["a"];
            a.OnRead = () => { a.ThrowRemaining = 2; rig.Capture(); };
            rig.Capture();
            Check(rig.Entry("a").Exists && !rig.Entry("a").Ending && rig.Collection.Contains(rig.Handles["a"]) && rig.Failures.Count == 2,
                "D5 failures within one outer attempt never escalate to Ending");
            rig.Capture();
            Check(!rig.Entry("a").Exists && rig.Collection.Contains(rig.Handles["a"]), "D5 the retry in the next outer attempt succeeds and clears it");
        }
    }

    // A RETRY evaluation whose getter calls Capture: the nested scan skips that incarnation.
    private static void RetryGuardSkipsNestedScan()
    {
        using (var rig = new Rig(Seed("0012abcd"), "a:q", "b:q", "c:q", "d:p"))
        {
            var a = rig.Probes["a"];
            a.ThrowRemaining = 1; rig.Capture();   // first failure (polled scan)
            Check(rig.Entry("a").Exists, "D5 retry-guard fixture starts Pending");
            int reads = a.Reads;
            a.OnRead = () => rig.Capture();   // runs inside the RETRY evaluation
            rig.Capture();
            Check(a.Reads - reads == 1 && !rig.Entry("a").Exists, "D5 the nested scan does not evaluate an incarnation whose retry is in progress");
        }
    }

    // After a successful retry (entry cleared), a Message handler Capture with the gate open evaluates it as ORDINARY.
    private static void RecoveredIsOrdinaryInNestedCapture()
    {
        using (var rig = new Rig(Seed("0013abcd"), "a:q", "b:q", "c:q", "d:p"))
        {
            var a = rig.Probes["a"];
            a.ThrowRemaining = 1; rig.Change("a", +1);    // Pending with an unpublished change (live 2)
            rig.OnMessage = m => { if (m.States.Any(s => s.BuffId == rig.Ids["a"])) { a.Live += 1; rig.Capture(); } };
            rig.Capture();
            Check(rig.Published("a") == 3 && !rig.Entry("a").Exists, "D5 a recovered incarnation is evaluated as ORDINARY by a nested Capture (not stale)");
            // Variant: the ORDINARY re-evaluation fails -> new Pending, no escalation in the same outer attempt.
            rig.OnMessage = m => { if (m.States.Any(s => s.BuffId == rig.Ids["a"])) { a.ThrowRemaining = 1; a.Live += 1; rig.Capture(); } };
            a.Live += 1; rig.Capture();
            Check(rig.Entry("a").Exists && !rig.Entry("a").Ending && rig.Collection.Contains(rig.Handles["a"]),
                "D5 an ORDINARY failure after recovery opens a new episode without a second RETRY or Ending in that outer attempt");
        }
    }

    // Healthy getters keep the baseline re-entry behavior: no guard, a nested Capture evaluates the same incarnation.
    private static void HealthyReentryHasNoGuard()
    {
        using (var rig = new Rig(Seed("0014abcd"), "a:q", "b:q", "c:q", "d:p"))
        {
            var a = rig.Probes["a"];
            int reads = a.Reads;
            a.OnRead = () => rig.Capture();
            rig.Capture();
            Check(a.Reads - reads == 2 && rig.Failures.Count == 0 && rig.FailureEntries() == 0, "D5 healthy re-entry is not guarded (outer and nested both evaluate)");
        }
    }

    // A Failed listener calling Capture (gate open) right after a first failure: the new Pending is not retried in this outer attempt.
    private static void FailedListenerCaptureDoesNotEscalate()
    {
        using (var rig = new Rig(Seed("0015abcd"), "a:q", "b:q", "c:q", "d:p"))
        {
            var a = rig.Probes["a"];
            a.ThrowAlways = true;
            bool seenRecorded = false; int nestedReads = -1;
            rig.OnFailed = e => { seenRecorded = rig.Entry("a").Exists; int before = a.Reads; rig.Capture(); nestedReads = a.Reads - before; };
            rig.Capture();
            Check(seenRecorded && nestedReads == 0 && rig.Entry("a").Exists && !rig.Entry("a").Ending && rig.Collection.Contains(rig.Handles["a"]),
                "D5 the entry is recorded before Report and a Pending created in this outer attempt is not retried in it");
            rig.Capture();
            Check(!rig.Collection.Contains(rig.Handles["a"]), "D5 that Pending is retried (and ended) in the next outer attempt");
        }
    }

    // Override while Pending: the new behavior incarnation is Healthy and is not ended by the old entry.
    private static void OverrideWhilePendingIsHealthy()
    {
        using (var rig = new Rig(Seed("0016abcd"), "a:q", "b:q", "c:q"))
        {
            rig.Probes["a"].ThrowAlways = true;
            rig.Change("a", +1);
            Check(rig.Entry("a").Exists, "D5 override fixture starts Pending");
            using (var lease = rig.Collection.Contribute("d5 override", new PNBuffOverrideContribution(rig.Alternate, 1), exact: rig.Handles["a"], selection: PNBuffSelection.Current))
            {
                rig.Capture(); rig.Capture();
                Check(rig.Probes.ContainsKey("alt") && rig.Collection.Contains(rig.Handles["a"]) && !rig.Entry("a").Exists,
                    "D5 a new behavior incarnation after an override is Healthy; the old entry does not end it");
            }
        }
    }

    // Override of a Pending incarnation happens inside an outer Capture that holds the eligibility snapshot; the new
    // incarnation fails (twice) and a nested Capture runs: it must not be RETRIED/ended in this outer attempt.
    private static void OverrideInsideOuterNewIncarnationWaits()
    {
        using (var rig = new Rig(Seed("0018abcd"), "x:q", "a:q", "b:q", "d:p"))
        {
            var a = rig.Probes["a"]; var x = rig.Probes["x"];
            a.ThrowAlways = true; rig.Capture();
            Check(rig.Entry("a").Exists && !rig.Entry("a").Ending, "D5 override fixture starts with the old incarnation Pending");
            rig.AltThrowRemaining = 2;
            PNBuffContributionLease lease = null;
            x.OnRead = () =>
            {
                lease = rig.Collection.Contribute("d5 override in outer", new PNBuffOverrideContribution(rig.Alternate, 1), exact: rig.Handles["a"], selection: PNBuffSelection.Current);
                rig.Capture();   // nested Capture after the new incarnation failed once (gate open: polled neighbour)
            };
            rig.Capture();
            var alt = rig.Probes["alt"];
            Check(rig.Collection.Contains(rig.Handles["a"]) && rig.Entry("a").Exists && !rig.Entry("a").Ending && alt.Thrown == 1,
                "D5 a new incarnation created inside an outer attempt is not retried or ended in that attempt (one read failed, entry Pending)");
            alt.ThrowRemaining = 0;
            rig.Capture();
            Check(!rig.Entry("a").Exists && rig.Collection.Contains(rig.Handles["a"]), "D5 the new incarnation is retried in the next outer attempt and recovers");
            lease?.Dispose();
        }
    }

    // Retry succeeds (entry cleared) -> a Message handler sets a new ORDINARY failure -> a second nested Capture in the SAME outer.
    private static void RetrySuccessThenOrdinaryFailureInOneOuter()
    {
        using (var rig = new Rig(Seed("0019abcd"), "a:q", "b:q", "c:q", "d:p"))
        {
            var a = rig.Probes["a"];
            a.ThrowRemaining = 1; rig.Change("a", +1);   // Pending with an unpublished change
            int nestedReads = -1; bool done = false;
            rig.OnMessage = m =>
            {
                if (done || !m.States.Any(s => s.BuffId == rig.Ids["a"])) return;
                done = true;
                a.ThrowRemaining = 1;
                rig.Capture();                       // nested 1: ORDINARY evaluation fails -> new Pending
                int before = a.Reads;
                rig.Capture();                       // nested 2 in the same outer attempt: must not RETRY it
                nestedReads = a.Reads - before;
            };
            rig.Capture();
            Check(done && nestedReads == 0 && rig.Entry("a").Exists && !rig.Entry("a").Ending && rig.Collection.Contains(rig.Handles["a"]),
                "D5 retry success -> ORDINARY failure -> nested Capture in one outer attempt: no second RETRY, no Ending");
            rig.Capture();
            Check(!rig.Entry("a").Exists && rig.Collection.Contains(rig.Handles["a"]), "D5 that new Pending is retried in the next outer attempt");
        }
    }

    // A retry fails (Ending) and a later instance throws an exception OUTSIDE the StackCount call (checked revision overflow):
    // the original exception reaches the caller, the end is not submitted, a later normal outer submits it exactly once.
    private static void OutOfScopeExceptionAfterEndingDefersEnd()
    {
        using (var rig = new Rig(Seed("0020abcd"), "a:q", "b:q", "c:q", "d:p"))
        {
            rig.Probes["a"].ThrowAlways = true;
            rig.Capture();   // first failure: Pending
            var field = typeof(PNBuffPublisher).GetField("previous", BindingFlags.NonPublic | BindingFlags.Instance);
            var previous = (Dictionary<long, PNBuffWireState>)field.GetValue(rig.Publisher);
            long local = rig.Handles["b"].InstanceId;
            var good = previous[local];
            previous[local] = new PNBuffWireState(good.InstanceId, long.MaxValue, good.BuffId, good.ProfileId, good.SourceId, good.Stacks, good.Suspended,
                good.Numbers, good.Clock, good.BuffTime, good.AnchorTime, good.Rate, good.Expiration, good.DynamicData, good.PayloadVersion);
            rig.Probes["b"].Live += 1;   // makes B construct a new state: checked(old.Revision + 1) overflows
            bool overflow = false;
            try { rig.Capture(); } catch (OverflowException) { overflow = true; }
            Check(overflow && rig.Collection.Contains(rig.Handles["a"]) && rig.Entry("a").Exists && rig.Entry("a").Ending,
                "D5 an exception outside the StackCount call reaches the caller; the failed retry stays marked Ending and is not ended yet");
            previous[local] = good;
            int mark = rig.Messages.Count;
            rig.Capture();
            var removed = rig.Since(mark).Where(m => m.Kind == PNBuffWireKind.Removed && m.RemovedId != 0).ToList();
            Check(!rig.Collection.Contains(rig.Handles["a"]) && removed.Count == 1 && removed[0].EndReason == PNBuffEndReason.RuntimeError,
                "D5 a later outer attempt that completes normally ends it exactly once");
            rig.Capture(); rig.Capture();
            Check(rig.Since(mark).Count(m => m.Kind == PNBuffWireKind.Removed) == 1, "D5 no second end is submitted");
        }
    }

    // Out of scope: an exception from EventDrivenReplication (read by the gate) keeps the previous propagation.
    private static void EventDrivenFailureStillPropagates()
    {
        using (var rig = new Rig(Seed("0017abcd"), "a:q", "b:q", "c:q"))
        {
            rig.Probes["b"].ThrowEventDriven = true;
            bool thrown = false;
            try { rig.Capture(); } catch (InvalidOperationException e) { thrown = e.Message == "D5 event-driven fixture"; }
            rig.Probes["b"].ThrowEventDriven = false;
            Check(thrown && rig.FailureEntries() == 0, "D5 exceptions outside the StackCount call still propagate from a direct Capture");
        }
    }
}
