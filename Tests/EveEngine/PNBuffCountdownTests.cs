using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using static EveSessionSmokeTests;

// Replica-derived countdown opt-in (RulesVersion70). Legacy profiles keep host publication every second;
// opted-in profiles keep the slot null on the wire while running and derive it from replicated clock state.
public static class PNBuffCountdownTests
{
    private static readonly EveId Legacy = EveId.Parse("test:cd_legacy"), OptIn = EveId.Parse("test:cd_optin"),
        OptInOther = EveId.Parse("test:cd_optin_other"), LegacyOther = EveId.Parse("test:cd_legacy_other"), OptInSingle = EveId.Parse("test:cd_optin_single"),
        OptInEntity = EveId.Parse("test:cd_optin_entity"), LegacyEntity = EveId.Parse("test:cd_legacy_entity");

    // Mirrors PNBuffTimedBehavior: duration comes from the Apply/Reapply input; seconds callback publishes the slot.
    private sealed class Timed : PNBuffBehavior
    {
        public static int Callbacks;
        private long? seconds;
        private readonly PNBuffClock clock;
        public Timed(PNBuffClock clock = PNBuffClock.Game) { this.clock = clock; }
        public override bool EventDrivenReplication => true;
        public override double? StackCount => 1;
        private void Start(PNBuffContext c, JToken input)
        {
            c.SetClock(clock);
            c.SetDuration((double?)input?["duration"] ?? 5);
            Presentation.WatchRemainingSeconds(value => { Callbacks++; seconds = value; Send(); });
            Send();
        }
        private void Send() => Presentation.Publish(new PNBuffNumbers(topRight: 1, bottomRight: seconds));
        public override void OnApply(PNBuffContext c, JToken input) => Start(c, input);
        public override bool OnReapply(PNBuffContext c, PNBuffApplyRequest r) { c.SetDuration((double?)r.Input?["duration"] ?? 5); return true; }
        public override void OnBehaviorStarted(PNBuffContext c, bool migrated) => Start(c, null);
    }
    private sealed class Empty : PNBuffBehavior { public override bool EventDrivenReplication => true; }

    private static EveRegistryHub Hub(bool optIn = true)
    {
        var hub = new EveRegistryHub(); new EveBuiltInContentModule().Register(hub); new PNContentModule().Register(hub);
        PNBuffDefinition Make(EveId id, string impl, Func<JToken, PNBuffBehavior> factory, bool countdown, bool multiple = true)
        {
            var definition = new PNBuffDefinition(id, EveId.Parse(impl), factory, null, multipleInstances: multiple, maximumInstances: multiple ? 8 : 1, validateInput: _ => { });
            return countdown ? definition.WithCountdown(PNBuffCountdown.BottomRight) : definition;
        }
        hub.Buffs().Register(Make(Legacy, "test:cd_timed_impl_a", _ => new Timed(), false));
        hub.Buffs().Register(Make(OptIn, "test:cd_timed_impl_b", _ => new Timed(), optIn));
        hub.Buffs().Register(Make(OptInSingle, "test:cd_timed_impl_c", _ => new Timed(), optIn, false));
        hub.Buffs().Register(Make(OptInEntity, "test:cd_timed_impl_d", _ => new Timed(PNBuffClock.Entity), optIn));
        hub.Buffs().Register(Make(LegacyEntity, "test:cd_timed_impl_e", _ => new Timed(PNBuffClock.Entity), false));
        hub.Buffs().Register(Make(OptInOther, "test:cd_empty_impl_a", _ => new Empty(), true, false));
        hub.Buffs().Register(Make(LegacyOther, "test:cd_empty_impl_b", _ => new Empty(), false, false));
        hub.Seal(); return hub;
    }

