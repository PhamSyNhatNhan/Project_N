using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using static EveSessionSmokeTests;

public static class PNBuffReplicationTests
{
    private static readonly EveId Buff = EveId.Parse("test:replica_buff"), Profile2 = EveId.Parse("test:replica_profile2"), Presenter = EveId.Parse("test:replica_presenter");
    private sealed class Empty : PNBuffBehavior { }
    private sealed class DynamicCounter : PNBuffBehavior
    {
        private PNBuffContext context;
        public override double? StackCount => Math.Floor(context?.Time ?? 0);
        public override void OnApply(PNBuffContext c, JToken input) { context = c; }
    }
    private sealed class ScheduledCounter : PNBuffBehavior
    {
        private int count;
        public override bool EventDrivenReplication => true;
        public override double? StackCount => count;
        public override void OnApply(PNBuffContext c, JToken input) { c.Schedule("tick", 1); }
        public override void OnScheduled(PNBuffContext c, string key) { count++; }
    }
    private sealed class Display : PNBuffBehavior
    {
        public override bool EventDrivenReplication => true;
        public override double? StackCount => 2;
        public override void OnApply(PNBuffContext c, JToken input) { c.SetClock(PNBuffClock.Entity); c.SetDuration(5); Presentation.Publish(new PNBuffNumbers(0, 2, null, 5)); Presentation.PublishVisual(new JObject { ["phase"] = 1 }); }
        public override void OnEnding(PNBuffContext c, PNBuffEndReason reason, PNBuffSnapshot final) => c.EndingCue("end");
        public override void Trigger(PNBuffContext c) => Presentation.Cue("important", true);
        public override bool CanTrigger => true;
    }
    private sealed class Probe : PNBuffPresenter
    {
        public static int Created, Active, Cues, Ends, Disposed; public static bool Throw;
        public Probe() { Created++; Active++; }
        public override void Update(IReadOnlyList<PNBuffWireState> states, double now) { if (Throw) throw new Exception("fixture presenter fault"); }
        public override void Cue(PNBuffCue cue) => Cues++;
        public override void Removed(PNBuffWireState state, PNBuffEndReason reason) => Ends++;
        public override void Dispose() { Active--; Disposed++; }
        public static void Reset() { Created = Active = Cues = Ends = Disposed = 0; Throw = false; }
    }
    private static EveRegistryHub Hub()
    {
        var hub = new EveRegistryHub(); new EveBuiltInContentModule().Register(hub); new PNContentModule().Register(hub);
        hub.Get<PNBuffPresenterDefinition>().Register(new PNBuffPresenterDefinition(Presenter, Presenter, _ => new Probe(), _ => { }, groupByProfile: true));
        hub.Buffs().Register(new PNBuffDefinition(Buff, EveId.Parse("test:display_impl"), _ => new Display(), null, multipleInstances: true, maximumInstances: 20, presenterId: Presenter));
        hub.Buffs().Register(new PNBuffDefinition(Profile2, EveId.Parse("test:empty_impl"), _ => new Empty(), null, presenterId: Presenter));
        hub.Buffs().Register(new PNBuffDefinition(EveId.Parse("test:dynamic_counter"), EveId.Parse("test:dynamic_counter_impl"), _ => new DynamicCounter(), null));
        hub.Buffs().Register(new PNBuffDefinition(EveId.Parse("test:scheduled_counter"), EveId.Parse("test:scheduled_counter_impl"), _ => new ScheduledCounter(), null));
        hub.Seal(); return hub;
    }
    private static PNBuffWireState State(long id = 1, long revision = 1, EveId profile = default)
        => new PNBuffWireState(id, revision, Buff, profile.IsValid ? profile : Buff, new EveEntityId(10), 2, false, new PNBuffNumbers(0, 2, null, 5), PNBuffClock.Entity, 3, 10, .5, 8, new JObject { ["phase"] = 1 });
    public static void Run()
    {
        var settings = (Newtonsoft.Json.JsonSerializerSettings)typeof(PNBuffWireCodec).GetMethod("Settings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
        void JsonParity<T>(T message) where T : class
        {
            var expected = JToken.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(new { Protocol = PNBuffWireCodec.ProtocolVersion, Message = message }, settings));
            var actual = JToken.Parse(System.Text.Encoding.UTF8.GetString(PNBuffWireCodec.Encode(message)));
            Check(JToken.DeepEquals(expected, actual), "Streaming Buff JSON preserves previous wire representation " + typeof(T).Name);
        }
        var paritySession = Guid.NewGuid(); var parityState = new PNBuffWireState(1, 2, Buff, Buff, new EveEntityId(10), 1, false,
            new PNBuffNumbers(long.MinValue, 0, null, long.MaxValue), PNBuffClock.Entity, .5, 1, .25, null, JObject.Parse("{'text':'đạn','array':[0,null,true]}"));
        var parityCue = new PNBuffCue(3, 1, "quoted\"cue", 1.5, payload: JObject.Parse("{'value':2}"));
        JsonParity(new PNBuffWireMessage(paritySession, new EveEntityId(20), 1, 4, PNBuffWireKind.Updated, new[] { parityState }, cues: new[] { parityCue }));
        JsonParity(new PNBuffOptionalCueMessage(paritySession, new EveEntityId(20), 1, parityCue));
        var hub = Hub(); var session = Guid.NewGuid(); var target = new EveEntityId(20); Probe.Reset();
        using (var dynamic = new PNBuffCollection(hub, session))
        using (var publisher = new PNBuffPublisher(dynamic, target))
        {
            dynamic.Apply(new PNBuffApplyRequest(EveId.Parse("test:dynamic_counter"), new PNBuffSource(session, new EveEntityId(10))));
            publisher.Capture(0); dynamic.Advance(1); publisher.Capture(1);
            Check(publisher.Snapshot().States.Single().Stacks == 1, "custom dynamic getter retains scanning without event-driven opt-in");
        }
        using (var scheduled = new PNBuffCollection(hub, session))
        using (var publisher = new PNBuffPublisher(scheduled, target))
        {
            scheduled.Apply(new PNBuffApplyRequest(EveId.Parse("test:scheduled_counter"), new PNBuffSource(session, new EveEntityId(10))));
            publisher.Capture(0); scheduled.Advance(1); publisher.Capture(1);
            Check(publisher.Snapshot().States.Single().Stacks == 1, "event-driven scheduled stack mutation invalidates publication without numeric UI slots");
        }
        using (var clocked = new PNBuffCollection(hub, session))
        {
            double time = 0; int updates = 0;
            using (var publisher = new PNBuffPublisher(clocked, target, authoritativeTime: () => time - clocked.RemainingGameDelta))
            {
                publisher.Message += m => { if (m.Kind == PNBuffWireKind.Updated) updates++; };
                clocked.Apply(new PNBuffApplyRequest(Buff, new PNBuffSource(session, new EveEntityId(10)))); publisher.Capture(0);
                time = .25; clocked.Advance(.25); publisher.Capture(time);
                Check(updates == 0 && Math.Abs(publisher.Snapshot().States.Single().VisualTime(time) - .25) < 1e-9, "Unchanged Buff clock extrapolates without periodic Updated heartbeat");
                publisher.Capture(time, 0, 0); time = 1.25; clocked.Advance(1, 0, 0); publisher.Capture(time, 0, 0);
                Check(updates == 1 && Math.Abs(publisher.Snapshot().States.Single().VisualTime(time) - .25) < 1e-9, "Clock stop publishes one anchor and remains frozen");
                publisher.Capture(time, 1, 1); time = 1.75; clocked.Advance(.5); publisher.Capture(time);
                Check(updates == 2 && Math.Abs(publisher.Snapshot().States.Single().VisualTime(time) - .75) < 1e-9, "Clock resume reanchors once and resumes extrapolation");
            }
        }
        PNBuffWireMessage Packet(long seq, PNBuffWireKind kind, PNBuffWireState state = null, long epoch = 1, long removed = 0, PNBuffCue cue = null)
            => new PNBuffWireMessage(session, target, epoch, seq, kind, state == null ? null : new[] { state }, removed, removed > 0 ? PNBuffEndReason.Expired : (PNBuffEndReason?)null, cue == null ? null : new[] { cue });
        var encoded = PNBuffWireCodec.Encode(Packet(0, PNBuffWireKind.Snapshot, State()));
        var decoded = PNBuffWireCodec.Decode<PNBuffWireMessage>(encoded);
        Check(decoded.SessionId == session && decoded.States[0].Numbers.TopLeft == 0 && decoded.States[0].Numbers.BottomLeft == null && decoded.States[0].ProfileId == Buff, "Buff wire codec roundtrip keeps identities and null/zero");
        Reject(() => PNBuffWireCodec.Decode<PNBuffWireMessage>(encoded, 2), "Codec rejects oversized envelope before parsing");
        using (var replica = new PNBuffReplica(hub, session, target))
        {
            int requested = 0; replica.SnapshotRequested += () => requested++;
            replica.Receive(Packet(0, PNBuffWireKind.Snapshot), 10);
            replica.Receive(Packet(2, PNBuffWireKind.Updated, State(1, 2)), 10);
            Check(replica.Snapshot().Count == 0, "Replica buffers reordered delta");
            replica.Receive(Packet(1, PNBuffWireKind.Added, State()), 10);
            Check(replica.Sequence == 2 && replica.Snapshot()[0].Revision == 2 && Probe.Active == 1, "Replica drains contiguous reordered stream");
            replica.Receive(Packet(1, PNBuffWireKind.Added, State()), 10);
            Check(replica.Sequence == 2 && Probe.Created == 1, "Duplicate delta does not recreate presenter");
            replica.Receive(Packet(4, PNBuffWireKind.Updated, State(1, 4)), 10); replica.Advance(10.2);
            Check(requested == 1 && replica.Status == PNBuffReplicaStatus.Resyncing, "Gap requests bounded snapshot");
            replica.Receive(Packet(5, PNBuffWireKind.Updated, State(1, 5)), 10.3);
            replica.Receive(Packet(3, PNBuffWireKind.Snapshot, State(1, 3)), 10.4);
            Check(replica.Sequence == 5 && replica.Status == PNBuffReplicaStatus.Live && Probe.Created == 1, "Snapshot reconcile retains presenter and replays buffered newer deltas");
            replica.Receive(Packet(3, PNBuffWireKind.Snapshot, State(1, 3)), 10.5);
            Check(replica.Snapshot()[0].Revision == 5 && Probe.Created == 1, "Duplicate snapshot ignored");
            var endCue = new PNBuffCue(1, 1, "end", 10.5);
            replica.Receive(Packet(6, PNBuffWireKind.Removed, removed: 1, cue: endCue), 10.5);
            replica.Receive(new PNBuffOptionalCueMessage(session, target, 1, new PNBuffCue(2, 1, "tick", 10.5)), 10.5);
            replica.Receive(Packet(5, PNBuffWireKind.Updated, State(1, 5)), 10.6);
            Check(replica.Snapshot().Count == 0 && Probe.Active == 0 && Probe.Cues == 1 && Probe.Ends == 1, "Removal cue exactly once, late update/cue cannot resurrect");
            replica.Receive(Packet(0, PNBuffWireKind.Snapshot, State(2), epoch: 2), 11);
            replica.Receive(Packet(99, PNBuffWireKind.Added, State(3), epoch: 1), 11);
            Check(replica.Epoch == 2 && replica.Sequence == 0 && replica.Snapshot().Single().InstanceId == 2, "Old epoch rejected despite higher sequence");
            replica.Receive(Packet(1, PNBuffWireKind.Snapshot, epoch: 2), 11);
            Check(Probe.Ends == 1 && Probe.Cues == 1 && Probe.Active == 0, "Snapshot disappearance is silent cleanup");
        }
        Probe.Reset();
        using (var faulty = new PNBuffReplica(hub, session, target))
        {
            int errors = 0; faulty.PresenterFailed += _ => errors++; Probe.Throw = true;
            faulty.Receive(Packet(0, PNBuffWireKind.Snapshot, State()), 10);
            faulty.Advance(11); faulty.Advance(12);
            Check(faulty.Snapshot().Count == 1 && errors == 1 && Probe.Active == 0, "Presenter fault isolated and cleaned once");
            faulty.Receive(Packet(0, PNBuffWireKind.Snapshot, State()), 12);
            Check(errors == 1, "Duplicate snapshot does not retry failed presenter");
            Probe.Throw = false; faulty.Receive(Packet(1, PNBuffWireKind.Snapshot, State(1, 2)), 12);
            Check(Probe.Active == 1 && errors == 1, "New snapshot permits presenter recovery");
            faulty.Receive(Packet(2, PNBuffWireKind.Updated, State(1, 3, EveId.Parse("test:missing"))), 12);
            Check(faulty.Status == PNBuffReplicaStatus.Failed && Probe.Active == 0, "Unknown profile terminates replica with cleanup");
        }
        Probe.Reset();
        using (var headless = new PNBuffReplica(hub, session, target, render: false))
        {
            headless.Receive(Packet(0, PNBuffWireKind.Snapshot, State()), 10);
            Check(headless.Snapshot().Count == 1 && Probe.Created == 0, "Headless replica creates no presenters");
        }
        using (var retries = new PNBuffReplica(hub, session, target, policy: new PNBuffReplicaPolicy(maximumResyncAttempts: 2, snapshotTimeout: 1)))
        {
            retries.Receive(Packet(0, PNBuffWireKind.Opened), 0); retries.Advance(1); retries.Advance(2);
            Check(retries.Status == PNBuffReplicaStatus.Failed, "Snapshot timeout exhausts retries");
        }
        Probe.Reset();
        using (var lost = new PNBuffReplica(hub, session, target))
        {
            int cues = 0; lost.CuePresented += _ => cues++;
            lost.Receive(Packet(0, PNBuffWireKind.Snapshot, State()), 10);
            // Lost seq1 is a removal with end cue. Seq2 alone cannot be applied.
            lost.Receive(Packet(2, PNBuffWireKind.Added, State(2)), 10); lost.Advance(10.2);
            lost.Receive(Packet(2, PNBuffWireKind.Snapshot, State(2)), 10.3);
            Check(lost.Status == PNBuffReplicaStatus.Live && lost.Snapshot().Single().InstanceId == 2 && cues == 0 && Probe.Ends == 0 && Probe.Active == 1, "Lost removal recovered by silent snapshot, shared presenter retained");
            lost.Receive(new PNBuffOptionalCueMessage(session, target, 1, new PNBuffCue(90, 2, "tick", 1)), 10);
            Check(cues == 0, "Expired optional cue discarded");
            lost.Receive(new PNBuffOptionalCueMessage(session, target, 1, new PNBuffCue(91, 2, "tick", 10)), 10);
            lost.Receive(new PNBuffOptionalCueMessage(session, target, 1, new PNBuffCue(91, 2, "tick", 10)), 10);
            Check(cues == 1, "Active optional cue duplicate is suppressed");
        }
        using (var reentrant = new PNBuffReplica(hub, session, target, render: false))
        {
            reentrant.Receive(Packet(0, PNBuffWireKind.Snapshot), 0);
            reentrant.PresentationChanged += change => { if (change.Kind == PNBuffPresentationChangeKind.Added) reentrant.Receive(Packet(2, PNBuffWireKind.Updated, State(1, 2)), 0); };
            reentrant.Receive(Packet(1, PNBuffWireKind.Added, State()), 0);
            Check(reentrant.Sequence == 2 && reentrant.Snapshot().Single().Revision == 2, "Observer-created network input queues at boundary");
        }
        // Same authoritative events through direct loopback and deterministic shuffled/duplicate delivery.
        for (int seed = 0; seed < 8; seed++)
        {
            using var collection = new PNBuffCollection(hub, session);
            using var publisher = new PNBuffPublisher(collection, target);
            using var direct = new PNBuffReplica(hub, session, target);
            using var proxy = new PNBuffReplica(hub, session, target);
            var packets = new List<PNBuffWireMessage>(); int directCues = 0, proxyCues = 0;
            direct.CuePresented += _ => directCues++; proxy.CuePresented += _ => proxyCues++;
            direct.Receive(publisher.Snapshot(), 0); proxy.Receive(publisher.Snapshot(), 0);
            publisher.Message += m => { packets.Add(m); direct.Receive(m, 0); };
            var receipt = collection.Apply(new PNBuffApplyRequest(Buff, new PNBuffSource(session, new EveEntityId(10))));
            collection.Trigger(receipt.Handle); collection.Trigger(receipt.Handle);
            collection.Remove(receipt.Handle);
            var random = new Random(seed);
            foreach (var message in packets.Concat(packets).OrderBy(_ => random.Next())) proxy.Receive(PNBuffWireCodec.Decode<PNBuffWireMessage>(PNBuffWireCodec.Encode(message)), 0);
            Check(direct.Status == PNBuffReplicaStatus.Live && proxy.Status == PNBuffReplicaStatus.Live && direct.Sequence == proxy.Sequence && direct.Snapshot().Count == proxy.Snapshot().Count && directCues == proxyCues && directCues == 3,
                "Seeded replica parity incl guaranteed ending cues seed " + seed);
            Check(Probe.Active == 0 && direct.PresenterCount == 0 && proxy.PresenterCount == 0, "Seeded renderer resources return to zero " + seed);
        }
        ReferencesAndMax();
    }
    private static void ReferencesAndMax()
    {
        var definitions = new EveRegistry<EveStatDefinition>(); var hp = EveId.Parse("test:hp"); var speed = EveId.Parse("test:speed");
        definitions.Register(new EveStatDefinition(hp, EveStatKind.Resource, EveMaxChangePolicy.PreserveRatio));
        definitions.Register(new EveStatDefinition(speed, EveStatKind.Number)); definitions.Seal();
        var set = new EveStatSet(definitions); set.AddBase(new EveResourceStat(hp, 100, 33.33333333333333, EveMaxChangePolicy.PreserveRatio)); set.AddBase(new EveBaseStat(speed, 1));
        long current = BitConverter.DoubleToInt64Bits(set.GetCurrent(hp));
        for (int i = 0; i < 50; i++) { set.SetSource(EveId.Parse("test:unrelated"), new[] { new EveStatModifier(speed, .2, 1) }); set.RemoveSource(EveId.Parse("test:unrelated")); }
        Check(BitConverter.DoubleToInt64Bits(set.GetCurrent(hp)) == current, "Unchanged resource max preserves Current bits");
        var hub = Hub();
        var pending = new PNBuffDefinition(EveId.Parse("test:pending"), EveId.Parse("test:pending_impl"), _ => new Empty(), null);
        var refs = new PNContentReferenceContext(hub, pending: new EveIIdentified[] { pending });
        refs.Require<PNBuffDefinition>(pending.Id, "customReference");
        Check(true, "Schema reference sees same-batch pending definition");
        Reject(() => refs.Require<PNBuffDefinition>(JValue.CreateNull(), "customReference"), "Null reference yields format failure");
        var document = new EveContentExtensionDocument("fixture", "addon", Array.Empty<string>(), new JObject());
        Reject(() => new PNContentReferenceContext(hub, document).Require<PNBuffDefinition>(Buff, "customReference"), "Schema callback enforces foreign dependency");
        new PNContentReferenceContext(hub, new EveContentExtensionDocument("fixture", "addon", new[] { "test" }, new JObject())).Require<PNBuffDefinition>(Buff, "customReference");
        Check(true, "Explicit dependency admits schema reference");
        var derived = pending.Derive(EveId.Parse("test:derived"), null, null, false, 1, PNBuffInstanceScope.Target, true);
        Check(derived.ImplementationId == pending.ImplementationId, "Derive retains explicit implementation identity");
        var alternative = new PNBuffDefinition(pending.Id, pending.ImplementationId, token => new Empty(), null);
        Check(JToken.DeepEquals(JToken.FromObject(pending), JToken.FromObject(alternative)), "Definition fingerprint ignores compiler lambda method names");
    }
}
