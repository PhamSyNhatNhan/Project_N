#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace ProjectN.Coop.Tests
{
    // G8c (Docs/SkillNetworkFrameworkPlan.md 4.6.1): the Action stream carries the whole context an Action asked for (direction, position, target ids), live and in a baseline, and refuses a bad one.
    public static class PNActionContextChecks
    {
        static PNActionStream Stream(PNActionPublication s) => new PNActionStream { Session = Guid.NewGuid(), Source = s.Source, NetId = 1, Entity = 1, Generation = 1, Subscription = 1, Nonce = 1, Scope = new PNWorldBindingScope { World = Guid.NewGuid(), Incarnation = 1, Membership = 1, Subscription = 1 } };

        // PNActionState is internal to Coop: the validator is reached by reflection, the same way the other wire checks reach internals.
        static void Validate(JArray row)
        {
            var type = typeof(PNActionReceiver).Assembly.GetType("ProjectN.Coop.PNActionState");
            var method = type.GetMethod("Context", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            try { method.Invoke(null, new object[] { row }); }
            catch (System.Reflection.TargetInvocationException error) { throw error.InnerException; }
        }

        public static string Run()
        {
            int checks = 0;
            void C(bool ok, string why) { if (!ok) throw new Exception("Action context " + why); checks++; }
            void Throws(Action action, string why) { try { action(); } catch (Exception) { checks++; return; } throw new Exception("Action context " + why); }

            // 1. The context object: which parts exist, what is refused.
            var dir = PNActionRunContext.FromDirection(0, 1);
            C(dir.Mask == 1 && dir.HasDirection && !dir.HasExtra, "direction-only mask is the old row");
            var full = PNActionRunContext.Of(true, 1, 0, true, -12.5, 0.25, 40, new long[] { 7, 9 });
            C(full.Mask == 7 && full.HasExtra && full.Targets.Count == 2 && full.Px == -12.5, "full context");
            var posOnly = PNActionRunContext.Of(false, 5, 5, true, 1, 2, 3, null);
            C(posOnly.Mask == 2 && posOnly.X == 0 && posOnly.Z == 0 && posOnly.Targets.Count == 0, "position only drops direction and targets");
            var targetsOnly = PNActionRunContext.Of(false, 0, 0, false, 9, 9, 9, new long[] { 3 });
            C(targetsOnly.Mask == 4 && targetsOnly.Px == 0, "targets only");
            var none = PNActionRunContext.Of(false, 0, 0, false, 0, 0, 0, null);
            C(none.Mask == 0 && !none.HasExtra, "an Action that asks for nothing has an empty mask");
            Throws(() => PNActionRunContext.Of(true, 0, 0, false, 0, 0, 0, null), "zero direction refused");
            Throws(() => PNActionRunContext.Of(false, 0, 0, true, double.NaN, 0, 0, null), "NaN position refused");
            Throws(() => PNActionRunContext.Of(false, 0, 0, true, 2e6, 0, 0, null), "out of range position refused");
            Throws(() => PNActionRunContext.Of(false, 0, 0, false, 0, 0, 0, new long[9].Select(_ => 1L).ToArray()), "too many targets refused");
            Throws(() => PNActionRunContext.Of(false, 0, 0, false, 0, 0, 0, new long[] { 0 }), "target id below one refused");

            // 2. Live delivery and a baseline (late join) round trip, one run per shape.
            var shapes = new[] { dir, full, posOnly, targetsOnly, none };
            using (var source = new PNActionPublication())
            {
                var stream = Stream(source); var sendBudget = new PNActionReceiveBudget(); var receiveBudget = new PNActionReceiveBudget();
                for (int i = 0; i < shapes.Length; i++) C(source.Begin(new PNActionRunKey(5, i + 1), "test:e", "test:d" + i, "test:a", 1, i == 1 ? 1.5 : 1, shapes[i]) == PNActionPublicationStatus.Ok, "begin " + i);
                var rows = new List<PNObservedAction>();
                using (var sender = new PNActionSender(source, stream, 1, 1, 5, sendBudget))
                using (var receiver = new PNActionReceiver(receiveBudget, stream, 1, 5))
                {
                    double t = 1.2; sender.Capture(t);
                    for (int n = 0; n < 16; n++) if (!sender.DrainOne(t, x => true, p => receiver.Receive(p, t))) break;
                    var ack = receiver.TryApply(t); if (ack.HasValue) sender.Confirm(ack.Value, t);
                    C(receiver.State == PNActionReceiveState.Prepared, "receiver prepared");
                    var coverage = receiver.ReadPresentation(1.1, rows);
                    C(coverage == PNActionPresentationCoverage.Active && rows.Count == shapes.Length, "all shapes read " + rows.Count + " " + coverage);
                    foreach (var row in rows)
                    {
                        int i = (int)row.Key.Ordinal - 1; var want = shapes[i]; var got = row.Context;
                        C(got.Mask == want.Mask && row.Capability == want.Mask, "mask " + i);
                        C(got.X == want.X && got.Z == want.Z && got.Px == want.Px && got.Py == want.Py && got.Pz == want.Pz, "numbers " + i);
                        C(got.Targets.SequenceEqual(want.Targets), "targets " + i);
                        C(row.PresentationScale == (i == 1 ? 1.5 : 1), "scale " + i);
                    }
                    source.End(new PNActionRunKey(5, 5), 1.6, PNActionRunEnd.Completed); sender.Capture(1.7);
                    for (int n = 0; n < 16; n++) if (!sender.DrainOne(1.7, x => true, p => receiver.Receive(p, 1.7))) break;
                    ack = receiver.TryApply(1.7); if (ack.HasValue) sender.Confirm(ack.Value, 1.7);
                    C(receiver.State == PNActionReceiveState.Prepared, "terminal accepted");
                }
                C(sendBudget.Used == 0 && receiveBudget.Used == 0, "cleanup");

                // A second receiver joins late: the baseline carries the same contexts.
                var lateStream = Stream(source); var lateSend = new PNActionReceiveBudget(); var lateReceive = new PNActionReceiveBudget();
                using (var sender = new PNActionSender(source, lateStream, 1, 2, 6, lateSend))
                using (var receiver = new PNActionReceiver(lateReceive, lateStream, 1, 6))
                {
                    double t = 2; sender.Capture(t);
                    for (int n = 0; n < 32; n++) if (!sender.DrainOne(t, x => true, p => receiver.Receive(p, t))) break;
                    var ack = receiver.TryApply(t); if (ack.HasValue) sender.Confirm(ack.Value, t);
                    C(receiver.State == PNActionReceiveState.Prepared, "late receiver prepared");
                    var rows2 = new List<PNObservedAction>();
                    var cov2 = receiver.ReadPresentation(2.0, rows2);
                    var fullRow = rows2.FirstOrDefault(r => r.Key.Ordinal == 2);
                    C(rows2.Count >= 4 && fullRow.Context != null && fullRow.Context.Mask == 7 && fullRow.Context.Targets.SequenceEqual(new long[] { 7, 9 }) && fullRow.Context.Pz == 40, "baseline carries the full context: " + cov2 + " rows=" + rows2.Count + " " + string.Join(",", rows2.Select(r => r.Key.Ordinal + ":" + r.Capability)));
                }
            }

            // 3. The wire validator refuses a malformed context row.
            Throws(() => Validate(new JArray("test:e", "test:d", "test:a", 8L, 0d, 1d, 1d)), "mask above seven refused");
            Throws(() => Validate(new JArray("test:e", "test:d", "test:a", 2L, 0d, 0d, 1d, 1d)), "position mask without extra refused");
            Throws(() => Validate(new JArray("test:e", "test:d", "test:a", 2L, 0d, 0d, 1d, 1d, new JArray(1d, 2d))), "short position refused");
            Throws(() => Validate(new JArray("test:e", "test:d", "test:a", 4L, 0d, 0d, 1d, 1d, new JArray())), "empty targets refused");
            Throws(() => Validate(new JArray("test:e", "test:d", "test:a", 4L, 0d, 0d, 1d, 1d, new JArray(0L))), "target zero refused");
            Throws(() => Validate(new JArray("test:e", "test:d", "test:a", 1L, 0d, 0d, 1d)), "direction mask with zero direction refused");
            Throws(() => Validate(new JArray("test:e", "test:d", "test:a", 2L, 0.5d, 0d, 1d, 1d, new JArray(1d, 2d, 3d))), "direction present without its bit refused");
            Throws(() => Validate(new JArray("test:e", "test:d", "test:a", 1L, 0d, 1d, 1d, 1d, new JArray(1d))), "extra without its bit refused");
            Validate(new JArray("test:e", "test:d", "test:a", 0L, 0d, 0d, 1d)); checks++;
            Validate(new JArray("test:e", "test:d", "test:a", 6L, 0d, 0d, 1d, 1d, new JArray(1d, 2d, 3d, 4L))); checks++;
            // 4. PV2 (Docs/ObservedViewPlan.md): presentation facts ride with their run, live and in a baseline, within bounds; recovery brings them back; the validator refuses bad ones.
            object Call(string name, object arg)
            {
                var type = typeof(PNActionReceiver).Assembly.GetType("ProjectN.Coop.PNActionState");
                var method = type.GetMethod(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new[] { arg.GetType() == typeof(JArray) ? (name == "Record" ? typeof(JArray) : typeof(JToken)) : arg.GetType() }, null);
                try { return method.Invoke(null, new[] { arg }); }
                catch (System.Reflection.TargetInvocationException error) { throw error.InnerException; }
            }
            Throws(() => new PNActionFact(0, "Bad", null), "fact key with capitals refused");
            Throws(() => new PNActionFact(0, new string('a', 33), null), "fact key over 32 refused");
            Throws(() => new PNActionFact(-1, "a", null), "negative fact age refused");
            Throws(() => new PNActionFact(0, "a", new double[9]), "nine numbers refused (the bound is eight since V4b: a melee door)");
            Throws(() => new PNActionFact(0, "a", new[] { double.NaN }), "NaN number refused");
            Throws(() => new PNActionFact(0, "a", new[] { 2e9 }), "huge number refused");
            C(new PNActionFact(0.5, "burst.hit:1", new[] { -3.5, 2 }).Count == 2, "a valid fact keeps negative numbers");
            using (var source = new PNActionPublication())
            {
                var key = new PNActionRunKey(9, 1); var key2 = new PNActionRunKey(9, 2);
                C(source.Begin(key, "test:e", "test:d", "test:a", 1, 1, dir) == PNActionPublicationStatus.Ok, "fact run begins");
                C(source.Begin(key2, "test:e", "test:d", "test:a", 1, 1, dir) == PNActionPublicationStatus.Ok, "second fact run begins");
                source.Advance(key, 1, 1.1, .1, 1);
                C(source.Fact(key, new PNActionFact(.1, "burst", new[] { -1.25, 4 })) == PNActionPublicationStatus.Ok, "a fact is published");
                for (int i = 0; i < 8; i++) source.Fact(key2, new PNActionFact(0, "f" + i, null));
                C(source.Fact(key2, new PNActionFact(0, "f8", null)) == PNActionPublicationStatus.Ok && source.FactsDropped == 1, "the ninth fact of a run is dropped and counted, not a failure");
                var stream = Stream(source); var sendBudget = new PNActionReceiveBudget(); var receiveBudget = new PNActionReceiveBudget();
                var rows = new List<PNObservedAction>();
                using (var sender = new PNActionSender(source, stream, 1, 1.2, 5, sendBudget))
                using (var receiver = new PNActionReceiver(receiveBudget, stream, 1, 5))
                {
                    double t = 1.2; sender.Capture(t);
                    for (int n = 0; n < 32; n++) if (!sender.DrainOne(t, x => true, p => receiver.Receive(p, t))) break;
                    var ack = receiver.TryApply(t); if (ack.HasValue) sender.Confirm(ack.Value, t);
                    C(receiver.State == PNActionReceiveState.Prepared, "fact receiver prepared (baseline)");
                    receiver.ReadPresentation(1.2, rows);
                    var r1 = rows.Single(r => r.Key.Equals(key)); var r2 = rows.Single(r => r.Key.Equals(key2));
                    C(r1.Facts.Count == 1 && r1.Facts[0].Key == "burst" && r1.Facts[0][0] == -1.25 && r1.Facts[0][1] == 4 && Math.Abs(r1.Facts[0].Age - .1) < 1e-12, "the baseline carries the fact with its age and numbers");
                    C(r2.Facts.Count == 8 && r2.Facts[7].Key == "f7", "the baseline carries the eight facts of a full run in order");
                    // live: a new fact after the baseline arrives as a required row
                    source.Advance(key, 1.2, 1.3, .1, 1);
                    C(source.Fact(key, new PNActionFact(.2, "spark", new[] { 1d })) == PNActionPublicationStatus.Ok, "a live fact is published");
                    sender.Capture(1.35);
                    for (int n = 0; n < 32; n++) if (!sender.DrainOne(1.35, x => true, p => receiver.Receive(p, 1.35))) break;
                    ack = receiver.TryApply(1.35); if (ack.HasValue) sender.Confirm(ack.Value, 1.35);
                    receiver.ReadPresentation(1.35, rows);
                    r1 = rows.Single(r => r.Key.Equals(key));
                    C(r1.Facts.Count == 2 && r1.Facts[1].Key == "spark" && Math.Abs(r1.Facts[1].Age - .2) < 1e-12, "the live fact arrives after the baseline ones");
                    source.End(key, 1.4, PNActionRunEnd.Completed); source.End(key2, 1.4, PNActionRunEnd.Cancelled);
                }
                C(sendBudget.Used == 0 && receiveBudget.Used == 0, "fact cleanup");
            }
            // recovery: a lost publication is rebuilt with the facts of its live runs
            using (var budget = new PNActionPublicationBudget())
            using (var census = new PNActionPublicationRecovery(budget))
            {
                var key = new PNActionRunKey(11, 1);
                census.Begin(key, "test:e", "test:d", "test:a", 1, 1, dir); census.Advance(key, 1.1, .1, 1);
                census.Fact(key, new PNActionFact(.1, "burst", new[] { 2d }));
                PNActionPublication rebuilt = null;
                C(census.Recover(ref rebuilt, 1.2, 5) == PNActionRecoveryState.Recovered && rebuilt != null, "recovery rebuilds the publication");
                using (rebuilt)
                {
                    var snapshot = rebuilt.Capture(new JArray(new string('1', 32), new string('2', 32), 1, 1, 1, new string('3', 32)), 1.2);
                    C(snapshot != null && snapshot.Json.Contains("\"burst\""), "the recovered baseline carries the fact");
                    snapshot?.Dispose();
                }
            }
            // the wire validator
            Call("Fact", new JArray(.1, "burst", -1d, 2d)); checks++;
            Throws(() => Call("Fact", new JArray(.1, "Burst")), "validator: capital key refused");
            Throws(() => Call("Fact", new JArray(-.1, "a")), "validator: negative age refused");
            Throws(() => Call("Fact", new JArray(.1, "a", 1d, 2d, 3d, 4d, 5d, 6d, 7d, 8d, 9d)), "validator: nine numbers refused");
            Throws(() => Call("Fact", new JArray(.1, "a", "x")), "validator: a text number refused");
            Throws(() => Call("Fact", new JArray(.1)), "validator: a fact without key refused");
            Call("Record", new JArray(5L, 3L, new JArray(9L, 1L), 1.2d, new JArray(.1, "burst"))); checks++;
            Throws(() => Call("Record", new JArray(5L, 3L, new JArray(9L, 1L), new JArray(.1, "burst"))), "validator: a fact row without stamp refused");
            Throws(() => Call("Record", new JArray(6L, 3L, new JArray(9L, 1L), 1.2d, new JArray(.1, "burst"))), "validator: tag 6 refused");
            C(ProjectN.Coop.PNNetworkAuthenticator.NetworkVersion == 48, "NetworkVersion is 48 (body motion M4: the owner-only forced_motion state carries a host-decided push or pull to the machine that writes a client player; before 47: instance lifecycle IL2: a blast Taken hit report names its strike in Detail, a blast End record is sent when its Ending starts; before that 46: instance lifecycle IL1a: an area End record can carry Replaced; before that 45: the shared value store adds two sync blocks; before that 44: a melee door can be a contact door that follows the body; a hit report can name a melee hit by cast; a run fact carries up to 8 numbers)");
            return "[PASS " + checks + " Action context checks]";
        }
    }
}
#endif
