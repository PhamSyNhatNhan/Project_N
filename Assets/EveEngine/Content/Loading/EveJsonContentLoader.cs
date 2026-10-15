using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EveEngine
{
    // EN: The caller supplies documents from project assets, mods or tests.
    // VI: Bên gọi cung cấp tài liệu từ asset của project, mod hoặc kiểm thử.
    // EN: This loader never reads Resources or the filesystem itself.
    // VI: Bộ nạp này không tự đọc Resources hay hệ thống tệp.
    // EN: Validates JSON envelopes and registers stats, characters and flexible skill payloads.
    // VI: Kiểm tra cấu trúc JSON và đăng ký chỉ số, nhân vật cùng dữ liệu skill linh hoạt.
    public sealed class EveJsonContentLoader
    {
        // EN: Root shape of one JSON content document.
        // VI: Cấu trúc gốc của một tài liệu nội dung JSON.
        private sealed class EveContentDto
        {
            public int schemaVersion { get; set; }
            public List<EveStatDefinitionDto> statDefinitions { get; set; }
            public List<EveCharacterDto> characters { get; set; }
            public List<EveOverrideDto> overrides { get; set; }
            public JObject extensions { get; set; }
        }

        private sealed class EveOverrideDto
        {
            public string registry { get; set; }
            public string id { get; set; }
            public string implementationId { get; set; }
            public string version { get; set; }
        }

        // EN: JSON fields describing one stat type.
        // VI: Các trường JSON mô tả một loại chỉ số.
        private sealed class EveStatDefinitionDto
        {
            public string id { get; set; }
            public string kind { get; set; }
            public string maxChangePolicy { get; set; }
            public string replication { get; set; }
        }

        // EN: JSON fields describing a character's starting stats.
        // VI: Các trường JSON mô tả chỉ số khởi đầu của nhân vật.
        private sealed class EveCharacterDto
        {
            public JToken data { get; set; }
            public JToken portraitPath { get; set; }
            public string id { get; set; }
            public List<EveInitialStatDto> stats { get; set; }
            public List<EveSkillDto> skill { get; set; } = new List<EveSkillDto>();
        }

        // EN: Only identity and the payload envelope belong to the engine schema.
        // VI: Schema engine chỉ quy định danh tính và phần bao ngoài dữ liệu.
        private sealed class EveSkillDto
        {
            public string id { get; set; }
            public JToken data { get; set; }
        }

        // EN: JSON fields for one starting stat value.
        // VI: Các trường JSON cho một giá trị chỉ số khởi đầu.
        private sealed class EveInitialStatDto
        {
            // Legacy schema 1 reference. Prefer statId in new content.
            public string id { get; set; }
            public string statId { get; set; }
            [JsonProperty("base")] public double? baseValue { get; set; }
            public double? initialCurrent { get; set; }
        }

        public void Register(IEnumerable<EveContentDocument> documents, EveRegistryHub hub,
            IEnumerable<EveIContentReferenceValidator> referenceValidators = null)
            => Register(documents, hub, referenceValidators, null);

        public void Register(IEnumerable<EveContentDocument> documents, EveRegistryHub hub,
            IEnumerable<EveIContentReferenceValidator> referenceValidators,
            IEnumerable<EveICharacterContentReferenceValidator> characterReferenceValidators)
            => Register(documents, hub, referenceValidators, characterReferenceValidators, null);

        public void Register(IEnumerable<EveContentDocument> documents, EveRegistryHub hub,
            IEnumerable<EveIContentReferenceValidator> referenceValidators,
            IEnumerable<EveICharacterContentReferenceValidator> characterReferenceValidators,
            IEnumerable<EveIContentExtension> extensions)
        {
            if (documents == null) throw new ArgumentNullException(nameof(documents));
            if (hub == null) throw new ArgumentNullException(nameof(hub));
            if (hub.IsSealed) throw new InvalidOperationException("Content cannot be registered after the registry hub is sealed.");

            var statRegistry = hub.GetOrCreate<EveStatDefinition>();
            var characterRegistry = hub.GetOrCreate<EveCharacterDefinition>();
            var skillRegistry = hub.GetOrCreate<EveSkillDefinition>();
            if (statRegistry.IsSealed || characterRegistry.IsSealed || skillRegistry.IsSealed)
                throw new InvalidOperationException("Content registries are sealed.");
            var parsed = new List<(string name, string contentNamespace, IReadOnlyCollection<string> dependencies, EveContentDto dto)>();
            var pendingStats = new Dictionary<EveId, EveStatDefinition>();
            var pendingCharacters = new Dictionary<EveId, EveCharacterDefinition>();
            var pendingSkills = new Dictionary<EveId, EveSkillDefinition>();
            var validators = (referenceValidators ?? Array.Empty<EveIContentReferenceValidator>()).ToArray();
            var characterValidators = (characterReferenceValidators ?? Array.Empty<EveICharacterContentReferenceValidator>()).ToArray();

            foreach (EveContentDocument document in documents)
            {
                if (document == null) throw new ArgumentException("Document list contains null.", nameof(documents));
                if (document.IsMetadata) continue;
                try
                {
                    EveContentDto dto;
                    using (var reader = new JsonTextReader(new StringReader(document.Json)) { DateParseHandling = DateParseHandling.None })
                    {
                        JToken json = JToken.Load(reader, new JsonLoadSettings
                        {
                            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                        });
                        if (reader.Read()) throw new FormatException("Unexpected content after JSON document.");
                        dto = json.ToObject<EveContentDto>(JsonSerializer.Create(new JsonSerializerSettings
                        {
                            MissingMemberHandling = MissingMemberHandling.Error
                        }));
                    }
                    if (dto == null || dto.schemaVersion != 1)
                        throw new FormatException("Expected schemaVersion 1.");
                    parsed.Add((document.Name, document.ContentNamespace, document.Dependencies, dto));
                }
                catch (Exception ex) when (ex is JsonException || ex is FormatException)
                {
                    throw new FormatException($"Invalid content document '{document.Name}': {ex.Message}", ex);
                }
            }

            // EN: Gather stat definitions before validating character references.
            // VI: Thu thập định nghĩa chỉ số trước khi kiểm tra tham chiếu của nhân vật.
            foreach (var document in parsed)
                foreach (EveStatDefinitionDto dto in document.dto.statDefinitions ?? new List<EveStatDefinitionDto>())
                {
                    try
                    {
                        if (dto == null) throw new FormatException("Null stat definition.");
                        EveId id = EveId.Parse(dto.id);
                        ValidateNamespace(id, document.contentNamespace);
                        EveStatKind kind = dto.kind == "resource" ? EveStatKind.Resource
                            : dto.kind == "number" ? EveStatKind.Number
                            : throw new FormatException($"Unknown stat kind '{dto.kind}'.");
                        EveMaxChangePolicy policy = dto.maxChangePolicy == null || dto.maxChangePolicy == "preserveCurrent"
                            ? EveMaxChangePolicy.PreserveCurrent
                            : dto.maxChangePolicy == "preserveRatio" ? EveMaxChangePolicy.PreserveRatio
                            : throw new FormatException($"Unknown maxChangePolicy '{dto.maxChangePolicy}'.");
                        if (kind != EveStatKind.Resource && dto.maxChangePolicy != null)
                            throw new FormatException("maxChangePolicy is only valid for resources.");
                        if (statRegistry.TryGet(id, out _) || pendingStats.ContainsKey(id))
                            throw new FormatException($"Duplicate stat ID {id}.");
                        var replication = dto.replication == null || dto.replication == "none" ? EveStatReplication.None
                            : dto.replication == "everyone" ? EveStatReplication.Everyone
                            : dto.replication == "owner" ? EveStatReplication.Owner
                            : throw new FormatException("Unknown Stat replication policy.");
                        pendingStats.Add(id, new EveStatDefinition(id, kind, policy, replication));
                    }
                    catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
                    {
                        throw new FormatException($"Stat definition in '{document.name}': {ex.Message}", ex);
                    }
                }

            foreach (var document in parsed)
                foreach (EveCharacterDto dto in document.dto.characters ?? new List<EveCharacterDto>())
                {
                    try
                    {
                        if (dto == null) throw new FormatException("Null character definition.");
                        EveId id = EveId.Parse(dto.id);
                        ValidateNamespace(id, document.contentNamespace);
                        var stats = new List<EveInitialStat>();
                        var used = new HashSet<EveId>();
                        foreach (EveInitialStatDto entry in dto.stats ?? new List<EveInitialStatDto>())
                        {
                            if (entry == null || !entry.baseValue.HasValue) throw new FormatException($"Character {id} has a stat without base value.");
                            if ((entry.id == null) == (entry.statId == null))
                                throw new FormatException($"Character {id} stat must specify exactly one statId (legacy id is accepted).");
                            EveId statId = EveId.Parse(entry.statId ?? entry.id);
                            ValidateReferenceNamespace(statId, document.contentNamespace, document.dependencies, statRegistry);
                            statId = statRegistry.ResolveIdentity(statId);
                            if (!used.Add(statId)) throw new FormatException($"Character {id} has duplicate stat {statId}.");
                            if (!pendingStats.TryGetValue(statId, out EveStatDefinition definition)
                                && !statRegistry.TryGet(statId, out definition))
                                throw new FormatException($"Character {id} references unknown stat {statId}.");
                            if (definition.Kind != EveStatKind.Resource && entry.initialCurrent.HasValue)
                                throw new FormatException($"Non-resource stat {statId} cannot have initialCurrent.");
                            if (definition.Kind == EveStatKind.Resource && entry.baseValue.Value < 0d)
                                throw new FormatException($"Resource {statId} has a negative base maximum.");
                            if (entry.initialCurrent.HasValue && entry.initialCurrent.Value > entry.baseValue.Value)
                                throw new FormatException($"Resource {statId} initialCurrent exceeds base maximum.");
                            stats.Add(new EveInitialStat(statId, entry.baseValue.Value, entry.initialCurrent));
                        }
                        if (characterRegistry.TryGet(id, out _) || pendingCharacters.ContainsKey(id))
                            throw new FormatException($"Duplicate character ID {id}.");
                        if (dto.skill == null) throw new FormatException($"Character {id} skill must be an array.");
                        var skillIds = new List<EveId>();
                        foreach (EveSkillDto entry in dto.skill)
                        {
                            if (entry == null)
                                throw new FormatException($"Character {id} has a null skill entry.");
                            EveId skillId = EveId.Parse(entry.id);
                            ValidateNamespace(skillId, document.contentNamespace);
                            if (skillRegistry.TryGet(skillId, out _) || pendingSkills.ContainsKey(skillId))
                                throw new FormatException($"Duplicate skill ID {skillId}.");
                            foreach (var validator in validators)
                                validator.ValidateReferences(skillId, entry.data, document.contentNamespace, document.dependencies, hub);
                            pendingSkills.Add(skillId, new EveSkillDefinition(skillId, entry.data));
                            skillIds.Add(skillId);
                        }
                        if(dto.portraitPath != null && dto.portraitPath.Type != JTokenType.String)
                            throw new FormatException("portraitPath must be a Resources path string.");
                        foreach (var validator in characterValidators)
                            validator.ValidateCharacterReferences(id, dto.data, document.contentNamespace, document.dependencies, hub);
                        pendingCharacters.Add(id, new EveCharacterDefinition(id, stats, skillIds, dto.portraitPath?.Value<string>(), dto.data));
                    }
                    catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
                    {
                        throw new FormatException($"Character definition in '{document.name}': {ex.Message}", ex);
                    }
                }

            var extensionDocuments = parsed.Where(x => x.dto.extensions != null).Select(x =>
                new EveContentExtensionDocument(x.name, x.contentNamespace, x.dependencies, x.dto.extensions)).ToArray();
            var processors = (extensions ?? Array.Empty<EveIContentExtension>()).ToArray();
            if (processors.GroupBy(x => x.ExtensionKey).Any(x => x.Count() != 1)) throw new FormatException("Duplicate content extension processor key.");
            foreach (var document in extensionDocuments)
                foreach (var property in document.Extensions.Properties())
                    if (!processors.Any(x => x.ExtensionKey == property.Name)) throw new FormatException("Unknown content extension " + property.Name);
            if (extensionDocuments.Length > 0 && processors.Length == 0) throw new FormatException("Content extensions require a registered schema processor.");
            var pendingBuiltins = pendingStats.Values.Cast<EveIIdentified>().Concat(pendingSkills.Values).Concat(pendingCharacters.Values).ToArray();
            var batches = processors.Select(x => x.PrepareExtensions(extensionDocuments, hub, pendingBuiltins)).ToArray();
            foreach (var batch in batches) batch.Validate();

            var requests = new List<(EveIBindingRegistry registry, EveId id, EveId implementation, string provider,
                string version, IReadOnlyCollection<string> dependencies)>();
            var requestKeys = new HashSet<string>(StringComparer.Ordinal);
            var pendingDefinitions = pendingStats.Values.Cast<EveIIdentified>().Concat(pendingSkills.Values)
                .Concat(pendingCharacters.Values).Concat(batches.SelectMany(x => x.Definitions)).ToArray();
            foreach (var document in parsed)
                foreach (var replacement in document.dto.overrides ?? new List<EveOverrideDto>())
                {
                    try
                    {
                        if (replacement == null || document.contentNamespace == null || document.dependencies == null)
                            throw new FormatException("Overrides require scoped pack content and dependency metadata.");
                        var registry = hub.Bindings(replacement.registry ?? "");
                        var id = EveId.Parse(replacement.id);
                        var implementation = EveId.Parse(replacement.implementationId);
                        if (!requestKeys.Add(replacement.registry + "|" + id + "|" + document.contentNamespace))
                            throw new FormatException($"Duplicate override request for {id} from {document.contentNamespace}.");
                        registry.CheckOverride(id, implementation, document.contentNamespace, replacement.version,
                            document.dependencies, pendingDefinitions);
                        requests.Add((registry, id, implementation, document.contentNamespace, replacement.version, document.dependencies));
                    }
                    catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
                    {
                        throw new FormatException($"Override in '{document.name}': {ex.Message}", ex);
                    }
                }

            // EN: Commit only after every document and reference has validated.
            // VI: Chỉ ghi vào registry sau khi mọi tài liệu và tham chiếu đều hợp lệ.
            foreach (EveCharacterDefinition character in characterRegistry.Snapshot().Values.Concat(pendingCharacters.Values))
                foreach (EveId skillId in character.SkillIds)
                    if (!pendingSkills.ContainsKey(skillId) && !skillRegistry.TryGet(skillId, out _))
                        throw new FormatException($"Character {character.Id} references unknown skill {skillId}.");
            foreach (var pair in pendingStats) statRegistry.Register(pair.Value);
            foreach (var pair in pendingSkills) skillRegistry.Register(pair.Value);
            foreach (var pair in pendingCharacters) characterRegistry.Register(pair.Value);
            foreach (var batch in batches) batch.Commit();
            foreach (var request in requests)
                request.registry.RequestOverride(request.id, request.implementation, request.provider, request.version, request.dependencies);
        }

        // EN: Definitions belong to their pack; references can target dependency namespaces.
        // VI: Định nghĩa thuộc namespace của gói; tham chiếu có thể trỏ sang namespace phụ thuộc.
        private static void ValidateNamespace(EveId id, string contentNamespace)
        {
            if (id.Namespace == "base") throw new FormatException("base is reserved for public references.");
            if (contentNamespace != null && id.Namespace != contentNamespace)
                throw new FormatException($"Definition '{id}' must belong to pack namespace '{contentNamespace}'.");
        }

        private static void ValidateReferenceNamespace(EveId id, string contentNamespace,
            IReadOnlyCollection<string> dependencies, EveRegistry<EveStatDefinition> registry)
        {
            if (id.Namespace == "base")
            {
                string owner = registry.BindingOwner(id);
                if (contentNamespace == null || dependencies == null || contentNamespace == owner || dependencies.Contains(owner)) return;
                throw new FormatException($"Reference to {id} requires dependency on {owner}.");
            }
            if (contentNamespace == null || dependencies == null || id.Namespace == contentNamespace ||
                id.Namespace == "eve" || dependencies.Contains(id.Namespace)) return;
            throw new FormatException($"Reference to stat '{id}' requires pack '{contentNamespace}' to declare dependency '{id.Namespace}'.");
        }
    }
}
