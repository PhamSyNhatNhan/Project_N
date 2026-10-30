#if UNITY_EDITOR
using System;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Native content checks of the Starkeeper (all BaseContent documents, a started session): the slash is registered, the stats load on a PNStarkeeperStat, the character names its
// visual and prefab, the prefab resolves from the catalog and the puppet appearance exists. No AI combat, no Play Mode: see the visual frames (PNVisualEditorChecks.RunStarkeeper).
public static class PNStarkeeperEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Editor required");
        var scene = EditorSceneManager.NewPreviewScene(); bool started = false; int n = 0;
        void Check(bool ok, string what) { if (!ok) throw new Exception("Starkeeper check " + n + ": " + what); n++; }
        try
        {
            var docs = new PNGameContentSource(Application.streamingAssetsPath + "/BaseContent", "unused", Array.Empty<string>()).ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
            EveRegistrySession.Begin(); started = true; EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:starkeeper"));
            var slash = (Newtonsoft.Json.Linq.JToken)hub.Get<EveSkillDefinition>().Get(PNStarkeeperCombatContent.Slash).Data;
            Check((double)slash["action"]["perform"]["value"]["amount"] == 120 && (double)slash["skill"]["abilities"][0]["cooldown"] == 1.5, "the slash is registered with its data");
            Check(hub.Get<PNVisualAppearanceDefinition>().Get(EveId.Parse("project_n:starkeeper")).Slots.ContainsKey("skHead"), "the Starkeeper appearance exists with its head piece");
            var go = new GameObject("Starkeeper stat probe"); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(978200), EveId.Parse("project_n:starkeeper"), 0);
            var stat = go.AddComponent<PNStarkeeperStat>();
            go.AddComponent<Rigidbody>();
            var capsule = go.AddComponent<CapsuleCollider>(); capsule.radius = .2f; capsule.height = 1.4f; capsule.center = Vector3.up * .7f;
            var movement = go.AddComponent<PNEnemyMove>();
            go.AddComponent<EveEntityLoader>().Load(hub);
            Check(movement.IsReady && stat.IsReady, "the stat and movement load for the character");
            Check(stat.GetCurrent(PNContentIds.Health) == 2500 && stat.GetCurrent(PNContentIds.Lorica) == 500, "health 2500, Lorica 500");
            Check(stat.Evaluate(PNContentIds.AttackPower) == 120 && stat.Evaluate(PNContentIds.Defense) == 15 && Math.Abs(stat.Evaluate(EveId.Parse("base:move_speed")) - 1.4) < 1e-6, "attack 120, defense 15, move speed 1.4 (got " + stat.Evaluate(PNContentIds.AttackPower) + ", " + stat.Evaluate(PNContentIds.Defense) + ", " + stat.Evaluate(EveId.Parse("base:move_speed")) + ")");
            var prefab = Resources.Load<PNEntityPrefabCatalog>(PNEntityPrefabCatalog.ResourcePath).Find("starkeeper");
            Check(prefab != null && prefab.GetComponent<PNStarkeeperBehavior>() != null && prefab.GetComponent<PNStarkeeperSkill>() != null && prefab.GetComponent<PNStarkeeperStat>() != null && prefab.GetComponent<PNVisualBody>() != null, "the catalog key resolves to the Starkeeper prefab");
            Check(prefab.GetComponent<PNStarweaverSkill>() == null && prefab.GetComponent<PNStarweaverBehavior>() == null && prefab.transform.Find("Golden needle") == null, "no Starweaver parts left on the prefab");
            return "PASS " + n + " Starkeeper native content/stat/prefab checks; actual AI combat not covered";
        }
        finally { try { if (started) EveRegistrySession.End(); } finally { EditorSceneManager.ClosePreviewScene(scene); } }
    }
}
#endif