    // One host (collection+publisher) feeding a replica through the actor lifecycle path.
    private sealed class Rig : IDisposable
    {
        public readonly PNBuffCollection Collection; public readonly PNBuffPublisher Publisher; public readonly PNBuffReplica Replica;
        public readonly List<PNBuffWireMessage> Messages = new List<PNBuffWireMessage>();
        public readonly List<PNBuffPresentationChange> Changes = new List<PNBuffPresentationChange>();
        public readonly List<Exception> Presenter = new List<Exception>();
        public double Time; public Guid Session; public readonly EveEntityId Source = new EveEntityId(10);
        public Rig(EveRegistryHub hub)
        {
            Session = Guid.NewGuid(); var target = new EveEntityId(20);
            Collection = new PNBuffCollection(hub, Session);
            Publisher = new PNBuffPublisher(Collection, target, authoritativeTime: () => Time - Collection.RemainingGameDelta);
            Replica = new PNBuffReplica(hub, Session, target);
            Replica.PresentationChanged += Changes.Add; Replica.PresenterFailed += Presenter.Add;
            Replica.ApplyLifecycle(Publisher.Snapshot(), 0);
            Publisher.Message += m => { Messages.Add(m); Replica.ApplyLifecycle(m, Time); };
        }
        public PNBuffHandle Apply(EveId id, double duration = 5)
        {
            var receipt = Collection.Apply(new PNBuffApplyRequest(id, new PNBuffSource(Session, Source), new JObject { ["duration"] = duration }));
            Check(receipt.Handle.IsAlive && receipt.Error == null, "countdown fixture apply " + id);
            Publisher.Capture(Time); return receipt.Handle;
        }
        public void Step(double seconds)
        { Time += seconds; Collection.Advance(seconds); Publisher.Capture(Time); Replica.Advance(Time); }
        // Entity-clock step: the Buff clock advances by seconds * scale and replication carries Rate = scale.
        public void StepScaled(double seconds, double scale)
        { Time += seconds; Collection.Advance(seconds, scale, 1); Publisher.Capture(Time, scale, 1); Replica.Advance(Time); }
        public long? ShownFirst() => Replica.PresentationSnapshot().First().Numbers.BottomRight;
        public int Updated(Func<PNBuffWireMessage, bool> predicate = null) => Messages.Count(m => m.Kind == PNBuffWireKind.Updated && (predicate == null || predicate(m)));
        public void Dispose() { Replica.Dispose(); Publisher.Dispose(); Collection.Dispose(); }
    }

    public static void Run()
    {
        Parser();
        Definition();
        Stream();
        Parity();
        SuspendAndOverride();
        Overflow();
        OverflowClockRecovery();
        Fingerprint();
        Delivery();
        EntityClockScale();
        JoinMidRun();
        ReapplyWhileSuspended();
        EpochReset();
    }

    // Entity clock: scale changes, stop and resume of the clock keep the derived number equal to the legacy path.
    private static void EntityClockScale()
    {
        using (var legacy = new Rig(Hub()))
        using (var opt = new Rig(Hub()))
        {
            legacy.Apply(LegacyEntity, 8); opt.Apply(OptInEntity, 8);
            double[] scales = { 1, 1, .5, .5, .5, .5, 2, 2, 2, 0, 0, 0, 1, 1, 1, .25, .25, 3, 3, 1, 1, 1 };
            long? previous = null; int heldSteps = 0;
            foreach (double scale in scales)
            {
                legacy.StepScaled(.25, scale); opt.StepScaled(.25, scale);
                long? a = legacy.Replica.PresentationSnapshot().FirstOrDefault()?.Numbers.BottomRight;
                long? b = opt.Replica.PresentationSnapshot().FirstOrDefault()?.Numbers.BottomRight;
                if (a != b)
                {
                    // Only a boundary-noise adjacent value is acceptable, never a lasting offset.
                    Check(a.HasValue && b.HasValue && Math.Abs(a.Value - b.Value) == 1, "Entity clock parity at scale " + scale + ": " + a + " vs " + b);
                }
                if (scale == 0) { Check(b == previous, "Stopped Entity clock holds the number (" + previous + " -> " + b + ")"); heldSteps++; }
                previous = b;
            }
            Check(heldSteps == 3, "Stopped-clock steps exercised");
            Check(opt.Replica.PresentationSnapshot().Count == 1 && opt.Updated() >= 1, "Rate changes are published (Updated) while the slot stays null");
            Check(opt.Publisher.Snapshot().States.Single().Numbers.BottomRight == null, "Wire slot stays null across scale changes");
        }
    }

    // A replica that joins mid-run (running or suspended) shows the right number immediately and keeps following.
    private static void JoinMidRun()
    {
        using (var rig = new Rig(Hub()))
        {
            rig.Apply(OptIn, 5); rig.Step(1.5);
            var late = new PNBuffReplica(Hub(), rig.Session, new EveEntityId(20));
            // Same registry content is required; build the late replica on the rig's own hub instead.
            late.Dispose();
        }
        var hub = Hub();
        using (var rig = new Rig(hub))
        {
            rig.Apply(OptIn, 5); rig.Step(1.5);
            using (var late = new PNBuffReplica(hub, rig.Session, new EveEntityId(20)))
            {
                late.ApplyLifecycle(rig.Publisher.Snapshot(), rig.Time);
                Check(late.PresentationSnapshot().Single().Numbers.BottomRight == 4, "Mid-run snapshot join shows the current derived value");
                late.Advance(rig.Time + 1);
                Check(late.PresentationSnapshot().Single().Numbers.BottomRight == 3, "Joined replica keeps counting down");
            }
        }
        hub = Hub();
        using (var rig = new Rig(hub))
        {
            rig.Apply(OptIn, 5); rig.Step(1.5);
            rig.Collection.Suspend(true); rig.Publisher.Capture(rig.Time);
            using (var late = new PNBuffReplica(hub, rig.Session, new EveEntityId(20)))
            {
                late.ApplyLifecycle(rig.Publisher.Snapshot(), rig.Time);
                Check(late.PresentationSnapshot().Single().Numbers.BottomRight == 4, "Suspended snapshot join shows the frozen host value");
                late.Advance(rig.Time + 3);
                Check(late.PresentationSnapshot().Single().Numbers.BottomRight == 4, "Frozen value holds for a joined replica");
            }
        }
    }

