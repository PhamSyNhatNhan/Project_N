#if UNITY_EDITOR
using System;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// The Starherald (Docs/StarheraldPlan.md S7): content + prefab + catalog + the free-rig puppet. Edit Mode, preview scene, nothing saved. What needs a simulating world (the cast, the Buffs it gives, the follow
// behavior) is covered by the opt-in Play review PNArenaPlayReview.
public static class PNStarheraldChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("Starherald check failed: " + message); }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Stopped Editor required");
        var scene = EditorSceneManager.NewPreviewScene(); GameObject go = null; bool began = false; int checks = 0;
        try
        {
            var docs = new PNGameContentSource(Application.streamingAssetsPath + "/BaseContent", "unused", Array.Empty<string>()).ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
            EveRegistrySession.Begin(); began = true; EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:starherald"));
            PNVisualPacks.Set("project_n", Application.streamingAssetsPath + "/BaseContent");
            var id = EveId.Parse("project_n:starherald"); var character = hub.Characters.Get(id);
            Require(character != null, "the character exists"); checks++;
            Require(PNEntityPrefabs.TryFind("project_n:starherald", out var prefab), "it is in the prefab catalog"); checks++;
            var expected = new[] { "CapsuleCollider", "EveEntityIdentity", "EveEntityLoader", "EveTimeScale", "PNAbilityManager", "PNBuffManager", "PNEnemyMove", "PNHeraldBehavior", "PNHeraldPresentation", "PNHeraldSkill", "PNHeraldStat", "PNVisualBody", "Rigidbody", "Transform" };
            var actual = prefab.GetComponents<Component>().Select(c => c == null ? "MISSING" : c.GetType().Name).OrderBy(n => n).ToArray();
            Require(actual.SequenceEqual(expected), "the prefab holds its own Stat / Skill / Behavior / Presentation on the shared enemy shape: " + string.Join(",", actual)); checks++;
            Require(prefab.layer == 7, "the prefab is on the enemy layer"); checks++;
            go = UnityEngine.Object.Instantiate(prefab); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene);
            go.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(978900), id, 0); go.GetComponent<EveEntityLoader>().Load(hub); go.SetActive(true);
            var body = go.GetComponent<PNVisualBody>(); body.Begin(character, hub);
            var actor = body.Actor;
            Require(actor != null && actor.Appearance.Id.ToString() == "project_n:starherald", "it draws the starherald puppet"); checks++;
            Require(Mathf.Abs(actor.CapsuleHeight - .40f) < 1e-3f, "the normal build (capsule " + actor.CapsuleHeight + ")"); checks++;
            var stat = go.GetComponent<PNEnemyStat>();
            Require(stat is PNHeraldStat && stat.EnemyTypeId == PNContentIds.NormalEnemy && stat.CanBeSidus, "a normal enemy, so it can itself be Sidus"); checks++;
            Require(Math.Abs(stat.GetMax(PNContentIds.Health) - 1200) < 1e-6 && stat.GetMax(PNContentIds.Lorica) > 0, "health 1200 and a Lorica"); checks++;
            Require(stat.Set.TryGetBase(hub.Stats.ResolveIdentity(EveId.Parse("base:move_speed")), out var speed) && Math.Abs(speed.Value - 1.2) < 1e-6, "move speed 1.2"); checks++;
            foreach (var state in new[] { "idle", "walk", "run", "hurt", "dead", "stun", "recover", "cast" }) { Require(actor.Animator.HasState(state), "it has the state " + state); checks++; }
            Require(actor.HasStrings, "its three puppet strings are drawn"); checks++;
            Require(go.GetComponent<PNHeraldPresentation>() != null && !go.GetComponent<PNHeraldPresentation>().HasEffects, "the effect creates nothing until a cast"); checks++;
            Require(hub.Skills.Get(EveId.Parse("project_n:starherald_sidus")) != null && go.GetComponent<PNHeraldBehavior>().SkillId == EveId.Parse("project_n:starherald_sidus"), "the Sidus skill is defined and the prefab behavior points at it"); checks++;
            return "PNStarheraldChecks: PASS (" + checks + " checks)";
        }
        finally { try { if (began) EveRegistrySession.End(); } finally { if (go != null) UnityEngine.Object.DestroyImmediate(go); EditorSceneManager.ClosePreviewScene(scene); PNVisualRuntime.Release(); PNVisualPacks.Clear(); } }
    }
}
#endif
