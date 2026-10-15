namespace EveEngine
{
    // EN: Named access to built-in definition registries; custom content still uses Get<T>().
    // VI: Truy cập registry định nghĩa có sẵn bằng tên; nội dung riêng vẫn dùng Get<T>().
    public sealed partial class EveRegistryHub
    {
        public EveRegistry<EveStatDefinition> Stats => Get<EveStatDefinition>();
        public EveRegistry<EveSkillDefinition> Skills => Get<EveSkillDefinition>();
        public EveRegistry<EveCharacterDefinition> Characters => Get<EveCharacterDefinition>();
    }
}
