using System;
using System.Collections.Generic;

// Who the Herald gives Sidus to (Docs/HeraldPlan.md H3, H5, H6). Pure selection over plain candidates (no Unity, no world access) so the smoke suite runs it; the behavior builds the candidates
// from the entities of the world and the Action builds them again at the release moment (H13: a target that died or left the zone is skipped).
public struct PNHeraldCandidate
{
    public int Key;            // stable tie-break (the entity id); lower wins
    public string TypeId;      // the character id: "different kind" compares these
    public bool Boss;          // a ready enemy that is not a normal one (it can never be Sidus)
    public bool HasSidus;      // a Sidus Buff is on it now
    public double Remaining;   // seconds left of that Buff (ignored without Sidus)
    public float Distance;     // planar distance from the Herald
}

public static class PNHeraldTargets
{
    // Candidates (already limited to living allies other than the caster) inside `radius`, at most `max`, best first.
    // Boss (H6): a boss is a candidate only when the zone holds at most 2 candidates in all; from 3 on it never is.
    // Order (H5), re-evaluated after every pick against the kinds already picked: a candidate with no Sidus that is also a different kind first, then no Sidus (same kind), then a different kind that already
    // has Sidus, then the rest; inside a tier the Buff with less time left, then the nearer, then the lower key.
    public static List<int> Pick(IList<PNHeraldCandidate> candidates, float radius, int max)
    {
        var result = new List<int>(); if (candidates == null || max <= 0) return result;
        var zone = new List<int>();
        for (int i = 0; i < candidates.Count; i++) if (candidates[i].Distance <= radius + 1e-6f) zone.Add(i);
        bool bossesAllowed = zone.Count <= 2;
        var pool = new List<int>(); foreach (int i in zone) if (!candidates[i].Boss || bossesAllowed) pool.Add(i);
        var kinds = new List<string>();
        while (result.Count < max && pool.Count > 0)
        {
            int best = -1;
            for (int n = 0; n < pool.Count; n++) if (best < 0 || Better(candidates[pool[n]], candidates[pool[best]], kinds)) best = n;
            int chosen = pool[best]; pool.RemoveAt(best); result.Add(chosen); kinds.Add(candidates[chosen].TypeId);
        }
        return result;
    }

    private static int Tier(PNHeraldCandidate c, List<string> kinds)
    {
        bool fresh = !c.HasSidus, different = !kinds.Contains(c.TypeId);
        return fresh && different ? 0 : fresh ? 1 : different ? 2 : 3;
    }

    private static bool Better(PNHeraldCandidate a, PNHeraldCandidate b, List<string> kinds)
    {
        int ta = Tier(a, kinds), tb = Tier(b, kinds); if (ta != tb) return ta < tb;
        if (a.HasSidus && b.HasSidus && Math.Abs(a.Remaining - b.Remaining) > 1e-9) return a.Remaining < b.Remaining;
        if (Math.Abs(a.Distance - b.Distance) > 1e-6f) return a.Distance < b.Distance;
        return a.Key < b.Key;
    }
}
