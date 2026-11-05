using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The three definitions of the roguelite frame (Docs/RogueliteFramePlan.md section 5): a run (floors in order, rules), a floor (steps, each offering rooms by weight) and a
//     room (kind, map world, how it starts, waves, how it clears, reward, exits). All are immutable content read from `extensions.project_n`
//     (rogueliteRuns / rogueliteFloors / rogueliteRooms). Unknown fields are refused, every number is finite and inside the hard limits (refused, never clamped).
//     A room is made from JSON alone; a map (a world with its scene and navigation) is code and Unity work.
// VI: Ba định nghĩa của khung roguelite: run, tầng, phòng. Bất biến, đọc từ JSON; trường lạ bị từ chối, số phải hữu hạn trong giới hạn cứng.
public static class PNRogueliteLimits
{
    public const int RunFloors = 16, FloorSteps = 32, StepChoices = 3, StepRooms = 16, RoomWaves = 16, WaveSpawns = 16, SpawnCount = 16, WaveEnemies = 32, RoomExits = 3, RoomInteractables = 16, FloorEnemyBuffs = 8, MaxShards = 100000, MaxDefinitionsPerDocument = 1024, MaxText = 128;
    public const double MaxCountdown = 10, MaxAutoDelay = 60, MaxWaveDelay = 30, MaxWeight = 1000, DefaultCountdown = 3;
    public static readonly EveId DefaultEntity = EveId.Parse("project_n:enemy");
}

public enum PNRoomKind { Combat, Boss, Rest, Shop, Event }
public enum PNRoomStartMode { Interact, Auto, None }
public enum PNRoomClear { AllWaves, None }
public enum PNRunVictory { LastRoomCleared }

internal static class PNRogueliteParse
{
    public static T Enum<T>(JObject obj, string field, params (string text, T value)[] map)
    {
        string text = PNBuffContentBatch.String(obj, field);
        foreach (var pair in map) if (pair.text == text) return pair.value;
        throw new FormatException(field + " must be one of " + string.Join(", ", map.Select(x => x.text)) + ".");
    }
    public static int IntegerIn(JObject obj, string field, int min, int max)
    {
        int value = PNBuffContentBatch.Integer(obj, field);
        if (value < min || value > max) throw new FormatException(field + " must be an integer in [" + min + ", " + max + "].");
        return value;
    }
    public static double NumberIn(JObject obj, string field, double min, double max, bool minExclusive = false)
    {
        double value = PNBuffContentBatch.Number(obj, field);
        if (value > max || (minExclusive ? value <= min : value < min)) throw new FormatException(field + " must be a number " + (minExclusive ? "above " : "from ") + min + " up to " + max + ".");
        return value;
    }
    public static JArray ArrayIn(JObject obj, string field, int min, int max)
    {
        var array = obj[field] as JArray ?? throw new FormatException(field + " must be an array.");
        if (array.Count < min || array.Count > max) throw new FormatException(field + " must hold " + min + "-" + max + " entries.");
        return array;
    }
    public static EveId Id(JToken token, string what)
    {
        if (token == null || token.Type != JTokenType.String || !EveId.TryParse(token.Value<string>(), out var id) || id.ToString().Length > PNRogueliteLimits.MaxText) throw new FormatException(what + " must be a namespaced id.");
        return id;
    }
    public static IReadOnlyList<string> Keys(JObject obj, string field, int min, int max, string what)
    {
        var array = ArrayIn(obj, field, min, max);
        var keys = new List<string>();
        foreach (var item in array)
        {
            if (item.Type != JTokenType.String) throw new FormatException(what + " keys must be strings.");
            keys.Add(PNSyncKey.Require(item.Value<string>(), what + " key"));
        }
        if (keys.Distinct().Count() != keys.Count) throw new FormatException(what + " keys must be unique.");
        return keys.AsReadOnly();
    }
}

