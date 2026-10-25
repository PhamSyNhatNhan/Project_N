#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;

// EN: Index of the live summons of the running session, by summoner and group, plus the spawns that were accepted but have not reached configure yet, so two casts in
//     the same frame cannot both pass a limit (Docs/SummonPlan.md S3/S4). Entries leave when the summon is cleared (returned, disabled, destroyed).
// VI: Chỉ mục summon đang sống theo chủ và nhóm, cộng các lần sinh đã nhận nhưng chưa tới configure, để hai lần cast cùng khung không cùng vượt giới hạn.
public static class PNSummons
{
    private sealed class Reserved { public EveEntityId Summoner; public string Group; public EveEntityPreparationScheduler.Candidate Candidate; public bool Begun; }
    private static readonly List<PNSummon> live = new List<PNSummon>();
    private static readonly List<Reserved> reserved = new List<Reserved>();
    private static long sequence;

    public static long NextSequence() => ++sequence;
    internal static void Add(PNSummon summon) { if (!live.Contains(summon)) live.Add(summon); }
    internal static void Remove(PNSummon summon) => live.Remove(summon);

    // A spawn the host accepted: it counts toward the limit until its object reaches configure (Begin) or is dropped.
    public static void Reserve(EveEntityId summoner, string group, EveEntityPreparationScheduler.Candidate candidate)
        => reserved.Add(new Reserved { Summoner = summoner, Group = group ?? "", Candidate = candidate });
    // The reserved spawn reached configure: the live entry replaces it.
    public static void Fulfil(EveEntityPreparationScheduler.Candidate candidate)
    { foreach (var item in reserved) if (ReferenceEquals(item.Candidate, candidate)) item.Begun = true; }

    public static int Count(EveEntityId summoner, string group)
    {
        group = group ?? ""; int count = 0;
        foreach (var summon in live) if (summon.Summoner == summoner && summon.Group == group && !summon.IsRetiring) count++;
        for (int i = reserved.Count - 1; i >= 0; i--)
        {
            var item = reserved[i];
            if (item.Begun || item.Candidate == null || item.Candidate.IsReturned) { reserved.RemoveAt(i); continue; }
            if (item.Summoner == summoner && item.Group == group) count++;
        }
        return count;
    }
    // The oldest live summon of the summoner in the group that is not already being removed; null when none is live (only reserved ones).
    public static PNSummon Oldest(EveEntityId summoner, string group)
    {
        group = group ?? ""; PNSummon best = null;
        foreach (var summon in live)
            if (summon.Summoner == summoner && summon.Group == group && !summon.IsRetiring && (best == null || summon.Sequence < best.Sequence)) best = summon;
        return best;
    }
    public static IReadOnlyList<PNSummon> Of(EveEntityId summoner)
    {
        var result = new List<PNSummon>();
        foreach (var summon in live) if (summon.Summoner == summoner) result.Add(summon);
        return result;
    }
    // Every live summon of the session (a copy).
    public static IReadOnlyList<PNSummon> All() => live.ToArray();
    public static int LiveCount => live.Count;
    // Test and session teardown: forget everything.
    public static void Reset() { live.Clear(); reserved.Clear(); }
}
#endif
