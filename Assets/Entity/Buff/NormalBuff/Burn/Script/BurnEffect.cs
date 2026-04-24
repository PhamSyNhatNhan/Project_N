using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Burn — StackingEffect
/// Mỗi stack tăng damage/tick, refresh duration khi stack thêm
/// Hết giờ → mất hết stack
/// </summary>
public class BurnEffect : StackingEffect
{
    // ── Identity ──────────────────────────────────────────────────
    public override EffectType     Type        => EffectType.Burn;
    public override EffectCategory Category    => EffectCategory.Debuff;
    public override string         DisplayName => "Burn";

    // Path trong Resources/ — không có extension
    // icons[0] = icon thường, icons[1] = icon max stack (optional)
    public override List<string> IconPaths => new List<string>
    {
        "Effects/Burn/Burn"
    };

    // ── Config ────────────────────────────────────────────────────
    private float baseDamagePerStack = 10f;

    // ── CalcValue ─────────────────────────────────────────────────
    protected override float CalcValue() => curStacks;

    // ── OnApplyValue / OnRemoveValue ──────────────────────────────
    protected override void OnApplyValue()  { }
    protected override void OnRemoveValue() { }

    // ── OnInterval — deal damage mỗi tick ────────────────────────
    protected override void OnInterval()
    {
        if (target == null) return;
        float damage = baseDamagePerStack * CalcValue();
        target.TakeDamage(DamageType.Magic, damage, 0f, 0f);
    }

    // ── OnStackAdded ──────────────────────────────────────────────
    protected override void OnStackAdded(int current)
    {
        // TODO: tăng intensity VFX theo stack
    }

    // ── OnApply / OnRemove ────────────────────────────────────────
    public override void OnApply()
    {
        // TODO: bật VFX burn
        base.OnApply();
    }

    public override void OnRemove()
    {
        // TODO: tắt VFX burn
        base.OnRemove();
    }
}