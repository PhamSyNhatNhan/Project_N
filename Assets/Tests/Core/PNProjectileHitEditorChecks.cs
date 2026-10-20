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

public class PNProjectileSkillProbe : PNSkill
{
    public static readonly EveId Volley = EveId.Parse("test:projectile_volley"), Silent = EveId.Parse("test:projectile_silent");
    public override IEnumerable<EveId> BuiltInSkillIds => new[] { Volley, Silent };
    public bool Begin(EveId id) { if (!CanActivateSkill(id)) return false; BeginActivation(id); return true; }
    public void End(bool cancel = false) => EndActivation(cancel);
}
public sealed class PNProjectileHitNativeModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    {
        var registry = hub.Get<PNProjectileDefinition>();
        var data = JObject.Parse("{'speed':10,'acceleration':0,'speedMin':0,'speedMax':200,'life':{'mode':'Time','value':5},'volume':{'size':[0.4,0.4,0.4]},'environmentMask':['Wall'],'hitLimit':1}");
        var directional = registry.Get(PNProjectileGameplayContent.Directional);
        void Variant(string name, Action<JObject> change) { var copy = (JObject)data.DeepClone(); change(copy); registry.Register(directional.Derive(EveId.Parse("test:" + name), copy)); }
        Variant("hit_once", d => { });
        Variant("hit_two", d => d["hitLimit"] = 2);
        Variant("hit_still", d => { d["speed"] = 0; d["speedMax"] = 0; d["hitLimit"] = 5; });
        Variant("hit_cooldown", d => { d["speed"] = 0; d["speedMax"] = 0; d["hitLimit"] = 3; d["hitCooldown"] = 0.25; });
        Variant("hit_wall", d => { d["hitLimit"] = 3; d["speed"] = 100; d["speedMax"] = 200; });
        Variant("ground_fall", d => { d["environmentMask"] = new JArray("Ground"); d["gravity"] = new JArray(0, -10, 0); d["speed"] = 4; });
        Variant("ground_pass", d => { d["gravity"] = new JArray(0, -10, 0); d["speed"] = 4; });
        Variant("hit_short", d => d["life"] = JObject.Parse("{'mode':'Time','value':0.3}"));
        var homingBase = registry.Get(PNProjectileGameplayContent.Homing);
        void Homing(string name, Action<JObject> change) { var copy = (JObject)homingBase.Payload; copy["environmentMask"] = new JArray(); copy["speed"] = 10; copy["speedMax"] = 200; copy["life"] = JObject.Parse("{'mode':'Time','value':6}"); copy["turnRate"] = 360; copy["homeAfter"] = 0; copy["searchRadius"] = 20; copy["searchInterval"] = 0.1; change(copy); registry.Register(homingBase.Derive(EveId.Parse("test:" + name), copy)); }
        Homing("homing_basic", d => { });
        Homing("homing_late", d => d["homeAfter"] = 0.5);
        Homing("homing_keep", d => d["lostTarget"] = "KeepHeading");
        void Skill(EveId id, string payload, string tags)
        {
            var root = JObject.Parse("{'categoryIds':['base:skill'],'affectedByAttackSpeed':false,'action':{'id':'project_n:projectile_action','perform':{'operationId':'project_n:create_projectile','data':" + payload + "}}}");
            hub.Skills.Register(new EveSkillDefinition(id, root));
        }
        Skill(PNProjectileSkillProbe.Volley, "{'projectileId':'test:hit_once','input':{},'count':3,'spread':20,'layerMask':128,'damage':{'value':{'amount':100},'canCrit':false,'tags':['base:true']}}", null);
        Skill(PNProjectileSkillProbe.Silent, "{'projectileId':'test:hit_once','input':{},'count':1,'spread':0,'layerMask':128}", null);
    }
}
public static class PNProjectileHitEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        int count = 0; var evidence = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS " + message); count++; }
        void Reject(Action action, string label) { try { action(); } catch (Exception) { Check(true, label); return; } throw new Exception("Expected rejection: " + label); }
        bool Near(double a, double b, double tolerance = 1e-6) => Math.Abs(a - b) <= tolerance;
        var docs = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNProjectileHitNativeModule() }, docs);
        var preview = EditorSceneManager.NewPreviewScene(); var fixtures = new List<GameObject>(); PNProjectileManager manager = null;
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:projectile_hit"));
            var session = EveRegistrySession.Context; int enemy = 1 << 7; long nextId = 991000;
            GameObject Fixture(string name) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, preview); fixtures.Add(obj); return obj; }
            manager = Fixture("Projectile manager").AddComponent<PNProjectileManager>(); manager.Initialize(session, new PNProjectileProfile { MaxActive = 20 });
            PNStat Dummy(Vector3 position)
            {
                var obj = Fixture("target " + nextId); obj.SetActive(false); obj.transform.position = position; obj.layer = 7;
                obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(nextId++), EveId.Parse("project_n:dummy"), 0);
                var stat = obj.AddComponent<PNEnemyStat>(); obj.AddComponent<BoxCollider>().size = Vector3.one;
                obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); obj.GetComponent<PNBuffManager>().Advance(0); return stat;
            }
            GameObject Shooter(Vector3 position, out PNProjectileSkillProbe skill)
            {
                var obj = Fixture("shooter " + nextId); obj.SetActive(false); obj.transform.position = position;
                obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(nextId++), EveId.Parse("project_n:eve"), 0);
                obj.AddComponent<EveCharacterStat>(); obj.AddComponent<PNAbilityManager>(); skill = obj.AddComponent<PNProjectileSkillProbe>();
                obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); obj.GetComponent<PNBuffManager>().Advance(0); return obj;
            }
            double Hp(PNStat stat) => stat.GetCurrent(PNContentIds.Health);
            PNProjectileReceipt Fire(string id, Vector3 position, Vector3 direction, PNBuffSource source, PNPreparedDamage damage = null, PNSkillHitSnapshot plan = null)
                => manager.Create(new PNProjectileLaunch(EveId.Parse(id), null, source, enemy, damage, plan), new PNProjectileSpawn(preview, position, direction));
            void Advance(int ticks, float step = .1f) { for (int i = 0; i < ticks; i++) manager.Advance(step); }

            // Damage from the prepared template: one hit, then the projectile ends.
            var shooterObject = Shooter(new Vector3(0, 0, 50), out var skill); var shooter = shooterObject.GetComponent<PNStat>();
            var source = PNBuffSource.Capture(shooter);
            var target = Dummy(new Vector3(2, 0, 0)); Physics.SyncTransforms();
            var prepared = source.Snapshot.PrepareDamage(100, false, 0d, PNContentIds.True);
            double before = Hp(target);
            var shot = Fire("test:hit_once", Vector3.zero, Vector3.right, source, prepared);
            Advance(1); Check(Hp(target) == before, "a projectile short of its target deals nothing");
            Advance(1);
            Check(Near(before - Hp(target), 100d) && !shot.Handle.IsAlive, "a hit applies the prepared amount once and ends a hitLimit 1 projectile");
            Advance(3); Check(Near(before - Hp(target), 100d), "an ended projectile deals no further damage");

            // Pierce: hitLimit 2 hits two targets in line, then ends.
            var second = Dummy(new Vector3(3.2f, 0, 0)); var third = Dummy(new Vector3(5f, 0, 0)); Physics.SyncTransforms();
            double hpB = Hp(second), hpC = Hp(third), hpA = Hp(target);
            var pierce = Fire("test:hit_two", Vector3.zero, Vector3.right, source, prepared);
            Advance(8);
            Check(!pierce.Handle.IsAlive && Hp(target) < hpA && Hp(second) < hpB && Near(hpC, Hp(third)), "hitLimit 2 hits two targets and ends before the third");

            // The same target is hit once unless a cooldown is declared.
            var still = Dummy(new Vector3(0, 0, 10)); Physics.SyncTransforms(); double hpStill = Hp(still);
            var rest = Fire("test:hit_still", new Vector3(0, 0, 10), Vector3.right, source, prepared);
            Advance(6);
            Check(Near(hpStill - Hp(still), 100d) && rest.Handle.IsAlive, "a stationary projectile overlapping a target hits it only once without a cooldown");
            rest.Handle.End();
            var again = Fire("test:hit_cooldown", new Vector3(0, 0, 10), Vector3.right, source, prepared); hpStill = Hp(still);
            Advance(4); Check(Near(hpStill - Hp(still), 200d) && again.Handle.IsAlive, "a cooldown projectile hits again only after the cooldown");
            Advance(3); Check(Near(hpStill - Hp(still), 300d) && !again.Handle.IsAlive, "the cooldown projectile ends at its hit limit");

            // Layers and aliveness decide who is hit.
            var wrongLayer = Dummy(new Vector3(0, 0, 20)); wrongLayer.gameObject.layer = 6; Physics.SyncTransforms(); double hpWrong = Hp(wrongLayer);
            var offLayer = Fire("test:hit_still", new Vector3(0, 0, 20), Vector3.right, source, prepared); Advance(3); offLayer.Handle.End();
            Check(Near(hpWrong, Hp(wrongLayer)), "an entity on another layer is never damaged");

            // The prepared amount already includes the source modifiers at launch; later changes do not reach a flying projectile.
            shooter.SetSource(EveId.Parse("test:pd_source"), new[] { new EveStatModifier(PNContentIds.ProjectileDamage, .5d, 1, PNContentIds.Scale), new EveStatModifier(PNContentIds.CritRate, 100d, 0) });
            var boosted = PNBuffSource.Capture(shooter);
            var tags = new[] { PNContentIds.True, PNContentIds.ProjectileDamage };
            var plain = boosted.Snapshot.PrepareDamage(100, false, 0d, tags); var crit = boosted.Snapshot.PrepareDamage(100, true, 0d, tags);
            Check(Near(plain.Amount, 150d) && crit.CanCrit && crit.CritRate >= 100d, "projectile_damage on the source raises the prepared amount (+50%): amount=" + plain.Amount + " rate=" + crit.CritRate);
            shooter.RemoveSource(EveId.Parse("test:pd_source"));
            var mitigated = Dummy(new Vector3(0, 0, 30)); Physics.SyncTransforms();
            mitigated.SetSource(EveId.Parse("test:pd_target"), new[] { new EveStatModifier(PNContentIds.ProjectileDamage, -.2d, 1, PNContentIds.Mitigation) });
            double hpMitigated = Hp(mitigated);
            Fire("test:hit_still", new Vector3(0, 0, 30), Vector3.right, boosted, plain); Advance(2);
            Check(Near(hpMitigated - Hp(mitigated), 120d), "the receiver mitigates the same projectile_damage tag (150 x 0.8) and a removed source modifier changes nothing");
            var critTarget = Dummy(new Vector3(0, 0, 40)); Physics.SyncTransforms(); double hpCrit = Hp(critTarget);
            Fire("test:hit_still", new Vector3(0, 0, 40), Vector3.right, boosted, crit); Advance(2);
            Check(Near(hpCrit - Hp(critTarget), 225d), "a guaranteed critical applies the critical multiplier on the hit (150 x 1.5)");

            // The shooter dies and unloads before a hit lands: damage and the frozen OnHit Burn still arrive with the right attribution.
            var burnTarget = Dummy(new Vector3(20, 0, 100));
            Physics.SyncTransforms();
            var burnShooterObject = Shooter(new Vector3(0, 0, 100), out var burnSkill); var burnShooter = burnShooterObject.GetComponent<PNStat>();
            var burnId = hub.Buffs().ResolveIdentity(EveId.Parse("base:burn")); var burnBuffs = burnTarget.GetComponent<PNBuffManager>().Buffs;
            int Burn(PNStat from, PNStat on) => (int)on.GetComponent<PNBuffManager>().Buffs.Snapshot().Where(x => x.Id == burnId && x.SourceId == from.EntityId).Sum(x => x.Stacks ?? 0);
            using (burnSkill.Effects.Contribute("burn on hit", new PNSkillContribution(PNSkillEffectsEditorChecks.Rule("Live", "OnHit", "HitTarget"), hub)))
            {
                Check(burnSkill.Begin(PNProjectileSkillProbe.Silent), "the volley skill begins");
                using (var action = burnSkill.CreateAction(PNProjectileSkillProbe.Silent, out var context, Vector3.right))
                {
                    action.Start(context);
                    action.Perform(context); action.Exit(context);
                    Check(((PNProjectileAction)action).Results.Count == 1 && ((PNProjectileAction)action).Results[0].Status == PNProjectileStatus.Created, "the projectile action creates its volley");
                }
                burnSkill.End();
            }
            var beforeBurn = Burn(burnShooter, burnTarget); var burnerId = burnShooter.EntityId;
            burnShooter.SetCurrent(PNContentIds.Health, 0); burnShooterObject.GetComponent<EveEntityLoader>().Unload();
            Advance(25);
            var carried = burnBuffs.Snapshot().Where(x => x.Id == burnId && x.SourceId == burnerId).Sum(x => x.Stacks ?? 0);
            Check(beforeBurn == 0 && carried == 1, "the Burn frozen at launch is applied by the hit after the shooter died and unloaded, attributed to the shooter");

            // The basic volley operation: three projectiles, damage prepared once, spread around the up axis, partial volleys on a full cap.
            var volleyObject = Shooter(new Vector3(0, 0, 200), out var volleySkill); var volleyStat = volleyObject.GetComponent<PNStat>();
            var rays = new[] { -20f, 0f, 20f }.Select(angle => Quaternion.AngleAxis(angle, Vector3.up) * Vector3.right).ToArray();
            var victims = rays.Select(r => Dummy(new Vector3(0, 0, 200) + r * 6f)).ToArray(); Physics.SyncTransforms();
            double[] hps = victims.Select(Hp).ToArray();
            Check(volleySkill.Begin(PNProjectileSkillProbe.Volley), "the three-projectile volley skill begins");
            IReadOnlyList<PNProjectileReceipt> volley;
            using (var action = volleySkill.CreateAction(PNProjectileSkillProbe.Volley, out var vcontext, Vector3.right))
            {
                action.Start(vcontext); action.Perform(vcontext); action.Exit(vcontext);
                var results = volley = ((PNProjectileAction)action).Results;
                Check(results.Count == 3 && results.All(x => x.Status == PNProjectileStatus.Created), "the volley creates three projectiles");
            }
            volleySkill.End(); Advance(10);
            Check(victims.Select((v, i) => hps[i] - Hp(v)).All(delta => Near(delta, 100d)), "each of the three projectiles hits its own target for the volley's damage (prepared once)");
            Check(volley.All(x => !x.Handle.IsAlive), "every projectile of the volley ended on its hit");
            using (var action = volleySkill.CreateAction(PNProjectileSkillProbe.Volley, out var noDirection, new PNStat[0], (Vector3?)null))
                Reject(() => action.Start(noDirection), "the projectile action refuses a context without a direction");
            manager.Shutdown(); manager.Initialize(session, new PNProjectileProfile { MaxActive = 2 });
            volleySkill.Begin(PNProjectileSkillProbe.Volley);
            using (var action = volleySkill.CreateAction(PNProjectileSkillProbe.Volley, out var capped, Vector3.right))
            {
                action.Start(capped);
                action.Perform(capped); action.Exit(capped);
                var statuses = ((PNProjectileAction)action).Results.Select(x => x.Status).ToArray();
                Check(statuses.Length == 3 && statuses[0] == PNProjectileStatus.Created && statuses[1] == PNProjectileStatus.Created && statuses[2] == PNProjectileStatus.Rejected && manager.ActiveCount == 2, "a volley against a full cap comes out short with a Rejected receipt instead of failing");
            }
        }
        finally
        {
            try { if (manager != null) manager.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var obj in fixtures) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(preview); }
        }
        Check(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "Authored scene and dirty state preserved.");
        string summary = "PASS " + count + " native projectile hit checks";
        Directory.CreateDirectory(".utmp/ProjectileReview"); File.WriteAllLines(".utmp/ProjectileReview/NativeHitResults.txt", evidence.Concat(new[] { summary })); return summary;
    }
}
#endif
