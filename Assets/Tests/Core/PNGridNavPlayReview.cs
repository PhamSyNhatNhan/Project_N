#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;

// Fixture for Docs/SandboxPlan.md steps G1 and G2 (Play Mode in the main Editor, no scene file touched, nothing saved): the production content with the grid test world `project_n:sandbox_test_maze`, real enemies
// (Sentinel, Starkeeper, Starweaver through the production role, factory, scheduler, Move and Seeker) walking twelve routes on the grid navigation, plus the chunk objects and their cleanup.
// Start it with `PNGridNavPlayReview.Begin(outputDirectory)` while the Editor is in Play Mode; read `results.txt`. The routes are the twelve of the navigation experiments (Docs/Sandbox.md 5b point 10).
public sealed class PNGridReviewRuntime : EveRuntime
{
    [NonSerialized] public EveContentDocument[] Documents;
    public override string RulesVersion => EveGameRuntime.CurrentRulesVersion;
    protected override void Awake() { }
    protected override IEnumerable<EveIContentModule> CreateContentModules() => new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() };
    protected override IEnumerable<EveContentDocument> ReadContentDocuments() => Documents;
}

public sealed class PNGridNavPlayReview : MonoBehaviour
{
    public static event Action<bool, string> Finished;
    // The Sentinel and the Dummy are authored in a scene and have no catalog prefab, so they cannot be spawned at run time; the five Star enemies can.
    public static string[] Characters = { "project_n:starkeeper", "project_n:starweaver", "project_n:starreaper", "project_n:starforger", "project_n:starchanter" };
    public static float TimeScale = 8f;
    private static readonly (float sx, float sz, float gx, float gz, string name)[] Routes =
    {
        (3, 3, 8, 27, "open field"), (6, 14.5f, 12, 14.5f, "door 1"), (5, 3.5f, 12, 3.5f, "door 2"), (13, 8, 27, 8, "corridor"),
        (27, 19, 39, 27.5f, "pillar field"), (15, 14, 15, 26, "zigzag"), (11, 5, 11, 25, "along the divider"), (30, 5.75f, 34, 5.75f, "0.5 m gap"),
        (1, 1, 39, 29, "far corner"), (20, 8, 20, 11, "corridor to outside"), (9, 14.5f, 11, 14.5f, "short, door"), (15, 14, 37, 27, "zigzag room to pillars")
    };
    private readonly List<string> evidence = new List<string>();
    private readonly List<Material> materials = new List<Material>();
    private readonly List<GameObject> actors = new List<GameObject>();
    private readonly List<string> incomplete = new List<string>();
    private long nextEntity = 992500;
    private static readonly System.Reflection.FieldInfo RouteField = typeof(PNEnemyNavigationRequest).GetField("Route", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    private static readonly System.Reflection.FieldInfo CornerField = typeof(PNEnemyNavigationRequest).GetField("Corner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    private EveEntityPreparationScheduler scheduler;
    private PNWorldRuntime worlds;
    private EveSessionContext session;
    private PNGridReviewRuntime runtime;
    private PNEntityFactory factory;
    private EveWorldContext world;
    private Camera camera;
    private string output;
    private int errors;
    private float previousTimeScale; private int previousFrameRate, previousVSync; private bool previousBackground;
    private int checks;

    public static void Begin(string directory)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Start the grid navigation review in Play Mode.");
        var root = new GameObject("Grid Navigation Review"); DontDestroyOnLoad(root);
        root.AddComponent<PNGridNavPlayReview>().Go(directory);
    }

    private void Go(string directory)
    {
        output = directory; Directory.CreateDirectory(output);
        previousTimeScale = Time.timeScale; previousFrameRate = Application.targetFrameRate; previousVSync = QualitySettings.vSyncCount; previousBackground = Application.runInBackground;
        Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0; Application.runInBackground = true;
        Application.logMessageReceived += Logged;
        var cameraObject = new GameObject("Grid review camera"); cameraObject.transform.SetParent(transform);
        camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 19; camera.transform.position = new Vector3(24, 40, 16); camera.transform.rotation = Quaternion.Euler(90, 0, 0);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .09f, .13f); camera.enabled = false;
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
        if (failure != null) { evidence.Add("FAIL " + failure); Debug.LogWarning("Grid review failure: " + failure); }   // a warning, not an exception: the Editor can be set to pause on errors
        Time.timeScale = previousTimeScale;
        foreach (var actor in actors.Where(x => x != null))
        { try { actor.GetComponent<EveEntityLoader>()?.Unload(); actor.GetComponent<EveEntityIdentity>()?.ResetIdentity(); } catch (Exception e) { failure = failure ?? e; evidence.Add("CLEANUP " + e); } Destroy(actor); }
        actors.Clear();
        try { runtime?.EndSession(); } catch (Exception e) { failure = failure ?? e; evidence.Add("STOP " + e); }
        float deadline = Time.realtimeSinceStartup + 10;
        while (EveRegistrySession.State != EveSessionState.Stopped && Time.realtimeSinceStartup < deadline) { EveRegistrySession.PumpCleanup(); yield return null; }
        try { if (EveRegistrySession.State == EveSessionState.Stopped) runtime?.UnloadContent(); } catch (Exception e) { failure = failure ?? e; evidence.Add("CONTENT CLEANUP " + e); }
        foreach (var material in materials) if (material != null) Destroy(material);
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

    private void Capture(string name)
    {
        var texture = RenderTexture.GetTemporary(1280, 860, 24); var previous = RenderTexture.active; Texture2D image = null;
        try
        {
            camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
            image = new Texture2D(1280, 860, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1280, 860), 0, 0); image.Apply();
            var pixels = image.GetPixels32(); int visible = pixels.Count(x => x.r > 80 || x.g > 80 || x.b > 100);
            Note(name + ": " + visible + " bright pixels");
            File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
        }
        finally { if (image != null) Destroy(image); camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(texture); }
    }

