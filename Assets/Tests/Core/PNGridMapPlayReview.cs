#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Fixture for Docs/SandboxPlan.md step G9 (Play Mode in the main Editor from an EMPTY scene, nothing saved): a world built from a registered map (generated, with a map block), the spawner module, and the save keeping the map block.
//   a generated map is registered by id; a world whose descriptor names it is built (ground from the generator, the map block on the controller, the player entry at the map's spawn); the spawner is idle until configured;
//   rounds with a living player ask for spots in the ring, away from the player, on walkable ground, in the budget; a spawn-block zone is never used; the despawn service retires an enemy and one round is skipped;
//   the world is saved and restored and keeps its map block. Requests are taken by `PNSpawnerService.Intercept` (no real monster is built). Read `results.txt`.
public sealed class PNGridMapReviewRuntime : EveRuntime
{
    [NonSerialized] public EveContentDocument[] Documents;
    public override string RulesVersion => EveGameRuntime.CurrentRulesVersion;
    protected override void Awake() { }
    protected override IEnumerable<EveIContentModule> CreateContentModules() => new List<EveIContentModule> { new PNContentModule(), new PNGameplayContentModule(), new PNGridMapReviewWorlds() };
    protected override IEnumerable<EveContentDocument> ReadContentDocuments() => Documents;
}

public sealed class PNGridMapReviewWorlds : EveIContentModule
{
    public const string MapId = "review:g9_map";
    public static readonly EveId World = EveId.Parse("project_n:review_g9_map");
    public void Register(EveRegistryHub hub)
    {
        if (!PNGridMaps.Has(MapId))
        {
            var meta = new PNGridMapMeta();
            meta.Spawns.Add(new PNMapSpawn { Name = "start", Kind = PNGridMapMeta.PlayerKind, X = 64, Z = 64 });
            meta.Zones.Add(new PNMapZone { Name = "safe", Region = new PNGridRegion(100, 60, 127, 68), SpawnBlock = true });
            PNGridMaps.RegisterGenerated(MapId, JArray.Parse("[{\"step\":\"noise_ground\",\"ground\":\"project_n:sand\",\"scale\":12,\"threshold\":0.5}]"), null, meta);
        }
        var descriptor = new PNGridWorldDescriptor(128, 128, .5f, MapId, 11);
        hub.GetOrCreate<PNWorldDefinition>().Register(new PNWorldDefinition(World, PNWorldDefinition.HashContent("review/g9map"), typeof(PNGridWorldProvider), () => new PNGridWorldProvider(descriptor),
            new PNWorldPolicy(PNEmptyWorldPolicy.Continue, null, null, null, default(EveId), new[] { PNGridIds.GridWorldFeature, PNDespawnIds.Feature, PNGridSaveIds.Feature, PNSpawnerIds.Feature })));
    }
}

public sealed class PNGridMapPlayReview : MonoBehaviour
{
    public static event Action<bool, string> Finished;
    private readonly List<string> evidence = new List<string>();
    private readonly List<GameObject> actors = new List<GameObject>();
    private long nextEntity = 994700;
    private EveEntityPreparationScheduler scheduler;
    private PNWorldRuntime worlds;
    private EveSessionContext session;
    private PNGridMapReviewRuntime runtime;
    private PNSyncService sync;
    private PNSyncWorldBinder binder;
    private EveWorldContext world;
    private string output, storeRoot;
    private int errors, checks;
    private float previousTimeScale; private int previousFrameRate, previousVSync; private bool previousBackground;

