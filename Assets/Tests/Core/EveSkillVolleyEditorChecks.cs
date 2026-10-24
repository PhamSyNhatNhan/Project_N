#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Eve's Skill with the real EveCharacter.json: a Dash-like glide along the movement direction (no target rush) that fires three homing
//     projectiles at the start of the glide, aimed at the nearest target (+-30 degrees, the middle one tilted 30 degrees up), with no gravity.
// VI: Skill của Eve với EveCharacter.json thật: lướt như Dash theo hướng di chuyển, bắn 3 đạn homing khi bắt đầu lướt về phía mục tiêu
//     gần nhất (±30 độ, viên giữa nghiêng lên 30 độ), không gravity.
public static class EveSkillVolleyEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var preview = EditorSceneManager.NewPreviewScene(); int checks = 0; string note = "";
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        bool Near(Vector3 a, Vector3 b, float tolerance = .03f) => (a - b).magnitude <= tolerance;
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        long id = 987000;
        T Entity<T>(string name, Vector3 position, string character, int layer = 0) where T : PNStat
        {
            var root = new GameObject(name); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview);
            root.transform.position = position; root.layer = layer;
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(++id), EveId.Parse(character), 1);
            var stat = root.AddComponent<T>();
            if (character == "project_n:eve")
            {
                root.AddComponent<Rigidbody>().useGravity = false;
                var capsule = root.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f; capsule.center = Vector3.up * .3f;
                root.AddComponent<PNPlayerMove>(); root.AddComponent<PNEveAttackSpeedProbe>();
            }
            else { var sphere = root.AddComponent<SphereCollider>(); sphere.radius = .3f; }
            root.AddComponent<EveEntityLoader>().Load(hub); root.SetActive(true); return stat;
        }
        EveRegistrySession.Begin();
        EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:eve_skill_volley"));
        var fixtures = new GameObject("Eve skill volley fixtures"); SceneManager.MoveGameObjectToScene(fixtures, preview);
        var areaManager = fixtures.AddComponent<PNAreaManager>(); areaManager.Initialize(EveRegistrySession.Context);
        var projectiles = fixtures.AddComponent<PNProjectileManager>(); projectiles.Initialize(EveRegistrySession.Context, new PNProjectileProfile { MaxActive = 20 }); projectiles.ManualAdvance = true;
        try
        {
            var caster = Entity<EveCharacterStat>("Eve skill probe", Vector3.zero, "project_n:eve");
            var skill = caster.GetComponent<PNEveAttackSpeedProbe>(); var move = caster.GetComponent<PNPlayerMove>(); var body = caster.GetComponent<Rigidbody>();
            caster.SetSource(EveId.Parse("test:crit_off"), new[] { new EveStatModifier(PNContentIds.CritRate, -20d, 0) });
            var target = Entity<DummyStat>("Skill target", new Vector3(0, .4f, 5), "project_n:dummy", 7);
            Physics.SyncTransforms();
            void Step(float seconds) { for (float t = 0; t < seconds - 1e-4f; t += 1f / 60f) { skill.TickAttack(1f / 60f); projectiles.Advance(1f / 60f); } }
            double Hp(PNStat stat) => stat.GetCurrent(PNContentIds.Health);

            // ---- with a target: the glide follows the movement direction (left), the volley aims at the target (+z) ----
            move.SetMoveDirection(Vector2.left); skill.SkillRecovery.ResetToFull(); double hpBefore = Hp(target);
            Check(skill.TrySkill(), "Skill activates");
            var launched = projectiles.Snapshot().ToArray();
            Check(launched.Length == 3, "the Skill fires three projectiles when the glide starts: " + launched.Length);
            Check(launched.All(x => x.DefinitionId == PNProjectileGameplayContent.EveSkillHoming), "the projectiles are Eve's registered homing definition");
            var middle = launched.OrderByDescending(x => x.Forward.y).First(); var sides = launched.Where(x => x != middle).OrderBy(x => x.Forward.x).ToArray();
            float sin30 = Mathf.Sin(30 * Mathf.Deg2Rad), cos30 = Mathf.Cos(30 * Mathf.Deg2Rad);
            Check(Near(middle.Forward, new Vector3(0, sin30, cos30)), "the middle projectile is tilted 30 degrees up toward the target: " + middle.Forward);
            Check(Near(sides[0].Forward, new Vector3(-sin30, 0, cos30)) && Near(sides[1].Forward, new Vector3(sin30, 0, cos30)), "the side projectiles are +-30 degrees around the target direction, level: " + sides[0].Forward + " " + sides[1].Forward);
            Check(launched.All(x => Near(x.Position, caster.transform.position, .01f)), "the volley starts at the caster");
            Check(skill.BurstGauge.Current == 5 && skill.Empowerment.IsActive, "Skill rewards are unchanged (five Burst points, empowerment)");
            Step(.2f);
            Check(Mathf.Abs(body.position.x + 2f) < .06f && Mathf.Abs(body.position.z) < .06f, "the glide runs along the movement direction for the Dash distance 2, not toward the target: " + body.position);
            Check(areaManager.ActiveCount == 0, "the Skill creates no TimeScale area (that belongs to Dash)");
            Check(projectiles.Snapshot().Where(x => x.IsRunning).All(x => Mathf.Abs(x.Position.y - 0f) < 3f) && projectiles.Snapshot().Max(x => x.Position.y) > .5f, "the middle projectile climbed while the glide ran");
            Step(2f);
            double loss = hpBefore - Hp(target);
            Check(loss > 0, "the projectiles hit the target: loss " + loss);
            Check(projectiles.ActiveCount == 0, "every projectile ended on its hit");
            note = "volley loss " + loss + " (ATK 10, 150% each, 3 hits)";

            // ---- without a target: the base direction is the movement direction ----
            Step(1.5f); target.gameObject.SetActive(false); skill.SkillRecovery.ResetToFull(); caster.transform.position = Vector3.zero; body.position = Vector3.zero; Physics.SyncTransforms();
            move.SetMoveDirection(Vector2.left); Check(skill.TrySkill(), "Skill activates without a target");
            var bare = projectiles.Snapshot().ToArray(); Check(bare.Length == 3, "three projectiles without a target");
            middle = bare.OrderByDescending(x => x.Forward.y).First();
            Check(Near(middle.Forward, new Vector3(-cos30, sin30, 0)), "without a target the middle projectile tilts up along the glide direction: " + middle.Forward);
            Step(3.3f); Check(projectiles.ActiveCount == 3, "with no target the projectiles keep flying until their life ends");
            Step(.35f); Check(projectiles.ActiveCount == 0, "the projectiles end after their 3.5 s life");
            Check(!projectiles.Snapshot().Any(x => x.IsRunning), "nothing is left running");
        }
        finally
        {
            try { projectiles.Shutdown(); areaManager.Shutdown(); EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        // Dash's TimeScale area includes the Projectile layer next to Enemy (layers 8 and 7), so it slows or stops running projectiles too.
        var dash = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "BaseContent/characters/EveCharacter.json")))["characters"][0]["skill"].First(x => (string)x["id"] == "project_n:eve_dash");
        var areaLayers = PNTargetLayers.Parse(dash["data"]["effects"][0]["data"]["input"]["targetLayers"], "dash area"); int areaMask = areaLayers.Mask;
        Check(areaMask == ((1 << LayerMask.NameToLayer("Enemy")) | (1 << LayerMask.NameToLayer("Projectile"))) && (string)dash["data"]["effects"][0]["data"]["input"]["layerMode"] == "Include", "Eve's Dash TimeScale area includes the Enemy and Projectile layers: " + areaMask);
        Check(original.isDirty == dirty, "the test left the active scene dirty state alone");
        return "PASS " + checks + " native Eve Skill volley checks; real Eve JSON, " + note;
    }
}
#endif
