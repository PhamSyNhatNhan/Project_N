using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace EveEngine
{
    // EN: Loads all skill payloads of an entity; games extend parsing and execution.
    // VI: Nạp toàn bộ dữ liệu skill của entity; game mở rộng phân tách và thực thi.
    public class EveSkill : MonoBehaviour, EveIEntityLoadStep
    {
        [SerializeField] private string characterId;
        private IReadOnlyDictionary<EveId, object> skills;
        private EveRegistry<EveSkillDefinition> definitions;
        private EveEntityLoadContext loadContext;
        public bool IsInitialized => skills != null;

        public virtual int LoadOrder => 0;
        public virtual IEnumerable<Type> Dependencies => Array.Empty<Type>();
        public virtual string CharacterDefinitionId => characterId;
        public virtual bool IsReady => IsInitialized && (loadContext == null || loadContext.CanUse);
        public virtual IReadOnlyDictionary<EveId, object> Skills => IsInitialized && (loadContext == null || loadContext.CanUse || loadContext.AllowsModuleAccess) ? skills : throw new InvalidOperationException("EveSkill is not published.");

        // EN: Spawn identity takes priority over the Inspector fallback.
        // VI: Danh tính spawn được ưu tiên hơn cấu hình dự phòng trong Inspector.
        public virtual void Load(EveEntityLoadContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            loadContext = context;
            LoadDefinition(context.Registry, context.Identity.CharacterId.IsValid
                ? context.Identity.CharacterId : EveId.Parse(CharacterDefinitionId));
        }

        public virtual void Load(EveRegistryHub registry) => LoadDefinition(registry, EveId.Parse(CharacterDefinitionId));

        protected virtual void LoadDefinition(EveRegistryHub registry, EveId id)
        {
            if (IsInitialized) return;
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (!registry.IsSealed) throw new InvalidOperationException("Content registry must be sealed before loading skills.");
            Initialize(registry.Characters.Get(id), registry.Skills);
        }

        public virtual void Initialize(EveCharacterDefinition character, EveRegistry<EveSkillDefinition> definitions)
        {
            if (IsInitialized) throw new InvalidOperationException("EveSkill is already loaded.");
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var loaded = new Dictionary<EveId, object>();
            this.definitions = definitions;
            foreach (EveId id in character.SkillIds)
            {
                EveId identity = definitions.ResolveIdentity(id);
                object data = LoadSkillData(identity, definitions.GetEffective(identity).Data);
                loaded.Add(identity, data);
            }
            skills = new ReadOnlyDictionary<EveId, object>(loaded);
        }

        // EN: Override to validate/convert game data. Null input/output is allowed; throw on invalid data.
        // VI: Override để kiểm tra/chuyển dữ liệu game. Cho phép đầu vào/đầu ra null; báo lỗi khi sai.
        protected virtual object LoadSkillData(EveId skillId, object data) => data;

        public virtual object GetSkillData(EveId skillId) => Skills[definitions?.ResolveIdentity(skillId) ?? skillId];
        public virtual bool TryGetSkillData(EveId skillId, out object data) => Skills.TryGetValue(definitions?.ResolveIdentity(skillId) ?? skillId, out data);

        // EN: Derived classes must also release their state, including partial failed loads.
        // VI: Lớp kế thừa phải dọn cả trạng thái riêng, kể cả khi nạp lỗi giữa chừng.
        public virtual void Unload() { skills = null; definitions = null; loadContext = null; }
    }
}
