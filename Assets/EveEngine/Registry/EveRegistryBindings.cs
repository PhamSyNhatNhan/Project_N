using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;

namespace EveEngine
{
    // EN: Diagnostics and compatibility data for one stable public concept.
    // VI: Dữ liệu chẩn đoán và tương thích của một khái niệm công khai ổn định.
    public sealed class EveRegistryBinding
    {
        public EveId Id { get; }
        public EveId Identity { get; }
        public EveId ImplementationId { get; }
        public string Owner { get; }
        public string Provider { get; }
        public string Version { get; }
        public string ContractVersion { get; }
        public bool AllowsOverride { get; }

        internal EveRegistryBinding(EveId id, EveId identity, EveId implementationId, string owner,
            string provider, string version, string contractVersion, bool allowsOverride)
        {
            Id = id; Identity = identity; ImplementationId = implementationId;
            Owner = owner; Provider = provider; Version = version;
            ContractVersion = contractVersion; AllowsOverride = allowsOverride;
        }
    }

    public interface EveIBindingRegistry
    {
        void ValidateBindings();
        IEnumerable<EveRegistryBinding> BindingSnapshot();
        void RequestOverride(EveId id, EveId implementationId, string provider, string version, IEnumerable<string> dependencies);
        void SelectOverride(EveId id, string provider);
        void CheckOverride(EveId id, EveId implementationId, string provider, string version,
            IEnumerable<string> dependencies, IEnumerable<EveIIdentified> pendingDefinitions);
    }

    // EN: Public bindings select implementations; identity always remains the original definition ID.
    // VI: Binding công khai chọn triển khai; danh tính luôn giữ ID định nghĩa gốc.
    public sealed partial class EveRegistry<T> : EveIBindingRegistry where T : class, EveIIdentified
    {
        private readonly ConcurrentDictionary<EveId, T> sealedDefinitions = new ConcurrentDictionary<EveId, T>();
        private sealed class Candidate
        {
            public EveId Target;
            public string Provider, Version;
        }
        private sealed class Binding
        {
            public EveId Identity;
            public string Owner, ContractVersion, Selection;
            public bool AllowsOverride;
            public Func<T, T, bool> Compatible;
            public readonly List<Candidate> Candidates = new List<Candidate>();
        }
        private readonly Dictionary<EveId, Binding> bindings = new Dictionary<EveId, Binding>();
        private readonly Dictionary<EveId, EveId> publicIds = new Dictionary<EveId, EveId>();

        // EN: Only trusted game modules publish concepts; moddable concepts require an explicit contract.
        // VI: Chỉ module game tin cậy công bố khái niệm; mục cho mod thay cần hợp đồng tường minh.
        public void RegisterBase(EveId id, EveId identity, string owner, string contractVersion = "1",
            Func<T, T, bool> compatible = null)
        {
            RequireMutable();
            if (!id.IsValid || id.Namespace != "base" || !identity.IsValid || identity.Namespace == "base")
                throw new ArgumentException("A base binding must point directly to a namespaced definition.");
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(contractVersion))
                throw new ArgumentException("Binding owner and contract version are required.");
            if (bindings.ContainsKey(id) || publicIds.ContainsKey(identity))
                throw new InvalidOperationException($"Duplicate public binding or identity: {id} / {identity}.");
            bindings.Add(id, new Binding { Identity = identity, Owner = owner,
                ContractVersion = contractVersion, AllowsOverride = compatible != null, Compatible = compatible });
            publicIds.Add(identity, id);
        }

        // EN: Replacement definitions remain in the provider namespace; dependencies are explicit.
        // VI: Định nghĩa thay thế giữ namespace của provider; phụ thuộc phải khai báo rõ.
        public void RequestOverride(EveId id, EveId implementationId, string provider, string version,
            IEnumerable<string> dependencies)
        {
            CheckRequest(id, implementationId, provider, version, dependencies);
            bindings[id].Candidates.Add(new Candidate { Target = implementationId, Provider = provider, Version = version });
        }

        private void CheckRequest(EveId id, EveId implementationId, string provider, string version,
            IEnumerable<string> dependencies)
        {
            RequireMutable();
            if (!bindings.TryGetValue(id, out var binding)) throw new KeyNotFoundException($"Unknown public concept {id}.");
            if (!binding.AllowsOverride) throw new InvalidOperationException($"Concept {id} does not allow overrides.");
            if (!implementationId.IsValid || implementationId.Namespace != provider || provider == "base" || provider == binding.Owner)
                throw new ArgumentException("Override implementation must belong to a separate provider namespace.");
            if (string.IsNullOrWhiteSpace(version) || dependencies == null || !dependencies.Contains(binding.Owner))
                throw new ArgumentException($"Override {id} requires a version and dependency on {binding.Owner}.");
            if (binding.Candidates.Any(candidate => candidate.Provider == provider))
                throw new InvalidOperationException($"Duplicate override from {provider} for {id}.");
        }

