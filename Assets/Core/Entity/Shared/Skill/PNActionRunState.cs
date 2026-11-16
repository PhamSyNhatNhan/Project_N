using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EveEngine;

// Producer contract only. Receiving this image does not execute an Action.
public readonly struct PNActionRunKey : IEquatable<PNActionRunKey>
{
    // Negative cast ids are unkeyed invocation ids, a separate namespace from input casts.
    public readonly long Cast, Ordinal;
    public PNActionRunKey(long cast, long ordinal) { if (cast == 0 || ordinal < 1) throw new ArgumentException(); Cast = cast; Ordinal = ordinal; }
    public bool Equals(PNActionRunKey other) => Cast == other.Cast && Ordinal == other.Ordinal;
    public override bool Equals(object other) => other is PNActionRunKey key && Equals(key);
    public override int GetHashCode() => Cast.GetHashCode() * 397 ^ Ordinal.GetHashCode();
}

public enum PNActionRunEnd { Active, Completed, Cancelled }
public readonly struct PNActionProgress
{
    public readonly long Revision;
    public readonly double Stamp, Value, Rate;
    public PNActionProgress(long revision, double stamp, double value, double rate)
    { if (revision < 1) throw new ArgumentException(); PNTimeMath.Nonnegative(stamp); PNTimeMath.Nonnegative(value); PNTimeMath.Nonnegative(rate); Revision = revision; Stamp = stamp; Value = value; Rate = rate; }
}

public sealed class PNActionRunRecord
{
    public PNActionRunKey Key { get; internal set; }
    public string Entry { get; internal set; }
    public string Data { get; internal set; }
    public string Action { get; internal set; }
    public double X { get; internal set; }
    public double Z { get; internal set; }
    public double Start { get; internal set; }
    public double Stamp { get; internal set; }
    public double Progress { get; internal set; }
    public double Rate { get; internal set; }
    public double Terminal { get; internal set; }
    public PNActionRunEnd End { get; internal set; }
    // A history cut is explicit. A consumer must baseline, never interpolate across it.
    public long Cut { get; internal set; }
    public long Revision { get; internal set; }
    internal readonly List<PNActionProgress> history = new List<PNActionProgress>();
    internal int Reservation;
    public IReadOnlyList<PNActionProgress> History => history.AsReadOnly();
    internal PNActionRunRecord Copy() { var copy = new PNActionRunRecord { Key = Key, Entry = Entry, Data = Data, Action = Action, X = X, Z = Z, Start = Start, Stamp = Stamp, Progress = Progress, Rate = Rate, Terminal = Terminal, End = End, Cut = Cut, Revision = Revision, Reservation = Reservation }; copy.history.AddRange(history); return copy; }
}

// Shared by stores in one play. Reservation is a protocol-memory charge, not measured heap.
public sealed class PNActionRunBudget : IDisposable
{
    public const int SessionBytes = 256 * 1024;
    public int Used { get; private set; }
    internal readonly HashSet<PNActionRunStore> Stores = new HashSet<PNActionRunStore>();
    internal bool Take(int bytes) { if (bytes > SessionBytes - Used) return false; Used += bytes; return true; }
    internal void Return(int bytes) { Used -= bytes; }
    public void Dispose() { foreach (var store in Stores.ToArray()) store.Dispose(); }
}

