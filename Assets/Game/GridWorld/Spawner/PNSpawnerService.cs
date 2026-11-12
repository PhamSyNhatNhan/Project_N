#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// EN: The monster spawner of a grid world (Docs/SandboxPlan.md, step G9), the world module `project_n:spawner`. One per world that has the feature, on the authority only, idle until a mode calls `Configure`.
//     Every `Interval` seconds it looks at the living players and the enemies of the world (the shared entity index), and for each player that is under its budget (enemies within `BudgetRadius`, per player and for
//     the whole world, counting those still being built) it asks `PNSpawnPlanner` for a spot in the ring around that player and spawns an entry chosen by weight through the session's entity host, the same path
//     the roguelite rooms use (`PNEntityRequest`). The rate scale of the spot (map zones, things with a safety radius) is the chance. When the despawn service retires an enemy the spawner skips ONE round, so a
//     monster that went away is not replaced at once. The random stream is the world's own (seed of the descriptor), never a shared or global one.
// VI: Bộ sinh quái của world lưới, module `project_n:spawner`. Mỗi world có feature một cái, chỉ ở authority, nằm im tới khi mode gọi `Configure`. Mỗi `Interval` giây xem người chơi còn sống và quái của world; người chơi nào dưới ngân sách
//     thì nhờ `PNSpawnPlanner` chọn chỗ trong vòng quanh người đó và sinh một mục chọn theo trọng số qua entity host của session, cùng đường với phòng roguelite. Khi dịch vụ hết hạn gỡ một quái, bộ sinh bỏ MỘT vòng.
public sealed class PNSpawnerConfig
{
    public PNSpawnSettings Settings = new PNSpawnSettings();
    public List<PNSpawnEntry> Entries = new List<PNSpawnEntry>();
    public string SceneKey = "sandbox:enemy";
}

public sealed class PNSpawnerService : MonoBehaviour
{
    private static readonly Dictionary<EveWorldContext, PNSpawnerService> services = new Dictionary<EveWorldContext, PNSpawnerService>();
    private readonly List<PNStat> candidates = new List<PNStat>();
    private readonly List<(float x, float z)> players = new List<(float, float)>();
    private readonly List<(float x, float z)> enemies = new List<(float, float)>();
    private readonly List<(float x, float z)> building = new List<(float, float)>();
    private PNGridRandom random;
    private PNSpawnPlanner planner;
    private PNSpawnerConfig config;
    private List<PNSpawnSource> sources;
    private int placementCount = -1, skip;
    private float accumulated;
    private bool disposed;
    private PNDespawnService despawn;

    public EveWorldContext World { get; private set; }
    public PNGridWorldController Controller { get; private set; }
    public bool Configured => config != null;
    public int Rounds { get; private set; }
    public int Spawned { get; private set; }
    public int Failed { get; private set; }
    public int SkippedRounds { get; private set; }
    public int InFlight => building.Count;
    // A fixture takes the requests instead of the entity host (it records them and returns true when it took one).
    public Func<PNEntityRequest, bool> Intercept { get; set; }
    // Raised on the authority when a monster has been built and published at a spot chosen by the planner.
    public event Action<EveEntityLoader, Vector3> Placed;

    public static PNSpawnerService Find(EveWorldContext world) => world != null && services.TryGetValue(world, out var value) ? value : null;

    public static PNSpawnerService Create(EveWorldContext world, UnityEngine.SceneManagement.Scene scene, PNGridWorldController controller, bool[] groundWalkable, long seed)
    {
        if (world == null || controller == null) throw new ArgumentNullException(world == null ? nameof(world) : nameof(controller));
        if (services.ContainsKey(world)) throw new InvalidOperationException("This world already has a spawner.");
        var root = new GameObject("PN Spawner Service"); root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var service = root.AddComponent<PNSpawnerService>();
        service.World = world; service.Controller = controller;
        service.planner = new PNSpawnPlanner(controller.Data, new PNGridNavigationSource(controller.Data, groundWalkable, PNNavigationProfile.GroundSmall));
        service.random = new PNGridRandom(PNGridRandom.Mix(unchecked((ulong)seed) ^ PNGridRandom.Fnv("project_n:spawner")));
        service.despawn = PNDespawnService.Find(world);
        if (service.despawn != null) service.despawn.Expired += service.OnExpired;
        services[world] = service; root.SetActive(true);
        return service;
    }

