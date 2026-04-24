using UnityEngine;

public class MageBonusMagic2 : StatMinorBuff
{
    public override string BuffId      => "mage_magic_2";
    public override string DisplayName => "Arcane Power II";
    public override string Description => "Tăng thêm 20% BonusMagic.";
 
    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { bonusMagicMultiplier = 20f };
}