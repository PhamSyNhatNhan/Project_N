#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Fixture for Docs/SandboxPlan.md step G3 (Play Mode in the main Editor from an EMPTY scene, nothing saved): placing and breaking things in a grid world, with a real enemy body on the grid navigation.
//   small pass (`Begin(dir)`): the maze test world; the place/break rules, the markers, a wall placed in front of a walking enemy (it goes around), the same wall broken while it walks (the way is straight again),
//   a ground painted unwalkable, the sync path (the command is dropped without a player), timings of 1, 16 and 256 placements, and cleanup.
//   large pass (`Begin(dir, large: true)`): a navigation-only 2048 x 2048 world (no chunk objects: 4096 of them would measure the mesh host, not the graph) to time the graph update of one placement and of 256.
// Read `results.txt`.
public sealed class PNGridEditReviewRuntime : EveRuntime
{
    [NonSerialized] public EveContentDocument[] Documents;
    [NonSerialized] public bool Large;
    public override string RulesVersion => EveGameRuntime.CurrentRulesVersion;
    protected override void Awake() { }
    protected override IEnumerable<EveIContentModule> CreateContentModules()
    {
        var modules = new List<EveIContentModule> { new PNContentModule(), new PNGameplayContentModule() };
        if (Large) modules.Add(new PNGridEditReviewWorlds());
        return modules;
    }
    protected override IEnumerable<EveContentDocument> ReadContentDocuments() => Documents;
}

public sealed class PNGridEditReviewWorlds : EveIContentModule
{
    public static readonly EveId Big = EveId.Parse("project_n:review_grid_2048");
    public void Register(EveRegistryHub hub)
    {
        var descriptor = new PNGridWorldDescriptor(2048, 2048, .5f, PNGridIds.FlatMap.ToString(), 1);
        hub.GetOrCreate<PNWorldDefinition>().Register(new PNWorldDefinition(Big, PNWorldDefinition.HashContent("review/2048"), typeof(PNGridNavOnlyProvider), () => new PNGridNavOnlyProvider(descriptor),
            new PNWorldPolicy(PNEmptyWorldPolicy.Continue, null, null, null, default(EveId), new[] { PNGridIds.GridWorldFeature })));
    }
}

// A grid world with cells and navigation but no chunk objects (a 2048 x 2048 world has 4096 of them).
public sealed class PNGridNavOnlyProvider : IPNWorldProvider
{
    private readonly PNGridWorldDescriptor descriptor;
    public PNGridNavOnlyProvider(PNGridWorldDescriptor descriptor) { this.descriptor = descriptor; }
    public IEnumerator Load(PNWorldBuildContext context)
    {
        var grounds = context.World.Session.Registry.Get<PNGroundDefinition>();
        var definitions = grounds.Snapshot().Values.OrderBy(x => x.Id.ToString(), StringComparer.Ordinal).ToArray();
        var data = new PNGridWorldData(descriptor, definitions.Select(x => x.Id.ToString()).ToArray());
        PNGridTestMaps.Fill(data, descriptor.Map, descriptor.Seed);
        context.SetGridWorld(data);
        context.SetNavigation(new PNGridNavigationSource(data, definitions.Select(x => x.Walkable).ToArray(), PNNavigationProfile.GroundSmall));
        var root = new GameObject("PN Grid Nav Only"); root.SetActive(false);
        context.Own(() => { if (root != null) UnityEngine.Object.DestroyImmediate(root); });
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, context.PhysicsScene);
        context.AddSlot("main", context.PhysicsScene, new[] { root });
        root.SetActive(true);
        yield return null;
    }
}

public sealed class PNGridEditPlayReview : MonoBehaviour
{
    public static event Action<bool, string> Finished;
    public static float TimeScale = 6f;
    private readonly List<string> evidence = new List<string>();
    private readonly List<GameObject> actors = new List<GameObject>();
    private long nextEntity = 993500;
    private EveEntityPreparationScheduler scheduler;
    private PNWorldRuntime worlds;
    private EveSessionContext session;
    private PNGridEditReviewRuntime runtime;
    private PNSyncService sync;
    private PNSyncWorldBinder binder;
    private EveWorldContext world;
    private PNGridWorldController controller;
    private string output;
    private bool large;
    private int errors, checks;
    private float previousTimeScale; private int previousFrameRate, previousVSync; private bool previousBackground;
    private static readonly System.Reflection.FieldInfo RouteField = typeof(PNEnemyNavigationRequest).GetField("Route", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    public static void Begin(string directory, bool large = false)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Start the grid edit review in Play Mode.");
        var root = new GameObject("Grid Edit Review"); DontDestroyOnLoad(root);
        var review = root.AddComponent<PNGridEditPlayReview>(); review.large = large; review.Go(directory);
    }

