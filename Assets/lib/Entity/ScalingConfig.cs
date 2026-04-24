using UnityEngine;

/// <summary>
/// Cấu hình scale chỉ số cho quái theo tầng.
/// Tất cả giá trị tính theo % cộng thêm vào bonusMultiplier hiện tại.
/// Ví dụ: healthMultiplier = 50 → HP tăng thêm 50%.
/// </summary>
[System.Serializable]
public struct ScalingConfig
{
    [Tooltip("% tăng HP")]           public float healthMultiplier;
    [Tooltip("% tăng Defense")]      public float defenseMultiplier;
    [Tooltip("% tăng Resist Phys")]  public float resistantPhysicalMultiplier;
    [Tooltip("% tăng Resist Magic")] public float resistantMagicMultiplier;
    [Tooltip("% tăng Attack")]       public float attackMultiplier;
    [Tooltip("% tăng Attack Speed")] public float attackSpeedMultiplier;
    [Tooltip("% tăng Bonus Damage")] public float bonusDamageMultiplier;
    [Tooltip("% tăng Bonus Phys")]   public float bonusPhysicalMultiplier;
    [Tooltip("% tăng Bonus Magic")]  public float bonusMagicMultiplier;
    [Tooltip("% tăng Crit Rate")]    public float critRateMultiplier;
    [Tooltip("% tăng Crit Damage")]  public float critDamageMultiplier;
    [Tooltip("% tăng DMG Bonus")]    public float multiplierDamageBonusMultiplier;
    [Tooltip("% tăng DMG Taken")]    public float multiplierDamageTakenMultiplier;

    /// <summary>Chỉ scale HP + Attack + Defense.</summary>
    public static ScalingConfig Simple(float hpPercent, float atkPercent, float defPercent = 0f)
        => new ScalingConfig
        {
            healthMultiplier  = hpPercent,
            attackMultiplier  = atkPercent,
            defenseMultiplier = defPercent,
        };

    /// <summary>Scale toàn bộ phòng thủ: Defense + Resist Phys + Resist Magic.</summary>
    public static ScalingConfig Tanky(float defPercent, float resistPhysPercent = 0f, float resistMagicPercent = 0f)
        => new ScalingConfig
        {
            defenseMultiplier            = defPercent,
            resistantPhysicalMultiplier  = resistPhysPercent,
            resistantMagicMultiplier     = resistMagicPercent,
        };

    /// <summary>Scale toàn bộ output sát thương: Attack + BonusDamage + BonusPhys/Magic + CritRate + CritDmg.</summary>
    public static ScalingConfig Aggressive(float atkPercent, float bonusDmgPercent = 0f,
        float bonusPhysPercent = 0f, float bonusMagicPercent = 0f,
        float critRatePercent  = 0f, float critDmgPercent    = 0f)
        => new ScalingConfig
        {
            attackMultiplier        = atkPercent,
            bonusDamageMultiplier   = bonusDmgPercent,
            bonusPhysicalMultiplier = bonusPhysPercent,
            bonusMagicMultiplier    = bonusMagicPercent,
            critRateMultiplier      = critRatePercent,
            critDamageMultiplier    = critDmgPercent,
        };

    /// <summary>Chỉ scale tốc độ đánh.</summary>
    public static ScalingConfig FastAttack(float attackSpeedPercent)
        => new ScalingConfig { attackSpeedMultiplier = attackSpeedPercent };

    /// <summary>Chỉ scale crit (rate + damage).</summary>
    public static ScalingConfig CritFocus(float critRatePercent, float critDmgPercent)
        => new ScalingConfig
        {
            critRateMultiplier   = critRatePercent,
            critDamageMultiplier = critDmgPercent,
        };

    /// <summary>
    /// Cộng dồn hai config lại với nhau.
    /// Dùng để combine preset loại quái + preset tầng.
    /// </summary>
    public static ScalingConfig Combine(ScalingConfig a, ScalingConfig b)
        => new ScalingConfig
        {
            healthMultiplier                = a.healthMultiplier                + b.healthMultiplier,
            defenseMultiplier               = a.defenseMultiplier               + b.defenseMultiplier,
            resistantPhysicalMultiplier     = a.resistantPhysicalMultiplier     + b.resistantPhysicalMultiplier,
            resistantMagicMultiplier        = a.resistantMagicMultiplier        + b.resistantMagicMultiplier,
            attackMultiplier                = a.attackMultiplier                + b.attackMultiplier,
            attackSpeedMultiplier           = a.attackSpeedMultiplier           + b.attackSpeedMultiplier,
            bonusDamageMultiplier           = a.bonusDamageMultiplier           + b.bonusDamageMultiplier,
            bonusPhysicalMultiplier         = a.bonusPhysicalMultiplier         + b.bonusPhysicalMultiplier,
            bonusMagicMultiplier            = a.bonusMagicMultiplier            + b.bonusMagicMultiplier,
            critRateMultiplier              = a.critRateMultiplier              + b.critRateMultiplier,
            critDamageMultiplier            = a.critDamageMultiplier            + b.critDamageMultiplier,
            multiplierDamageBonusMultiplier = a.multiplierDamageBonusMultiplier + b.multiplierDamageBonusMultiplier,
            multiplierDamageTakenMultiplier = a.multiplierDamageTakenMultiplier + b.multiplierDamageTakenMultiplier,
        };
}
