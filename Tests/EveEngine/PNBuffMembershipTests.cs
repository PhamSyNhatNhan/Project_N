using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using Newtonsoft.Json.Linq;
using static EveSessionSmokeTests;

// Fixed-clock trace replay runs against both the saved baseline and the candidate.
public static class PNBuffMembershipTests
{
    private sealed class Probe : PNBuffBehavior
    {
        internal Action<PNBuffContext, string> Observe;
        internal bool Broken;
        private int stacks = 1;
        public override bool EventDrivenReplication => true;
        public override bool CanTrigger => true;
        public override double? StackCount => stacks;
        private void Publish() { Presentation.Publish(new PNBuffNumbers(topRight: stacks)); Presentation.PublishVisual(new JObject { ["stacks"] = stacks }); }
        public override void OnApply(PNBuffContext c, JToken data)
        {
            c.SetDuration(8); c.SetClock(PNBuffClock.Entity); Publish(); Observe(c, "apply");
            if (Broken) throw new InvalidOperationException("membership apply fixture");
        }
        public override bool OnReapply(PNBuffContext c, PNBuffApplyRequest request) { stacks++; Publish(); Observe(c, "reapply"); return true; }
        public override bool OnFull(PNBuffContext c, PNBuffApplyRequest request) { Publish(); Observe(c, "full"); return true; }
        public override bool CanChangeStacks => true;
        public override void ChangeStacks(PNBuffContext c, int delta) { stacks += delta; Publish(); Observe(c, "stacks"); }
        public override void Trigger(PNBuffContext c) { stacks++; Publish(); Presentation.Cue("tick"); Observe(c, "trigger"); }
        public override void OnBehaviorStarted(PNBuffContext c, bool migrated) => OnApply(c, null);
        public override void OnContributionsChanged(PNBuffContext c) => Observe(c, "contribution");
        public override void OnEnding(PNBuffContext c, PNBuffEndReason reason, PNBuffSnapshot state) => Observe(c, "ending:" + reason);
        public override void OnCleanup(PNBuffContext c) => Observe(c, "cleanup");
    }

