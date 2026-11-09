#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// Native checks for the world side of the sync layer and the roguelite frame (Docs/NetworkStatePlan.md N1-N2, Docs/RogueliteFramePlan.md F0/F2): the real base content loads with the
// roguelite definitions and their worlds, authored world objects are registered per world instance and per content slot, a world that repeats or misspells a key refuses to load,
// unloading a world forgets its objects and blocks, and spawn groups pick the same places for the same seed. Run: PNSyncWorldChecks.Run().
public sealed class PNSyncFixtureProvider : IPNWorldProvider
{
    public enum Variant { Good, DuplicateKey, BadKey, DuplicateGroup }
    private readonly Variant variant;
    public PNSyncFixtureProvider(Variant variant) { this.variant = variant; }
    private static void SetKey(PNWorldObject item, string key) { typeof(PNWorldObject).GetField("key", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(item, key); }
    private static void AddObject(GameObject root, string key)
    {
        var go = new GameObject("object " + key); go.transform.SetParent(root.transform);
        SetKey(go.AddComponent<PNWorldObject>(), key);
    }
    private static void AddGroup(GameObject root, string key, float radius)
    {
        var go = new GameObject("group " + key); go.transform.SetParent(root.transform); go.transform.position = new Vector3(10, 0, 5);
        go.AddComponent<PNRoomSpawnGroup>().Configure(key, PNRoomSpawnGroup.Shape.Circle, null, radius);
    }
    public IEnumerator Load(PNWorldBuildContext context)
    {
        for (int slot = 0; slot < 2; slot++)
        {
            var root = new GameObject("Sync fixture " + slot); SceneManager.MoveGameObjectToScene(root, context.PhysicsScene);
            context.Own(() => { if (root != null) UnityEngine.Object.DestroyImmediate(root); });
            // the same keys in the two slots are fine: a key is unique inside its content slot
            AddObject(root, "a"); AddObject(root, variant == Variant.DuplicateKey && slot == 0 ? "a" : variant == Variant.BadKey && slot == 0 ? "Bad Key" : "b");
            if (slot == 0) { AddGroup(root, "north", 3f); if (variant == Variant.DuplicateGroup) AddGroup(root, "north", 2f); }
            context.AddSlot("chunk-" + slot, context.PhysicsScene, new[] { root });
            yield return null;
        }
    }
}

public sealed class PNSyncFixtureModule : EveIContentModule
{
    public static readonly EveId State = EveId.Parse("fixture:sync_state");
    public void Register(EveRegistryHub hub)
    {
        var worlds = hub.GetOrCreate<PNWorldDefinition>();
        worlds.Register(new PNWorldDefinition(EveId.Parse("fixture:sync_good"), new string('a', 64), typeof(PNSyncFixtureProvider), () => new PNSyncFixtureProvider(PNSyncFixtureProvider.Variant.Good)));
        worlds.Register(new PNWorldDefinition(EveId.Parse("fixture:sync_dup_key"), new string('b', 64), typeof(PNSyncFixtureProvider), () => new PNSyncFixtureProvider(PNSyncFixtureProvider.Variant.DuplicateKey)));
        worlds.Register(new PNWorldDefinition(EveId.Parse("fixture:sync_bad_key"), new string('c', 64), typeof(PNSyncFixtureProvider), () => new PNSyncFixtureProvider(PNSyncFixtureProvider.Variant.BadKey)));
        worlds.Register(new PNWorldDefinition(EveId.Parse("fixture:sync_dup_group"), new string('d', 64), typeof(PNSyncFixtureProvider), () => new PNSyncFixtureProvider(PNSyncFixtureProvider.Variant.DuplicateGroup)));
        hub.SyncStates().Register(new PNSyncStateDefinition(State, 1, 256, PNSyncTargets.WorldObject));
    }
}

public static class PNSyncWorldChecks
{
    private sealed class Source : IPNSyncStateSource { public EveId StateId => PNSyncFixtureModule.State; public JToken Value = new JObject(); public JToken Capture() => Value.DeepClone(); }
    private sealed class Sink : IPNSyncStateSink { public EveId StateId => PNSyncFixtureModule.State; public readonly List<ulong> Revisions = new List<ulong>(); public void Apply(JToken value, ulong revision) { Revisions.Add(revision); } }

