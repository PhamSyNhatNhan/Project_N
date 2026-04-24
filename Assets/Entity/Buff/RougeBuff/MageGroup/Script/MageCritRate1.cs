using UnityEngine;

public class MageCritRate1 : StatMinorBuff
{
    public override string BuffId      => "mage_critrate_1";
    public override string DisplayName => "Arcane Precision";
    public override string Description => "Tăng 10% CritRate và giảm 15% sát thương nhận vào từ phép.";
 
    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig
        {
            critRateMultiplier              = 10f,
            resistantMagicMultiplier        = 15f,
        };
}