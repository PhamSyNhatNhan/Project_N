using UnityEngine;

public class WarriorCrit1 : StatMinorBuff
{
    public override string BuffId      => "warrior_crit_1";
    public override string DisplayName => "Warrior's Precision I";
    public override string Description => "Tăng 10% CritRate và 20% CritDamage.";


    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { critRateMultiplier = 10f, critDamageMultiplier = 20f };
}
