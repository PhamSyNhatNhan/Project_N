using UnityEngine;

public class AssassinCritDamage1 : StatMinorBuff
{
    public override string BuffId      => "assassin_critdmg_1";
    public override string DisplayName => "Shadow Strike I";
    public override string Description => "Tăng 25% CritDamage.";
 
    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { critDamageMultiplier = 25f };
}