public sealed class PNActionRunStore : IDisposable
{
    // Opt-in, unbuffered measurement. Observe before admission/history coalescing;
    // a failing recorder must never interrupt gameplay. Subscribers own cleanup.
    public static event Action<PNActionRunStore, JObject> Measurement;
    private void Measure(string kind, PNActionRunKey key, double stamp, double value = 0, double rate = 0, PNActionRunRecord candidate = null, double? begin = null)
    {
        var listeners = Measurement; if (listeners == null) return;
        var image = new JObject { ["kind"] = kind, ["cast"] = key.Cast, ["ordinal"] = key.Ordinal,
            ["stamp"] = stamp, ["value"] = value, ["rate"] = rate, ["reserved"] = ReservedBytes,
            ["session"] = budget.Used, ["active"] = runs.Values.Count(r => r.End == PNActionRunEnd.Active),
            ["terminal"] = runs.Values.Count(r => r.End != PNActionRunEnd.Active),
            ["rowsBytes"] = PNActionRunWire.Bytes(PNActionRunWire.Capture(runs.Values.ToArray())) };
        if (candidate != null) { image["candidate"] = PNActionRunWire.Row(candidate); image["candidateReservation"] = candidate.Reservation; }
        if (begin.HasValue) image["begin"] = begin.Value;
        foreach (Action<PNActionRunStore, JObject> listener in listeners.GetInvocationList())
            try { listener(this, (JObject)image.DeepClone()); } catch { /* measurement is observational */ }
    }
    public const int MaxActive = 4, MaxSlots = 16, MaxSegments = 16;
    public const int WireBytes = 4096, LegacyBytes = 1024, EnvelopeBytes = 512;
    public const double Retention = 3;
    private readonly Dictionary<PNActionRunKey, PNActionRunRecord> runs = new Dictionary<PNActionRunKey, PNActionRunRecord>();
    private readonly PNActionRunBudget budget;
    private bool disposed;
    public int ReservedBytes { get; private set; }
    public long Revision { get; private set; }
    public long Rejected { get; private set; }
    public long ByteRejections { get; private set; }
    public long SlotRejections { get; private set; }
    public long ActiveRejections { get; private set; }
    public long SessionRejections { get; private set; }
    public long HistoryCuts { get; private set; }
    public int Count => runs.Count;
    public bool NeedsCapture(double stamp) => runs.Values.Any(r => r.End == PNActionRunEnd.Active || stamp - r.Terminal >= Retention);
    public PNActionRunStore(PNActionRunBudget budget) { this.budget = budget ?? throw new ArgumentNullException(nameof(budget)); budget.Stores.Add(this); }
    public IReadOnlyList<PNActionRunRecord> Snapshot(double stamp)
    { Prune(stamp); return runs.Values.OrderBy(x => x.Key.Cast).ThenBy(x => x.Key.Ordinal).Select(x => x.Copy()).ToArray(); }
    public void CaptureHolds(double stamp)
    {
        Measure("seal", default, stamp);
        // Capture is after drivers. A run not advanced at this frame's source stamp is
        // held (world/permission/component stop); do not extrapolate its previous rate.
        foreach (var r in runs.Values.ToArray()) if (r.End == PNActionRunEnd.Active && r.Stamp < stamp)
        { Sample(r.Key, r.Stamp, r.Progress, 0); Sample(r.Key, stamp, r.Progress, 0); }
    }
    public void Advance(PNActionRunKey key, double stamp, double step, double rate)
        => AdvanceInterval(key, rate > 0 ? Math.Max(0, stamp - step / rate) : stamp, stamp, step, rate);

