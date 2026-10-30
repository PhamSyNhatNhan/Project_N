#if UNITY_5_3_OR_NEWER
using EveEngine;

// The Starkeeper fights up close: the shared melee state machine (acquire, chase, windup, execute, recover) casting the Starkeeper's own registered slash.
// Tuning (ranges, windup, recovery) is on the component in the prefab; damage and cooldown are the registered data in PNStarkeeperCombatContent.
public sealed class PNStarkeeperBehavior : PNEnemyMeleeBehavior
{
    protected override EveId AttackSkillId => PNStarkeeperCombatContent.Slash;
}
#endif