    // Duration changes while running and while suspended.
    private static void ReapplyWhileSuspended()
    {
        using (var rig = new Rig(Hub()))
        {
            rig.Apply(OptInSingle, 5); rig.Step(2);
            Check(rig.ShownFirst() == 3, "Value before running reapply");
            rig.Collection.Apply(new PNBuffApplyRequest(OptInSingle, new PNBuffSource(rig.Session, rig.Source), new JObject { ["duration"] = 20 }));
            rig.Publisher.Capture(rig.Time); rig.Replica.Advance(rig.Time);
            Check(rig.ShownFirst() == 20, "Running reapply publishes the new expiration and the number follows (" + rig.ShownFirst() + ")");
            rig.Step(1);
            Check(rig.ShownFirst() == 19, "Countdown continues after a running reapply");
        }
        using (var rig = new Rig(Hub()))
        {
            // Engine contract: a suspended target rejects a reapply, so the held countdown must stay untouched.
            rig.Apply(OptInSingle, 5); rig.Step(1.5);
            rig.Collection.Suspend(true); rig.Publisher.Capture(rig.Time);
            int messagesBefore = rig.Messages.Count;
            var reapply = rig.Collection.Apply(new PNBuffApplyRequest(OptInSingle, new PNBuffSource(rig.Session, rig.Source), new JObject { ["duration"] = 20 }));
            rig.Publisher.Capture(rig.Time); rig.Replica.Advance(rig.Time);
            Check(reapply.Status == PNBuffStatus.Rejected && rig.Messages.Count == messagesBefore, "Suspended target rejects a reapply and publishes nothing");
            var state = rig.Publisher.Snapshot().States.Single();
            Check(state.Suspended && state.Numbers.BottomRight == 4 && state.Expiration == 5, "Rejected reapply leaves the held number and expiration unchanged");
            rig.Step(2);
            Check(rig.ShownFirst() == 4, "Held number stays through the suspension");
            rig.Collection.Suspend(false); rig.Publisher.Capture(rig.Time); rig.Replica.Advance(rig.Time);
            Check(rig.ShownFirst() == 4, "Resume continues from the held value");
            rig.Step(1);
            Check(rig.ShownFirst() == 3, "Countdown continues after resume (" + rig.ShownFirst() + ")");
        }
    }

    // Epoch and profile changes replace the cached presentation instead of leaking stale derived numbers.
    private static void EpochReset()
    {
        var hub = Hub(); var session = Guid.NewGuid(); var target = new EveEntityId(20); var source = new EveEntityId(10);
        PNBuffWireMessage Snapshot(long epoch, PNBuffWireState state)
            => new PNBuffWireMessage(session, target, epoch, 0, PNBuffWireKind.Snapshot, new[] { state });
        // Instance IDs are monotonic across epochs of one authoritative session, so each epoch uses a new ID.
        PNBuffWireState State(long id, EveId profile, PNBuffNumbers numbers, double expiration, double buffTime, double anchor)
            => new PNBuffWireState(id, 1, profile, profile, source, 1, false, numbers, PNBuffClock.Game, buffTime, anchor, 1, expiration);
        using (var replica = new PNBuffReplica(hub, session, target))
        {
            replica.Receive(Snapshot(1, State(1, OptIn, default(PNBuffNumbers), 10, 0, 0)), 0);
            Check(replica.PresentationSnapshot().Single().Numbers.BottomRight == 10, "Epoch 1 derives from its state");
            replica.Advance(3);
            Check(replica.PresentationSnapshot().Single().Numbers.BottomRight == 7, "Epoch 1 counts down");
            replica.Receive(Snapshot(2, State(2, LegacyOther, new PNBuffNumbers(bottomRight: 42), 10, 0, 0)), 3);
            Check(replica.Epoch == 2 && replica.PresentationSnapshot().Single().Numbers.BottomRight == 42, "New epoch with a legacy profile shows the wire number, not a derived one");
            replica.Advance(6);
            Check(replica.PresentationSnapshot().Single().Numbers.BottomRight == 42, "Legacy slot is not derived after the epoch change");
            replica.Receive(Snapshot(3, State(3, OptIn, default(PNBuffNumbers), 100, 0, 50)), 60);
            Check(replica.Epoch == 3 && replica.PresentationSnapshot().Single().Numbers.BottomRight == 90, "Later epoch derives from its own anchor (" + replica.PresentationSnapshot().Single().Numbers.BottomRight + ")");
        }
    }

