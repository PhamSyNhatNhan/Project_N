#if UNITY_5_3_OR_NEWER
using UnityEngine;

// Starchanter's enemy-skill owner. Phase policy (HP thresholds, behavior switches) belongs here, as PNSentinelSkill does; the shared owner knows none.
// Empty on purpose: the skill set is not made yet.
[DisallowMultipleComponent]
public class PNStarchanterSkill : PNEnemySkill
{
}
#endif
