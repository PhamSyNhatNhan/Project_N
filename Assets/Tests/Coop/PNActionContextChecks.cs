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
            return "[PASS " + checks + " Action context checks]";
        }
    }
}
#endif
