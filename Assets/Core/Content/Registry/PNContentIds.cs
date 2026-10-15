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
    public static readonly EveId NormalEnemy = EveId.Parse("project_n:normal");
    public static readonly EveId EliteEnemy = EveId.Parse("project_n:elite");
    public static readonly EveId BossEnemy = EveId.Parse("project_n:boss");
    public static readonly EveId Heal = EveId.Parse("project_n:heal");
    public static readonly EveId Health = EveId.Parse("project_n:health");
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
}
