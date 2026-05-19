using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Slow — SimpleEffect
/// Giảm tốc độ di chuyển qua Move.BuffMoveSpeed — không ảnh hưởng animation.
/// </summary>
public class SlowEffect : SimpleEffect
{
    public override EffectType     Type        => EffectType.Slow;
    public override EffectCategory Category    => EffectCategory.Debuff;
    public override string         DisplayName => "Slow";
    public override List<string>   IconPaths   => new List<string> { "Effects/Slow/Slow" };

    // ── Default Config ────────────────────────────────────────────
    public override float DefaultDuration => 3f;

    private const string ModifierKey  = "slow_effect";
    private const float  SlowMultiplier = -50f; // giảm 50% speed

    private Move _move;

    public override void OnApply()
    {
        _move = target?.GetComponent<Move>();
        _move?.BuffMoveSpeed.SetMultiplier(ModifierKey, SlowMultiplier);
        base.OnApply();
    }

    public override void OnRemove()
    {
        _move?.BuffMoveSpeed.RemoveMultiplier(ModifierKey);
        base.OnRemove();
    }

    public override void Reset()
    {
        base.Reset();
        _move = null;
    }
}