    // Driver-owned interval avoids reconstructing its beginning from a rounded float
    // gameplay step. Explicit gaps, even very short ones, remain real holds.
    public void AdvanceInterval(PNActionRunKey key, double begin, double stamp, double step, double rate)
    {
        PNTimeMath.Nonnegative(begin); PNTimeMath.Nonnegative(stamp); PNTimeMath.Nonnegative(step); PNTimeMath.Nonnegative(rate);
        if (begin > stamp) throw new ArgumentException("Action interval order.");
        Measure("driver", key, stamp, step, rate, begin: begin);
        if (disposed || !runs.TryGetValue(key, out var r) || r.End != PNActionRunEnd.Active) return;
        // The store owns the published boundary. Capture/hold and float phase budgets
        // must not make a driver use an older private copy of that boundary.
        stamp = Math.Max(stamp, r.Stamp);
        begin = Math.Max(r.Stamp, begin);
        double progress = r.Progress;
        if (begin > r.Stamp) Sample(key, r.Stamp, progress, 0);
        Sample(key, begin, progress, rate);
        Sample(key, stamp, progress + step, rate);
    }
    public bool Admit(PNActionRunKey key, string entry, string data, string action, double x, double z, double stamp)
    {
        if (disposed) return false;
        PNTimeMath.Nonnegative(stamp); PNActionRunWire.Id(entry); PNActionRunWire.Id(data); PNActionRunWire.Id(action);
        if (!PNActionRunWire.Direction(x, z)) { Rejected++; return false; }
        Prune(stamp);
        if (runs.TryGetValue(key, out var old))
        {
            if (old.Entry != entry || old.Data != data || old.Action != action || old.X != x || old.Z != z || old.Start != stamp) throw new InvalidOperationException("Conflicting Action run identity.");
            return old.End == PNActionRunEnd.Active;
        }
        var run = new PNActionRunRecord { Key = key, Entry = entry, Data = data, Action = action, X = x, Z = z, Start = stamp, Stamp = stamp, Revision = 1 };
        run.history.Add(new PNActionProgress(1, stamp, 0, 0));
        // Reserve all sixteen numeric anchors and the terminal at admission, including UTF8 ids.
        run.Reservation = PNActionRunWire.Reservation(run);
        Measure("attempt", key, stamp, candidate: run);
        if (runs.Count >= MaxSlots) { Rejected++; SlotRejections++; Measure("refused-slots", key, stamp); return false; }
        if (runs.Values.Count(v => v.End == PNActionRunEnd.Active) >= MaxActive) { Rejected++; ActiveRejections++; Measure("refused-active", key, stamp); return false; }
        if (run.Reservation > WireBytes - LegacyBytes - EnvelopeBytes - ReservedBytes) { Rejected++; ByteRejections++; Measure("refused-bytes", key, stamp); return false; }
        if (!budget.Take(run.Reservation)) { Rejected++; SessionRejections++; Measure("refused-session", key, stamp); return false; }
        runs.Add(key, run); ReservedBytes += run.Reservation; Revision++; Measure("admitted", key, stamp); return true;
    }
    public void Sample(PNActionRunKey key, double stamp, double progress, double rate)
    {
        PNTimeMath.Nonnegative(stamp); PNTimeMath.Nonnegative(progress); PNTimeMath.Nonnegative(rate);
        Measure("sample", key, stamp, progress, rate);
        if (disposed || !runs.TryGetValue(key, out var run) || run.End != PNActionRunEnd.Active) return;
        if (stamp < run.Stamp || progress < run.Progress) throw new ArgumentException("Action progress cannot go backwards.");
        if (stamp == run.Stamp && progress == run.Progress && rate == run.Rate) return;
        var anchor = run.history[run.history.Count - 1];
        // Keep actual driver samples; only coalesce an exactly equivalent linear segment.
        bool linear = rate == anchor.Rate && Math.Abs(anchor.Value + (stamp - anchor.Stamp) * rate - progress) <= 1e-7;
        run.Revision++;
        if (!linear)
        {
            if (run.history.Count == MaxSegments || stamp == anchor.Stamp && progress != anchor.Value)
            { Measure("before-cut", key, stamp, progress, rate, run); run.history.Clear(); run.Cut++; HistoryCuts++; }
            run.history.Add(new PNActionProgress(run.Revision, stamp, progress, rate));
        }
        run.Stamp = stamp; run.Progress = progress; run.Rate = rate; Revision++;
    }
    public void Finish(PNActionRunKey key, double stamp, PNActionRunEnd end)
    {
        if (end == PNActionRunEnd.Active) throw new ArgumentException(); PNTimeMath.Nonnegative(stamp);
        Measure("finish-attempt", key, stamp, (int)end);
        if (disposed || !runs.TryGetValue(key, out var run) || run.End != PNActionRunEnd.Active) return;
        if (stamp < run.Stamp) throw new ArgumentException("Terminal precedes progress.");
        run.End = end; run.Terminal = stamp; run.Stamp = stamp; run.Rate = 0; run.Revision++; Revision++;
        // Immutable terminal: exact row bytes plus one array delimiter. Outer array
        // brackets remain covered by EnvelopeBytes. Return surplus exactly once.
        int charge = PNActionRunWire.Bytes(PNActionRunWire.Row(run)) + 1;
        int released = run.Reservation - charge;
        if (released < 0) throw new InvalidOperationException("Terminal exceeded reserved bytes.");
        run.Reservation = charge; ReservedBytes -= released; budget.Return(released);
        Measure("terminal", key, stamp, candidate: run);
    }
    public void Prune(double stamp)
    {
        PNTimeMath.Nonnegative(stamp);
        foreach (var key in runs.Where(p => p.Value.End != PNActionRunEnd.Active && stamp - p.Value.Terminal >= Retention).Select(p => p.Key).ToArray())
        { var bytes = runs[key].Reservation; runs.Remove(key); ReservedBytes -= bytes; budget.Return(bytes); Revision++; }
    }
    public void Dispose() { if (disposed) return; disposed = true; budget.Return(ReservedBytes); budget.Stores.Remove(this); ReservedBytes = 0; runs.Clear(); }
}

