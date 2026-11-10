using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;

// EN: Entity modules and world features (Docs/EntityModulePlan.md, step E1). A FEATURE is a registered id that says "this world has capability X" (for example `project_n:rogue_buff`); a world carries an
//     immutable set of them (`PNWorldFeatures`, fixed when the world is created, step E2). An ENTITY MODULE is what a feature registers to say "an entity of this kind, in a world that has all of these features,
//     carries these components": the entity factory attaches what matches and knows no feature by name. This file is the pure part (definitions, the set, the matching rule, the check of the registries);
//     attaching components to a real entity is the factory's job (step E3). Modules are registered in code by content modules; nothing here reads JSON.
// VI: Mô-đun thực thể và feature của world (E1). Feature là id đã đăng ký nói "world này có khả năng X"; world mang một tập feature bất biến. Mô-đun thực thể là thứ feature đăng ký: "thực thể loại này, ở world có đủ
//     các feature này, mang các component này"; factory chỉ gắn những gì khớp, không biết tên feature nào. Đây là phần thuần (định nghĩa, tập, luật khớp, kiểm registry); việc gắn vào thực thể thật ở E3.
public static class PNEntityModuleLimits
{
    public const int MaxText = 128, MaxDescription = 256, MaxFeaturesPerWorld = 32, MaxRequires = 8, MaxComponents = 8, MaxRoleIds = 16;
}

// A capability a world can have. Registered so that a mistyped id in a module or a run fails when the content loads instead of silently never matching.
public sealed class PNWorldFeatureDefinition : EveIIdentified
{
    public EveId Id { get; }
    public string Description { get; }

    public PNWorldFeatureDefinition(EveId id, string description = "")
    {
        if (!id.IsValid || id.ToString().Length > PNEntityModuleLimits.MaxText) throw new ArgumentException("A world feature needs a valid id of at most " + PNEntityModuleLimits.MaxText + " characters.", nameof(id));
        if (description != null && description.Length > PNEntityModuleLimits.MaxDescription) throw new ArgumentException("A feature description is at most " + PNEntityModuleLimits.MaxDescription + " characters.", nameof(description));
        Id = id; Description = description ?? "";
    }
}

// The features of ONE world: an immutable, de-duplicated set kept sorted by id, so that two machines that build it from the same ids hold identical sets and an id list can be compared or sent as is.
public sealed class PNWorldFeatures
{
    public static readonly PNWorldFeatures Empty = new PNWorldFeatures(Array.Empty<EveId>());
    private readonly EveId[] ids;
    private readonly HashSet<EveId> lookup;
    public IReadOnlyList<EveId> Ids => Array.AsReadOnly(ids);
    public int Count => ids.Length;

    public PNWorldFeatures(IEnumerable<EveId> source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var set = new HashSet<EveId>();
        foreach (var id in source)
        {
            if (!id.IsValid || id.ToString().Length > PNEntityModuleLimits.MaxText) throw new ArgumentException("A world feature id is required (at most " + PNEntityModuleLimits.MaxText + " characters).", nameof(source));
            set.Add(id);
        }
        if (set.Count > PNEntityModuleLimits.MaxFeaturesPerWorld) throw new ArgumentException("A world has at most " + PNEntityModuleLimits.MaxFeaturesPerWorld + " features.", nameof(source));
        ids = set.OrderBy(x => x.ToString(), StringComparer.Ordinal).ToArray(); lookup = set;
    }

    public bool Has(EveId id) => lookup.Contains(id);
    // A new set with these ids added (the set itself never changes).
    public PNWorldFeatures With(IEnumerable<EveId> more) => more == null ? this : new PNWorldFeatures(ids.Concat(more));
    public bool SameAs(PNWorldFeatures other) => other != null && ids.SequenceEqual(other.ids);
    public override string ToString() => string.Join(",", ids.Select(x => x.ToString()));
}

[Flags]
public enum PNEntityModuleRoles { None = 0, Player = 1, Any = 2 }

// What the matching rule needs to know about an entity: whether it is a player (identity, not `PNPlayerStat`) and the id of its registered role (invalid when it has none, for example an authored scene entity).
public readonly struct PNEntityModuleTarget
{
    public readonly bool IsPlayer;
    public readonly EveId RoleId;
    public PNEntityModuleTarget(bool isPlayer, EveId roleId = default) { IsPlayer = isPlayer; RoleId = roleId; }
}

// EN: Which components an entity carries when its world has certain features. `Roles` / `RoleIds` say which entities (every player, every entity, or these registered roles); `Requires` lists the features the
//     world must ALL have (empty = every world: discouraged, what every world needs belongs on the prefab); `Components` are concrete component types, added when missing (two modules may list the same type).
// VI: Component nào một thực thể mang khi world có feature nào đó. `Roles`/`RoleIds`: thực thể nào; `Requires`: world phải có TẤT CẢ feature (rỗng = mọi world); `Components`: kiểu component cụ thể, thêm khi chưa có.
public sealed class PNEntityModuleDefinition : EveIIdentified, EveIRegistrationReferences
{
    public EveId Id { get; }
    public PNEntityModuleRoles Roles { get; }
    public IReadOnlyList<EveId> RoleIds { get; }
    public IReadOnlyList<EveId> Requires { get; }
    public IReadOnlyList<Type> Components { get; }

