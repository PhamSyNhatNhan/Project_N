#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;

// EN: Plays one room (Docs/RogueliteFramePlan.md 7.3-7.4). The PNRoomFlow machine decides what happens next; this class carries the commands out in the room's world: it spawns each wave
//     (places picked from the map's spawn groups with the run's Spawn stream, put on the navigation mesh, built through the entity host so a coop host enrols them), puts the floor's enemy
//     Buffs on them, counts the deaths for the machine, watches the players, and on a clear pays the reward. The start switch is not here yet: PressStart is what the switch will call
//     (Docs/InteractablePlan.md), and tests press it directly.
// VI: Chơi một phòng: máy PNRoomFlow quyết định, lớp này thực hiện lệnh trong world của phòng (sinh wave quái ở nhóm điểm trên navigation, áp Buff của tầng, đếm quái chết, theo dõi người chơi, trả thưởng).
public sealed class PNRoomDirector : IDisposable
{
    public const string EnemySceneKey = "roguelite:enemy";
    private struct PendingSpawn { public PNRoomSpawn Spawn; public Vector3 Position; public string Slot; }
    private readonly PNRunDirector runs;
    private readonly PNGameSession game;
    // A place enemies may appear: the content slot it is in and how to pick a spot (from the run's Spawn stream).
    private struct GroupPlace { public string Slot; public Func<PNRunRandom, Vector3> Pick; }
    private readonly Dictionary<string, GroupPlace> groups = new Dictionary<string, GroupPlace>();
    private readonly Dictionary<string, PNInteractable> interactables = new Dictionary<string, PNInteractable>();
    private readonly Queue<PendingSpawn> pending = new Queue<PendingSpawn>();
    private readonly List<EveEntityLoader> enemies = new List<EveEntityLoader>();
    private int deaths, inFlight;
    private float nextPlayerCheck;
    private bool disposed, cleared;

    public PNRoomDefinition Definition { get; }
    public EveWorldContext World { get; }
    public string Slot { get; }
    public PNRoomFlow Flow { get; }
    // The start switch may be used (the room waits for it).
    public bool StartAvailable { get; private set; }
    public IReadOnlyList<EveEntityLoader> Enemies => enemies;
    public int EnemiesSpawned { get; private set; }

    public PNRoomDirector(PNRunDirector runs, PNGameSession game, EveWorldContext world, PNRoomDefinition definition, string slot)
    {
        this.runs = runs; this.game = game; World = world; Definition = definition; Slot = slot; Flow = new PNRoomFlow(definition);
        // The layout of the room has two sources: spawn group components in a scene map, and the map block of a grid map (named points with a radius, G10a).
        foreach (var content in game.Worlds.Get(world).Slots)
            foreach (var group in content.Roots.Where(x => x != null).SelectMany(x => x.GetComponentsInChildren<PNRoomSpawnGroup>(true)))
            { var captured = group; groups[group.Group] = new GroupPlace { Slot = content.Id, Pick = random => captured.Pick(random) }; }
        var grid = PNGridWorldController.Find(world);
        if (grid?.Meta != null)
            foreach (var group in grid.Meta.Groups)
            {
                if (groups.ContainsKey(group.Name)) continue;
                var data = grid.Data; var centre = new Vector3((group.X + .5f) * data.CellSize, 0f, (group.Z + .5f) * data.CellSize); double radius = group.Radius;
                groups[group.Name] = new GroupPlace { Slot = PNGridWorldController.SlotId, Pick = random => PickInDisc(random, centre, radius) };
            }
        foreach (var content in game.Worlds.Get(world).Slots)
            foreach (var item in content.Roots.Where(x => x != null).SelectMany(x => x.GetComponentsInChildren<PNInteractable>(true)))
                interactables[item.Key] = item;
        // A room is only as good as its map: every group a wave names has to be there.
        foreach (var name in definition.Waves.SelectMany(x => x.Spawns).Select(x => x.Group).Distinct())
            if (!groups.ContainsKey(name)) throw new InvalidOperationException("Room " + definition.Id + " needs spawn group '" + name + "' that its map does not have.");
    }

