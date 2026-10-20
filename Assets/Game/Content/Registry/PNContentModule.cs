using EveEngine;

// EN: Registers definitions owned by Project_N; add further game definitions here.
// VI: Đăng ký định nghĩa thuộc Project_N; thêm các định nghĩa của game tại đây.
public class PNContentModule : EveIContentModule, EveIContentFinalizeModule, EveICharacterContentReferenceValidator
{
    public static readonly EveId Health = EveId.Parse("project_n:health");
    public static readonly EveId AttackPower = EveId.Parse("project_n:attack_power");
    public static readonly EveId Defense = EveId.Parse("project_n:defense")
        ;
    public static readonly EveId Physical = EveId.Parse("project_n:physical");
    public static readonly EveId Magic = EveId.Parse("project_n:magic");
    public static readonly EveId True = EveId.Parse("project_n:true");
    
    public static readonly EveId Fire = EveId.Parse("project_n:fire");
    public static readonly EveId Ice = EveId.Parse("project_n:ice");
    public static readonly EveId Lightning = EveId.Parse("project_n:lightning");
    public static readonly EveId Scale = EveId.Parse("project_n:scale");
    public static readonly EveId Mitigation = EveId.Parse("project_n:mitigation");

    public virtual void Register(EveRegistryHub hub)
    {
        PNSkillBindingContent.RegisterRegistries(hub);
        hub.GetOrCreate<PNBuffDefinition>(); hub.NameRegistry<PNBuffDefinition>("buffs");
        hub.GetOrCreate<PNBuffPresenterDefinition>(); hub.NameRegistry<PNBuffPresenterDefinition>("buff_presenters");
        var buffTags = hub.GetOrCreate<PNBuffTagDefinition>(); hub.NameRegistry<PNBuffTagDefinition>("buff_tags");
        foreach (var name in new[] { "buff", "debuff", "neutral", "stun", "silence", "dot", "fire", "ice", "lightning", "vulnerability", "stat", "timescale" })
        {
            var id = EveId.Parse("project_n:" + name); buffTags.Register(new PNBuffTagDefinition(id));
            buffTags.RegisterBase(new EveId("base", name), id, "project_n");
        }
        var permissions = hub.GetOrCreate<PNControlPermissionDefinition>();
        foreach (var id in PNControlIds.All)
        {
            permissions.Register(new PNControlPermissionDefinition(id));
            permissions.RegisterBase(new EveId("base", id.Path), id, "project_n");
        }
        hub.NameRegistry<PNControlPermissionDefinition>("control_permissions");
        var categories = hub.GetOrCreate<PNSkillCategoryDefinition>(); hub.NameRegistry<PNSkillCategoryDefinition>("skill_categories");
        var categoryPermissions = new[] { PNControlIds.Attack, PNControlIds.Skill, PNControlIds.Ultimate, PNControlIds.Dash, PNControlIds.Burst };
        var categoryNames = new[] { "attack", "skill", "ultimate", "dash", "burst" };
        for (int i = 0; i < categoryNames.Length; i++)
        {
            var category = new PNSkillCategoryDefinition(EveId.Parse("project_n:" + categoryNames[i]), categoryPermissions[i],
                EveId.Parse("base:" + categoryNames[i]));
            categories.Register(category); categories.RegisterBase(new EveId("base", categoryNames[i]), category.Id, "project_n");
        }
        hub.GetOrCreate<PNSkillActionDefinition>();
        hub.GetOrCreate<PNSkillOperationDefinition>();
        var enemyTypes = hub.GetOrCreate<PNEnemyTypeDefinition>();
        enemyTypes.Register(new PNEnemyTypeDefinition(PNContentIds.NormalEnemy, "Normal"));
        enemyTypes.Register(new PNEnemyTypeDefinition(PNContentIds.EliteEnemy, "Elite"));
        enemyTypes.Register(new PNEnemyTypeDefinition(PNContentIds.BossEnemy, "Boss"));
        foreach (var type in enemyTypes.Snapshot().Values)
            enemyTypes.RegisterBase(new EveId("base", type.Id.Path), type.Id, "project_n");
        hub.NameRegistry<PNEnemyTypeDefinition>("enemy_types");
        var stats = hub.GetOrCreate<EveStatDefinition>();
        stats.Register(new EveStatDefinition(Health, EveStatKind.Resource, EveMaxChangePolicy.PreserveRatio, EveStatReplication.Everyone));
        stats.Register(new EveStatDefinition(PNContentIds.Lorica, EveStatKind.Resource, EveMaxChangePolicy.PreserveRatio, EveStatReplication.Everyone));
        stats.Register(new EveStatDefinition(AttackPower, EveStatKind.Number));
        stats.Register(new EveStatDefinition(PNContentIds.AttackSpeed, EveStatKind.Number));
        stats.Register(new EveStatDefinition(PNContentIds.CooldownReduction, EveStatKind.Number));
        stats.Register(new EveStatDefinition(PNContentIds.CritRate, EveStatKind.Number));
        stats.Register(new EveStatDefinition(PNContentIds.CritDamage, EveStatKind.Number));
        stats.Register(new EveStatDefinition(Defense, EveStatKind.Number));

        var damageTypes = hub.GetOrCreate<PNDamageTypeDefinition>();
        damageTypes.Register(new PNDamageTypeDefinition(Physical, 1f));
        damageTypes.Register(new PNDamageTypeDefinition(Magic, 0.5f));
        damageTypes.Register(new PNDamageTypeDefinition(True, 0f));

        var elements = hub.GetOrCreate<PNElementDefinition>();
        elements.Register(new PNElementDefinition(Fire));
        elements.Register(new PNElementDefinition(Ice));
        elements.Register(new PNElementDefinition(Lightning));

        var tags = hub.GetOrCreate<PNTagDefinition>();
        foreach (var id in new[] { Physical, Magic, True, Fire, Ice, Lightning, PNContentIds.Heal })
            tags.Register(new PNTagDefinition(id));
        foreach (var id in new[] { PNContentIds.AttackTag, PNContentIds.SkillTag, PNContentIds.UltimateTag, PNContentIds.DashTag, PNContentIds.BurstTag })
            tags.Register(new PNTagDefinition(id));
        foreach (var id in new[] { PNContentIds.ProjectileSpeed, PNContentIds.ProjectileScale, PNContentIds.ProjectileDamage, PNContentIds.ProjectileLifetime })
            tags.Register(new PNTagDefinition(id));

        var calculations = hub.GetOrCreate<PNStatCalculationDefinition>();
        var contributions = hub.GetOrCreate<PNStatContributionDefinition>();
        contributions.Register(new PNStatContributionDefinition(PNContentIds.Before));
        contributions.Register(new PNStatContributionDefinition(PNContentIds.After));
        foreach (var definition in contributions.Snapshot().Values)
            contributions.RegisterBase(new EveId("base", definition.Id.Path), definition.Id, "project_n");
        hub.NameRegistry<PNStatContributionDefinition>("contributions");
        calculations.Register(new PNStatCalculationDefinition(Scale, typeof(PNScaleCalculation)));
        calculations.Register(new PNStatCalculationDefinition(Mitigation, typeof(PNMitigationCalculation)));

        // EN: Project_N owns the public vocabulary; the engine remains namespace-independent.
        // VI: Project_N sở hữu bộ tên công khai; engine không phụ thuộc namespace game.
        foreach (var definition in stats.Snapshot().Values)
            if (definition.Id.Namespace == "eve" || definition.Id.Namespace == "project_n")
                stats.RegisterBase(new EveId("base", definition.Id.Path), definition.Id, "project_n",
                    compatible: (original, replacement) => original.Kind == replacement.Kind);
        foreach (var definition in damageTypes.Snapshot().Values)
            damageTypes.RegisterBase(new EveId("base", definition.Id.Path), definition.Id, "project_n",
                compatible: (original, replacement) => true);
        foreach (var definition in elements.Snapshot().Values)
            elements.RegisterBase(new EveId("base", definition.Id.Path), definition.Id, "project_n");
        foreach (var definition in tags.Snapshot().Values)
            tags.RegisterBase(new EveId("base", definition.Id.Path), definition.Id, "project_n");
        foreach (var definition in calculations.Snapshot().Values)
            calculations.RegisterBase(new EveId("base", definition.Id.Path), definition.Id, "project_n",
                compatible: (original, replacement) => true);
        hub.NameRegistry<PNSkillActionDefinition>("actions");
        hub.NameRegistry<PNSkillOperationDefinition>("operations");
        hub.NameRegistry<PNStatCalculationDefinition>("calculations");
        hub.NameRegistry<PNDamageTypeDefinition>("damage_types");
        hub.NameRegistry<PNElementDefinition>("elements");
        hub.NameRegistry<PNTagDefinition>("tags");
    }

    public virtual void FinalizeContent(EveRegistryHub hub) { }

    public virtual void ValidateContent(EveRegistryHub hub)
    {
        // Covers code-registered character definitions as well as JSON.
        foreach (var character in hub.Characters.Snapshot().Values)
        {
            PNEnemyContent.ResolveType(character.Data, hub);
            PNLoricaSettings.Parse(character.Data);
        }
    }

    public virtual void ValidateCharacterReferences(EveId characterId, object data, string contentNamespace,
        System.Collections.Generic.IReadOnlyCollection<string> dependencies, EveRegistryHub hub)
    {
        var type = PNEnemyContent.ReadTypeId(data);
        var registry = hub.EnemyTypes();
        registry.Get(type);
        // Omission uses the game's compatibility default, not an authored external reference.
        if (!(data is Newtonsoft.Json.Linq.JObject obj) || obj["enemyTypeId"] == null) return;
        string owner = type.Namespace == "base" ? registry.BindingOwner(type) : type.Namespace;
        if (contentNamespace == null || dependencies == null || owner == contentNamespace ||
            System.Linq.Enumerable.Contains(dependencies, owner)) return;
        throw new System.FormatException($"Character {characterId} enemy type {type} requires dependency on {owner}.");
    }
}
