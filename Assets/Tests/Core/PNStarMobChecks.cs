#if UNITY_EDITOR
using System;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// The three mobs made from the rig editor's outfits (Starchanter, Starforger, Starreaper): content + prefab + catalog, with an empty skill set. Edit Mode, preview scene, nothing saved.
public static class PNStarMobChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("Star mob check failed: " + message); }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Stopped Editor required");
        var scene = EditorSceneManager.NewPreviewScene(); var made = new System.Collections.Generic.List<GameObject>(); GameObject go = null; bool began = false; int checks = 0;
        try
        {
            var docs = new PNGameContentSource(Application.streamingAssetsPath + "/BaseContent", "unused", Array.Empty<string>()).ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
            EveRegistrySession.Begin(); began = true; EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:star-mobs"));
            PNVisualPacks.Set("project_n", Application.streamingAssetsPath + "/BaseContent");
            // the shape of an old mob: the Starweaver's components minus its skill, behavior and presentation
            var old = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starweaver/Prefab/Starweaver.prefab");
            var oldNames = old.GetComponents<Component>().Select(c => c.GetType().Name).ToArray();
            foreach (var (name, build, capsule) in new[] { ("starchanter", "normal", .40f), ("starforger", "tall", .46f), ("starreaper", "short", .34f) })
            {
                var id = EveId.Parse("project_n:" + name); var character = hub.Characters.Get(id);
                Require(character != null, name + " character exists"); checks++;
                Require(PNEntityPrefabs.TryFind("project_n:" + name, out var prefab), name + " is in the prefab catalog"); checks++;
                string cap = char.ToUpperInvariant(name[0]) + name.Substring(1);
                var expected = oldNames.Where(n => n != "PNStarweaverPresentation").Select(n => n == "PNEnemyStat" ? "PN" + cap + "Stat" : n == "PNStarweaverSkill" ? "PN" + cap + "Skill" : n == "PNStarweaverBehavior" ? "PN" + cap + "Behavior" : n).Concat(name == "starchanter" || name == "starreaper" || name == "starforger" ? new[] { "PN" + cap + "Presentation" } : new string[0]).OrderBy(n => n).ToArray();
                var actual = prefab.GetComponents<Component>().Select(c => c == null ? "MISSING" : c.GetType().Name).OrderBy(n => n).ToArray();
                Require(actual.SequenceEqual(expected), name + " has the Starweaver's components (its own Stat, Skill, Behavior; a presentation only where the mob has one): " + string.Join(",", actual)); checks++;
                go = UnityEngine.Object.Instantiate(prefab); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene);
                go.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(978200 + checks), id, 0); go.GetComponent<EveEntityLoader>().Load(hub); go.SetActive(true);
                var body = go.GetComponent<PNVisualBody>(); body.Begin(character, hub);
                Require(body.Actor != null && body.Actor.Appearance.Id.ToString() == "project_n:" + name, name + " draws its puppet"); checks++;
                Require(Mathf.Abs(body.Actor.CapsuleHeight - capsule) < 1e-3f, name + " uses the " + build + " build (capsule " + body.Actor.CapsuleHeight + ")"); checks++;
                var stat = go.GetComponent<PNEnemyStat>();
                Require(stat.HasHealth && stat.GetMax(PNContentIds.Health) > 0 && stat.GetMax(PNContentIds.Lorica) > 0, name + " has health and lorica"); checks++;
                Require(character.SkillIds.Count == 0, name + " has no skills"); checks++;
                foreach (var state in new[] { "idle", "walk", "run", "hurt", "dead", "stun", "recover" }) Require(body.Actor.Animator.HasState(state), name + " has state " + state); checks++;
                if (name == "starchanter")
                {
                    // the support Buffs are registered with art that loads (applying them needs a simulating world: the Edit Mode preview target is suspended; the behavior is covered by the smoke suite)
                    var blockDef = hub.Buffs().Get(PNSupportBuffContent.BlockHits); var speedDef = hub.Buffs().Get(PNSupportBuffContent.SpeedBoost);
                    Require(blockDef != null && speedDef != null, "block_hits and speed_boost are registered"); checks++;
                    Require(Resources.Load<Sprite>(blockDef.IconPath) != null && Resources.Load<Sprite>(speedDef.IconPath) != null, "both Buff icons load as sprites"); checks++;
                    Require(!string.IsNullOrEmpty(PNBuffGlowArt.Tint(blockDef.Glow).ToString()) && !string.IsNullOrEmpty(PNBuffGlowArt.Tint(speedDef.Glow).ToString()), "both glow colours resolve"); checks++;
                }
                made.Add(go); go = null;                      // destroyed after the session ends (the skill module cleans up with the session)
            }
            return "PNStarMobChecks: PASS (" + checks + " checks)";
        }
        finally { try { if (began) EveRegistrySession.End(); } finally { foreach (var m in made) if (m != null) UnityEngine.Object.DestroyImmediate(m); if (go != null) UnityEngine.Object.DestroyImmediate(go); EditorSceneManager.ClosePreviewScene(scene); PNVisualRuntime.Release(); PNVisualPacks.Clear(); } }
    }
}
#endif
