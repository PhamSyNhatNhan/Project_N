using System;

/// <summary>
/// Lưu trạng thái player giữa các tầng.
/// Ghi ra JSON khi clear phòng, đọc lại khi load scene mới.
/// </summary>
[Serializable]
public class RunSaveData
{
    public Talent  talent;
    public int     currentFloor;
    public string  dungeonGroupId;
    public bool    isRoomCleared;   // room đã clear, cần spawn ExitGate khi continue
    public bool    isBuffSelected;  // đã chọn buff, cần load scene tiếp khi continue

    // ── Health runtime ────────────────────────────────────────────
    public float curHealth;

    // ── Bonus Flat ────────────────────────────────────────────────
    // Chỉ các chỉ số có base value lớn, có ý nghĩa khi cộng flat
    public float bonusFlatHealth;
    public float bonusFlatDefense;
    public float bonusFlatResistantPhysical;
    public float bonusFlatResistantMagic;
    public float bonusFlatAttack;

    // ── Bonus Multiplier (%) ──────────────────────────────────────
    public float bonusMultiplierHealth;
    public float bonusMultiplierDefense;
    public float bonusMultiplierResistantPhysical;
    public float bonusMultiplierResistantMagic;
    public float bonusMultiplierAttack;
    public float bonusMultiplierAttackSpeed;
    public float bonusMultiplierBonusDamage;
    public float bonusMultiplierBonusPhysical;
    public float bonusMultiplierBonusMagic;
    public float bonusMultiplierCritRate;
    public float bonusMultiplierCritDamage;
    public float bonusMultiplierMultiplierDamageBonus;
    public float bonusMultiplierMultiplierDamageTaken;

    // ── RogueBuff ─────────────────────────────────────────────────
    public RogueBuffSaveData rogueBuffData;
}