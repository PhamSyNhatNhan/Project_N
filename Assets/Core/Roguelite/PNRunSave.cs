using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: A run as it stood on entering a room, ready to be written to disk and read back (Docs/RunSavePlan.md 5.3). Plain data, no Unity: Capture takes it from a PNRunState, ToJson/Parse
//     move it in and out of text, ToState rebuilds the state. A file is data, not trusted: Parse refuses unknown fields, non-finite or out-of-range numbers, bad ids and bad seat counts
//     (it never clamps). `seed` and the three stream positions are written as decimal strings because a 64-bit number does not survive a JSON number.
//     A seat is a person (PNPlayerId), not a session owner: owners change every session, the person does not.
// VI: Run lúc vào phòng, để ghi ra đĩa và đọc lại. Dữ liệu thuần. Tệp là dữ liệu không tin cậy: trường lạ, số sai và id sai bị từ chối. Seed và vị trí luồng ngẫu nhiên ghi bằng chuỗi.
//     Ghế là một người (mã người chơi), không phải owner của phiên.
public sealed class PNRunSaveSeat
{
    public string PlayerId { get; }
    public EveId Character { get; }
    public double Health { get; }
    public bool Dead { get; }
    public PNRunSaveSeat(string playerId, EveId character, double health, bool dead)
    {
        PNPlayerId.Require(playerId);
        if (!character.IsValid) throw new FormatException("a seat needs a character.");
        if (double.IsNaN(health) || double.IsInfinity(health) || health < 0 || health > 1) throw new FormatException("seat health must be a number in [0, 1].");
        PlayerId = playerId; Character = character; Health = health; Dead = dead;
    }
}

public sealed class PNRunSave
{
    public const int SchemaVersion = 1, MaxBytes = 64 * 1024, MaxSeats = 4, MaxLoadout = 64, MaxText = 128, MaxOptions = 3;
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public string RulesVersion { get; private set; }
    public string Content { get; private set; }
    public string Mode { get; private set; }
    public DateTime SavedAtUtc { get; private set; }
    public Guid RunId { get; private set; }
    public EveId Definition { get; private set; }
    public ulong Seed { get; private set; }
    public IReadOnlyList<ulong> Streams { get; private set; }
    public int Floor { get; private set; }
    public int Step { get; private set; }
    public EveId Room { get; private set; }
    public IReadOnlyList<EveId> Options { get; private set; }
    public int Shards { get; private set; }
    public IReadOnlyList<string> Loadout { get; private set; }
    public double Seconds { get; private set; }
    public int Kills { get; private set; }
    public int ShardsEarned { get; private set; }
    public IReadOnlyDictionary<PNRoomKind, int> Cleared { get; private set; }
    public IReadOnlyList<PNRunSaveSeat> Seats { get; private set; }

    private PNRunSave() { }

