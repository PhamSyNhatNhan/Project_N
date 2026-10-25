#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// One target query: where from, how far, which layers, which rule, the target already held (optional) and who must never be chosen.
public struct PNTargetQuery
{
    public Vector3 Origin;
    public float Range;
    public int LayerMask;
    public PNTargetRule Rule;
    // Kept while still valid unless the rule says to switch (PNTargetRanking.ShouldSwitch); null = no held target.
    public PNStat Held;
    // Never chosen (the chooser itself); an invalid ID excludes nobody.
    public EveEntityId Exclude;
    // Distance on the ground plane (XZ) instead of in 3D.
    public bool Planar;
    // Skill option: only a candidate in line of sight from Chooser (walls block; entities do not). Off by default.
    public bool RequireSight;
    public PNStat Chooser;
    // Line-of-sight tests allowed for one query (1..16; 0 = default 4). The best candidates are tested in rank order.
    public int SightBudget;
}

// EN: The single target selector. Allocation-free over a PNTargetCandidates list: cheap filters first (layer, excluded ID, range), then
//     liveness, then the values the rule needs (health only in the health modes). Skills with a rule of their own pass another
//     PNTargetRule, or walk the candidates themselves.
// VI: Bộ chọn mục tiêu duy nhất. Không cấp phát: lọc rẻ trước (layer, ID loại, tầm), rồi còn sống, rồi mới đọc giá trị luật cần.
public static class PNTargetSelector
{
    public const int DefaultSightBudget = 4, MaxSightBudget = 16;
    private static readonly IEnumerable<EveId> AggroIds = new[] { PNContentIds.Aggro };
    private static readonly IEnumerable<EveId> HealthIds = new[] { PNContentIds.Health };
    private static readonly PNStat[] ranked = new PNStat[MaxSightBudget];
    private static readonly PNTargetScore[] rankedScores = new PNTargetScore[MaxSightBudget];
    private static RaycastHit[] sightBuffer = new RaycastHit[16];

