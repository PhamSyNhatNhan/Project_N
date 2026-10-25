#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Real content and native entity lifecycle, without touching authored scene objects.
public static class PNEnemyTypeEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped)
            throw new InvalidOperationException("Enemy type checks require idle Edit Mode and a stopped session.");
        var active = SceneManager.GetActiveScene(); bool dirty = active.isDirty;
        var preview = EditorSceneManager.NewPreviewScene(); bool started = false; int checks = 0;
        Action<bool, string> check = (valid, message) => { if (!valid) throw new Exception(message); checks++; };
        try
        {
            var documents = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"),
                Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
            var fixtures = new EveContentDocument("native_enemy_types.json", @"{schemaVersion:1,characters:[
                {id:'test:elite',data:{enemyTypeId:'base:elite'},stats:[{statId:'base:health',base:100,initialCurrent:100}]},
                {id:'test:boss',data:{enemyTypeId:'base:boss'},stats:[{statId:'base:health',base:100,initialCurrent:100}]},
                {id:'test:old',stats:[{statId:'base:health',base:100,initialCurrent:100}]}
            ]}", "test", dependencies: new[] { "project_n" });
            var modules = new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() };
            var hub = new EveContentBootstrap().Build(modules, documents.Concat(new[] { fixtures }));
            check(hub.EnemyTypes().Count == 3 && hub.EnemyTypes().IsSealed, "Three sealed registered types");
            check(hub.EnemyTypes().BindingSnapshot().All(binding => !binding.AllowsOverride), "Original types locked");
            EveRegistrySession.Begin(); started = true;
            EveRegistrySession.Start(hub, EveSessionRole.Offline,
                EveContentCompatibility.Create(hub, documents.Concat(new[] { fixtures }), "test:native_enemy_types"));
            var characters = new[] { "project_n:dummy", "test:elite", "test:boss", "test:old" };
            var expected = new[] { PNContentIds.NormalEnemy, PNContentIds.EliteEnemy, PNContentIds.BossEnemy, PNContentIds.NormalEnemy };
            PNEnemyStat first = null;
            for (int i = 0; i < characters.Length; i++)
            {
                var root = new GameObject("Enemy classification probe " + characters[i]); root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, preview);
                root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(97200 + i), EveId.Parse(characters[i]), 0);
                var stat = root.AddComponent<PNEnemyStat>(); var loader = root.AddComponent<EveEntityLoader>();
                loader.Load(hub);
                check(loader.IsReady && stat.EnemyTypeId == expected[i], "Native Loader classification " + characters[i]);
                check(stat.EnemyType.Id == expected[i] && stat.TargetLayers.Mask == PNLayers.Bit(PNLayers.Player), "Registry definition and Enemy targets the Player layer");
                check(stat.GetCurrent(PNContentIds.Health) == (i == 0 ? 200000000d : 100d), "Classification leaves authored HP unchanged");
                if (i == 0) first = stat;
                else check(first.EnemyTypeId == PNContentIds.NormalEnemy, "Classification isolated between entities");
                stat.SetCurrent(PNContentIds.Health, 1d); loader.Load(hub);
                check(stat.GetCurrent(PNContentIds.Health) == 1d && stat.EnemyTypeId == expected[i], "Ready Load is idempotent");
                loader.Unload();
                bool unavailable = false; try { var unused = stat.EnemyTypeId; } catch (InvalidOperationException) { unavailable = true; }
                check(!stat.IsReady && unavailable, "Unload clears classification");
                loader.Load(hub); check(stat.EnemyTypeId == expected[i], "Reload restores classification");
            }
            var invalid = new EveRegistryHub(); new PNContentModule().Register(invalid);
            invalid.GetOrCreate<EveCharacterDefinition>().Register(new EveCharacterDefinition(EveId.Parse("test:bad"),
                Array.Empty<EveInitialStat>(), null, "", JObject.Parse("{enemyTypeId:'test:missing'}")));
            invalid.Seal();
            var badRoot = new GameObject("Invalid enemy probe"); badRoot.SetActive(false); SceneManager.MoveGameObjectToScene(badRoot, preview);
            var bad = badRoot.AddComponent<PNEnemyStat>();
            bool rejected = false; try { bad.Initialize(invalid.Characters.Get(EveId.Parse("test:bad")), invalid); }
            catch (System.Collections.Generic.KeyNotFoundException) { rejected = true; }
            check(rejected && !bad.IsReady, "Invalid classification cannot initialize native stats");
            var playerRoot = new GameObject("Player parity probe"); playerRoot.SetActive(false); SceneManager.MoveGameObjectToScene(playerRoot, preview);
            playerRoot.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(97210), EveId.Parse("test:old"), 0);
            var player = playerRoot.AddComponent<PNPlayerStat>().AsPlayer(); playerRoot.AddComponent<EveEntityLoader>().Load(hub);
            check(player.TargetLayers.Mask == PNLayers.Bit(PNLayers.Enemy) && player.GetCurrent(PNContentIds.Health) == 100d, "Player stat loading unchanged");
            bad.Initialize(new PNStatSet(hub));
            check(bad.EnemyTypeId == PNContentIds.NormalEnemy, "Direct stat-set initialization defaults to registered Normal");
            bad.Unload();
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        check(SceneManager.GetActiveScene() == active && active.isDirty == dirty, "Authored scene untouched");
        return "PASS " + checks + " native enemy type checks: real Dummy JSON, Normal/Elite/Boss, legacy fallback, entity isolation, idempotent Load, unload/reload, invalid IDs and player parity; scene unchanged.";
    }
}
#endif
