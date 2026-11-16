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
        int terminalCharge = PNActionRunWire.Bytes(PNActionRunWire.Row(row)) + 1;
        Check(store.ReservedBytes == terminalCharge && budget.Used == terminalCharge && terminalCharge < reserved, "terminal shrinks entity/session charge once");
        store.Finish(key, 4, PNActionRunEnd.Completed);
        Check(budget.Used == terminalCharge, "duplicate finish cannot return charge twice");
        Check(!store.NeedsCapture(4) && store.NeedsCapture(6.5), "retained terminal does not poll; expiry publishes removal");
        Check(row.End == PNActionRunEnd.Cancelled && row.Terminal == 3.5 && row.Progress == final, "exact first terminal wins and prevents progress resurrection");
        Check(!store.Admit(key, "test:attack", "test:sweep", "test:action", 1, 0, 1), "terminal rejects delayed duplicate start");
        var other = new PNActionRunKey(257, 2);
        bool nextAdmitted = store.Admit(other, "test:attack", "test:sweep", "test:action", 1, 0, 4);
        Check(store.Snapshot(6.49).Any(r => r.Key.Equals(key)), "terminal kept minimum retention under pressure");
        Check(store.Snapshot(6.5).All(r => !r.Key.Equals(key)) && budget.Used == (nextAdmitted ? reserved : 0), "only retained terminal charge released at expiry");
        if (nextAdmitted) store.Finish(other, 6.5, PNActionRunEnd.Completed);
        store.Prune(10);
        Check(!store.NeedsCapture(7), "empty producer has no idle stream churn");
        Check(store.Admit(other, "test:attack", "test:sweep", "test:action", 1, 0, 10), "admission after expiry");
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
            foreach (int field in new[] { 14, 15 })
            {
                var changed = (JArray)good.DeepClone(); changed[0][field] = changed[0][field].Value<long>() + 1;
                Bad(() => PNActionRunWire.ValidateSuccessor(before, PNActionRunWire.Parse(changed, 2)), "terminal cut/revision immutable " + field);
            }
            var changedHistory = (JArray)good.DeepClone(); changedHistory[0][15] = 10; changedHistory[0][16][0][0] = 2;
            Bad(() => PNActionRunWire.ValidateSuccessor(before, PNActionRunWire.Parse(changedHistory, 2)), "terminal history immutable with newer revision");
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
        using (var interval = new PNActionRunStore(budget))
        {
            interval.Admit(key, "test:a", "test:b", "test:c", 1, 0, 1);
            double progress = 0;
            for (int i = 1; i <= 1000; i++)
            {
                interval.AdvanceInterval(key, 1 + (i - 1) * .01, 1 + i * .01, (float).01, 1);
                progress += (float).01;
                interval.CaptureHolds(1 + i * .01);
                Check(Math.Abs(interval.Snapshot(1 + i * .01).Single().Progress - progress) <= 1e-12, "explicit interval preserves float progress");
            }
            var steadyRow = interval.Snapshot(11).Single();
            Check(steadyRow.History.All(a => a.Rate != 0 || a.Stamp == 1), "constant interval has no false holds");
            // A tiny explicitly supplied gap is real; no blanket epsilon may erase it.
            interval.AdvanceInterval(key, 11 + 1e-10, 11.01, .01, 1);
            Check(interval.Snapshot(11.01).Single().History.Any(a => a.Rate == 0 && a.Stamp == 11), "tiny explicit hold preserved");
        }
        var stores = new System.Collections.Generic.List<PNActionRunStore>();
        for (int i = 0; i < 200; i++) { var s = new PNActionRunStore(budget); stores.Add(s); s.Admit(key, "test:a", "test:b", "test:c", 1, 0, 1); }
        Check(budget.Used <= PNActionRunBudget.SessionBytes && stores.Any(s => s.Rejected != 0), "shared session cap");
        foreach (var s in stores) s.Dispose(); Check(budget.Used == 0, "session overload cleanup");
        Check(MeasureCapacity() > 0, "uncensored workload measurement and recorder cleanup");
        return "PASS " + checks + " Action run contracts";
    }

    // Deterministic producer pressure, not native gameplay/render/network acceptance.
    public static int MeasureCapacity()
    {
        int events = 0, refusedDrivers = 0;
        string folder = Environment.GetEnvironmentVariable("PN_ACTION_MEASURE_DIR");
        System.IO.StreamWriter output = null;
        if (!string.IsNullOrEmpty(folder)) { System.IO.Directory.CreateDirectory(folder); output = new System.IO.StreamWriter(System.IO.Path.Combine(folder, "capacity.jsonl")); }
        string scenario = ""; int storeId = 0;
        void Record(PNActionRunStore store, JObject item) { events++; item["scenario"] = scenario; item["store"] = storeId; output?.WriteLine(item.ToString(Newtonsoft.Json.Formatting.None)); }
        Action<PNActionRunStore, JObject> broken = (s, j) => throw new Exception("recorder failure");
        PNActionRunStore.Measurement += Record; PNActionRunStore.Measurement += broken;
        try
        {
            foreach (var spec in new[] { ("baseline41", 1, 41, 1.0, 2), ("overlap", 4, 80, .05, 8), ("dense", 16, 80, .025, 40), ("long-pause", 4, 8, 10.0, 24) })
            {
                scenario = spec.Item1;
                using (var shared = new PNActionRunBudget())
                {
                    var cohort = Enumerable.Range(0, spec.Item2).Select(_ => new PNActionRunStore(shared)).ToArray();
                    var pending = cohort.Select(_ => new System.Collections.Generic.Queue<(PNActionRunKey key, double end)>()).ToArray();
                    try {
                    for (int i = 0; i < spec.Item3; i++)
                    for (int entity = 0; entity < cohort.Length; entity++)
                    {
                        var store = cohort[entity]; storeId = entity + 1;
                        var key = new PNActionRunKey(i + 1, 1); double start = 1 + i * spec.Item4;
                        while (pending[entity].Count > 0 && pending[entity].Peek().end <= start)
                        { var done = pending[entity].Dequeue(); store.Finish(done.key, done.end, PNActionRunEnd.Completed); }
                        bool admitted = store.Admit(key, "test:attack", "test:sweep", "test:action", 1, 0, start);
                        for (int t = 0; t < spec.Item5; t++)
                            store.Advance(key, start + t * .001, .001, t % 2 == 0 ? 1 : 8);
                        if (!admitted) refusedDrivers++;
                        pending[entity].Enqueue((key, start + (scenario == "long-pause" ? 60 : .1)));
                    }
                    for (int entity = 0; entity < cohort.Length; entity++)
                    { storeId = entity + 1; foreach (var done in pending[entity]) cohort[entity].Finish(done.key, done.end, PNActionRunEnd.Completed); }
                    } finally { foreach (var store in cohort) store.Dispose(); }
                }
            }
        }
        finally { PNActionRunStore.Measurement -= Record; PNActionRunStore.Measurement -= broken; output?.Dispose(); }
        if (refusedDrivers == 0) throw new Exception("Pressure fixture did not exercise refused-run driver capture");
        return events;
    }
}
