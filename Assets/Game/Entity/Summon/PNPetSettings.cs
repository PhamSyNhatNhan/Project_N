using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The `pet` block of a character's game-owned data (Docs/SummonPlan.md section 3.3): a summon that hovers near its owner and every `interval` seconds stops
//     for `aimStop` seconds and fires one shot at a target within `searchRadius` of itself. Every field is required (one authoritative payload, no hidden
//     defaults); unknown fields are rejected.
//       interval: seconds of entity time between shots (> 0), aimStop: seconds it stands still before a shot (>= 0), searchRadius: meters around the pet (> 0),
//       followRate: how fast it closes on its hover spot (1/s, > 0), offset: [x,y,z] meters from the owner's position to the hover spot.
//     The shot itself is the pet's registered skill (PNSummonGameplayContent.Eve2PetAttack: projectile, damage, tags); the pet's target layers are the character's `combat.targetLayers`.
// VI: Khối `pet` trong dữ liệu character: summon lơ lửng gần chủ, mỗi `interval` giây đứng yên `aimStop` giây rồi bắn vào mục tiêu quanh nó; phát bắn là skill đăng ký của pet.
public sealed class PNPetSettings
{
    public double Interval, AimStop, SearchRadius, FollowRate;
    public double[] Offset;

    public static PNPetSettings Parse(JToken token)
    {
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "interval", "aimStop", "searchRadius", "followRate", "offset");
        var result = new PNPetSettings { Interval = PNBuffContentBatch.Number(obj, "interval"), AimStop = PNBuffContentBatch.Number(obj, "aimStop"), SearchRadius = PNBuffContentBatch.Number(obj, "searchRadius"), FollowRate = PNBuffContentBatch.Number(obj, "followRate") };
        if (!(result.Interval > 0d) || double.IsInfinity(result.Interval)) throw new FormatException("pet interval must be finite and above 0.");
        if (!(result.AimStop >= 0d) || double.IsInfinity(result.AimStop)) throw new FormatException("pet aimStop must be finite and not negative.");
        if (!(result.SearchRadius > 0d) || result.SearchRadius > 1000d) throw new FormatException("pet searchRadius must be above 0 and at most 1000.");
        if (!(result.FollowRate > 0d) || double.IsInfinity(result.FollowRate)) throw new FormatException("pet followRate must be finite and above 0.");
        var offset = PNBuffContentBatch.Array(obj["offset"]).ToArray();
        if (offset.Length != 3 || offset.Any(x => x.Type != JTokenType.Integer && x.Type != JTokenType.Float || double.IsNaN(x.Value<double>()) || double.IsInfinity(x.Value<double>()) || Math.Abs(x.Value<double>()) > 1000d)) throw new FormatException("pet offset needs three finite numbers.");
        result.Offset = offset.Select(x => x.Value<double>()).ToArray();
        return result;
    }

    // The block of a character, or null when it has none.
    public static PNPetSettings Read(EveCharacterDefinition character)
    {
        if (character == null) throw new ArgumentNullException(nameof(character));
        var root = character.Data as JObject;
        return root?["pet"] == null ? null : Parse(root["pet"]);
    }

    // Content check: every character with a pet block parses.
    public static void ValidateAll(EveRegistryHub hub)
    {
        foreach (var character in hub.Characters.Snapshot().Values)
        {
            try { Read(character); }
            catch (FormatException error) { throw new FormatException("Character " + character.Id + ": " + error.Message); }
        }
    }
}
