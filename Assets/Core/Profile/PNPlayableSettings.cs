using System;
using EveEngine;
using Newtonsoft.Json.Linq;

public enum PNPlayableAvailability { Default, Locked, Forbidden }

// EN: The `data.playable` block of a character (Docs/ModeMenuPlan.md section 7). A character without the block is not selectable.
//     Fields: name (required, 1..32), description (0..240), accent ([r,g,b] 0..255, default grey), art (resource path, optional),
//     availability ("default" | "locked" | "forbidden", default "default"), prefab (catalog key of the player prefab, optional). Unknown fields are rejected.
// VI: Khối `data.playable` của nhân vật; không có khối thì nhân vật không chọn được.
public sealed class PNPlayableSettings
{
    public const int MaxNameLength = 32, MaxDescriptionLength = 240, MaxPrefabLength = 64;
    public string Name { get; }
    public string Description { get; }
    public int[] AccentRgb => (int[])accent.Clone();
    public string Art { get; }
    public PNPlayableAvailability Availability { get; }
    // Key of the player prefab in the prefab catalog; empty = the spawn point's own prefab. [a-z0-9_], 1..64.
    public string Prefab { get; }
    private readonly int[] accent;

    public PNPlayableSettings(string name, string description, int[] accentRgb, string art, PNPlayableAvailability availability, string prefab = "")
    {
        prefab = prefab ?? "";
        if (prefab.Length > MaxPrefabLength) throw new FormatException("playable prefab key is longer than " + MaxPrefabLength + ".");
        foreach (var c in prefab) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_')) throw new FormatException("playable prefab key allows a-z, 0-9 and _ only.");
        Prefab = prefab;
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength) throw new FormatException("playable name must be 1.." + MaxNameLength + " characters.");
        description = description ?? "";
        if (description.Length > MaxDescriptionLength) throw new FormatException("playable description is longer than " + MaxDescriptionLength + ".");
        accentRgb = accentRgb ?? new[] { 200, 200, 200 };
        if (accentRgb.Length != 3) throw new FormatException("playable accent needs three numbers.");
        foreach (var v in accentRgb) if (v < 0 || v > 255) throw new FormatException("playable accent values must be 0..255.");
        if (!Enum.IsDefined(typeof(PNPlayableAvailability), availability)) throw new FormatException("playable availability is unknown.");
        Name = name; Description = description; accent = (int[])accentRgb.Clone();
        Art = string.IsNullOrEmpty(art) ? "" : EveResourcePath.Validate(art);
        Availability = availability;
    }

    // Whether a host accepts this character in a room: Forbidden ones only with the testing switch.
    public bool HostAllows(bool allowForbidden) => Availability != PNPlayableAvailability.Forbidden || allowForbidden;

    // data is the character's game-owned data object; null when the character has no `playable` block.
    public static PNPlayableSettings Parse(object data)
    {
        if (data == null || data is JToken empty && empty.Type == JTokenType.Null) return null;
        if (!(data is JObject root)) throw new FormatException("Project_N character data must be an object.");
        var token = root["playable"];
        if (token == null) return null;
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "name", "description", "accent", "art", "availability", "prefab");
        int[] rgb = null;
        if (obj["accent"] != null)
        {
            var array = obj["accent"] as JArray ?? throw new FormatException("playable accent must be an array.");
            if (array.Count != 3) throw new FormatException("playable accent needs three numbers.");
            rgb = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (array[i].Type != JTokenType.Integer) throw new FormatException("playable accent values must be integers.");
                rgb[i] = checked(array[i].Value<int>());
            }
        }
        var availability = PNPlayableAvailability.Default;
        if (obj["availability"] != null)
        {
            switch (PNBuffContentBatch.String(obj, "availability"))
            {
                case "default": break;
                case "locked": availability = PNPlayableAvailability.Locked; break;
                case "forbidden": availability = PNPlayableAvailability.Forbidden; break;
                default: throw new FormatException("playable availability must be default, locked or forbidden.");
            }
        }
        return new PNPlayableSettings(PNBuffContentBatch.String(obj, "name"), PNBuffContentBatch.OptionalString(obj, "description"), rgb,
            PNBuffContentBatch.OptionalString(obj, "art"), availability, PNBuffContentBatch.OptionalString(obj, "prefab"));
    }
}
