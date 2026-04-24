using UnityEngine;

public class MageBonusMagic1 : StatMinorBuff
{
    public override string BuffId      => "mage_magic_1";
    public override string DisplayName => "Arcane Power I";
    public override string Description => "Tăng 20% BonusMagic.";
 
    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { bonusMagicMultiplier = 20f };
}
