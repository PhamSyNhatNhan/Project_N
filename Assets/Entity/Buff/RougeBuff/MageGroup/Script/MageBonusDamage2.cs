using UnityEngine;

public class MageBonusDamage2 : StatMinorBuff
{
    public override string BuffId      => "mage_dmg_2";
    public override string DisplayName => "Arcane Surge II";
    public override string Description => "Tăng thêm 15% Bonus Damage.";
 
    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { bonusDamageMultiplier = 15f };
}