    // Standalone replica fed through delayed/reordered delivery; its clock is authoritative time, so network delay
    // must never make the derived number lag, only delay when cards and state changes become visible.
    private sealed class DelayedRig : IDisposable
    {
        public readonly PNBuffCollection Collection; public readonly PNBuffPublisher Publisher; public readonly PNBuffReplica Replica;
        private readonly List<KeyValuePair<double, PNBuffWireMessage>> queue = new List<KeyValuePair<double, PNBuffWireMessage>>();
        public double Time; public readonly Guid Session = Guid.NewGuid(); public int SnapshotRequests;
        public Func<PNBuffWireMessage, double> Delay = _ => 0;
        public DelayedRig(EveRegistryHub hub)
        {
            var target = new EveEntityId(20);
            Collection = new PNBuffCollection(hub, Session);
            Publisher = new PNBuffPublisher(Collection, target, authoritativeTime: () => Time - Collection.RemainingGameDelta);
            Replica = new PNBuffReplica(hub, Session, target);
            Replica.SnapshotRequested += () => SnapshotRequests++;
            Replica.Receive(Publisher.Snapshot(), 0);
            Publisher.Message += m => queue.Add(new KeyValuePair<double, PNBuffWireMessage>(Time + Delay(m), m));
        }
        public PNBuffHandle Apply(EveId id, double duration)
        {
            var receipt = Collection.Apply(new PNBuffApplyRequest(id, new PNBuffSource(Session, new EveEntityId(10)), new JObject { ["duration"] = duration }));
            Check(receipt.Handle.IsAlive && receipt.Error == null, "delayed fixture apply");
            Publisher.Capture(Time); Deliver(); return receipt.Handle;
        }
        public void Deliver()
        {
            foreach (var item in queue.Where(x => x.Key <= Time + 1e-9).OrderBy(x => x.Key).ThenBy(x => x.Value.Sequence).ToList())
            { queue.Remove(item); Replica.Receive(item.Value, Time); }
        }
        public void Step(double seconds) { Time += seconds; Collection.Advance(seconds); Publisher.Capture(Time); Deliver(); Replica.Advance(Time); }
        public long? Shown() => Replica.PresentationSnapshot().FirstOrDefault()?.Numbers.BottomRight;
        public int Cards => Replica.PresentationSnapshot().Count;
        public void Dispose() { Replica.Dispose(); Publisher.Dispose(); Collection.Dispose(); }
    }

    private static void Delivery()
    {
        // Late Added: no card until arrival, then the CURRENT derived value, not the starting value.
        using (var rig = new DelayedRig(Hub()))
        {
            rig.Delay = m => m.Kind == PNBuffWireKind.Added ? .7 : 0;
            rig.Apply(OptIn, 5); rig.Step(.25); rig.Step(.25);
            Check(rig.Cards == 0, "Late Added shows no card before arrival");
            rig.Step(.25);
            Check(rig.Cards == 1 && rig.Shown() == 5, "Late Added shows the current derived value on arrival (" + rig.Shown() + ")");
            rig.Step(.5);
            Check(rig.Shown() == 4 && rig.SnapshotRequests == 0, "Countdown follows authoritative time after a late Added");
        }
        // Late Removed: the number hides at expiry without ever showing zero or negative, card leaves on arrival.
        using (var rig = new DelayedRig(Hub()))
        {
            rig.Delay = m => m.Kind == PNBuffWireKind.Removed ? .4 : 0;
            rig.Apply(OptIn, 2); var seen = new List<long?>();
            for (int i = 0; i < 4; i++) { rig.Step(.5); seen.Add(rig.Shown()); }
            rig.Step(.25); seen.Add(rig.Shown());
            Check(seen.All(x => !x.HasValue || x.Value > 0), "Late Removed never shows zero or negative");
            Check(rig.Cards == 1 && rig.Shown() == null, "Expired Buff stays hidden while its Removed is in flight");
            rig.Step(.25);
            Check(rig.Cards == 0, "Card leaves when Removed arrives");
        }
        // Late suspend: counting continues during the delay, then the frozen host value is restored on arrival.
        using (var rig = new DelayedRig(Hub()))
        {
            rig.Delay = m => m.Kind == PNBuffWireKind.Updated ? .75 : 0;
            rig.Apply(OptIn, 5); rig.Step(1.5);
            Check(rig.Shown() == 4, "Value before late suspend");
            rig.Collection.Suspend(true); rig.Publisher.Capture(rig.Time);
            rig.Step(.5);
            Check(rig.Shown() == 3, "Replica keeps counting while the suspend message is in flight (" + rig.Shown() + ")");
            rig.Step(.25);
            Check(rig.Shown() == 4, "Late suspend restores the frozen host value on arrival (" + rig.Shown() + ")");
            rig.Step(1.5);
            Check(rig.Shown() == 4, "Frozen value holds after the late suspend");
        }
        // Jittered, reordered delivery inside the reorder window: same numbers as undelayed host time, no recovery.
        using (var rig = new DelayedRig(Hub()))
        {
            int n = 0; rig.Delay = _ => (++n * 37 % 11) * .004;
            rig.Apply(OptIn, 5); rig.Apply(OptInSingle, 6);
            for (int i = 0; i < 12; i++)
            {
                rig.Step(.25);
                foreach (var card in rig.Replica.PresentationSnapshot())
                {
                    double expiration = card.ProfileId == OptIn ? 5 : 6;
                    long? expected = expiration - rig.Time > 0 ? (long)Math.Ceiling(expiration - rig.Time) : (long?)null;
                    Check(card.Numbers.BottomRight == expected, "Jittered delivery keeps exact derived value at t=" + rig.Time + " for " + card.ProfileId);
                }
            }
            Check(rig.SnapshotRequests == 0 && rig.Replica.Status == PNBuffReplicaStatus.Live, "Small jitter triggers no snapshot recovery");
        }
    }

