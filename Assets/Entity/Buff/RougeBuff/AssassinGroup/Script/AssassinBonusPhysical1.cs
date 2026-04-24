using UnityEngine;

public class AssassinBonusPhysical1 : StatMinorBuff
{
    public override string BuffId      => "assassin_phys_1";
    public override string DisplayName => "Shadow Edge";
    public override string Description => "Tăng 20% BonusPhysical.";
 
    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { bonusPhysicalMultiplier = 20f };
}