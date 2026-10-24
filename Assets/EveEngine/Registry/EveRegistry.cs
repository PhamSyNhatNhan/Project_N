using System;
using System.Collections.Generic;

namespace EveEngine
{
    // EN: Marks a content definition that can be stored under an EveId.
    // VI: Đánh dấu định nghĩa nội dung có thể lưu theo EveId.
    public interface EveIIdentified
    {
        EveId Id { get; }
    }

    // EN: Exposes the registration lifecycle shared by all typed registries.
    // VI: Cung cấp vòng đời đăng ký chung cho các registry theo loại.
    public interface EveIRegistry
    {
        bool IsSealed { get; }
        IEnumerable<EveIIdentified> Definitions { get; }
        void Seal();
    }

    // EN: Lets a family registry accept a subclass definition whose static type was inferred as the subclass (mod registration).
    // VI: Cho registry họ nhận định nghĩa lớp con mà kiểu tĩnh bị suy ra là lớp con (đăng ký của mod).
    internal interface EveIUntypedRegistry { void RegisterUntyped(EveIIdentified definition); }

    // EN: Stores one type of content definition and rejects duplicate IDs.
    // VI: Lưu một loại định nghĩa nội dung và từ chối ID trùng.
    public sealed partial class EveRegistry<T> : EveIRegistry, EveIUntypedRegistry where T : class, EveIIdentified
    {
        void EveIUntypedRegistry.RegisterUntyped(EveIIdentified definition)
            => Register(definition as T ?? throw new ArgumentException($"Definition {definition?.Id} is not a {typeof(T).Name}.", nameof(definition)));
        private readonly Dictionary<EveId, T> entries = new Dictionary<EveId, T>();
        public bool IsSealed { get; private set; }
        public int Count => entries.Count;
        public IEnumerable<EveIIdentified> Definitions => entries.Values;

        public void Register(T definition)
        {
            if (IsSealed) throw new InvalidOperationException($"Registry<{typeof(T).Name}> is sealed.");
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            EveId id = definition.Id;
            if (!id.IsValid) throw new ArgumentException("Definition ID is required.", nameof(definition));
            if (id.Namespace == "base") throw new InvalidOperationException("base is reserved for public bindings, not definitions.");
            if (entries.ContainsKey(id)) throw new InvalidOperationException($"Duplicate {typeof(T).Name} ID: {id}.");
            entries.Add(id, definition);
        }

        public T Get(EveId id)
        {
            if (bindings.ContainsKey(id)) return GetBindingDefinition(id);
            if (!entries.TryGetValue(id, out T value))
                throw new KeyNotFoundException($"Unknown {typeof(T).Name} ID: {id}.");
            return value;
        }

        public bool TryGet(EveId id, out T value)
        {
            if (bindings.ContainsKey(id))
            {
                value = GetBindingDefinition(id);
                return true;
            }
            return entries.TryGetValue(id, out value);
        }
        public IReadOnlyDictionary<EveId, T> Snapshot() => new Dictionary<EveId, T>(entries);
        public void Seal()
        {
            ValidateBindings();
            IsSealed = true;
        }
    }
}