    private static void Parser()
    {
        Check(PNBuffContentBatch.Countdown(null) == null && PNBuffContentBatch.Countdown(JValue.CreateNull()) == null, "Absent or null countdown stays disabled");
        var parsed = PNBuffContentBatch.Countdown(JObject.Parse("{'slot':'TopLeft','rounding':'Ceiling','zero':'Hide'}"));
        Check(parsed.Slot == PNBuffCountdownSlot.TopLeft && parsed.Rounding == "Ceiling" && parsed.Zero == "Hide", "Countdown descriptor parses slot, rounding and zero");
        Reject(() => PNBuffContentBatch.Countdown(JObject.Parse("{'slot':'Middle','rounding':'Ceiling','zero':'Hide'}")), "unknown slot");
        Reject(() => PNBuffContentBatch.Countdown(JObject.Parse("{'slot':'BottomRight','rounding':'Floor','zero':'Hide'}")), "unsupported rounding");
        Reject(() => PNBuffContentBatch.Countdown(JObject.Parse("{'slot':'BottomRight','rounding':'Ceiling','zero':'Show'}")), "unsupported zero");
        Reject(() => PNBuffContentBatch.Countdown(JObject.Parse("{'slot':'BottomRight','rounding':'Ceiling'}")), "missing zero");
        Reject(() => PNBuffContentBatch.Countdown(JObject.Parse("{'slot':'BottomRight','rounding':'Ceiling','zero':'Hide','extra':1}")), "unknown descriptor field");
        Reject(() => PNBuffContentBatch.Countdown(new JValue("BottomRight")), "non-object descriptor");
    }

    private static void Definition()
    {
        var hub = new EveRegistryHub(); new EveBuiltInContentModule().Register(hub); new PNContentModule().Register(hub);
        var tag = EveId.Parse("test:cd_tag");
        var baseDefinition = new PNBuffDefinition(EveId.Parse("test:cd_base"), EveId.Parse("test:cd_base_impl"), _ => new Empty(), JObject.Parse("{'a':1}"),
            new[] { tag }, multipleInstances: true, maximumInstances: 3, scope: PNBuffInstanceScope.Source, canDispel: false, iconPath: "icon", glow: "glow", showInUI: false);
        var withCountdown = baseDefinition.WithCountdown(PNBuffCountdown.BottomRight);
        Check(baseDefinition.Countdown == null && withCountdown.Countdown != null && !ReferenceEquals(baseDefinition, withCountdown), "WithCountdown returns a new immutable definition");
        Check(withCountdown.Id == baseDefinition.Id && withCountdown.ImplementationId == baseDefinition.ImplementationId && withCountdown.MultipleInstances &&
              withCountdown.MaximumInstances == 3 && withCountdown.Scope == PNBuffInstanceScope.Source && !withCountdown.CanDispel && withCountdown.IconPath == "icon" &&
              withCountdown.Glow == "glow" && !withCountdown.ShowInUI && withCountdown.Tags.SequenceEqual(baseDefinition.Tags) && JToken.DeepEquals(withCountdown.Payload, baseDefinition.Payload),
              "WithCountdown preserves every definition component");
        var derived = withCountdown.Derive(EveId.Parse("test:cd_derived"), JObject.Parse("{'a':2}"), new[] { tag }, false, 1, PNBuffInstanceScope.Target, true);
        Check(derived.Countdown == null, "Derive never inherits the countdown descriptor");
        Check(derived.PresenterId == baseDefinition.PresenterId && derived.ImplementationId == baseDefinition.ImplementationId, "Derive keeps implementation while dropping the descriptor");
        Reject(() => withCountdown.ValidateInput(JObject.Parse("{'x':1}")), "copy keeps absent input validator behavior");
        Check(PNBuffCountdownMath.Compute(null, 0, out bool none) == null && !none, "No expiration shows no countdown");
        Check(PNBuffCountdownMath.Compute(5, 0, out _) == 5 && PNBuffCountdownMath.Compute(5, 1.2, out _) == 4 && PNBuffCountdownMath.Compute(5, 4, out _) == 1, "Countdown uses Ceiling of remaining seconds");
        Check(PNBuffCountdownMath.Compute(5, 5, out _) == null && PNBuffCountdownMath.Compute(5, 7, out _) == null, "Boundary is 1 to hidden, not 1 to 0");
        Check(PNBuffCountdownMath.Compute(9.2e18, 0, out bool maxOverflow) == 9200000000000000000L && !maxOverflow, "Largest representable valid value below Int64 limit is supported");
        Check(PNBuffCountdownMath.Compute(1e19, 0, out bool over) == null && over, "Value beyond Int64 hides and flags overflow");
    }