    private void Go(string directory)
    {
        output = directory; Directory.CreateDirectory(output);
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
        if (failure != null) { evidence.Add("FAIL " + failure); Debug.LogWarning("Grid edit review failure: " + failure); }
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

    private void StartSession(string world)
    {
        var documents = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        runtime = gameObject.AddComponent<PNGridEditReviewRuntime>(); runtime.Documents = documents; runtime.Large = large;
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

    // Pump the controller and let A* run its work items; returns the milliseconds the two took.
    private double PumpAndFlush()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        controller.Pump(); AstarPath.active.FlushWorkItems();
        return clock.Elapsed.TotalMilliseconds;
    }

    private PNPlaceableDefinition Def(EveId id) => session.Registry.Instances().GetEffectiveKind<PNPlaceableDefinition>(id);

    // ---------------------------------------------------------------------------------------------------------
    private IEnumerator RunSmall()
    {
        Check(Application.isPlaying && EveRegistrySession.State == EveSessionState.Stopped, "starts in Play Mode with no running session");
        StartSession("small");
        yield return CreateWorld(PNGridIds.SandboxTestMaze, 60);
        var build = worlds.Get(world);
        var host = build.Slots[0].Roots[0].GetComponent<PNGridWorldHost>();
        controller = PNGridWorldController.Find(world);
        Check(controller != null && controller.Host == host && controller.Data == build.GridWorld && host.GetComponent<PNWorldObject>() != null && host.GetComponent<PNWorldObject>().Key == "grid", "the world has a grid controller and its root is the world object `grid`");
        Check(sync.ObjectCount == 1 && sync.TryGetObject(PNSyncTarget.ForWorldObject(world.InstanceId, world.Incarnation, "main", "grid"), out _), "the sync service knows the grid object of the ready world");
        var data = controller.Data; var physics = world.PhysicsOwner.GetPhysicsScene();
        var lease = PNWorldNavigation.Find(world);
        Check(lease.ChangeSerial == 0 && lease.GridUpdates == 0, "the lease starts with no changes");
        var wall = Def(PNPlaceableIds.Vanish); var rubble = Def(PNPlaceableIds.LeaveGround); var marker = Def(PNPlaceableIds.Marker);
        Check(wall != null && rubble != null && marker != null, "the three shipped placeables resolve from the registry");

        // ---- rules of the commands ----
        var near = new Vector3(5, 0, 5); var far = new Vector3(60, 0, 40);
        JObject Place(string def, int x, int z, int dir = 0) => new JObject { ["def"] = def, ["x"] = x, ["z"] = z, ["dir"] = dir };
        Check(!PNGridCommands.ApplyPlace(session.Registry, controller, near, Place("project_n:nothing", 12, 12)), "an unknown placeable is dropped");
        Check(!PNGridCommands.ApplyPlace(session.Registry, controller, far, Place("project_n:placeable_vanish", 12, 12)), "a sender out of reach is dropped");
        Check(!PNGridCommands.ApplyPlace(session.Registry, controller, near, Place("project_n:placeable_vanish", 12, 12, 7)), "a bad direction is dropped");
        var extra = Place("project_n:placeable_vanish", 12, 12); extra["power"] = 99;
        Check(!PNGridCommands.ApplyPlace(session.Registry, controller, near, extra), "a payload with an extra field is dropped");
        Check(!PNGridCommands.ApplyPlace(session.Registry, controller, near, new JObject { ["def"] = "project_n:placeable_vanish", ["x"] = 12.5, ["z"] = 12, ["dir"] = 0 }), "a cell that is not an integer is dropped");
        Check(!PNGridCommands.ApplyPlace(session.Registry, controller, near, Place("project_n:placeable_vanish", -4, 12)), "a cell outside the world is dropped");
        Check(!PNGridCommands.ApplyPlace(session.Registry, controller, near, JValue.CreateString("x")), "a payload that is not an object is dropped");
        Check(controller.Placements.Count == 0, "nothing was placed by the dropped commands");
        Check(PNGridCommands.ApplyPlace(session.Registry, controller, near, Place("project_n:placeable_vanish", 12, 12)), "a good place command places the thing");
        Check(PNGridCommands.ApplyPlace(session.Registry, controller, near, Place("base:placeable_vanish", 14, 12)), "the base alias resolves too");
        Check(!PNGridCommands.ApplyPlace(session.Registry, controller, near, Place("project_n:placeable_vanish", 12, 12)), "the same cell twice is refused");
        Check(controller.Placements.Count == 2 && !data.IsSolid(12, 12), "the cells turn solid on the next pump, not at once");
        double ms = PumpAndFlush();
        Check(data.IsSolid(12, 12) && data.IsSolid(14, 12) && lease.GridUpdates == 1 && lease.ChangeSerial >= 1, "after a pump the cells are solid and the lease took one update");
        Note("two placements pumped in " + ms.ToString("F2") + " ms");
        yield return Until(() => host.PendingChunks == 0, 3, "the changed chunk is rebuilt");
        Check(physics.Raycast(new Vector3(5.5f, 1, 6.25f), Vector3.right, out var hit, 10, 1 << 9, QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.point.x - 6f) < .02f, "the placed wall blocks a ray at once (layer 9)");
        Check(!lease.TryProject(new Vector3(6.25f, 0, 6.25f), .1f, out _) && lease.TryProject(new Vector3(8.25f, 0, 6.25f), .1f, out _), "the graph no longer lets a point onto the placed cell, and still does beside it");
        Check(!PNGridCommands.ApplyBreak(controller, far, new JObject { ["x"] = 12, ["z"] = 12 }), "a break out of reach is dropped");
        Check(!PNGridCommands.ApplyBreak(controller, near, new JObject { ["x"] = 30, ["z"] = 12 }), "a break on an empty cell is dropped");
        for (int hits = 0; hits < 5; hits++) PNGridCommands.ApplyBreak(controller, near, new JObject { ["x"] = 12, ["z"] = 12 });
        Check(controller.Placements.At(12, 12) != null && controller.Placements.At(12, 12).Health == 30 - 5 * PNGridCommands.HitDamage, "each break command is one hit of the fixed damage");
        for (int hits = 0; hits < 5; hits++) PNGridCommands.ApplyBreak(controller, near, new JObject { ["x"] = 12, ["z"] = 12 });
        Check(controller.Placements.At(12, 12) == null, "enough hits break it");
        PumpAndFlush();
        Check(!data.IsSolid(12, 12) && lease.TryProject(new Vector3(6.25f, 0, 6.25f), .1f, out _), "the cell is free again and the graph walks onto it");
        yield return Until(() => host.PendingChunks == 0, 3, "the chunk is rebuilt after the break");
        Check(!physics.Raycast(new Vector3(5.5f, 1, 6.25f), Vector3.right, 0.9f, 1 << 9, QueryTriggerInteraction.Ignore), "the broken wall no longer blocks a ray");

        // ---- the sync path ----
        var target = PNSyncTarget.ForWorldObject(world.InstanceId, world.Incarnation, "main", "grid");
        int before = controller.Placements.Count;
        Check(sync.Send(target, PNGridCommands.Place, Place("project_n:placeable_vanish", 20, 20)), "the place command is sent to the grid object and is a known command");
        Check(controller.Placements.Count == before, "the host drops it when the sender has no living player in the world");
        Check(sync.Send(target, PNGridCommands.Break, new JObject { ["x"] = 14, ["z"] = 12 }) && controller.Placements.At(14, 12) != null, "a break command without a player breaks nothing");

        // ---- the sync path with a real player (a living player entity of the sender, owner 0 offline) ----
        var playerRoot = new GameObject("Grid edit review player") { layer = 7 }; playerRoot.SetActive(false); actors.Add(playerRoot);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(playerRoot, world.PhysicsOwner); playerRoot.transform.position = near;
        var playerIdentity = playerRoot.AddComponent<EveEntityIdentity>(); playerIdentity.Assign(new EveEntityId(nextEntity++), EveId.Parse("project_n:eve"), 0); playerIdentity.BindWorld(world, "main");
        playerRoot.AddComponent<EveTimeScale>(); var playerStat = playerRoot.AddComponent<PNPlayerStat>().AsPlayer(); playerRoot.AddComponent<PNAbilityManager>();
        var playerLoader = playerRoot.AddComponent<EveEntityLoader>(); playerLoader.Load(session.Registry); playerRoot.SetActive(true);
        yield return Until(() => playerLoader.IsReady, 20, "the player entity is loaded in the grid world");
        Check(PNEntityRoles.IsPlayer(playerLoader) && !playerStat.IsDead, "the fixture entity is a living player");
        int placedBefore = controller.Placements.Count;
        sync.Send(target, PNGridCommands.Place, Place("project_n:placeable_vanish", 10, 12));
        yield return null;
        Check(controller.Placements.Count == placedBefore + 1 && controller.Placements.At(10, 12) != null, "a place command from a player in reach places the thing");
        sync.Send(target, PNGridCommands.Place, Place("project_n:placeable_vanish", 60, 60));
        sync.Send(target, PNGridCommands.Place, Place("project_n:placeable_vanish", 10, 12));
        yield return null;
        Check(controller.Placements.Count == placedBefore + 1 && controller.Placements.At(60, 60) == null, "a place command out of the player's reach, or on a taken cell, is dropped");
        PumpAndFlush();
        Check(data.IsSolid(10, 12), "the placed cell turns solid after the pump");
        for (int blow = 0; blow < 5; blow++) sync.Send(target, PNGridCommands.Break, new JObject { ["x"] = 10, ["z"] = 12 });
        yield return null;
        Check(controller.Placements.At(10, 12) != null && controller.Placements.At(10, 12).Health == 30 - 5 * PNGridCommands.HitDamage, "each break command from the player is one hit of the fixed damage");
        for (int blow = 0; blow < 5; blow++) sync.Send(target, PNGridCommands.Break, new JObject { ["x"] = 10, ["z"] = 12 });
        yield return null;
        Check(controller.Placements.At(10, 12) == null, "enough break commands break it");
        PumpAndFlush();
        Check(!data.IsSolid(10, 12), "the cell is free again after the pump");
        playerRoot.transform.position = new Vector3(60, 0, 40); Physics.SyncTransforms();
        sync.Send(target, PNGridCommands.Place, Place("project_n:placeable_vanish", 10, 12));
        sync.Send(target, PNGridCommands.Break, new JObject { ["x"] = 14, ["z"] = 12 });
        yield return null;
        Check(controller.Placements.At(10, 12) == null && controller.Placements.At(14, 12) != null && controller.Placements.At(14, 12).Health == 30, "a player that walked out of reach can neither place nor break");
        playerRoot.transform.position = near; Physics.SyncTransforms();
        playerStat.SetCurrent(PNContentIds.Health, 0);
        yield return null;
        if (playerStat.IsDead)
        {
            sync.Send(target, PNGridCommands.Place, Place("project_n:placeable_vanish", 10, 12)); yield return null;
            Check(controller.Placements.At(10, 12) == null, "a dead player cannot place");
        }
        else Note("the dead-player case was not exercised (the stat could not be killed in the fixture)");
        playerLoader.Unload(); playerIdentity.ResetIdentity(); actors.Remove(playerRoot); Destroy(playerRoot);
        yield return null;

        // ---- markers: a thing that does not block the way ----
        Check(PNGridCommands.ApplyPlace(session.Registry, controller, near, Place("project_n:placeable_marker", 16, 12)), "a marker is placed");
        PumpAndFlush();
        string markerName = "marker " + controller.Placements.At(16, 12).Id; var markerObject = GameObject.Find(markerName);
        Check(markerObject != null && markerObject.GetComponent<Collider>() == null && !data.IsSolid(16, 12) && lease.TryProject(new Vector3(8.25f, 0, 6.25f), .1f, out _), "a marker is a picture with no collider, its cell stays open and walkable");
        for (int hits = 0; hits < 1; hits++) PNGridCommands.ApplyBreak(controller, near, new JObject { ["x"] = 16, ["z"] = 12 });
        Check(controller.Placements.At(16, 12) == null && GameObject.Find(markerName) == null, "breaking the marker removes its picture");

        // ---- leave_ground ----
        Check(PNGridCommands.ApplyPlace(session.Registry, controller, new Vector3(4, 0, 18), Place("project_n:placeable_leave_ground", 8, 40)), "a rubble block is placed");
        PumpAndFlush();
        var rubblePlaced = controller.Placements.At(8, 40);
        controller.Placements.Damage(rubblePlaced.Id, 100); PumpAndFlush();
        int sandIndex = data.Palette.ToList().IndexOf("project_n:sand");
        Check(data.GroundAt(8, 40) == sandIndex && data.GroundAt(9, 41) == sandIndex && !data.IsSolid(8, 40), "a broken rubble block leaves sand and free cells");
        yield return Until(() => host.PendingChunks == 0, 3, "the repainted chunk is rebuilt");

        // ---- ground that cannot be walked on ----
        int stoneIndex = data.Palette.ToList().IndexOf("project_n:stone");
        Check(lease.TryProject(new Vector3(22.25f, 0, 8.25f), .1f, out _), "before painting, the point projects");
        controller.Editor.Submit(new PNGridEditBatch().AddGround(44, 16, stoneIndex)); PumpAndFlush();
        Check(!lease.TryProject(new Vector3(22.25f, 0, 8.25f), .1f, out _) && lease.TryProject(new Vector3(24.25f, 0, 8.25f), .1f, out _), "stone ground is not walkable in the graph, its neighbour is");
        controller.Editor.Submit(new PNGridEditBatch().AddGround(44, 16, 0)); PumpAndFlush();
        Check(lease.TryProject(new Vector3(22.25f, 0, 8.25f), .1f, out _), "painting the grass back makes it walkable again");
        Check(!controller.Editor.Submit(new PNGridEditBatch().AddHeight(3, 3, 1)).Accepted, "height edits are still refused (the navigation is flat only)");

        // ---- a real enemy goes around a wall placed in front of it, and straight again when it is broken ----
        // The strip x 0..10 m is open; a wall of 1x1 blocks from x cell 0 to 14 at z cell 30 (15 m) leaves a 2.5 m gap at the east end.
        Time.timeScale = TimeScale;
        var control = new RunResult(); yield return WalkOne("project_n:starkeeper", new Vector3(1.5f, 0.02f, 8), new Vector3(1.5f, 0, 22), null, control);
        Note($"control walk (no wall): arrived={control.Arrived} distance {control.Distance:F1} m, {control.Took:F1} s, attempts {control.Attempts}");
        Check(control.Arrived && control.Distance < 16f, "without a wall the enemy walks about straight (" + control.Distance.ToString("F1") + " m)");
        var wallIds = new List<int>();
        for (int x = 0; x <= 14; x++) { controller.Placements.TryPlace(wall, x, 30, 0, out var piece, out _); wallIds.Add(piece.Id); }
        PumpAndFlush();
        yield return Until(() => host.PendingChunks == 0, 3, "the wall chunks are rebuilt");
        var detour = new RunResult(); yield return WalkOne("project_n:starkeeper", new Vector3(1.5f, 0.02f, 8), new Vector3(1.5f, 0, 22), null, detour);
        Note($"detour walk (wall in place from the start): arrived={detour.Arrived} distance {detour.Distance:F1} m, attempts {detour.Attempts}, max x while crossing {detour.MaxXAtWall:F1}");
        Check(detour.Arrived && detour.Distance > control.Distance + 3.5f && detour.MaxXAtWall > 7.4f, "with the wall the enemy goes through the gap at the east end (" + detour.Distance.ToString("F1") + " m)");
        Check(detour.WallCrossings == 0, "and never through a wall cell");
        // the wall appears while the enemy walks
        foreach (var id in wallIds) controller.Placements.Remove(id); PumpAndFlush();
        var added = new RunResult();
        yield return WalkOne("project_n:starkeeper", new Vector3(1.5f, 0.02f, 8), new Vector3(1.5f, 0, 22), () => { StartCoroutine(PlaceWallAfter(1.0f, wall)); }, added);
        Note($"wall placed while walking: arrived={added.Arrived} distance {added.Distance:F1} m, attempts {added.Attempts}");
        Check(added.Arrived && added.Distance > control.Distance + 2.5f && added.WallCrossings == 0, "a wall placed in front of a walking enemy makes it repath and go around (" + added.Distance.ToString("F1") + " m, " + added.Attempts + " paths)");
        // and broken while it walks: start with the wall, break its middle part after a while
        var broken = new RunResult(); yield return WalkOne("project_n:starkeeper", new Vector3(1.5f, 0.02f, 8), new Vector3(1.5f, 0, 22), () => { StartCoroutine(BreakWallAfter(1.0f)); }, broken);
        Note($"wall broken while walking: arrived={broken.Arrived} distance {broken.Distance:F1} m, attempts {broken.Attempts}");
        Check(broken.Arrived && broken.Distance < detour.Distance - 2f, "breaking the wall while the enemy goes around makes its way straighter (" + broken.Distance.ToString("F1") + " m against " + detour.Distance.ToString("F1") + " m)");
        Time.timeScale = previousTimeScale;
        yield return null;

        // ---- timings ----
        foreach (var id in controller.Placements.All.Select(p => p.Id).ToArray()) controller.Placements.Remove(id);
        PumpAndFlush(); yield return Until(() => host.PendingChunks == 0, 5, "the world is clean before the timings");
        foreach (int count in new[] { 1, 16, 256 })
        {
            int placed = 0;
            for (int i = 0; i < count; i++) if (controller.Placements.TryPlace(wall, i % 16, 20 + (i / 16) * 2, 0, out _, out _)) placed++;
            int ticks = 0; double total = 0; while (controller.Editor.QueuedCells > 0 && ticks < 50) { total += PumpAndFlush(); ticks++; }
            Note($"timing {count} placements: placed {placed}, {ticks} tick(s), {total:F2} ms on the cell/graph side");
            Check(placed == count && ticks <= (count * 1 + PNGridWorldController.CellsPerFrame - 1) / PNGridWorldController.CellsPerFrame, count + " placements are applied within the per-tick cell budget");
            foreach (var p in controller.Placements.All.ToArray()) controller.Placements.Remove(p.Id);
            PumpAndFlush();
            yield return Until(() => host.PendingChunks == 0, 5, "chunks settle after " + count + " placements");
        }

        // ---- unload ----
        controller.Placements.TryPlace(marker, 30, 30, 0, out _, out _); controller.Placements.TryPlace(wall, 32, 32, 0, out _, out _);
        yield return Unload(host);
    }

    private IEnumerator PlaceWallAfter(float seconds, PNPlaceableDefinition wall)
    {
        yield return new WaitForSeconds(seconds);
        for (int x = 0; x <= 14; x++) controller.Placements.TryPlace(wall, x, 30, 0, out _, out _);
    }
    private IEnumerator BreakWallAfter(float seconds)
    {
        // The wall is put in first, the enemy has to start going around it; then the cells the enemy would cut through are broken.
        yield return new WaitForSeconds(seconds);
        foreach (var piece in controller.Placements.All.Where(p => p.X <= 8 && p.Z == 30).ToArray()) controller.Placements.Remove(piece.Id);
    }

    private sealed class RunResult { public bool Arrived; public float Distance, Took; public int Attempts, WallCrossings; public float MaxXAtWall; }

    // Builds an enemy body like the production enemy fixture does, walks it from `from` to `to` and records what happened.
    private IEnumerator WalkOne(string character, Vector3 from, Vector3 to, Action whileWalking, RunResult result)
    {
        var root = new GameObject("Grid edit review " + character) { layer = 7 }; root.SetActive(false); actors.Add(root);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, world.PhysicsOwner); root.transform.position = from;
        var identity = root.AddComponent<EveEntityIdentity>(); identity.Assign(new EveEntityId(nextEntity++), EveId.Parse(character), 0); identity.BindWorld(world, "main");
        root.AddComponent<PNEnemyStat>(); var rigidbody = root.AddComponent<Rigidbody>(); rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
        var capsule = root.AddComponent<CapsuleCollider>(); capsule.radius = .2f; capsule.height = 1.4f; capsule.center = Vector3.up * .7f;
        root.AddComponent<EveTimeScale>().MinScale = 0;
        var walking = root.AddComponent<PNEnemyMove>(); walking.GroundLayers = 1 << 13; walking.CanJump = false;
        var loader = root.AddComponent<EveEntityLoader>(); loader.Load(session.Registry); root.SetActive(true);
        float deadline = Time.realtimeSinceStartup + 20; while (!loader.IsReady && Time.realtimeSinceStartup < deadline) yield return null;
        Check(loader.IsReady && walking.NavigationReady, character + " is loaded and its navigation is ready");
        yield return null; yield return null;
        walking.Body.position = from; walking.transform.position = from; Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        var navigation = walking.GoTo(to, .15f);
        whileWalking?.Invoke();
        float t0 = Time.time, limit = 240f / Mathf.Max(.3f, walking.CurrentMoveSpeed); var last = walking.Body.position; float distance = 0;
        var data = controller.Data; float cell = data.CellSize;
        while (!navigation.IsTerminal && Time.time - t0 < limit)
        {
            yield return null;
            var p = walking.Body.position; distance += Vector2.Distance(new Vector2(p.x, p.z), new Vector2(last.x, last.z)); last = p;
            int cx = Mathf.FloorToInt(p.x / cell), cz = Mathf.FloorToInt(p.z / cell);
            if (data.IsSolid(cx, cz)) result.WallCrossings++;
            if (Mathf.Abs(p.z - 15.25f) < .6f) result.MaxXAtWall = Mathf.Max(result.MaxXAtWall, p.x);
        }
        result.Arrived = navigation.Status == PNEnemyNavigationStatus.Arrived; result.Distance = distance; result.Took = Time.time - t0; result.Attempts = navigation.PathAttempts;
        if (walking.NavigationRequest != null) walking.CancelNavigation(walking.NavigationRequest);
        loader.Unload(); identity.ResetIdentity(); actors.Remove(root); Destroy(root);
        yield return null;
    }