    private PNEntityRequest Request(string character, Vector3 position) => new PNEntityRequest
    {
        Entity = "project_n:enemy", Template = "project_n:enemy", Character = character, World = world, WorldIncarnation = world.Incarnation,
        ContentSlot = "main", SceneKey = PNRoomDirector.EnemySceneKey, EntityKey = "", Position = position
    };

    private IEnumerator Run()
    {
        Check(Application.isPlaying && EveRegistrySession.State == EveSessionState.Stopped, "starts in Play Mode with no running session");
        var documents = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        runtime = gameObject.AddComponent<PNGridReviewRuntime>(); runtime.Documents = documents;
        runtime.BeginSession(EveSessionRole.Offline); session = EveRegistrySession.Context;
        scheduler = new EveEntityPreparationScheduler(session, PNGameBootstrap.Config.Preparation); worlds = new PNWorldRuntime(session, scheduler);
        gameObject.AddComponent<PNWorldPhysicsLoop>().Bind(session);
        factory = new PNEntityFactory(session, worlds) { Scheduler = scheduler };

        // ---- G1: the world is built from cell data ----
        double started = Time.realtimeSinceStartupAsDouble;
        world = worlds.Create(PNGridIds.SandboxTestMaze);
        float readyDeadline = Time.realtimeSinceStartup + 60;
        while (!world.CanExecute && world.State != EveWorldState.Failed && Time.realtimeSinceStartup < readyDeadline) yield return null;
        if (!world.CanExecute) throw new Exception("the grid test world did not become ready: state " + world.State + ", failure " + (world.Failure ?? world.FirstFailure));
        Check(world.CanExecute, "the grid test world prepares and becomes ready");
        Note("world ready in " + ((Time.realtimeSinceStartupAsDouble - started) * 1000).ToString("F0") + " ms");
        var build = worlds.Get(world);
        Check(build.Features.Has(PNGridIds.GridWorldFeature) && build.GridWorld != null && build.Navigation != null && build.Slots.Count == 1 && build.Slots[0].Id == "main", "the world has the grid_world feature, its cells, its navigation source and one slot `main`");
        var data = build.GridWorld;
        Check(data.Width == 96 && data.Depth == 64 && data.ChunkCount == 6 && data.SolidCount > 100 && data.Palette.Count == 3, "the cell data is the 96 x 64 maze with the three registered ground types");
        var host = build.Slots[0].Roots[0].GetComponent<PNGridWorldHost>();
        Check(host != null && host.gameObject.activeInHierarchy && host.ChunkObjectCount == 6 && host.PendingChunks == 0, "the host has one object per chunk and nothing is out of date");
        Check(host.WallObjectCount >= 4 && host.WallObjectCount <= 6, "chunks with solid cells have a wall object (" + host.WallObjectCount + " of 6)");
        var physics = world.PhysicsOwner.GetPhysicsScene();
        bool groundHit = physics.Raycast(new Vector3(5, 5, 5), Vector3.down, out var ground, 10, 1 << 13, QueryTriggerInteraction.Ignore);
        Check(groundHit && Mathf.Abs(ground.point.y) < .06f, "a ray down onto open ground hits the floor collider (layer 13) at height 0 in the world's own physics scene");
        bool wallHit = physics.Raycast(new Vector3(8, 1, 1.5f), Vector3.right, out var wall, 10, 1 << 9, QueryTriggerInteraction.Ignore);
        Check(wallHit && Mathf.Abs(wall.point.x - 10f) < .02f, "a ray along the floor hits the divider wall (layer 9) at its face");
        bool doorOpen = !physics.Raycast(new Vector3(8, 1, 3.5f), Vector3.right, out _, 10, 1 << 9, QueryTriggerInteraction.Ignore);
        Check(doorOpen, "the 1.0 m doorway has no wall collider in it");
        Check(!Physics.Raycast(new Vector3(8, 1, 1.5f), Vector3.right, 10, 1 << 9), "the default physics scene does not see the world's walls");
        yield return null;
        Capture("maze-top");

        // editing a cell rebuilds only what changed
        int rebuildsBefore = host.Rebuilds;
        data.SetSolid(70, 50, true);
        yield return Until(() => host.PendingChunks == 0, 3, "an edited chunk is rebuilt by the host");
        Check(host.Rebuilds > rebuildsBefore && host.Rebuilds - rebuildsBefore <= 2 && host.ChunkObjectCount == 6, "an edit rebuilds the edited chunk (and a neighbour at most), not the world");
        Check(physics.Raycast(new Vector3(33, 1, 25.25f), Vector3.right, out var added, 10, 1 << 9, QueryTriggerInteraction.Ignore) && Mathf.Abs(added.point.x - 35f) < .02f, "the new wall cell blocks a ray at once");
        data.SetSolid(70, 50, false);
        yield return Until(() => host.PendingChunks == 0, 3, "clearing the cell rebuilds again");
        Check(!physics.Raycast(new Vector3(33, 1, 25.25f), Vector3.right, 10, 1 << 9, QueryTriggerInteraction.Ignore), "the removed wall cell no longer blocks");

        // ---- G2: grid navigation ----
        var lease = PNWorldNavigation.Find(world);
        Check(lease != null && lease.ProfileId == "ground-small" && Mathf.Approximately(lease.Radius, .2f) && Mathf.Approximately(lease.Height, 1.4f) && lease.Source != null, "the world has a grid navigation lease with the ground-small profile");
        Check(PNWorldNavigation.ForSession(session).GraphCount == 1, "one navigation owner holds one graph for the world");
        Check(lease.TryProject(new Vector3(5.1f, 0, 5.1f), .3f, out var projected) && Vector3.Distance(projected, new Vector3(5.1f, 0, 5.1f)) < .3f, "a point on open ground projects onto the graph");
        Check(!lease.TryProject(new Vector3(10.25f, 0, 1f), .15f, out _), "a point inside a wall cell does not project");
        Check(lease.TryProject(new Vector3(10.25f, 0, 3.5f), .3f, out _), "a point in the doorway projects");

        Time.timeScale = TimeScale;
        foreach (var character in Characters) yield return WalkRoutes(character);
        Time.timeScale = previousTimeScale;
        Check(incomplete.Count == 0, "every enemy reaches every route on the grid navigation" + (incomplete.Count == 0 ? "" : " (not: " + string.Join(", ", incomplete) + ")"));

        // ---- cleanup ----
        yield return Unload(host);
    }

