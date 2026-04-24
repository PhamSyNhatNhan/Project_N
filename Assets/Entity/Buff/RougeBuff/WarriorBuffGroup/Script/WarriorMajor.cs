using UnityEngine;

public class WarriorMajor : MajorBuff
{
    public override string BuffId      => "warrior_major";
    public override string DisplayName => "Warrior's Dominion";
    public override string Description => "Tăng mạnh Attack. Mỗi lần nhận sát thương tích 1 rage (+5% Attack, tối đa 10 stack).";

    private string entityKey;
    private int    rageStacks = 0;
    private const int   MaxRage         = 10;
    private const float AttackPerRage   = 5f;

    protected override void OnApply()
    {
        stat.Initialize(new ScalingConfig
        {
            attackMultiplier      = 30f,
            bonusDamageMultiplier = 15f,
            critRateMultiplier    = 5f,
            critDamageMultiplier  = 30f,
            healthMultiplier      = 40f,
        });

        entityKey = $"{stat.NameCharacter}_{stat.GetInstanceID()}";
        stat.OnAfterTakeDamage += HandleTakeDamage;
    }

    protected override void OnRemove()
    {
        stat.Initialize(new ScalingConfig
        {
            attackMultiplier      = -30f,
            bonusDamageMultiplier = -15f,
            critRateMultiplier    = -5f,
            critDamageMultiplier  = -30f,
            healthMultiplier      = -40f,
        });

        // Rollback rage stacks đã tích
        if (rageStacks > 0)
        {
            stat.Initialize(new ScalingConfig { attackMultiplier = -(rageStacks * AttackPerRage) });
            rageStacks = 0;
        }

        stat.OnAfterTakeDamage -= HandleTakeDamage;
    }

    private void HandleTakeDamage(float damage, DamageType type)
    {
        if (rageStacks >= MaxRage) return;
        rageStacks++;
        stat.Initialize(new ScalingConfig { attackMultiplier = AttackPerRage });
    }
}
