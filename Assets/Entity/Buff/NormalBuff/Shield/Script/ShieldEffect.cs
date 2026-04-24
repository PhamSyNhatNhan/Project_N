using System.Collections.Generic;
using UnityEngine;
 
// ── ShieldEffect ──────────────────────────────────────────────────────
/// <summary>
/// Shield — SimpleEffect
/// Hấp thụ damage trước khi trừ vào HP
/// </summary>
public class ShieldEffect : SimpleEffect
{
    public override EffectType Type => EffectType.Shield;
    public override EffectCategory Category    => EffectCategory.Buff;
    public override string         DisplayName => "Shield";
    public override List<string>   IconPaths   => new List<string> { "Effects/Shield/Shield" };
 
    private float shieldAmount;
    private float curShield;
 
    public void SetShield(float amount)
    {
        shieldAmount = amount;
        curShield    = amount;
    }
 
    public override void OnApply()
    {
        target.OnBeforeTakeDamage += AbsorbDamage;
        base.OnApply();
    }
 
    public override void OnRemove()
    {
        target.OnBeforeTakeDamage -= AbsorbDamage;
        base.OnRemove();
    }
 
    private float AbsorbDamage(float damage, DamageType type)
    {
        if (curShield <= 0f) return damage;
 
        float absorbed = Mathf.Min(curShield, damage);
        curShield -= absorbed;
        float remaining = damage - absorbed;
 
        // Shield hết → tự remove
        if (curShield <= 0f)
            target.GetComponent<StatusEffectManager>()?.Remove(Type);
 
        return remaining;
    }
}