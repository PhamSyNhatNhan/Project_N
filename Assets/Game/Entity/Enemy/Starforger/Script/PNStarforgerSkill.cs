#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// Starforger's enemy-skill owner. Phase policy (HP thresholds, behavior switches) belongs here, as PNSentinelSkill does; the shared owner knows none.
// It also finds the stars the owner made (summons, one group per slot), so the star skill knows which slots are free (and when all are taken) and the slam can
// make every star shoot toward its landing.
[DisallowMultipleComponent]
public class PNStarforgerSkill : PNEnemySkill
{
    private EveEntityId Owner => GetComponent<PNStat>().EntityId;
    // The stars are summons of this entity, one group per slot (PNStarforgerCombatContent.SlotGroup). The summon index also counts a star whose entity is still being prepared.
    public int AliveCount { get { int n = 0; foreach (var s in PNSummons.Of(Owner)) if (PNStarforgerCombatContent.TrySlot(s.Group, out _) && !s.IsRetiring) n++; return n; } }
    // Slots in use, for the planner (index = slot).
    public bool[] UsedSlots(int count) { var used = new bool[count]; for (int i = 0; i < count; i++) used[i] = PNSummons.Count(Owner, PNStarforgerCombatContent.SlotGroup(i)) > 0; return used; }
    // Every running star shoots one bolt toward `point` now (the slam's take-off); the stars' own rhythm is untouched.
    public int FireStarsToward(Vector3 point) { int n = 0; foreach (var s in PNSummons.Of(Owner)) { var star = s.GetComponent<PNStarforgerStarSummon>(); if (star != null && star.FireToward(point)) n++; } return n; }
}
#endif
