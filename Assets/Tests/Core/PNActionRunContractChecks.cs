using System;
using System.Linq;
using Newtonsoft.Json.Linq;

// Pure contracts, run both by the standalone harness and native Unity.
public static class PNActionRunContractChecks
{
    public static string Run()
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("Action run: " + name); checks++; }
        void Bad(Action call, string name) { bool failed = false; try { call(); } catch (Exception e) when (e is ArgumentException || e is FormatException || e is InvalidOperationException) { failed = true; } Check(failed, name); }
        var budget = new PNActionRunBudget(); var store = new PNActionRunStore(budget); var key = new PNActionRunKey(257, 1);
        Check(store.Admit(key, "test:attack", "test:sweep", "test:action", 1, 0, 1), "admit");
        int reserved = store.ReservedBytes;
        Check(reserved > 0 && budget.Used == reserved, "session reservation");
        Check(store.Admit(key, "test:attack", "test:sweep", "test:action", 1, 0, 1) && store.Count == 1 && budget.Used == reserved, "duplicate start idempotent");
        Bad(() => store.Admit(key, "test:attack", "test:other", "test:action", 1, 0, 1), "conflicting identity");
        store.Sample(key, 1, 0, 2); store.Sample(key, 1.25, .5, 2);
        store.Sample(key, 1.25, .5, 0); store.Sample(key, 2, .5, 0);
        store.Sample(key, 2, .5, 8); store.Sample(key, 2.125, 1.5, 8);
        store.Sample(key, 2.125, 1.5, 0);
        var row = store.Snapshot(2.125).Single();
        Check(row.Progress == 1.5 && row.Rate == 0, "live rate and hold integration");
        Check(row.History.Any(a => a.Rate == 2) && row.History.Any(a => a.Rate == 8) && row.History.Any(a => a.Rate == 0), "transitions retained before capture");
        var wire = PNActionRunWire.Capture(store.Snapshot(2.125));
        Check(PNActionRunWire.Bytes(wire) < reserved, "actual bytes fit reservation");
        var read = PNActionRunWire.Parse(wire, 2.125).Single();
        Check(read.Progress == 1.5 && read.Key.Equals(key), "round trip");
        foreach (var a in read.History) Check(a.Stamp >= read.Start && a.Stamp <= read.Stamp, "anchor scope");
        var copy = store.Snapshot(2.125).Single(); store.Sample(key, 2.25, 1.5, 0);
        Check(copy.Stamp == 2.125, "snapshot immutable after capture");
        Bad(() => store.Sample(key, 2.1, 1.5, 0), "stamp regression");
        Bad(() => store.Sample(key, 2.3, 1.4, 0), "progress regression");
        Bad(() => store.Sample(key, 2.3, 1.5, double.NaN), "invalid rate");
        for (int i = 0; i < 40; i++) store.Sample(key, 3 + i * .01, 1.5 + i * .01, i % 2);
        row = store.Snapshot(3.4).Single();
        Check(row.Cut > 0 && row.History.Count <= PNActionRunStore.MaxSegments && Math.Abs(row.Progress - 1.89) < 1e-12, "capacity makes explicit coherent cut");
        double final = row.Progress; store.Finish(key, 3.5, PNActionRunEnd.Cancelled);
        store.Finish(key, 3.6, PNActionRunEnd.Completed); store.Sample(key, 4, 999, 1);
        row = store.Snapshot(4).Single();
        Check(!store.NeedsCapture(4) && store.NeedsCapture(6.5), "retained terminal does not poll; expiry publishes removal");
        Check(row.End == PNActionRunEnd.Cancelled && row.Terminal == 3.5 && row.Progress == final, "exact first terminal wins and prevents progress resurrection");
        Check(!store.Admit(key, "test:attack", "test:sweep", "test:action", 1, 0, 1), "terminal rejects delayed duplicate start");
        var other = new PNActionRunKey(257, 2);
        Check(!store.Admit(other, "test:attack", "test:sweep", "test:action", 1, 0, 4), "byte pressure refuses admission without eviction");
        Check(store.Snapshot(6.49).Single().Key.Equals(key), "terminal kept minimum retention under pressure");
        Check(store.Snapshot(6.5).Count == 0 && budget.Used == 0, "terminal reservation released only after retention");
        Check(!store.NeedsCapture(7), "empty producer has no idle stream churn");
        Check(store.Admit(other, "test:attack", "test:sweep", "test:action", 1, 0, 7), "admission after expiry");
        Check(store.Snapshot(1000).Single().End == PNActionRunEnd.Active, "paused active never expires by TTL");
        store.Dispose(); store.Dispose(); Check(budget.Used == 0, "dispose idempotent cleanup");

        using (var utf = new PNActionRunStore(budget))
        {
            // IDs are the canonical registry grammar; Unicode is invalid, not silently truncated.
            Bad(() => utf.Admit(key, "test:" + new string('界', 20), "test:sweep", "test:action", 1, 0, 1), "invalid Unicode id");
            Check(utf.Admit(key, "test:attack", "test:sweep", "test:action", 1, 0, 1), "valid after invalid attempt");
            utf.Finish(key, 2, PNActionRunEnd.Completed);
            var good = PNActionRunWire.Capture(utf.Snapshot(2));
            Check(PNActionRunWire.Parse(good, 2).Single().End == PNActionRunEnd.Completed, "completed terminal round trip");
            var before = PNActionRunWire.Parse(good, 2);
            PNActionRunWire.ValidateSuccessor(before, PNActionRunWire.Parse(good, 2)); Check(true, "duplicate image idempotent");
            var resurrection = (JArray)good.DeepClone(); resurrection[0][12] = 0; resurrection[0][13] = 0;
            Bad(() => PNActionRunWire.ValidateSuccessor(before, PNActionRunWire.Parse(resurrection, 2)), "terminal before delayed start cannot resurrect");
            var conflict = (JArray)good.DeepClone(); conflict[0][3] = "test:other";
            Bad(() => PNActionRunWire.ValidateSuccessor(before, PNActionRunWire.Parse(conflict, 2)), "same key conflicting definition");
            for (int field = 0; field < 17; field++)
            {
                var bad = (JArray)good.DeepClone(); ((JArray)bad[0])[field] = JValue.CreateNull();
                Bad(() => PNActionRunWire.Parse(bad, 2), "null field " + field);
            }
            var duplicate = (JArray)good.DeepClone(); duplicate.Add(good[0].DeepClone()); Bad(() => PNActionRunWire.Parse(duplicate, 2), "duplicate keys");
            Bad(() => PNActionRunWire.Parse(good, 1.5), "capture before terminal");
            var invalid = (JArray)good.DeepClone(); invalid[0][12] = 3; Bad(() => PNActionRunWire.Parse(invalid, 2), "unknown terminal enum");
            invalid = (JArray)good.DeepClone(); invalid[0][5] = 2; Bad(() => PNActionRunWire.Parse(invalid, 2), "unknown capability");
        }
        Check(budget.Used == 0, "all stores released");
        using (var boundary = new PNActionRunStore(budget))
        {
            boundary.Admit(key, "test:a", "test:b", "test:c", 1, 0, 1);
            boundary.Advance(key, 1.1 - 1e-10, .1, 1);
            boundary.CaptureHolds(1.1);
            boundary.Advance(key, 1.2, .1, 1);
            Check(Math.Abs(boundary.Snapshot(1.2).Single().Progress - .2) < 1e-12, "published hold boundary wins over float-rounded driver stamp");
            boundary.CaptureHolds(1.3); boundary.Advance(key, 1.3, .1, 1);
            Check(boundary.Snapshot(1.3).Single().Cut > 0, "same-frame correction requires explicit baseline cut");
        }
        var stores = new System.Collections.Generic.List<PNActionRunStore>();
        for (int i = 0; i < 200; i++) { var s = new PNActionRunStore(budget); stores.Add(s); s.Admit(key, "test:a", "test:b", "test:c", 1, 0, 1); }
        Check(budget.Used <= PNActionRunBudget.SessionBytes && stores.Any(s => s.Rejected != 0), "shared session cap");
        foreach (var s in stores) s.Dispose(); Check(budget.Used == 0, "session overload cleanup");
        return "PASS " + checks + " Action run contracts";
    }
}
