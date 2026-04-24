using UnityEngine;

// ════════════════════════════════════════════════════════════════
//  RogueBuff — Base class cho tất cả buff
// ════════════════════════════════════════════════════════════════
public abstract class RogueBuff : MonoBehaviour
{
    public abstract string BuffId      { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }

    [SerializeField] private Sprite icon;
    public Sprite Icon => icon;

    protected Stat stat;

    public bool IsApplied { get; private set; }

    protected virtual void Awake()
    {
        stat = GetComponentInParent<Stat>();
    }

    public void Apply()
    {
        if (IsApplied) return;
        IsApplied = true;
        OnApply();
    }

    public void Remove()
    {
        if (!IsApplied) return;
        IsApplied = false;
        OnRemove();
    }

    protected abstract void OnApply();
    protected abstract void OnRemove();
}

// ════════════════════════════════════════════════════════════════
//  MinorBuff / MajorBuff
// ════════════════════════════════════════════════════════════════
public abstract class MinorBuff : RogueBuff { }
public abstract class MajorBuff : RogueBuff { }

// ════════════════════════════════════════════════════════════════
//  StatMinorBuff — apply vào StatBonus layer, không cộng thẳng
//  bonusMultiplier. Dùng BuffId làm key để add/remove độc lập.
//  Không bị double-apply khi restore từ save.
// ════════════════════════════════════════════════════════════════
public abstract class StatMinorBuff : MinorBuff
{
    protected abstract ScalingConfig GetScalingConfig();

    protected override void OnApply()
    {
        ApplyToStatBonus(GetScalingConfig());
        stat.RecalculateStat();
    }

    protected override void OnRemove()
    {
        RemoveFromStatBonus();
        stat.RecalculateStat();
    }

    private void ApplyToStatBonus(ScalingConfig c)
    {
        if (c.healthMultiplier                != 0) stat.BuffHealth.SetMultiplier(BuffId,                c.healthMultiplier);
        if (c.defenseMultiplier               != 0) stat.BuffDefense.SetMultiplier(BuffId,               c.defenseMultiplier);
        if (c.resistantPhysicalMultiplier     != 0) stat.BuffResistantPhysical.SetMultiplier(BuffId,     c.resistantPhysicalMultiplier);
        if (c.resistantMagicMultiplier        != 0) stat.BuffResistantMagic.SetMultiplier(BuffId,        c.resistantMagicMultiplier);
        if (c.attackMultiplier                != 0) stat.BuffAttack.SetMultiplier(BuffId,                c.attackMultiplier);
        if (c.attackSpeedMultiplier           != 0) stat.BuffAttackSpeed.SetMultiplier(BuffId,           c.attackSpeedMultiplier);
        if (c.bonusDamageMultiplier           != 0) stat.BuffBonusDamage.SetMultiplier(BuffId,           c.bonusDamageMultiplier);
        if (c.bonusPhysicalMultiplier         != 0) stat.BuffBonusPhysical.SetMultiplier(BuffId,         c.bonusPhysicalMultiplier);
        if (c.bonusMagicMultiplier            != 0) stat.BuffBonusMagic.SetMultiplier(BuffId,            c.bonusMagicMultiplier);
        if (c.critRateMultiplier              != 0) stat.BuffCritRate.SetMultiplier(BuffId,              c.critRateMultiplier);
        if (c.critDamageMultiplier            != 0) stat.BuffCritDamage.SetMultiplier(BuffId,            c.critDamageMultiplier);
        if (c.multiplierDamageBonusMultiplier != 0) stat.BuffMultiplierDamageBonus.SetMultiplier(BuffId, c.multiplierDamageBonusMultiplier);
        if (c.multiplierDamageTakenMultiplier != 0) stat.BuffMultiplierDamageTaken.SetMultiplier(BuffId, c.multiplierDamageTakenMultiplier);
    }

