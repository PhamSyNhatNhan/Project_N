using System;
using System.Collections.Generic;

namespace EveEngine
{
    // EN: Games and mods implement this to register content definitions.
    // VI: Game và mod triển khai interface này để đăng ký định nghĩa nội dung.
    public interface EveIContentModule
    {
        void Register(EveRegistryHub hub);
    }

    // EN: Trusted game modules publish JSON-backed concepts after documents have registered.
    // VI: Module game tin cậy công bố khái niệm từ JSON sau khi đăng ký tài liệu.
    public interface EveIContentFinalizeModule
    {
        void FinalizeContent(EveRegistryHub hub);
        void ValidateContent(EveRegistryHub hub);
    }

    // EN: Games validate their payload reference namespaces without teaching the engine their schema.
    // VI: Game kiểm tra namespace tham chiếu payload mà không đưa schema game vào engine.
    public interface EveIContentReferenceValidator
    {
        void ValidateReferences(EveId skillId, object data, string contentNamespace,
            IReadOnlyCollection<string> dependencies, EveRegistryHub hub);
    }

    // Games validate character metadata without adding game-specific fields to the engine.
    public interface EveICharacterContentReferenceValidator
    {
        void ValidateCharacterReferences(EveId characterId, object data, string contentNamespace,
            IReadOnlyCollection<string> dependencies, EveRegistryHub hub);
    }

    // EN: Registers code and JSON content into a new hub, then seals it.
    // VI: Nạp nội dung từ code và JSON vào hub mới, sau đó khóa hub.
    public sealed class EveContentBootstrap
    {
        private readonly EveJsonContentLoader jsonLoader = new EveJsonContentLoader();

        public EveRegistryHub Build(IEnumerable<EveIContentModule> modules, IEnumerable<EveContentDocument> documents,
            IEnumerable<EveOverrideSelection> selections = null)
        {
            if (modules == null) throw new ArgumentNullException(nameof(modules));
            if (documents == null) throw new ArgumentNullException(nameof(documents));

            var hub = new EveRegistryHub();
            hub.NameRegistry<EveStatDefinition>("stats");
            hub.NameRegistry<EveSkillDefinition>("skills");
            hub.NameRegistry<EveCharacterDefinition>("characters");
            var finalizers = new List<EveIContentFinalizeModule>();
            var validators = new List<EveIContentReferenceValidator>();
            var characterValidators = new List<EveICharacterContentReferenceValidator>();
            var extensions = new List<EveIContentExtension>();
            new EveBuiltInContentModule().Register(hub);
            foreach (EveIContentModule module in modules)
            {
                if (module == null) throw new ArgumentException("Module list contains null.", nameof(modules));
                module.Register(hub);
                if (module is EveIContentFinalizeModule finalizer) finalizers.Add(finalizer);
                if (module is EveIContentReferenceValidator validator) validators.Add(validator);
                if (module is EveICharacterContentReferenceValidator characterValidator) characterValidators.Add(characterValidator);
                if (module is EveIContentExtension extension) extensions.Add(extension);
            }
            jsonLoader.Register(documents, hub, validators, characterValidators, extensions);
            foreach (var finalizer in finalizers) finalizer.FinalizeContent(hub);
            foreach (var selection in selections ?? Array.Empty<EveOverrideSelection>())
            {
                if (selection == null) throw new ArgumentException("Null override selection.");
                hub.Bindings(selection.Registry).SelectOverride(selection.Id, selection.Provider);
            }
            hub.Seal();
            foreach (var character in hub.Characters.Snapshot().Values)
                foreach (var initial in character.Stats)
                {
                    var definition = hub.Stats.GetEffective(hub.Stats.ResolveIdentity(initial.Id));
                    if (definition.Kind != EveStatKind.Resource && initial.InitialCurrent.HasValue)
                        throw new InvalidOperationException($"Character {character.Id} has resource data for number stat {initial.Id}.");
                }
            foreach (var finalizer in finalizers) finalizer.ValidateContent(hub);
            return hub;
        }
    }
}