    // ---- capture ----
    // EN: Take the state of a running run. `rulesVersion` and `content` say what the run was played with (see the compatibility check); `mode` is the play mode id.
    public static PNRunSave Capture(PNRunState state, string rulesVersion, string content, string mode, DateTime utcNow)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (!state.IsRunning) throw new InvalidOperationException("Only a running run is saved.");
        var seats = state.Players.OrderBy(x => x.Owner).Select(x => new PNRunSaveSeat(x.PlayerId, x.Character, x.HealthRatio, x.Dead)).ToList();
        if (seats.Count < 1 || seats.Count > MaxSeats) throw new InvalidOperationException("A saved run has 1-" + MaxSeats + " seats, not " + seats.Count + ".");
        if (seats.Select(x => x.PlayerId).Distinct().Count() != seats.Count) throw new InvalidOperationException("Two seats name the same person.");
        var cleared = new Dictionary<PNRoomKind, int>();
        foreach (PNRoomKind kind in Enum.GetValues(typeof(PNRoomKind))) { int count = state.Stats.RoomsCleared(kind); if (count > 0) cleared[kind] = count; }
        var save = new PNRunSave
        {
            RulesVersion = Text(rulesVersion, "rulesVersion"), Content = Text(content, "content"), Mode = Text(mode, "mode"), SavedAtUtc = Truncate(utcNow),
            RunId = state.RunId, Definition = state.Definition, Seed = state.Random.Seed, Streams = state.Random.Streams(),
            Floor = state.Floor, Step = state.Step, Room = state.Room, Options = state.Options.ToList(), Shards = state.Shards, Loadout = state.Loadout.ToList(),
            Seconds = state.Stats.Seconds, Kills = state.Stats.Kills, ShardsEarned = state.Stats.ShardsEarned, Cleared = cleared, Seats = seats
        };
        save.Validate();
        return save;
    }

    private static DateTime Truncate(DateTime utc) => new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    private static string Text(string value, string what)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxText) throw new FormatException(what + " must be 1-" + MaxText + " characters.");
        foreach (var c in value) if (c < 33 || c > 126) throw new FormatException(what + " must be printable ASCII without spaces.");
        return value;
    }

    private void Validate()
    {
        if (RunId == Guid.Empty) throw new FormatException("run id is required.");
        if (!Definition.IsValid || Definition.ToString().Length > MaxText) throw new FormatException("run definition must be a namespaced id.");
        if (!Room.IsValid || Room.ToString().Length > MaxText) throw new FormatException("room must be a namespaced id.");
        if (Streams == null || Streams.Count != PNRunRandom.StreamCount) throw new FormatException("random must hold " + PNRunRandom.StreamCount + " stream positions.");
        if (Floor < 0 || Step < 0) throw new FormatException("floor and step cannot be negative.");
        if (Options.Count > MaxOptions || Options.Any(x => !x.IsValid || x.ToString().Length > MaxText)) throw new FormatException("options must hold up to " + MaxOptions + " room ids.");
        if (Shards < 0 || Shards > PNRunState.MaxShards) throw new FormatException("shards must be in [0, " + PNRunState.MaxShards + "].");
        if (Loadout.Count > MaxLoadout || Loadout.Any(x => string.IsNullOrEmpty(x) || x.Length > MaxText)) throw new FormatException("loadout must hold up to " + MaxLoadout + " ids.");
        if (double.IsNaN(Seconds) || double.IsInfinity(Seconds) || Seconds < 0) throw new FormatException("seconds must be a finite number from 0.");
        if (Kills < 0 || ShardsEarned < 0 || Cleared.Values.Any(x => x < 0)) throw new FormatException("stats cannot be negative.");
        if (Seats == null || Seats.Count < 1 || Seats.Count > MaxSeats) throw new FormatException("a run has 1-" + MaxSeats + " seats.");
        if (Seats.Select(x => x.PlayerId).Distinct().Count() != Seats.Count) throw new FormatException("two seats name the same person.");
    }

    // ---- rebuild ----
    // EN: The run state for a session. `ownerOf` gives the owner this session assigned to a person (null = that person is not here). A seat nobody takes is left out of the run;
    //     with nobody left the run cannot continue.
    public PNRunState ToState(Func<string, long?> ownerOf)
    {
        if (ownerOf == null) throw new ArgumentNullException(nameof(ownerOf));
        var present = new List<PNRunState.SavedPlayer>(); var owners = new HashSet<long>();
        foreach (var seat in Seats)
        {
            long? owner = ownerOf(seat.PlayerId);
            if (!owner.HasValue) continue;
            if (!owners.Add(owner.Value)) throw new InvalidOperationException("Two seats were given owner " + owner.Value + ".");
            present.Add(new PNRunState.SavedPlayer { Owner = owner.Value, PlayerId = seat.PlayerId, Character = seat.Character, Health = seat.Health, Dead = seat.Dead });
        }
        if (present.Count == 0) throw new InvalidOperationException("No player of the saved run is here.");
        return PNRunState.Restore(RunId, Definition, new PNRunRandom(Seed, Streams), Floor, Step, Room, Options, Shards, Seconds, Kills, ShardsEarned, Cleared, Loadout, present);
    }

    // ---- text ----
    public string ToJson()
    {
        var stats = new JObject { ["seconds"] = Seconds, ["kills"] = Kills, ["shardsEarned"] = ShardsEarned };
        var cleared = new JObject();
        foreach (var pair in Cleared.OrderBy(x => (int)x.Key)) cleared[KindName(pair.Key)] = pair.Value;
        stats["cleared"] = cleared;
        var run = new JObject
        {
            ["id"] = RunId.ToString("D"), ["definition"] = Definition.ToString(), ["seed"] = Seed.ToString(CultureInfo.InvariantCulture),
            ["random"] = new JArray(Streams.Select(x => (object)x.ToString(CultureInfo.InvariantCulture)).ToArray()),
            ["floor"] = Floor, ["step"] = Step, ["room"] = Room.ToString(), ["options"] = new JArray(Options.Select(x => (object)x.ToString()).ToArray()),
            ["shards"] = Shards, ["loadout"] = new JArray(Loadout.Select(x => (object)x).ToArray()), ["stats"] = stats
        };
        var seats = new JArray();
        foreach (var seat in Seats) seats.Add(new JObject { ["player"] = seat.PlayerId, ["character"] = seat.Character.ToString(), ["health"] = seat.Health, ["dead"] = seat.Dead });
        var root = new JObject
        {
            ["schemaVersion"] = SchemaVersion, ["rulesVersion"] = RulesVersion, ["content"] = Content, ["savedAt"] = SavedAtUtc.ToString(TimeFormat, CultureInfo.InvariantCulture),
            ["mode"] = Mode, ["run"] = run, ["seats"] = seats
        };
        return root.ToString(Newtonsoft.Json.Formatting.Indented);
    }

    private static string KindName(PNRoomKind kind) => kind.ToString().ToLowerInvariant();

    // EN: `newer` is true when the text was written by a newer schema (then nothing is read and the result is null). Malformed text throws FormatException.
    public static PNRunSave Parse(string json, out bool newer)
    {
        newer = false;
        if (json == null || json.Length > MaxBytes) throw new FormatException("Run save is missing or too large.");
        JObject root = Load(json);
        if (root["schemaVersion"]?.Type != JTokenType.Integer) throw new FormatException("Run save schemaVersion is required.");
        int version = root["schemaVersion"].Value<int>();
        if (version > SchemaVersion) { newer = true; return null; }
        if (version < 1) throw new FormatException("Run save schemaVersion is invalid.");
        Fields(root, "run save", "schemaVersion", "rulesVersion", "content", "savedAt", "mode", "run", "seats");
        var run = Object(root, "run");
        Fields(run, "run", "id", "definition", "seed", "random", "floor", "step", "room", "options", "shards", "loadout", "stats");
        var stats = Object(run, "stats");
        Fields(stats, "stats", "seconds", "kills", "shardsEarned", "cleared");
        var save = new PNRunSave
        {
            RulesVersion = Text(Str(root, "rulesVersion"), "rulesVersion"), Content = Text(Str(root, "content"), "content"), Mode = Text(Str(root, "mode"), "mode"),
            SavedAtUtc = Time(Str(root, "savedAt")),
            RunId = Guid.TryParseExact(Str(run, "id"), "D", out var id) ? id : throw new FormatException("run id must be a GUID."),
            Definition = Id(Str(run, "definition"), "run definition"), Seed = U64(Str(run, "seed"), "seed"),
            Streams = Array(run, "random").Select(x => U64(x.Type == JTokenType.String ? x.Value<string>() : null, "random")).ToList(),
            Floor = Int(run, "floor"), Step = Int(run, "step"), Room = Id(Str(run, "room"), "room"),
            Options = Array(run, "options").Select(x => Id(x.Type == JTokenType.String ? x.Value<string>() : null, "option")).ToList(),
            Shards = Int(run, "shards"), Loadout = Array(run, "loadout").Select(x => x.Type == JTokenType.String ? x.Value<string>() : throw new FormatException("loadout entries are strings.")).ToList(),
            Seconds = Number(stats, "seconds"), Kills = Int(stats, "kills"), ShardsEarned = Int(stats, "shardsEarned"), Cleared = ReadCleared(Object(stats, "cleared"))
        };
        var seats = new List<PNRunSaveSeat>();
        foreach (var token in Array(root, "seats"))
        {
            var seat = token as JObject ?? throw new FormatException("a seat must be an object.");
            Fields(seat, "seat", "player", "character", "health", "dead");
            seats.Add(new PNRunSaveSeat(Str(seat, "player"), Id(Str(seat, "character"), "seat character"), Number(seat, "health"), Bool(seat, "dead")));
        }
        save.Seats = seats;
        save.Validate();
        return save;
    }

    // Dates stay text (the default reader turns "2026-11-04T10:00:00Z" into a date token), and nothing may follow the object.
    internal static JObject Load(string json)
    {
        try
        {
            using (var reader = new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(json)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
            {
                var root = JObject.Load(reader);
                while (reader.Read()) if (reader.TokenType != Newtonsoft.Json.JsonToken.Comment) throw new FormatException("Run save has text after its object.");
                return root;
            }
        }
        catch (Newtonsoft.Json.JsonException e) { throw new FormatException("Run save is not valid JSON: " + e.Message); }
    }

    // ---- parse helpers ----
    private static void Fields(JObject obj, string what, params string[] allowed)
    {
        foreach (var property in obj.Properties()) if (System.Array.IndexOf(allowed, property.Name) < 0) throw new FormatException(what + " has an unknown field \"" + property.Name + "\".");
        foreach (var name in allowed) if (obj[name] == null) throw new FormatException(what + " field \"" + name + "\" is required.");
    }
    private static JObject Object(JObject obj, string field) => obj[field] as JObject ?? throw new FormatException(field + " must be an object.");
    private static JArray Array(JObject obj, string field) => obj[field] as JArray ?? throw new FormatException(field + " must be an array.");
    private static string Str(JObject obj, string field) => obj[field]?.Type == JTokenType.String ? obj.Value<string>(field) : throw new FormatException(field + " must be a string.");
    private static bool Bool(JObject obj, string field) => obj[field]?.Type == JTokenType.Boolean ? obj.Value<bool>(field) : throw new FormatException(field + " must be true or false.");
    private static int Int(JObject obj, string field)
    {
        if (obj[field]?.Type != JTokenType.Integer) throw new FormatException(field + " must be an integer.");
        long value = obj.Value<long>(field);
        if (value < int.MinValue || value > int.MaxValue) throw new FormatException(field + " is out of range.");
        return (int)value;
    }
    private static double Number(JObject obj, string field)
    {
        var type = obj[field]?.Type;
        if (type != JTokenType.Float && type != JTokenType.Integer) throw new FormatException(field + " must be a number.");
        double value = obj.Value<double>(field);
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException(field + " must be finite.");
        return value;
    }
    private static EveId Id(string text, string what)
    {
        if (text == null || text.Length > MaxText || !EveId.TryParse(text, out var id)) throw new FormatException(what + " must be a namespaced id.");
        return id;
    }
    private static ulong U64(string text, string what)
    {
        if (text == null || text.Length == 0 || text.Length > 20 || text.Any(c => c < '0' || c > '9') || !ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new FormatException(what + " must be an unsigned 64-bit number written as text.");
        return value;
    }
    private static DateTime Time(string text)
    {
        if (!DateTime.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)) throw new FormatException("savedAt must look like 2026-11-04T10:00:00Z.");
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
    private static Dictionary<PNRoomKind, int> ReadCleared(JObject obj)
    {
        var result = new Dictionary<PNRoomKind, int>();
        foreach (var property in obj.Properties())
        {
            var kind = Enum.GetValues(typeof(PNRoomKind)).Cast<PNRoomKind>().Where(x => KindName(x) == property.Name).Select(x => (PNRoomKind?)x).FirstOrDefault()
                ?? throw new FormatException("cleared has an unknown room kind \"" + property.Name + "\".");
            if (property.Value.Type != JTokenType.Integer || property.Value.Value<long>() < 0 || property.Value.Value<long>() > int.MaxValue) throw new FormatException("cleared counts are integers from 0.");
            result[kind] = property.Value.Value<int>();
        }
        return result;
    }
}
