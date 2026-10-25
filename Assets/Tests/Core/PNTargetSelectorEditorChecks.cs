#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Real entities, colliders and PhysX in a disposable preview scene: the shared candidate index serves queries with no projectile in
//     flight and refreshes when the entity list changes; PNTargetSelector filters (layer, range, excluded chooser, dead) and ranks
//     (modes, aggro tier, Aggro rule), keeps a held target with the 20% rule, honours line of sight; PNAutoTarget keeps one held target per
//     character and leaves it alone for a skill's own rule; PNTargetRules gives the local player its Settings mode and enemies Aggro.
// VI: Entity, collider, PhysX thật trong preview scene: chỉ mục ứng viên phục vụ truy vấn khi không có đạn bay và làm mới khi danh sách
//     entity đổi; bộ chọn lọc và xếp hạng, giữ mục tiêu theo luật 20%, kiểm tầm nhìn; PNAutoTarget và PNTargetRules.
public static class PNTargetSelectorEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        int count = 0; var evidence = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS " + message); count++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNProjectileHitNativeModule() }, docs);
        var preview = EditorSceneManager.NewPreviewScene(); var fixtures = new List<GameObject>(); PNProjectileManager manager = null;
        var previousMode = PNTargetRules.FixedLocalMode;
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:target_selector"));
            var session = EveRegistrySession.Context; long nextId = 993000;
            PNTargetRules.FixedLocalMode = PNTargetMode.Nearest;
            GameObject Fixture(string name) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, preview); fixtures.Add(obj); return obj; }
            manager = Fixture("Projectile manager").AddComponent<PNProjectileManager>(); manager.Initialize(session, new PNProjectileProfile { MaxActive = 10 }); manager.ManualAdvance = true;
            PNStat Enemy(Vector3 position)
            {
                var obj = Fixture("enemy " + nextId); obj.SetActive(false); obj.transform.position = position; obj.layer = PNLayers.Enemy;
                obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(nextId++), EveId.Parse("project_n:dummy"), 0);
                var stat = obj.AddComponent<PNEnemyStat>(); obj.AddComponent<BoxCollider>().size = Vector3.one;
                obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); obj.GetComponent<PNBuffManager>().Advance(0); return stat;
            }
            PNStat Player(Vector3 position)
            {
                var obj = Fixture("player " + nextId); obj.SetActive(false); obj.transform.position = position; obj.layer = PNLayers.Player;
                obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(nextId++), EveId.Parse("project_n:eve"), 0);
                var stat = obj.AddComponent<EveCharacterStat>(); obj.AddComponent<PNAbilityManager>(); obj.AddComponent<PNProjectileSkillProbe>(); obj.AddComponent<BoxCollider>().size = Vector3.one;
                obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); obj.GetComponent<PNBuffManager>().Advance(0); return stat;
            }
            PNStat Pick(PNStat chooser, float range, int mask, PNTargetRule rule = default, PNStat held = null, bool sight = false, bool exclude = true)
                => PNTargetSelector.Select(manager.Targets.Acquire(null), new PNTargetQuery
                { Origin = chooser.transform.position, Range = range, LayerMask = mask, Rule = rule, Held = held, Exclude = exclude ? chooser.EntityId : default, Planar = true, RequireSight = sight, Chooser = chooser });
            int enemy = PNLayers.Bit(PNLayers.Enemy), player = PNLayers.Bit(PNLayers.Player);
            var nearest = PNTargetRule.Autotarget(PNTargetMode.Nearest); var lowest = PNTargetRule.Autotarget(PNTargetMode.LowestHealth); var percent = PNTargetRule.Autotarget(PNTargetMode.LowestHealthPercent);

            // Index and filters, with no projectile in flight.
            var p = Player(new Vector3(0, 0, 0)); var ally = Player(new Vector3(1.5f, 0, 0));
            var e1 = Enemy(new Vector3(3, 0, 0)); var e2 = Enemy(new Vector3(5, 0, 0)); var far = Enemy(new Vector3(30, 0, 0)); var dead = Enemy(new Vector3(2, 0, 0));
            dead.SetCurrent(PNContentIds.Health, 0); Physics.SyncTransforms();
            Check(manager.ActiveCount == 0 && ReferenceEquals(Pick(p, 10, enemy), e1), "with no projectile in flight the shared index still finds the nearest live enemy (the dead nearer one is skipped)");
            Check(Pick(p, 2.4f, enemy) == null && ReferenceEquals(Pick(p, 3f, enemy), e1), "the range is inclusive and limits the candidates");
            Check(ReferenceEquals(Pick(p, 40, enemy, lowest), e1) || ReferenceEquals(Pick(p, 40, enemy, lowest), e2) || ReferenceEquals(Pick(p, 40, enemy, lowest), far), "LowestHealth picks among live enemies only");
            Check(ReferenceEquals(Pick(p, 10, player), ally) && ReferenceEquals(Pick(p, 10, player, exclude: false), p), "the chooser is excluded by ID; without Exclude it is a candidate like any other");
            Check(Pick(p, 10, 0) == null && Pick(p, 10, PNLayers.Bit(PNLayers.Wall)) == null, "an empty mask or a layer without entities finds nothing");

            // Modes over real Health resources.
            e2.SetCurrent(PNContentIds.Health, e1.GetCurrent(PNContentIds.Health) / 2);
            Check(ReferenceEquals(Pick(p, 10, enemy, lowest), e2) && ReferenceEquals(Pick(p, 10, enemy, percent), e2) && ReferenceEquals(Pick(p, 10, enemy, nearest), e1),
                "LowestHealth / LowestHealthPercent pick the damaged farther enemy, Nearest the nearer one");
            PNTargetSelector.HealthOf(e2, out var health2, out var max2);
            Check(health2 == e2.GetCurrent(PNContentIds.Health) && max2 == e2.GetMax(PNContentIds.Health) && max2 > 0, "the selector reads the Health resource and its maximum");

            // Held target and the 20% rule.
            Check(ReferenceEquals(Pick(p, 10, enemy, nearest, e2), e1), "a held target 5 m away gives way to one at 3 m (40% nearer)");
            e1.transform.position = new Vector3(4.5f, 0, 0); Physics.SyncTransforms();
            Check(ReferenceEquals(Pick(p, 10, enemy, nearest, e2), e2) && ReferenceEquals(Pick(p, 10, enemy, nearest), e1), "a held target is kept against one only 10% nearer; without a held target the nearer wins");
            Check(ReferenceEquals(Pick(p, 10, enemy, lowest, e1), e1), "the health modes keep a valid held target");
            Check(ReferenceEquals(Pick(p, 4.6f, enemy, nearest, e2), e1), "a held target out of range is dropped");
            e1.transform.position = new Vector3(3, 0, 0); Physics.SyncTransforms();

            // Aggro tier (Autotarget) and the Aggro rule (enemy side), through ordinary stat modifiers.
            var mark = EveId.Parse("test:mark");
            e2.SetSource(mark, new[] { new EveStatModifier(PNContentIds.Aggro, 5d, 0) });
            Check(PNTargetSelector.AggroOf(e2) == 5d && ReferenceEquals(Pick(p, 10, enemy, nearest), e2) && ReferenceEquals(Pick(p, 10, enemy, nearest, e1), e2), "a marked enemy (aggro +5) ranks first and takes over a held target at once");
            e2.SetSource(mark, new[] { new EveStatModifier(PNContentIds.Aggro, -5d, 0) });
            e1.SetCurrent(PNContentIds.Health, 1);
            Check(PNTargetSelector.AggroOf(e2) == -5d && ReferenceEquals(Pick(p, 10, enemy, nearest), e1), "negative aggro ranks the enemy after the others");
            e2.RemoveSource(mark);
            Check(ReferenceEquals(Pick(e1, 10, player, PNTargetRule.Aggro), ally), "Aggro rule: an enemy picks the nearer player (1.5 m from it vs 3 m)");
            p.SetSource(mark, new[] { new EveStatModifier(PNContentIds.Aggro, 10d, 0) });
            Check(ReferenceEquals(Pick(e1, 10, player, PNTargetRule.Aggro), p), "Aggro rule: a taunting player (aggro +10) is picked although farther");
            p.SetSource(mark, new[] { new EveStatModifier(PNContentIds.Aggro, 1000d, 0) });
            Check(ReferenceEquals(Pick(e1, 10, player, PNTargetRule.Aggro, ally), p), "Aggro rule: a hard taunt takes over a held target");
            p.RemoveSource(mark);

            // The index refreshes when the entity list changes (a new entity is visible to the next query at once).
            var fresh = Enemy(new Vector3(1, 0, 0)); Physics.SyncTransforms();
            Check(ReferenceEquals(Pick(p, 10, enemy, nearest), fresh), "an entity loaded just now is a candidate of the next query (entity list revision)");
            fresh.GetComponent<EveEntityLoader>().Unload();
            Check(!ReferenceEquals(Pick(p, 10, enemy, nearest), fresh), "an unloaded entity is gone from the next query");

            // Line of sight (skill option): a wall hides the nearer enemy.
            var q = Player(new Vector3(0, 0, 50)); var hidden = Enemy(new Vector3(3, 0, 50)); var open = Enemy(new Vector3(0, 0, 55));
            var wall = Fixture("wall"); wall.layer = PNLayers.Wall; wall.transform.position = new Vector3(1.5f, .5f, 50); wall.AddComponent<BoxCollider>().size = new Vector3(.2f, 3, 3);
            Physics.SyncTransforms();
            Check(ReferenceEquals(Pick(q, 10, enemy), hidden) && ReferenceEquals(Pick(q, 10, enemy, sight: true), open), "without the option the hidden nearer enemy is picked; with RequireSight the visible one");
            Check(ReferenceEquals(Pick(q, 10, enemy, nearest, hidden, true), open), "with RequireSight a held target behind a wall is dropped");
            UnityEngine.Object.DestroyImmediate(wall); Physics.SyncTransforms();
            Check(ReferenceEquals(Pick(q, 10, enemy, sight: true), hidden), "once the wall is gone the nearer enemy is in sight again");
            bool needsChooser = false; try { PNTargetSelector.Select(manager.Targets.Acquire(null), new PNTargetQuery { Range = 1, LayerMask = enemy, RequireSight = true }); } catch (ArgumentException) { needsChooser = true; }
            Check(needsChooser, "a line-of-sight query without its chooser is rejected");

            // PNAutoTarget: one held target per character; a skill's own rule leaves it alone.
            var auto = PNAutoTarget.For(p);
            Check(ReferenceEquals(PNAutoTarget.For(p), auto), "PNAutoTarget.For returns the character's single component");
            var first = auto.Resolve(10, enemy);
            Check(ReferenceEquals(first, e1) && ReferenceEquals(auto.Held, e1), "Resolve with the default rule picks and holds the nearest enemy");
            e2.transform.position = new Vector3(2.8f, 0, 0); Physics.SyncTransforms();
            Check(ReferenceEquals(auto.Resolve(10, enemy), e1), "the held target is kept while another is less than 20% nearer");
            var own = auto.Resolve(10, enemy, PNTargetRule.Autotarget(PNTargetMode.LowestHealth));
            Check(ReferenceEquals(own, e1) || ReferenceEquals(own, e2), "a skill's own rule returns its pick");
            e1.SetCurrent(PNContentIds.Health, e1.GetMax(PNContentIds.Health));
            Check(ReferenceEquals(auto.Resolve(10, enemy, PNTargetRule.Autotarget(PNTargetMode.LowestHealth)), e2) && ReferenceEquals(auto.Held, e1), "a skill's own rule does not change the held target");
            e1.SetCurrent(PNContentIds.Health, 0);
            Check(auto.Held == null && ReferenceEquals(auto.Resolve(10, enemy), e2), "a held target that dies is dropped and replaced");
            auto.Clear();
            Check(auto.Held == null && ReferenceEquals(auto.Resolve(10, enemy), e2) && ReferenceEquals(auto.Held, e2), "Clear drops the held target; the next Resolve picks and holds again");
            auto.Clear(); // (OnDisable also clears it; Edit Mode does not call OnDisable on ordinary components, so that path is not checked here.)

            // PNTargetRules: the local player follows Settings (pinned here), enemies use Aggro.
            PNTargetRules.FixedLocalMode = PNTargetMode.LowestHealthPercent;
            Check(PNTargetRules.For(p).Equals(percent) && PNTargetRules.IsLocal(p), "offline, a player's rule is the local auto target mode");
            Check(PNTargetRules.For(e2).Equals(PNTargetRule.Aggro), "an enemy chooser uses the Aggro rule");
            e2.SetCurrent(PNContentIds.Health, e2.GetMax(PNContentIds.Health)); var weak = Enemy(new Vector3(-6, 0, 0)); weak.SetCurrent(PNContentIds.Health, 1); Physics.SyncTransforms();
            auto.Clear();
            Check(ReferenceEquals(auto.Resolve(10, enemy), weak), "with the local mode LowestHealthPercent, Resolve picks the weakest enemy in range");
        }
        finally
        {
            PNTargetRules.FixedLocalMode = previousMode;
            try { if (manager != null) manager.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var obj in fixtures) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(preview); }
        }
        Check(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "Authored scene and dirty state preserved.");
        string summary = "PASS " + count + " native target selector checks";
        Directory.CreateDirectory(".utmp/AutotargetReview"); File.WriteAllLines(".utmp/AutotargetReview/NativeResults.txt", evidence.Concat(new[] { summary })); return summary;
    }
}

