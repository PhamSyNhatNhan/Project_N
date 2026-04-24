using UnityEngine;

public class WarriorAttack2 : StatMinorBuff
{
    public override string BuffId      => "warrior_atk_2";
    public override string DisplayName => "Warrior's Strength II";
    public override string Description => "Tăng thêm 15% Attack.";


    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { attackMultiplier = 15f };
}
