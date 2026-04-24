using UnityEngine;

public class AssassinMajor : StatMajorBuff
{
    public override string BuffId      => "assassin_major";
    public override string DisplayName => "Death's Shadow";
    public override string Description => "Hiện thân của bóng tối — tăng mạnh toàn bộ chỉ số sát thương chí mạng.";
 
    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig
        {
            critRateMultiplier      = 20f,
            critDamageMultiplier    = 80f,
            attackSpeedMultiplier   = 30f,
            bonusPhysicalMultiplier = 30f,
            attackMultiplier        = 20f,
        };
}

