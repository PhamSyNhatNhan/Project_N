using UnityEngine;

public class WarriorVitality1 : StatMinorBuff
{
    public override string BuffId      => "warrior_vit_1";
    public override string DisplayName => "Warrior's Vitality I";
    public override string Description => "Tăng 20% HP và 10% Defense.";

    protected override ScalingConfig GetScalingConfig()
        => new ScalingConfig { healthMultiplier = 20f, defenseMultiplier = 10f };
}