    // The real map of the sample rooms (Docs/RogueliteFramePlan.md F2), checked as authored content: the scene is opened additively and closed without saving (a scene provider loads with
    // LoadSceneAsync, which needs Play Mode: loading it as a world is part of the Player review). Every key the sample rooms name exists exactly once, the spawn groups and the player spawn
    // point are in place, the navigation cache is the one baked from the floor, and the scene carries nothing a world scene must not (camera, runtime, light, event system).
    // Run: PNSyncWorldChecks.RunMap().
    public static string RunMap()
    {
        var rows = new List<string>(); void Check(bool value, string label) { if (!value) throw new Exception(label); rows.Add("PASS " + label); }
        var baseDocs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, baseDocs);
        var id = EveId.Parse("project_n:map_star_test_01");
        Check(AssetDatabaseHas(PNScenes.RoomStarTest01), "the map scene exists at the registered path");
        Check(UnityEditor.EditorBuildSettings.scenes.Any(x => x.path == PNScenes.RoomStarTest01 && x.enabled), "and is in the build settings");
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(PNScenes.RoomStarTest01, UnityEditor.SceneManagement.OpenSceneMode.Additive);
        try
        {
            var roots = scene.GetRootGameObjects();
            var objects = roots.SelectMany(x => x.GetComponentsInChildren<PNWorldObject>(true)).ToArray(); var groupList = roots.SelectMany(x => x.GetComponentsInChildren<PNRoomSpawnGroup>(true)).ToArray();
            PNWorldObject.ValidateSet(objects); PNRoomSpawnGroup.ValidateSet(groupList);
            var keys = objects.Select(x => x.Key).OrderBy(x => x).ToArray(); var groups = groupList.Select(x => x.Group).OrderBy(x => x).ToArray();
            Check(keys.SequenceEqual(new[] { "fountain", "gate_a", "gate_b", "gate_c", "room", "shop_heal", "start_switch" }), "its seven world objects: room, start switch, three gates, fountain, shop");
            Check(groups.SequenceEqual(new[] { "center", "north", "south" }), "its three spawn groups: north, south, center");
            var spawn = roots.SelectMany(x => x.GetComponentsInChildren<PNPlayerSpawnPoint>(true)).Single(); spawn.Validate();
            Check(spawn.SpawnEntry == "player" && spawn.Ground != null && spawn.Ground.gameObject.layer == 13 && spawn.Character == "project_n:eve", "the player spawn point has the Eve prefab and a ground on layer 13");
            var nav = roots.SelectMany(x => x.GetComponentsInChildren<PNWorldNavigationSource>(true)).Single(); nav.Validate();
            Check(nav.Cache.ProfileId == "ground-small" && Mathf.Approximately(nav.Cache.Radius, 0.2f) && Mathf.Approximately(nav.Cache.Height, 1.4f), "navigation uses the same ground-small profile as ErMode (radius 0.2, height 1.4)");
            Check(PNNavigationBaker.GeometryHash(nav.GeometryRoots) == nav.Cache.MapHash && nav.GeometryRoots.Length == 1 && nav.GeometryRoots[0].layer == 13, "the cache was baked from the floor as it is now");
            var interactive = objects.Where(x => x.GetComponent<PNInteractable>() != null).ToArray();
            Check(interactive.Select(x => x.Key).OrderBy(x => x).SequenceEqual(new[] { "fountain", "gate_a", "gate_b", "gate_c", "shop_heal", "start_switch" }), "the switch, the three gates, the fountain and the shop heal carry a PNInteractable");
            Check(interactive.All(x => hub.Instances().TryGetKind<PNInteractableDefinition>(EveId.Parse(x.GetComponent<PNInteractable>().DefinitionId), out var found) && found.Settings.Range > 0), "each names a registered interactable definition");
            var keySet = new HashSet<string>(keys); var groupSet = new HashSet<string>(groups);
            foreach (var room in hub.RogueliteRooms().Snapshot().Values.Where(x => x.World == id))
            {
                var wanted = room.Exits.Concat(room.Interactables).Concat(room.Start.Interactable.Length > 0 ? new[] { room.Start.Interactable } : new string[0]);
                Check(wanted.All(keySet.Contains) && room.Waves.SelectMany(w => w.Spawns).All(s => groupSet.Contains(s.Group)), "room " + room.Id + " finds all its keys and spawn groups in the map");
            }
            Check(!roots.SelectMany(x => x.GetComponentsInChildren<Camera>(true)).Any() && !roots.SelectMany(x => x.GetComponentsInChildren<EveRuntime>(true)).Any() && !roots.SelectMany(x => x.GetComponentsInChildren<Light>(true)).Any()
                && !roots.SelectMany(x => x.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true)).Any() && !roots.SelectMany(x => x.GetComponentsInChildren<EveEntityLoader>(true)).Any(), "no camera, runtime, light, event system or authored entity in the map");
            Check(roots.SelectMany(x => x.GetComponentsInChildren<Collider>(true)).Where(c => c.gameObject.layer == 9).Count() == 4 && roots.SelectMany(x => x.GetComponentsInChildren<Collider>(true)).Count(c => c.gameObject.layer == 13) == 1, "one ground collider on layer 13 and four wall colliders on layer 9");
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true); }
        return "PASS " + rows.Count + " native map content cases" + System.Environment.NewLine + string.Join(System.Environment.NewLine, rows);
    }
    private static bool AssetDatabaseHas(string path) => UnityEditor.AssetDatabase.LoadMainAssetAtPath(path) != null;

    public static string Run()
    {
        var rows = new List<string>(); void Check(bool value, string label) { if (!value) throw new Exception(label); rows.Add("PASS " + label); }
        void Reject(Action action, string label) { bool rejected = false; try { action(); } catch (Exception) { rejected = true; } Check(rejected, label); }
        var baseDocs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var modules = new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNSyncFixtureModule() };
        var hub = new EveContentBootstrap().Build(modules, baseDocs);

        // ---- the real base content: the frame's sample run, its rooms, and the map world they name ----
        var run = hub.RogueliteRuns().Get(EveId.Parse("project_n:standard_run"));
        Check(run.Floors.Count == 3 && run.Floors.All(f => hub.RogueliteFloors().Get(f).Rooms.Count >= 1) && hub.RogueliteFloors().Get(run.Floors[1]).Choices == 2, "the base content carries the sample run with three floors, the second one offering two gates");
        var rooms = hub.RogueliteRooms().Snapshot().Values.Where(x => x.Id.Namespace == "project_n").ToArray();
        Check(rooms.Length == 5 && rooms.All(x => hub.Get<PNWorldDefinition>().TryGet(x.World, out _)) && rooms.All(x => x.World == EveId.Parse("project_n:map_star_test_01")), "its five rooms are played on the registered map world");
        var mapDefinition = hub.Get<PNWorldDefinition>().Get(EveId.Parse("project_n:map_star_test_01"));
        Check(mapDefinition.Policy.Empty == PNEmptyWorldPolicy.Continue && hub.Get<PNWorldDefinition>().Get(EveId.Parse("base:map_star_test_01")) == mapDefinition, "the map world is not discarded when empty (a new room is empty until the team arrives) and has its base: alias");
        var unknownWorld = new JObject { ["schemaVersion"] = 1, ["extensions"] = new JObject { ["project_n"] = new JObject { ["rogueliteRooms"] = new JArray(new JObject {
            ["id"] = "test:orphan", ["kind"] = "rest", ["world"] = "test:no_such_map", ["start"] = new JObject { ["mode"] = "none" }, ["clear"] = "none", ["exits"] = new JArray("gate_a") }) } } };
        Reject(() => new EveContentBootstrap().Build(modules, baseDocs.Concat(new[] { new EveContentDocument("orphan", unknownWorld.ToString(), "test", dependencies: new[] { "project_n" }) }).ToArray()), "a room on a world nobody registered is refused when the content loads");

        // ---- worlds, objects, blocks ----
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, baseDocs, "fixture:sync"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile()); var worlds = new PNWorldRuntime(session, scheduler);
        var sync = new PNSyncService(session.Registry, true, 0, session); var binder = new PNSyncWorldBinder(worlds, sync);
        void Pump(Func<bool> ready) { for (int i = 0; i < 2000 && !ready(); i++) scheduler.AdvanceFrame(); if (!ready()) throw new Exception("Sync fixture did not drain."); }
        try
        {
            var first = worlds.Create(EveId.Parse("fixture:sync_good")); Pump(() => first.CanExecute);
            var a0 = PNSyncTarget.ForWorldObject(first.InstanceId, first.Incarnation, "chunk-0", "a"); var a1 = PNSyncTarget.ForWorldObject(first.InstanceId, first.Incarnation, "chunk-1", "a");
            object o0 = null, o1 = null;
            Check(sync.ObjectCount == 4 && sync.TryGetObject(a0, out o0) && sync.TryGetObject(a1, out o1) && !ReferenceEquals(o0, o1) && ((PNWorldObject)o0).IsBound && ((PNWorldObject)o0).Target == a0, "a ready world registers its objects, the same key once per content slot");
            Check(!sync.TryGetObject(PNSyncTarget.ForWorldObject(first.InstanceId, first.Incarnation, "chunk-0", "zzz"), out _) && !sync.TryGetObject(PNSyncTarget.ForWorldObject(first.InstanceId, first.Incarnation + 1, "chunk-0", "a"), out _), "another key or another incarnation names nothing");

            var source = new Source(); var sink = new Sink();
            using (sync.RegisterSource(a0, source)) using (sync.RegisterSink(a0, sink))
            {
                source.Value = new JObject { ["open"] = true }; sync.MarkDirty(a0, PNSyncFixtureModule.State);
                Check(sync.Flush() == 1 && sink.Revisions.SequenceEqual(new ulong[] { 1 }), "a block on a world object reaches its reader");
            }

            var second = worlds.Create(EveId.Parse("fixture:sync_good")); Pump(() => second.CanExecute);
            var b0 = PNSyncTarget.ForWorldObject(second.InstanceId, second.Incarnation, "chunk-0", "a");
            Check(sync.ObjectCount == 8 && b0 != a0 && sync.TryGetObject(b0, out var other) && !ReferenceEquals(other, o0), "two worlds built from one map keep their objects apart");
            using (sync.RegisterSource(b0, new Source())) { sync.MarkDirty(b0, PNSyncFixtureModule.State); sync.Flush(); Check(sync.TryGetLatest(b0, PNSyncFixtureModule.State, out var rb, out var vb) && sync.TryGetLatest(a0, PNSyncFixtureModule.State, out var ra, out var va) && rb == 1 && ra == 1 && va["open"] != null && vb["open"] == null, "each world has its own value of the block"); }

            var kept = new Source(); sync.RegisterSource(a1, kept);
            Pump(() => worlds.Unload(first));
            Check(sync.ObjectCount == 4 && !sync.TryGetObject(a0, out _) && sync.TryGetObject(b0, out _) && !((PNWorldObject)o0).IsBound, "unloading a world forgets its objects and unbinds them");
            Check(!sync.TryGetLatest(a0, PNSyncFixtureModule.State, out _, out _) && sync.TryGetLatest(b0, PNSyncFixtureModule.State, out _, out _), "and its blocks, while the other world keeps its own");
            Reject(() => sync.MarkDirty(a1, PNSyncFixtureModule.State), "a writer of an unloaded world is gone");

            // ---- worlds that must refuse to load ----
            int objectsBefore = sync.ObjectCount;
            foreach (var bad in new[] { ("fixture:sync_dup_key", "a world that repeats a key in one slot"), ("fixture:sync_bad_key", "a world with a misspelled key"), ("fixture:sync_dup_group", "a world that repeats a spawn group") })
            {
                var failing = worlds.Create(EveId.Parse(bad.Item1)); Pump(() => failing.State == EveWorldState.Failed);
                Check(failing.Failure != null && sync.ObjectCount == objectsBefore, bad.Item2 + " fails to load and registers nothing");
                Pump(() => worlds.Unload(failing));
            }

            // ---- spawn groups ----
            var holder = new GameObject("groups"); holder.transform.position = new Vector3(2, 0.5f, -3);
            var circle = holder.AddComponent<PNRoomSpawnGroup>(); circle.Configure("round", PNRoomSpawnGroup.Shape.Circle, null, 4f);
            var one = new PNRunRandom(77); var two = new PNRunRandom(77); bool same = true, inside = true;
            for (int i = 0; i < 200; i++)
            {
                var p = circle.Pick(one); var q = circle.Pick(two);
                same &= p == q; inside &= new Vector2(p.x - 2, p.z + 3).magnitude <= 4.0001f && Mathf.Approximately(p.y, 0.5f);
            }
            Check(same && inside, "a circle group puts the same seed in the same places, all inside the disc at the group's height");
            var pointsHolder = new GameObject("points"); var p1 = new GameObject("p1").transform; var p2 = new GameObject("p2").transform; p1.position = new Vector3(1, 0, 1); p2.position = new Vector3(-1, 0, -1);
            var points = pointsHolder.AddComponent<PNRoomSpawnGroup>(); points.Configure("pair", PNRoomSpawnGroup.Shape.Points, new[] { p1, p2 }, 1f);
            var rng = new PNRunRandom(3); int hits1 = 0; for (int i = 0; i < 400; i++) if (points.Pick(rng) == p1.position) hits1++;
            Check(hits1 > 140 && hits1 < 260, "a points group picks each point about half of the time");
            var tempA = new GameObject("tempA"); var tempB = new GameObject("tempB");
            Reject(() => tempA.AddComponent<PNRoomSpawnGroup>().Configure("empty", PNRoomSpawnGroup.Shape.Points, new Transform[0], 1f), "a points group without points is refused");
            Reject(() => tempB.AddComponent<PNRoomSpawnGroup>().Configure("huge", PNRoomSpawnGroup.Shape.Circle, null, 51f), "a disc wider than 50 m is refused");
            UnityEngine.Object.DestroyImmediate(tempA); UnityEngine.Object.DestroyImmediate(tempB);
            UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(pointsHolder); UnityEngine.Object.DestroyImmediate(p1.gameObject); UnityEngine.Object.DestroyImmediate(p2.gameObject);

            Pump(() => worlds.Unload(second));
            Check(sync.ObjectCount == 0, "after every world is gone no object is left");
        }
        finally { binder.Dispose(); sync.Dispose(); EveRegistrySession.End(); }
        return "PASS " + rows.Count + " native sync world and roguelite content cases\n" + string.Join("\n", rows);
    }
}
#endif
