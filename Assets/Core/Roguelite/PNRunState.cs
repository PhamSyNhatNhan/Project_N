using System;
using System.Collections.Generic;
using EveEngine;

// EN: The state of one run, kept by the host (Docs/RogueliteFramePlan.md section 6). Plain data and small rules, no Unity: the run director owns an instance, the planner moves it
//     along the floors, and the network layer will publish a view of it. Nothing here is saved to disk (a run is not continued after it ends).
// VI: Trạng thái của một run, do host giữ. Dữ liệu thuần và vài luật nhỏ, không Unity.
public enum PNRunResult { Running, Victory, Defeat, Abandoned, Failed }

public sealed class PNRunPlayer
{
    public long Owner { get; }
    public EveId Character { get; }
    public double HealthRatio { get; private set; } = 1d;
    public bool Dead { get; private set; }
    public PNRunPlayer(long owner, EveId character)
    {
        if (owner < 0) throw new ArgumentOutOfRangeException(nameof(owner));
        if (!character.IsValid) throw new ArgumentException("A run player needs a character.", nameof(character));
        Owner = owner; Character = character;
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
    public int MoneyEarned { get; private set; }
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
    internal void AddMoneyEarned(int amount) { MoneyEarned = checked(MoneyEarned + amount); }
}

public sealed class PNRunState
{
    public const int MaxMoney = 10000000;
    public Guid RunId { get; }
    public EveId Definition { get; }
    public PNRunRandom Random { get; }
    public int Floor { get; internal set; }
    public int Step { get; internal set; }
    public EveId Room { get; internal set; }
    // The rooms offered at the gates of the room being played, in gate order (empty while a room is played and after the last one).
    public List<EveId> Options { get; } = new List<EveId>();
    public int Money { get; private set; }
    public PNRunResult Result { get; private set; }
    public PNRunStats Stats { get; } = new PNRunStats();
    // Roguelite Buff items the team holds (ids). Empty until the Roguelite Buff work; the frame only keeps the place.
    public List<string> Loadout { get; } = new List<string>();
    private readonly Dictionary<long, PNRunPlayer> players = new Dictionary<long, PNRunPlayer>();
    public IReadOnlyCollection<PNRunPlayer> Players => players.Values;
    public bool IsRunning => Result == PNRunResult.Running;

    public PNRunState(Guid runId, EveId definition, ulong seed, int startMoney)
    {
        if (runId == Guid.Empty) throw new ArgumentException("A run needs an id.", nameof(runId));
        if (!definition.IsValid) throw new ArgumentException("A run needs a definition.", nameof(definition));
        if (startMoney < 0 || startMoney > MaxMoney) throw new ArgumentOutOfRangeException(nameof(startMoney));
        RunId = runId; Definition = definition; Random = new PNRunRandom(seed); Money = startMoney;
    }

    public PNRunPlayer AddPlayer(long owner, EveId character)
    {
        if (players.ContainsKey(owner)) throw new InvalidOperationException("Owner " + owner + " already plays in this run.");
        var player = new PNRunPlayer(owner, character); players.Add(owner, player); return player;
    }
    public bool TryGetPlayer(long owner, out PNRunPlayer player) => players.TryGetValue(owner, out player);

    // EN: The team wallet. Negative amounts, overflow and sums above MaxMoney are refused (a Roguelite Buff that pays out calls this, it never edits the wallet).
    public void AddMoney(int amount, string source)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Use TrySpend to take money.");
        if (string.IsNullOrEmpty(source)) throw new ArgumentException("A money source is required.", nameof(source));
        long total = (long)Money + amount;
        if (total > MaxMoney) throw new InvalidOperationException("The wallet cannot hold more than " + MaxMoney + ".");
        Money = (int)total; Stats.AddMoneyEarned(amount);
    }
    public bool TrySpend(int amount, string source)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrEmpty(source)) throw new ArgumentException("A money source is required.", nameof(source));
        if (amount > Money) return false;
        Money -= amount; return true;
    }

    // EN: Ends the run once. A result is never replaced by another.
    public void End(PNRunResult result)
    {
        if (result == PNRunResult.Running) throw new ArgumentException("A run ends with a result.", nameof(result));
        if (Result != PNRunResult.Running) throw new InvalidOperationException("The run already ended: " + Result);
        Result = result;
    }
}
