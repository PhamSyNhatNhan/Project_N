using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// EN: Measurement only (Docs/SpawnedObjectSyncPlan.md 4.4d, V4 tolerance work). For each hit the authority ACCEPTS from a player's machine, the distance by which the player's body was past the shape's own reach
//     (distance - radius; negative = inside). Keys name the direction and kind: `taken/<melee|blast|projectile>` (the hit player's machine claimed it) and `dealt/<melee|projectile>` (the attacker's machine reported it).
//     It never changes a decision: the tolerance values stay where they are until the numbers say what they should be.
// VI: Chi do, khong doi quyet dinh. Moi cu trung ma authority NHAN tu may nguoi choi, ghi so met than nguoi choi nam ngoai tam hinh (khoang cach - ban kinh; am = ben trong).
public static class PNReportOvershoot
{
    private const int Cap = 4096, MaxKeys = 256;
    // Off in the game: only a test that wants the numbers turns it on (the Player fixtures do), so a long run keeps nothing.
    public static bool Enabled;
    private static readonly Dictionary<string, List<float>> samples = new Dictionary<string, List<float>>();

    public static void Note(string key, float meters, long victim = 0)
    {
        if (!Enabled || float.IsNaN(meters) || float.IsInfinity(meters)) return;
        Add(key, meters); if (victim != 0) Add(key + "@" + victim, meters);
    }

    private static void Add(string key, float meters)
    {
        if (!samples.TryGetValue(key, out var list)) { if (samples.Count >= MaxKeys) return; samples[key] = list = new List<float>(); }
        if (list.Count < Cap) list.Add(meters);
    }

    public static void Clear() => samples.Clear();
    public static int Count(string key) => samples.TryGetValue(key, out var list) ? list.Count : 0;

    // `key: n=<count> over0=<how many past the reach> p50 p90 p99 max` (metres), one entry per key.
    public static string Summary(long victim = 0)
    {
        var text = new StringBuilder();
        foreach (var pair in samples.Where(x => victim == 0 ? x.Key.IndexOf('@') < 0 : x.Key.EndsWith("@" + victim, StringComparison.Ordinal)).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var sorted = pair.Value.OrderBy(x => x).ToArray(); int n = sorted.Length; if (n == 0) continue;
            float At(double q) => sorted[Math.Min(n - 1, (int)Math.Ceiling(q * n) - 1 < 0 ? 0 : (int)Math.Ceiling(q * n) - 1)];
            text.Append(" ").Append(pair.Key).Append(": n=").Append(n).Append(" over0=").Append(sorted.Count(x => x > 0f))
                .Append(" p50=").Append(At(.5).ToString("F2")).Append(" p90=").Append(At(.9).ToString("F2")).Append(" p99=").Append(At(.99).ToString("F2")).Append(" max=").Append(sorted[n - 1].ToString("F2")).Append(";");
        }
        return text.Length == 0 ? " overshoot: none" : " overshoot{" + text.ToString().Trim() + "}";
    }
}