    public void Enter()
    {
        // Everything of the map starts closed; the room opens what it names.
        foreach (var item in interactables.Values) item.SetAvailable(false);
        foreach (var key in Definition.Interactables) Interactable(key)?.SetAvailable(true);
        Apply(Flow.Entered());
    }

    private PNInteractable Interactable(string key)
        => interactables.TryGetValue(key, out var item) ? item : throw new InvalidOperationException("Room " + Definition.Id + " names interactable '" + key + "' that its map has no PNInteractable for.");

    // The index of a gate by its key (the order of the room's `exits`), or -1.
    public int GateIndex(string key) { for (int i = 0; i < Definition.Exits.Count; i++) if (Definition.Exits[i] == key) return i; return -1; }

    // After a clear: one gate per drawn option, each saying which kind of room is behind it; the last room has one gate that finishes the run.
    private void OpenGates()
    {
        // An offer of Roguelite Buff items waits for the host: the gates stay shut until it is picked.
        if (runs.Rogue != null && runs.Rogue.HasPendingOffer) { CloseGates(); return; }
        var options = runs.Exits();
        for (int i = 0; i < Definition.Exits.Count; i++)
        {
            var gate = Interactable(Definition.Exits[i]);
            if (i < options.Count) gate.SetAvailable(true, PNRoomInteractions.KindKey(runs.KindOf(options[i])));
            else if (i == 0 && options.Count == 0 && runs.IsLastRoom) gate.SetAvailable(true, PNRoomInteractions.FinishKey);
            else gate.SetAvailable(false);
        }
    }

    // The offer was picked: open the gates a cleared room shows.
    internal void RefreshGates() { if (cleared && !disposed) OpenGates(); }
    public void CloseGates() { foreach (var key in Definition.Exits) Interactable(key)?.SetAvailable(false); }

    // The switch was pressed. False when the room is not waiting for it.
    public bool PressStart()
    {
        if (!StartAvailable || Flow.Phase != PNRoomPhase.Waiting) return false;
        StartAvailable = false; SetStartSwitch(false); Apply(Flow.StartPressed()); return true;
    }

    private void SetStartSwitch(bool open)
    {
        if (Definition.Start.Interactable.Length > 0) Interactable(Definition.Start.Interactable)?.SetAvailable(open);
    }

    public void Tick(float deltaTime)
    {
        if (disposed || !runs.IsActive) return;
        DrainSpawns();
        if (deaths > 0 && Flow.Phase == PNRoomPhase.Combat && Flow.Alive > 0)
        {
            int count = Math.Min(deaths, Flow.Alive); deaths -= count; runs.State.Stats.AddKills(count);
            Apply(Flow.EnemiesCleared(count));
        }
        Apply(Flow.Tick(deltaTime));
        if (Time.unscaledTime >= nextPlayerCheck) { nextPlayerCheck = Time.unscaledTime + .25f; WatchPlayers(); }
    }

    private void WatchPlayers()
    {
        var players = runs.Players(World);
        if (players.Length == 0) return;
        foreach (var player in players) { var stat = player.GetComponent<PNStat>(); if (stat == null || !stat.IsDead) return; }
        Apply(Flow.AllPlayersDead());
    }

    private void Apply(IReadOnlyList<PNRoomCommand> commands)
    {
        foreach (var command in commands)
        {
            switch (command.Kind)
            {
                case PNRoomCommandKind.EnableStart: StartAvailable = true; SetStartSwitch(true); break;
                case PNRoomCommandKind.StartCountdown: break;
                case PNRoomCommandKind.SpawnWave: BeginWave(command.Wave); break;
                case PNRoomCommandKind.RoomCleared: cleared = true; runs.OnRoomCleared(); OpenGates(); break;
                case PNRoomCommandKind.Defeat: runs.OnDefeat(); break;
            }
            runs.Raise();
        }
    }