        public void CheckOverride(EveId id, EveId implementationId, string provider, string version,
            IEnumerable<string> dependencies, IEnumerable<EveIIdentified> pendingDefinitions)
        {
            CheckRequest(id, implementationId, provider, version, dependencies);
            var pending = (pendingDefinitions ?? Array.Empty<EveIIdentified>()).OfType<T>().ToDictionary(value => value.Id);
            var binding = bindings[id];
            if (!entries.TryGetValue(binding.Identity, out var original) && !pending.TryGetValue(binding.Identity, out original))
                throw new KeyNotFoundException($"Missing original {binding.Identity}.");
            if (!entries.TryGetValue(implementationId, out var replacement) && !pending.TryGetValue(implementationId, out replacement))
                throw new KeyNotFoundException($"Missing override implementation {implementationId}.");
            if (!binding.Compatible(original, replacement)) throw new InvalidOperationException($"Override {implementationId} violates contract of {id}.");
        }

        // EN: User/game configuration resolves conflicts, never module registration order.
        // VI: Cấu hình người dùng/game giải quyết xung đột, không dùng thứ tự nạp module.
        public void SelectOverride(EveId id, string provider)
        {
            RequireMutable();
            if (!bindings.TryGetValue(id, out var binding)) throw new KeyNotFoundException($"Unknown public concept {id}.");
            if (string.IsNullOrWhiteSpace(provider) || binding.Selection != null)
                throw new InvalidOperationException($"Invalid or duplicate selection for {id}.");
            binding.Selection = provider;
        }

        // EN: Normalize only public references, not provider-owned implementation IDs.
        // VI: Chỉ chuẩn hóa tham chiếu công khai, không đổi ID triển khai riêng của provider.
        public EveId ResolveIdentity(EveId id) => bindings.TryGetValue(id, out var binding) ? binding.Identity : id;
        public string BindingOwner(EveId id) => bindings.TryGetValue(id, out var binding) ? binding.Owner
            : throw new KeyNotFoundException($"Unknown public concept {id}.");

        // EN: Runtime identity keys use the selected implementation while direct Get(original) bypasses it.
        // VI: Khóa danh tính runtime dùng triển khai đã chọn; Get(ID gốc) gọi đích danh bản gốc.
        public T GetEffective(EveId id)
            => publicIds.TryGetValue(id, out var publicId) ? Get(publicId) : Get(id);

        private Candidate ResolveCandidate(EveId id, Binding binding)
        {
            if (binding.Selection == binding.Owner)
                return new Candidate { Target = binding.Identity, Provider = binding.Owner, Version = binding.ContractVersion };
            if (binding.Selection != null)
                return binding.Candidates.SingleOrDefault(candidate => candidate.Provider == binding.Selection)
                    ?? throw new InvalidOperationException($"Selected provider {binding.Selection} is absent for {id}.");
            if (binding.Candidates.Count > 1)
                throw new InvalidOperationException($"Conflicting overrides for {id}: {string.Join(", ", binding.Candidates.Select(c => c.Provider).OrderBy(p => p, StringComparer.Ordinal))}. Select a provider explicitly.");
            return binding.Candidates.Count == 1 ? binding.Candidates[0]
                : new Candidate { Target = binding.Identity, Provider = binding.Owner, Version = binding.ContractVersion };
        }

        private T GetBindingDefinition(EveId id)
        {
            if (IsSealed && sealedDefinitions.TryGetValue(id, out var cached)) return cached;
            var candidate = ResolveCandidate(id, bindings[id]);
            if (!entries.TryGetValue(candidate.Target, out var value))
                throw new KeyNotFoundException($"Binding {id} references missing implementation {candidate.Target}.");
            if (IsSealed) sealedDefinitions.TryAdd(id, value);
            return value;
        }

        public void ValidateBindings()
        {
            foreach (var pair in bindings)
            {
                if (!entries.TryGetValue(pair.Value.Identity, out var original))
                    throw new KeyNotFoundException($"Binding {pair.Key} references missing original {pair.Value.Identity}.");
                foreach (var candidate in pair.Value.Candidates)
                {
                    if (!entries.TryGetValue(candidate.Target, out var replacement))
                        throw new KeyNotFoundException($"Override {pair.Key} references missing implementation {candidate.Target}.");
                    if (!pair.Value.Compatible(original, replacement))
                        throw new InvalidOperationException($"Override {candidate.Target} violates contract of {pair.Key}.");
                }
                ResolveCandidate(pair.Key, pair.Value);
            }
        }

        public IEnumerable<EveRegistryBinding> BindingSnapshot()
            => bindings.OrderBy(pair => pair.Key).Select(pair =>
            {
                var candidate = ResolveCandidate(pair.Key, pair.Value);
                return new EveRegistryBinding(pair.Key, pair.Value.Identity, candidate.Target, pair.Value.Owner,
                    candidate.Provider, candidate.Version, pair.Value.ContractVersion, pair.Value.AllowsOverride);
            }).ToArray();

        private void RequireMutable()
        {
            if (IsSealed) throw new InvalidOperationException($"Registry<{typeof(T).Name}> is sealed.");
        }
    }
}