public static class PNActionRunWire
{
    public const int Schema = 1, Capability = 1;
    public static int Bytes(JToken value) => Encoding.UTF8.GetByteCount(value.ToString(Formatting.None));
    public static void Id(string value) { if (value == null || value.Length > 96 || !EveId.TryParse(value, out _)) throw new FormatException("Action definition id."); }
    public static bool Direction(double x, double z) => !double.IsNaN(x) && !double.IsInfinity(x) && !double.IsNaN(z) && !double.IsInfinity(z) && Math.Abs(x) <= 1 && Math.Abs(z) <= 1 && x * x + z * z > 1e-8;
    public static JArray Row(PNActionRunRecord r)
    {
        var history = new JArray(); foreach (var a in r.history) history.Add(new JArray(a.Revision, a.Stamp, a.Value, a.Rate));
        return new JArray(r.Key.Cast, r.Key.Ordinal, r.Entry, r.Data, r.Action, Capability, r.X, r.Z, r.Start, r.Stamp, r.Progress, r.Rate, (int)r.End, r.Terminal, r.Cut, r.Revision, history);
    }
    internal static int Reservation(PNActionRunRecord r)
    {
        // 25 ASCII bytes per number covers every finite Json.NET double/int64 spelling.
        // Three UTF8 ids are exact; seventeen row fields and sixteen four-number anchors.
        return Bytes(new JArray(r.Entry, r.Data, r.Action)) + (13 + PNActionRunStore.MaxSegments * 4) * 26 + 64;
    }
    public static JArray Capture(IReadOnlyList<PNActionRunRecord> records) { var rows = new JArray(); foreach (var r in records) rows.Add(Row(r)); return rows; }
    public static void ValidateSuccessor(IReadOnlyList<PNActionRunRecord> previous, IReadOnlyList<PNActionRunRecord> next)
    {
        foreach (var r in next)
        {
            var old = previous.FirstOrDefault(p => p.Key.Equals(r.Key)); if (old == null) continue;
            if (old.End != PNActionRunEnd.Active && !JToken.DeepEquals(Row(old), Row(r)))
                throw new FormatException("Terminal Action row is immutable.");
            if (old.Entry != r.Entry || old.Data != r.Data || old.Action != r.Action || old.Start != r.Start || old.X != r.X || old.Z != r.Z
                || r.Revision < old.Revision || r.Stamp < old.Stamp || r.Progress < old.Progress || r.Cut < old.Cut
                || old.End != PNActionRunEnd.Active && (r.End != old.End || r.Terminal != old.Terminal || r.Progress != old.Progress))
                throw new FormatException("Conflicting Action successor/terminal resurrection.");
            if (r.Revision == old.Revision && !JToken.DeepEquals(Row(old), Row(r))) throw new FormatException("Changed Action without revision.");
        }
    }
    public static List<PNActionRunRecord> Parse(JToken value, double capture)
    {
        PNTimeMath.Nonnegative(capture);
        if (!(value is JArray rows) || rows.Count > PNActionRunStore.MaxSlots || Bytes(value) > PNActionRunStore.WireBytes) throw new FormatException("Action rows bound.");
        var result = new List<PNActionRunRecord>(); var keys = new HashSet<PNActionRunKey>(); int active = 0;
        foreach (var token in rows)
        {
            if (!(token is JArray row) || row.Count != 17 || Long(row[5]) != Capability) throw new FormatException("Action row/capability.");
            long cast = Long(row[0]), ordinal = Long(row[1]); if (cast == 0 || ordinal < 1) throw new FormatException("Invalid run key.");
            var key = new PNActionRunKey(cast, ordinal); if (!keys.Add(key)) throw new FormatException("Duplicate run key.");
            var r = new PNActionRunRecord { Key = key, Entry = Text(row[2]), Data = Text(row[3]), Action = Text(row[4]), X = Number(row[6]), Z = Number(row[7]), Start = Nonnegative(row[8]), Stamp = Nonnegative(row[9]), Progress = Nonnegative(row[10]), Rate = Nonnegative(row[11]), Terminal = Nonnegative(row[13]), Cut = Long(row[14]), Revision = Long(row[15]) };
            Id(r.Entry); Id(r.Data); Id(r.Action); long end = Long(row[12]);
            if (end < 0 || end > 2 || r.Cut < 0 || r.Revision < 1 || !Direction(r.X, r.Z) || r.Start > r.Stamp || r.Stamp > capture) throw new FormatException("Action lifecycle/order.");
            r.End = (PNActionRunEnd)end;
            if (end == 0) { if (++active > PNActionRunStore.MaxActive || r.Terminal != 0) throw new FormatException("Active Action bound."); }
            else if (r.Terminal < r.Start || r.Terminal != r.Stamp || r.Rate != 0) throw new FormatException("Action terminal.");
            if (!(row[16] is JArray anchors) || anchors.Count < 1 || anchors.Count > PNActionRunStore.MaxSegments) throw new FormatException("Action history bound.");
            long previous = 0; double stamp = r.Start, progress = 0;
            foreach (var item in anchors)
            {
                if (!(item is JArray a) || a.Count != 4) throw new FormatException("Action anchor.");
                var anchor = new PNActionProgress(Long(a[0]), Nonnegative(a[1]), Nonnegative(a[2]), Nonnegative(a[3]));
                if (anchor.Revision <= previous || anchor.Revision > r.Revision || anchor.Stamp < stamp || anchor.Stamp > r.Stamp || anchor.Value < progress || anchor.Value > r.Progress) throw new FormatException("Action history order.");
                previous = anchor.Revision; stamp = anchor.Stamp; progress = anchor.Value; r.history.Add(anchor);
            }
            result.Add(r);
        }
        return result;
    }
    internal static string Text(JToken t) { if (t?.Type != JTokenType.String) throw new FormatException("Text required."); return (string)t; }
    public static long Long(JToken t) { if (t?.Type != JTokenType.Integer) throw new FormatException("Integer required."); try { return t.Value<long>(); } catch (Exception e) { throw new FormatException("Integer range.", e); } }
    public static double Number(JToken t) { if (t == null || t.Type != JTokenType.Float && t.Type != JTokenType.Integer) throw new FormatException("Number required."); double v = t.Value<double>(); if (double.IsNaN(v) || double.IsInfinity(v)) throw new FormatException("Finite required."); return v; }
    public static double Nonnegative(JToken t) { double v = Number(t); if (v < 0) throw new FormatException("Nonnegative required."); return v; }
}
