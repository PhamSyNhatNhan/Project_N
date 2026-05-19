using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Immortal — SimpleEffect
/// Không thể chết trong duration — HP không xuống dưới 1.
/// UseTimeScale = false — không bị ảnh hưởng bởi time stop.
/// </summary>
public class ImmortalEffect : SimpleEffect
{
    public override EffectType     Type        => EffectType.Immortal;
    public override EffectCategory Category    => EffectCategory.Buff;
    public override string         DisplayName => "Immortal";
    public override List<string>   IconPaths   => new List<string> { "Effects/Immortal/Immortal" };

    // ── Default Config ────────────────────────────────────────────
    public override float DefaultDuration  => 3f;
    public override bool  UseTimeScale     => false;

    public override void OnApply()
    {
        target.OnBeforeTakeDamage += ClampDamage;
        base.OnApply();
    }

    public override void OnRemove()
    {
        target.OnBeforeTakeDamage -= ClampDamage;
        base.OnRemove();
    }

    private float ClampDamage(float damage, DamageType type)
    {
        float maxAllowedDamage = target.CurHealth - 1f;
        return Mathf.Min(damage, Mathf.Max(0f, maxAllowedDamage));
    }
}