    private IEnumerator WalkRoutes(string character)
    {
        // The body is built like the existing enemy fixture does it (PNEnemyMoveSkillPlayReview): the production PNEnemyStat and PNEnemyMove on a Rigidbody and a capsule with the values of the Star prefabs (radius 0.2,
        // height 1.4, rotation frozen, layer Enemy), loaded through the entity loader as the character in the base content (so its move speed is the real one). The Star prefabs themselves also need the visual asset
        // catalogue, which is another system and is not set up in this fixture.
        var start = new Vector3(Routes[0].sx, .02f, Routes[0].sz);
        var root = new GameObject("Grid review " + character) { layer = 7 }; root.SetActive(false); actors.Add(root);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, world.PhysicsOwner); root.transform.position = start;
        var identity = root.AddComponent<EveEntityIdentity>(); identity.Assign(new EveEntityId(nextEntity++), EveId.Parse(character), 0); identity.BindWorld(world, "main");
        root.AddComponent<PNEnemyStat>(); var rigidbody = root.AddComponent<Rigidbody>(); rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
        var capsule = root.AddComponent<CapsuleCollider>(); capsule.radius = .2f; capsule.height = 1.4f; capsule.center = Vector3.up * .7f;
        root.AddComponent<EveTimeScale>().MinScale = 0;
        var walking = root.AddComponent<PNEnemyMove>(); walking.GroundLayers = 1 << 13; walking.CanJump = false;
        var loader = root.AddComponent<EveEntityLoader>(); loader.Load(session.Registry); root.SetActive(true);
        yield return Until(() => loader.IsReady, 20, character + " is loaded as its base-content character");
        var move = walking;
        Check(move.NavigationReady, character + " has PNEnemyMove and the world's navigation is ready for it");
        yield return null; yield return null;
        int reached = 0; var lines = new List<string>();
        for (int i = 0; i < Routes.Length; i++)
        {
            var route = Routes[i];
            if (move.NavigationRequest != null) move.CancelNavigation(move.NavigationRequest);
            var from = new Vector3(route.sx, .02f, route.sz); var to = new Vector3(route.gx, 0, route.gz);
            move.Body.position = from; move.transform.position = from; move.Body.linearVelocity = Vector3.zero; Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            var navigation = move.GoTo(to, .15f);
            float straight = Vector3.Distance(from, to), speed = Mathf.Max(.3f, move.CurrentMoveSpeed), limit = (straight * 6f + 30f) / speed;   // a detour route is several times the straight distance (the zigzag is 4x), and the slowest Star walks at 0.6 m/s
            float t0 = Time.time; Vector3[] lastRoute = null; int lastCorner = 0; var trail = new List<Vector3>(); float nextTrail = 0;
            while (!navigation.IsTerminal && Time.time - t0 < limit)
            {
                var current = (Vector3[])RouteField.GetValue(navigation); if (current != null) { lastRoute = (Vector3[])current.Clone(); lastCorner = (int)CornerField.GetValue(navigation); }
                if (Time.time >= nextTrail) { nextTrail = Time.time + 1f; trail.Add(move.Body.position); }
                yield return null;
            }
            float took = Time.time - t0; float distance = Vector2.Distance(new Vector2(move.Body.position.x, move.Body.position.z), new Vector2(to.x, to.z));
            bool ok = navigation.Status == PNEnemyNavigationStatus.Arrived && distance <= .5f;
            if (ok) reached++;
            if (!ok)
            {
                var at = move.Body.position; var nav = worlds.Get(world).Navigation;
                lines.Add($"  STALLED at ({at.x:F2},{at.z:F2}) corner {lastCorner} of route " + (lastRoute == null ? "none" : string.Join(" ", lastRoute.Select(v => "(" + v.x.ToString("F2") + "," + v.z.ToString("F2") + ")"))));
                lines.Add("  trail every second: " + string.Join(" ", trail.Select(v => "(" + v.x.ToString("F1") + "," + v.z.ToString("F1") + ")")));
                if (lastRoute != null && lastCorner < lastRoute.Length)
                    lines.Add($"  clearance of the body to a blocked cell {PNGridPathRefiner.Clearance(new PNGridPoint(at.x, at.z), nav, 1f):F3} m; the next corner ({lastRoute[lastCorner].x:F2},{lastRoute[lastCorner].z:F2}) has {PNGridPathRefiner.Clearance(new PNGridPoint(lastRoute[lastCorner].x, lastRoute[lastCorner].z), nav, 1f):F3} m");
            }
            lines.Add($"ROUTE {character} q{i + 1} {route.name,-32} {(ok ? "ARRIVED" : navigation.Status.ToString().ToUpperInvariant() + " " + navigation.EndReason),-22} took {took,6:F1} s (limit {limit:F0}), attempts {navigation.PathAttempts}, {distance:F2} m from the goal {navigation.Diagnostic}");
            if (move.NavigationRequest != null) move.CancelNavigation(move.NavigationRequest);
        }
        foreach (var line in lines) evidence.Add(line);
        evidence.Add($"SUMMARY {character} reached {reached}/{Routes.Length} (speed {move.CurrentMoveSpeed:F1} m/s)");
        loader.Unload(); identity.ResetIdentity(); actors.Remove(root); Destroy(root);
        yield return null;
        if (reached == Routes.Length) { checks++; evidence.Add("PASS " + character + " reaches all " + Routes.Length + " routes on the grid navigation"); }
        else incomplete.Add(character + " " + reached + "/" + Routes.Length);
    }

    private IEnumerator Unload(PNGridWorldHost host)
    {
        var scene = world.PhysicsOwner;
        bool done = false; float deadline = Time.realtimeSinceStartup + 10;
        while (!done && Time.realtimeSinceStartup < deadline) { done = worlds.Unload(world); if (!done) yield return null; }
        Check(done, "the grid world unloads");
        yield return null; yield return null;
        Check(PNWorldNavigation.ForSession(session).GraphCount == 0, "unloading removes the world's graph from the navigation owner");
        Check(FindObjectsByType<PNGridWorldHost>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0, "no grid host object is left");
        int meshes = Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m != null && (m.name.StartsWith("ground ") || m.name.StartsWith("walls ")));
        Check(meshes == 0, "no chunk mesh is left (" + meshes + ")");
        int materialsLeft = Resources.FindObjectsOfTypeAll<Material>().Count(m => m != null && m.name == "PN Grid World (placeholder)");
        Check(materialsLeft == 0, "the placeholder material is destroyed (" + materialsLeft + ")");
    }
}
#endif
