using System;
using System.Collections.Generic;
using UnityEngine;

namespace EveEngine
{
    // EN: Inheritable stat component attached directly to an entity GameObject.
    // VI: Component chỉ số cho entity, có thể kế thừa và gắn trực tiếp lên GameObject.
    public class EveStat : MonoBehaviour, EveIEntityLoadStep
    {
        [SerializeField] private string characterId;
        private EveStatSet set;
        protected EveEntityLoadContext loadContext;
        public bool IsInitialized => set != null;
        public virtual int LoadOrder => 0;
        public virtual IEnumerable<Type> Dependencies => Array.Empty<Type>();
        public virtual string CharacterDefinitionId => characterId;
        public virtual bool IsReady => IsInitialized && (loadContext == null || loadContext.CanUse);
        public virtual EveStatSet Set => IsInitialized && (loadContext == null || loadContext.CanUse || loadContext.AllowsModuleAccess) ? set : throw new InvalidOperationException("EveStat is not published.");

        // EN: Resolve this entity's configured definition after the registry is sealed.
        // VI: Tra định nghĩa được cấu hình của entity sau khi registry đã khóa.
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
            if (!registry.IsSealed) throw new InvalidOperationException("Content registry must be sealed before loading stats.");

            EveCharacterDefinition character = registry.Characters.Get(id);
            Initialize(character, registry.Stats);
        }

        // EN: Discard runtime values after load failure or session end.
        // VI: Xóa giá trị runtime sau lỗi nạp hoặc khi kết thúc phiên.
        public virtual void Unload() { set = null; loadContext = null; }

        // EN: Call after content registration; Unity components are not constructed with new.
        // VI: Gọi sau khi đăng ký nội dung; không tạo component Unity bằng new.
        public virtual void Initialize(EveCharacterDefinition character, EveRegistry<EveStatDefinition> definitions)
        {
            if (IsInitialized) throw new InvalidOperationException("EveStat is already initialized.");
            Initialize(EveStatSet.FromCharacter(character, definitions));
        }

        // EN: Derived games can supply a custom set with their own stat formulas.
        // VI: Game kế thừa có thể đưa vào stat set riêng với công thức tùy chỉnh.
        public virtual void Initialize(EveStatSet statSet)
        {
            if (IsInitialized) throw new InvalidOperationException("EveStat is already initialized.");
            set = statSet ?? throw new ArgumentNullException(nameof(statSet));
        }

        public virtual double Evaluate(params EveId[] ids) => Set.Evaluate(ids);
        protected void RequireStatMutation()
        { if (loadContext != null && !loadContext.CanMutate && !loadContext.AllowsModuleAccess) throw new InvalidOperationException("Stat mutation requires published authority or loader initialization/cleanup scope."); }
        public virtual void SetSource(EveId sourceId, IEnumerable<EveStatModifier> modifiers) { RequireStatMutation(); Set.SetSource(sourceId, modifiers); }
        public virtual bool RemoveSource(EveId sourceId) { RequireStatMutation(); return Set.RemoveSource(sourceId); }
        public virtual double GetMax(EveId id) => Set.GetMax(id);
        public virtual double GetCurrent(EveId id) => Set.GetCurrent(id);
        public virtual void SetCurrent(EveId id, double value) { RequireStatMutation(); Set.SetCurrent(id, value); }
    }
}
