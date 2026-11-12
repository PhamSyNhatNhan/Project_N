#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// EN: The home of an enemy that has one (its role data names a leash radius): where it started and how far it may stray. The despawn service sends it back when it strays beyond the leash with no
//     player near, and when its despawn timer starts. Nothing else reads it yet.
// VI: Nhà của quái có dây xích (dữ liệu vai trò có `leash`): chỗ xuất phát và bán kính được lang thang. Dịch vụ hết hạn gọi nó về khi nó vượt dây xích lúc không có người chơi gần, và khi đồng hồ hết hạn bắt đầu.
public sealed class PNEnemyHome : MonoBehaviour
{
    public Vector3 Position { get; private set; }
    public float Leash { get; private set; }
    public PNEnemyNavigationRequest Return { get; internal set; }
    public bool IsReturning => Return != null && !Return.IsTerminal;
    public void Set(Vector3 position, float leash) { Position = position; Leash = leash; Return = null; }
    public float DistanceFlat(Vector3 point) { var d = point - Position; d.y = 0; return d.magnitude; }
}

// EN: The despawn service (Docs/SandboxPlan.md, step G4), the world module `project_n:despawn`. One per world that has the feature, on the authority only. Every `Interval` seconds it measures, for each
//     enemy of the world, the flat distance to the nearest living player and applies PNDespawnRule: inside `Near` the timer resets, beyond `Far` it runs, at `Seconds` the enemy is removed through the shared
//     entity removal (the host's retire path: unloaded and returned to its pool, no drop, no kill credit). An enemy is left alone while it is exempt (role data), held (PNEntityLifetime.Hold), executing an action, or
//     marked with PNRoomClearMarker (a defensive check: the room-clear count must never lose an enemy to this service). The settings are the world's, with the role's override on top. Other modes do not get the service:
//     a roguelite room keeps every enemy until it is killed.
// VI: Dịch vụ hết hạn (G4), module world `project_n:despawn`. Mỗi world có feature một cái, chỉ ở authority. Mỗi `Interval` giây đo khoảng cách phẳng tới người chơi còn sống gần nhất cho từng quái và áp PNDespawnRule;
//     đến hạn thì gỡ qua đường gỡ thực thể chung (không rơi vật phẩm, không tính hạ gục). Bỏ qua quái miễn trừ, đang bị giữ, đang chạy hành động, hoặc có PNRoomClearMarker.
public sealed class PNDespawnService : MonoBehaviour
{
    public static EveId Feature => PNDespawnIds.Feature;
    private static readonly Dictionary<EveWorldContext, PNDespawnService> services = new Dictionary<EveWorldContext, PNDespawnService>();
    private readonly Dictionary<PNStat, PNDespawnState> states = new Dictionary<PNStat, PNDespawnState>();
    private readonly List<PNStat> candidates = new List<PNStat>();
    private readonly List<Vector3> players = new List<Vector3>();
    private readonly List<PNStat> stale = new List<PNStat>();
    private float accumulated;
    private bool disposed;

    public EveWorldContext World { get; private set; }
    public PNDespawnSettings Settings { get; private set; }
    // Removes a published entity; by default the session's entity host. A fixture that has no host sets its own.
    public Func<EveEntityLoader, bool> Retire { get; set; }
    public int Removed { get; private set; }
    public int Tracked => states.Count;
    public int Scans { get; private set; }

    public static PNDespawnService Find(EveWorldContext world) => world != null && services.TryGetValue(world, out var value) ? value : null;

