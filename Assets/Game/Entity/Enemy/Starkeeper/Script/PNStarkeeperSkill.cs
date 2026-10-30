#if UNITY_5_3_OR_NEWER
using UnityEngine;

// Starkeeper's enemy-skill owner. Phase policy (HP thresholds, behavior switches) belongs here, as PNSentinelSkill does; the shared owner knows none.
// Empty on purpose: the melee behavior casts the registered slash, nothing else is decided yet.
[DisallowMultipleComponent]
public class PNStarkeeperSkill : PNEnemySkill
{
}
#endif
