using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Poison — StackingEffect
/// Damage tăng theo stack nhưng diminishing returns
/// Stack 1→10, Stack 2→16, Stack 3→19...
/// </summary>
public class PoisonEffect : StackingEffect
{
    public override EffectType     Type        => EffectType.Poison;
    public override EffectCategory Category    => EffectCategory.Debuff;
    public override string         DisplayName => "Poison";
    public override List<string>   IconPaths   => new List<string> { "Effects/Poison/Poison" };

    private float baseDamage = 10f;

    // Diminishing returns: value(n) = base * (1 - 0.5^n) / (1 - 0.5)
    protected override float CalcValue()
    {
        float ratio = 0.5f;
        return baseDamage * (1f - Mathf.Pow(ratio, curStacks)) / (1f - ratio);
    }

    protected override void OnInterval()
    {
        if (target == null) return;
        target.TakeDamage(DamageType.True, CalcValue(), 0f, 0f);
    }

    public override void OnApply()  => base.OnApply();
    public override void OnRemove() => base.OnRemove();
}