    // Aggro of an entity (0 when it is not ready or has no aggro base: the stat is not registered in its session).
    public static double AggroOf(PNStat stat)
    {
        if (stat == null || !stat.IsReady) return 0d;
        var set = stat.Set;
        return set.TryGetBase(PNContentIds.Aggro, out _) ? set.Evaluate(AggroIds) : 0d;
    }
    // Health and its maximum; double.MaxValue / 0 for an entity without a Health resource.
    public static void HealthOf(PNStat stat, out double health, out double max)
    {
        health = double.MaxValue; max = 0d;
        if (stat == null || !stat.IsReady) return;
        var set = stat.Set;
        if (!set.TryGetBase(PNContentIds.Health, out var value) || !(value is EveResourceStat resource)) return;
        health = resource.Current; max = Math.Max(0d, set.Evaluate(HealthIds));
    }
    public static PNStat Select(PNTargetCandidates candidates, in PNTargetQuery query)
    {
        if (candidates == null || candidates.Count == 0) return null;
        if (!(query.Range >= 0f) || float.IsInfinity(query.Range)) throw new ArgumentOutOfRangeException(nameof(query), "Target range must be finite and nonnegative.");
        if (query.RequireSight && query.Chooser == null) throw new ArgumentException("A line-of-sight query needs its chooser.", nameof(query));
        int budget = !query.RequireSight ? 0 : query.SightBudget <= 0 ? DefaultSightBudget : Math.Min(query.SightBudget, MaxSightBudget);
        float range2 = query.Range * query.Range; var origin = query.Origin; var items = candidates.RawItems; int count = candidates.Count;
        bool healthMode = query.Rule.Kind == PNTargetRuleKind.Autotarget && query.Rule.Mode != PNTargetMode.Nearest;
        long exclude = query.Exclude.IsValid ? query.Exclude.Value : 0; var held = query.Held;
        PNStat best = null; var bestScore = default(PNTargetScore);
        bool heldFound = false; var heldScore = default(PNTargetScore);
        int kept = 0;
        for (int i = 0; i < count; i++)
        {
            ref var candidate = ref items[i];
            if (((query.LayerMask >> candidate.Layer) & 1) == 0) continue;
            if (exclude != 0 && candidate.EntityId == exclude) continue;
            if (!candidate.Damageable && query.Rule.Kind == PNTargetRuleKind.Autotarget) continue;
            float dx = candidate.Center.x - origin.x, dy = query.Planar ? 0f : candidate.Center.y - origin.y, dz = candidate.Center.z - origin.z;
            float distance2 = dx * dx + dy * dy + dz * dz;
            if (distance2 > range2) continue;
            var stat = candidate.Stat; bool isHeld = held != null && ReferenceEquals(stat, held);
            // Aggro and the entity ID come from the candidate (read with its box); health only in the health modes.
            var score = new PNTargetScore { Distance = Mathf.Sqrt(distance2), Aggro = candidate.Aggro, EntityId = candidate.EntityId, Health = double.MaxValue };
            if (healthMode) HealthOf(stat, out score.Health, out score.HealthMax);
            else if (best != null && score.Aggro == bestScore.Aggro && score.Distance == bestScore.Distance && query.Rule.Kind == PNTargetRuleKind.Autotarget)
            { HealthOf(stat, out score.Health, out score.HealthMax); HealthOf(best, out bestScore.Health, out bestScore.HealthMax); } // exact tie: lower health first
            if (budget == 0)
            {
                bool better = best == null || PNTargetRanking.Better(score, bestScore, query.Rule);
                if (!better && !isHeld) continue;
                // Liveness is checked only for a candidate that would be kept (it may have died since the box update).
                if (!PNTargetCandidates.IsLive(stat)) continue;
                if (isHeld) { heldFound = true; heldScore = score; }
                if (better) { best = stat; bestScore = score; }
                continue;
            }
            // Keep the `budget` best in rank order (insertion into a short array).
            int at = kept;
            while (at > 0 && PNTargetRanking.Better(score, rankedScores[at - 1], query.Rule)) at--;
            if (at >= budget && !isHeld) continue;
            if (!PNTargetCandidates.IsLive(stat)) continue;
            if (isHeld) { heldFound = true; heldScore = score; }
            if (at >= budget) continue;
            for (int k = Math.Min(kept, budget - 1); k > at; k--) { ranked[k] = ranked[k - 1]; rankedScores[k] = rankedScores[k - 1]; }
            ranked[at] = stat; rankedScores[at] = score; if (kept < budget) kept++;
        }
        bool heldVisible = heldFound;
        if (budget != 0)
        {
            // Rank order: the first candidate in sight wins; every kept candidate is at most one test.
            bool heldTested = false;
            for (int k = 0; k < kept; k++)
            {
                bool visible = Visible(query, ranked[k]);
                if (ReferenceEquals(ranked[k], query.Held)) { heldTested = true; heldVisible = visible; }
                if (visible) { best = ranked[k]; bestScore = rankedScores[k]; break; }
            }
            // A held target that was not tested (ranked lower, or beyond the budget) gets one extra test.
            if (heldFound && !heldTested) heldVisible = Visible(query, query.Held);
            Array.Clear(ranked, 0, kept);
        }
        if (heldFound && heldVisible && (best == null || !PNTargetRanking.ShouldSwitch(heldScore, bestScore, query.Rule))) return query.Held;
        return best;
    }
    private static bool Visible(in PNTargetQuery query, PNStat target)
    {
        var start = query.Origin + Vector3.up * .6f;
        var end = PNTargetCandidates.TryGetHitVolume(target, out var center, out _) ? center : target.transform.position + Vector3.up * .6f;
        return PNAgentSpatial.HasSight(query.Chooser, start, target, end, ref sightBuffer);
    }
}
#endif
