using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// EN: How an entity role is built when it is spawned. The implementation of a PNEntityDefinition is a class implementing this interface
//     (not a component): the prefab comes from the entity prefab catalog, so the recipe only adds what the role needs (input owner,
//     ground binding, local-player binding). The classes that do the work are in Game (PNEntityRecipe and its two built-in subclasses).
// VI: Cách dựng một vai trò entity khi sinh. Implementation của PNEntityDefinition là lớp cài interface này (không phải component).
//     A recipe keeps no state of its own (one instance, made by the factory given at registration, serves every spawn of the role).
//     IsPlayer is the one place that says a role is a player: placed at a spawn point, with input owners and ground binding, treated as a player by the coop layer.
public interface IPNEntityRecipe { bool IsPlayer { get; } }

public enum PNEntityOwner { Peer, Host }

// EN: The `data` payload of an entity definition. Every field is required except the ones marked optional.
//       owner: "peer" | "host" (who controls it; no effect yet - being a player is the recipe's IsPlayer)          persist: bool (written into the world checkpoint)      followsOwner: bool (goes along when the owner changes world)
//       lifetime: number > 0 (seconds) or null (no limit)
//       countsForClear: bool and dropsLoot: bool are read and kept, nothing consumes them yet (no clear condition / loot system exists)
//       prefab (optional): catalog key that overrides the character's prefab        spawnBuffs (optional): Buff ids applied right after publication
// VI: Payload `data` của entity. Mọi trường bắt buộc trừ khi ghi tuỳ chọn; countsForClear và dropsLoot chưa có tác dụng.
public sealed class PNEntitySettings
{
    public const double MaxLifetimeSeconds = 86400d;
    public const int MaxSpawnBuffs = 16;
    public PNEntityOwner Owner { get; }
    public bool Persist { get; }
    public bool FollowsOwner { get; }
    public double? Lifetime { get; }
    public bool CountsForClear { get; }
    public bool DropsLoot { get; }
    public string Prefab { get; }
    public IReadOnlyList<EveId> SpawnBuffs { get; }

    public PNEntitySettings(PNEntityOwner owner, bool persist, bool followsOwner, double? lifetime, bool countsForClear, bool dropsLoot, string prefab, IReadOnlyList<EveId> spawnBuffs)
    {
        if (!Enum.IsDefined(typeof(PNEntityOwner), owner)) throw new FormatException("entity owner must be peer or host.");
        if (lifetime.HasValue && (double.IsNaN(lifetime.Value) || double.IsInfinity(lifetime.Value) || lifetime.Value <= 0d || lifetime.Value > MaxLifetimeSeconds))
            throw new FormatException("entity lifetime must be null or a number in (0, " + MaxLifetimeSeconds + "].");
        prefab = prefab ?? "";
        if (prefab.Length > 0) PNBodySettings.ValidateKey(prefab, "entity prefab");
        var buffs = new List<EveId>(spawnBuffs ?? Array.Empty<EveId>());
        if (buffs.Count > MaxSpawnBuffs) throw new FormatException("entity spawnBuffs allows at most " + MaxSpawnBuffs + " Buffs.");
        var seen = new HashSet<EveId>();
        foreach (var id in buffs) if (!id.IsValid || !seen.Add(id)) throw new FormatException("entity spawnBuffs must be valid and unique.");
        Owner = owner; Persist = persist; FollowsOwner = followsOwner; Lifetime = lifetime; CountsForClear = countsForClear; DropsLoot = dropsLoot; Prefab = prefab; SpawnBuffs = buffs.AsReadOnly();
    }