    // Opt-in stream: slot is null on the wire while running, no per-second callbacks or Updated messages.
    private static void Stream()
    {
        using (var rig = new Rig(Hub()))
        {
            Timed.Callbacks = 0;
            rig.Apply(OptIn, 5);
            Check(Timed.Callbacks == 0, "Opted-in profile never invokes the legacy seconds callback");
            var state = rig.Publisher.Snapshot().States.Single();
            Check(state.Numbers.BottomRight == null && state.Numbers.TopRight == 1, "Wire slot is null while running; other slots still published");
            Check(rig.ShownFirst() == 5, "Replica derives the initial countdown");
            for (int i = 0; i < 8; i++) rig.Step(.25);
            Check(rig.ShownFirst() == 3, "Replica shows the derived countdown while the wire slot stays null");
            Check(rig.Updated() == 0, "No Updated message is produced by seconds passing");
            for (int i = 0; i < 12; i++) rig.Step(.25);
            Check(rig.Updated() == 0 && rig.Replica.PresentationSnapshot().Count == 0, "Expiry removes the instance without countdown Updated messages");
        }
        using (var rig = new Rig(Hub()))
        {
            rig.Apply(OptIn, 5);
            var seen = new List<long?>();
            rig.Replica.PresentationChanged += c => { if (c.Kind == PNBuffPresentationChangeKind.Updated) seen.Add(c.State.Numbers.BottomRight); };
            int sequenceBefore = rig.Messages.Count; long revision = rig.Publisher.Snapshot().States.Single().Revision;
            rig.Step(.5); rig.Step(.5); rig.Step(.25);
            Check(seen.Count == 1 && seen[0] == 4, "Replica emits one UI update exactly at the integer boundary");
            rig.Step(.25); rig.Step(.25);
            Check(seen.SequenceEqual(new long?[] { 4 }), "No UI update inside a displayed second");
            rig.Step(.25);
            Check(seen.SequenceEqual(new long?[] { 4, 3 }), "Next boundary emits the next value");
            Check(rig.Messages.Count == sequenceBefore && rig.Publisher.Snapshot().States.Single().Revision == revision,
                "Countdown UI changes do not create wire messages, sequence or revision");
        }
        using (var rig = new Rig(Hub()))
        {
            // A long frame skips several boundaries at once; the gate must still produce the exact value.
            rig.Apply(OptIn, 5); rig.Step(.25);
            rig.Step(3.5);
            Check(rig.ShownFirst() == 2, "Large time jump shows the exact derived value (" + rig.ShownFirst() + ")");
            rig.Step(.5); rig.Step(.5);
            Check(rig.ShownFirst() == 1, "Value keeps following after a jump");
        }
        using (var rig = new Rig(Hub()))
        {
            Timed.Callbacks = 0; rig.Apply(Legacy, 5);
            for (int i = 0; i < 8; i++) rig.Step(.5);
            Check(Timed.Callbacks > 0 && rig.Updated() > 0, "Legacy profile keeps publishing seconds through the host callback");
        }
    }

    // Same behavior under legacy and opt-in: displayed value must match at every sampled authoritative time.
    private static void Parity()
    {
        foreach (double step in new[] { .25, .1 })
        {
            using (var legacy = new Rig(Hub()))
            using (var opt = new Rig(Hub()))
            {
                legacy.Apply(Legacy, 5); opt.Apply(OptIn, 5);
                long? Of(Rig rig) => rig.ShownFirst();
                Check(Of(legacy) == Of(opt), "Initial countdown parity");
                for (int i = 0; i < 60 && legacy.Collection.PresentationSnapshot().Count > 0; i++)
                {
                    legacy.Step(step); opt.Step(step);
                    long? a = legacy.Replica.PresentationSnapshot().FirstOrDefault()?.Numbers.BottomRight, b = opt.Replica.PresentationSnapshot().FirstOrDefault()?.Numbers.BottomRight;
                    if (a == b) continue;
                    // Only adjacent values within float noise of an integer boundary are acceptable (never a lasting offset).
                    double remaining = 5 - opt.Time; double distance = Math.Abs(remaining - Math.Round(remaining));
                    Check(a.HasValue && b.HasValue && Math.Abs(a.Value - b.Value) == 1 && distance <= 1e-8 * Math.Max(1, Math.Abs(opt.Time)),
                        "Countdown parity within boundary tolerance at t=" + opt.Time);
                }
                Check(legacy.Replica.PresentationSnapshot().Count == 0 && opt.Replica.PresentationSnapshot().Count == 0, "Both expire together");
            }
        }
        using (var rig = new Rig(Hub()))
        {
            // Hidden at expiry boundary: value 1 then removal, never a visible 0.
            rig.Apply(OptIn, 2); var values = new List<long?>();
            rig.Replica.PresentationChanged += c => values.Add(c.State.Numbers.BottomRight);
            rig.Step(.5); rig.Step(.5); rig.Step(.5);
            Check(values.All(x => x != 0), "Countdown never displays zero");
            long? beforeRemoval = null; rig.Replica.PresentationChanged += c => { if (c.Kind == PNBuffPresentationChangeKind.Removed) beforeRemoval = c.State.Numbers.BottomRight; };
            rig.Step(.5);
            Check(beforeRemoval == 1, "Removed event carries the last shown Numbers");
        }
    }

