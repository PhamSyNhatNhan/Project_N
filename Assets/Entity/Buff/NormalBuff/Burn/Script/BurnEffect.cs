using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Burn — StackingEffect
/// Mỗi stack tăng damage/tick, refresh duration khi stack thêm.
/// Hết giờ → mất hết stack.
/// </summary>
public class BurnEffect : StackingEffect
{
    public override EffectType     Type        => EffectType.Burn;
    public override EffectCategory Category    => EffectCategory.Debuff;
    public override string         DisplayName => "Burn";
    public override List<string>   IconPaths   => new List<string> { "Effects/Burn/Burn" };

    // ── Default Config ────────────────────────────────────────────
    public override float DefaultDuration     => 7f;
    public override float DefaultTickInterval => 1f;
    public override int   DefaultMaxStacks    => 30;

    // ── Default Damages ───────────────────────────────────────────
    public override List<DamageEntry> DefaultDamages => new List<DamageEntry>
    {
        new DamageEntry(DamageType.Magic, 10f)
    };

    // ── OnInterval — deal damage mỗi tick theo stack ──────────────
    protected override void OnInterval()
    {
        if (target == null) return;

        var damages = GetDamages();
        foreach (var entry in damages)
            target.TakeDamage(entry.Type, entry.Amount * curStacks, 0f, 0f);
    }

    protected override void OnApplyValue()  { }
    protected override void OnRemoveValue() { }

    protected override void OnStackAdded(int current)
    {
        // TODO: tăng intensity VFX theo stack
    }

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