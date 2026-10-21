using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public enum PNCharacterState { Owned, NotOwned, Forbidden }

// EN: The machine's single player profile (Docs/ModeMenuPlan.md section 7): the last chosen character and a per-character state.
//     It never holds definitions. Ids it does not know (a disabled mod) are kept and written back untouched.
//     JSON: { "schemaVersion": 1, "lastCharacter": "project_n:eve", "characters": { "mod:vex": "owned" } }  (states: owned | not_owned | forbidden)
// VI: Hồ sơ người chơi duy nhất của máy: nhân vật chọn lần trước và trạng thái từng nhân vật; id lạ được giữ nguyên.
public sealed class PNPlayerProfile
{
    public const int SchemaVersion = 1, MaxBytes = 64 * 1024, MaxEntries = 512, MaxIdLength = 128;
    private readonly Dictionary<string, PNCharacterState> states = new Dictionary<string, PNCharacterState>(StringComparer.Ordinal);
    public string LastCharacter { get; private set; } = "";
    public int Count => states.Count;

    public static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength || id.IndexOf(':') <= 0 || id.IndexOf(':') == id.Length - 1) throw new FormatException("Character id must be 'namespace:name' (max " + MaxIdLength + ").");
        foreach (var c in id) if (c < 33 || c > 126) throw new FormatException("Character id must be printable ASCII without spaces.");
    }

    public bool TryGetState(string id, out PNCharacterState state) => states.TryGetValue(id ?? "", out state);

    public void SetState(string id, PNCharacterState state)
    {
        ValidateId(id);
        if (!Enum.IsDefined(typeof(PNCharacterState), state)) throw new ArgumentOutOfRangeException(nameof(state));
        if (!states.ContainsKey(id) && states.Count >= MaxEntries) throw new InvalidOperationException("Profile character limit reached.");
        states[id] = state;
    }

    public bool ClearState(string id) => id != null && states.Remove(id);

    public void SetLastCharacter(string id)
    {
        if (!string.IsNullOrEmpty(id)) ValidateId(id);
        LastCharacter = id ?? "";
    }

    // EN: Effective state: content Forbidden or profile Forbidden wins (the profile cannot undo a content ban); otherwise the profile;
    //     otherwise the content default (default -> Owned, locked -> NotOwned). No `playable` block = never playable (NotOwned).
    //     `allowForbidden` (Editor/Development flag) turns Forbidden into Owned for testing.
    public PNCharacterState Effective(string id, PNPlayableSettings playable, bool allowForbidden = false)
    {
        if (playable == null) return PNCharacterState.NotOwned;
        TryGetState(id, out var stored);
        bool known = states.ContainsKey(id ?? "");
        if (playable.Availability == PNPlayableAvailability.Forbidden || known && stored == PNCharacterState.Forbidden)
            return allowForbidden ? PNCharacterState.Owned : PNCharacterState.Forbidden;
        if (known) return stored;
        return playable.Availability == PNPlayableAvailability.Locked ? PNCharacterState.NotOwned : PNCharacterState.Owned;
    }

    public bool CanPlay(string id, PNPlayableSettings playable, bool allowForbidden = false) => Effective(id, playable, allowForbidden) == PNCharacterState.Owned;

    private static string Name(PNCharacterState s) => s == PNCharacterState.Owned ? "owned" : s == PNCharacterState.NotOwned ? "not_owned" : "forbidden";

    public string ToJson()
    {
        var characters = new JObject();
        var ids = new List<string>(states.Keys); ids.Sort(StringComparer.Ordinal);
        foreach (var id in ids) characters[id] = Name(states[id]);
        var root = new JObject { ["schemaVersion"] = SchemaVersion, ["lastCharacter"] = LastCharacter, ["characters"] = characters };
        return root.ToString(Newtonsoft.Json.Formatting.Indented);
    }

    // Throws FormatException for malformed text; `newer` is true when the file was written by a newer schema (then nothing is read).
    public static PNPlayerProfile Parse(string json, out bool newer)
    {
        newer = false;
        if (json == null || json.Length > MaxBytes) throw new FormatException("Profile is missing or too large.");
        JObject root;
        try { root = JObject.Parse(json); } catch (Newtonsoft.Json.JsonException e) { throw new FormatException("Profile is not valid JSON: " + e.Message); }
        if (root["schemaVersion"]?.Type != JTokenType.Integer) throw new FormatException("Profile schemaVersion is required.");
        int version = root["schemaVersion"].Value<int>();
        if (version > SchemaVersion) { newer = true; return new PNPlayerProfile(); }
        if (version < 1) throw new FormatException("Profile schemaVersion is invalid.");
        var profile = new PNPlayerProfile();
        if (root["lastCharacter"] != null)
        {
            if (root["lastCharacter"].Type != JTokenType.String) throw new FormatException("lastCharacter must be a string.");
            profile.SetLastCharacter(root["lastCharacter"].Value<string>());
        }
        if (root["characters"] != null)
        {
            var map = root["characters"] as JObject ?? throw new FormatException("characters must be an object.");
            foreach (var p in map.Properties())
            {
                if (p.Value.Type != JTokenType.String) throw new FormatException("Character state must be a string.");
                PNCharacterState state;
                switch (p.Value.Value<string>())
                {
                    case "owned": state = PNCharacterState.Owned; break;
                    case "not_owned": state = PNCharacterState.NotOwned; break;
                    case "forbidden": state = PNCharacterState.Forbidden; break;
                    default: throw new FormatException("Unknown character state.");
                }
                profile.SetState(p.Name, state);
            }
        }
        return profile;
    }
}
