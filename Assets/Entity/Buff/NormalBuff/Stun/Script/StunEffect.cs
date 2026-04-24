using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stun — SimpleEffect
/// Khóa hoàn toàn movement và input
/// </summary>
public class StunEffect : SimpleEffect
{
    public override EffectType     Type        => EffectType.Stun;
    public override EffectCategory Category    => EffectCategory.Debuff;
    public override string         DisplayName => "Stun";
    public override List<string>   IconPaths   => new List<string> { "Effects/Stun/Stun" };

    private Move move;

    public override void OnApply()
    {
        move = target?.GetComponent<Move>();
        if (move != null) move.CanMove = false;
        base.OnApply();
    }

    public override void OnRemove()
    {
        if (move != null) move.CanMove = true;
        base.OnRemove();
    }
}