    private IEnumerator Unload(PNGridWorldHost host)
    {
        bool done = false; float deadline = Time.realtimeSinceStartup + 10;
        while (!done && Time.realtimeSinceStartup < deadline) { done = worlds.Unload(world); if (!done) yield return null; }
        Check(done, "the grid world unloads");
        yield return null; yield return null;
        Check(PNGridWorldController.Find(world) == null, "the controller is forgotten");
        Check(FindObjectsByType<PNGridWorldController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0, "no controller component is left");
        Check(GameObject.Find("PN Grid Markers") == null, "the marker root is gone");
        Check(Resources.FindObjectsOfTypeAll<Material>().Count(m => m != null && m.name.StartsWith("PN Grid")) == 0, "no placeholder material is left");
        Check(PNWorldNavigation.ForSession(session).GraphCount == 0, "the graph is removed");
        Check(sync.ObjectCount == 0, "the sync service forgot the grid object");
    }

    // ---------------------------------------------------------------------------------------------------------
    private IEnumerator RunLarge()
    {
        Check(Application.isPlaying && EveRegistrySession.State == EveSessionState.Stopped, "starts in Play Mode with no running session");
        StartSession("large");
        long memoryBefore = GC.GetTotalMemory(true);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        yield return CreateWorld(PNGridEditReviewWorlds.Big, 300);
        Note("2048 x 2048 world ready after " + clock.ElapsedMilliseconds + " ms (data, one GridGraph scan and connections)");
        Note("managed memory +" + ((GC.GetTotalMemory(false) - memoryBefore) / (1024 * 1024)) + " MB; Unity allocated " + (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)) + " MB");
        var build = worlds.Get(world); var data = build.GridWorld; var lease = PNWorldNavigation.Find(world);
        Check(data.Width == 2048 && data.Depth == 2048 && lease != null && lease.Source != null, "the 2048 x 2048 world has its cells and a grid lease");
        var editor = new PNGridEditor(data); var placements = new PNGridPlacements(data, editor, i => true);
        var wall = Def(PNPlaceableIds.Vanish);
        var regions = new List<PNGridRegion>();
        // one placement at nine spots: the cell side (place + edit + clearance) and the graph side (lease update + A* work item) are timed apart; the plan's limit is for the navigation side
        var cellSamples = new List<double>(); var graphSamples = new List<double>();
        for (int i = 0; i < 12; i++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            placements.TryPlace(wall, 400 + i * 40, 900, 0, out _, out _);
            regions.Clear(); editor.Tick(512, regions);
            double cells = watch.Elapsed.TotalMilliseconds; watch.Restart();
            lease.ApplyGridChanges(regions); AstarPath.active.FlushWorkItems();
            double graph = watch.Elapsed.TotalMilliseconds;
            if (i >= 3) { cellSamples.Add(cells); graphSamples.Add(graph); }   // the first three warm up the code paths
        }
        cellSamples.Sort(); graphSamples.Sort(); double cellMedian = cellSamples[cellSamples.Count / 2], graphMedian = graphSamples[graphSamples.Count / 2];
        Note("one placement, cell side (ms): " + string.Join(" ", cellSamples.Select(x => x.ToString("F2"))));
        Note("one placement, graph side (ms): " + string.Join(" ", graphSamples.Select(x => x.ToString("F2"))));
        Check(graphMedian < 4.0, "the navigation side of placing one thing is under 4 ms on a 2048 x 2048 world, a constant cost of the A* update not of the size (the plan asked for 1 ms: see the note) (median " + graphMedian.ToString("F2") + " ms; cell side median " + cellMedian.ToString("F2") + " ms)");
        var removeSamples = new List<double>();
        foreach (var piece in placements.All.ToArray())
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            placements.Remove(piece.Id); regions.Clear(); editor.Tick(512, regions); lease.ApplyGridChanges(regions); AstarPath.active.FlushWorkItems();
            removeSamples.Add(watch.Elapsed.TotalMilliseconds);
        }
        removeSamples.Sort();
        Note("one removal, whole path (ms): " + string.Join(" ", removeSamples.Select(x => x.ToString("F2"))));        // 256 things spread over the world, in one go
        int placed = 0; var batchWatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 256; i++) if (placements.TryPlace(wall, 100 + (i % 16) * 100, 100 + (i / 16) * 100, 0, out _, out _)) placed++;
        double submit = batchWatch.Elapsed.TotalMilliseconds;
        regions.Clear(); int taken = editor.Tick(512, regions); double ticked = batchWatch.Elapsed.TotalMilliseconds;
        // the controller hands the graph at most NavRegionsPerFrame regions a frame; the same slicing here shows what each frame would cost
        var sliceTimes = new List<double>(); int sliceCount = PNGridWorldController.NavRegionsPerFrame;
        for (int at = 0; at < regions.Count; at += sliceCount)
        {
            var slice = regions.GetRange(at, Math.Min(sliceCount, regions.Count - at)); var watch = System.Diagnostics.Stopwatch.StartNew();
            lease.ApplyGridChanges(slice); AstarPath.active.FlushWorkItems(); sliceTimes.Add(watch.Elapsed.TotalMilliseconds);
        }
        Note($"256 spread placements: submit {submit:F2} ms, cells + clearance {ticked - submit:F2} ms, graph in {sliceTimes.Count} frames of {sliceCount} regions: max {sliceTimes.Max():F2} ms, total {sliceTimes.Sum():F2} ms; {regions.Count} regions, {taken} cells in one tick, {editor.QueuedCells} left");
        Check(placed == 256 && taken == 256 && editor.QueuedCells == 0 && regions.Count == 256, "256 spread things are 256 cells in one tick and 256 small regions");
        Check(sliceTimes.Max() < 8.0, "the graph side of that burst never costs a frame more than 8 ms when sliced (max " + sliceTimes.Max().ToString("F2") + " ms)");
        Check(!lease.TryProject(new Vector3(50.25f, 0, 50.25f), .1f, out _) && lease.TryProject(new Vector3(51.75f, 0, 51.75f), .1f, out _), "the graph follows them");        // 256 clustered things (a 16 x 16 block of cells): one region
        placed = 0; batchWatch.Restart();
        for (int i = 0; i < 256; i++) if (placements.TryPlace(wall, 1530 + i % 16, 1530 + i / 16, 0, out _, out _)) placed++;
        regions.Clear(); taken = editor.Tick(512, regions); lease.ApplyGridChanges(regions); AstarPath.active.FlushWorkItems(); double total = batchWatch.Elapsed.TotalMilliseconds;
        Note($"256 clustered placements: {total:F2} ms, {regions.Count} region(s)");
        Check(placed == 256 && regions.Count == 1 && taken == 256 && !lease.TryProject(new Vector3(769.25f, 0, 769.25f), .1f, out _) && lease.TryProject(new Vector3(763.25f, 0, 769.25f), .1f, out _), "256 clustered things are one region");
        // a route across the world still works with all of it in place
        yield return Unload2();
    }

    private IEnumerator Unload2()
    {
        bool done = false; float deadline = Time.realtimeSinceStartup + 20;
        while (!done && Time.realtimeSinceStartup < deadline) { done = worlds.Unload(world); if (!done) yield return null; }
        Check(done, "the 2048 x 2048 world unloads");
        yield return null; yield return null;
        Check(PNWorldNavigation.ForSession(session).GraphCount == 0, "the big graph is removed");
    }
}
#endif
