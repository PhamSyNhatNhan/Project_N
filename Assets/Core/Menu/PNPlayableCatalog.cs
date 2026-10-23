using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EveEngine;

public sealed class PNPlayableEntry
{
    public string Id { get; }
    public PNPlayableSettings Settings { get; }
    public EveCharacterDefinition Definition { get; }
    internal PNPlayableEntry(string id, PNPlayableSettings settings, EveCharacterDefinition definition) { Id = id; Settings = settings; Definition = definition; }
}

public struct PNStatRow { public string Label, Value, LocalizationKey; }

// EN: The characters the character screen lists (Docs/ModeMenuPlan.md M7): those with a `data.playable` block whose effective state is Owned
//     (NotOwned and Forbidden stay hidden; the allow switch turns Forbidden into Owned), shipped content first, then by name. Also the
//     base-stat rows shown on the right of that screen (numbers only, taken from the character's own base values).
// VI: Danh sách nhân vật màn nhân vật hiện (có khối playable và đang Owned) và các dòng chỉ số gốc.
public static class PNPlayableCatalog
{
    public static IReadOnlyList<PNPlayableEntry> Playable(IEnumerable<EveCharacterDefinition> definitions, PNPlayerProfile profile, bool allowForbidden)
    {
        if (definitions == null) throw new ArgumentNullException(nameof(definitions));
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        var list = new List<PNPlayableEntry>();
        foreach (var definition in definitions)
        {
            var settings = PNPlayableSettings.Parse(definition.Data);
            if (settings == null) continue;
            string id = definition.Id.ToString();
            if (profile.Effective(id, settings, allowForbidden) != PNCharacterState.Owned) continue;
            list.Add(new PNPlayableEntry(id, settings, definition));
        }
        return list.OrderBy(e => e.Id.StartsWith("project_n:", StringComparison.Ordinal) || e.Id.StartsWith("base:", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(e => e.Settings.Name, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal).ToList().AsReadOnly();
    }

    // Which entry the screen starts on: the last character when it is still listed, otherwise the first.
    public static int StartIndex(IReadOnlyList<PNPlayableEntry> list, string lastCharacter)
    {
        for (int i = 0; i < list.Count; i++) if (list[i].Id == lastCharacter) return i;
        return 0;
    }

    private static readonly string[][] Rows =
    {
        new[] { "health", "Máu", "" }, new[] { "attack_power", "Tấn công", "" }, new[] { "defense", "Phòng thủ", "" },
        new[] { "crit_rate", "Tỉ lệ chí mạng", "%" }, new[] { "crit_damage", "Sát thương chí mạng", "%" },
        new[] { "cooldown_reduction", "Giảm hồi chiêu", "%" },
    };

    public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // The base stats shown, in a fixed order; a stat the character does not define shows 0.
    public static IReadOnlyList<PNStatRow> StatRows(EveCharacterDefinition definition)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        var rows = new List<PNStatRow>();
        foreach (var row in Rows)
        {
            double value = 0;
            foreach (var stat in definition.Stats) if (stat.Id.Path == row[0]) { value = stat.BaseValue; break; }
            rows.Add(new PNStatRow { Label = row[1], Value = Number(value) + row[2], LocalizationKey="project_n:stat."+row[0]+".name" });
        }
        return rows.AsReadOnly();
    }
}
