namespace EveEngine
{
    // EN: Extension accessors keep Core contracts in a separate assembly from the engine hub.
    // VI: Accessor mở rộng giữ hợp đồng Core trong assembly riêng khỏi hub engine.
    public static class PNRegistryDefinitions
    {
        public static EveRegistry<PNSkillCategoryDefinition> SkillCategories(this EveRegistryHub hub) => hub.Get<PNSkillCategoryDefinition>();
        public static EveRegistry<PNBuffDefinition> Buffs(this EveRegistryHub hub) => hub.Get<PNBuffDefinition>();
        public static EveRegistry<PNBuffPresenterDefinition> BuffPresenters(this EveRegistryHub hub) => hub.Get<PNBuffPresenterDefinition>();
        public static EveRegistry<PNBuffTagDefinition> BuffTags(this EveRegistryHub hub) => hub.Get<PNBuffTagDefinition>();
        public static EveRegistry<PNControlPermissionDefinition> ControlPermissions(this EveRegistryHub hub) => hub.Get<PNControlPermissionDefinition>();
        public static EveRegistry<PNSkillActionDefinition> Actions(this EveRegistryHub hub) => hub.Get<PNSkillActionDefinition>();
        public static EveRegistry<PNSkillOperationDefinition> Operations(this EveRegistryHub hub) => hub.Get<PNSkillOperationDefinition>();
        public static EveRegistry<PNStatCalculationDefinition> Calculations(this EveRegistryHub hub) => hub.Get<PNStatCalculationDefinition>();
        public static EveRegistry<PNStatContributionDefinition> Contributions(this EveRegistryHub hub) => hub.Get<PNStatContributionDefinition>();
        public static EveRegistry<PNDamageTypeDefinition> DamageTypes(this EveRegistryHub hub) => hub.Get<PNDamageTypeDefinition>();
        public static EveRegistry<PNElementDefinition> Elements(this EveRegistryHub hub) => hub.Get<PNElementDefinition>();
        public static EveRegistry<PNTagDefinition> Tags(this EveRegistryHub hub) => hub.Get<PNTagDefinition>();
        public static EveRegistry<PNEnemyTypeDefinition> EnemyTypes(this EveRegistryHub hub) => hub.Get<PNEnemyTypeDefinition>();
    }
}