    // Turns the spawner on. The entries name registered entities (a mode chooses them); settings are checked.
    public void Configure(PNSpawnerConfig value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        value.Settings.Validate();
        if (value.Entries.Count == 0) throw new ArgumentException("The spawner needs at least one entry.");
        foreach (var entry in value.Entries)
            if (string.IsNullOrEmpty(entry.Entity) || string.IsNullOrEmpty(entry.Character) || entry.Weight < 0 || double.IsNaN(entry.Weight) || entry.Clearance < 0 || entry.Clearance >= PNGridWorldLimits.ClearanceCap)
                throw new ArgumentException("A spawner entry needs an entity, a character, a weight of 0 or more and a clearance of 0 to " + (PNGridWorldLimits.ClearanceCap - 1) + ".");
        config = value; accumulated = 0; skip = 0;
    }
    public void Disable() { config = null; building.Clear(); }

    private void OnExpired() { if (config != null) skip = 1; }

    private void Update()
    {
        if (disposed || config == null || World == null || !World.CanExecute || !World.Session.HasAuthority) return;
        accumulated += Time.deltaTime;
        if (accumulated < config.Settings.Interval) return;
        accumulated = 0f;
        Round();
    }

    // One round. Public so a test can drive it without waiting for frames. Returns how many monsters it asked the entity host for.
    public int Round()
    {
        if (config == null) return 0;
        Rounds++;
        if (skip > 0) { skip--; SkippedRounds++; return 0; }
        var settings = config.Settings;
        var index = PNWorldEntityIndex.For(World); index.Refresh(true);
        candidates.Clear(); index.All(candidates); players.Clear(); enemies.Clear();
        foreach (var stat in candidates)
        {
            var loader = stat.GetComponent<EveEntityLoader>();
            if (loader == null || !loader.IsReady || stat.IsDead) continue;
            var p = stat.transform.position;
            if (PNEntityRoles.IsPlayer(loader)) players.Add((p.x, p.z)); else if (stat is PNEnemyStat) enemies.Add((p.x, p.z));
        }
        if (players.Count == 0) return 0;
        // A change of the placed things changes the spawn sources; the list is rebuilt only then.
        if (sources == null || placementCount != Controller.Placements.Count) { sources = PNSpawnPlanner.SourcesFrom(Controller.Placements, Controller.Data.CellSize); placementCount = Controller.Placements.Count; }
        int worldCount = enemies.Count + building.Count, asked = 0;
        var host = Intercept == null ? PNEntityHosts.For(World.Session) : null;
        for (int i = 0; i < players.Count && worldCount < settings.WorldBudget; i++)
        {
            var player = players[i];
            if (Near(enemies, player, settings.BudgetRadius) + Near(building, player, settings.BudgetRadius) >= settings.PerPlayerBudget) continue;
            var entry = PNSpawnPlanner.Choose(config.Entries, random);
            if (entry == null) break;
            planner.Prepare(player.x, player.z, settings.RingMax);
            if (!planner.TryPick(settings, entry, player.x, player.z, players, Controller.Meta, sources, random, out float x, out float z, out double scale)) continue;
            if (scale < 1 && random.NextDouble() >= scale) continue;
            var position = new Vector3(x, .05f, z);
            var request = new PNEntityRequest
            {
                Entity = entry.Entity, Template = entry.Entity, Character = entry.Character, World = World, WorldIncarnation = World.Incarnation,
                ContentSlot = "main", SceneKey = config.SceneKey, EntityKey = "", Position = position
            };
            if (Intercept != null) { if (Intercept(request)) { asked++; worldCount++; Spawned++; } continue; }
            var spot = (x, z); building.Add(spot);
            var candidate = host.TrySpawn(request, null, loader => { building.Remove(spot); Spawned++; if (!disposed) Placed?.Invoke(loader, position); },
                error => { building.Remove(spot); Failed++; Debug.LogException(error); });
            if (candidate == null) { building.Remove(spot); continue; }
            asked++; worldCount++;
        }
        return asked;
    }

    private static int Near(List<(float x, float z)> list, (float x, float z) point, float radius)
    {
        int count = 0; float r2 = radius * radius;
        foreach (var e in list) { float dx = e.x - point.x, dz = e.z - point.z; if (dx * dx + dz * dz <= r2) count++; }
        return count;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; config = null; building.Clear();
        if (despawn != null) despawn.Expired -= OnExpired;
        if (World != null) services.Remove(World);
        if (this != null) Destroy(gameObject);
    }
    private void OnDestroy() { if (!disposed) Dispose(); }
}
#endif