// EN: How a room begins. interact: the player at the host presses the room's start switch (key `interactable`), then `countdown` seconds pass. auto: `delay` seconds after
//     entering, then `countdown`. none: the room acts at once (only for rooms without combat).
public sealed class PNRoomStart
{
    public PNRoomStartMode Mode { get; }
    public string Interactable { get; }
    public double Countdown { get; }
    public double Delay { get; }
    public PNRoomStart(PNRoomStartMode mode, string interactable, double countdown, double delay)
    {
        if (!System.Enum.IsDefined(typeof(PNRoomStartMode), mode)) throw new FormatException("room start mode is unknown.");
        if (double.IsNaN(countdown) || double.IsInfinity(countdown) || countdown < 0 || countdown > PNRogueliteLimits.MaxCountdown) throw new FormatException("room start countdown must be in [0, " + PNRogueliteLimits.MaxCountdown + "].");
        if (double.IsNaN(delay) || double.IsInfinity(delay) || delay < 0 || delay > PNRogueliteLimits.MaxAutoDelay) throw new FormatException("room start delay must be in [0, " + PNRogueliteLimits.MaxAutoDelay + "].");
        if (mode == PNRoomStartMode.Interact) PNSyncKey.Require(interactable, "room start interactable");
        else if (!string.IsNullOrEmpty(interactable)) throw new FormatException("room start interactable is only for mode interact.");
        if (mode != PNRoomStartMode.Auto && delay != 0) throw new FormatException("room start delay is only for mode auto.");
        if (mode == PNRoomStartMode.None && countdown != 0) throw new FormatException("a room that starts at once has no countdown.");
        Mode = mode; Interactable = interactable ?? ""; Countdown = countdown; Delay = delay;
    }
    public static PNRoomStart Parse(JToken token)
    {
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "mode", "interactable", "countdown", "delay");
        var mode = PNRogueliteParse.Enum(obj, "mode", ("interact", PNRoomStartMode.Interact), ("auto", PNRoomStartMode.Auto), ("none", PNRoomStartMode.None));
        double countdown = obj["countdown"] != null ? PNRogueliteParse.NumberIn(obj, "countdown", 0, PNRogueliteLimits.MaxCountdown) : mode == PNRoomStartMode.None ? 0 : PNRogueliteLimits.DefaultCountdown;
        double delay = obj["delay"] != null ? PNRogueliteParse.NumberIn(obj, "delay", 0, PNRogueliteLimits.MaxAutoDelay) : 0;
        return new PNRoomStart(mode, PNBuffContentBatch.OptionalString(obj, "interactable"), countdown, delay);
    }
}

public sealed class PNRoomSpawn
{
    public EveId Character { get; }
    public EveId Entity { get; }
    public int Count { get; }
    public string Group { get; }
    public PNRoomSpawn(EveId character, EveId entity, int count, string group)
    {
        if (!character.IsValid || !entity.IsValid) throw new FormatException("a spawn needs a character and an entity role.");
        if (count < 1 || count > PNRogueliteLimits.SpawnCount) throw new FormatException("a spawn count is 1-" + PNRogueliteLimits.SpawnCount + ".");
        Character = character; Entity = entity; Count = count; Group = PNSyncKey.Require(group, "spawn group");
    }
    public static PNRoomSpawn Parse(JToken token)
    {
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "character", "entity", "count", "group");
        foreach (var required in new[] { "character", "count", "group" }) if (obj[required] == null) throw new FormatException("spawn " + required + " is required.");
        return new PNRoomSpawn(PNRogueliteParse.Id(obj["character"], "spawn character"), obj["entity"] != null ? PNRogueliteParse.Id(obj["entity"], "spawn entity") : PNRogueliteLimits.DefaultEntity,
            PNRogueliteParse.IntegerIn(obj, "count", 1, PNRogueliteLimits.SpawnCount), PNBuffContentBatch.String(obj, "group"));
    }
}

