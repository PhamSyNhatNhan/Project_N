#if UNITY_5_3_OR_NEWER
using EveEngine;

// A second selectable set shares the decision skeleton, and executes registered special motion.
public sealed class PNEnemyChargeBehavior : PNEnemyMeleeBehavior
{
    protected override EveId AttackSkillId => PNEnemyGameplayContent.Charge;
    protected override PNEnemyActionExecution Execution => PNEnemyActionExecution.AuxiliaryTimeline;
    public override System.Collections.Generic.IEnumerable<EveId> SkillIds => new[] { PNEnemyGameplayContent.Strike, PNEnemyGameplayContent.Charge };
}
#endif
