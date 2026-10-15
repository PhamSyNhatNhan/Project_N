using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using static EveSessionSmokeTests;

public static class EveHotPathDiagnosticTests
{
    private sealed class FlowBehavior : PNBuffBehavior
    {
        public int Reads, Stacks = 1;
        public override bool EventDrivenReplication => true;
        public override double? StackCount { get { Reads++; return Stacks; } }
        public override void OnApply(PNBuffContext c, JToken input) { c.SetDuration(10); c.Schedule("tick", 1, 1); Presentation.PublishVisual(new JObject { ["v"] = 1 }); }
        public override void ChangeStacks(PNBuffContext c, int delta) { Stacks += delta; Presentation.Publish(new PNBuffNumbers(topRight: Stacks)); }
        public override void OnScheduled(PNBuffContext c, string key) { ChangeStacks(c, 1); Presentation.Cue("guaranteed", true); Presentation.Cue("optional"); }
    }
    private static (string[], int, EveHotPathMetrics.Event[]) Flow(bool diagnostic)
    {
        EveHotPathMetrics.Enabled = false; EveHotPathMetrics.CountOnly = diagnostic;
        EveHotPathMetrics.Drain(); EveHotPathMetrics.DrainEvents();
        var hub = new EveRegistryHub(); new EveBuiltInContentModule().Register(hub); new PNContentModule().Register(hub);
        var id = EveId.Parse("test:diagnostic"); FlowBehavior behavior = null;
        hub.Buffs().Register(new PNBuffDefinition(id, EveId.Parse("test:diagnostic_impl"), _ => behavior = new FlowBehavior(), null)); hub.Seal();
        var session = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var messages = new List<string>(); double time = 0; bool reentered = false;
        using (var collection = new PNBuffCollection(hub, session))
        using (var publisher = new PNBuffPublisher(collection, new EveEntityId(2), 3, () => time))
        {
            publisher.Message += m =>
            {
                // Fixed network identity makes independent fixture bytes directly comparable.
                var states = m.States.Select(s => new PNBuffWireState(1, s.Revision, s.BuffId, s.ProfileId, s.SourceId, s.Stacks, s.Suspended, s.Numbers, s.Clock, s.BuffTime, s.AnchorTime, s.Rate, s.Expiration, s.DynamicData, s.PayloadVersion));
                var cues = m.Cues.Select(c => new PNBuffCue(c.Id, 1, c.Name, c.At, c.MaximumAge, c.Payload));
                var normalized = new PNBuffWireMessage(m.SessionId, m.TargetId, m.Epoch, m.Sequence, m.Kind, states, m.RemovedId == 0 ? 0 : 1, m.EndReason, cues);
                messages.Add(Convert.ToBase64String(PNBuffWireCodec.Encode(normalized)));
                if (m.Kind == PNBuffWireKind.Updated && !reentered) { reentered = true; var h = collection.Snapshot()[0].Handle; collection.ChangeStacks(h, 1); }
            };
            publisher.OptionalCue += m => messages.Add("optional:" + m.Cue.Name);
            var receipt = collection.Apply(new PNBuffApplyRequest(id, new PNBuffSource(session, new EveEntityId(1))));
            collection.ChangeStacks(receipt.Handle, 1);
            time = 1; collection.Advance(1); publisher.Capture(time);
            collection.Remove(receipt.Handle, PNBuffEndReason.Bypass, true);
        }
        var records = EveHotPathMetrics.DrainEvents(); EveHotPathMetrics.Drain();
        EveHotPathMetrics.Enabled = EveHotPathMetrics.CountOnly = false;
        return (messages.ToArray(), behavior.Reads, records);
    }
    private static PNBuffWireState State(double? stacks = 1, PNBuffNumbers numbers = default,
        double rate = 1, PNBuffClock clock = PNBuffClock.Game, double? expiration = 10,
        double time = 1, JToken data = null, int version = 1, string profile = "test:profile", bool suspended = false)
        => new PNBuffWireState(1, 1, EveId.Parse("test:buff"), EveId.Parse(profile), new EveEntityId(1), stacks, suspended,
            numbers, clock, time, 1, rate, expiration, data, version);
    public static void Run()
    {
        EveHotPathMetrics.Enabled = EveHotPathMetrics.CountOnly = false; EveHotPathMetrics.TraceAllocations = true;
        EveHotPathMetrics.Drain(); EveHotPathMetrics.DrainEvents();
        using (EveHotPathMetrics.Measure("disabled")) { }
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { using (EveHotPathMetrics.Measure("disabled")) { } EveHotPathMetrics.Increment("disabled"); EveHotPathMetrics.Record(default); }
        Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "Diagnostic disabled hot path allocates no bytes, even TraceAllocations alone.");
        Check(EveHotPathMetrics.Drain().Count == 0 && EveHotPathMetrics.DrainEvents().Length == 0, "Disabled diagnostics record nothing.");
        EveHotPathMetrics.TraceAllocations = false; EveHotPathMetrics.CountOnly = true;
        using (EveHotPathMetrics.Measure("count")) { }
        EveHotPathMetrics.Increment("count", 3);
        var count = EveHotPathMetrics.Drain()["count"];
        Check(count.Calls == 4 && count.Ticks == 0 && count.ExclusiveTicks == 0, "CountOnly does not timestamp scopes.");
        EveHotPathMetrics.CountOnly = false; EveHotPathMetrics.Enabled = true;
        using (EveHotPathMetrics.Measure("parent"))
        { using (EveHotPathMetrics.Measure("child")) { using (EveHotPathMetrics.Measure("grandchild")) { var t = Stopwatch.GetTimestamp(); while (Stopwatch.GetTimestamp() - t < Stopwatch.Frequency / 1000) { } } } }
        var nested = EveHotPathMetrics.Drain();
        Check(nested["parent"].Ticks >= nested["child"].Ticks && nested["child"].Ticks >= nested["grandchild"].Ticks, "Nested inclusive scopes enclose child time.");
        Check(nested["parent"].ExclusiveTicks == nested["parent"].Ticks - nested["child"].Ticks, "Parent exclusive subtracts direct child exactly once.");
        Check(nested["child"].ExclusiveTicks == nested["child"].Ticks - nested["grandchild"].Ticks, "Nested child subtracts grandchild, without double subtraction.");
        var old = State(data: JObject.Parse("{'a':1}"));
        Action<PNBuffWireState, PNBuffDiagnosticReason> reason = (state, flag) => Check(old.DiagnosticDifference(state, 1) == (long)flag, "Diagnostic reason " + flag);
        reason(State(stacks: 2, data: JObject.Parse("{'a':1}")), PNBuffDiagnosticReason.Stacks);
        reason(State(numbers: new PNBuffNumbers(topRight: 2), data: JObject.Parse("{'a':1}")), PNBuffDiagnosticReason.Numbers);
        reason(State(rate: 2, data: JObject.Parse("{'a':1}")), PNBuffDiagnosticReason.Rate);
        reason(State(clock: PNBuffClock.Entity, data: JObject.Parse("{'a':1}")), PNBuffDiagnosticReason.Clock);
        reason(State(expiration: 11, data: JObject.Parse("{'a':1}")), PNBuffDiagnosticReason.Expiration);
        reason(State(time: 2, data: JObject.Parse("{'a':1}")), PNBuffDiagnosticReason.VisualTime);
        reason(State(data: JObject.Parse("{'a':2}")), PNBuffDiagnosticReason.DynamicData);
        reason(State(version: 2, data: JObject.Parse("{'a':1}")), PNBuffDiagnosticReason.PayloadVersion);
        reason(State(profile: "test:other", data: JObject.Parse("{'a':1}")), PNBuffDiagnosticReason.Profile);
        reason(State(suspended: true, data: JObject.Parse("{'a':1}")), PNBuffDiagnosticReason.Suspended);
        Check(old.DiagnosticDifference(State(time: 1 + 1e-9, data: JObject.Parse("{'a':1}")), 1) == 0, "Visual clock tolerance unchanged.");
        var changed = State(stacks: 2, rate: 2, data: JObject.Parse("{'a':1}"));
        Check(old.DiagnosticDifference(changed, 1) == (long)(PNBuffDiagnosticReason.Stacks | PNBuffDiagnosticReason.Rate), "Multiple Updated reasons retained.");
        var message = new PNBuffWireMessage(Guid.NewGuid(), new EveEntityId(1), 1, 1, PNBuffWireKind.Updated, new[] { changed });
        EveHotPathMetrics.Enabled = false; byte[] before = PNBuffWireCodec.Encode(message);
        EveHotPathMetrics.Enabled = true; old.DiagnosticDifference(changed, 1); byte[] after = PNBuffWireCodec.Encode(message);
        Check(before.SequenceEqual(after), "Diagnostic field diff leaves encoded message bytes unchanged.");
        for (int i = 0; i < EveHotPathMetrics.MaximumEventsPerDrain + 1; i++) EveHotPathMetrics.Record(default);
        Check(EveHotPathMetrics.DrainEvents().Length == EveHotPathMetrics.MaximumEventsPerDrain && EveHotPathMetrics.Drain()["diagnostic.events-dropped"].Calls == 1, "Event recording bounded with explicit loss count.");
        EveHotPathMetrics.Enabled = EveHotPathMetrics.CountOnly = EveHotPathMetrics.TraceAllocations = false;
        EveHotPathMetrics.Drain(); EveHotPathMetrics.DrainEvents();
        var disabled = Flow(false); var enabled = Flow(true);
        Check(disabled.Item1.SequenceEqual(enabled.Item1), "Independent live fixtures retain lifecycle/cue order, revision, sequence and normalized wire bytes with diagnostics.");
        Check(disabled.Item2 == enabled.Item2, "Diagnostics do not read dynamic behavior getter extra times, including nested mutation.");
        Check(enabled.Item3.Any(e => e.Origin == "Changed" && (e.Causes & (long)PNBuffCollection.DiagnosticCause.Visual) != 0), "Visual provenance survives queued Notify.");
        Check(enabled.Item3.Any(e => e.Reasons == (long)PNBuffDiagnosticReason.GuaranteedCue), "Guaranteed cue revision has a distinct reason without field changes.");
    }
}
