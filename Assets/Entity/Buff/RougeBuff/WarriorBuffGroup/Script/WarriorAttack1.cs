using UnityEngine;

public class WarriorAttack1 : StatMinorBuff
{
    public override string BuffId      => "warrior_atk_1";
    public override string DisplayName => "Warrior's Strength I";
    public override string Description => "Tăng 15% Attack.";


    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { attackMultiplier = 15f };
}
