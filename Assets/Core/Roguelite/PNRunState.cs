using System;
using System.Collections.Generic;
using EveEngine;

// EN: The state of one run, kept by the host (Docs/RogueliteFramePlan.md section 6). Plain data and small rules, no Unity: the run director owns an instance, the planner moves it
//     along the floors, and the network layer will publish a view of it. Nothing here is saved to disk (a run is not continued after it ends).
// VI: Trạng thái của một run, do host giữ. Dữ liệu thuần và vài luật nhỏ, không Unity.
// Retired: the team left the run for good before its end (the saved run was summed up instead of continued). It is appended so the other values keep their numbers.
public enum PNRunResult { Running, Victory, Defeat, Abandoned, Failed, Retired }

public sealed class PNRunPlayer
{
    public long Owner { get; }
    public EveId Character { get; }
    // The person's own id (PNPlayerId), empty until the run learns it. A saved run needs it for every player: it is what a seat is found by when the run is continued.
    public string PlayerId { get; private set; } = "";
    public double HealthRatio { get; private set; } = 1d;
    public bool Dead { get; private set; }
    public PNRunPlayer(long owner, EveId character)
    {
        if (owner < 0) throw new ArgumentOutOfRangeException(nameof(owner));
        if (!character.IsValid) throw new ArgumentException("A run player needs a character.", nameof(character));
        Owner = owner; Character = character;
    }
    public void AssignPlayerId(string playerId)
    {
        if (PlayerId.Length > 0) throw new InvalidOperationException("The player id of owner " + Owner + " is already set.");
        PlayerId = PNPlayerId.Require(playerId);
    }
    // The ratio is kept for the next room; a dead player keeps what it had until it is revived.
    public void SetHealthRatio(double ratio)
    {
        if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio < 0 || ratio > 1) throw new ArgumentOutOfRangeException(nameof(ratio), "A health ratio is a finite number in [0, 1].");
        HealthRatio = ratio;
    }
    public void MarkDead() { Dead = true; }
    public void Revive(double ratio)
    {
        if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio <= 0 || ratio > 1) throw new ArgumentOutOfRangeException(nameof(ratio), "A revive ratio is in (0, 1].");
        Dead = false; HealthRatio = ratio;
    }
}

public sealed class PNRunStats
{
    public double Seconds { get; private set; }
    public int Kills { get; private set; }
    public int ShardsEarned { get; private set; }
    private readonly int[] cleared = new int[Enum.GetValues(typeof(PNRoomKind)).Length];
    public int RoomsCleared(PNRoomKind kind) => cleared[(int)kind];
    public int RoomsClearedTotal { get { int total = 0; foreach (var count in cleared) total += count; return total; } }
    public void AddTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        Seconds += seconds;
    }
    public void AddKills(int count) { if (count < 0) throw new ArgumentOutOfRangeException(nameof(count)); Kills = checked(Kills + count); }
    public void AddCleared(PNRoomKind kind) { cleared[(int)kind] = checked(cleared[(int)kind] + 1); }
    internal void AddShardsEarned(int amount) { ShardsEarned = checked(ShardsEarned + amount); }

    // EN: Put back the numbers of a saved run (only PNRunState.Restore calls this). Everything is checked: a saved file is data, not trusted.
    internal void Restore(double seconds, int kills, int shardsEarned, IReadOnlyDictionary<PNRoomKind, int> clearedByKind)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (kills < 0 || shardsEarned < 0) throw new ArgumentOutOfRangeException(nameof(kills));
        Seconds = seconds; Kills = kills; ShardsEarned = shardsEarned;
        Array.Clear(cleared, 0, cleared.Length);
        foreach (var pair in clearedByKind ?? throw new ArgumentNullException(nameof(clearedByKind)))
        {
            if (!Enum.IsDefined(typeof(PNRoomKind), pair.Key) || pair.Value < 0) throw new ArgumentOutOfRangeException(nameof(clearedByKind));
            cleared[(int)pair.Key] = pair.Value;
        }
    }
}

public sealed class PNRunState
{
    public const int MaxShards = 10000000;
    public Guid RunId { get; }
    public EveId Definition { get; }
    public PNRunRandom Random { get; }
    public int Floor { get; internal set; }
    public EveId Room { get; internal set; }
    // The rooms offered at the gates of the room being played, in gate order (empty while a room is played and after the last one).
    public List<EveId> Options { get; } = new List<EveId>();
    public int Shards { get; private set; }
    public PNRunResult Result { get; private set; }
    public PNRunStats Stats { get; } = new PNRunStats();
    // Roguelite Buff items the team holds (ids). Empty until the Roguelite Buff work; the frame only keeps the place.
    public List<string> Loadout { get; } = new List<string>();
    private readonly Dictionary<long, PNRunPlayer> players = new Dictionary<long, PNRunPlayer>();
    public IReadOnlyCollection<PNRunPlayer> Players => players.Values;
    public bool IsRunning => Result == PNRunResult.Running;

