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

// EN: Real JSON, PhysX, projectile manager and Buff flow in a disposable preview scene: a source carrying penetration modifiers must change the
//     receiver's Mitigation / DEF for Slash, projectile and DOT hits, and must leave nothing on the receiver.
// VI: JSON, PhysX, projectile manager và Buff flow thật trong preview scene dùng tạm: nguồn mang modifier penetration phải đổi Mitigation / DEF
//     của bên nhận cho Slash, projectile và DOT, và không để lại gì trên bên nhận.
public static class PNPenetrationEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        int count = 0; var evidence = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS " + message); count++; }
        bool Near(double a, double b, double tolerance = 1e-6) => Math.Abs(a - b) <= tolerance;
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNProjectileHitNativeModule() }, docs);
        var preview = EditorSceneManager.NewPreviewScene(); var fixtures = new List<GameObject>(); PNProjectileManager manager = null;
        var resist = EveId.Parse("test:pen_resist"); var penetration = EveId.Parse("test:pen_source"); var defenseSource = EveId.Parse("test:pen_defense");
        var fireResist = new EveStatModifier(PNContentModule.Fire, -.5d, 1, PNContentIds.Mitigation);
        var firePenetration = new EveStatModifier(PNContentModule.Fire, .2d, 1, PNContentIds.Penetration);
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:penetration_native"));
            var session = EveRegistrySession.Context; long nextId = 996000; var enemyMask = PNTargetLayers.Of(PNLayers.Enemy);
            GameObject Fixture(string name) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, preview); fixtures.Add(obj); return obj; }
            T Entity<T>(string name, Vector3 position, string character, bool skill = false, int layer = 0) where T : PNStat
            {
                var go = Fixture(name); go.SetActive(false); go.transform.position = position; go.layer = layer;
                go.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(nextId++), EveId.Parse(character), 1);
                var stat = go.AddComponent<T>(); stat.gameObject.layer = stat is PNEnemyStat ? PNLayers.Enemy : stat is PNPlayerStat ? PNLayers.Player : stat.gameObject.layer;
                if (skill) { go.AddComponent<Rigidbody>().useGravity = false; go.AddComponent<CapsuleCollider>(); go.AddComponent<PNPlayerMove>(); go.AddComponent<PNEveAttackSpeedProbe>(); }
                else go.AddComponent<BoxCollider>().size = Vector3.one;
                go.AddComponent<EveEntityLoader>().Load(hub); go.SetActive(true); go.GetComponent<PNBuffManager>().Advance(0); return stat;
            }
            double Hp(PNStat stat) => stat.GetCurrent(PNContentIds.Health);

            // ---- Slash: real Eve JSON (tags physical + fire + ice + lightning) -> sector -> Dummy HP.
            var caster = Entity<EveCharacterStat>("Penetration slash caster", new Vector3(0, 0, 400), "project_n:eve", true);
            caster.SetSource(EveId.Parse("test:pen_fixed_crit"), new[] { new EveStatModifier(PNContentIds.CritRate, -caster.Evaluate(PNContentIds.CritRate), 0) });
            var victim = Entity<DummyStat>("Penetration slash victim", new Vector3(.5f, .18f, 400), "project_n:dummy");
            var child = new GameObject("Slash child hurtbox"); child.transform.SetParent(victim.transform, false); child.AddComponent<SphereCollider>().radius = .05f;
            victim.SetSource(resist, new[] { fireResist });
            var skill = caster.GetComponent<EveCharacterSkill>(); var probe = caster.GetComponent<PNEveAttackSpeedProbe>();
            PNDamageResult Slash()
            {
                victim.AdvanceCombatTime(100d); Physics.SyncTransforms(); probe.BeginCategoryReview();
                try
                {
                    var action = (PNSlashAction)skill.CreateAction(EveId.Parse("base:eve_slash"), out var context, Vector3.right);
                    using (action) { action.Start(context); action.Perform(context); action.Advance(1f); var hit = action.Results.First(x => x.TargetId == victim.EntityId); action.Exit(context); return hit; }
                }
                finally { probe.EndCategoryReview(); }
            }
            var plainSlash = Slash();
            Check(plainSlash.Damage.Penetration.Count == 0 && Near(plainSlash.CalculatedDamage, plainSlash.Damage.Amount * .5d, 1e-4), "Slash without penetration: fire Mitigation -0.5 halves the hit");
            caster.SetSource(penetration, new[] { firePenetration });
            var penSlash = Slash();
            Check(penSlash.Damage.Penetration.Count == 1 && Near(penSlash.CalculatedDamage, penSlash.Damage.Amount * .7d, 1e-4),
                "Real Slash against Dummy: fire penetration +0.2 joins Mitigation (-0.5 + 0.2 = x0.7)");
            Check(Near(penSlash.Damage.Amount, plainSlash.Damage.Amount, 1e-4), "penetration does not change the outgoing amount");
            caster.RemoveSource(penetration);
            var afterSlash = Slash();
            Check(Near(afterSlash.CalculatedDamage, afterSlash.Damage.Amount * .5d, 1e-4) && Near(victim.Evaluate(PNContentIds.Defense), 0d), "removing the source modifier restores the plain result and the receiver keeps nothing");

            // ---- Projectile: prepared damage frozen at launch carries the penetration; a hit later applies it.
            manager = Fixture("Projectile manager").AddComponent<PNProjectileManager>(); manager.Initialize(session, new PNProjectileProfile { MaxActive = 20 });
            GameObject Shooter(Vector3 position)
            {
                var obj = Fixture("shooter " + nextId); obj.SetActive(false); obj.transform.position = position;
                obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(nextId++), EveId.Parse("project_n:eve"), 0);
                obj.AddComponent<EveCharacterStat>(); obj.AddComponent<PNAbilityManager>(); obj.AddComponent<PNProjectileSkillProbe>();
                obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); obj.GetComponent<PNBuffManager>().Advance(0); return obj;
            }
            void Advance(int ticks, float step = .1f) { for (int i = 0; i < ticks; i++) manager.Advance(step); }
            PNProjectileReceipt Fire(Vector3 position, PNCombatSource source, PNPreparedDamage prepared)
                => manager.Create(new PNProjectileLaunch(EveId.Parse("test:hit_still"), null, source, enemyMask, prepared, null), new PNProjectileSpawn(preview, position, Vector3.right));
            var shooter = Shooter(new Vector3(0, 0, 450)).GetComponent<PNStat>();
            double Shoot(PNStat target, Vector3 at, PNPreparedDamage prepared, PNCombatSource source)
            {
                Physics.SyncTransforms(); double before = Hp(target); var shot = Fire(at, source, prepared); Advance(3); shot.Handle.End(); return before - Hp(target);
            }
            var projectileTarget = Entity<PNEnemyStat>("Penetration projectile target", new Vector3(0, 0, 460), "project_n:dummy", false, 7);
            projectileTarget.SetSource(resist, new[] { fireResist });
            var plainSource = PNCombatSource.Capture(shooter);
            var plainPrepared = plainSource.Snapshot.PrepareDamage(100, false, 0d, PNContentIds.True, PNContentModule.Fire);
            Check(Near(Shoot(projectileTarget, new Vector3(0, 0, 460), plainPrepared, plainSource), 50d, 1e-3), "projectile without penetration: true fire 100 x 0.5");
            shooter.SetSource(penetration, new[] { firePenetration });
            var penSource = PNCombatSource.Capture(shooter);
            var penPrepared = penSource.Snapshot.PrepareDamage(100, false, 0d, PNContentIds.True, PNContentModule.Fire);
            shooter.RemoveSource(penetration);
            Check(penPrepared.Penetration.Count == 1 && Near(Shoot(projectileTarget, new Vector3(0, 0, 460), penPrepared, penSource), 70d, 1e-3),
                "projectile prepared with penetration still deals x0.7 after the source modifier is removed (frozen at launch)");
            projectileTarget.SetSource(defenseSource, new[] { new EveStatModifier(PNContentIds.Defense, 500d, 0, default(EveId), PNContentIds.Before) });
            projectileTarget.RemoveSource(resist);
            var plainDefense = plainSource.Snapshot.PrepareDamage(100, false, 0d, PNContentIds.Physical);
            Check(Near(Shoot(projectileTarget, new Vector3(0, 0, 460), plainDefense, plainSource), 50d, 1e-3), "projectile against DEF 500: plain physical hit is halved");
            shooter.SetSource(penetration, new[] { new EveStatModifier(PNContentIds.Defense, -.2d, 1, PNContentIds.Penetration) });
            var defenseSourceCapture = PNCombatSource.Capture(shooter);
            var defensePrepared = defenseSourceCapture.Snapshot.PrepareDamage(100, false, 0d, PNContentIds.Physical);
            shooter.RemoveSource(penetration);
            double expectedDefense = 100d * (1d - 2d / Math.PI * Math.Atan(400d / 500d));
            Check(Near(Shoot(projectileTarget, new Vector3(0, 0, 460), defensePrepared, defenseSourceCapture), expectedDefense, 1e-3) && Near(projectileTarget.Evaluate(PNContentIds.Defense), 500d),
                "projectile with defense penetration -0.2 hits through DEF 400 and the receiver keeps DEF 500");

            // ---- DOT: Burn (magic + fire) ticks use the snapshot taken when the buff was applied.
            var dotTarget = Entity<PNEnemyStat>("Penetration DOT target", new Vector3(0, 0, 500), "project_n:dummy");
            dotTarget.SetSource(resist, new[] { fireResist });
            var dotSource = Entity<PNEnemyStat>("Penetration DOT source", new Vector3(0, 0, 520), "project_n:dummy");
            dotSource.SetSource(EveId.Parse("test:pen_attack"), new[] { new EveStatModifier(PNContentIds.AttackPower, 100, 0) });
            var buffs = dotTarget.GetComponent<PNBuffManager>().Buffs; var burn = EveId.Parse("base:burn");
            PNDamageResult Tick(PNCombatSource from)
            {
                var grant = buffs.Apply(new PNBuffApplyRequest(burn, from, JObject.Parse("{'stacks':35}")));
                PNDamageResult hit = null; Action<PNDamageResult> watch = r => hit = r; dotTarget.DamageReceived += watch;
                try { buffs.Trigger(grant.Handle); } finally { dotTarget.DamageReceived -= watch; }
                buffs.Remove(grant.Handle); return hit;
            }
            var plainTick = Tick(PNCombatSource.Capture(dotSource));
            dotSource.SetSource(penetration, new[] { firePenetration });
            var penTick = Tick(PNCombatSource.Capture(dotSource));
            Check(plainTick != null && penTick != null && plainTick.Damage.Penetration.Count == 0 && penTick.Damage.Penetration.Count == 1, "Burn ticks carry penetration only when the applying source had it");
            Check(Near(penTick.CalculatedDamage / plainTick.CalculatedDamage, 1.4d, 1e-6), "Burn with fire penetration +0.2 deals x1.4 of the plain tick (0.7 / 0.5)");
            dotSource.RemoveSource(penetration);
            var afterTick = Tick(PNCombatSource.Capture(dotSource));
            Check(Near(afterTick.CalculatedDamage, plainTick.CalculatedDamage, 1e-6), "a later Burn from the same source without the modifier is plain again");
        }
        finally
        {
            try { if (manager != null) manager.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var obj in fixtures) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(preview); }
        }
        Check(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "Authored scene and dirty state preserved.");
        string summary = "PASS " + count + " native penetration checks";
        Directory.CreateDirectory(".utmp/PenetrationReview"); File.WriteAllLines(".utmp/PenetrationReview/NativeResults.txt", evidence.Concat(new[] { summary })); return summary;
    }
}

