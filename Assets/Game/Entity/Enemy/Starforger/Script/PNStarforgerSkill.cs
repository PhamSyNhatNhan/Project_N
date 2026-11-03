#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using UnityEngine;

// Starforger's enemy-skill owner. Phase policy (HP thresholds, behavior switches) belongs here, as PNSentinelSkill does; the shared owner knows none.
// It also keeps the stars the owner made: each star area registers itself here while it runs, so the star skill knows which slots are free (and when all are taken) and the slam can
// make every star shoot toward its landing.
[DisallowMultipleComponent]
public class PNStarforgerSkill : PNEnemySkill
{
    private readonly List<PNStarforgerStarArea> stars = new List<PNStarforgerStarArea>();
    public int AliveCount => stars.Count;
    public void Register(PNStarforgerStarArea star) { if (star != null && !stars.Contains(star)) stars.Add(star); }
    public void Unregister(PNStarforgerStarArea star) { stars.Remove(star); }
    // Slots in use, for the planner (index = slot).
    public bool[] UsedSlots(int count) { var used = new bool[count]; foreach (var s in stars) if (s.Slot >= 0 && s.Slot < count) used[s.Slot] = true; return used; }
    // Every running star shoots one bolt toward `point` now (the slam's take-off); the stars' own rhythm is untouched.
    public int FireStarsToward(Vector3 point) { int n = 0; foreach (var s in stars.ToArray()) if (s.FireToward(point)) n++; return n; }
}
#endif
