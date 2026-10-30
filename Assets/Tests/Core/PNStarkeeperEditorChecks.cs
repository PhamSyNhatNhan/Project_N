#if UNITY_EDITOR
using System;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Native checks of the Starkeeper (all BaseContent documents, a started session, preview scenes, Edit Mode): Run = content, stats, prefab; RunCombat = the two skills' Actions
// (timeline, hits, re-aim, cancel, cleanup, the star point's windup/clamp). The lunge's real physics (stopping at a wall, hitting along the path) is NOT covered here: that
// needs a Player with prepared navigation. No AI combat, no Play Mode.
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
            foreach (var id in new[] { PNStarkeeperCombatContent.StarSweep, PNStarkeeperCombatContent.StarPoint })
            {
                var data = (Newtonsoft.Json.Linq.JToken)hub.Get<EveSkillDefinition>().Get(id).Data;
                Check((string)data["skill"]["abilities"][0]["id"] == id + "_ready", id + " has its own cooldown Ability");
            }
            var sweep = (Newtonsoft.Json.Linq.JToken)hub.Get<EveSkillDefinition>().Get(PNStarkeeperCombatContent.StarSweep).Data;
            var point = (Newtonsoft.Json.Linq.JToken)hub.Get<EveSkillDefinition>().Get(PNStarkeeperCombatContent.StarPoint).Data;
            Check((double)sweep["skill"]["abilities"][0]["cooldown"] == 3 && (double)point["skill"]["abilities"][0]["cooldown"] == 7, "cooldowns 3 s and 7 s are registered per skill");
            Check((double)point["action"]["perform"]["windup"] == 2 && (double)point["action"]["perform"]["maxRange"] == 3, "the star point: 2 s windup, 3 m range");
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
            Check(stat.Evaluate(PNContentIds.AttackPower) == 120 && stat.Evaluate(PNContentIds.Defense) == 15 && Math.Abs(stat.Evaluate(EveId.Parse("base:move_speed")) - 1.4) < 1e-6, "attack 120, defense 15, move speed 1.4");
            var prefab = Resources.Load<PNEntityPrefabCatalog>(PNEntityPrefabCatalog.ResourcePath).Find("starkeeper");
            Check(prefab != null && prefab.GetComponent<PNStarkeeperBehavior>() != null && prefab.GetComponent<PNStarkeeperSkill>() != null && prefab.GetComponent<PNStarkeeperStat>() != null && prefab.GetComponent<PNVisualBody>() != null && prefab.GetComponent<PNStarkeeperPresentation>() != null, "the catalog key resolves to the Starkeeper prefab with its spear presentation");
            Check(prefab.GetComponent<PNStarweaverSkill>() == null && prefab.GetComponent<PNStarweaverBehavior>() == null && prefab.transform.Find("Golden needle") == null && prefab.transform.Find("Star spear") != null, "no Starweaver parts, the spear is there");
            return "PASS " + n + " Starkeeper native content/stat/prefab checks; actual AI combat not covered";
        }
        finally { try { if (started) EveRegistrySession.End(); } finally { EditorSceneManager.ClosePreviewScene(scene); } }
    }

    // The two Actions on a real Starkeeper prefab instance, with two dummy targets in front of it.
    public static string RunCombat()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Editor required");
        var scene = EditorSceneManager.NewPreviewScene(); bool started = false; int n = 0; GameObject go = null, a = null, b = null;
        void Check(bool ok, string what) { if (!ok) throw new Exception("Starkeeper combat check " + n + ": " + what); n++; }
        try
        {
            var docs = new PNGameContentSource(Application.streamingAssetsPath + "/BaseContent", "unused", Array.Empty<string>()).ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
            EveRegistrySession.Begin(); started = true; EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:starkeeper-combat"));
            PNVisualPacks.Set("project_n", Application.streamingAssetsPath + "/BaseContent");
            go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starkeeper/Prefab/Starkeeper.prefab")); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene);
            go.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(978210), EveId.Parse("project_n:starkeeper"), 0); go.GetComponent<EveEntityLoader>().Load(hub); go.SetActive(true);
            go.GetComponent<PNVisualBody>().Begin(hub.Characters.Get(EveId.Parse("project_n:starkeeper")), hub);
            PNStat Dummy(string name, int id, Vector3 where)
            {
                var d = new GameObject(name); d.SetActive(false); SceneManager.MoveGameObjectToScene(d, scene); d.transform.position = where;
                d.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:eve"), 0);
                var s = d.AddComponent<PNPlayerStat>().AsPlayer(); d.AddComponent<Rigidbody>().isKinematic = true;
                var c = d.AddComponent<CapsuleCollider>(); c.radius = .2f; c.height = 1.4f; c.center = Vector3.up * .7f; d.layer = PNLayers.Player;
                d.AddComponent<EveEntityLoader>().Load(hub); d.SetActive(true); return s;
            }
            var targetA = Dummy("Starkeeper dummy A", 978211, new Vector3(1f, 0, 0)); a = targetA.gameObject;
            var targetB = Dummy("Starkeeper dummy B", 978212, new Vector3(2.6f, 0, 0)); b = targetB.gameObject;
            var skill = go.GetComponent<PNStarkeeperSkill>(); var presentation = go.GetComponent<PNStarkeeperPresentation>();
            Check(targetA.IsReady && targetB.IsReady && skill.IsReady, "the dummies and the Starkeeper are ready");
            float Health(PNStat s) => (float)s.GetCurrent(PNContentIds.Health);
            float h0 = Health(targetA), b0 = Health(targetB);
            string sweepDamage = ""; var caster = go.GetComponent<PNStat>();
            int Markers() => scene.GetRootGameObjects().Count(o => o.name.StartsWith("Starkeeper sweep") || o.name.StartsWith("Starkeeper star point"));
            // --- Quet sao ---
            var sweep = skill.CreateAction(PNStarkeeperCombatContent.StarSweep, out var sc, new[] { targetA }, Vector3.right, null);
            try
            {
                if (sweep is IPNSkillMotionAction motion) motion.BindMovementOwner(() => { });
                var origin = go.transform.position;
                sweep.Start(sc); Check(Markers() == 1, "the sweep sector marker exists during the windup");
                sweep.AdvanceAuxiliary(.39f); Check(Health(targetA) == h0, "no hit before the registered windup (0.4 s)");
                float step1 = Vector3.Distance(origin, go.transform.position); Check(step1 > .3f && step1 <= .5f + 1e-3f && go.transform.position.x > origin.x, "the first sweep carries the Starkeeper toward the target by at most 0.5 m (" + step1.ToString("F2") + " m)");
                sweep.AdvanceAuxiliary(.02f); float afterFirst = Health(targetA); Check(afterFirst < h0, "the first sweep hits the target in front");
                Check(Health(targetB) == b0, "the target 2.6 m away is outside the 1.4 m sector");
                targetA.transform.position = new Vector3(.9f, 0, .75f);                        // dodged to the side: ~40 degrees off the first sweep, inside the 70 degree re-aim
                var beforeSecond = go.transform.position;
                sweep.AdvanceAuxiliary(.2f); sweep.AdvanceAuxiliary(.14f);                      // up to the second sweep at 0.4 + 0.35 s
                float step2 = Vector3.Distance(beforeSecond, go.transform.position); Check(step2 > .02f && step2 <= .5f + 1e-3f, "the second sweep also steps toward the target, at most 0.5 m (" + step2.ToString("F2") + " m)");
                float afterSecond = Health(targetA); Check(afterSecond < afterFirst, "the second sweep re-aims and catches the target that moved"); sweepDamage = "sweep damage " + (h0 - afterFirst).ToString("F1") + " + " + (afterFirst - afterSecond).ToString("F1") + " (attack " + caster.Evaluate(PNContentIds.AttackPower) + ")";
                Check(Health(targetB) == b0, "the far target is still untouched");
                sweep.AdvanceAuxiliary(.7f); sweep.Exit(sc); Check(Markers() == 0, "the marker is gone after the cast");
            }
            finally { sweep.Dispose(); }
            targetA.transform.position = new Vector3(1f, 0, 0); float h1 = Health(targetA);
            var cancelled = skill.CreateAction(PNStarkeeperCombatContent.StarSweep, out var cc, new[] { targetA }, Vector3.right, null);
            try { cancelled.Start(cc); cancelled.AdvanceAuxiliary(.3f); cancelled.Cancel(cc); } finally { cancelled.Dispose(); }
            Check(Health(targetA) == h1 && Markers() == 0, "a sweep cancelled in the windup hurts nobody and leaves no marker");
            // --- Diem sao ---
            var anchor = new Vector3(3f, 0, 0); var start = go.transform.position;
            var star = skill.CreateAction(PNStarkeeperCombatContent.StarPoint, out var pc, new[] { targetB }, Vector3.right, anchor);
            try
            {
                if (star is PNAgentTrajectoryAction trajectory) trajectory.BindMovementOwner(() => { });
                star.Start(pc); Check(Markers() == 1, "the star point marker exists during the windup");
                star.AdvanceAuxiliary(1.99f); Check((go.transform.position - start).sqrMagnitude < 1e-6f && Health(targetB) == b0, "the Starkeeper stands still and hurts nobody for the whole 2 s windup");
                star.Cancel(pc); Check(Markers() == 0, "cancelling in the windup removes the marker");
            }
            finally { star.Dispose(); }
            Check(Health(targetB) == b0, "a star point cancelled before the lunge hurts nobody");
            var data = skill.GetData(PNStarkeeperCombatContent.StarPoint); Check((float)data.Action.Perform["maxRange"] == 3f, "the behavior reads the 3 m range from the registered data");
            return "PASS " + n + " Starkeeper combat checks (" + sweepDamage + "; sweep timeline/re-aim/cancel/cleanup, star point windup/cancel/marker); the lunge's wall stop and path hits are not covered";
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally { foreach (var o in new[] { go, a, b }) if (o != null) UnityEngine.Object.DestroyImmediate(o); EditorSceneManager.ClosePreviewScene(scene); PNVisualRuntime.Release(); PNVisualPacks.Clear(); }
        }
    }
}
#endif