    public static void Run()
    {
        var trace = new List<string>();
        var hub = new EveRegistryHub(); new EveBuiltInContentModule().Register(hub); new PNContentModule().Register(hub);
        var single = EveId.Parse("test:membership_single"); var multi = EveId.Parse("test:membership_multi");
        var alternate = EveId.Parse("test:membership_alternate"); var broken = EveId.Parse("test:membership_broken");
        var made = new List<Probe>();
        PNBuffCollection collection = null; PNBuffPublisher publisher = null;
        double now = 0; bool nested = false;
        var field = typeof(PNBuffCollection).GetField("orderedInstances", BindingFlags.NonPublic | BindingFlags.Instance);
        if (Environment.GetEnvironmentVariable("PN_R1_REQUIRE_INDEX") == "1") Check(field != null, "R1 candidate index exists");
        int observations = 0, unexpectedErrors = 0;
        void Inspect(string where)
        {
            if (collection == null) return;
            observations++;
            var dictionary = (IDictionary)typeof(PNBuffCollection).GetField("instances", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(collection);
            var values = dictionary.Values.Cast<PNBuffInstance>().OrderBy(x => x.Handle.InstanceId).ToArray();
            if (field != null) Check(((IEnumerable)field.GetValue(collection)).Cast<PNBuffInstance>().SequenceEqual(values), "Membership consistent inside " + where);
            var snapshot = collection.ReplicationInstances();
            Check(snapshot.SequenceEqual(values.Where(x => !x.Ending && x.Behavior != null)), "Replication membership at " + where);
            if (snapshot.Length != 0) Check(!ReferenceEquals(snapshot, collection.ReplicationInstances()), "Each nonempty capture owns its snapshot at " + where);
            var stable = snapshot.ToArray();
            trace.Add("read:" + where + ":" + string.Join(",", snapshot.Select(x => x.Handle.InstanceId)) + ":" +
                string.Join(",", collection.Snapshot().Select(x => x.Handle.InstanceId)) + ":" + string.Join(",", collection.PresentationSnapshot().Select(x => x.InstanceId)));
            if (publisher != null && !publisher.IsDisposed && !nested)
            {
                nested = true;
                try { publisher.Capture(now, .75, 1.25); } finally { nested = false; }
            }
            Check(snapshot.SequenceEqual(stable), "Nested capture preserves outer snapshot at " + where);
        }
        foreach (var id in new[] { single, multi, alternate, broken })
        {
            var selected = id;
            hub.Buffs().Register(new PNBuffDefinition(id, EveId.Parse("test:membership_impl"), _ =>
            {
                var probe = new Probe { Broken = selected == broken, Observe = (c, where) =>
                {
                    trace.Add("behavior:" + selected + ":" + c.Handle.InstanceId + ":" + where);
                    if (c.IsEnding) Check(!c.Collection.ReplicationInstances().Any(x => x.Handle.Equals(c.Handle)), "Ending hidden before dictionary remove");
                    Inspect(where);
                }};
                made.Add(probe); return probe;
            }, null, multipleInstances: id == multi, maximumInstances: id == multi ? 3 : 1));
        }
        hub.Seal(); var session = Guid.Parse("af77415b-93e8-4208-8c1a-7b1041f8a610");
        collection = new PNBuffCollection(hub, session);
        var source = new PNBuffSource(session, new EveEntityId(123));
        publisher = new PNBuffPublisher(collection, new EveEntityId(456), generation: 77, authoritativeTime: () => now);
        collection.Failed += e => { trace.Add("failure:" + e.Message); if (e.Message != "membership apply fixture") unexpectedErrors++; };
        collection.PresentationChanged += change =>
        {
            var s = change.State;
            trace.Add("presentation:" + change.Kind + ":" + change.Sequence + ":" + (s == null ? "reset" :
                s.InstanceId + ":" + s.ProfileId + ":" + s.GroupOrder + ":" + s.Numbers.TopRight + ":" + s.Suspended));
            Inspect("presentation:" + change.Kind);
        };
        publisher.Message += message => { trace.Add("wire:" + Convert.ToBase64String(PNBuffWireCodec.Encode(message))); Inspect("message:" + message.Kind); };
        publisher.OptionalCue += message => trace.Add("cue:" + Convert.ToBase64String(PNBuffWireCodec.Encode(message)));
        try
        {
            var first = collection.Apply(new PNBuffApplyRequest(single, source));
            collection.Apply(new PNBuffApplyRequest(single, source));
            var handles = new List<PNBuffHandle>();
            for (int i = 0; i < 3; i++) handles.Add(collection.Apply(new PNBuffApplyRequest(multi, source)).Handle);
            collection.Apply(new PNBuffApplyRequest(multi, source));
            collection.ChangeStacks(first.Handle, 2); collection.Trigger(first.Handle);
            now = .5; collection.Advance(.5, .75, 1.25); publisher.Capture(now, .75, 1.25); Inspect("advance");
            collection.Suspend(true); collection.Suspend(false);
            var oldWriter = made[0].Presentation;
            using (var lease = collection.Contribute("membership override", new PNBuffOverrideContribution(alternate, 1), exact: first.Handle, selection: PNBuffSelection.Current))
            { Inspect("override"); oldWriter.Publish(new PNBuffNumbers(topRight: 999)); }
            using (var lease = collection.Contribute("membership shrink", new PNBuffNumberContribution(typeof(PNBuffInstanceLimitContract), "maximum", -2), multi))
                Inspect("limit shrink");
            var retained = collection.ReplicationInstances(); var retainedMembers = retained.ToArray();
            using (var immunity = collection.Immune(new[] { multi }, purge: true)) Inspect("purge");
            Check(retained.SequenceEqual(retainedMembers), "Ending does not mutate previously returned snapshot");
            var failed = collection.Apply(new PNBuffApplyRequest(broken, source));
            Check(failed.Status == PNBuffStatus.Failed && !failed.Handle.IsAlive, "Failed apply does not retain membership");
            // Mutation from an event is queued while the current snapshot is being consumed.
            bool queued = false;
            Action<PNBuffPresentationChange> mutate = change =>
            {
                if (queued || change.Kind != PNBuffPresentationChangeKind.Updated) return;
                queued = true;
                var receipt = collection.Apply(new PNBuffApplyRequest(multi, source));
                Check(receipt.Status == PNBuffStatus.Pending, "Observer apply is queued");
                collection.Remove(first.Handle); Inspect("queued mutation");
            };
            collection.PresentationChanged += mutate;
            collection.Trigger(first.Handle);
            collection.PresentationChanged -= mutate;
            Check(queued, "Observer mutation scenario actually ran");
            collection.TargetDied(); Inspect("death");
            collection.Apply(new PNBuffApplyRequest(multi, source));
            collection.Dispose(); Inspect("dispose");
            Check(collection.ReplicationInstances().Length == 0 && collection.PresentationSnapshot().Count == 0, "Dispose retires all membership");
        }
        finally { collection.Dispose(); publisher.Dispose(); }
        Check(observations > 30, "Membership visibility checked throughout lifecycle and callbacks");
        Check(unexpectedErrors == 0, "Callback assertions and nested captures report no unexpected failures");
        var output = Environment.GetEnvironmentVariable("PN_R1_TRACE");
        if (!string.IsNullOrWhiteSpace(output)) File.WriteAllLines(output, trace);
    }
}