    public static void Begin(string directory)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Start the grid map review in Play Mode.");
        var root = new GameObject("Grid Map Review"); DontDestroyOnLoad(root);
        root.AddComponent<PNGridMapPlayReview>().Go(directory);
    }

    private void Go(string directory)
    {
        output = directory; Directory.CreateDirectory(output);
        storeRoot = Path.Combine(output, "store-g9");
        if (Directory.Exists(storeRoot)) Directory.Delete(storeRoot, true);
        previousTimeScale = Time.timeScale; previousFrameRate = Application.targetFrameRate; previousVSync = QualitySettings.vSyncCount; previousBackground = Application.runInBackground;
        Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0; Application.runInBackground = true;
        Application.logMessageReceived += Logged;
        StartCoroutine(Drive(Run()));
    }

    private void Check(bool ok, string name) { checks += ok ? 1 : 0; evidence.Add((ok ? "PASS " : "FAIL ") + name); if (!ok) throw new Exception(name); }
    private void Note(string text) => evidence.Add("NOTE " + text);
    private void Logged(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception) return;
        errors++; File.AppendAllText(Path.Combine(output, "errors.txt"), message + "\n" + stack + "\n");
    }
    private void Update() { if (scheduler != null && (session?.IsActive == true || EveRegistrySession.State == EveSessionState.Stopping)) scheduler.AdvanceFrame(); }

    private IEnumerator Drive(IEnumerator routine)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(routine); Exception failure = null;
        while (stack.Count > 0)
        {
            object yielded = null; bool more = false;
            try { more = stack.Peek().MoveNext(); if (more) yielded = stack.Peek().Current; }
            catch (Exception e) { failure = e; break; }
            if (!more) { stack.Pop(); continue; }
            if (yielded is IEnumerator nested) stack.Push(nested); else yield return yielded;
        }
        if (failure != null) { evidence.Add("FAIL " + failure); Debug.LogWarning("Grid map review failure: " + failure); }
        Time.timeScale = previousTimeScale;
        foreach (var actor in actors.Where(x => x != null))
        { try { actor.GetComponent<EveEntityLoader>()?.Unload(); actor.GetComponent<EveEntityIdentity>()?.ResetIdentity(); } catch (Exception e) { failure = failure ?? e; evidence.Add("CLEANUP " + e); } Destroy(actor); }
        actors.Clear();
        try { binder?.Dispose(); } catch (Exception e) { failure = failure ?? e; evidence.Add("BINDER " + e); }
        try { runtime?.EndSession(); } catch (Exception e) { failure = failure ?? e; evidence.Add("STOP " + e); }
        float deadline = Time.realtimeSinceStartup + 20;
        while (EveRegistrySession.State != EveSessionState.Stopped && Time.realtimeSinceStartup < deadline) { EveRegistrySession.PumpCleanup(); yield return null; }
        try { if (EveRegistrySession.State == EveSessionState.Stopped) runtime?.UnloadContent(); } catch (Exception e) { failure = failure ?? e; evidence.Add("CONTENT CLEANUP " + e); }
        PNGridMaps.Remove(PNGridMapReviewWorlds.MapId);
        Application.logMessageReceived -= Logged;
        Application.targetFrameRate = previousFrameRate; QualitySettings.vSyncCount = previousVSync; Application.runInBackground = previousBackground;
        bool pass = failure == null && errors == 0 && EveRegistrySession.State == EveSessionState.Stopped;
        evidence.Add($"RESULT {(pass ? "PASS" : "FAIL")} checks={checks} errors={errors} session={EveRegistrySession.State}");
        File.WriteAllLines(Path.Combine(output, "results.txt"), evidence);
        Finished?.Invoke(pass, output); Destroy(gameObject);
    }

    private IEnumerator Until(Func<bool> condition, float seconds, string name)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(condition(), name);
    }

    private IEnumerator WaitReady(EveWorldContext target, string label, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (target.State != EveWorldState.Ready && !target.CanExecute && target.State != EveWorldState.Failed && Time.realtimeSinceStartup < deadline) yield return null;
        if (!target.CanExecute) throw new Exception(label + " did not become ready: state " + target.State + ", failure " + (target.Failure ?? target.FirstFailure));
    }

    private IEnumerator Player(Vector3 position, Action<PNPlayerStat> made)
    {
        var root = new GameObject("Grid map review player " + nextEntity) { layer = 7 }; root.SetActive(false); actors.Add(root);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, world.PhysicsOwner); root.transform.position = position;
        var identity = root.AddComponent<EveEntityIdentity>(); identity.Assign(new EveEntityId(nextEntity++), EveId.Parse("project_n:eve"), 0); identity.BindWorld(world, "main");
        root.AddComponent<EveTimeScale>(); var stat = root.AddComponent<PNPlayerStat>().AsPlayer(); root.AddComponent<PNAbilityManager>();
        var loader = root.AddComponent<EveEntityLoader>(); loader.Load(session.Registry); root.SetActive(true);
        yield return Until(() => loader.IsReady, 20, "a player entity is loaded in the map world");
        made(stat);
    }

    private IEnumerator Run()
    {
        Check(Application.isPlaying && EveRegistrySession.State == EveSessionState.Stopped, "starts in Play Mode with no running session");
        var documents = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        runtime = gameObject.AddComponent<PNGridMapReviewRuntime>(); runtime.Documents = documents;
        runtime.BeginSession(EveSessionRole.Offline); session = EveRegistrySession.Context;
        scheduler = new EveEntityPreparationScheduler(session, PNGameBootstrap.Config.Preparation); worlds = new PNWorldRuntime(session, scheduler);
        gameObject.AddComponent<PNWorldPhysicsLoop>().Bind(session);
        sync = new PNSyncService(session.Registry, true, 0, session); binder = new PNSyncWorldBinder(worlds, sync);

        // ---- a world built from the registered map ----
        world = worlds.Create(PNGridMapReviewWorlds.World);
        yield return WaitReady(world, "the map world", 60);
        var controller = PNGridWorldController.Find(world); var data = controller.Data;
        Check(PNGridMaps.Has(PNGridMapReviewWorlds.MapId), "the generated map is registered by id");
        Check(controller.Meta != null && controller.Meta.Spawns.Count == 1 && controller.Meta.Zones.Count == 1, "the controller has the map block of the map");
        int sand = Array.IndexOf(data.Palette.ToArray(), "project_n:sand"), sandCells = 0;
        for (int z = 0; z < data.Depth; z++) for (int x = 0; x < data.Width; x++) if (data.GroundAt(x, z) == sand) sandCells++;
        Check(sand >= 0 && sandCells > 500 && sandCells < data.Width * data.Depth - 500, "the ground comes from the generator (sand and the default ground both present: " + sandCells + " sand cells)");

        // ---- the spawner ----
        var spawner = PNSpawnerService.Find(world);
        Check(spawner != null && !spawner.Configured, "the world has a spawner, idle until configured");
        Check(spawner.Round() == 0 && spawner.Rounds == 0, "an unconfigured spawner does nothing");
        PNStat playerStat = null;
        yield return Player(new Vector3(32.25f, 0, 32.25f), p => playerStat = p);
        var requests = new List<Vector3>();
        spawner.Intercept = request => { requests.Add(request.Position); return true; };
        var settings = new PNSpawnSettings { PerPlayerBudget = 4, WorldBudget = 10, Interval = 600 };
        var entry = new PNSpawnEntry { Entity = "project_n:test_enemy", Character = "project_n:test_enemy", Weight = 1 };
        spawner.Configure(new PNSpawnerConfig { Settings = settings, Entries = new List<PNSpawnEntry> { entry } });
        Check(spawner.Configured, "a configured spawner is on");
        var player = playerStat.transform.position;
        int asked = spawner.Round();
        Check(asked == 1 && requests.Count == 1, "one round asks for one monster for the one player (" + asked + ")");
        float d = new Vector2(requests[0].x - player.x, requests[0].z - player.z).magnitude;
        Check(d >= settings.SafeDistance - .01f && d <= settings.RingMax + data.CellSize, "the spot is in the ring around the player (" + d.ToString("F1") + " m)");
        int cx = (int)(requests[0].x / data.CellSize), cz = (int)(requests[0].z / data.CellSize);
        Check(data.InBounds(cx, cz) && !data.IsSolid(cx, cz), "the spot is on open ground");
        for (int i = 0; i < 40; i++) spawner.Round();
        Check(requests.Count > 1 && requests.All(r => !(r.x / data.CellSize >= 100 && r.z / data.CellSize >= 60 && r.z / data.CellSize <= 69)), "no spot under the spawn-block zone in " + requests.Count + " requests");
        Check(requests.All(r => new Vector2(r.x - player.x, r.z - player.z).magnitude >= settings.SafeDistance - .01f), "no spot closer than the safe distance");
        Check(spawner.Spawned == requests.Count, "the spawner counts what was taken");

        // ---- a retired monster skips one round ----
        int before = requests.Count, rounds = spawner.Rounds, skipped = spawner.SkippedRounds;
        var despawn = PNDespawnService.Find(world);
        Check(despawn != null, "the world has the despawn service");
        var field = typeof(PNDespawnService).GetField("Expired", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        ((Action)field.GetValue(despawn))?.Invoke();   // what the service raises when it retires a monster
        Check(spawner.Round() == 0 && spawner.SkippedRounds == skipped + 1, "after a monster is retired the next round is skipped");
        Check(spawner.Round() >= 0 && spawner.Rounds > rounds, "rounds go on after that");
        spawner.Intercept = null; spawner.Disable();
        Check(!spawner.Configured, "Disable turns the spawner off");

        // ---- save and restore keep the map block ----
        var service = PNGridWorldSaveService.Find(world);
        PNWorldFileStore.ValidateKey("g9-world");
        var store = new PNWorldFileStore(storeRoot);
        service.Configure(store, "g9-world");
        Check(service.SaveNow() || service.IsDirty(), "the first save starts");
        yield return Until(() => !service.Busy, 60, "the save finishes");
        Check(service.LastError == null, "the save had no error");
        Task<PNGridSaveRead> task = PNGridWorldSaves.ReadAsync(store, "g9-world", session.Compatibility);
        yield return Until(() => task.IsCompleted, 30, "the save is read back");
        if (task.IsFaulted) throw task.Exception.GetBaseException();
        var restored = PNGridWorldSaves.Restore(worlds, session, task.Result);
        float deadline = Time.realtimeSinceStartup + 90;
        while (restored.State != EveWorldState.Ready && restored.State != EveWorldState.Failed && Time.realtimeSinceStartup < deadline) yield return null;
        if (restored.State != EveWorldState.Ready) throw new Exception("the restored world did not become ready: " + restored.State + " " + (restored.Failure ?? restored.FirstFailure));
        worlds.PublishStaged(restored);
        var restoredController = PNGridWorldController.Find(restored);
        Check(restoredController?.Meta != null && restoredController.Meta.Spawns.Count == 1 && restoredController.Meta.Zones.Count == 1 && restoredController.Meta.Zones[0].SpawnBlock, "the restored world keeps the map block");
        long a = 0, b = 0;
        unchecked { for (int z = 0; z < data.Depth; z++) for (int x = 0; x < data.Width; x++) { a = a * 31 + data.GroundAt(x, z); b = b * 31 + restoredController.Data.GroundAt(x, z); } }
        Check(a == b, "the restored ground is the saved ground");
    }
}
#endif