public sealed class PNRoomWave
{
    public double Delay { get; }
    public IReadOnlyList<PNRoomSpawn> Spawns { get; }
    public int Total { get; }
    public PNRoomWave(double delay, IEnumerable<PNRoomSpawn> spawns)
    {
        if (double.IsNaN(delay) || double.IsInfinity(delay) || delay < 0 || delay > PNRogueliteLimits.MaxWaveDelay) throw new FormatException("wave delay must be in [0, " + PNRogueliteLimits.MaxWaveDelay + "].");
        var list = (spawns ?? throw new ArgumentNullException(nameof(spawns))).ToList();
        if (list.Count < 1 || list.Count > PNRogueliteLimits.WaveSpawns) throw new FormatException("a wave holds 1-" + PNRogueliteLimits.WaveSpawns + " spawns.");
        int total = 0; foreach (var spawn in list) total += spawn.Count;
        if (total > PNRogueliteLimits.WaveEnemies) throw new FormatException("a wave spawns at most " + PNRogueliteLimits.WaveEnemies + " enemies.");
        Delay = delay; Spawns = list.AsReadOnly(); Total = total;
    }
    public static PNRoomWave Parse(JToken token)
    {
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "delay", "spawns");
        return new PNRoomWave(obj["delay"] != null ? PNRogueliteParse.NumberIn(obj, "delay", 0, PNRogueliteLimits.MaxWaveDelay) : 0,
            PNRogueliteParse.ArrayIn(obj, "spawns", 1, PNRogueliteLimits.WaveSpawns).Select(PNRoomSpawn.Parse));
    }
}

public sealed class PNRoomReward
{
    public int Shards { get; }
    public PNRoomReward(int shards)
    { if (shards < 0 || shards > PNRogueliteLimits.MaxShards) throw new FormatException("reward shards must be an integer in [0, " + PNRogueliteLimits.MaxShards + "]."); Shards = shards; }
    public static PNRoomReward Parse(JToken token)
    {
        if (token == null) return new PNRoomReward(0);
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "shards");
        return new PNRoomReward(obj["shards"] != null ? PNRogueliteParse.IntegerIn(obj, "shards", 0, PNRogueliteLimits.MaxShards) : 0);
    }
}

public sealed class PNRoomDefinition : EveIIdentified
{
    public EveId Id { get; }
    public PNRoomKind Kind { get; }
    public EveId World { get; }
    public PNRoomStart Start { get; }
    public IReadOnlyList<PNRoomWave> Waves { get; }
    public PNRoomClear Clear { get; }
    public PNRoomReward Reward { get; }
    public IReadOnlyList<string> Exits { get; }
    public IReadOnlyList<string> Interactables { get; }
    public bool HasCombat => Kind == PNRoomKind.Combat || Kind == PNRoomKind.Boss;

    public PNRoomDefinition(EveId id, PNRoomKind kind, EveId world, PNRoomStart start, IEnumerable<PNRoomWave> waves, PNRoomClear clear, PNRoomReward reward, IEnumerable<string> exits, IEnumerable<string> interactables)
    {
        if (!id.IsValid || id.ToString().Length > PNRogueliteLimits.MaxText) throw new FormatException("a room needs a valid id.");
        if (!System.Enum.IsDefined(typeof(PNRoomKind), kind) || !System.Enum.IsDefined(typeof(PNRoomClear), clear)) throw new FormatException("room kind or clear is unknown.");
        if (!world.IsValid) throw new FormatException("a room needs a world.");
        Id = id; Kind = kind; World = world; Start = start ?? throw new ArgumentNullException(nameof(start)); Clear = clear; Reward = reward ?? throw new ArgumentNullException(nameof(reward));
        Waves = (waves ?? throw new ArgumentNullException(nameof(waves))).ToList().AsReadOnly();
        Exits = (exits ?? throw new ArgumentNullException(nameof(exits))).ToList().AsReadOnly();
        Interactables = (interactables ?? Enumerable.Empty<string>()).ToList().AsReadOnly();
        if (Waves.Count > PNRogueliteLimits.RoomWaves) throw new FormatException("a room holds at most " + PNRogueliteLimits.RoomWaves + " waves.");
        if (Exits.Count < 1 || Exits.Count > PNRogueliteLimits.RoomExits) throw new FormatException("a room has 1-" + PNRogueliteLimits.RoomExits + " exits.");
        if (Interactables.Count > PNRogueliteLimits.RoomInteractables) throw new FormatException("a room lists at most " + PNRogueliteLimits.RoomInteractables + " interactables.");
        if (HasCombat)
        {
            if (Start.Mode == PNRoomStartMode.None) throw new FormatException("a combat or boss room starts by interact or auto.");
            if (Waves.Count < 1) throw new FormatException("a combat or boss room has at least one wave.");
            if (Clear != PNRoomClear.AllWaves) throw new FormatException("a combat or boss room clears when all waves are done.");
        }
        else
        {
            if (Start.Mode != PNRoomStartMode.None) throw new FormatException("a room without combat starts at once (mode none).");
            if (Waves.Count != 0) throw new FormatException("a room without combat has no waves.");
            if (Clear != PNRoomClear.None) throw new FormatException("a room without combat has clear none.");
        }
        var all = new List<string>(Exits); all.AddRange(Interactables); if (Start.Interactable.Length > 0) all.Add(Start.Interactable);
        if (all.Distinct().Count() != all.Count) throw new FormatException("a key is used twice among the exits, interactables and start switch of room " + id + ".");
    }

