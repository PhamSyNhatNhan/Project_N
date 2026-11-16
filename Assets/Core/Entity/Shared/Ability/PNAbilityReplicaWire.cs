using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// skill_owner schema2 rows. The sync block's 4096B bound also applies to grants.
public static class PNAbilityReplicaWire
{
    public static JArray Capture(IEnumerable<PNAbilityReplicaItem> items)
        => new JArray(items.Select(i => (object)new JArray(i.Id.ToString(), i.Index, i.Amount, i.Maximum, i.Active ? 1 : 0,
            i.CooldownRemaining, i.HasDuration ? i.DurationRemaining : -1d, i.BaseCooldown, i.ReductionFlat, i.ReductionPercent,
            i.AutoRecover ? 1 : 0, i.Transient ? 1 : 0,
            new JArray(i.Grants.Select(g => (object)new JArray(g.Id, g.Amount, g.Remaining ?? -1))))));
    public static List<PNAbilityReplicaItem> Parse(JArray rows)
    {
        if (rows == null || rows.Count > 256) throw new FormatException("Bounded Ability rows required.");
        var result = new List<PNAbilityReplicaItem>(); var seen = new HashSet<(EveId, int)>(); int grants = 0;
        foreach (var value in rows)
        {
            if (!(value is JArray r) || r.Count != 13 || r[0].Type != JTokenType.String || !(r[12] is JArray groups)) throw new FormatException("Ability schema2 row.");
            var id = EveId.Parse((string)r[0]); int index = Integer(r[1]), amount = Integer(r[2]), maximum = Integer(r[3]);
            if (index < 0 || amount < 0 || maximum < 1 || amount > maximum || !seen.Add((id, index))) throw new FormatException("Ability identity/quantity.");
            double duration = Number(r[6]); if (duration < 0 && duration != -1) throw new FormatException("Ability duration sentinel.");
            var groupsList = new List<PNAbilityReplicaGrant>(); long previous = 0;
            foreach (var group in groups)
            {
                if (++grants > 256 || !(group is JArray g) || g.Count != 3) throw new FormatException("Ability grant bound.");
                long key = Long(g[0]); double seconds = Number(g[2]); int count = Integer(g[1]);
                if (key <= previous || count < 1 || seconds < 0 && seconds != -1) throw new FormatException("Ability grant.");
                previous = key; groupsList.Add(new PNAbilityReplicaGrant(key, count, seconds == -1 ? (double?)null : seconds));
            }
            var item = new PNAbilityReplicaItem(id, index, amount, maximum, Flag(r[4]), Nonnegative(r[5]), duration >= 0, Math.Max(0, duration),
                Nonnegative(r[7]), Nonnegative(r[8]), Number(r[9]), Flag(r[10]), groupsList, Flag(r[11]));
            if (groupsList.Count > 0 && groupsList.Sum(g => (long)g.Amount) != amount) throw new FormatException("Ability grant sum.");
            result.Add(item);
        }
        foreach (var group in result.GroupBy(i => i.Id))
            if (!group.Select(i => i.Index).OrderBy(i => i).SequenceEqual(Enumerable.Range(0,group.Count()))) throw new FormatException("Ability instance sequence.");
        return result;
    }
    public static double Number(JToken t)
    { if (t == null || t.Type != JTokenType.Integer && t.Type != JTokenType.Float) throw new FormatException("Ability number."); double v = t.Value<double>(); if (double.IsNaN(v) || double.IsInfinity(v)) throw new FormatException("Finite Ability number."); return v; }
    public static double Nonnegative(JToken t) { double v = Number(t); if (v < 0) throw new FormatException("Nonnegative Ability number."); return v; }
    public static long Long(JToken t) { if (t == null || t.Type != JTokenType.Integer) throw new FormatException("Ability integer."); try { return t.Value<long>(); } catch (Exception e) when (e is OverflowException || e is FormatException) { throw new FormatException("Ability integer range.", e); } }
    public static int Integer(JToken t) { long v = Long(t); if (v < int.MinValue || v > int.MaxValue) throw new FormatException("Ability int range."); return (int)v; }
    public static bool Flag(JToken t) { int v = Integer(t); if (v != 0 && v != 1) throw new FormatException("Ability flag."); return v == 1; }
}
