using UnityEngine;

public class AssassinCritRate2 : StatMinorBuff
{
    public override string BuffId      => "assassin_critrate_2";
    public override string DisplayName => "Shadow Precision II";
    public override string Description => "Tăng thêm 12% CritRate.";
 
    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { critRateMultiplier = 12f };
}