    public static PNRoomDefinition Parse(JObject obj, EveId id)
    {
        PNBuffContentBatch.Fields(obj, "id", "kind", "world", "start", "waves", "clear", "reward", "exits", "interactables");
        foreach (var required in new[] { "kind", "world", "start", "clear", "exits" }) if (obj[required] == null) throw new FormatException("room " + required + " is required.");
        var kind = PNRogueliteParse.Enum(obj, "kind", ("combat", PNRoomKind.Combat), ("boss", PNRoomKind.Boss), ("rest", PNRoomKind.Rest), ("shop", PNRoomKind.Shop), ("event", PNRoomKind.Event));
        var clear = PNRogueliteParse.Enum(obj, "clear", ("all_waves", PNRoomClear.AllWaves), ("none", PNRoomClear.None));
        var waves = obj["waves"] == null ? Enumerable.Empty<PNRoomWave>() : PNRogueliteParse.ArrayIn(obj, "waves", 0, PNRogueliteLimits.RoomWaves).Select(PNRoomWave.Parse);
        var interactables = obj["interactables"] == null ? (IReadOnlyList<string>)new string[0] : PNRogueliteParse.Keys(obj, "interactables", 0, PNRogueliteLimits.RoomInteractables, "room interactable");
        return new PNRoomDefinition(id, kind, PNRogueliteParse.Id(obj["world"], "room world"), PNRoomStart.Parse(obj["start"]), waves.ToList(), clear, PNRoomReward.Parse(obj["reward"]),
            PNRogueliteParse.Keys(obj, "exits", 1, PNRogueliteLimits.RoomExits, "room exit"), interactables);
    }
}

public sealed class PNFloorRoom
{
    public EveId Room { get; }
    public double Weight { get; }
    public PNFloorRoom(EveId room, double weight)
    {
        if (!room.IsValid) throw new FormatException("a step entry needs a room.");
        if (double.IsNaN(weight) || double.IsInfinity(weight) || weight <= 0 || weight > PNRogueliteLimits.MaxWeight) throw new FormatException("a room weight is above 0 and at most " + PNRogueliteLimits.MaxWeight + ".");
        Room = room; Weight = weight;
    }
}

