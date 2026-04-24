using UnityEngine;

public class MageMajor : StatMajorBuff
{
    public override string BuffId      => "mage_major";
    public override string DisplayName => "Arcane Dominance";
    public override string Description => "Tăng mạnh toàn bộ chỉ số phép thuật.";
 
    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig
        {
            bonusMagicMultiplier            = 40f,
            bonusDamageMultiplier           = 25f,
            critRateMultiplier              = 15f,
            critDamageMultiplier            = 50f,
            multiplierDamageBonusMultiplier = 20f,
        };
}
