#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;

// EN: Plays a run on the machine with authority (Docs/RogueliteFramePlan.md 7.1-7.2). It owns the run state, moves the team from room to room and hands each room to a PNRoomDirector.
//     A room change is: draw the room, create its map world, wait until the world is ready, move the whole team with a REPLACEMENT transfer (every player is rebuilt from scratch as the
//     character the run recorded for them, keeping only the health ratio: PNRunActorStateProvider), then unload the room the team has left. One director per play (PNGameSession.Runs),
//     advanced once a frame by PNGameSession.Update. Nothing here draws UI: a view reads Phase, State and Room, and listens to Changed.
// VI: Điều khiển một run ở máy có quyền: giữ trạng thái run, đưa cả đội sang phòng kế (tạo world, chuyển thay thế dựng lại từng người chơi, dỡ phòng cũ), giao từng phòng cho PNRoomDirector.
public enum PNRunPhase { None, CreatingRoom, Transferring, Playing, Ended }

public sealed class PNRunDirector : IDisposable
{
    public const string PlayerRole = "project_n:player";
    public const double RoomDeadlineSeconds = 60d;
    // After the last player falls the room plays on this long before the run is lost (the fall is seen, not cut).
    public const float DefeatDelaySeconds = 1.5f;
    private static readonly Dictionary<Guid, PNRunDirector> directors = new Dictionary<Guid, PNRunDirector>();
    public static PNRunDirector Find(EveSessionContext session) => session != null && directors.TryGetValue(session.SessionId, out var director) ? director : null;

    private readonly PNGameSession game;
    private readonly List<EveWorldContext> unloading = new List<EveWorldContext>();
    private PNRunPlanner planner;
    private EveWorldContext source, destination;
    private PNRoomDefinition nextRoom;
    private PNWorldTransferReceipt receipt;
    private double phaseStarted;
    private bool disposed;
    private float defeatTimer = -1f;
    private int shownCountdown;

    public PNRunPhase Phase { get; private set; }
    public PNRunDefinition Definition { get; private set; }
    public PNRunState State { get; private set; }
    public PNRoomDirector Room { get; private set; }
    public string Error { get; private set; }
    public double ReviveHealth => Definition?.ReviveHealth ?? PNRunActorStateProvider.DefaultReviveRatio;
    public bool IsActive => Phase != PNRunPhase.None && Phase != PNRunPhase.Ended;
    // Something a view shows changed: phase, room, cleared, money, result.
    public event Action Changed;

    public PNRunDirector(PNGameSession game)
    {
        this.game = game ?? throw new ArgumentNullException(nameof(game));
        directors[game.Session.SessionId] = this;
        game.Session.RegisterCleanup(Dispose);
    }

    internal void Raise() { try { Changed?.Invoke(); } catch (Exception error) { Debug.LogException(error); } }

    // ---- start ----
    // EN: Begin a run with the players that are in `from` (the lobby). Returns false with the reason when it cannot start; otherwise the director moves through CreatingRoom and
    //     Transferring to Playing by itself (watch Phase), or to Ended with Error set.
    public bool Begin(EveId runId, EveWorldContext from, ulong seed, out string error)
    {
        error = null;
        try
        {
            if (disposed || !game.Session.IsActive || !game.Session.HasAuthority) throw new InvalidOperationException("Only an active authority plays a run.");
            if (IsActive) throw new InvalidOperationException("A run is already being played.");
            if (from == null || !from.CanExecute) throw new InvalidOperationException("The team's world is not ready.");
            var hub = game.Session.Registry;
            Definition = hub.RogueliteRuns().Get(runId);
            planner = new PNRunPlanner(Definition, id => hub.RogueliteFloors().Get(id));
            var players = Players(from);
            if (players.Length == 0) throw new InvalidOperationException("There is no player to take into the run.");
            var team = players.ToDictionary(x => x.LoadContext.Identity.OwnerId, x => CharacterOf(x));
            State = planner.Begin(Guid.NewGuid(), seed, team.Select(x => new KeyValuePair<long, EveId>(x.Key, x.Value)));
            Room = null; Error = null; source = from;
            ChangeRoom(hub.RogueliteRooms().Get(State.Room));
            return true;
        }
        catch (Exception exception) { error = exception.Message; Phase = PNRunPhase.None; State = null; Definition = null; return false; }
    }

