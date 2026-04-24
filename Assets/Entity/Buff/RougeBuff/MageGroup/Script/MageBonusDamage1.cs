using UnityEngine;

public class MageBonusDamage1 : StatMinorBuff
{
    public override string BuffId      => "mage_dmg_1";
    public override string DisplayName => "Arcane Surge I";
    public override string Description => "Tăng 15% Bonus Damage.";
 
    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { bonusDamageMultiplier = 15f };
}
