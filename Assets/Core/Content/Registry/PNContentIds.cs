using EveEngine;

// EN: Shared identity constants belong to Core, independent of concrete game registration.
// VI: Hằng danh tính chung thuộc Core, độc lập việc đăng ký triển khai game cụ thể.
public static class PNContentIds
{
    public static readonly EveId Before = EveId.Parse("project_n:before");
    public static readonly EveId After = EveId.Parse("project_n:after");
    public static readonly EveId AttackTag = EveId.Parse("project_n:attack");
    public static readonly EveId SkillTag = EveId.Parse("project_n:skill");
    public static readonly EveId UltimateTag = EveId.Parse("project_n:ultimate");
    public static readonly EveId DashTag = EveId.Parse("project_n:dash");
    public static readonly EveId BurstTag = EveId.Parse("project_n:burst");
    // Projectile modifiers: ordinary tags in the shared tag registry, read from the launch-time source snapshot.
    public static readonly EveId ProjectileSpeed = EveId.Parse("project_n:projectile_speed");
    public static readonly EveId ProjectileScale = EveId.Parse("project_n:projectile_scale");
    public static readonly EveId ProjectileDamage = EveId.Parse("project_n:projectile_damage");
    public static readonly EveId ProjectileLifetime = EveId.Parse("project_n:projectile_lifetime");
    // Summon modifiers: ordinary tags read from the owner's source snapshot when the summon is created (Docs/SummonPlan.md).
    public static readonly EveId SummonDamage = EveId.Parse("project_n:summon_damage");
    public static readonly EveId SummonScale = EveId.Parse("project_n:summon_scale");
    public static readonly EveId NormalEnemy = EveId.Parse("project_n:normal");
    public static readonly EveId EliteEnemy = EveId.Parse("project_n:elite");
    public static readonly EveId BossEnemy = EveId.Parse("project_n:boss");
    public static readonly EveId Heal = EveId.Parse("project_n:heal");
    public static readonly EveId Health = EveId.Parse("project_n:health");
    // Enemy damage-sharing resource (see PNEnemyStat) and its two Buffs.
    public static readonly EveId Lorica = EveId.Parse("project_n:lorica");
    public static readonly EveId LoricaBreak = EveId.Parse("project_n:lorica_break");
    public static readonly EveId LoricaRecovery = EveId.Parse("project_n:lorica_recovery");
    public static readonly EveId AttackPower = EveId.Parse("project_n:attack_power");
    public static readonly EveId AttackSpeed = EveId.Parse("project_n:attack_speed");
    public static readonly EveId CooldownReduction = EveId.Parse("project_n:cooldown_reduction");
    public static readonly EveId CritRate = EveId.Parse("project_n:crit_rate");
    public static readonly EveId CritDamage = EveId.Parse("project_n:crit_damage");
    public static readonly EveId Defense = EveId.Parse("project_n:defense");
    public static readonly EveId Physical = EveId.Parse("project_n:physical");
    public static readonly EveId Magic = EveId.Parse("project_n:magic");
    public static readonly EveId True = EveId.Parse("project_n:true");
    public static readonly EveId Fire = EveId.Parse("project_n:fire");
    public static readonly EveId Ice = EveId.Parse("project_n:ice");
    public static readonly EveId Lightning = EveId.Parse("project_n:lightning");
    public static readonly EveId Scale = EveId.Parse("project_n:scale");
    public static readonly EveId Mitigation = EveId.Parse("project_n:mitigation");
    public static readonly EveId Penetration = EveId.Parse("project_n:penetration");
    // Targeting weight in world meters (may be negative): Aggro rules subtract it from the distance, Autotarget ranks higher aggro first.
    public static readonly EveId Aggro = EveId.Parse("project_n:aggro");
}