    // ---- gates ----
    // EN: The team takes the gate with this index (it was drawn when the room was cleared). Returns false when no such gate is open.
    public bool Advance(int option)
    {
        if (Phase != PNRunPhase.Playing || Room == null || Room.Flow.Phase != PNRoomPhase.Cleared) return false;
        var options = planner.PrepareExits(State);
        if (option < 0 || option >= options.Count || !planner.Choose(State, option)) return false;
        Room.Flow.ExitChosen(); Room.CloseGates();
        source = Room.World;
        ChangeRoom(game.Session.Registry.RogueliteRooms().Get(State.Room));
        return true;
    }

    // The gates of the room that was just cleared (drawn once, in gate order).
    public IReadOnlyList<EveId> Exits() => Phase == PNRunPhase.Playing && Room != null && Room.Flow.Phase == PNRoomPhase.Cleared ? planner.PrepareExits(State) : (IReadOnlyList<EveId>)new EveId[0];
    public bool IsLastRoom => State != null && planner != null && planner.IsLastRoom(State);

    public void Abandon() { if (IsActive) End(PNRunResult.Abandoned, null); }

    // The gate of the last room: the run is won. (What happens next, the end screen and the way back, is the end-of-run work.)
    public bool Complete()
    {
        if (Phase != PNRunPhase.Playing || Room == null || Room.Flow.Phase != PNRoomPhase.Cleared || !IsLastRoom) return false;
        Room.Flow.ExitChosen(); Room.CloseGates(); End(PNRunResult.Victory, null); return true;
    }
    public PNRoomKind KindOf(EveId room) => game.Session.Registry.RogueliteRooms().Get(room).Kind;

    // ---- events from the room ----
    internal void OnRoomCleared()
    {
        State.Stats.AddCleared(Room.Definition.Kind);
        if (Room.Definition.Reward.Money > 0) State.AddMoney(Room.Definition.Reward.Money, "room:" + Room.Definition.Id);
        planner.PrepareExits(State);
        Raise();
    }
    internal void OnDefeat() { if (defeatTimer < 0f && State != null && State.IsRunning) defeatTimer = DefeatDelaySeconds; Raise(); }
    internal void Fail(string reason) { End(PNRunResult.Failed, reason); }

    private void End(PNRunResult result, string reason)
    {
        if (Phase == PNRunPhase.Ended) return;
        Error = reason; if (reason != null) Debug.LogWarning("Run ended (" + result + "): " + reason);
        if (State != null && State.IsRunning) State.End(result);
        // A world made for a room change that did not complete is dropped; the team stays where it was.
        if (destination != null && destination != Room?.World) unloading.Add(destination);
        destination = null; nextRoom = null; receipt = null;
        Phase = PNRunPhase.Ended; Raise();
    }

    // ---- room change ----
    private void ChangeRoom(PNRoomDefinition room)
    {
        nextRoom = room; phaseStarted = Time.realtimeSinceStartupAsDouble;
        destination = game.Worlds.Create(room.World);
        Phase = PNRunPhase.CreatingRoom; Raise();
    }

    public void Tick(float deltaTime)
    {
        if (disposed) return;
        DropOldRooms();
        try
        {
            switch (Phase)
            {
                case PNRunPhase.CreatingRoom: WaitForRoomWorld(); break;
                case PNRunPhase.Transferring: WaitForTransfer(); break;
                case PNRunPhase.Playing:
                    if (State.IsRunning)
                    {
                        State.Stats.AddTime(deltaTime); Room?.Tick(deltaTime);
                        if (defeatTimer >= 0f && (defeatTimer -= deltaTime) <= 0f) End(PNRunResult.Defeat, null);
                        // The countdown is shown in whole seconds: tell the views when the second changes (and not every frame).
                        int second = Room != null && Room.Flow.Phase == PNRoomPhase.Countdown ? (int)Math.Ceiling(Room.Flow.CountdownRemaining) : 0;
                        if (second != shownCountdown) { shownCountdown = second; Raise(); }
                    }
                    break;
            }
        }
        catch (Exception error) { Debug.LogException(error); Fail(error.Message); }
    }

