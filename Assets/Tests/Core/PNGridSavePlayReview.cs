#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EveEngine;
using UnityEngine;

// Fixture for Docs/SandboxPlan.md step G7 (Play Mode in the main Editor from an EMPTY scene, nothing saved): saving a running grid world and building a world from the save.
//   small pass (`Begin(dir)`): the maze test world; saving is off until configured; a first save of edited ground, placed things (one damaged) and a living player; nothing changed means nothing is written; a small edit
//     rewrites only the chunks that changed; autosave; reading the file back; a world restored from it (same ground, same things with the same ids and health, the player's place, nothing dirty at once); a corrupted file
//     is refused; a final save when the world is unloaded; cleanup.
//   large pass (`Begin(dir, large: true)`): a 1024 x 1024 flat world (1024 chunks): the time of the first full save with the per-frame copy slice, of a one-chunk save, and of the restore.
// The saves go to a folder under the output directory, never to the real save folder. Read `results.txt`.
public sealed class PNGridSaveReviewRuntime : EveRuntime
{
    [NonSerialized] public EveContentDocument[] Documents;
    [NonSerialized] public bool Large;
    public override string RulesVersion => EveGameRuntime.CurrentRulesVersion;
    protected override void Awake() { }
    protected override IEnumerable<EveIContentModule> CreateContentModules()
    {
        var modules = new List<EveIContentModule> { new PNContentModule(), new PNGameplayContentModule() };
        if (Large) modules.Add(new PNGridSaveReviewWorlds());
        return modules;
    }
    protected override IEnumerable<EveContentDocument> ReadContentDocuments() => Documents;
}

public sealed class PNGridSaveReviewWorlds : EveIContentModule
{
    public static readonly EveId Big = EveId.Parse("project_n:review_grid_save_1024");
    public void Register(EveRegistryHub hub)
    {
        var descriptor = new PNGridWorldDescriptor(1024, 1024, .5f, PNGridIds.FlatMap.ToString(), 1);
        hub.GetOrCreate<PNWorldDefinition>().Register(new PNWorldDefinition(Big, PNWorldDefinition.HashContent("review/save1024"), typeof(PNGridWorldProvider), () => new PNGridWorldProvider(descriptor),
            new PNWorldPolicy(PNEmptyWorldPolicy.Continue, null, null, null, default(EveId), new[] { PNGridIds.GridWorldFeature, PNGridSaveIds.Feature })));
    }
}

public sealed class PNGridSavePlayReview : MonoBehaviour
{
    public static event Action<bool, string> Finished;
    private readonly List<string> evidence = new List<string>();
    private readonly List<GameObject> actors = new List<GameObject>();
    private long nextEntity = 994500;
    private EveEntityPreparationScheduler scheduler;
    private PNWorldRuntime worlds;
    private EveSessionContext session;
    private PNGridSaveReviewRuntime runtime;
    private PNSyncService sync;
    private PNSyncWorldBinder binder;
    private EveWorldContext world;
    private string output, storeRoot;
    private bool large;
    private int errors, checks;
    private float previousTimeScale; private int previousFrameRate, previousVSync; private bool previousBackground;