    public static PNDespawnService Create(EveWorldContext world, UnityEngine.SceneManagement.Scene scene, PNDespawnSettings settings = null)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (services.ContainsKey(world)) throw new InvalidOperationException("This world already has a despawn service.");
        var root = new GameObject("PN Despawn Service"); root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var service = root.AddComponent<PNDespawnService>();
        service.World = world; service.Settings = settings ?? PNDespawnSettings.Default;
        service.Retire = loader => { var host = PNEntityHosts.Find(world.Session); return host != null && host.Retire(loader); };
        services[world] = service; root.SetActive(true);
        return service;
    }

    private void Update()
    {
        if (disposed || World == null || !World.CanExecute || !World.Session.HasAuthority) return;
        accumulated += Time.deltaTime;
        if (accumulated < Settings.Interval) return;
        float delta = accumulated; accumulated = 0f;
        Scan(delta);
    }

    // One pass over the world's entities with `delta` seconds since the last one. Public so a test can drive it without waiting for frames.
    public void Scan(float delta)
    {
        Scans++;
        var index = PNWorldEntityIndex.For(World); index.Refresh(true);
        candidates.Clear(); index.All(candidates);
        players.Clear();
        foreach (var stat in candidates)
        {
            var loader = stat.GetComponent<EveEntityLoader>();
            if (loader != null && loader.IsReady && !stat.IsDead && PNEntityRoles.IsPlayer(loader)) players.Add(stat.transform.position);
        }
        foreach (var stat in candidates)
        {
            if (!(stat is PNEnemyStat) || !stat.IsReady) continue;
            Evaluate(stat, delta);
        }
        // Forget what no longer exists.
        stale.Clear();
        foreach (var pair in states) if (pair.Key == null || !pair.Key.IsReady) stale.Add(pair.Key);
        foreach (var key in stale) states.Remove(key);
    }

    private void Evaluate(PNStat stat, float delta)
    {
        var loader = stat.GetComponent<EveEntityLoader>(); if (loader == null || !loader.IsReady) return;
        var role = stat.GetComponent<PNEntityRole>();
        var over = role?.Despawn;
        states.TryGetValue(stat, out var state);
        var skill = stat.GetComponent<PNEnemySkill>();
        bool busy = skill != null && skill.IsExecutingEnemyAction;
        bool exempt = stat.IsDead || stat.GetComponent<PNRoomClearMarker>() != null || (over != null && over.Exempt) || busy;
        var life = stat.GetComponent<PNEntityLifetime>();
        bool held = life != null && life.IsHeld;
        float distance = players.Count == 0 ? float.PositiveInfinity : Nearest(stat.transform.position);
        var settings = Settings.With(over);
        bool wasRunning = state.Running;
        var verdict = PNDespawnRule.Step(ref state, distance, exempt, held, delta, settings);
        states[stat] = state;
        Home(stat, over, distance, !wasRunning && state.Running, settings);
        if (verdict != PNDespawnVerdict.Expire) return;
        states.Remove(stat);
        if (Retire != null && Retire(loader)) Removed++;
    }

    private float Nearest(Vector3 point)
    {
        float best = float.PositiveInfinity;
        foreach (var p in players) { float dx = p.x - point.x, dz = p.z - point.z; best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz)); }
        return best;
    }

    // An enemy with a leash walks home when its timer starts, or when it is beyond the leash with no player within `Near`.
    private void Home(PNStat stat, PNDespawnOverride over, float distance, bool timerStarted, PNDespawnSettings settings)
    {
        if (over == null || over.Leash <= 0f) return;
        var home = stat.GetComponent<PNEnemyHome>();
        if (home == null) { home = stat.gameObject.AddComponent<PNEnemyHome>(); home.Set(stat.transform.position, over.Leash); return; }
        var move = stat.GetComponent<PNEnemyMove>(); if (move == null || home.IsReturning) return;
        bool strayed = home.DistanceFlat(stat.transform.position) > home.Leash && distance > settings.Near;
        if (!timerStarted && !strayed) return;
        if (home.DistanceFlat(stat.transform.position) < .75f) return;
        home.Return = move.GoTo(home.Position, .5f);
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (World != null) services.Remove(World);
        states.Clear(); candidates.Clear(); players.Clear();
        if (this != null) DestroyImmediate(gameObject);
    }

    private void OnDestroy() { if (!disposed) { disposed = true; if (World != null) services.Remove(World); } }
}
#endif