    public PNEntityModuleDefinition(EveId id, PNEntityModuleRoles roles, IEnumerable<EveId> roleIds, IEnumerable<EveId> requires, IEnumerable<Type> components)
    {
        if (!id.IsValid || id.ToString().Length > PNEntityModuleLimits.MaxText) throw new ArgumentException("An entity module needs a valid id of at most " + PNEntityModuleLimits.MaxText + " characters.", nameof(id));
        if ((roles & ~(PNEntityModuleRoles.Player | PNEntityModuleRoles.Any)) != 0) throw new ArgumentException("Unknown module roles.", nameof(roles));
        var roleList = Ids(roleIds, PNEntityModuleLimits.MaxRoleIds, "role ids", nameof(roleIds)); var requireList = Ids(requires, PNEntityModuleLimits.MaxRequires, "required features", nameof(requires));
        if (roles == PNEntityModuleRoles.None && roleList.Length == 0) throw new ArgumentException("A module applies to somebody: name the roles or at least one role id.", nameof(roles));
        var types = (components ?? throw new ArgumentNullException(nameof(components))).ToArray();
        if (types.Length < 1 || types.Length > PNEntityModuleLimits.MaxComponents) throw new ArgumentException("A module lists 1 to " + PNEntityModuleLimits.MaxComponents + " components.", nameof(components));
        foreach (var type in types)
            if (type == null || type.IsAbstract || type.IsInterface || type.ContainsGenericParameters || !typeof(MonoBehaviour).IsAssignableFrom(type)) throw new ArgumentException("A module component is a concrete MonoBehaviour type.", nameof(components));
        if (types.Distinct().Count() != types.Length) throw new ArgumentException("A module lists each component once.", nameof(components));
        Id = id; Roles = roles; RoleIds = Array.AsReadOnly(roleList); Requires = Array.AsReadOnly(requireList); Components = Array.AsReadOnly(types);
    }

    private static EveId[] Ids(IEnumerable<EveId> source, int maximum, string what, string parameter)
    {
        var list = (source ?? Array.Empty<EveId>()).ToArray();
        if (list.Length > maximum) throw new ArgumentException("A module lists at most " + maximum + " " + what + ".", parameter);
        if (list.Any(x => !x.IsValid || x.ToString().Length > PNEntityModuleLimits.MaxText) || list.Distinct().Count() != list.Length) throw new ArgumentException("Valid, unique " + what + " are required.", parameter);
        return list;
    }

    // The same rule as a world definition's provider ids: what a module names must come from `base`, its own pack or a declared dependency.
    public void ValidateRegistrationReferences(string owner, IReadOnlyCollection<string> dependencies)
    {
        foreach (var id in Requires.Concat(RoleIds))
            if (id.Namespace != "base" && id.Namespace != owner && !dependencies.Contains(id.Namespace)) throw new InvalidOperationException("Entity module " + Id + " reference requires a declared content dependency: " + id);
    }
}

// The matching rule. No state: the same (modules, entity, features) give the same answer on every machine, in a fixed order.
public static class PNEntityModules
{
    public static bool AppliesTo(PNEntityModuleDefinition module, PNEntityModuleTarget target)
    {
        if (module == null) throw new ArgumentNullException(nameof(module));
        if ((module.Roles & PNEntityModuleRoles.Any) != 0) return true;
        if ((module.Roles & PNEntityModuleRoles.Player) != 0 && target.IsPlayer) return true;
        return target.RoleId.IsValid && module.RoleIds.Contains(target.RoleId);
    }

    public static bool Matches(PNEntityModuleDefinition module, PNEntityModuleTarget target, PNWorldFeatures features)
    {
        if (features == null) throw new ArgumentNullException(nameof(features));
        return AppliesTo(module, target) && module.Requires.All(features.Has);
    }

    // The modules an entity carries in a world with these features, sorted by module id (the order they are applied in).
    public static IReadOnlyList<PNEntityModuleDefinition> Select(IEnumerable<PNEntityModuleDefinition> modules, PNEntityModuleTarget target, PNWorldFeatures features)
    {
        if (modules == null) throw new ArgumentNullException(nameof(modules));
        return modules.Where(x => Matches(x, target, features)).OrderBy(x => x.Id.ToString(), StringComparer.Ordinal).ToArray();
    }

    // The component types of the selected modules, each once, in the order the modules apply.
    public static IReadOnlyList<Type> ComponentTypes(IEnumerable<PNEntityModuleDefinition> selected)
    {
        if (selected == null) throw new ArgumentNullException(nameof(selected));
        var seen = new HashSet<Type>(); var result = new List<Type>();
        foreach (var module in selected) foreach (var type in module.Components) if (seen.Add(type)) result.Add(type);
        return result;
    }
}

// The checks that need the sealed registries: what a module requires is a registered feature and what it names as a role is a registered entity role.
public static class PNEntityModuleValidation
{
    public static void Validate(EveRegistryHub hub)
    {
        if (hub == null) throw new ArgumentNullException(nameof(hub));
        var modules = hub.Get<PNEntityModuleDefinition>(); var features = hub.Get<PNWorldFeatureDefinition>();
        foreach (var module in modules.Snapshot().Values)
        {
            foreach (var feature in module.Requires)
                if (!features.TryGet(feature, out _)) throw new FormatException("Entity module " + module.Id + " requires unknown world feature " + feature);
            foreach (var role in module.RoleIds)
                if (!hub.Instances().TryGetKind<PNEntityDefinition>(role, out _)) throw new FormatException("Entity module " + module.Id + " names unknown entity role " + role);
        }
    }
}
