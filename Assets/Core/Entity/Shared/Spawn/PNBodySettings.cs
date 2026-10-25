using System;
using EveEngine;
using Newtonsoft.Json.Linq;

public enum PNDamageMode { Normal, Hits }
public enum PNHealthBarMode { Auto, Hidden }

// EN: The `body` block of a character's game-owned data (Docs/EntitySpawnPlan.md section 3.3): what the thing physically is when spawned.
//     Fields (all optional, unknown fields rejected):
//       prefab: key of the prefab in the entity prefab catalog ("eve_2" reads as "project_n:eve_2"); empty = playable.prefab, then the spawn point's own prefab.
//       damageMode: "normal" (default) | "hits" (every damaging hit outside iframe removes exactly 1 of the declared maximum Health; needs Health, an integer
//                   maximum >= 1 and no Lorica).
//       healthBar: "auto" (default, shown when the entity has Health) | "hidden".
//       loot: id of a loot table. Read and checked only; nothing consumes it yet.
//     Absent block = every default. Damageable <=> the character declares a project_n:health stat.
// VI: Khối `body` trong dữ liệu character: thứ đó là gì khi được sinh. Có Health <=> nhận sát thương được. `loot` chưa có nơi dùng.
public sealed class PNBodySettings
{
    public const int MaxPrefabLength = 128;
    public static readonly PNBodySettings Default = new PNBodySettings("", PNDamageMode.Normal, PNHealthBarMode.Auto, null);
    // Catalog key as authored (may be empty); NormalizeKey gives the namespaced form.
    public string Prefab { get; }
    public PNDamageMode DamageMode { get; }
    public PNHealthBarMode HealthBar { get; }
    public EveId? Loot { get; }

    public PNBodySettings(string prefab, PNDamageMode damageMode, PNHealthBarMode healthBar, EveId? loot)
    {
        prefab = prefab ?? "";
        if (prefab.Length > 0) ValidateKey(prefab, "body prefab");
        if (!Enum.IsDefined(typeof(PNDamageMode), damageMode)) throw new FormatException("body damageMode is unknown.");
        if (!Enum.IsDefined(typeof(PNHealthBarMode), healthBar)) throw new FormatException("body healthBar is unknown.");
        if (loot.HasValue && !loot.Value.IsValid) throw new FormatException("body loot needs a valid id.");
        Prefab = prefab; DamageMode = damageMode; HealthBar = healthBar; Loot = loot;
    }

    // Catalog keys are [a-z0-9_] with at most one "namespace:" prefix; an old key without a namespace belongs to project_n.
    public static void ValidateKey(string key, string what)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxPrefabLength) throw new FormatException(what + " key must be 1.." + MaxPrefabLength + " characters.");
        int colon = key.IndexOf(':'), colons = 0;
        foreach (var c in key) if (c == ':') colons++;
        if (colons > 1 || colon == 0 || colon == key.Length - 1) throw new FormatException(what + " key allows one namespace prefix such as mod:name.");
        foreach (var c in key) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == ':')) throw new FormatException(what + " key allows a-z, 0-9, _ and one : only.");
    }
    public static string NormalizeKey(string key) => key.IndexOf(':') >= 0 ? key : "project_n:" + key;

    // The prefab key this character asks for: body.prefab, else playable.prefab, else "" (the spawn point's prefab). Both set and different is rejected by Read.
    public string EffectivePrefab(PNPlayableSettings playable)
    {
        if (Prefab.Length > 0) return NormalizeKey(Prefab);
        return playable != null && playable.Prefab.Length > 0 ? NormalizeKey(playable.Prefab) : "";
    }

    // Field-level parse of `data.body` (no stat knowledge). Absent block = Default.
    public static PNBodySettings Parse(object data)
    {
        if (data == null || data is JToken empty && empty.Type == JTokenType.Null) return Default;
        if (!(data is JObject root)) throw new FormatException("Project_N character data must be an object.");
        var token = root["body"];
        if (token == null) return Default;
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "prefab", "damageMode", "healthBar", "loot");
        var mode = PNDamageMode.Normal;
        if (obj["damageMode"] != null)
            switch (PNBuffContentBatch.String(obj, "damageMode"))
            {
                case "normal": break;
                case "hits": mode = PNDamageMode.Hits; break;
                default: throw new FormatException("body damageMode must be normal or hits.");
            }
        var bar = PNHealthBarMode.Auto;
        if (obj["healthBar"] != null)
            switch (PNBuffContentBatch.String(obj, "healthBar"))
            {
                case "auto": break;
                case "hidden": bar = PNHealthBarMode.Hidden; break;
                default: throw new FormatException("body healthBar must be auto or hidden.");
            }
        EveId? loot = null;
        if (obj["loot"] != null)
        {
            if (!EveId.TryParse(PNBuffContentBatch.String(obj, "loot"), out var id)) throw new FormatException("body loot must be a namespaced id.");
            loot = id;
        }
        return new PNBodySettings(PNBuffContentBatch.OptionalString(obj, "prefab"), mode, bar, loot);
    }

    // Whole check for one character: block fields plus the rules that need its stats and its playable block.
    public static PNBodySettings Read(EveCharacterDefinition character)
    {
        if (character == null) throw new ArgumentNullException(nameof(character));
        var body = Parse(character.Data);
        var playable = PNPlayableSettings.Parse(character.Data);
        if (body.Prefab.Length > 0 && playable != null && playable.Prefab.Length > 0 && NormalizeKey(body.Prefab) != NormalizeKey(playable.Prefab))
            throw new FormatException("Character " + character.Id + ": body.prefab and playable.prefab name different prefabs.");
        EveInitialStat health = null; bool lorica = false;
        foreach (var stat in character.Stats)
        {
            if (stat.Id == PNContentIds.Health) health = stat;
            else if (stat.Id == PNContentIds.Lorica && stat.BaseValue > 0d) lorica = true;
        }
        if (body.DamageMode == PNDamageMode.Hits)
        {
            if (health == null) throw new FormatException("Character " + character.Id + ": damageMode hits needs a health stat.");
            if (health.BaseValue < 1d || health.BaseValue != Math.Floor(health.BaseValue)) throw new FormatException("Character " + character.Id + ": damageMode hits needs an integer maximum health of at least 1.");
            if (lorica) throw new FormatException("Character " + character.Id + ": damageMode hits cannot be combined with lorica.");
        }
        return body;
    }
}
