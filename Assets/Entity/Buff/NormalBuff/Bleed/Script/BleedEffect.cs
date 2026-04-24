using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bleed — StackingEffect
/// Damage flat mỗi tick + giảm MaxHP theo số stack
/// Khi remove → trả lại MaxHP
/// </summary>
public class BleedEffect : StackingEffect
{
    public override EffectType     Type        => EffectType.Bleed;
    public override EffectCategory Category    => EffectCategory.Debuff;
    public override string         DisplayName => "Bleed";
    public override List<string>   IconPaths   => new List<string> { "Effects/Bleed/Bleed" };

    private float damagePerStack  = 15f;
    private float maxHpReducePerStack = 50f; // giảm flat MaxHP mỗi stack

    protected override float CalcValue() => curStacks * damagePerStack;

    protected override void OnApplyValue()
    {
        if (target == null) return;
        target.MaxHealth -= maxHpReducePerStack;
        target.CurHealth  = Mathf.Min(target.CurHealth, target.MaxHealth);
    }

    protected override void OnRemoveValue()
    {
        if (target == null) return;
        target.MaxHealth += maxHpReducePerStack;
    }

    protected override void OnInterval()
    {
        if (target == null) return;
        target.TakeDamage(DamageType.Physical, CalcValue(), 0f, 0f);
    }

    public override void OnApply()  => base.OnApply();
    public override void OnRemove() => base.OnRemove();
}