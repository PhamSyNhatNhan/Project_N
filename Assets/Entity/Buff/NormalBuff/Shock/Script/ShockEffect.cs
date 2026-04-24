using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shock — StackableEffect
/// Tích đủ stack → burst damage AOE xung quanh target
/// Không giữ stack dư, không stack trong trigger
/// </summary>
public class ShockEffect : StackableEffect
{
    public override EffectType     Type        => EffectType.Shock;
    public override EffectCategory Category    => EffectCategory.Debuff;
    public override string         DisplayName => "Shock";
    public override List<string>   IconPaths   => new List<string> { "Effects/Shock/Shock" };

    private float burstDamage = 300f;
    private float burstRadius = 3f;
    private LayerMask damageLayer;

    public void SetConfig(float damage, float radius, LayerMask layer)
    {
        burstDamage  = damage;
        burstRadius  = radius;
        damageLayer  = layer;
    }

    protected override void OnThresholdMet()
    {
        // AOE burst — damage tất cả entity trong radius
        var hits = Physics2D.OverlapCircleAll(target.transform.position, burstRadius, damageLayer);
        foreach (var hit in hits)
        {
            var stat = hit.GetComponent<Stat>();
            if (stat != null)
                stat.TakeDamage(DamageType.Magic, burstDamage, 0f, 0f);
        }
    }

    protected override void OnTriggerEnd() { } // không cần cleanup

    public override void OnApply()  => base.OnApply();
    public override void OnRemove() => base.OnRemove();
}