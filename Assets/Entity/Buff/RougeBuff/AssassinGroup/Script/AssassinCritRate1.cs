using UnityEngine;

public class AssassinCritRate1 : StatMinorBuff
{
    public override string BuffId      => "assassin_critrate_1";
    public override string DisplayName => "Shadow Precision I";
    public override string Description => "Tăng 12% CritRate.";
 
    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { critRateMultiplier = 12f };
}
