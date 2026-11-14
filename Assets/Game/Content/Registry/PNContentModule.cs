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
    public static readonly EveId Penetration = EveId.Parse("project_n:penetration");

    public virtual void Register(EveRegistryHub hub)
    {
        PNSkillBindingContent.RegisterRegistries(hub);
        hub.GetOrCreate<PNBuffDefinition>(); hub.NameRegistry<PNBuffDefinition>("buffs");
        hub.GetOrCreate<PNBuffPresenterDefinition>(); hub.NameRegistry<PNBuffPresenterDefinition>("buff_presenters");
        var buffTags = hub.GetOrCreate<PNBuffTagDefinition>(); hub.NameRegistry<PNBuffTagDefinition>("buff_tags");
        foreach (var name in new[] { "buff", "debuff", "neutral", "stun", "silence", "dot", "fire", "ice", "lightning", "vulnerability", "stat", "timescale", "sidus" })
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
        // Targeting weight in world meters, signed (see PNTargetRule).
        stats.Register(new EveStatDefinition(PNContentIds.Aggro, EveStatKind.Number));

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
        foreach (var id in new[] { PNContentIds.ProjectileSpeed, PNContentIds.ProjectileScale, PNContentIds.ProjectileDamage, PNContentIds.ProjectileLifetime, PNContentIds.SummonDamage, PNContentIds.SummonScale, PNContentIds.AreaScale, PNContentIds.AreaDamage, PNContentIds.MeleeScale, PNContentIds.MeleeDamage, PNContentIds.BlastScale, PNContentIds.BlastDamage })
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
        calculations.Register(new PNStatCalculationDefinition(Penetration, typeof(PNPenetrationCalculation)));

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
        hub.GetOrCreate<PNVisualRigDefinition>(); hub.NameRegistry<PNVisualRigDefinition>("visual_rigs");
        hub.GetOrCreate<PNVisualPartDefinition>(); hub.NameRegistry<PNVisualPartDefinition>("visual_parts");
        hub.GetOrCreate<PNVisualBuildDefinition>(); hub.NameRegistry<PNVisualBuildDefinition>("visual_builds");
        hub.GetOrCreate<PNVisualMotionDefinition>(); hub.NameRegistry<PNVisualMotionDefinition>("visual_motions");
        hub.GetOrCreate<PNVisualMotionSetDefinition>(); hub.NameRegistry<PNVisualMotionSetDefinition>("visual_motion_sets");
        hub.GetOrCreate<PNVisualExpressionDefinition>(); hub.NameRegistry<PNVisualExpressionDefinition>("visual_expressions");
        hub.GetOrCreate<PNVisualAppearanceDefinition>(); hub.NameRegistry<PNVisualAppearanceDefinition>("visual_appearances");
        hub.GetOrCreate<PNVisualStringShapeDefinition>(); hub.NameRegistry<PNVisualStringShapeDefinition>("visual_string_shapes");
        hub.GetOrCreate<PNVisualStringColorDefinition>(); hub.NameRegistry<PNVisualStringColorDefinition>("visual_string_colors");
        hub.GetOrCreate<PNVisualStringStyleDefinition>(); hub.NameRegistry<PNVisualStringStyleDefinition>("visual_string_styles");
        hub.GetOrCreate<PNSyncStateDefinition>(); hub.GetOrCreate<PNSyncCommandDefinition>();
        hub.GetOrCreate<PNRunDefinition>(); hub.NameRegistry<PNRunDefinition>("roguelite_runs");
        hub.GetOrCreate<PNFloorDefinition>(); hub.NameRegistry<PNFloorDefinition>("roguelite_floors");
        hub.GetOrCreate<PNRoomDefinition>(); hub.NameRegistry<PNRoomDefinition>("roguelite_rooms");
        hub.GetOrCreate<PNRogueConstellationDefinition>(); hub.NameRegistry<PNRogueConstellationDefinition>("rogue_constellations");
        hub.GetOrCreate<PNRogueSparkDefinition>(); hub.NameRegistry<PNRogueSparkDefinition>("rogue_sparks");
        hub.GetOrCreate<PNRogueLodestarDefinition>(); hub.NameRegistry<PNRogueLodestarDefinition>("rogue_lodestars");
        hub.GetOrCreate<PNRogueFactoryDefinition>(); hub.NameRegistry<PNRogueFactoryDefinition>("rogue_factories");
        hub.GetOrCreate<PNWorldFeatureDefinition>(); hub.NameRegistry<PNWorldFeatureDefinition>("world_features");
        hub.GetOrCreate<PNEntityModuleDefinition>(); hub.NameRegistry<PNEntityModuleDefinition>("entity_modules");
        // The world feature a run with Roguelite Buff gives its rooms (Docs/EntityModulePlan.md): the vocabulary lives here, the module that answers to it in the gameplay module.
        hub.WorldFeatures().Register(new PNWorldFeatureDefinition(PNRunFeatures.RogueBuff, "Worlds of a run that has Roguelite Buff."));
        // Rooms of a run count their enemies for the clear and let them drop loot (G6); the sandbox declares neither.
        hub.WorldFeatures().Register(new PNWorldFeatureDefinition(PNRunFeatures.RoomClear, "Worlds whose enemies count for a room clear."));
        hub.WorldFeatures().Register(new PNWorldFeatureDefinition(PNRunFeatures.Loot, "Worlds whose enemies drop loot."));
        // Grid worlds (Docs/SandboxPlan.md G1): the feature they all have and the ground types they can be painted with (colour is the placeholder look).
        hub.WorldFeatures().Register(new PNWorldFeatureDefinition(PNGridIds.GridWorldFeature, "Worlds built from grid cell data."));
        // Worlds whose enemies are removed when no player is near for a while (Docs/SandboxPlan.md G4); a roguelite room does not have it.
        hub.WorldFeatures().Register(new PNWorldFeatureDefinition(PNDespawnIds.Feature, "Worlds that remove enemies far from every living player."));
        // Worlds that bring monsters to the players on their own (Docs/SandboxPlan.md G9); the service stays idle until a mode configures it.
        hub.WorldFeatures().Register(new PNWorldFeatureDefinition(PNSpawnerIds.Feature, "Worlds whose monsters are brought to the players by the spawner."));
        hub.WorldFeatures().Register(new PNWorldFeatureDefinition(PNGridSaveIds.Feature, "Grid worlds that save their ground, placed things and player places, and can be restored from that save."));
        // Terrains and locomotions (Docs/SandboxPlan.md G12): what a ground makes, and what a body can stand on. A mod registers more of both; at most PNTerrainLayers.LayerCount terrains besides land.
        hub.GetOrCreate<PNTerrainDefinition>(); hub.NameRegistry<PNTerrainDefinition>("terrains");
        hub.Terrains().Register(new PNTerrainDefinition(PNTerrainIds.Land, "Ground one can walk on."));
        hub.Terrains().Register(new PNTerrainDefinition(PNTerrainIds.Void, "Nothing under the cell: a hole, or the outside of a map."));
        hub.Terrains().Register(new PNTerrainDefinition(PNTerrainIds.Rock, "A floor that is drawn but cannot be walked on."));
        hub.GetOrCreate<PNLocomotionDefinition>(); hub.NameRegistry<PNLocomotionDefinition>("locomotions");
        hub.Locomotions().Register(new PNLocomotionDefinition(PNTerrainIds.Ground, new[] { PNTerrainIds.Land }, 0, "Walking: on land only (the default)."));
        hub.Locomotions().Register(new PNLocomotionDefinition(PNTerrainIds.Air, new[] { PNTerrainIds.Land, PNTerrainIds.Void, PNTerrainIds.Rock }, 10, "Flying: over land, holes and rock alike."));
        hub.GetOrCreate<PNGroundDefinition>(); hub.NameRegistry<PNGroundDefinition>("grounds");
        hub.Grounds().Register(new PNGroundDefinition(PNGridIds.Grass, PNTerrainIds.Land, 112, 160, 96, "Grass."));
        hub.Grounds().Register(new PNGroundDefinition(PNGridIds.Sand, PNTerrainIds.Land, 214, 196, 138, "Sand."));
        hub.Grounds().Register(new PNGroundDefinition(PNGridIds.RoomFloor, PNTerrainIds.Land, 92, 77, 128, "Floor of the roguelite room maps."));
        hub.Grounds().Register(new PNGroundDefinition(PNGridIds.Stone, PNTerrainIds.Rock, 120, 120, 128, "Bare stone that cannot be walked on."));
        hub.Grounds().Register(new PNGroundDefinition(PNGridIds.Void, PNTerrainIds.Void, 30, 32, 40, "Nothing: no floor, cannot be walked on.", false));
        // Work types of placeable things (Docs/SandboxPlan.md): the vocabulary a work packet and a placeable's `work.types` share. Named after the tool, like Terraria; a mod registers more.
        hub.GetOrCreate<PNWorkTypeDefinition>(); hub.NameRegistry<PNWorkTypeDefinition>("work_types");
        hub.WorkTypes().Register(new PNWorkTypeDefinition(PNWorkIds.Pickaxe, "Digs and breaks stone and ore."));
        hub.WorkTypes().Register(new PNWorkTypeDefinition(PNWorkIds.Axe, "Chops wood."));
    }

    public virtual void FinalizeContent(EveRegistryHub hub) { }

    public virtual void ValidateContent(EveRegistryHub hub)
    {
        PNTerrainLayers.Validate(hub);
        // Covers code-registered character definitions as well as JSON.
        foreach (var character in hub.Characters.Snapshot().Values)
        {
            PNEnemyContent.ResolveType(character.Data, hub);
            PNLoricaSettings.Parse(character.Data);
            PNPlayableSettings.Parse(character.Data);
            PNCombatSettings.Parse(character.Data);
            var body = PNBodySettings.Read(character);
            if (!hub.Get<PNLocomotionDefinition>().TryGet(body.Locomotion, out _)) throw new System.FormatException("Character " + character.Id + ": body locomotion " + body.Locomotion + " is not registered.");
            if (body.Visual.HasValue)
            {
                var appearance = hub.Get<PNVisualAppearanceDefinition>();
                if (!appearance.TryGet(body.Visual.Value, out _)) throw new System.FormatException("Character " + character.Id + ": body visual " + body.Visual.Value + " is not a registered appearance.");
                if (body.BuildOverride.HasValue && !hub.Get<PNVisualBuildDefinition>().TryGet(body.BuildOverride.Value, out _)) throw new System.FormatException("Character " + character.Id + ": build " + body.Build + " is not registered.");
            }
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
