using UnityEngine;

public class AssassinCritDamage2 : StatMinorBuff
{
    public override string BuffId      => "assassin_critdmg_2";
    public override string DisplayName => "Shadow Strike II";
    public override string Description => "Tăng thêm 25% CritDamage.";
 
    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { critDamageMultiplier = 25f };
}