    private static void SuspendAndOverride()
    {
        using (var rig = new Rig(Hub()))
        {
            rig.Apply(OptIn, 5); rig.Step(1.5);
            Check(rig.ShownFirst() == 4, "Running value before suspend");
            rig.Collection.Suspend(true); rig.Publisher.Capture(rig.Time);
            var suspended = rig.Publisher.Snapshot().States.Single();
            Check(suspended.Suspended && suspended.Numbers.BottomRight == 4, "Suspend freezes the last tracked value in the existing slot");
            rig.Step(3);
            Check(rig.ShownFirst() == 4, "Frozen value stays through suspension");
            rig.Collection.Suspend(false); rig.Publisher.Capture(rig.Time);
            Check(rig.Publisher.Snapshot().States.Single().Numbers.BottomRight == null, "Resume returns the slot to null");
            Check(rig.ShownFirst() == 4, "Replica resumes from the frozen value");
            rig.Step(1);
            Check(rig.ShownFirst() == 3, "Countdown continues after resume");
        }
        using (var rig = new Rig(Hub()))
        {
            // Override while suspended: replacement has no number from the previous writer/profile.
            var handle = rig.Apply(OptIn, 5); rig.Step(1);
            rig.Collection.Suspend(true); rig.Publisher.Capture(rig.Time);
            using (rig.Collection.Contribute("cd override", new PNBuffOverrideContribution(OptInOther, 1), exact: handle, selection: PNBuffSelection.Current))
            {
                rig.Publisher.Capture(rig.Time);
                var pending = rig.Publisher.Snapshot().States.Single();
                Check(pending.ProfileId == OptInOther && pending.Suspended && pending.Numbers.BottomRight == null, "Suspended override carries no countdown from the prior profile");
                Check(rig.ShownFirst() == null, "Replica hides the countdown for the pending replacement");
            }
        }
        using (var rig = new Rig(Hub()))
        {
            // Override from opt-in to legacy profile: slot is no longer replica-derived.
            var handle = rig.Apply(OptIn, 5); rig.Step(1);
            using (rig.Collection.Contribute("cd legacy override", new PNBuffOverrideContribution(LegacyOther, 1), exact: handle, selection: PNBuffSelection.Current))
            {
                rig.Publisher.Capture(rig.Time); rig.Replica.Advance(rig.Time);
                Check(rig.Publisher.Snapshot().States.Single().ProfileId == LegacyOther && rig.ShownFirst() == null, "Override to a legacy profile stops deriving the countdown");
            }
        }
    }

    private static void Overflow()
    {
        using (var rig = new Rig(Hub()))
        {
            var handle = rig.Apply(OptInSingle, 1e19);
            Check(rig.ShownFirst() == null, "Beyond-Int64 expiration hides the countdown without failing");
            for (int i = 0; i < 5; i++) rig.Step(1);
            Check(rig.Presenter.Count(e => e is OverflowException) == 1, "Overflow is reported once per replica instance");
            Check(handle.IsAlive && rig.Replica.Status == PNBuffReplicaStatus.Live, "Overflow never ends the Buff or fails the replica");
            rig.Collection.Apply(new PNBuffApplyRequest(OptInSingle, new PNBuffSource(rig.Session, rig.Source), new JObject { ["duration"] = 100 }));
            rig.Publisher.Capture(rig.Time); rig.Replica.Advance(rig.Time);
            Check(rig.Replica.PresentationSnapshot().Count == 1 && rig.ShownFirst() == 100, "Same instance recovers its countdown once the value is valid again");
        }
        using (var rig = new Rig(Hub()))
        {
            rig.Apply(OptInSingle, 9.2e18);
            Check(rig.ShownFirst() == 9200000000000000000L, "Largest valid duration is displayed");
        }
    }

