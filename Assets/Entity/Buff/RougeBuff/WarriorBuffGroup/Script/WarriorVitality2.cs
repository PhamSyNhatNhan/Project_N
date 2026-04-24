using UnityEngine;

public class WarriorVitality2 : StatMinorBuff
{
    public override string BuffId      => "warrior_vit_2";
    public override string DisplayName => "Warrior's Vitality II";
    public override string Description => "Tăng thêm 20% HP và 10% Defense.";

    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { healthMultiplier = 20f, defenseMultiplier = 10f };
}
