// EN: Extension point for skill behavior shared by enemies: the behavior runner (PNBehaviorSkill) plus what an enemy needs to walk: PNEnemyMove, prepared world navigation, and navigation held while a skill casts.
// VI: Điểm kế thừa cho hành vi skill chung của các nhân vật địch: bộ chạy behavior (PNBehaviorSkill) cộng phần quái cần để đi lại: PNEnemyMove, navigation của world, và giữ navigation khi đang tung skill.
public class PNEnemySkill : PNBehaviorSkill
{
#if UNITY_5_3_OR_NEWER
    protected override System.Collections.Generic.IEnumerable<System.Type> BehaviorDependencies => new[] { typeof(PNControlManager), typeof(PNAbilityManager), typeof(PNEnemyMove), typeof(PNStat) };
    protected override void ValidateBehaviorWorld(EveEngine.EveEntityLoadContext context)
    {
        if (context.Session.HasAuthority && context.Identity.World?.CanExecute == true && PNWorldNavigation.Find(context.Identity.World) == null)
            throw new System.InvalidOperationException("Enemy behavior requires prepared world navigation before publication.");
    }
    protected override bool CanStartBehavior => GetComponent<PNEnemyMove>().NavigationReady;
    protected override System.IDisposable SuspendMovement() => GetComponent<PNEnemyMove>().SuspendNavigation();
    protected override void CancelMovement()
    {
        var move = GetComponent<PNEnemyMove>();
        if (move?.NavigationRequest != null) move.CancelNavigation(move.NavigationRequest);
    }
#endif
}
