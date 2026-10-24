using System;
using System.Collections.Generic;
using EveEngine;

// EN: Access to the one registry of spawnable definitions. Typed access checks the kind, so asking for a projectile
//     with the id of an area fails with a clear message instead of a cast error.
// VI: Truy cập registry duy nhất của định nghĩa sinh được. Truy cập có kiểu kiểm kind để hỏi projectile bằng id của area báo lỗi rõ.
public static class PNInstances
{
    public const string RegistryName = "instances";
    // Called once by the gameplay content module before any kind registers.
    public static EveRegistry<PNInstanceDefinition> Create(EveRegistryHub hub)
    {
        var registry = hub.GetOrCreateFamily<PNInstanceDefinition>();
        hub.NameRegistry<PNInstanceDefinition>(RegistryName);
        return registry;
    }
    public static EveRegistry<PNInstanceDefinition> Instances(this EveRegistryHub hub) => hub.Get<PNInstanceDefinition>();
    public static T GetKind<T>(this EveRegistry<PNInstanceDefinition> registry, EveId id) where T : PNInstanceDefinition => As<T>(registry.Get(id), id);
    public static T GetEffectiveKind<T>(this EveRegistry<PNInstanceDefinition> registry, EveId id) where T : PNInstanceDefinition => As<T>(registry.GetEffective(id), id);
    public static bool TryGetKind<T>(this EveRegistry<PNInstanceDefinition> registry, EveId id, out T value) where T : PNInstanceDefinition
    {
        value = registry.TryGet(id, out var found) ? found as T : null;
        return value != null;
    }
    // A public alias whose replacement must be the same kind (and satisfy the caller's rule).
    public static void RegisterBase<T>(this EveRegistry<PNInstanceDefinition> registry, EveId id, EveId identity, string owner, Func<T, T, bool> compatible)
        where T : PNInstanceDefinition
        => registry.RegisterBase(id, identity, owner, compatible: (original, replacement) => original is T a && replacement is T b && compatible(a, b));
    private static T As<T>(PNInstanceDefinition value, EveId id) where T : PNInstanceDefinition
        => value as T ?? throw new KeyNotFoundException(id + " is a " + value.Kind + " definition, not " + typeof(T).Name + ".");
}
