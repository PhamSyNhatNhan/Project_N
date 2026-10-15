#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Verify the migrated scene dummy using a disposable clone and real JSON content.
// VI: Kiem tra dummy da chuyen bang ban sao dung tam va du lieu JSON that.
public static class PNDummyEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped)
            throw new InvalidOperationException("Stop Play Mode and the session before dummy checks.");
        var scene = SceneManager.GetActiveScene();
        bool dirty = scene.isDirty;
        var original = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<DummyStat>(true)).Single();
        var visual = original.transform.Find("Visual");
        var shadow = original.transform.Find("Dummy Ground Shadow");
        if (original.GetComponent<SpriteRenderer>() != null || visual == null ||
            visual.GetComponent<SpriteRenderer>() == null || visual.GetComponent<SpriteRenderer>().sprite == null ||
            shadow == null || shadow.GetComponent<SpriteRenderer>() == null ||
            shadow.GetComponent<ErModeCharacterShadow>() == null)
            throw new Exception("Dummy visual/shadow hierarchy is incomplete.");
        var shadowSettings = new SerializedObject(shadow.GetComponent<ErModeCharacterShadow>());
        if (shadowSettings.FindProperty("target").objectReferenceValue != original.transform ||
            shadowSettings.FindProperty("ground").objectReferenceValue == null)
            throw new Exception("Dummy shadow target/ground references are missing.");
        if (original.CharacterDefinitionId != "project_n:dummy" || original.GetComponent<EveEntityIdentity>() == null ||
            original.GetComponent<EveEntityLoader>() == null || original.GetComponent<EveTimeScale>() == null ||
            original.GetComponent<CapsuleCollider>() == null || original.GetComponent<Rigidbody>() == null ||
            original.GetComponent<Stat>() != null || original.GetComponent<TimeScale>() != null ||
            original.GetComponent<StatusEffectManager>() != null || original.GetComponent<Collider2D>() != null ||
            original.GetComponent<Rigidbody2D>() != null)
            throw new Exception("Dummy component migration is incomplete.");
        var preview = EditorSceneManager.NewPreviewScene();
        bool started = false;
        try
        {
            var documents = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"),
                Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] {
                new PNContentModule(), new PNGameplayContentModule()
            }, documents);
            EveRegistrySession.Begin();
            started = true;
            EveRegistrySession.Start(hub, EveSessionRole.Offline,
                EveContentCompatibility.Create(hub, documents, "test:dummy_migration"));
            var clone = UnityEngine.Object.Instantiate(original.gameObject);
            clone.SetActive(false);
            SceneManager.MoveGameObjectToScene(clone, preview);
            var loader = clone.GetComponent<EveEntityLoader>();
            loader.Load(hub);
            var stat = clone.GetComponent<DummyStat>();
            if (!loader.IsReady || stat.GetCurrent(PNContentModule.Health) != 200000000f ||
                stat.GetMax(PNContentModule.Health) != 200000000f ||
                stat.Evaluate(PNContentModule.AttackPower) != 0f || stat.Evaluate(PNContentModule.Defense) != 0f)
                throw new Exception("Dummy JSON stats failed to load.");
            var source = new GameObject("Dummy damage probe");
            source.SetActive(false);
            SceneManager.MoveGameObjectToScene(source, preview);
            source.AddComponent<EveEntityIdentity>().Assign(EveEntityId.NewLocal(), EveId.Parse("project_n:eve"), 0);
            var sender = source.AddComponent<PNStat>();
            source.AddComponent<EveEntityLoader>().Load(hub);
            int hits = 0, deaths = 0;
            stat.DamageReceived += hit => hits++;
            stat.Died += hit => deaths++;
            var damage = sender.SendDamage(stat, 1024f, PNContentModule.Physical, PNContentModule.Fire);
            if (damage.AppliedDamage != 1024f || stat.GetCurrent(PNContentModule.Health) != 199998976f || hits != 1)
                throw new Exception("Dummy damage delivery failed.");
            stat.SetCurrent(PNContentModule.Health, 64f);
            var kill = sender.SendDamage(stat, 128f, PNContentModule.True);
            if (!kill.Killed || kill.AppliedDamage != 64f || deaths != 1 || !stat.IsDead)
                throw new Exception("Dummy death transition failed.");
            loader.Unload();
            if (stat.IsReady) throw new Exception("Dummy did not unload.");
            loader.Load(hub);
            if (!loader.IsReady || stat.GetCurrent(PNContentModule.Health) != 200000000f)
                throw new Exception("Dummy did not reload its JSON health.");
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        if (scene.isDirty != dirty) throw new Exception("Dummy checks changed the gameplay scene.");
        return "PASS migrated dummy: separate visual/shadow, native components, real JSON HP/ATK/DEF, damage1024, death, unload/reload; gameplay scene unchanged.";
    }
}
#endif
