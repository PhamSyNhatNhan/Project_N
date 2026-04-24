using System.Collections.Generic;
using UnityEngine;

// ── ImmortalEffect ────────────────────────────────────────────────────
/// <summary>
/// Immortal — SimpleEffect
/// Không thể chết trong duration — HP không xuống dưới 1
/// </summary>
public class ImmortalEffect : SimpleEffect
{
    public override EffectType     Type        => EffectType.Immortal; 
    public override EffectCategory Category    => EffectCategory.Buff;
    public override string         DisplayName => "Immortal";
    public override List<string>   IconPaths   => new List<string> { "Effects/Immortal/Immortal" };

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
        // Giới hạn damage không được làm HP xuống dưới 1
        float maxAllowedDamage = target.CurHealth - 1f;
        return Mathf.Min(damage, Mathf.Max(0f, maxAllowedDamage));
    }
}