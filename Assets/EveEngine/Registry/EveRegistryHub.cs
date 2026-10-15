using System;
using System.Collections.Generic;

namespace EveEngine
{
    // EN: Owns typed content registries and seals them after loading.
    // VI: Quản lý các registry nội dung theo loại và khóa chúng sau khi nạp.
    public sealed partial class EveRegistryHub
    {
        private readonly Dictionary<Type, EveIRegistry> registries = new Dictionary<Type, EveIRegistry>();
        private readonly Dictionary<string, EveIBindingRegistry> namedRegistries = new Dictionary<string, EveIBindingRegistry>(StringComparer.Ordinal);
        public bool IsSealed { get; private set; }

        public IReadOnlyDictionary<Type, EveIRegistry> Snapshot() => new Dictionary<Type, EveIRegistry>(registries);

        // EN: Read a registry that was already created during content registration.
        // VI: Đọc registry đã được tạo trong giai đoạn đăng ký nội dung.
        public EveRegistry<T> Get<T>() where T : class, EveIIdentified
        {
            if (!registries.TryGetValue(typeof(T), out EveIRegistry registry))
                throw new KeyNotFoundException($"Registry<{typeof(T).Name}> has not been registered.");
            return (EveRegistry<T>)registry;
        }

        public EveRegistry<T> GetOrCreate<T>() where T : class, EveIIdentified
        {
            if (IsSealed) throw new InvalidOperationException("Registry hub is sealed.");
            Type type = typeof(T);
            if (registries.TryGetValue(type, out EveIRegistry existing)) return (EveRegistry<T>)existing;
            var registry = new EveRegistry<T>();
            registries.Add(type, registry);
            return registry;
        }

        public void Seal()
        {
            if (IsSealed) return;
            foreach (EveIRegistry registry in registries.Values)
                if (registry is EveIBindingRegistry bindings) bindings.ValidateBindings();
            foreach (EveIRegistry registry in registries.Values) registry.Seal();
            IsSealed = true;
        }

        // EN: Games name extension registries so configuration does not contain CLR type names.
        // VI: Game đặt tên registry mở rộng để cấu hình không phải chứa tên kiểu CLR.
        public void NameRegistry<T>(string name) where T : class, EveIIdentified
        {
            if (IsSealed || string.IsNullOrWhiteSpace(name) || namedRegistries.ContainsKey(name))
                throw new InvalidOperationException($"Invalid or duplicate registry name {name}.");
            namedRegistries.Add(name, GetOrCreate<T>());
        }

        public EveIBindingRegistry Bindings(string registry)
            => namedRegistries.TryGetValue(registry, out var value) ? value
                : throw new KeyNotFoundException($"Unknown binding registry {registry}.");
    }
}
