using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static EveSessionSmokeTests;

public class PNSkillEffectsProbe : PNSkill
{
    public static readonly EveId Attack = EveId.Parse("test:effect_attack"), SkillEntry = EveId.Parse("test:effect_skill"), Hybrid = EveId.Parse("test:effect_hybrid"), SlowIndependent = EveId.Parse("test:effect_independent");
    public override IEnumerable<EveId> BuiltInSkillIds => new[] { Attack, SkillEntry, Hybrid, SlowIndependent };
    public bool Begin(EveId id) { if (!CanActivateSkill(id)) return false; BeginActivation(id); return true; }
    public void Commit(IEnumerable<PNStat> targets = null) => CommitActivation(targets, Vector3.right);
    public void End() => EndActivation();
    public float Speed => ExecutionSpeed;
    public void DisableExecution() => OnDisable();
}
public class PNSkillEffectsModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    {
        hub.GetOrCreate<PNTagDefinition>().Register(new PNTagDefinition(EveId.Parse("test:projectile_damage")));
        foreach (var name in new[] { "projectile", "charged" })
            hub.GetOrCreate<PNSkillCategoryDefinition>().Register(new PNSkillCategoryDefinition(
                EveId.Parse("test:" + name), PNControlIds.Skill, EveId.Parse("test:projectile_damage")));
        JObject Data(string[] categories, bool speed) => JObject.Parse("{'categoryIds':[], 'affectedByAttackSpeed':false,'action':{'id':'base:damage_action','perform':{'value':{'amount':1},'tags':['base:true'],'canCrit':false,'iframeDuration':0}}}");
        void Add(EveId id, string[] categories, bool speed, bool action)
        {
            var data = Data(categories, speed); data["categoryIds"] = new JArray(categories.Select(x => "base:" + x)); data["affectedByAttackSpeed"] = speed;
            if (!action) data.Remove("action"); hub.Skills.Register(new EveSkillDefinition(id, data));
        }
        Add(PNSkillEffectsProbe.Attack, new[] { "attack" }, true, true);
        Add(PNSkillEffectsProbe.SkillEntry, new[] { "skill" }, false, false);
        Add(PNSkillEffectsProbe.Hybrid, new[] { "attack", "skill" }, true, true);
        Add(PNSkillEffectsProbe.SlowIndependent, new[] { "attack" }, false, true);
    }
}
public class PNSkillEffectsRuntime : EveGameRuntime
{
    protected override IEnumerable<EveIContentModule> CreateContentModules()
    { foreach (var module in base.CreateContentModules()) yield return module; yield return new PNSkillEffectsModule(); }
}
public static class PNSkillEffectsTests
{
    public static JObject Effect(string trigger, string target, string scope = "PerTarget")
        => JObject.Parse("{'trigger':'" + trigger + "','kind':'Operation','id':'base:apply_buff','data':{'buffId':'base:burn'},'target':'" + target + "','hitScope':'" + scope + "'}");
    public static JObject Rule(string mode, string category, JObject effect)
        => new JObject { ["readMode"] = mode, ["categoryIds"] = new JArray("base:" + category), ["effects"] = new JArray(effect) };
    private static (EveTestLoader loader, PNStat stat, PNSkillEffectsProbe skill) Entity(long id)
    {
        var loader = new EveTestLoader(); var stat = new EveCharacterStat(); var skill = new PNSkillEffectsProbe(); var identity = new EveEntityIdentity(); identity.Assign(new EveEntityId(id), EveId.Parse("project_n:eve"), 0);
        MonoBehaviour[] components = { loader, identity, stat, skill, new PNAbilityManager() }; foreach (var component in components) { component.Components = components; component.gameObject.SetActive(true); } return (loader, stat, skill);
    }
    public static void Run()
    {
        var runtime = new PNSkillEffectsRuntime(); runtime.Initialize(); var hub = EveRegistrySession.Current;
        var source = Entity(982001); var target = Entity(982002); var other = Entity(982003);
        try
        {
            source.loader.Load(hub); target.loader.Load(hub); other.loader.Load(hub);
            var effects = source.skill.Effects; var buffs = target.stat.GetComponent<PNBuffManager>().Buffs; var ownerBuffs = source.stat.GetComponent<PNBuffManager>().Buffs;
            var burnId = EveId.Parse("base:burn"); var tuning = EveId.Parse("test:effects_stats");
            target.stat.SetSource(tuning, new[] { new EveStatModifier(PNContentIds.Health, 10000, 0) }); target.stat.SetCurrent(PNContentIds.Health, target.stat.GetMax(PNContentIds.Health));
            Check(hub.SkillCategories().Snapshot().Count == 7 && hub.ControlPermissions().Get(PNControlIds.Skill).Id == PNControlIds.Skill, "Categories have separate registered permission mappings, including mod categories");
            CategoryDamage(hub, source.skill, source.stat, target.stat);
            Reject(() => hub.ControlPermissions().Get(EveId.Parse("base:skill_cast")), "Removed skill_cast alias is unavailable");
            var metadata = PNSkillMetadata.Parse(hub.Skills.Get(PNSkillEffectsProbe.Hybrid).Data, hub);
            Check(metadata.CategoryIds.Count == 2 && metadata.AffectedByAttackSpeed, "Hybrid definition carries array and explicit speed flag");
            Reject(() => PNSkillMetadata.Parse(JObject.Parse("{'categoryIds':['base:attack']}"), hub), "Activation requires explicit speed flag");
            Reject(() => PNSkillMetadata.Parse(JObject.Parse("{'categoryIds':[],'affectedByAttackSpeed':true}"), hub), "Empty authored categories rejected");
            Reject(() => new PNSkillContribution(JObject.Parse("{'readMode':'Live','categoryIds':['base:attack'],'addCategories':['base:skill']}"), hub), "Circular modifier category filters rejected");
            Reject(() => new PNSkillContribution(Rule("Live", "attack", Effect("OnActivated", "HitTarget")), hub), "Hit target selector on activation rejected");
            using (source.skill.Controls.Acquire("silence", new[] { PNControlIds.Skill }))
            {
                Check(!source.skill.Begin(PNSkillEffectsProbe.Hybrid) && source.skill.Begin(PNSkillEffectsProbe.Attack), "Hybrid checks every category right; ordinary attack remains allowed"); source.skill.End();
                using (effects.Contribute("classify", new PNSkillContribution(JObject.Parse("{'readMode':'Live','addCategories':['base:skill']}"), hub)))
                    Check(!source.skill.Begin(PNSkillEffectsProbe.Attack), "Dynamically added category also gates activation");
            }
            using (effects.Contribute("empty", new PNSkillContribution(JObject.Parse("{'readMode':'Live','suppressCategories':['base:attack']}"), hub)))
                Check(!source.skill.Begin(PNSkillEffectsProbe.Attack), "Effective empty categories cannot activate");
            source.stat.SetSource(tuning, new[] { new EveStatModifier(PNContentIds.AttackSpeed, -100, 0) });
            Check(!source.skill.Begin(PNSkillEffectsProbe.Attack) && source.skill.Begin(PNSkillEffectsProbe.SlowIndependent), "Explicit speed flag controls zero-speed admission independently of attack category"); source.skill.End();
            source.stat.RemoveSource(tuning); Check(source.skill.Begin(PNSkillEffectsProbe.Attack), "Attack prepares with positive speed");
            source.stat.SetSource(tuning, new[] { new EveStatModifier(PNContentIds.AttackSpeed, 50, 0) }); Check(source.skill.Speed == 1.5f, "Running invocation reads speed live");
            source.stat.SetSource(tuning, new[] { new EveStatModifier(PNContentIds.AttackSpeed, -100, 0) }); Check(source.skill.Speed == 0, "Live speed zero pauses without ending invocation"); source.skill.End(); source.stat.RemoveSource(tuning);
            var granted = ownerBuffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_skill_effect_template"), PNBuffSource.Capture(source.stat)));
            Check(granted.Handle.IsAlive && source.skill.Begin(PNSkillEffectsProbe.Attack), "Registered Buff grants Attack OnHit contribution");
            using (var action = source.skill.CreateAction(PNSkillEffectsProbe.Attack, out var context, new[] { target.stat }))
            {
                action.Start(context); source.skill.Commit(); ownerBuffs.Remove(granted.Handle); action.Perform(context);
                Check(buffs.Snapshot().Single().Id == hub.Buffs().ResolveIdentity(burnId), "Snapshot OnHit remains after granting Buff removal");
                Check(context.EntrySkillId == PNSkillEffectsProbe.Attack && context.InvocationId > 0 && context.CategoryIds.Contains(EveId.Parse("project_n:attack")), "Action carries entry/category/invocation identity"); action.Exit(context);
            }
            source.skill.End();
            var live = effects.Contribute("live", new PNSkillContribution(Rule("Live", "attack", Effect("OnHit", "HitTarget")), hub));
            source.skill.Begin(PNSkillEffectsProbe.Attack);
            using (var action = source.skill.CreateAction(PNSkillEffectsProbe.Attack, out var context, new[] { target.stat }))
            {
                action.Start(context); live.Dispose(); action.Perform(context);
                Check(buffs.Snapshot().Single().Stacks == 1, "Live removal before hit removes effect"); action.Exit(context);
            }
            source.skill.End(); source.skill.Begin(PNSkillEffectsProbe.Attack);
            using (var action = source.skill.CreateAction(PNSkillEffectsProbe.Attack, out var context, new[] { target.stat }))
            {
                action.Start(context); live = effects.Contribute("live again", new PNSkillContribution(Rule("Live", "attack", Effect("OnHit", "HitTarget")), hub));
                Action<PNDamageResult> removeDuringHit = _ => live.Dispose(); target.stat.DamageReceived += removeDuringHit;
                try { action.Perform(context); } finally { target.stat.DamageReceived -= removeDuringHit; }
                Check(buffs.Snapshot().Single().Stacks == 2, "Live effects captured once before damage callbacks mutate contributions"); action.Exit(context);
            }
            source.skill.End();
            using (effects.Contribute("activation", new PNSkillContribution(Rule("Snapshot", "skill", Effect("OnActivated", "InvocationTargets")), hub)))
            {
                int activation = 0; Action<PNSkillInvocation, string> watch = (_, trigger) => { if (trigger == "OnActivated") activation++; }; effects.Triggered += watch;
                source.skill.Begin(PNSkillEffectsProbe.SkillEntry); source.skill.Commit(new[] { target.stat }); source.skill.Commit(new[] { target.stat });
                Check(activation == 1 && buffs.Snapshot().Single().Stacks == 3, "Buff-only Skill activation fires once without an Action or hit"); effects.Triggered -= watch; source.skill.End();
            }
            using (effects.Contribute("tags", new PNSkillContribution(JObject.Parse("{'readMode':'Live','addTags':['base:fire']}"), hub)))
            {
                source.skill.Begin(PNSkillEffectsProbe.Attack); using (var action = source.skill.CreateAction(PNSkillEffectsProbe.Attack, out var context, new[] { target.stat }))
                { action.Start(context); action.Perform(context); Check(((PNDamageAction)action).Results[0].Damage.Tags.Contains(PNContentIds.Fire), "Live tag modifier reaches actual damage packet"); action.Exit(context); } source.skill.End();
            }
            using (effects.Contribute("hybrid", new PNSkillContribution(Rule("Live", "attack", Effect("OnHit", "HitTarget")), hub)))
            {
                source.skill.Begin(PNSkillEffectsProbe.Hybrid); using (var action = source.skill.CreateAction(PNSkillEffectsProbe.Hybrid, out var context, new[] { target.stat }))
                { action.Start(context); action.Perform(context); Check(buffs.Snapshot().Single().Stacks == 4, "Any filter executes once for a hybrid trigger"); action.Exit(context); } source.skill.End();
            }
            var allRule = Rule("Live", "attack", Effect("OnHit", "HitTarget")); allRule["categoryIds"] = new JArray("base:attack", "base:skill"); allRule["match"] = "All";
            using (effects.Contribute("all categories", new PNSkillContribution(allRule, hub)))
            {
                source.skill.Begin(PNSkillEffectsProbe.Hybrid); using (var action = source.skill.CreateAction(PNSkillEffectsProbe.Hybrid, out var context, new[] { target.stat }))
                { action.Start(context); action.Perform(context); Check(buffs.Snapshot().Single().Stacks == 5, "All filter matches complete hybrid category set once"); action.Exit(context); } source.skill.End();
            }
            var perHit = Rule("Live", "attack", Effect("OnHit", "Self", "PerHit")); var sameValue = new PNSkillContribution(perHit, hub);
            using (effects.Contribute("first independent", sameValue)) using (effects.Contribute("second independent", sameValue))
            {
                source.skill.Begin(PNSkillEffectsProbe.Attack); using (var action = source.skill.CreateAction(PNSkillEffectsProbe.Attack, out var context, new[] { target.stat, other.stat }))
                { action.Start(context); action.Perform(context); Check(ownerBuffs.Snapshot().Single(x => x.Id == hub.Buffs().ResolveIdentity(burnId)).Stacks == 2, "PerHit self effect deduplicates targets without deduplicating independent owners"); action.Exit(context); } source.skill.End();
            }
            var external = effects.Contribute("survives Skill disable", new PNSkillContribution(Rule("Live", "attack", Effect("OnHit", "HitTarget")), hub));
            source.skill.Begin(PNSkillEffectsProbe.Attack); var ended = source.skill.CurrentInvocation; source.skill.DisableExecution();
            Check(ended.IsEnded && external.IsActive, "Skill disable cancels execution but retains external contribution ownership");
            source.loader.Unload(); Check(!external.IsActive && effects.IsDisposed, "Unload invalidates state and contribution leases"); external.Dispose();
            source.loader.Load(hub); Check(!ReferenceEquals(source.skill.Effects, effects) && source.skill.Effects.GenerationId != effects.GenerationId, "Reload creates independent effect generation");
        }
        finally { source.loader.Unload(); target.loader.Unload(); other.loader.Unload(); EveRegistrySession.End(); }
    }
    private static void CategoryDamage(EveRegistryHub hub, PNSkillEffectsProbe skill, PNStat source, PNStat target)
    {
        var attack = PNContentIds.AttackTag; var bonus = EveId.Parse("test:category_bonus");
        foreach (var name in new[] { "attack", "skill", "ultimate", "dash", "burst" })
        {
            var category = hub.SkillCategories().Get(EveId.Parse("base:" + name)); category.Validate(hub);
            Check(hub.Tags().ResolveIdentity(category.DamageTagId.Value) == EveId.Parse("project_n:" + name), "Default mapping " + name);
            Check(!hub.Tags().TryGet(EveId.Parse("base:" + name + "_damage"), out _) && !hub.Tags().TryGet(EveId.Parse("project_n:" + name + "_damage"), out _), "Legacy category tag IDs removed " + name);
            var selector = EveId.Parse("base:" + name);
            source.SetSource(bonus, new[] { new EveStatModifier(selector, .2d, 1, PNContentIds.Scale) });
            target.SetSource(bonus, new[] { new EveStatModifier(selector, .25d, 1, PNContentIds.Mitigation) });
            var tagged = source.CaptureCombatSource().CreateDamage(100d, false, 0d, PNContentIds.True, selector);
            Check(Math.Abs(tagged.Amount - 120d) < 1e-9 && tagged.Tags.Contains(EveId.Parse("project_n:" + name)), "Renamed category selector applies outgoing bonus " + name);
            Check(Math.Abs(target.Calculate(PNContentIds.Mitigation, tagged.Amount, selector) - 150d) < 1e-9, "Renamed category selector applies incoming mitigation " + name);
            source.RemoveSource(bonus); target.RemoveSource(bonus);
        }
        new PNSkillCategoryDefinition(EveId.Parse("test:unmapped"), PNControlIds.Skill).Validate(hub);
        Check(new PNSkillCategoryDefinition(EveId.Parse("test:unmapped"), PNControlIds.Skill).DamageTagId == null, "Old category constructor remains unmapped");
        Reject(() => new PNSkillCategoryDefinition(EveId.Parse("test:bad"), PNControlIds.Skill, EveId.Parse("test:missing")).Validate(hub), "Missing linked tag rejected");
        Reject(() => new PNSkillCategoryDefinition(EveId.Parse("test:bad"), PNControlIds.Skill, EveId.Parse("base:physical")).Validate(hub), "Linked damage type rejected");
        var isolated = new EveRegistryHub(); new PNContentModule().Register(isolated);
        isolated.GetOrCreate<PNTagDefinition>().Register(new PNTagDefinition(PNContentIds.AttackPower));
        isolated.Get<PNTagDefinition>().RegisterBase(EveId.Parse("base:attack_power"), PNContentIds.AttackPower, "project_n");
        Reject(() => new PNSkillCategoryDefinition(EveId.Parse("test:bad"), PNControlIds.Skill, EveId.Parse("base:attack_power")).Validate(isolated), "Linked stat alias rejected");
        Reject(() => new PNSkillCategoryDefinition(EveId.Parse("test:bad"), PNControlIds.Skill, PNContentIds.AttackPower).Validate(isolated), "Linked stat canonical ID rejected");
        isolated.GetOrCreate<PNTagDefinition>().Register(new PNTagDefinition(EveId.Parse("test:late")));
        new PNSkillCategoryDefinition(EveId.Parse("test:valid"), PNControlIds.Skill, EveId.Parse("test:late")).Validate(isolated);
        Check(true, "Mapping validates after tag registration completes");
        Check(skill.Begin(PNSkillEffectsProbe.Attack), "Category damage invocation begins");
        var data = skill.GetData(PNSkillEffectsProbe.Attack);
        using (var action = skill.CreateAction(PNSkillEffectsProbe.Attack, out var context, new[] { target }))
        {
            action.Start(context);
            Check(context.EffectiveTags().Contains(attack), "Main Action automatically maps category");
            var auxiliary = new PNSkillActionContext(skill, PNSkillEffectsProbe.Attack, data, hub, null, null, skill.CurrentInvocation, false, null,
                new[] { EveId.Parse("test:projectile"), EveId.Parse("test:charged") });
            Check(auxiliary.EffectiveTags().Count(x => x == EveId.Parse("test:projectile_damage")) == 1, "Mod categories share one deduplicated tag on auxiliary context");
            using (skill.Effects.Contribute("duplicates", new PNSkillContribution(JObject.Parse("{'readMode':'Live','addTags':['base:attack','project_n:attack']}"), hub)))
                Check(context.EffectiveTags().Count(x => x == attack) == 1, "Mapped and contribution aliases deduplicate");
            using (skill.Effects.Contribute("suppress", new PNSkillContribution(JObject.Parse("{'readMode':'Live','suppressTags':['base:attack']}"), hub)))
            {
                Check(!context.EffectiveTags().Contains(attack), "Tag suppression wins over category mapping");
                Check(auxiliary.EffectiveTags().Contains(EveId.Parse("test:projectile_damage")), "Auxiliary does not replay parent tag contributions");
            }
            using (skill.Effects.Contribute("reclassify", new PNSkillContribution(JObject.Parse("{'readMode':'Live','addCategories':['base:skill'],'suppressCategories':['base:attack']}"), hub)))
                Check(!context.EffectiveTags().Contains(attack) && context.EffectiveTags().Contains(PNContentIds.SkillTag), "Live effective categories drive mapping");
            source.SetSource(bonus, new[] { new EveStatModifier(attack, .2d, 1, PNContentIds.Scale) });
            target.SetSource(bonus, new[] { new EveStatModifier(attack, .25d, 1, PNContentIds.Mitigation) });
            var capture = source.CaptureCombatSource();
            var packet = capture.CreateDamage(100d, false, 0d, PNContentIds.True);
            source.RemoveSource(bonus);
            var result = context.ReceiveHit(target, null, packet, 100d, capture);
            Check(result.Damage.Tags.Contains(attack) && Math.Abs(result.Damage.Amount - 120d) < 1e-9, "Outgoing category bonus uses source snapshot exactly once");
            Check(Math.Abs(result.CalculatedDamage - 150d) < 1e-9, "Receiver receives category tag and applies mitigation");
            var critical = new PNDamage(packet.SessionId, packet.SourceId, 150d, packet.Tags, 0d, true, true, 100d, 150d);
            var classified = context.ReclassifyDamage(critical, 100d, capture);
            Check(classified.IsCritical && Math.Abs(classified.Amount - 180d) < 1e-9, "Category reclassification preserves crit without multiplying twice");
            action.Cancel(context);
        }
        skill.End(); target.RemoveSource(bonus);
        var snapshot = skill.Effects.Contribute("snapshot category", new PNSkillContribution(JObject.Parse("{'readMode':'Snapshot','addCategories':['base:skill']}"), hub));
        skill.Begin(PNSkillEffectsProbe.Attack);
        using (var action = skill.CreateAction(PNSkillEffectsProbe.Attack, out var context))
        { action.Start(context); snapshot.Dispose(); Check(context.EffectiveTags().Contains(PNContentIds.SkillTag), "Category mapping preserves BeginAction Snapshot after lease removal"); action.Cancel(context); }
        skill.End();
        var first = new EveRegistryHub(); new PNContentModule().Register(first); first.GetOrCreate<PNSkillCategoryDefinition>().Register(new PNSkillCategoryDefinition(EveId.Parse("test:hash"), PNControlIds.Skill)); first.Seal();
        var second = new EveRegistryHub(); new PNContentModule().Register(second);
        second.GetOrCreate<PNSkillCategoryDefinition>().Register(new PNSkillCategoryDefinition(EveId.Parse("test:hash"), PNControlIds.Skill, attack)); second.Seal();
        Check(EveContentCompatibility.Create(first, Array.Empty<EveContentDocument>(), "test:rules").ContentHashes["registry:" + typeof(PNSkillCategoryDefinition).FullName] !=
            EveContentCompatibility.Create(second, Array.Empty<EveContentDocument>(), "test:rules").ContentHashes["registry:" + typeof(PNSkillCategoryDefinition).FullName], "Changing only category mapping changes compatibility hash");
    }
}
