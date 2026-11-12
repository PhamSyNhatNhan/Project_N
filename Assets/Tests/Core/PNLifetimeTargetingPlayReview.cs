#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// A behavior that only holds the shared targeting of the enemy it sits on, so a fixture can ask it (Docs/SandboxPlan.md step G5).
public sealed class PNTargetingProbeBehavior : PNEnemyBehavior
{
    public float Fallback = 8f;
    public PNEnemyTargeting Targeting { get; private set; }
    public override void Enter() { Targeting = new PNEnemyTargeting(Context, Fallback); }
    public override void Tick(float delta) { }
}

// Fixture for Docs/SandboxPlan.md steps G4 and G5 (Play Mode in the main Editor from an EMPTY scene, nothing saved): in the maze test world (it has the `project_n:despawn` feature)
//   G4: the world index, the despawn service (distance, timer, reset, hold, exempt role, room-clear marker, role override, no living player, the leash home, the service running by itself) and cleanup;
//   G5: the shared enemy targeting (range from role data or the default, aggro choice, sight by cells against the physics `Wall` ray, a wall placed and removed) and the agreement of the two sight tests.
// Read `results.txt`. The real Star behaviors are exercised by the existing checks and by PNRunFlowPlayReview, not here.
public sealed class PNLifetimeTargetingPlayReview : MonoBehaviour
{
    public static event Action<bool, string> Finished;
    public static float TimeScale = 6f;
    private readonly List<string> evidence = new List<string>();
    private readonly List<GameObject> actors = new List<GameObject>();
    private long nextEntity = 994500;
    private EveEntityPreparationScheduler scheduler;
    private PNWorldRuntime worlds;
    private EveSessionContext session;
    private PNGridEditReviewRuntime runtime;
    private PNSyncService sync;
    private PNSyncWorldBinder binder;
    private EveWorldContext world;
    private PNGridWorldController controller;
    private PNDespawnService service;
    private string output;
    private int errors, checks;
    private float previousTimeScale; private int previousFrameRate, previousVSync; private bool previousBackground;

