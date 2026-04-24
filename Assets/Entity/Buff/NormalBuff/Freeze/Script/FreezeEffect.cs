using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Freeze — StackableEffect
/// Tích đủ stack → freeze hoàn toàn (TimeScale = 0) trong triggerDuration
/// Giữ stack dư sau khi trigger
/// </summary>
public class FreezeEffect : StackableEffect
{
    public override EffectType     Type        => EffectType.Freeze;
    public override EffectCategory Category    => EffectCategory.Debuff;
    public override string         DisplayName => "Freeze";
    public override List<string>   IconPaths   => new List<string> { "Effects/Freeze/Freeze" };

    private const string ModifierKey = "freeze_effect";
    private Move move;

    protected override void OnThresholdMet()
    {
        // Freeze hoàn toàn — TimeScale = 0 + khóa movement
        timeScale?.AddModifier(ModifierKey, 0f);
        move = target?.GetComponent<Move>();
        if (move != null) move.CanMove = false;
    }

    protected override void OnTriggerEnd()
    {
        timeScale?.RemoveModifier(ModifierKey);
        if (move != null) move.CanMove = true;
    }

    public override void OnApply()  => base.OnApply();
    public override void OnRemove()
    {
        timeScale?.RemoveModifier(ModifierKey);
        if (move != null) move.CanMove = true;
        base.OnRemove();
    }
}