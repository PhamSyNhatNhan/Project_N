#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The run as every machine sees it (Docs/RogueliteFramePlan.md F6b): one Session sync block, `project_n:run_state`. The host writes it from its PNRunDirector each time the director says something
//     changed (phase, room, wallet, countdown second, result); every machine, the host and offline too, reads it back through the same sink, so the run HUD and the end screen look the
//     same everywhere and a client needs nothing of the director. The elapsed time is only put in once the run is over (a block that changed every frame would be sent every frame).
// VI: Run như mọi máy thấy: một khối Session `project_n:run_state`. Host ghi từ PNRunDirector mỗi khi có thay đổi; mọi máy đọc lại qua cùng một sink nên HUD và màn kết thúc giống nhau, client không cần director.
public sealed class PNRunSnapshot
{
    public static readonly PNRunSnapshot None = new PNRunSnapshot();
    public string Phase = "none", Result = "running", RoomPhase = "";
    // The run's id (32 hex, no dashes) and its definition id; stella is paid once per RunId and computed from the definition's `stella` rule (Docs/StellaPlan.md).
    public string RunId = "", Definition = "";
    public PNRoomKind RoomKind = PNRoomKind.Combat;
    public int Shards, Countdown, Floor, Step, Seconds, Kills, Rooms, Earned;
    public long[] Players = new long[0];
    public bool Playing => Phase == "playing";
    public bool Ended => Phase == "ended";
}

public sealed class PNRunStateSync : IPNSyncStateSource, IPNSyncStateSink, IDisposable
{
    public static readonly EveId State = EveId.Parse("project_n:run_state");
    public const int StateBytes = 1024;
    private readonly PNSyncService sync;
    private readonly PNRunDirector director;
    private IDisposable sinkLease, sourceLease;
    private bool disposed;
    public PNRunSnapshot Current { get; private set; } = PNRunSnapshot.None;
    public EveId StateId => State;

    public PNRunStateSync(PNSyncService sync, PNRunDirector director)
    {
        this.sync = sync ?? throw new ArgumentNullException(nameof(sync)); this.director = director;
        sinkLease = sync.RegisterSink(PNSyncTarget.Session, this);
        if (sync.IsAuthority && director != null)
        {
            sourceLease = sync.RegisterSource(PNSyncTarget.Session, this);
            director.Changed += Touch; Touch();
        }
    }

    private void Touch() { if (!disposed) sync.MarkDirty(PNSyncTarget.Session, State); }

    public JToken Capture()
    {
        var o = new JObject();
        var state = director.State; var room = director.Room;
        o["phase"] = director.Phase == PNRunPhase.Ended ? "ended" : director.Phase == PNRunPhase.Playing ? "playing" : director.Phase == PNRunPhase.None ? "none" : "loading";
        if (state == null) return o;
        var stats = state.Stats; bool over = !state.IsRunning;
        o["run"] = state.RunId.ToString("N"); o["definition"] = state.Definition.ToString();
        o["result"] = state.Result.ToString().ToLowerInvariant(); o["shards"] = state.Shards; o["floor"] = state.Floor; o["step"] = state.Step;
        o["time"] = over ? (int)stats.Seconds : 0; o["kills"] = stats.Kills; o["rooms"] = stats.RoomsClearedTotal; o["earned"] = stats.ShardsEarned;
        o["players"] = new JArray(state.Players.Select(x => (object)x.Owner).OrderBy(x => (long)x).ToArray());
        if (room != null)
        {
            o["kind"] = room.Definition.Kind.ToString().ToLowerInvariant(); o["roomPhase"] = room.Flow.Phase.ToString().ToLowerInvariant();
            o["countdown"] = room.Flow.Phase == PNRoomPhase.Countdown ? (int)Math.Ceiling(room.Flow.CountdownRemaining) : 0;
        }
        return o;
    }

    public void Apply(JToken value, ulong revision)
    {
        var o = value as JObject; var snap = new PNRunSnapshot();
        if (o != null)
        {
            snap.Phase = (string)o["phase"] ?? "none"; snap.Result = (string)o["result"] ?? "running";
            snap.RunId = (string)o["run"] ?? ""; snap.Definition = (string)o["definition"] ?? "";
            snap.Shards = (int?)o["shards"] ?? 0; snap.Floor = (int?)o["floor"] ?? 0; snap.Step = (int?)o["step"] ?? 0;
            snap.Seconds = (int?)o["time"] ?? 0; snap.Kills = (int?)o["kills"] ?? 0; snap.Rooms = (int?)o["rooms"] ?? 0; snap.Earned = (int?)o["earned"] ?? 0;
            snap.Countdown = (int?)o["countdown"] ?? 0; snap.RoomPhase = (string)o["roomPhase"] ?? "";
            if (Enum.TryParse<PNRoomKind>((string)o["kind"] ?? "", true, out var kind)) snap.RoomKind = kind;
            if (o["players"] is JArray list) snap.Players = list.Select(x => (long)x).ToArray();
        }
        Current = snap;
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (director != null) director.Changed -= Touch;
        sinkLease?.Dispose(); sourceLease?.Dispose(); sinkLease = sourceLease = null;
    }
}
#endif
#if UNITY_5_3_OR_NEWER
// The run of the play this machine is in, for views (None when there is no active play or no run block yet).
public static class PNRunStateSyncs
{
    public static PNRunSnapshot Current()
    {
        if (!EveRegistrySession.IsReady || PNGameBootstrap.Runtime == null) return PNRunSnapshot.None;
        var game = PNGameBootstrap.Runtime.GetComponent<PNGameSession>();
        return game != null && ReferenceEquals(game.Session, EveRegistrySession.Context) && game.RunView != null ? game.RunView.Current : PNRunSnapshot.None;
    }
}
#endif
