#if UNITY_5_3_OR_NEWER
using EveEngine;
using UnityEngine;

// Example enemy-specific phase policy. The shared owner does not know HP thresholds.
public sealed class PNSentinelSkill : PNEnemySkill
{
    [SerializeField, Range(.01f, .99f)] private float chargePhaseHealth = .5f;
    public override void Load(EveEntityLoadContext context)
    { if (float.IsNaN(chargePhaseHealth) || chargePhaseHealth <= 0 || chargePhaseHealth >= 1) throw new System.ArgumentException("Phase HP fraction must be between zero and one."); base.Load(context); }
    protected override void Update()
    {
        base.Update();
        var loader = GetComponent<EveEntityLoader>(); var stat = GetComponent<PNStat>(); var clock = GetComponent<EveTimeScale>();
        if (ActiveBehaviorId != "melee" || Failure != null || loader?.LoadContext?.CanSimulate != true ||
            stat == null || !stat.IsReady || !stat.HasHealth || stat.IsDead || (clock != null && clock.isActiveAndEnabled && clock.Scale <= 0) || Time.deltaTime <= 0) return;
        if (stat.GetCurrent(PNContentIds.Health) > stat.Evaluate(PNContentIds.Health) * chargePhaseHealth) return;
        Switch("charge", PNEnemySwitchPolicy.CancelCurrent);
    }
}
#endif