    private static void OverflowClockRecovery()
    {
        var hub = Hub(); var session = Guid.NewGuid(); var target = new EveEntityId(20); var source = new EveEntityId(10);
        const double limit = 9223372036854775808d;
        int Members(PNBuffReplica r) => ((HashSet<long>)typeof(PNBuffReplica).GetField("overflowingCountdowns", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(r)).Count;
        long? Number(PNBuffReplica r) => r.PresentationSnapshot().Single().Numbers.BottomRight;
        foreach (double expiration in new[] { limit - 1024, limit, limit + 2048 })
        foreach (double rate in new[] { 1e-12, 1d, 1e12 })
        using (var replica = new PNBuffReplica(hub, session, target))
        {
            var state = new PNBuffWireState(1, 1, OptIn, OptIn, source, 1, false, default, PNBuffClock.Entity, 0, 0, rate, expiration);
            replica.ApplyLifecycle(new PNBuffWireMessage(session, target, 1, 0, PNBuffWireKind.Snapshot, new[] { state }), 0);
            long? initial = PNBuffCountdownMath.Compute(expiration, 0, out bool overflow);
            Check(Number(replica) == initial && Members(replica) == (overflow ? 1 : 0), "Overflow boundary membership at rate " + rate);
            double now = 8192 / rate;
            replica.Advance(now);
            var expected = PNBuffCountdownMath.Compute(expiration, state.VisualTime(now), out bool stillOverflow);
            Check(!stillOverflow && Number(replica) == expected && Members(replica) == 0, "Clock-only Advance recovers without a packet at rate " + rate);
            replica.Advance(now + 4096 / rate);
            Check(Number(replica) == PNBuffCountdownMath.Compute(expiration, state.VisualTime(now + 4096 / rate), out _), "Recovered countdown continues at rate " + rate);
        }
        using (var replica = new PNBuffReplica(hub, session, target))
        {
            PNBuffWireState State(long revision, double rate, bool suspended = false, EveId? profile = null)
                => new PNBuffWireState(2, revision, OptIn, profile ?? OptIn, source, 1, suspended, default, PNBuffClock.Entity, 0, 0, rate, limit);
            replica.ApplyLifecycle(new PNBuffWireMessage(session, target, 2, 0, PNBuffWireKind.Snapshot, new[] { State(1, 1) }), 0);
            Check(Members(replica) == 1, "Active overflow tracked.");
            replica.ApplyLifecycle(new PNBuffWireMessage(session, target, 2, 1, PNBuffWireKind.Updated, new[] { State(2, 0) }), 0);
            Check(Members(replica) == 0, "Stopped clock excluded from overflow scan.");
            replica.ApplyLifecycle(new PNBuffWireMessage(session, target, 2, 2, PNBuffWireKind.Updated, new[] { State(3, 1) }), 0);
            Check(Members(replica) == 1, "Rate update re-enters overflow scan.");
            replica.ApplyLifecycle(new PNBuffWireMessage(session, target, 2, 3, PNBuffWireKind.Updated, new[] { State(4, 0, true) }), 0);
            Check(Members(replica) == 0, "Suspended overflow excluded.");
            replica.ApplyLifecycle(new PNBuffWireMessage(session, target, 2, 4, PNBuffWireKind.Updated, new[] { State(5, 1) }), 0);
            replica.ApplyLifecycle(new PNBuffWireMessage(session, target, 2, 5, PNBuffWireKind.Updated, new[] { State(6, 1, profile: LegacyOther) }), 0);
            Check(Members(replica) == 0, "Legacy profile clears overflow membership.");
            replica.ApplyLifecycle(new PNBuffWireMessage(session, target, 2, 6, PNBuffWireKind.Updated, new[] { State(7, 1) }), 0);
            replica.ApplyLifecycle(new PNBuffWireMessage(session, target, 2, 7, PNBuffWireKind.Removed, removedId: 2, endReason: PNBuffEndReason.Bypass), 0);
            Check(Members(replica) == 0, "Remove clears overflow membership.");
            var next = new PNBuffWireState(3, 1, OptIn, OptIn, source, 1, false, default, PNBuffClock.Game, 0, 0, 1, limit);
            replica.Receive(new PNBuffWireMessage(session, target, 3, 0, PNBuffWireKind.Snapshot, new[] { next }), 0);
            Check(Members(replica) == 1, "New epoch binds a new overflow instance.");
            replica.Dispose(); Check(Members(replica) == 0, "Dispose clears overflow scan buffers.");
        }
    }

    private static void Fingerprint()
    {
        var without = EveContentCompatibility.Create(Hub(false), Array.Empty<EveContentDocument>(), "rules:test");
        var with = EveContentCompatibility.Create(Hub(true), Array.Empty<EveContentDocument>(), "rules:test");
        string key = without.ContentHashes.Keys.Single(k => k.StartsWith("registry:") && k.Contains("PNBuffDefinition"));
        Check(without.ContentHashes[key] != with.ContentHashes[key], "Descriptor participates in the content fingerprint");
        var again = EveContentCompatibility.Create(Hub(true), Array.Empty<EveContentDocument>(), "rules:test");
        Check(again.ContentHashes[key] == with.ContentHashes[key], "Fingerprint is stable for identical definitions");
    }
}