// EN: Batch entry for the clone project: the autotarget checks plus every native check the change can affect.
// VI: Điểm vào batch cho project clone: kiểm autotarget cùng mọi kiểm native mà thay đổi có thể ảnh hưởng.
public static class PNAutoTargetBatch
{
    public static void Run()
    {
        var lines = new List<string>(); bool failed = false;
        void Step(string name, Func<string> run)
        {
            try { lines.Add(name + ": " + run()); }
            catch (Exception error) { failed = true; lines.Add(name + ": FAIL " + error.Message); Debug.LogException(error); }
        }
        Step("TargetSelector", PNTargetSelectorEditorChecks.Run);
        Step("ProjectileTeam", PNProjectileTeamEditorChecks.Run);
        Step("ProjectileHit", PNProjectileHitEditorChecks.Run);
        Step("Projectile", PNProjectileEditorChecks.Run);
        Step("ProjectileHoming", PNProjectileHomingEditorChecks.Run);
        Step("ProjectileGround", PNProjectileGroundEditorChecks.Run);
        Step("ProjectileTimeStop", PNProjectileTimeStopEditorChecks.Run);
        Step("EveSkillVolley", EveSkillVolleyEditorChecks.Run);
        Step("Penetration", PNPenetrationEditorChecks.Run);
        Step("Slash", PNSlashCombatEditorChecks.Run);
        Step("Lorica", PNLoricaEditorChecks.Run);
        Step("EveMoveset", EveMovesetEditorChecks.Run);
        Directory.CreateDirectory(".utmp/AutotargetReview");
        File.WriteAllLines(".utmp/AutotargetReview/Batch.txt", lines);
        EditorApplication.Exit(failed ? 1 : 0);
    }
}
#endif
