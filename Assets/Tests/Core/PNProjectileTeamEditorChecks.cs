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

// Stationary variants used to observe Destroy / bypass without any target interaction.
public sealed class PNProjectileTeamNativeModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    {
        var registry = hub.Instances();
        var directional = registry.GetKind<PNProjectileDefinition>(PNProjectileGameplayContent.Directional);
        var data = JObject.Parse("{'speed':0,'acceleration':0,'speedMin':0,'speedMax':0,'life':{'mode':'Time','value':5},'volume':{'size':[0.4,0.4,0.4]},'environmentMask':[],'hitLimit':5}");
        registry.Register(directional.Derive(EveId.Parse("test:open_still"), data));
        registry.Register(directional.Derive(EveId.Parse("test:locked_still"), data, false));
        var shortLife = (JObject)data.DeepClone(); shortLife["life"] = JObject.Parse("{'mode':'Time','value':0.3}");
        registry.Register(directional.Derive(EveId.Parse("test:locked_short"), shortLife, false));
    }
}

// EN: Real PhysX, projectile manager and entities in a disposable preview scene: the shooter's captured team decides who a Player/Enemy projectile hits, snapshots
//     expose team/source/destroyable, and Destroy honours the definition flag unless bypassed while lifecycle ends never do.
// VI: PhysX, projectile manager và entity thật trong preview scene dùng tạm: phe người bắn đã chụp quyết định đạn Player/Enemy trúng ai, snapshot lộ phe/nguồn/destroyable,
//     và Destroy tôn trọng cờ của định nghĩa trừ khi bypass, còn kết thúc vòng đời thì không bao giờ bị chặn.
public static class PNProjectileTeamEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        int count = 0; var evidence = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS " + message); count++; }
        bool Near(double a, double b, double tolerance = 1e-6) => Math.Abs(a - b) <= tolerance;
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNProjectileHitNativeModule(), new PNProjectileTeamNativeModule() }, docs);
        var preview = EditorSceneManager.NewPreviewScene(); var fixtures = new List<GameObject>(); PNProjectileManager manager = null;
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:projectile_team"));
            var session = EveRegistrySession.Context; long nextId = 997000; int both = (1 << 6) | (1 << 7);
            GameObject Fixture(string name) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, preview); fixtures.Add(obj); return obj; }
            manager = Fixture("Projectile manager").AddComponent<PNProjectileManager>(); manager.Initialize(session, new PNProjectileProfile { MaxActive = 40 });
            PNStat Enemy(Vector3 position)
            {
                var obj = Fixture("enemy " + nextId); obj.SetActive(false); obj.transform.position = position; obj.layer = 7;
                obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(nextId++), EveId.Parse("project_n:dummy"), 0);
                var stat = obj.AddComponent<PNEnemyStat>(); obj.AddComponent<BoxCollider>().size = Vector3.one;
                obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); obj.GetComponent<PNBuffManager>().Advance(0); return stat;
            }
            // A Player-team entity (Eve) on layer Player that can also shoot.
            PNStat Player(Vector3 position, out GameObject obj)
            {
                obj = Fixture("player " + nextId); obj.SetActive(false); obj.transform.position = position; obj.layer = 6;
                obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(nextId++), EveId.Parse("project_n:eve"), 0);
                var stat = obj.AddComponent<EveCharacterStat>(); obj.AddComponent<PNAbilityManager>(); obj.AddComponent<PNProjectileSkillProbe>(); obj.AddComponent<BoxCollider>().size = Vector3.one;
                obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); obj.GetComponent<PNBuffManager>().Advance(0); return stat;
            }
            double Hp(PNStat stat) => stat.GetCurrent(PNContentIds.Health);
            void Advance(int ticks, float step = .1f) { for (int i = 0; i < ticks; i++) manager.Advance(step); }
            PNProjectileReceipt Fire(string id, Vector3 position, PNCombatSource source, int mask, PNPreparedDamage damage = null)
                => manager.Create(new PNProjectileLaunch(EveId.Parse(id), null, source, mask, damage), new PNProjectileSpawn(preview, position, Vector3.right));
            PNPreparedDamage Damage(PNCombatSource source, double amount = 100) => source.Snapshot.PrepareDamage(amount, false, 0d, PNContentIds.True);

            // 1. A Player projectile with a mask covering both teams passes through a Player ally and hits the enemy behind it.
            var shooterA = Player(new Vector3(0, 0, 600), out _); var allyA = Player(new Vector3(2, 0, 600), out _); var enemyA = Enemy(new Vector3(3.2f, 0, 600)); Physics.SyncTransforms();
            var sourceA = PNCombatSource.Capture(shooterA); double allyHp = Hp(allyA), enemyHp = Hp(enemyA);
            Check(sourceA.Team == PNCombatTeam.Player, "a captured Player shooter gives a Player source");
            var shotA = Fire("test:hit_once", new Vector3(0, 0, 600), sourceA, both, Damage(sourceA));
            Advance(2); Check(shotA.Handle.IsAlive && Near(Hp(allyA), allyHp), "a Player projectile passes through a Player ally without a hit");
            Advance(7);
            Check(Near(Hp(allyA), allyHp) && Near(enemyHp - Hp(enemyA), 100d) && !shotA.Handle.IsAlive, "it then hits the enemy behind (hitLimit 1 was not spent on the ally) and ends");

            // 2. An Enemy projectile with the same wide mask passes through another enemy and hits the player.
            var shooterB = Enemy(new Vector3(0, 0, 620)); var enemyB = Enemy(new Vector3(2, 0, 620)); var playerB = Player(new Vector3(3.2f, 0, 620), out _); Physics.SyncTransforms();
            var sourceB = PNCombatSource.Capture(shooterB); double enemyBHp = Hp(enemyB), playerBHp = Hp(playerB);
            Check(sourceB.Team == PNCombatTeam.Enemy, "a captured Enemy shooter gives an Enemy source");
            var shotB = Fire("test:hit_once", new Vector3(0, 0, 620), sourceB, both, Damage(sourceB, 10)); // Eve starts with 40 HP, so use a small hit
            Advance(9);
            Check(Near(Hp(enemyB), enemyBHp) && Near(playerBHp - Hp(playerB), 10d) && !shotB.Handle.IsAlive, "an Enemy projectile passes through another enemy and hits the player");

            // 3. A Neutral source keeps the plain layer-mask rule: the first target in range is hit.
            var neutral = PNCombatSource.System(session.SessionId, new EveEntityId(nextId++));
            var allyC = Player(new Vector3(2, 0, 640), out _); var enemyC = Enemy(new Vector3(3.2f, 0, 640)); Physics.SyncTransforms();
            var shotC = Fire("test:hit_once", new Vector3(0, 0, 640), neutral, both);
            Advance(5);
            Check(neutral.Team == PNCombatTeam.Neutral && !shotC.Handle.IsAlive, "a Neutral source's projectile is stopped by the first target on the mask (layer rule unchanged)");

            // 4. The shooter dies and unloads before the hit: the captured team still decides.
            var shooterD = Player(new Vector3(0, 0, 660), out var shooterDObject); var allyD = Player(new Vector3(2, 0, 660), out _); var enemyD = Enemy(new Vector3(3.2f, 0, 660)); Physics.SyncTransforms();
            var sourceD = PNCombatSource.Capture(shooterD); double allyDHp = Hp(allyD), enemyDHp = Hp(enemyD);
            var shotD = Fire("test:hit_once", new Vector3(0, 0, 660), sourceD, both, Damage(sourceD));
            shooterD.SetCurrent(PNContentIds.Health, 0); shooterDObject.GetComponent<EveEntityLoader>().Unload();
            Advance(9);
            Check(Near(Hp(allyD), allyDHp) && Near(enemyDHp - Hp(enemyD), 100d) && !shotD.Handle.IsAlive, "a projectile whose shooter died and unloaded still passes the ally and hits the enemy");

            // 5. Snapshots expose team, source and destroyable; a clearing skill filters by team.
            var shooterE = Player(new Vector3(0, 0, 680), out _); var hostile = Enemy(new Vector3(0, 0, 700)); Physics.SyncTransforms();
            var playerSourceE = PNCombatSource.Capture(shooterE); var enemySourceE = PNCombatSource.Capture(hostile);
            var mineOpen = Fire("test:open_still", new Vector3(0, 0, 690), playerSourceE, 1 << 7);
            var mineLocked = Fire("test:locked_still", new Vector3(1, 0, 690), playerSourceE, 1 << 7);
            var theirs = Fire("test:open_still", new Vector3(2, 0, 690), enemySourceE, 1 << 6);
            var theirsLocked = Fire("test:locked_still", new Vector3(3, 0, 690), enemySourceE, 1 << 6);
            var neutralShot = Fire("test:open_still", new Vector3(4, 0, 690), neutral, 1 << 7);
            Advance(1);
            var s1 = manager.Get(mineOpen.Handle); var s2 = manager.Get(mineLocked.Handle); var s3 = manager.Get(theirs.Handle); var s4 = manager.Get(neutralShot.Handle);
            Check(s1.Team == PNCombatTeam.Player && s1.SourceId == playerSourceE.EntityId && s1.Destroyable && !s2.Destroyable && s3.Team == PNCombatTeam.Enemy && s3.SourceId == enemySourceE.EntityId &&
                s4.Team == PNCombatTeam.Neutral && s4.SourceId == neutral.EntityId, "snapshots expose the shooter's team and entity and the destroyable flag");
            Check(manager.Snapshot().Count(x => x.Team == PNCombatTeam.Enemy) >= 2 && manager.Snapshot().Count(x => x.Team == PNCombatTeam.Player) >= 2, "the running-projectile list carries team for scanning skills");

            // A shield / clear-screen skill: scan around a point, destroy only the opposing team's projectiles.
            var found = new List<PNProjectileHandle>();
            manager.QueryRunning(s1.World, new Vector3(2, 0, 690), 6f, found);
            int destroyed = 0, blocked = 0;
            foreach (var handle in found)
            {
                var snap = manager.Get(handle);
                if (snap.Team != PNCombatTeam.Enemy) continue;
                if (manager.Destroy(handle, PNProjectileEndReason.Destroyed, playerSourceE)) destroyed++; else blocked++;
            }
            Advance(1);
            Check(found.Count >= 5 && destroyed == 1 && blocked == 1 && !theirs.Handle.IsAlive && theirsLocked.Handle.IsAlive && mineOpen.Handle.IsAlive && mineLocked.Handle.IsAlive && neutralShot.Handle.IsAlive,
                "a clearing skill destroys only the opposing team's destroyable projectile; the locked one, the owner's and the Neutral one stay");
            Check(!manager.Destroy(theirsLocked.Handle) && theirsLocked.Handle.IsAlive, "Destroy without bypass is refused for a destroyable:false projectile");
            Check(manager.Destroy(theirsLocked.Handle, PNProjectileEndReason.Destroyed, playerSourceE, bypass: true), "Destroy with bypass ends it");
            Advance(1); Check(!theirsLocked.Handle.IsAlive, "the bypassed projectile is gone");
            Check(!manager.Destroy(theirsLocked.Handle) && !manager.Destroy(theirsLocked.Handle, bypass: true), "destroying an ended projectile returns false");

            // Lifecycle ends are never blocked by the flag.
            Check(mineLocked.Handle.End(), "End ends a destroyable:false projectile");
            var expiring = Fire("test:locked_short", new Vector3(0, 0, 720), playerSourceE, 1 << 7);
            Advance(6); Check(!expiring.Handle.IsAlive, "a destroyable:false projectile still expires with its lifetime");
            var kept = Fire("test:locked_still", new Vector3(0, 0, 730), playerSourceE, 1 << 7); Advance(1);
            manager.Shutdown();
            Check(!kept.Handle.IsAlive, "manager Shutdown ends destroyable:false projectiles");
        }
        finally
        {
            try { if (manager != null) manager.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var obj in fixtures) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(preview); }
        }
        Check(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "Authored scene and dirty state preserved.");
        string summary = "PASS " + count + " native projectile team checks";
        Directory.CreateDirectory(".utmp/ProjectileTeamReview"); File.WriteAllLines(".utmp/ProjectileTeamReview/NativeResults.txt", evidence.Concat(new[] { summary })); return summary;
    }
}

// EN: Batch entry for the clone project: the new check plus the existing native projectile and combat checks the change can affect.
// VI: Điểm vào batch cho project clone: kiểm mới cùng các kiểm native projectile và combat sẵn có mà thay đổi có thể ảnh hưởng.
public static class PNProjectileTeamBatch
{
    public static void Run()
    {
        var lines = new List<string>(); bool failed = false;
        void Step(string name, Func<string> run)
        {
            try { lines.Add(name + ": " + run()); }
            catch (Exception error) { failed = true; lines.Add(name + ": FAIL " + error.Message); Debug.LogException(error); }
        }
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
        Directory.CreateDirectory(".utmp/ProjectileTeamReview");
        File.WriteAllLines(".utmp/ProjectileTeamReview/Batch.txt", lines);
        EditorApplication.Exit(failed ? 1 : 0);
    }
}
#endif
