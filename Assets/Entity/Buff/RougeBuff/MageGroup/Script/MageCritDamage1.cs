using UnityEngine;

public class MageCritDamage1 : StatMinorBuff
{
    public override string BuffId      => "mage_critdmg_1";
    public override string DisplayName => "Arcane Overload";
    public override string Description => "Tăng 30% CritDamage.";
 
    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { critDamageMultiplier = 30f };
}