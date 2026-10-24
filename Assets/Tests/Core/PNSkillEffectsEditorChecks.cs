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

public class PNSkillEffectsNativeProbe : PNSkill
{
    public static readonly EveId Attack = EveId.Parse("test:native_effect_attack"), Hybrid = EveId.Parse("test:native_effect_hybrid"), SkillEntry = EveId.Parse("test:native_effect_skill");
    public override IEnumerable<EveId> BuiltInSkillIds => new[] { Attack, Hybrid, SkillEntry };
    public bool Begin(EveId id) { if (!CanActivateSkill(id)) return false; BeginActivation(id); return true; }
    public void Commit(PNStat target = null) => CommitActivation(target == null ? null : new[] { target }, Vector3.right);
    public void End(bool cancel = false) => EndActivation(cancel);
    public float Speed => ExecutionSpeed;
    public void Tick(float delta) => AdvanceAuxiliaryEffects(delta);
}
// Detached context carrying a frozen source, standing in for a projectile.
public sealed class PNSkillEffectsNativeHost : PNSkillActionContext
{
    private readonly PNCombatSource frozen;
    public PNSkillEffectsNativeHost(EveRegistryHub hub, PNCombatSource source, IEnumerable<PNStat> targets)
        : base(PNSkillEffectsNativeProbe.Attack, null, hub, targets, null, null, null) { frozen = source; }
    public override PNCombatSource Source => frozen;
    public override EveWorldContext World => null;
}
public sealed class PNSkillEffectsNativeModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    {
        foreach (var id in new[] { PNSkillEffectsNativeProbe.Attack, PNSkillEffectsNativeProbe.Hybrid, PNSkillEffectsNativeProbe.SkillEntry })
        {
            var root = JObject.Parse("{'categoryIds':['base:attack'],'affectedByAttackSpeed':true,'action':{'id':'base:damage_action','perform':{'value':{'amount':1},'tags':['base:true'],'canCrit':false,'iframeDuration':0}}}");
            if (id == PNSkillEffectsNativeProbe.Hybrid) root["categoryIds"] = new JArray("base:attack", "base:skill");
            if (id == PNSkillEffectsNativeProbe.SkillEntry) { root["categoryIds"] = new JArray("base:skill"); root["affectedByAttackSpeed"] = false; root.Remove("action"); }
            hub.Skills.Register(new EveSkillDefinition(id, root));
        }
    }
}
public static class PNSkillEffectsEditorChecks
{
    public static JObject Rule(string mode, string trigger, string target, string scope = "PerTarget")
        => JObject.Parse("{'readMode':'" + mode + "','effects':[{'trigger':'" + trigger + "','kind':'Operation','id':'base:apply_buff','data':{'buffId':'base:burn'},'target':'" + target + "','hitScope':'" + scope + "'}]}");
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNSkillEffectsNativeModule() }, docs);
        var preview = EditorSceneManager.NewPreviewScene(); var evidence = new List<string>(); int checks = 0;
        void Check(bool condition, string text) { if (!condition) throw new Exception(text); evidence.Add("PASS " + text); checks++; }
        GameObject Entity(string name, long id, Vector3 position)
        {
            var root = new GameObject(name); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview); root.transform.position = position;
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:eve"), 0);
            root.AddComponent<EveCharacterStat>(); root.AddComponent<PNAbilityManager>(); root.AddComponent<PNSkillEffectsNativeProbe>(); root.AddComponent<EveEntityLoader>().Load(hub); root.SetActive(true); root.GetComponent<PNBuffManager>().Advance(0); return root;
        }
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:native_skill_effects"));
            var caster = Entity("Skill effects source", 989001, Vector3.zero); var target = Entity("Skill effects target", 989002, Vector3.right);
            var stat = caster.GetComponent<PNStat>(); var receiver = target.GetComponent<PNStat>(); var skill = caster.GetComponent<PNSkillEffectsNativeProbe>(); var state = skill.Effects;
            Check(!hub.Stats.TryGet(EveId.Parse("base:attack"), out _) && hub.Stats.ResolveIdentity(EveId.Parse("base:attack_power")) == PNContentIds.AttackPower,
                "Native character uses attack_power while attack is absent from the stat registry.");
            Check(hub.Tags().ResolveIdentity(EveId.Parse("base:attack")) == PNContentIds.AttackTag && !hub.Tags().TryGet(EveId.Parse("base:attack_damage"), out _),
                "Native attack tag uses the freed ID without legacy aliases.");
            var categoryBonus = EveId.Parse("test:native_category_bonus");
            foreach (var name in new[] { "skill", "ultimate", "dash", "burst" })
            {
                var tag = EveId.Parse("base:" + name);
                var canonical = hub.Tags().ResolveIdentity(tag);
                Check(canonical == EveId.Parse("project_n:" + name) && hub.Tags().ResolveIdentity(hub.SkillCategories().Get(tag).DamageTagId.Value) == canonical &&
                    !hub.Tags().TryGet(EveId.Parse("base:" + name + "_damage"), out _) && !hub.Tags().TryGet(EveId.Parse("project_n:" + name + "_damage"), out _), "Native renamed category/tag IDs without legacy aliases: " + name);
                stat.SetSource(categoryBonus, new[] { new EveStatModifier(tag, .2d, 1, PNContentIds.Scale) });
                receiver.SetSource(categoryBonus, new[] { new EveStatModifier(tag, .25d, 1, PNContentIds.Mitigation) });
                var packet = stat.CaptureCombatSource().CreateDamage(100d, false, 0d, PNContentIds.True, tag);
                Check(packet.Tags.Contains(canonical) && Math.Abs(packet.Amount - 120d) < 1e-9 && Math.Abs(receiver.Calculate(PNContentIds.Mitigation, packet.Amount, tag) - 150d) < 1e-9,
                    "Native outgoing/incoming renamed tag modifiers: " + name);
                stat.RemoveSource(categoryBonus); receiver.RemoveSource(categoryBonus);
            }
            stat.SetSource(categoryBonus, new[] { new EveStatModifier(PNContentIds.AttackTag, .2d, 1, PNContentIds.Scale) });
            receiver.SetSource(categoryBonus, new[] { new EveStatModifier(PNContentIds.AttackTag, .25d, 1, PNContentIds.Mitigation) });
            PNDamageResult receivedCategoryHit = null;
            Action<PNDamageResult> categoryObserver = result => receivedCategoryHit = result;
            receiver.DamageReceived += categoryObserver;
            skill.Begin(PNSkillEffectsNativeProbe.Attack);
            using (var categoryAction = (PNDamageAction)skill.CreateAction(PNSkillEffectsNativeProbe.Attack, out var categoryContext, new[] { receiver }))
            {
                categoryAction.Start(categoryContext); categoryAction.Perform(categoryContext);
                Check(receivedCategoryHit != null && receivedCategoryHit.Damage.Tags.Contains(PNContentIds.AttackTag), "Native receiver event contains mapped category tag.");
                Check(Math.Abs(receivedCategoryHit.Damage.Amount - 1.2d) < 1e-9 && Math.Abs(receivedCategoryHit.CalculatedDamage - 1.5d) < 1e-9, "Native category outgoing bonus and incoming mitigation apply exactly once.");
                var auxiliaryContext = new PNSkillActionContext(skill, PNSkillEffectsNativeProbe.Attack, categoryContext.Data, hub, null, null, skill.CurrentInvocation, false, null, categoryContext.CategoryIds);
                Check(auxiliaryContext.EffectiveTags().Contains(PNContentIds.AttackTag), "Native auxiliary inherits category mapping without contribution replay.");
                using (state.Contribute("native suppression", new PNSkillContribution(JObject.Parse("{'readMode':'Live','suppressTags':['base:attack']}"), hub)))
                    Check(!categoryContext.EffectiveTags().Contains(PNContentIds.AttackTag), "Native tag suppression wins over mapping.");
                categoryAction.Exit(categoryContext);
            }
            skill.End(); receiver.DamageReceived -= categoryObserver; stat.RemoveSource(categoryBonus); receiver.RemoveSource(categoryBonus);
            var buffs = target.GetComponent<PNBuffManager>().Buffs; var ownerBuffs = caster.GetComponent<PNBuffManager>().Buffs;
            using (skill.Controls.Acquire("silence", new[] { PNControlIds.Skill }))
            {
                Check(!skill.Begin(PNSkillEffectsNativeProbe.Hybrid) && skill.Begin(PNSkillEffectsNativeProbe.Attack), "Real hybrid checks both mapped permissions."); skill.End();
                using (state.Contribute("new category", new PNSkillContribution(JObject.Parse("{'readMode':'Live','addCategories':['base:skill']}"), hub)))
                    Check(!skill.Begin(PNSkillEffectsNativeProbe.Attack), "Live category addition participates in activation gates.");
            }
            var grant = ownerBuffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_skill_effect_template"), PNCombatSource.Capture(stat)));
            Check(skill.Begin(PNSkillEffectsNativeProbe.Attack), "Attack begins with registered Buff contribution.");
            using (var action = skill.CreateAction(PNSkillEffectsNativeProbe.Attack, out var context, new[] { receiver }))
            {
                action.Start(context); skill.Commit(receiver); ownerBuffs.Remove(grant.Handle); action.Perform(context);
                Check(buffs.Snapshot().Count == 1 && buffs.Snapshot()[0].Stacks == 1, "Real damage contact applies Burn from retained Snapshot after Buff removal.");
                action.Exit(context);
            }
            skill.End();
            // P0-B: freeze the real Buff-granted OnHit, destroy the shooter entity, then run the frozen effects.
            {
                var shooter = Entity("Frozen shooter", 989003, new Vector3(0, 0, 3)); var shooterStat = shooter.GetComponent<PNStat>(); var shooterSkill = shooter.GetComponent<PNSkillEffectsNativeProbe>();
                var shooterBuffs = shooter.GetComponent<PNBuffManager>().Buffs;
                var shooterGrant = shooterBuffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_skill_effect_template"), PNCombatSource.Capture(shooterStat)));
                PNSkillHitSnapshot plan;
                Check(shooterSkill.Begin(PNSkillEffectsNativeProbe.Attack), "Frozen shooter begins Attack with the registered Buff contribution.");
                using (var action = shooterSkill.CreateAction(PNSkillEffectsNativeProbe.Attack, out var freezeContext, new[] { receiver }))
                { action.Start(freezeContext); plan = shooterSkill.Effects.FreezeOnHit(freezeContext); action.Cancel(freezeContext); }
                shooterSkill.End(); shooterBuffs.Remove(shooterGrant.Handle);
                var shooterSource = PNCombatSource.Capture(shooterStat); var shooterId = shooterStat.EntityId;
                Check(plan.Effects.Count == 1 && plan.Skipped.Count == 0, "FreezeOnHit keeps the Buff-granted Burn OnHit effect.");
                shooter.GetComponent<EveEntityLoader>().Unload(); UnityEngine.Object.DestroyImmediate(shooter);
                var report = plan.Execute(new PNSkillEffectsNativeHost(hub, shooterSource, new[] { receiver }), receiver, receiver.transform.position, Vector3.right, 0, new PNSkillHitAdmission());
                var frozenBurn = buffs.Snapshot().FirstOrDefault(x => x.SourceId == shooterId);
                Check(report.Executed == 1 && report.Errors.Count == 0 && frozenBurn != null && frozenBurn.Stacks == 1, "Frozen Burn applies with the destroyed shooter's attribution.");
                if (frozenBurn != null) buffs.Remove(frozenBurn.Handle, PNBuffEndReason.Bypass, true);
            }
            var live = state.Contribute("live", new PNSkillContribution(Rule("Live", "OnHit", "HitTarget"), hub));
            skill.Begin(PNSkillEffectsNativeProbe.Attack);
            using (var action = skill.CreateAction(PNSkillEffectsNativeProbe.Attack, out var context, new[] { receiver }))
            { action.Start(context); live.Dispose(); action.Perform(context); Check(buffs.Snapshot()[0].Stacks == 1, "Real Live removal suppresses next hit effect."); action.Exit(context); }
            skill.End();
            using (state.Contribute("activation", new PNSkillContribution(Rule("Snapshot", "OnActivated", "InvocationTargets"), hub)))
            {
                int activations = 0; Action<PNSkillInvocation, string> watch = (_, key) => { if (key == "OnActivated") activations++; }; state.Triggered += watch;
                skill.Begin(PNSkillEffectsNativeProbe.SkillEntry); skill.Commit(receiver); skill.Commit(receiver);
                Check(activations == 1 && buffs.Snapshot()[0].Stacks == 2, "Buff-only activation grants Burn once, without hit."); state.Triggered -= watch; skill.End();
            }
            var speedOwner = EveId.Parse("test:native_speed");
            skill.Begin(PNSkillEffectsNativeProbe.Attack); stat.SetSource(speedOwner, new[] { new EveStatModifier(PNContentIds.AttackSpeed, 50, 0) });
            Check(skill.Speed == 1.5f, "Native execution speed reads current stat."); stat.SetSource(speedOwner, new[] { new EveStatModifier(PNContentIds.AttackSpeed, -100, 0) });
            Check(skill.Speed == 0 && !skill.CurrentInvocation.IsEnded, "Zero speed pauses current invocation."); skill.End();
            Check(!skill.Begin(PNSkillEffectsNativeProbe.Attack) && skill.Begin(PNSkillEffectsNativeProbe.SkillEntry), "Zero speed blocks only flagged activations."); skill.End(); stat.RemoveSource(speedOwner);
            var body = caster.AddComponent<Rigidbody>(); body.useGravity = false;
            JObject Motion(string ownership) => JObject.Parse("{'readMode':'Live','effects':[{'trigger':'OnActivated','kind':'Action','id':'base:motion_action','data':{'perform':{'distance':1,'duration':1,'ignoreEntities':true,'permissionId':'base:motion_voluntary'}},'direction':'Invocation','ownership':'" + ownership + "','affectedByAttackSpeed':false}]}");
            using (state.Contribute("parent motion", new PNSkillContribution(Motion("Parent"), hub)))
            {
                skill.Begin(PNSkillEffectsNativeProbe.SkillEntry); skill.Commit(); skill.Tick(.25f); float moved = body.position.x;
                Check(moved > .2f && !skill.Controls.IsAllowed(PNControlIds.Move), "Registry-created auxiliary Action advances real Rigidbody and owns locks.");
                skill.End(true); Check(skill.Controls.IsAllowed(PNControlIds.Move), "Parent cancellation returns auxiliary motion lease."); skill.Tick(.25f); Check(body.position.x == moved, "Cancelled auxiliary does not advance further.");
            }
            using (state.Contribute("independent motion", new PNSkillContribution(Motion("Independent"), hub)))
            {
                skill.Begin(PNSkillEffectsNativeProbe.SkillEntry); skill.Commit(); skill.End(); float start = body.position.x;
                skill.Tick(.5f); Check(body.position.x > start + .4f, "Independent auxiliary survives parent completion."); skill.Tick(.5f);
                Check(skill.Controls.IsAllowed(PNControlIds.Move), "Independent completion releases owned locks.");
            }
            var external = state.Contribute("external", new PNSkillContribution(Rule("Live", "OnHit", "HitTarget"), hub));
            skill.enabled = false; Check(external.IsActive, "Skill disable retains contribution owner while cancelling execution."); skill.enabled = true;
            caster.GetComponent<EveEntityLoader>().Unload(); Check(!external.IsActive && state.IsDisposed, "Native unload retires effect generation."); external.Dispose();
        }
        finally { try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); } finally { EditorSceneManager.ClosePreviewScene(preview); } }
        Check(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "Authored scene/dirty state preserved.");
        string summary = "PASS " + checks + " native Skill effects checks"; Directory.CreateDirectory(".utmp/SkillEffectsReview"); File.WriteAllLines(".utmp/SkillEffectsReview/NativeResults.txt", evidence.Concat(new[] { summary })); return summary;
    }
}
#endif