    // ---- enemies ----
    private void BeginWave(int index)
    {
        var lease = PNWorldNavigation.Find(World);
        foreach (var spawn in Definition.Waves[index].Spawns)
            for (int n = 0; n < spawn.Count; n++)
                pending.Enqueue(new PendingSpawn { Spawn = spawn, Position = Place(spawn.Group, lease), Slot = groups[spawn.Group].Slot });
        DrainSpawns();
    }

    // A place of the group on the navigation mesh (the group's own point when the map has no navigation). Eight tries, then the map is at fault.
    private Vector3 Place(string group, PNWorldNavigationLease lease)
    {
        var place = groups[group];
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var point = place.Pick(runs.State.Random);
            if (lease == null) return point;
            if (lease.TryProject(point, 1.5f, out var projected)) return projected;
        }
        throw new InvalidOperationException("Spawn group '" + group + "' of room " + Definition.Id + " has no place on the navigation mesh.");
    }

    // Uniform over a disc (the square root keeps the density even), drawn from the Spawn stream like PNRoomSpawnGroup's circle.
    private static Vector3 PickInDisc(PNRunRandom random, Vector3 centre, double radius)
    {
        double angle = random.NextDouble(PNRunStream.Spawn) * 2d * Math.PI, distance = radius * Math.Sqrt(random.NextDouble(PNRunStream.Spawn));
        return new Vector3(centre.x + (float)(Math.Cos(angle) * distance), centre.y, centre.z + (float)(Math.Sin(angle) * distance));
    }

    private void DrainSpawns()
    {
        var host = PNEntityHosts.For(game.Session);
        while (pending.Count > 0)
        {
            var next = pending.Peek();
            var request = new PNEntityRequest
            {
                Entity = next.Spawn.Entity.ToString(), Template = next.Spawn.Entity.ToString(), Character = next.Spawn.Character.ToString(), World = World, WorldIncarnation = World.Incarnation,
                ContentSlot = next.Slot, SceneKey = EnemySceneKey, EntityKey = "", Position = next.Position
            };
            inFlight++;
            var candidate = host.TrySpawn(request, null, OnEnemyPublished, error => { inFlight--; Debug.LogException(error); runs.Fail("An enemy of room " + Definition.Id + " could not be built: " + error.Message); });
            if (candidate == null) { inFlight--; break; }
            pending.Dequeue();
        }
    }

    private void OnEnemyPublished(EveEntityLoader loader)
    {
        inFlight--; EnemiesSpawned++;
        if (disposed) return;
        var stat = loader.GetComponent<PNStat>();
        enemies.Add(loader);
        // Only a marked enemy counts for the clear (the module gives the marker in a world with the feature `project_n:room_clear`, i.e. every room); an unmarked one would leave the room unclearable.
        if (loader.GetComponent<PNRoomClearMarker>() == null) { runs.Fail("An enemy of room " + Definition.Id + " has no room-clear marker: the world lacks the feature " + PNRunFeatures.RoomClear + "."); return; }
        // The floor's Buffs (the run's difficulty) go on every enemy of the floor, with the enemy itself as their source.
        var buffs = loader.GetComponent<PNBuffManager>();
        if (buffs != null) foreach (var id in runs.EnemyBuffs()) buffs.Buffs.Apply(new PNBuffApplyRequest(id, PNCombatSource.Capture(stat)));
        bool counted = false;
        stat.Died += result => { if (counted) return; counted = true; deaths++; };
    }

    public void Dispose()
    {
        disposed = true; pending.Clear(); enemies.Clear();
        // The room is being left or the run ended: nothing in it can be used any more (the world may already be going away).
        foreach (var item in interactables.Values) { try { if (item != null && item.IsBound) item.SetAvailable(false); } catch (Exception error) { Debug.LogException(error); } }
    }
}
#endif
