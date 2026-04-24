using UnityEngine;

public class AssassinAttackSpeed1 : StatMinorBuff
{
    public override string BuffId      => "assassin_atkspd_1";
    public override string DisplayName => "Shadow Haste";
    public override string Description => "Tăng 25% tốc độ tấn công.";
 
    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { attackSpeedMultiplier = 25f };
}