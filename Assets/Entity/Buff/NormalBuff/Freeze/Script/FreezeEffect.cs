using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Freeze — StackableEffect
/// Tích đủ stack → freeze hoàn toàn (TimeScale = 0) trong triggerDuration.
/// Hết freeze → xóa toàn bộ stack.
/// UseTimeScale = false — trigger timer dùng real time.
/// Icon 1: đang tích stack. Icon 2: đang đóng băng.
/// </summary>
public class FreezeEffect : StackableEffect
{
    public override EffectType     Type        => EffectType.Freeze;
    public override EffectCategory Category    => EffectCategory.Debuff;
    public override string         DisplayName => "Freeze";
    public override List<string>   IconPaths   => new List<string>
    {
        "Effects/Freeze/Freeze",
        "Effects/Freeze/Freeze_Active"
    };

    // ── Default Config ────────────────────────────────────────────
    public override int   DefaultThreshold       => 5;
    public override float DefaultTriggerDuration => 2f;
    public override float DefaultStackDuration   => 5f;

    // ── UseTimeScale = false ──────────────────────────────────────
    public override bool UseTimeScale => false;

    private const string ModifierKey = "freeze_effect";
    private Move move;

    // ── Threshold Met — freeze hoàn toàn ─────────────────────────
    protected override void OnThresholdMet()
    {
        timeScale?.AddModifier(ModifierKey, 0f);
        move = target?.GetComponent<Move>();
        if (move != null) move.CanMove = false;
        FireEvent();
    }

    // ── Trigger End — unfreeze + xóa stack ───────────────────────
    protected override void OnTriggerEnd()
    {
        timeScale?.RemoveModifier(ModifierKey);
        if (move != null) move.CanMove = true;

        curStacks       = 0;
        stackDecayTimer = 0f;
        FireEvent(isRemoved: true);
    }

    // ── BuildDisplayData ──────────────────────────────────────────
    protected override EffectDisplayData BuildDisplayData(bool isRemoved) => new EffectDisplayData
    {
        Type        = Type,
        Category    = Category,
        DisplayName = DisplayName,
        Icon        = isTriggerActive
                        ? (icons.Count > 1 ? icons[1] : null)
                        : (icons.Count > 0 ? icons[0] : null),
        Duration    = isTriggerActive ? triggerTimer : stackDecayTimer,
        CurStacks   = isTriggerActive ? 0 : curStacks,
        MaxStacks   = isTriggerActive ? 0 : threshold,
        IsRemoved   = isRemoved
    };

    public override void OnApply() => base.OnApply();

    public override void OnRemove()
    {
        timeScale?.RemoveModifier(ModifierKey);
        if (move != null) move.CanMove = true;
        base.OnRemove();
    }
}