    private void RemoveFromStatBonus()
    {
        stat.BuffHealth.RemoveMultiplier(BuffId);
        stat.BuffDefense.RemoveMultiplier(BuffId);
        stat.BuffResistantPhysical.RemoveMultiplier(BuffId);
        stat.BuffResistantMagic.RemoveMultiplier(BuffId);
        stat.BuffAttack.RemoveMultiplier(BuffId);
        stat.BuffAttackSpeed.RemoveMultiplier(BuffId);
        stat.BuffBonusDamage.RemoveMultiplier(BuffId);
        stat.BuffBonusPhysical.RemoveMultiplier(BuffId);
        stat.BuffBonusMagic.RemoveMultiplier(BuffId);
        stat.BuffCritRate.RemoveMultiplier(BuffId);
        stat.BuffCritDamage.RemoveMultiplier(BuffId);
        stat.BuffMultiplierDamageBonus.RemoveMultiplier(BuffId);
        stat.BuffMultiplierDamageTaken.RemoveMultiplier(BuffId);
    }
}

// ════════════════════════════════════════════════════════════════
//  StatMajorBuff — tương tự StatMinorBuff
// ════════════════════════════════════════════════════════════════
public abstract class StatMajorBuff : MajorBuff
{
    protected abstract ScalingConfig GetScalingConfig();

    protected override void OnApply()
    {
        ApplyToStatBonus(GetScalingConfig());
        stat.RecalculateStat();
    }

    protected override void OnRemove()
    {
        RemoveFromStatBonus();
        stat.RecalculateStat();
    }

    private void ApplyToStatBonus(ScalingConfig c)
    {
        if (c.healthMultiplier                != 0) stat.BuffHealth.SetMultiplier(BuffId,                c.healthMultiplier);
        if (c.defenseMultiplier               != 0) stat.BuffDefense.SetMultiplier(BuffId,               c.defenseMultiplier);
        if (c.resistantPhysicalMultiplier     != 0) stat.BuffResistantPhysical.SetMultiplier(BuffId,     c.resistantPhysicalMultiplier);
        if (c.resistantMagicMultiplier        != 0) stat.BuffResistantMagic.SetMultiplier(BuffId,        c.resistantMagicMultiplier);
        if (c.attackMultiplier                != 0) stat.BuffAttack.SetMultiplier(BuffId,                c.attackMultiplier);
        if (c.attackSpeedMultiplier           != 0) stat.BuffAttackSpeed.SetMultiplier(BuffId,           c.attackSpeedMultiplier);
        if (c.bonusDamageMultiplier           != 0) stat.BuffBonusDamage.SetMultiplier(BuffId,           c.bonusDamageMultiplier);
        if (c.bonusPhysicalMultiplier         != 0) stat.BuffBonusPhysical.SetMultiplier(BuffId,         c.bonusPhysicalMultiplier);
        if (c.bonusMagicMultiplier            != 0) stat.BuffBonusMagic.SetMultiplier(BuffId,            c.bonusMagicMultiplier);
        if (c.critRateMultiplier              != 0) stat.BuffCritRate.SetMultiplier(BuffId,              c.critRateMultiplier);
        if (c.critDamageMultiplier            != 0) stat.BuffCritDamage.SetMultiplier(BuffId,            c.critDamageMultiplier);
        if (c.multiplierDamageBonusMultiplier != 0) stat.BuffMultiplierDamageBonus.SetMultiplier(BuffId, c.multiplierDamageBonusMultiplier);
        if (c.multiplierDamageTakenMultiplier != 0) stat.BuffMultiplierDamageTaken.SetMultiplier(BuffId, c.multiplierDamageTakenMultiplier);
    }

    private void RemoveFromStatBonus()
    {
        stat.BuffHealth.RemoveMultiplier(BuffId);
        stat.BuffDefense.RemoveMultiplier(BuffId);
        stat.BuffResistantPhysical.RemoveMultiplier(BuffId);
        stat.BuffResistantMagic.RemoveMultiplier(BuffId);
        stat.BuffAttack.RemoveMultiplier(BuffId);
        stat.BuffAttackSpeed.RemoveMultiplier(BuffId);
        stat.BuffBonusDamage.RemoveMultiplier(BuffId);
        stat.BuffBonusPhysical.RemoveMultiplier(BuffId);
        stat.BuffBonusMagic.RemoveMultiplier(BuffId);
        stat.BuffCritRate.RemoveMultiplier(BuffId);
        stat.BuffCritDamage.RemoveMultiplier(BuffId);
        stat.BuffMultiplierDamageBonus.RemoveMultiplier(BuffId);
        stat.BuffMultiplierDamageTaken.RemoveMultiplier(BuffId);
    }
}