    private void WaitForRoomWorld()
    {
        if (destination.State == EveWorldState.Failed || destination.State == EveWorldState.Unloading || destination.State == EveWorldState.Disposed)
        { Fail("The map world of room " + nextRoom.Id + " did not load: " + (destination.Failure?.Message ?? destination.State.ToString())); return; }
        if (Time.realtimeSinceStartupAsDouble - phaseStarted > RoomDeadlineSeconds) { Fail("The map world of room " + nextRoom.Id + " took too long to load."); return; }
        if (!destination.CanExecute) return;
        var slot = game.Worlds.Get(destination).Slots.FirstOrDefault(x => x.Roots.Any(r => r != null && r.GetComponentsInChildren<PNPlayerSpawnPoint>(true).Any(p => p.SpawnEntry == "player")));
        if (slot == null) { Fail("The map of room " + nextRoom.Id + " has no \"player\" spawn point."); return; }
        var players = Players(source);
        if (players.Length == 0) { Fail("No player is left to move into room " + nextRoom.Id + "."); return; }
        var characters = players.ToDictionary(x => x.LoadContext.Identity.OwnerId, x => (State.TryGetPlayer(x.LoadContext.Identity.OwnerId, out var known) ? known.Character : CharacterOf(x)).ToString());
        receipt = game.Transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(), players, destination, slot.Id, "player", PlayerRole, characters: characters, stateProviders: new[] { PNRunActorStateProvider.Id }));
        phaseStarted = Time.realtimeSinceStartupAsDouble; Phase = PNRunPhase.Transferring; Raise();
    }

    private void WaitForTransfer()
    {
        if (!receipt.IsTerminal || receipt.CleanupPending) return;
        if (receipt.State != PNWorldTransferState.Completed) { Fail("Moving the team into room " + nextRoom.Id + " failed (" + receipt.State + "): " + receipt.Error); return; }
        var old = Room?.World ?? source;
        Room?.Dispose();
        Room = new PNRoomDirector(this, game, destination, nextRoom, SlotOf(destination));
        if (old != null && old != destination) unloading.Add(old);
        destination = null; nextRoom = null; receipt = null;
        Phase = PNRunPhase.Playing; Room.Enter(); Raise();
    }

    private string SlotOf(EveWorldContext world)
        => game.Worlds.Get(world).Slots.First(x => x.Roots.Any(r => r != null && r.GetComponentsInChildren<PNPlayerSpawnPoint>(true).Any(p => p.SpawnEntry == "player"))).Id;

    // A room the team has left (or a world made for a change that failed) is unloaded once it is empty. A world that discards itself is simply forgotten.
    private void DropOldRooms()
    {
        for (int i = unloading.Count - 1; i >= 0; i--)
        {
            var world = unloading[i];
            bool gone = world.State == EveWorldState.Disposed || world.State == EveWorldState.Unloading && !game.Session.Worlds.Snapshot().Contains(world);
            try { if (gone || SelfDiscarding(world) || game.Worlds.Unload(world)) unloading.RemoveAt(i); }
            catch (Exception error) { Debug.LogException(error); unloading.RemoveAt(i); }
        }
    }

    // The lobby discards itself once the team has left; unloading it here as well would race the world lifetime controller.
    private bool SelfDiscarding(EveWorldContext world)
        => game.Session.Registry.Get<PNWorldDefinition>().TryGet(world.DefinitionId, out var definition) && definition.Policy.Empty == PNEmptyWorldPolicy.Discard;

    // ---- helpers ----
    internal EveEntityLoader[] Players(EveWorldContext world)
        => game.Session.Entities.Snapshot(world).Where(x => x.IsReady && PNEntityRoles.IsPlayer(x)).ToArray();
    private static EveId CharacterOf(EveEntityLoader loader)
    {
        var identity = loader.LoadContext.Identity;
        return identity.CharacterId.IsValid ? identity.CharacterId : loader.GetComponent<PNStat>().CharacterId;
    }
    internal IReadOnlyList<EveId> EnemyBuffs() => planner.FloorAt(State.Floor).EnemyBuffs;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Room?.Dispose(); Room = null; directors.Remove(game.Session.SessionId);
    }
}
#endif
