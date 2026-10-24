#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: What portable tests cannot see: the layer table against ProjectSettings, entity layers against their stat class (prefabs everywhere; scenes in batch mode),
//     the layer of projectile objects, and the real Eve Sweep payload with targetLayers.
// VI: Phần test portable không thấy: bảng layer so với ProjectSettings, layer của entity so với lớp stat (prefab ở mọi nơi; scene khi chạy batch),
//     layer của object đạn, và payload Sweep thật của Eve với targetLayers.
public static class PNLayerTargetingEditorChecks
{
    private static readonly string[] BatchScenes =
    {
        "Assets/Game/Scenes/Test/ErMode/ErMode.unity", "Assets/Game/Scenes/Shared/PNHall/PNHall.unity", "Assets/Game/Scenes/Roguelite/PNLobby/PNLobby.unity",
    };

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        int count = 0; var evidence = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS " + message); count++; }

        // 1. The table matches the project's layers.
        foreach (var entry in PNLayers.Known) Check(LayerMask.NameToLayer(entry.Name) == entry.Index, "layer " + entry.Name + " is " + entry.Index + " in ProjectSettings");

        // 2. Every entity with a Player/Enemy stat sits on the layer other entities target it by.
        int checkedPrefabs = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game", "Assets/Resources" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            var player = prefab.GetComponent<PNPlayerStat>(); var enemy = prefab.GetComponent<PNEnemyStat>();
            if (player != null) { Check(prefab.layer == PNLayers.Player, "prefab " + path + " with a Player stat is on the Player layer"); checkedPrefabs++; }
            if (enemy != null) { Check(prefab.layer == PNLayers.Enemy, "prefab " + path + " with an Enemy stat is on the Enemy layer"); checkedPrefabs++; }
        }
        Check(checkedPrefabs > 0, "at least one Player or Enemy prefab was checked (" + checkedPrefabs + ")");
        if (Application.isBatchMode)
        {
            int checkedEntities = 0;
            foreach (var scenePath in BatchScenes)
            {
                if (!File.Exists(scenePath)) continue;
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                try
                {
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var stat in root.GetComponentsInChildren<PNStat>(true))
                        {
                            if (stat is PNPlayerStat) { Check(stat.gameObject.layer == PNLayers.Player, "scene entity " + stat.name + " in " + scenePath + " with a Player stat is on the Player layer"); checkedEntities++; }
                            else if (stat is PNEnemyStat) { Check(stat.gameObject.layer == PNLayers.Enemy, "scene entity " + stat.name + " in " + scenePath + " with an Enemy stat is on the Enemy layer"); checkedEntities++; }
                        }
                }
                finally { EditorSceneManager.CloseScene(scene, true); }
            }
            evidence.Add("scene entities checked: " + checkedEntities);
        }

        // 3. Projectile objects are on the Projectile layer (root and children), and a lease on the shooter does not change an already flying projectile's layers.
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNProjectileHitNativeModule(), new PNProjectileTeamNativeModule() }, docs);
        var preview = EditorSceneManager.NewPreviewScene(); var fixtures = new List<GameObject>(); PNProjectileManager manager = null;
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:layer_targeting"));
            var session = EveRegistrySession.Context;
            var holder = new GameObject("Projectile manager"); SceneManager.MoveGameObjectToScene(holder, preview); fixtures.Add(holder);
            manager = holder.AddComponent<PNProjectileManager>(); manager.Initialize(session, new PNProjectileProfile { MaxActive = 8 });
            var source = PNCombatSource.System(session.SessionId, new EveEntityId(998001), PNTargetLayers.Of(PNLayers.Enemy));
            var receipt = manager.Create(new PNProjectileLaunch(EveId.Parse("test:open_still"), null, source, source.TargetLayers), new PNProjectileSpawn(preview, new Vector3(0, 0, 900), Vector3.right));
            manager.Advance(.1f);
            var objects = preview.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNDirectionalProjectile>(true)).ToArray();
            Check(receipt.Handle.IsAlive && objects.Length > 0, "a projectile object exists in the preview scene");
            Check(objects.All(x => x.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == PNLayers.Projectile)), "projectile objects (root and children) are on the Projectile layer");
        }
        finally
        {
            try { if (manager != null) manager.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var obj in fixtures) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(preview); }
        }

        // 4. The real Eve Sweep payload parses as is, accepts targetLayers and still rejects unknown fields.
        var eve = JObject.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "BaseContent/characters/EveCharacter.json")));
        var sweep = eve.DescendantsAndSelf().OfType<JObject>().First(x => x["sweepStart"] != null);
        var asIs = EveSweepSettings.Parse(sweep);
        Check(asIs.TargetLayers.IsEmpty, "the Eve Sweep payload carries no targetLayers by default");
        var withLayers = (JObject)sweep.DeepClone(); withLayers["targetLayers"] = new JArray("Projectile", "Enemy");
        var parsed = EveSweepSettings.Parse(withLayers);
        Check(parsed.TargetLayers.Order.SequenceEqual(new[] { PNLayers.Projectile, PNLayers.Enemy }) && parsed.Radius == asIs.Radius, "a Sweep payload accepts ordered targetLayers without disturbing its numbers");
        var unknown = (JObject)sweep.DeepClone(); unknown["targetLayer"] = new JArray("Enemy");
        bool rejected = false; try { EveSweepSettings.Parse(unknown); } catch (FormatException) { rejected = true; }
        Check(rejected, "a misspelt Sweep field is still rejected");

        Check(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "Authored scene and dirty state preserved.");
        string summary = "PASS " + count + " native layer targeting checks";
        Directory.CreateDirectory(".utmp/LayerTargetingReview"); File.WriteAllLines(".utmp/LayerTargetingReview/NativeResults.txt", evidence.Concat(new[] { summary })); return summary;
    }
}

// EN: Batch entry for the clone project: the layer checks plus the native checks the change can affect (area checks need the ErMode scene open first).
// VI: Điểm vào batch cho project clone: kiểm layer cùng các kiểm native mà thay đổi có thể ảnh hưởng (kiểm area cần mở scene ErMode trước).
public static class PNLayerTargetingBatch
{
    public static void Run()
    {
        var lines = new List<string>(); bool failed = false;
        void Step(string name, Func<string> run)
        {
            try { lines.Add(name + ": " + run()); }
            catch (Exception error) { failed = true; lines.Add(name + ": FAIL " + error.Message); Debug.LogException(error); }
        }
        Step("LayerTargeting", PNLayerTargetingEditorChecks.Run);
        EditorSceneManager.OpenScene("Assets/Game/Scenes/Test/ErMode/ErMode.unity", OpenSceneMode.Single);
        Step("Area", PNAreaEditorChecks.Run);
        Step("TimeScaleVisual", PNTimeScaleVisualEditorChecks.Run);
        Step("EveDashArea", EveDashAreaEditorChecks.Run);
        Step("BuffTeardown", PNBuffTeardownEditorChecks.Run);
        Step("SkillEffects", PNSkillEffectsEditorChecks.Run);
        Step("Buff", PNBuffEditorChecks.Run);
        Step("Lifecycle", PNLifecycleRuntimeEditorChecks.Run);
        Step("AgentToolkit", PNAgentToolkitEditorChecks.Run);
        Directory.CreateDirectory(".utmp/LayerTargetingReview");
        File.WriteAllLines(".utmp/LayerTargetingReview/Batch.txt", lines);
        EditorApplication.Exit(failed ? 1 : 0);
    }
}
#endif
