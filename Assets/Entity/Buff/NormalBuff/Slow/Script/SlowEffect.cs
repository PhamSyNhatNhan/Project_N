using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Slow — SimpleEffect
/// Giảm tốc độ di chuyển qua TimeScale modifier
/// </summary>
public class SlowEffect : SimpleEffect
{
    public override EffectType     Type        => EffectType.Slow;
    public override EffectCategory Category    => EffectCategory.Debuff;
    public override string         DisplayName => "Slow";
    public override List<string>   IconPaths   => new List<string> { "Effects/Slow/Slow" };

    private const string ModifierKey = "slow_effect";
    private float slowScale = 0.5f; // 50% tốc độ

    public override void OnApply()
    {
        timeScale?.AddModifier(ModifierKey, slowScale);
        base.OnApply();
    }

    public override void OnRemove()
    {
        timeScale?.RemoveModifier(ModifierKey);
        base.OnRemove();
    }
}