    public static void Begin(string directory, bool large = false)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Start the grid save review in Play Mode.");
        var root = new GameObject("Grid Save Review"); DontDestroyOnLoad(root);
        var review = root.AddComponent<PNGridSavePlayReview>(); review.large = large; review.Go(directory);
    }

    private void Go(string directory)
    {
        output = directory; Directory.CreateDirectory(output);
        storeRoot = Path.Combine(output, "store" + (large ? "-large" : ""));
        if (Directory.Exists(storeRoot)) Directory.Delete(storeRoot, true);
        previousTimeScale = Time.timeScale; previousFrameRate = Application.targetFrameRate; previousVSync = QualitySettings.vSyncCount; previousBackground = Application.runInBackground;
        Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0; Application.runInBackground = true;
        Application.logMessageReceived += Logged;
        StartCoroutine(Drive(large ? RunLarge() : RunSmall()));
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
        if (failure != null) { evidence.Add("FAIL " + failure); Debug.LogWarning("Grid save review failure: " + failure); }
        Time.timeScale = previousTimeScale;
        foreach (var actor in actors.Where(x => x != null))
        { try { actor.GetComponent<EveEntityLoader>()?.Unload(); actor.GetComponent<EveEntityIdentity>()?.ResetIdentity(); } catch (Exception e) { failure = failure ?? e; evidence.Add("CLEANUP " + e); } Destroy(actor); }
        actors.Clear();
        try { binder?.Dispose(); } catch (Exception e) { failure = failure ?? e; evidence.Add("BINDER " + e); }
        try { runtime?.EndSession(); } catch (Exception e) { failure = failure ?? e; evidence.Add("STOP " + e); }
        float deadline = Time.realtimeSinceStartup + 20;
        while (EveRegistrySession.State != EveSessionState.Stopped && Time.realtimeSinceStartup < deadline) { EveRegistrySession.PumpCleanup(); yield return null; }
        try { if (EveRegistrySession.State == EveSessionState.Stopped) runtime?.UnloadContent(); } catch (Exception e) { failure = failure ?? e; evidence.Add("CONTENT CLEANUP " + e); }
        Application.logMessageReceived -= Logged;
        Application.targetFrameRate = previousFrameRate; QualitySettings.vSyncCount = previousVSync; Application.runInBackground = previousBackground;
        bool pass = failure == null && errors == 0 && EveRegistrySession.State == EveSessionState.Stopped;
        evidence.Add($"RESULT {(pass ? "PASS" : "FAIL")} checks={checks} errors={errors} session={EveRegistrySession.State}");
        File.WriteAllLines(Path.Combine(output, large ? "results-large.txt" : "results.txt"), evidence);
        Finished?.Invoke(pass, output); Destroy(gameObject);
    }

    private IEnumerator Until(Func<bool> condition, float seconds, string name)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(condition(), name);
    }

    private void StartSession()
    {
        var documents = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        runtime = gameObject.AddComponent<PNGridSaveReviewRuntime>(); runtime.Documents = documents; runtime.Large = large;
        runtime.BeginSession(EveSessionRole.Offline); session = EveRegistrySession.Context;
        scheduler = new EveEntityPreparationScheduler(session, PNGameBootstrap.Config.Preparation); worlds = new PNWorldRuntime(session, scheduler);
        gameObject.AddComponent<PNWorldPhysicsLoop>().Bind(session);
        sync = new PNSyncService(session.Registry, true, 0, session); binder = new PNSyncWorldBinder(worlds, sync);
    }

    private IEnumerator CreateWorld(EveId id, float seconds)
    {
        double started = Time.realtimeSinceStartupAsDouble;
        world = worlds.Create(id);
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!world.CanExecute && world.State != EveWorldState.Failed && Time.realtimeSinceStartup < deadline) yield return null;
        if (!world.CanExecute) throw new Exception("the world did not become ready: state " + world.State + ", failure " + (world.Failure ?? world.FirstFailure));
        Note("world ready in " + ((Time.realtimeSinceStartupAsDouble - started) * 1000).ToString("F0") + " ms");
    }

    // A fingerprint of every cell: ground type, floor height and solid flag.
    private static long Fingerprint(PNGridWorldData data)
    {
        unchecked
        {
            long hash = 1469598103934665603L;
            for (int z = 0; z < data.Depth; z++) for (int x = 0; x < data.Width; x++)
                hash = (hash ^ (data.GroundAt(x, z) * 31 + data.HeightRaw(x, z) * 17 + (data.IsSolid(x, z) ? 1 : 0))) * 1099511628211L;
            return hash;
        }
    }

    // Places `definition` on the first free cell found around (x, z) (the maze has walls where a fixed cell may be); null when none within 8 cells.
    private PNPlacedObject Put(PNGridWorldController controller, PNPlaceableDefinition definition, int x, int z, int direction = 0)
    {
        for (int ring = 0; ring <= 8; ring++)
            for (int dz = -ring; dz <= ring; dz++) for (int dx = -ring; dx <= ring; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue;
                if (controller.Placements.TryPlace(definition, x + dx, z + dz, direction, out var placed, out _)) return placed;
            }
        return null;
    }

    private PNPlaceableDefinition Def(EveId id) => session.Registry.Instances().GetEffectiveKind<PNPlaceableDefinition>(id);

    // Waits (frame by frame) until the service is idle and returns the longest frame time seen meanwhile.
    private IEnumerator WaitSaved(PNGridWorldSaveService service, float seconds, string name, Action<float> longestFrame = null)
    {
        float deadline = Time.realtimeSinceStartup + seconds; float longest = 0;
        while (service.Busy && Time.realtimeSinceStartup < deadline) { yield return null; longest = Mathf.Max(longest, Time.unscaledDeltaTime); }
        Check(!service.Busy && service.LastError == null, name);
        longestFrame?.Invoke(longest);
    }

    private IEnumerator Restore(PNGridWorldSaveService source, PNWorldFileStore store, string key, Action<EveWorldContext> done, Action<PNGridSaveRead> read = null)
    {
        Task<PNGridSaveRead> task = PNGridWorldSaves.ReadAsync(store, key, session.Compatibility);
        yield return Until(() => task.IsCompleted, 30, "the save is read and checked on a worker");
        if (task.IsFaulted) throw task.Exception.GetBaseException();
        read?.Invoke(task.Result);
        double started = Time.realtimeSinceStartupAsDouble;
        var restored = PNGridWorldSaves.Restore(worlds, session, task.Result);
        float deadline = Time.realtimeSinceStartup + 90;
        while (restored.State != EveWorldState.Ready && restored.State != EveWorldState.Failed && Time.realtimeSinceStartup < deadline) yield return null;
        if (restored.State != EveWorldState.Ready) throw new Exception("the restored world did not become ready: state " + restored.State + ", failure " + (restored.Failure ?? restored.FirstFailure));
        Note("restored world ready in " + ((Time.realtimeSinceStartupAsDouble - started) * 1000).ToString("F0") + " ms");
        worlds.PublishStaged(restored);
        done(restored);
    }

    // ---------------------------------------------------------------------------------------------------------
    private IEnumerator RunSmall()
    {
        Check(Application.isPlaying && EveRegistrySession.State == EveSessionState.Stopped, "starts in Play Mode with no running session");
        StartSession();
        yield return CreateWorld(PNGridIds.SandboxTestMaze, 60);
        var controller = PNGridWorldController.Find(world); var data = controller.Data;
        var service = PNGridWorldSaveService.Find(world);
        Check(service != null && service.Store == null && PNGridRestoreStaging.Waiting == 0, "a world with the feature has a save service, idle until configured");
        Check(!service.SaveNow() && service.Saves == 0, "an unconfigured service writes nothing");
        Check(PNGridWorldSaveService.Find(null) == null, "no world, no service");

        // ---- a player and some placed things ----
        var near = new Vector3(5.25f, 0, 5.25f);
        var playerRoot = new GameObject("Grid save review player") { layer = 7 }; playerRoot.SetActive(false); actors.Add(playerRoot);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(playerRoot, world.PhysicsOwner); playerRoot.transform.position = near;
        var playerIdentity = playerRoot.AddComponent<EveEntityIdentity>(); playerIdentity.Assign(new EveEntityId(nextEntity++), EveId.Parse("project_n:eve"), 0); playerIdentity.BindWorld(world, "main");
        playerRoot.AddComponent<EveTimeScale>(); var playerStat = playerRoot.AddComponent<PNPlayerStat>().AsPlayer(); playerRoot.AddComponent<PNAbilityManager>();
        var playerLoader = playerRoot.AddComponent<EveEntityLoader>(); playerLoader.Load(session.Registry); playerRoot.SetActive(true);
        yield return Until(() => playerLoader.IsReady, 20, "the player entity is loaded in the grid world");
        var wall = Def(PNPlaceableIds.Vanish); var marker = Def(PNPlaceableIds.Marker);
        var first = Put(controller, wall, 12, 12); var second = Put(controller, wall, 20, 14, 1); var third = Put(controller, marker, 30, 20);
        Check(first != null && second != null && third != null, "three things are placed");
        controller.Pump(); AstarPath.active.FlushWorkItems();
        controller.Placements.Damage(first.Id, PNGridCommands.HitDamage);
        Check(controller.Placements.Get(first.Id) != null && first.Health < wall.Settings.Health, "one of them is damaged but stands");
        long fingerprint = Fingerprint(data); int placed = controller.Placements.Count; int firstHealth = first.Health;

        // ---- the first save ----
        PNWorldFileStore.ValidateKey("review-world");
        var store = new PNWorldFileStore(storeRoot);
        service.Configure(store, "review-world");
        Check(service.IsDirty() && service.SaveNow() && service.Busy, "after Configure the first save starts, because every chunk is new");
        bool clean = false; float longest = 0;
        yield return WaitSaved(service, 30, "the first save finishes", f => longest = f);
        Check(service.Saves == 1 && service.LastRevision >= 1 && service.LastBytes > 0 && service.LastChunksCopied == data.ChunkCount, "the first save copied every chunk and wrote revision " + service.LastRevision + " (" + service.LastBytes + " bytes)");
        Note("first save: " + service.LastChunksCopied + " chunks, longest copy slice " + service.LongestSliceMilliseconds.ToString("F2") + " ms, longest service frame " + service.LongestUpdateMilliseconds.ToString("F2") + " ms, longest frame " + (longest * 1000).ToString("F1") + " ms");
        Check(!service.IsDirty() && !service.SaveNow() && service.Saves == 1, "nothing changed, so nothing is written");
        long firstRevision = service.LastRevision;

        // ---- a small edit rewrites only what changed ----
        int secondHealth = second.Health; controller.Placements.Damage(second.Id, PNGridCommands.HitDamage);
        Check(second.Health < secondHealth, "a placed thing is hit");
        Check(service.IsDirty() && service.SaveNow(), "a hit makes the world dirty and starts a save");
        yield return WaitSaved(service, 30, "the second save finishes");
        Check(service.Saves == 2 && service.LastRevision > firstRevision, "the second save is a newer revision");
        Check(controller.Placements.Remove(third.Id), "a third thing is removed");
        controller.Pump(); AstarPath.active.FlushWorkItems();
        Check(Put(controller, wall, 40, 30) != null, "a wall is put on another cell");
        controller.Pump(); AstarPath.active.FlushWorkItems();
        Check(service.SaveNow(), "a save starts after the edit");
        yield return WaitSaved(service, 30, "the third save finishes");
        Check(service.LastChunksCopied > 0 && service.LastChunksCopied < data.ChunkCount, "a small edit copies only the chunks that changed (" + service.LastChunksCopied + " of " + data.ChunkCount + ")");
        fingerprint = Fingerprint(data); placed = controller.Placements.Count;

        // ---- autosave ----
        service.Interval = .4f;
        Check(Put(controller, wall, 50, 40) != null, "one more wall is placed");
        controller.Pump(); AstarPath.active.FlushWorkItems();
        int savesBefore = service.Saves;
        yield return Until(() => service.Saves > savesBefore, 10, "the autosave writes the change without being asked");
        yield return WaitSaved(service, 10, "the autosave is done");
        service.Interval = 300f;
        fingerprint = Fingerprint(data); placed = controller.Placements.Count;

        // ---- read it back ----
        PNGridSaveRead loaded = null; EveWorldContext second1 = null;
        yield return Restore(service, store, "review-world", w => second1 = w, r => loaded = r);
        Check(loaded.Revision == service.LastRevision && loaded.Descriptor.Map == PNGridIds.TestMazeMap.ToString() && loaded.Descriptor.Restore == null && !loaded.Recovered, "the file read back is the last revision, with the descriptor of the world and no restore token");
        Check(loaded.Snapshot.Placed.Count == placed && loaded.Snapshot.Players.Count == 1 && loaded.Snapshot.Players[0].Key == "owner:0", "it holds the placed things and the one living player under owner:0");
        Check(Mathf.Abs(loaded.Snapshot.Players[0].X - near.x) < .01f && Mathf.Abs(loaded.Snapshot.Players[0].Z - near.z) < .01f, "the player's saved place is where the player stood");
        Check(PNGridRestoreStaging.Waiting == 0, "the restore token was used up by the load");
        var data2 = PNGridWorldController.Find(second1).Data; var controller2 = PNGridWorldController.Find(second1); var service2 = PNGridWorldSaveService.Find(second1);
        Check(Fingerprint(data2) == fingerprint, "the restored world has the same ground, floor heights and solid cells");
        Check(controller2.Placements.Count == placed && controller2.Placements.All.All(p => { var o = controller.Placements.At(p.X, p.Z); return o != null && o.Id == p.Id && o.Health == p.Health && o.Definition.Id == p.Definition.Id; }), "the same things stand on the same cells with the same ids and health");
        Check(controller2.Placements.All.Any(p => p.Id == first.Id && p.Health == firstHealth), "the damaged thing kept its health");
        Check(service2 != null && !service2.IsDirty() && service2.Players.ContainsKey("owner:0"), "the restored world is not dirty, and remembers where the player stood");
        Check(PNWorldNavigation.Find(second1) != null && PNWorldNavigation.Find(second1).TryProject(new Vector3(.75f, 0, .75f), .1f, out _) == PNWorldNavigation.Find(world).TryProject(new Vector3(.75f, 0, .75f), .1f, out _), "navigation answers the same in both worlds");
        // a new edit in the restored world writes only what changed since the restore
        var wall2 = Def(PNPlaceableIds.Vanish);
        Check(Put(controller2, wall2, 60, 50) != null, "an edit in the restored world");
        controller2.Pump(); AstarPath.active.FlushWorkItems();
        service2.Configure(store, "review-restored");
        Check(service2.IsDirty(), "the restored world is dirty once edited, and unconfigured so far");
        Check(service2.SaveNow(), "the restored world saves under its own key");
        yield return WaitSaved(service2, 30, "the restored world's save finishes");
        Check(service2.LastChunksCopied < data2.ChunkCount, "it copied only the changed chunks (" + service2.LastChunksCopied + " of " + data2.ChunkCount + ") because the restore was its baseline");
        worlds.Unload(second1);
        yield return null;

        // ---- a corrupted file is refused ----
        var records = Directory.GetFiles(Path.Combine(storeRoot, "review-world"), "*.record").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Check(records.Length >= 2, "the store keeps the two newest revisions of the world");
        var originals = records.Select(File.ReadAllBytes).ToArray();
        void Damage(int index) { var bytes = (byte[])originals[index].Clone(); bytes[bytes.Length / 2] ^= 0x5A; File.WriteAllBytes(records[index], bytes); }
        void Repair() { for (int i = 0; i < records.Length; i++) File.WriteAllBytes(records[i], originals[i]); }
        Damage(records.Length - 1);
        var newestBroken = PNGridWorldSaves.ReadAsync(new PNWorldFileStore(storeRoot), "review-world", session.Compatibility);
        yield return Until(() => newestBroken.IsCompleted, 30, "the read with a damaged newest revision returns");
        Check(!newestBroken.IsFaulted && newestBroken.Result.Recovered && newestBroken.Result.Revision < service.LastRevision, "a damaged newest revision falls back to the one before it, marked recovered");
        for (int i = 0; i < records.Length; i++) Damage(i);
        var allBroken = PNGridWorldSaves.ReadAsync(new PNWorldFileStore(storeRoot), "review-world", session.Compatibility);
        yield return Until(() => allBroken.IsCompleted, 30, "the read with every revision damaged returns");
        Check(allBroken.IsFaulted, "when every revision is damaged the read is refused, never half read");
        Repair();
        Check(PNGridRestoreStaging.Take("0123456789abcdef") == null, "an unknown restore token gives nothing");

        // ---- a final save when the world goes away ----
        Check(Put(controller, wall, 70, 40) != null, "a last wall is placed");
        controller.Pump(); AstarPath.active.FlushWorkItems();
        long beforeUnload = service.LastRevision; Check(service.IsDirty(), "an edit is waiting");
        foreach (var left in actors.ToArray())
        {
            try { left.GetComponent<EveEntityLoader>()?.Unload(); left.GetComponent<EveEntityIdentity>()?.ResetIdentity(); } catch (Exception e) { Debug.LogWarning("fixture drop: " + e.Message); }
            actors.Remove(left); Destroy(left);
        }
        yield return null; yield return null;
        bool unloaded = false; float until = Time.realtimeSinceStartup + 20;
        while (!unloaded && Time.realtimeSinceStartup < until) { unloaded = worlds.Unload(world); if (!unloaded) yield return null; }
        Check(unloaded, "the world is unloaded while dirty");
        yield return null; yield return null;
        Check(PNGridWorldSaveService.Find(world) == null && FindObjectsByType<PNGridWorldSaveService>(FindObjectsInactive.Include).Length == 0, "the save service is forgotten and no component is left");
        var after = store.Read("review-world");
        Check(after.Revision > beforeUnload, "unloading the world wrote a final save (revision " + after.Revision + ")");
        Note("small pass: " + checks + " checks");
    }

    // ---------------------------------------------------------------------------------------------------------
    private IEnumerator RunLarge()
    {
        StartSession();
        yield return CreateWorld(PNGridSaveReviewWorlds.Big, 240);
        var controller = PNGridWorldController.Find(world); var data = controller.Data; var service = PNGridWorldSaveService.Find(world);
        Check(data.Width == 1024 && data.Depth == 1024 && data.ChunkCount == 1024, "a 1024 x 1024 world of 1024 chunks");
        var wall = Def(PNPlaceableIds.Vanish); int placed = 0;
        for (int i = 0; i < 100; i++) if (controller.Placements.TryPlace(wall, 20 + (i % 10) * 90, 20 + (i / 10) * 90, 0, out _, out _)) placed++;
        Check(placed == 100, "100 things are placed over the world");
        controller.Pump(); yield return null;
        service.Configure(new PNWorldFileStore(storeRoot), "big-world");
        long fingerprint = Fingerprint(data);
        Check(service.SaveNow(), "the first save starts");
        float longestFrame = 0; double started = Time.realtimeSinceStartupAsDouble; int frames = 0;
        while (service.Busy && Time.realtimeSinceStartupAsDouble - started < 120) { yield return null; frames++; longestFrame = Mathf.Max(longestFrame, Time.unscaledDeltaTime); }
        Check(!service.Busy && service.LastError == null && service.Saves == 1, "the first full save finishes");
        Note("first save of 1024 chunks: " + ((Time.realtimeSinceStartupAsDouble - started) * 1000).ToString("F0") + " ms over " + frames + " frames, " + service.LastBytes + " bytes, longest copy slice " + service.LongestSliceMilliseconds.ToString("F2") + " ms, longest service frame " + service.LongestUpdateMilliseconds.ToString("F2") + " ms, slowest single chunk copy " + service.LongestChunkCopyMilliseconds.ToString("F3") + " ms, gen-0 collections " + service.CollectionsDuringSave + ", longest frame " + (longestFrame * 1000).ToString("F1") + " ms");
        Check(service.LastChunksCopied == 1024, "every chunk was copied by the first save");
        // a second full save with every chunk changed, now that the code is warm: a one-cell change in the middle of each chunk
        for (int cz = 0; cz < data.ChunksZ; cz++) for (int cx = 0; cx < data.ChunksX; cx++) data.SetSolid(cx * 32 + 15, cz * 32 + 15, true, false);
        Check(service.SaveNow(), "a save of all-changed chunks starts");
        started = Time.realtimeSinceStartupAsDouble; frames = 0; longestFrame = 0;
        while (service.Busy && Time.realtimeSinceStartupAsDouble - started < 120) { yield return null; frames++; longestFrame = Mathf.Max(longestFrame, Time.unscaledDeltaTime); }
        Check(!service.Busy && service.LastError == null && service.Saves == 2 && service.LastChunksCopied == 1024, "the warm full save finishes with every chunk copied");
        Note("warm full save of 1024 chunks: " + ((Time.realtimeSinceStartupAsDouble - started) * 1000).ToString("F0") + " ms over " + frames + " frames, " + service.LastBytes + " bytes, longest copy slice " + service.LongestSliceMilliseconds.ToString("F2") + " ms, longest service frame " + service.LongestUpdateMilliseconds.ToString("F2") + " ms, slowest single chunk copy " + service.LongestChunkCopyMilliseconds.ToString("F3") + " ms, gen-0 collections " + service.CollectionsDuringSave + ", longest frame " + (longestFrame * 1000).ToString("F1") + " ms");
        Check(service.LongestSliceMilliseconds < 6 && service.LongestUpdateMilliseconds < 12, "warm, no copy slice passes 6 ms and the service never holds the main thread over 12 ms (gen-0 collections in the save: " + service.CollectionsDuringSave + ")");
        // one-chunk edit
        Check(controller.Placements.TryPlace(wall, 500, 500, 0, out _, out _), "one more thing");
        controller.Pump();
        Check(service.SaveNow(), "the small save starts");
        started = Time.realtimeSinceStartupAsDouble;
        while (service.Busy && Time.realtimeSinceStartupAsDouble - started < 60) yield return null;
        Check(service.Saves == 3 && service.LastChunksCopied > 0 && service.LastChunksCopied <= 4, "a one-thing edit copies one to four chunks (" + service.LastChunksCopied + ")");
        Note("small save: " + ((Time.realtimeSinceStartupAsDouble - started) * 1000).ToString("F0") + " ms, " + service.LastBytes + " bytes");
        fingerprint = Fingerprint(data); int count = controller.Placements.Count;
        // restore
        EveWorldContext restored = null;
        yield return Restore(service, new PNWorldFileStore(storeRoot), "big-world", w => restored = w);
        var data2 = PNGridWorldController.Find(restored).Data;
        Check(Fingerprint(data2) == fingerprint && PNGridWorldController.Find(restored).Placements.Count == count, "the restored 1024 x 1024 world matches ground and things");
        Check(!PNGridWorldSaveService.Find(restored).IsDirty(), "and is not dirty");
        Note("large pass: " + checks + " checks");
    }
}
#endif