    public static void Begin(string directory)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Start the lifetime/targeting review in Play Mode.");
        var root = new GameObject("Lifetime Targeting Review"); DontDestroyOnLoad(root);
        root.AddComponent<PNLifetimeTargetingPlayReview>().Go(directory);
    }

    private void Go(string directory)
    {
        output = directory; Directory.CreateDirectory(output);
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
        if (failure != null) { evidence.Add("FAIL " + failure); Debug.LogWarning("Lifetime/targeting review failure: " + failure); }
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
        File.WriteAllLines(Path.Combine(output, "results.txt"), evidence);
        Finished?.Invoke(pass, output); Destroy(gameObject);
    }

    private IEnumerator Until(Func<bool> condition, float seconds, string name)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(condition(), name);
    }

    private static JObject RoleData(JToken despawn = null)
    {
        var data = new JObject { ["owner"] = "host", ["persist"] = true, ["followsOwner"] = false, ["lifetime"] = JValue.CreateNull() };
        if (despawn != null) data["despawn"] = despawn;
        return data;
    }

    // A hand-built enemy body with the probe behavior and, optionally, a role built from role data and a body (the factory does the same with the registered definition).
    private IEnumerator Enemy(Vector3 position, Action<PNTargetingProbeBehavior, GameObject> made, JObject roleData = null, PNBodySettings body = null)
    {
        var root = new GameObject("Review enemy " + nextEntity) { layer = 7 }; root.SetActive(false); actors.Add(root);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, world.PhysicsOwner); root.transform.position = position;
        var identity = root.AddComponent<EveEntityIdentity>(); identity.Assign(new EveEntityId(nextEntity++), EveId.Parse("project_n:sentinel"), 0); identity.BindWorld(world, "main");
        root.AddComponent<PNEnemyStat>(); var rigid = root.AddComponent<Rigidbody>(); rigid.constraints = RigidbodyConstraints.FreezeRotation;
        var capsule = root.AddComponent<CapsuleCollider>(); capsule.radius = .2f; capsule.height = 1.4f; capsule.center = Vector3.up * .7f;
        root.AddComponent<EveTimeScale>().MinScale = 0;
        var walking = root.AddComponent<PNEnemyMove>(); walking.GroundLayers = 1 << 13; walking.CanJump = false;
        var owner = root.AddComponent<PNEnemySkill>(); owner.ConfigureInitialBehavior("probe");
        var probe = root.AddComponent<PNTargetingProbeBehavior>(); probe.ConfigureId("probe");
        if (roleData != null) root.AddComponent<PNEntityRole>().Apply("project_n:enemy", PNEntitySettings.Parse(roleData), body ?? PNBodySettings.Default, null, false);
        root.AddComponent<EveEntityLoader>().Load(session.Registry); root.SetActive(true);
        var loader = root.GetComponent<EveEntityLoader>();
        float deadline = Time.realtimeSinceStartup + 20; while ((!loader.IsReady || probe.Targeting == null) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(loader.IsReady && probe.Targeting != null, "an enemy body is loaded and its probe behavior is running at " + position);
        made(probe, root);
    }

    private IEnumerator Player(Vector3 position, Action<PNStat, GameObject> made)
    {
        var root = new GameObject("Review player " + nextEntity) { layer = 6 }; root.SetActive(false); actors.Add(root);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, world.PhysicsOwner); root.transform.position = position;
        var identity = root.AddComponent<EveEntityIdentity>(); identity.Assign(new EveEntityId(nextEntity++), EveId.Parse("project_n:eve"), 0); identity.BindWorld(world, "main");
        var collider = root.AddComponent<CapsuleCollider>(); collider.radius = .2f; collider.height = 1.4f; collider.center = Vector3.up * .7f;
        var rigid = root.AddComponent<Rigidbody>(); rigid.isKinematic = true;
        var stat = root.AddComponent<PNPlayerStat>().AsPlayer(); root.AddComponent<EveTimeScale>(); root.AddComponent<EveEntityLoader>().Load(session.Registry); root.SetActive(true);
        var loader = root.GetComponent<EveEntityLoader>();
        yield return Until(() => loader.IsReady, 20, "a player body is loaded at " + position);
        made(stat, root);
    }

    private bool Alive(GameObject root) => root != null && root.GetComponent<EveEntityLoader>() != null && session.Entities.Snapshot(world).Contains(root.GetComponent<EveEntityLoader>());

    private int ScansUntilGone(GameObject root, int limit)
    {
        for (int i = 1; i <= limit; i++) { service.Scan(.5f); if (!Alive(root) || root.GetComponent<EveEntityLoader>() == null) return i; scheduler.AdvanceFrame(); }
        return -1;
    }

    // Removes a hand-built body the way the end of Drive does: unload the loader, free the identity, destroy.
    private void Drop(GameObject root)
    {
        if (root == null) return;
        try { root.GetComponent<EveEntityLoader>()?.Unload(); root.GetComponent<EveEntityIdentity>()?.ResetIdentity(); } catch (Exception e) { Debug.LogWarning("fixture drop: " + e.Message); }
        actors.Remove(root); Destroy(root);
    }

    private static void Move(GameObject root, Vector3 position)
    {
        root.transform.position = position; var rigid = root.GetComponent<Rigidbody>(); if (rigid != null && !rigid.isKinematic) rigid.position = position; Physics.SyncTransforms();
    }

    // ---------------------------------------------------------------------------------------------------------
    private IEnumerator Run()
    {
        Check(Application.isPlaying && EveRegistrySession.State == EveSessionState.Stopped, "starts in Play Mode with no running session");
        var documents = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        runtime = gameObject.AddComponent<PNGridEditReviewRuntime>(); runtime.Documents = documents;
        runtime.BeginSession(EveSessionRole.Offline); session = EveRegistrySession.Context;
        scheduler = new EveEntityPreparationScheduler(session, PNGameBootstrap.Config.Preparation); worlds = new PNWorldRuntime(session, scheduler);
        gameObject.AddComponent<PNWorldPhysicsLoop>().Bind(session);
        sync = new PNSyncService(session.Registry, true, 0, session); binder = new PNSyncWorldBinder(worlds, sync);
        world = worlds.Create(PNGridIds.SandboxTestMaze);
        float ready = Time.realtimeSinceStartup + 60; while (!world.CanExecute && world.State != EveWorldState.Failed && Time.realtimeSinceStartup < ready) yield return null;
        Check(world.CanExecute, "the maze test world is ready");
        controller = PNGridWorldController.Find(world); service = PNDespawnService.Find(world);
        Check(controller != null && service != null, "the world has its grid controller and its despawn service (feature project_n:despawn)");
        Check(worlds.Get(world).Features.Has(PNDespawnIds.Feature) && service.Settings.Near == 25f && service.Settings.Far == 35f && service.Settings.Seconds == 10f, "the world's despawn settings are the defaults 25 m, 35 m, 10 s");
        // The enemies of this fixture are built by hand, not through the preparation scheduler, so the shared retire path (host.Retire) would refuse them (it is checked by PNEntitySpawnEditorChecks);
        // the fixture removes them the way the host does at the end: unload, free the identity, destroy.
        service.Retire = loader => { var root = loader.gameObject; try { loader.Unload(); root.GetComponent<EveEntityIdentity>()?.ResetIdentity(); } catch (Exception e) { Debug.LogWarning("fixture retire: " + e.Message); return false; } actors.Remove(root); Destroy(root); return true; };
        yield return new WaitForSeconds(.2f);

        // ---- G4: the world index ----
        PNStat player = null; GameObject playerRoot = null;
        yield return Player(new Vector3(5, 0, 5), (s, r) => { player = s; playerRoot = r; });
        PNTargetingProbeBehavior nearProbe = null; GameObject nearRoot = null;
        yield return Enemy(new Vector3(10, 0, 8), (p, r) => { nearProbe = p; nearRoot = r; });
        PNTargetingProbeBehavior farProbe = null; GameObject farRoot = null;
        yield return Enemy(new Vector3(45, 0, 28), (p, r) => { farProbe = p; farRoot = r; });
        var index = PNWorldEntityIndex.For(world); var found = new List<PNStat>();
        index.Refresh(true); index.Near(player.transform.position, 8f, found);
        Check(found.Contains(player) && found.Contains(nearProbe.GetComponent<PNStat>()) && !found.Contains(farProbe.GetComponent<PNStat>()), "the world index finds the player and the near enemy within 8 m and not the far one");
        found.Clear(); index.All(found);
        Check(found.Count == 3 && ReferenceEquals(PNWorldEntityIndex.For(world), index), "the index holds the three entities, one index per world");
        int refreshes = index.Refreshes; index.Near(player.transform.position, 8f, found); index.Near(player.transform.position, 8f, found);
        Check(index.Refreshes - refreshes <= 1, "two queries in one frame refill the index at most once (" + (index.Refreshes - refreshes) + ")");

        // ---- G4: the despawn service ----
        service.enabled = false;   // the scans below are driven by hand; the service is switched on again for the test of its own rhythm
        Check(service.Removed == 0, "the service has removed nothing yet");
        var farHold = PNEntityLifetime.Of(farRoot).Hold(this);   // the far enemy is scanned during the near enemy test: held, its timer stays at zero
        int gone = ScansUntilGone(nearRoot, 60);
        Check(gone == -1 && Alive(nearRoot), "an enemy within 25 m of a living player is not removed (60 scans)");
        farHold.Dispose();
        for (int i = 0; i < 19; i++) service.Scan(.5f);
        Check(Alive(farRoot), "an enemy 46 m from the player is still there after 9.5 s");
        service.Scan(.5f);
        yield return Until(() => !Alive(farRoot), 5, "the far enemy is removed at 10 s through the entity removal");
        Check(service.Removed == 1, "the service counts one removal");

        // reset by coming near, and the hysteresis band
        PNTargetingProbeBehavior walkerProbe = null; GameObject walkerRoot = null;
        yield return Enemy(new Vector3(45, 0, 28), (p, r) => { walkerProbe = p; walkerRoot = r; });
        for (int i = 0; i < 15; i++) service.Scan(.5f);
        Move(playerRoot, new Vector3(40, 0, 25)); service.Scan(.5f);
        Move(playerRoot, new Vector3(5, 0, 5));
        for (int i = 0; i < 19; i++) service.Scan(.5f);
        Check(Alive(walkerRoot), "a player coming within 25 m resets the timer: 9.5 s after the reset the enemy is still there");
        service.Scan(.5f);
        yield return Until(() => !Alive(walkerRoot), 5, "and it goes at 10 s after the reset");

        // held
        PNTargetingProbeBehavior heldProbe = null; GameObject heldRoot = null;
        yield return Enemy(new Vector3(45, 0, 28), (p, r) => { heldProbe = p; heldRoot = r; });
        var lifetime = PNEntityLifetime.Of(heldRoot); var hold = lifetime.Hold(this);
        Check(lifetime.IsHeld && lifetime.HoldCount == 1, "a hold counts on the entity's lifetime component");
        gone = ScansUntilGone(heldRoot, 40);
        Check(gone == -1 && Alive(heldRoot), "a held enemy is not removed (40 scans)");
        hold.Dispose();
        Check(!lifetime.IsHeld, "disposing the handle releases the hold");
        for (int i = 0; i < 19; i++) service.Scan(.5f);
        Check(Alive(heldRoot), "the released enemy needs its full 10 s");
        service.Scan(.5f);
        yield return Until(() => !Alive(heldRoot), 5, "and is then removed");
        var ownerObject = new GameObject("hold owner");
        PNTargetingProbeBehavior holdProbe = null; GameObject holdRoot = null;
        yield return Enemy(new Vector3(45, 0, 28), (p, r) => { holdProbe = p; holdRoot = r; });
        PNEntityLifetime.Of(holdRoot).Hold(ownerObject);
        Check(PNEntityLifetime.Of(holdRoot).IsHeld, "a hold by a live owner object holds");
        DestroyImmediate(ownerObject);
        Check(!PNEntityLifetime.Of(holdRoot).IsHeld, "the hold ends when its owner object is destroyed");

        // role data
        PNTargetingProbeBehavior exemptProbe = null; GameObject exemptRoot = null;
        yield return Enemy(new Vector3(45, 0, 28), (p, r) => { exemptProbe = p; exemptRoot = r; }, RoleData(new JObject { ["exempt"] = true }));
        PNTargetingProbeBehavior clearProbe = null; GameObject clearRoot = null;
        yield return Enemy(new Vector3(44, 0, 28), (p, r) => { clearProbe = p; clearRoot = r; }, RoleData());
        clearRoot.AddComponent<PNRoomClearMarker>();   // what the module `project_n:room_clear_enemy` does in a room
        PNTargetingProbeBehavior quickProbe = null; GameObject quickRoot = null;
        yield return Enemy(new Vector3(43, 0, 28), (p, r) => { quickProbe = p; quickRoot = r; }, RoleData(new JObject { ["seconds"] = 2 }));
        int quick = ScansUntilGone(quickRoot, 40);
        Check(quick == 4 || quick == 5, "a role with `seconds` 2 is removed after 2 s (scan " + quick + ")");
        for (int i = 0; i < 60; i++) service.Scan(.5f);
        Check(Alive(exemptRoot), "an exempt role is never removed (60 scans)");
        Check(Alive(clearRoot), "an enemy with the room-clear marker is never removed (60 scans)");

        // nobody to measure against
        player.SetCurrent(PNContentIds.Health, 0);
        Check(player.IsDead, "the only player is dead");
        PNTargetingProbeBehavior lonelyProbe = null; GameObject lonelyRoot = null;
        yield return Enemy(new Vector3(45, 0, 27), (p, r) => { lonelyProbe = p; lonelyRoot = r; });
        for (int i = 0; i < 60; i++) service.Scan(.5f);
        Check(Alive(lonelyRoot), "with no living player in the world nothing is removed (60 scans)");
        player.SetCurrent(PNContentIds.Health, player.GetMax(PNContentIds.Health));
        Check(!player.IsDead, "the player is alive again");
        gone = ScansUntilGone(lonelyRoot, 40);
        Check(gone >= 19 && gone <= 21, "and the enemy goes 10 s after a living player is back (scan " + gone + ")");
        Destroy(exemptRoot); Destroy(clearRoot); actors.Remove(exemptRoot); actors.Remove(clearRoot);
        yield return null;

        // home and leash
        PNTargetingProbeBehavior homeProbe = null; GameObject homeRoot = null;
        yield return Enemy(new Vector3(40, 0, 20), (p, r) => { homeProbe = p; homeRoot = r; }, RoleData(new JObject { ["leash"] = 4, ["seconds"] = 3600, ["far"] = 1000 }));
        service.Scan(.5f);
        var home = homeRoot.GetComponent<PNEnemyHome>();
        Check(home != null && home.Leash == 4f && (home.Position - new Vector3(40, 0, 20)).magnitude < .5f, "the first scan gives an enemy with a leash its home where it stands");
        Move(homeRoot, new Vector3(43, 0, 25)); service.Scan(.5f);
        Check(home.DistanceFlat(homeRoot.transform.position) > 4f && home.IsReturning, "beyond the leash with no player near it is sent home");
        yield return Until(() => home.DistanceFlat(homeRoot.transform.position) < 1.5f, 30, "and walks back to within 1.5 m of its home");

        // the service by itself
        PNTargetingProbeBehavior autoProbe = null; GameObject autoRoot = null;
        yield return Enemy(new Vector3(45, 0, 28), (p, r) => { autoProbe = p; autoRoot = r; });
        service.enabled = true; int scans = service.Scans; Time.timeScale = TimeScale;
        float began = Time.time;
        yield return Until(() => !Alive(autoRoot), 20, "the service removes a far enemy by itself, without being driven");
        Time.timeScale = previousTimeScale;
        Check(service.Scans > scans + 15, "its own scans ran at the 0.5 s rhythm (" + (service.Scans - scans) + " scans in " + (Time.time - began).ToString("F1") + " s)");

        // ---- G5: the shared targeting ----
        Drop(homeRoot); Drop(walkerRoot); Drop(holdRoot); Drop(nearRoot);
        yield return null; yield return null;
        Move(playerRoot, new Vector3(8, 0, 6.25f));
        PNTargetingProbeBehavior seeker = null; GameObject seekerRoot = null;
        yield return Enemy(new Vector3(4, 0, 6.25f), (p, r) => { seeker = p; seekerRoot = r; });
        var targeting = seeker.Targeting;
        Check(targeting.Range == 8f, "without role data the range is the behavior's default (8 m)");
        index.Refresh(true);
        Check(targeting.Acquire() == player, "the shared targeting acquires the player 4 m away through the world index");
        Check(targeting.Keep(player), "and keeps it");
        Check(PNGridSight.Clear(controller.Data, 4, 6.25f, 8, 6.25f), "the cells let the sight through before a wall is placed");
        Check(PNGridCommands.ApplyPlace(session.Registry, controller, new Vector3(5, 0, 6.25f), new JObject { ["def"] = "project_n:placeable_vanish", ["x"] = 12, ["z"] = 12, ["dir"] = 0 }), "a wall is placed on cell (12, 12) between them");
        controller.Pump(); AstarPath.active.FlushWorkItems();
        yield return Until(() => controller.Host.PendingChunks == 0, 5, "the wall's chunk is rebuilt");
        var physics = world.PhysicsOwner.GetPhysicsScene();
        bool rayBlocked = physics.Raycast(new Vector3(4, .2f, 6.25f), Vector3.right, 4f, LayerMask.GetMask("Wall"), QueryTriggerInteraction.Ignore);
        Check(!PNGridSight.Clear(controller.Data, 4, 6.25f, 8, 6.25f) && rayBlocked, "the cells and the physics `Wall` ray both say the wall blocks the sight");
        index.Refresh(true);
        Check(targeting.Acquire() == null && !targeting.Keep(player), "the targeting no longer acquires or keeps the player behind the wall");
        for (int hit = 0; hit < 6; hit++) PNGridCommands.ApplyBreak(controller, new Vector3(5, 0, 6.25f), new JObject { ["x"] = 12, ["z"] = 12 });
        controller.Pump(); AstarPath.active.FlushWorkItems();
        yield return Until(() => controller.Host.PendingChunks == 0 && controller.Placements.At(12, 12) == null, 5, "the wall is broken and its chunk rebuilt");
        index.Refresh(true);
        Check(targeting.Acquire() == player, "the player is acquired again once the wall is gone");

        // aggro choice: a second candidate with a higher aggro stat at a similar distance
        PNStat second = null; GameObject secondRoot = null;
        yield return Player(new Vector3(7, 0, 8.25f), (s, r) => { second = s; secondRoot = r; });
        index.Refresh(true);
        var chosen = targeting.Acquire();
        Note("two candidates at " + targeting.Distance(player.transform.position).ToString("F2") + " m and " + targeting.Distance(second.transform.position).ToString("F2") + " m: the rule chose " + (chosen == player ? "the first" : chosen == second ? "the second" : "nobody"));
        Check(chosen == (targeting.Distance(second.transform.position) < targeting.Distance(player.transform.position) ? second : player) || PNTargetSelector.AggroOf(player) != PNTargetSelector.AggroOf(second), "with equal aggro the nearer candidate wins");
        Drop(secondRoot);
        yield return null; yield return null;

        // range from role data
        PNTargetingProbeBehavior visionProbe = null; GameObject visionRoot = null;
        yield return Enemy(new Vector3(4, 0, 6.25f), (p, r) => { visionProbe = p; visionRoot = r; }, RoleData(), new PNBodySettings("", PNDamageMode.Normal, PNHealthBarMode.Auto, null, PNAggroMode.Normal, null, "", 3f));
        index.Refresh(true);
        Check(visionProbe.Targeting.Range == 3f && visionProbe.Targeting.Acquire() == null, "the role's `body.vision` (3 m) replaces the default: the player 4 m away is out of range");
        Check(seeker.Targeting.Acquire() == player, "the other enemy, without role data, still sees 4 m");

        // cells against the physics ray over many pairs
        var random = new System.Random(11); int agree = 0, total = 0, clear = 0;
        for (int i = 0; i < 400; i++)
        {
            float x0 = (float)(random.NextDouble() * 40 + 3), z0 = (float)(random.NextDouble() * 24 + 3), x1 = (float)(random.NextDouble() * 40 + 3), z1 = (float)(random.NextDouble() * 24 + 3);
            if (controller.Data.IsSolid((int)(x0 / .5f), (int)(z0 / .5f)) || controller.Data.IsSolid((int)(x1 / .5f), (int)(z1 / .5f))) continue;
            var d = new Vector3(x1 - x0, 0, z1 - z0); if (d.sqrMagnitude < .01f) continue;
            bool byCells = PNGridSight.Clear(controller.Data, x0, z0, x1, z1);
            bool byRay = !physics.Raycast(new Vector3(x0, .2f, z0), d.normalized, d.magnitude, LayerMask.GetMask("Wall"), QueryTriggerInteraction.Ignore);
            total++; if (byCells == byRay) agree++; if (byCells) clear++;
        }
        Note("sight by cells against the `Wall` ray over " + total + " random pairs in the maze: " + agree + " agree, " + clear + " clear");
        Check(total >= 150 && agree >= total * 0.97, "the two sight tests agree on at least 97% of the pairs (" + agree + "/" + total + ")");

        // ---- cleanup ----
        Drop(visionRoot); Drop(seekerRoot);
        yield return null;
        foreach (var left in actors.ToArray()) Drop(left);   // every hand-built body still in the world (the player, the exempt and held enemies)
        yield return null; yield return null;
        bool unloaded = false; float until = Time.realtimeSinceStartup + 10;
        while (!unloaded && Time.realtimeSinceStartup < until) { unloaded = worlds.Unload(world); if (!unloaded) yield return null; }
        Check(unloaded, "the grid world unloads");
        yield return null; yield return null;
        Check(PNDespawnService.Find(world) == null, "the despawn service is forgotten");
        Check(FindObjectsByType<PNDespawnService>(FindObjectsInactive.Include).Length == 0, "no despawn service component is left");
    }
}
#endif
