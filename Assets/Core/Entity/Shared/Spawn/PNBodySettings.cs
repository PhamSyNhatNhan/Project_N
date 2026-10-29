using System;
using EveEngine;
using Newtonsoft.Json.Linq;

public enum PNDamageMode { Normal, Hits }
public enum PNHealthBarMode { Auto, Hidden }
public enum PNAggroMode { Normal, Ignored }

// EN: The `body` block of a character's game-owned data (Docs/EntitySpawnPlan.md section 3.3): what the thing physically is when spawned.
//     Fields (all optional, unknown fields rejected):
//       prefab: key of the prefab in the entity prefab catalog ("eve_2" reads as "project_n:eve_2"); empty = playable.prefab, then the spawn point's own prefab.
//       damageMode: "normal" (default) | "hits" (every damaging hit outside iframe removes exactly 1 of the declared maximum Health; needs Health, an integer
//                   maximum >= 1 and no Lorica).
//       healthBar: "auto" (default, shown when the entity has Health) | "hidden".
//       aggro: "normal" (default) | "ignored" (the enemy Aggro rule never chooses this entity; the aggro stat stays a plain weight, so a no-Health
//              taunt stone with a high aggro stat still draws enemies).
//       loot: id of a loot table. Read and checked only; nothing consumes it yet.
//       visual: id of a Visual appearance (Docs/VisualPlan.md): the character is drawn as a rig puppet instead of its prefab's own sprite. Absent = unchanged.
//       build: "short" | "normal" | "tall": replaces the appearance's build (project_n:build_short ...); needs visual.
//     Absent block = every default. Damageable <=> the character declares a project_n:health stat.
// VI: Khối `body` trong dữ liệu character: thứ đó là gì khi được sinh. Có Health <=> nhận sát thương được. `loot` chưa có nơi dùng.
public sealed class PNBodySettings
{
    public const int MaxPrefabLength = 128;
    public static readonly PNBodySettings Default = new PNBodySettings("", PNDamageMode.Normal, PNHealthBarMode.Auto, null, PNAggroMode.Normal);
    // Catalog key as authored (may be empty); NormalizeKey gives the namespaced form.
    public string Prefab { get; }
    public PNDamageMode DamageMode { get; }
    public PNHealthBarMode HealthBar { get; }
    public EveId? Loot { get; }
    public PNAggroMode Aggro { get; }
    public EveId? Visual { get; }
    public string Build { get; }

    public PNBodySettings(string prefab, PNDamageMode damageMode, PNHealthBarMode healthBar, EveId? loot, PNAggroMode aggro = PNAggroMode.Normal, EveId? visual = null, string build = "")
    {
        prefab = prefab ?? "";
        if (prefab.Length > 0) ValidateKey(prefab, "body prefab");
        if (!Enum.IsDefined(typeof(PNDamageMode), damageMode)) throw new FormatException("body damageMode is unknown.");
        if (!Enum.IsDefined(typeof(PNHealthBarMode), healthBar)) throw new FormatException("body healthBar is unknown.");
        if (loot.HasValue && !loot.Value.IsValid) throw new FormatException("body loot needs a valid id.");
        if (!Enum.IsDefined(typeof(PNAggroMode), aggro)) throw new FormatException("body aggro is unknown.");
        build = build ?? "";
        if (visual.HasValue && !visual.Value.IsValid) throw new FormatException("body visual needs a valid id.");
        if (build.Length > 0 && build != "short" && build != "normal" && build != "tall") throw new FormatException("body build must be short, normal or tall.");
        if (build.Length > 0 && !visual.HasValue) throw new FormatException("body build needs a body visual.");
        Prefab = prefab; DamageMode = damageMode; HealthBar = healthBar; Loot = loot; Aggro = aggro; Visual = visual; Build = build;
    }

    // The build id this character is drawn with: the `build` shortcut (project_n:build_<name>) when given, else the appearance's own build.
    public EveId? BuildOverride => Build.Length == 0 ? (EveId?)null : EveId.Parse("project_n:build_" + Build);

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
        PNBuffContentBatch.Fields(obj, "prefab", "damageMode", "healthBar", "aggro", "loot", "visual", "build");
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
        var aggro = PNAggroMode.Normal;
        if (obj["aggro"] != null)
            switch (PNBuffContentBatch.String(obj, "aggro"))
            {
                case "normal": break;
                case "ignored": aggro = PNAggroMode.Ignored; break;
                default: throw new FormatException("body aggro must be normal or ignored.");
            }
        EveId? loot = null;
        if (obj["loot"] != null)
        {
            if (!EveId.TryParse(PNBuffContentBatch.String(obj, "loot"), out var id)) throw new FormatException("body loot must be a namespaced id.");
            loot = id;
        }
        EveId? visual = null;
        if (obj["visual"] != null)
        {
            if (!EveId.TryParse(PNBuffContentBatch.String(obj, "visual"), out var visualId)) throw new FormatException("body visual must be a namespaced id.");
            visual = visualId;
        }
        return new PNBodySettings(PNBuffContentBatch.OptionalString(obj, "prefab"), mode, bar, loot, aggro, visual, PNBuffContentBatch.OptionalString(obj, "build"));
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
