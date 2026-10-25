using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The `pet` block of a character's game-owned data (Docs/SummonPlan.md section 3.3): a summon that hovers near its owner and every `interval` seconds stops
//     for `aimStop` seconds and fires one projectile at a target within `searchRadius` of itself. Every field is required (one authoritative payload, no hidden
//     defaults); unknown fields are rejected.
//       interval: seconds of entity time between shots (> 0), aimStop: seconds it stands still before a shot (>= 0), searchRadius: meters around the pet (> 0),
//       followRate: how fast it closes on its hover spot (1/s, > 0), offset: [x,y,z] meters from the owner's position to the hover spot,
//       projectileId: the registered projectile it fires,
//       damage: {amount (number >= 0), canCrit (bool), tags (damage tag ids, exactly one damage type)}; it is added to the owner's modifiers when the shot is
//       prepared: the owner's captured snapshot with projectile_damage and summon_damage.
// VI: Khối `pet` trong dữ liệu character: summon lơ lửng gần chủ, mỗi `interval` giây đứng yên `aimStop` giây rồi bắn một viên vào mục tiêu quanh nó.
public sealed class PNPetSettings
{
    public double Interval, AimStop, SearchRadius, FollowRate, DamageAmount;
    public double[] Offset;
    public EveId ProjectileId;
    public bool CanCrit;
    public EveId[] DamageTags;

    public static PNPetSettings Parse(JToken token)
    {
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "interval", "aimStop", "searchRadius", "followRate", "offset", "projectileId", "damage");
        var result = new PNPetSettings { Interval = PNBuffContentBatch.Number(obj, "interval"), AimStop = PNBuffContentBatch.Number(obj, "aimStop"), SearchRadius = PNBuffContentBatch.Number(obj, "searchRadius"), FollowRate = PNBuffContentBatch.Number(obj, "followRate") };
        if (!(result.Interval > 0d) || double.IsInfinity(result.Interval)) throw new FormatException("pet interval must be finite and above 0.");
        if (!(result.AimStop >= 0d) || double.IsInfinity(result.AimStop)) throw new FormatException("pet aimStop must be finite and not negative.");
        if (!(result.SearchRadius > 0d) || result.SearchRadius > 1000d) throw new FormatException("pet searchRadius must be above 0 and at most 1000.");
        if (!(result.FollowRate > 0d) || double.IsInfinity(result.FollowRate)) throw new FormatException("pet followRate must be finite and above 0.");
        var offset = PNBuffContentBatch.Array(obj["offset"]).ToArray();
        if (offset.Length != 3 || offset.Any(x => x.Type != JTokenType.Integer && x.Type != JTokenType.Float || double.IsNaN(x.Value<double>()) || double.IsInfinity(x.Value<double>()) || Math.Abs(x.Value<double>()) > 1000d)) throw new FormatException("pet offset needs three finite numbers.");
        result.Offset = offset.Select(x => x.Value<double>()).ToArray();
        if (!EveId.TryParse(PNBuffContentBatch.String(obj, "projectileId"), out result.ProjectileId)) throw new FormatException("pet projectileId must be a namespaced id.");
        var damage = PNBuffContentBatch.Object(obj["damage"]); PNBuffContentBatch.Fields(damage, "amount", "canCrit", "tags");
        result.DamageAmount = PNBuffContentBatch.Number(damage, "amount");
        if (!(result.DamageAmount >= 0d) || double.IsInfinity(result.DamageAmount)) throw new FormatException("pet damage amount must be finite and not negative.");
        result.CanCrit = PNBuffContentBatch.Bool(damage, "canCrit");
        result.DamageTags = PNSkillEffect.Ids(damage["tags"]).ToArray();
        if (result.DamageTags.Length == 0) throw new FormatException("pet damage needs tags with a damage type.");
        return result;
    }

    // The block of a character, or null when it has none.
    public static PNPetSettings Read(EveCharacterDefinition character)
    {
        if (character == null) throw new ArgumentNullException(nameof(character));
        var root = character.Data as JObject;
        return root?["pet"] == null ? null : Parse(root["pet"]);
    }

    // Content check: every character with a pet block parses, fires a registered projectile and names exactly one damage type.
    public static void ValidateAll(EveRegistryHub hub)
    {
        foreach (var character in hub.Characters.Snapshot().Values)
        {
            PNPetSettings settings;
            try { settings = Read(character); }
            catch (FormatException error) { throw new FormatException("Character " + character.Id + ": " + error.Message); }
            if (settings == null) continue;
            hub.Instances().GetEffectiveKind<PNProjectileDefinition>(settings.ProjectileId).ValidateInput(new JObject());
            new PNStatSet(hub).ResolveDamageType(settings.DamageTags);
        }
    }
}