public sealed class PNFloorStep
{
    public int Choices { get; }
    public IReadOnlyList<PNFloorRoom> Rooms { get; }
    public PNFloorStep(int choices, IEnumerable<PNFloorRoom> rooms)
    {
        var list = (rooms ?? throw new ArgumentNullException(nameof(rooms))).ToList();
        if (list.Count < 1 || list.Count > PNRogueliteLimits.StepRooms) throw new FormatException("a step offers 1-" + PNRogueliteLimits.StepRooms + " rooms.");
        if (list.Select(x => x.Room).Distinct().Count() != list.Count) throw new FormatException("a step lists each room once.");
        if (choices < 1 || choices > PNRogueliteLimits.StepChoices) throw new FormatException("a step gives 1-" + PNRogueliteLimits.StepChoices + " choices.");
        if (choices > list.Count) throw new FormatException("a step cannot give more choices (" + choices + ") than it has rooms (" + list.Count + ").");
        Choices = choices; Rooms = list.AsReadOnly();
    }
    public static PNFloorStep Parse(JToken token)
    {
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "choices", "rooms");
        if (obj["choices"] == null) throw new FormatException("step choices is required.");
        var rooms = PNRogueliteParse.ArrayIn(obj, "rooms", 1, PNRogueliteLimits.StepRooms).Select(entry =>
        {
            var e = PNBuffContentBatch.Object(entry);
            PNBuffContentBatch.Fields(e, "room", "weight");
            if (e["room"] == null || e["weight"] == null) throw new FormatException("a step entry needs room and weight.");
            return new PNFloorRoom(PNRogueliteParse.Id(e["room"], "step room"), PNBuffContentBatch.Number(e, "weight"));
        });
        return new PNFloorStep(PNRogueliteParse.IntegerIn(obj, "choices", 1, PNRogueliteLimits.StepChoices), rooms);
    }
}

public sealed class PNFloorDefinition : EveIIdentified
{
    public EveId Id { get; }
    public IReadOnlyList<PNFloorStep> Steps { get; }
    public IReadOnlyList<EveId> EnemyBuffs { get; }
    public PNFloorDefinition(EveId id, IEnumerable<PNFloorStep> steps, IEnumerable<EveId> enemyBuffs)
    {
        if (!id.IsValid || id.ToString().Length > PNRogueliteLimits.MaxText) throw new FormatException("a floor needs a valid id.");
        var list = (steps ?? throw new ArgumentNullException(nameof(steps))).ToList();
        if (list.Count < 1 || list.Count > PNRogueliteLimits.FloorSteps) throw new FormatException("a floor has 1-" + PNRogueliteLimits.FloorSteps + " steps.");
        var buffs = (enemyBuffs ?? Enumerable.Empty<EveId>()).ToList();
        if (buffs.Count > PNRogueliteLimits.FloorEnemyBuffs || buffs.Any(x => !x.IsValid) || buffs.Distinct().Count() != buffs.Count) throw new FormatException("floor enemyBuffs are up to " + PNRogueliteLimits.FloorEnemyBuffs + " unique valid ids.");
        Id = id; Steps = list.AsReadOnly(); EnemyBuffs = buffs.AsReadOnly();
    }
    public static PNFloorDefinition Parse(JObject obj, EveId id)
    {
        PNBuffContentBatch.Fields(obj, "id", "steps", "enemyBuffs");
        var buffs = obj["enemyBuffs"] == null ? new List<EveId>() : PNRogueliteParse.ArrayIn(obj, "enemyBuffs", 0, PNRogueliteLimits.FloorEnemyBuffs).Select(x => PNRogueliteParse.Id(x, "enemy buff")).ToList();
        return new PNFloorDefinition(id, PNRogueliteParse.ArrayIn(obj, "steps", 1, PNRogueliteLimits.FloorSteps).Select(PNFloorStep.Parse), buffs);
    }
}