    public PNRunState(Guid runId, EveId definition, ulong seed, int startShards)
    {
        if (runId == Guid.Empty) throw new ArgumentException("A run needs an id.", nameof(runId));
        if (!definition.IsValid) throw new ArgumentException("A run needs a definition.", nameof(definition));
        if (startShards < 0 || startShards > MaxShards) throw new ArgumentOutOfRangeException(nameof(startShards));
        RunId = runId; Definition = definition; Random = new PNRunRandom(seed); Shards = startShards;
    }

    private PNRunState(Guid runId, EveId definition, PNRunRandom random, int shards)
    {
        if (runId == Guid.Empty) throw new ArgumentException("A run needs an id.", nameof(runId));
        if (!definition.IsValid) throw new ArgumentException("A run needs a definition.", nameof(definition));
        if (shards < 0 || shards > MaxShards) throw new ArgumentOutOfRangeException(nameof(shards));
        RunId = runId; Definition = definition; Random = random ?? throw new ArgumentNullException(nameof(random)); Shards = shards;
    }

    // One seat of a saved run, handed to Restore: who (by player id), with which character, how healthy. The owner is the one this session gave that person.
    public struct SavedPlayer
    {
        public long Owner; public string PlayerId; public EveId Character; public double Health; public bool Dead;
    }

    // EN: Rebuild a run that was saved at the start of a room. The state is the one the saved run had on entering that room; the streams carry on from where they stood.
    // VI: Dựng lại run đã lưu lúc vào phòng; ngẫu nhiên tiếp tục đúng chỗ đã dừng.
    public static PNRunState Restore(Guid runId, EveId definition, PNRunRandom random, int floor, EveId room, IEnumerable<EveId> options, int shards,
        double seconds, int kills, int shardsEarned, IReadOnlyDictionary<PNRoomKind, int> cleared, IEnumerable<string> loadout, IEnumerable<SavedPlayer> players)
    {
        var state = new PNRunState(runId, definition, random, shards);
        if (floor < 0) throw new ArgumentOutOfRangeException(nameof(floor));
        if (!room.IsValid) throw new ArgumentException("A run is in a room.", nameof(room));
        state.Floor = floor; state.Room = room;
        if (options != null) state.Options.AddRange(options);
        if (loadout != null) state.Loadout.AddRange(loadout);
        state.Stats.Restore(seconds, kills, shardsEarned, cleared);
        foreach (var saved in players ?? throw new ArgumentNullException(nameof(players)))
        {
            var player = state.AddPlayer(saved.Owner, saved.Character);
            player.AssignPlayerId(saved.PlayerId);
            player.SetHealthRatio(saved.Health);
            if (saved.Dead) player.MarkDead();
        }
        if (state.players.Count == 0) throw new ArgumentException("A run needs at least one player.", nameof(players));
        return state;
    }

    public PNRunPlayer AddPlayer(long owner, EveId character)
    {
        if (players.ContainsKey(owner)) throw new InvalidOperationException("Owner " + owner + " already plays in this run.");
        var player = new PNRunPlayer(owner, character); players.Add(owner, player); return player;
    }
    public bool TryGetPlayer(long owner, out PNRunPlayer player) => players.TryGetValue(owner, out player);

    // EN: The team wallet. Negative amounts, overflow and sums above MaxShards are refused (a Roguelite Buff that pays out calls this, it never edits the wallet).
    public void AddShards(int amount, string source)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Use TrySpendShards to take shards.");
        if (string.IsNullOrEmpty(source)) throw new ArgumentException("A shard source is required.", nameof(source));
        long total = (long)Shards + amount;
        if (total > MaxShards) throw new InvalidOperationException("The wallet cannot hold more than " + MaxShards + ".");
        Shards = (int)total; Stats.AddShardsEarned(amount);
    }
    public bool TrySpendShards(int amount, string source)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrEmpty(source)) throw new ArgumentException("A shard source is required.", nameof(source));
        if (amount > Shards) return false;
        Shards -= amount; return true;
    }

    // EN: Ends the run once. A result is never replaced by another.
    public void End(PNRunResult result)
    {
        if (result == PNRunResult.Running) throw new ArgumentException("A run ends with a result.", nameof(result));
        if (Result != PNRunResult.Running) throw new InvalidOperationException("The run already ended: " + Result);
        Result = result;
    }
}