// EN: Batch entry for the clone project: runs the new check plus the existing native combat checks the penetration change can affect.
// VI: Điểm vào batch cho project clone: chạy kiểm mới cùng các kiểm combat native sẵn có mà thay đổi penetration có thể ảnh hưởng.
public static class PNPenetrationBatch
{
    public static void Run()
    {
        var lines = new List<string>(); bool failed = false;
        void Step(string name, Func<string> run)
        {
            try { lines.Add(name + ": " + run()); }
            catch (Exception error) { failed = true; lines.Add(name + ": FAIL " + error.Message); Debug.LogException(error); }
        }
        Step("Penetration", PNPenetrationEditorChecks.Run);
        Step("DamageFlowFlatScale", PNDamageFlowEditorChecks.RunFlatScale);
        Step("DamageFlow", PNDamageFlowEditorChecks.Run);
        Step("Slash", PNSlashCombatEditorChecks.Run);
        Step("ProjectileHit", PNProjectileHitEditorChecks.Run);
        Step("Lorica", PNLoricaEditorChecks.Run);
        Directory.CreateDirectory(".utmp/PenetrationReview");
        File.WriteAllLines(".utmp/PenetrationReview/Batch.txt", lines);
        EditorApplication.Exit(failed ? 1 : 0);
    }
}
#endif
