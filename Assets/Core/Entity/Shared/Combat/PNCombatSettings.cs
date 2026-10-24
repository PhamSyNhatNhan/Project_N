using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The `combat` block of a character's game-owned data. Field: targetLayers (required, ordered array of layer names, [] = targets nothing).
//     Absent block = the stat class default. Unknown fields are rejected. Example: "combat": { "targetLayers": ["Enemy"] }.
// VI: Khối `combat` trong dữ liệu character. Trường: targetLayers (bắt buộc, mảng tên layer có thứ tự, [] = không nhắm gì). Vắng khối = mặc định theo lớp stat.
public sealed class PNCombatSettings
{
    public PNTargetLayers TargetLayers { get; }
    public PNCombatSettings(PNTargetLayers targetLayers) { TargetLayers = targetLayers ?? throw new ArgumentNullException(nameof(targetLayers)); }

    // data is the character's game-owned data object; null when the character has no `combat` block.
    public static PNCombatSettings Parse(object data)
    {
        if (data == null || data is JToken empty && empty.Type == JTokenType.Null) return null;
        if (!(data is JObject root)) throw new FormatException("Project_N character data must be an object.");
        var token = root["combat"];
        if (token == null) return null;
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "targetLayers");
        if (obj["targetLayers"] == null) throw new FormatException("combat.targetLayers is required.");
        return new PNCombatSettings(PNTargetLayers.Parse(obj["targetLayers"], "combat.targetLayers"));
    }
}