public sealed class PNRunDefinition : EveIIdentified
{
    public EveId Id { get; }
    public string Name { get; }
    public IReadOnlyList<EveId> Floors { get; }
    public int StartShards { get; }
    public double ReviveHealth { get; }
    public PNRunVictory Victory { get; }
    // How the map is shown where it is chosen (Docs/RunSavePlan.md 5.2): `Art` is a Resources path like a playable character's art, `Description` a localization key; both may be empty.
    // A hidden run is not offered on the map screen (an internal or test run); every other run needs a name.
    public string Art { get; }
    public string Description { get; }
    public bool Hidden { get; }
    // What a run of this map pays in stella when it is summed up (Docs/StellaPlan.md); null = nothing.
    public PNStellaRule Stella { get; }
    public PNRunDefinition(EveId id, string name, IEnumerable<EveId> floors, int startShards, double reviveHealth, PNRunVictory victory, string art = "", string description = "", bool hidden = false, PNStellaRule stella = null)
    {
        if (!id.IsValid || id.ToString().Length > PNRogueliteLimits.MaxText) throw new FormatException("a run needs a valid id.");
        name = name ?? "";
        if (name.Length > PNRogueliteLimits.MaxText || name.Length > 0 && !EveId.TryParse(name, out _)) throw new FormatException("a run name is a namespaced localization key of at most " + PNRogueliteLimits.MaxText + " characters.");
        var list = (floors ?? throw new ArgumentNullException(nameof(floors))).ToList();
        if (list.Count < 1 || list.Count > PNRogueliteLimits.RunFloors || list.Any(x => !x.IsValid) || list.Distinct().Count() != list.Count) throw new FormatException("a run lists 1-" + PNRogueliteLimits.RunFloors + " unique floors.");
        if (startShards < 0 || startShards > PNRogueliteLimits.MaxShards) throw new FormatException("startShards must be an integer in [0, " + PNRogueliteLimits.MaxShards + "].");
        if (double.IsNaN(reviveHealth) || double.IsInfinity(reviveHealth) || reviveHealth <= 0 || reviveHealth > 1) throw new FormatException("reviveHealth must be above 0 and at most 1.");
        if (!System.Enum.IsDefined(typeof(PNRunVictory), victory)) throw new FormatException("run victory is unknown.");
        description = description ?? "";
        if (description.Length > PNRogueliteLimits.MaxText || description.Length > 0 && !EveId.TryParse(description, out _)) throw new FormatException("a run description is a namespaced localization key of at most " + PNRogueliteLimits.MaxText + " characters.");
        if (!hidden && name.Length == 0) throw new FormatException("a run that is not hidden needs a name (it is offered on the map screen).");
        Id = id; Name = name; Floors = list.AsReadOnly(); StartShards = startShards; ReviveHealth = reviveHealth; Victory = victory;
        Art = string.IsNullOrEmpty(art) ? "" : EveResourcePath.Validate(art); Description = description; Hidden = hidden; Stella = stella;
    }
    public static PNRunDefinition Parse(JObject obj, EveId id)
    {
        PNBuffContentBatch.Fields(obj, "id", "name", "floors", "startShards", "reviveHealth", "victory", "art", "description", "hidden", "stella");
        if (obj["hidden"] != null && obj["hidden"].Type != JTokenType.Boolean) throw new FormatException("run hidden must be true or false.");
        if (obj["stella"] != null && !(obj["stella"] is JObject)) throw new FormatException("run stella must be an object.");
        foreach (var required in new[] { "floors", "startShards", "reviveHealth", "victory" }) if (obj[required] == null) throw new FormatException("run " + required + " is required.");
        return new PNRunDefinition(id, PNBuffContentBatch.OptionalString(obj, "name"), PNRogueliteParse.ArrayIn(obj, "floors", 1, PNRogueliteLimits.RunFloors).Select(x => PNRogueliteParse.Id(x, "run floor")),
            PNRogueliteParse.IntegerIn(obj, "startShards", 0, PNRogueliteLimits.MaxShards), PNBuffContentBatch.Number(obj, "reviveHealth"),
            PNRogueliteParse.Enum(obj, "victory", ("last_room_cleared", PNRunVictory.LastRoomCleared)),
            PNBuffContentBatch.OptionalString(obj, "art"), PNBuffContentBatch.OptionalString(obj, "description"), obj["hidden"] != null && obj.Value<bool>("hidden"),
            obj["stella"] is JObject stella ? PNStellaRule.Parse(stella) : null);
    }
}