    public static PNEntitySettings Parse(JToken data)
    {
        var obj = PNBuffContentBatch.Object(data);
        PNBuffContentBatch.Fields(obj, "owner", "persist", "followsOwner", "lifetime", "countsForClear", "dropsLoot", "prefab", "spawnBuffs");
        foreach (var required in new[] { "owner", "persist", "followsOwner", "lifetime", "countsForClear", "dropsLoot" })
            if (obj[required] == null) throw new FormatException("entity " + required + " is required.");
        PNEntityOwner owner;
        switch (PNBuffContentBatch.String(obj, "owner"))
        {
            case "peer": owner = PNEntityOwner.Peer; break;
            case "host": owner = PNEntityOwner.Host; break;
            default: throw new FormatException("entity owner must be peer or host.");
        }
        double? lifetime = null;
        if (obj["lifetime"].Type != JTokenType.Null) lifetime = PNBuffContentBatch.Number(obj, "lifetime");
        var buffs = new List<EveId>();
        if (obj["spawnBuffs"] != null)
        {
            var array = obj["spawnBuffs"] as JArray ?? throw new FormatException("entity spawnBuffs must be an array.");
            foreach (var item in array)
            {
                if (item.Type != JTokenType.String || !EveId.TryParse(item.Value<string>(), out var id)) throw new FormatException("entity spawnBuffs entries must be Buff ids.");
                buffs.Add(id);
            }
        }
        return new PNEntitySettings(owner, PNBuffContentBatch.Bool(obj, "persist"), PNBuffContentBatch.Bool(obj, "followsOwner"), lifetime,
            PNBuffContentBatch.Bool(obj, "countsForClear"), PNBuffContentBatch.Bool(obj, "dropsLoot"), PNBuffContentBatch.OptionalString(obj, "prefab"), buffs);
    }
}

// EN: Kind "entity" of the shared instances registry: the role something plays when it is spawned (player, enemy, npc, summon, prop, mod roles).
//     The character says what the thing is; this says how it is built and owned. Docs/EntitySpawnPlan.md.
// VI: Kind "entity" của registry instances: vai trò của thứ được sinh. Nhân vật là "thứ đó là gì", entity là "vai trò khi sinh".
public sealed class PNEntityDefinition : PNInstanceDefinition
{
    public const string KindName = "entity";
    public override string Kind => KindName;
    [JsonIgnore] public PNEntitySettings Settings { get; }
    private readonly Func<IPNEntityRecipe> create;
    private IPNEntityRecipe recipe;
    // The one shared instance of this role's recipe, made by the factory the definition was registered with (no reflection: safe under IL2CPP stripping).
    [JsonIgnore] public IPNEntityRecipe Recipe => recipe ?? (recipe = Make());
    [JsonIgnore] public bool IsPlayer => Recipe.IsPlayer;

    public PNEntityDefinition(EveId id, Type recipe, Func<IPNEntityRecipe> create, JToken data) : this(id, recipe, create, data, ValidatePayload, input => { }, ValidateReferences) { }
    private PNEntityDefinition(EveId id, Type recipe, Func<IPNEntityRecipe> create, JToken data, Action<JToken, EveRegistryHub> validator, Action<JToken> inputValidator, Action<JToken, PNContentReferenceContext> references)
        : base(id, RequireRecipe(recipe), data, validator, inputValidator, references)
    {
        this.create = create ?? throw new ArgumentNullException(nameof(create));
        Settings = PNEntitySettings.Parse(data);
    }
    private IPNEntityRecipe Make()
    {
        var made = create();
        if (made == null || made.GetType() != ImplementationType) throw new InvalidOperationException("Entity " + Id + " recipe factory must return a " + ImplementationType.Name + ".");
        return made;
    }

    private static Type RequireRecipe(Type recipe)
    {
        if (recipe == null || recipe.IsAbstract || recipe.IsInterface || recipe.ContainsGenericParameters || !typeof(IPNEntityRecipe).IsAssignableFrom(recipe))
            throw new ArgumentException("Entity implementation must be a concrete IPNEntityRecipe class.");
        return recipe;
    }
    private static void ValidatePayload(JToken data, EveRegistryHub hub) => PNEntitySettings.Parse(data);
    private static void ValidateReferences(JToken data, PNContentReferenceContext context)
    {
        var obj = PNBuffContentBatch.Object(data);
        if (obj["spawnBuffs"] is JArray buffs)
            for (int i = 0; i < buffs.Count; i++) context.Require<PNBuffDefinition>(buffs[i], "spawnBuffs[" + i + "]");
    }

    // Same recipe with new data (JSON `instances` entry whose factoryId is this definition).
    public override PNInstanceDefinition Derive(EveId id, JToken data, JObject entry) => new PNEntityDefinition(id, ImplementationType, create, data, Validator, InputValidator, ReferenceValidator);
}
