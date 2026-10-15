using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static EveSessionSmokeTests;

public class PNSeparateValuesOperation : PNSkillOperation
{
    public double First, Second;
    public override void Execute(PNSkillActionContext context, JToken data)
    {
        var values = PNSkillValues.Parse(data["value"]);
        if (values.Count != 2) throw new FormatException("Probe expects two components.");
        var source = context.Skill.GetComponent<PNStat>();
        First = values[0].Evaluate(source); Second = values[1].Evaluate(source);
    }
}

public class PNFailingHealReceiver : PNStat
{
    public override PNHealResult ReceiveHeal(PNHeal heal) => throw new InvalidOperationException("receiver probe");
}

public class PNHealOverrideStat : PNStat
{
    public override double Calculate(EveId id, double amount, params EveId[] ids)
        => base.Calculate(id, amount, ids) * (ids.Contains(PNContentIds.Heal) ? 2d : 1d);
}

public class PNHealSelectedScale : PNScaleCalculation
{
    public override double Calculate(PNStatCalculationContext context)
        => base.Calculate(context) + (context.Ids.Contains(PNContentIds.Heal) ? 10d : 0d);
}

public class PNSelectedHealOperation : PNHealOperation
{
    public bool Selected;
    public override void Execute(PNSkillActionContext context, JToken data)
    { Selected = true; base.Execute(context, data); }
}

public static class PNHealTests
{
    private static readonly EveId Hero = EveId.Parse("test:heal_hero");
    private static readonly EveId Skill = EveId.Parse("test:heal_context");
    private static readonly EveId Buff = EveId.Parse("test:heal_buff");
    private static readonly EveId Separate = EveId.Parse("test:separate_values");
    private const string Parts = "[{\"amount\":500,\"statId\":\"base:attack_power\"},{\"amount\":50,\"statId\":\"base:defense\"},{\"amount\":100}]";

    public static void Run()
    {
        ValueComponents();
        HealFlow();
        OperationSafety();
        SessionAndOverrides();
        ReferenceValidation();
    }

    private static void ValueComponents()
    {
        var single = PNSkillValues.Parse(JObject.Parse("{\"amount\":42}"));
        Check(single.Count == 1 && single.RequireSingle().Evaluate(null) == 42d, "single JSON normalizes to one component");
        var raw = JArray.Parse("[{\"amount\":10},{\"amount\":-2},{\"amount\":10}]");
        var values = PNSkillValues.Parse(raw); raw[0]["amount"] = 999;
        Check(values.Count == 3 && values.EvaluateEach(null).SequenceEqual(new[] { 10d, -2d, 10d }),
            "components preserve order, duplicates, signs and source isolation without aggregation");
        Reject(() => ((IList<PNSkillValue>)values.Components)[0] = new PNSkillValue(99d), "component list is immutable");
        Reject(() => values.RequireSingle(), "single access cannot truncate a composite value");
        foreach (var bad in new[] { "[]", "null", "[null]", "[[{\"amount\":1}]]", "[1]", "[{\"amount\":1},{}]", "[{\"amount\":1,\"unknown\":2}]", "[{\"amount\":1e400}]" })
            Reject(() => PNSkillValues.Parse(JToken.Parse(bad)), "invalid composite payload rejected");
        var hub = Hub(); var stat = new PNStat(); stat.Initialize(hub.Characters.Get(Hero), hub);
        var scale = PNSkillValues.Parse(JArray.Parse(Parts));
        Check(scale.EvaluateEach(stat).SequenceEqual(new[] { 500d, 250d, 100d }), "multiple scaling stats return separate results");
        stat.SetSource(Buff, new[] { new EveStatModifier(PNContentIds.AttackPower, 1d, 1) });
        Check(scale.EvaluateEach(stat).SequenceEqual(new[] { 1000d, 250d, 100d }), "value components evaluate current stat modifiers");
        Reject(() => PNSkillValues.Parse(JArray.Parse("[{\"amount\":1},{\"amount\":100,\"statId\":\"addon:missing\"}]")).ValidateStats(hub),
            "every scaling stat including later components is validated");
        var resource = PNSkillValues.Parse(JArray.Parse("[{\"amount\":10,\"statId\":\"base:health\"},{\"amount\":1}]"));
        stat.SetCurrent(PNContentIds.Health, 50d);
        Check(resource.EvaluateEach(stat).SequenceEqual(new[] { 200d, 1d }), "resource components still scale from maximum");
        Reject(() => PNHealSettings.Parse(JObject.Parse("{\"value\":[{\"amount\":10},{\"amount\":-1}]}")), "heal operation rejects negative coefficient in later component");
        stat.Unload();
    }

    private static void HealFlow()
    {
        var hub = Hub(); Start(hub, EveSessionRole.Offline);
        var source = Entity(5101); var target = Entity(5102); var other = Entity(5103);
        try
        {
            source.loader.Load(hub); target.loader.Load(hub); other.loader.Load(hub);
            target.stat.SetCurrent(PNContentIds.Health, 400d); other.stat.SetCurrent(PNContentIds.Health, 400d);
            source.stat.SetSource(Buff, new[] {
                new EveStatModifier(PNContentIds.Heal, .2d, 1), new EveStatModifier(PNContentIds.Heal, 100d, 0),
                new EveStatModifier(PNContentModule.Fire, 9d, 1)
            });
            target.stat.SetSource(Buff, new[] { new EveStatModifier(PNContentIds.Heal, -.5d, 1, PNContentIds.Mitigation) });
            Check(source.stat.Evaluate(PNContentIds.Heal) == 0d && !source.stat.Set.TryGetBase(PNContentIds.Heal, out _),
                "heal modifier ID needs no entity base and Evaluate ignores heal flat");
            var packet = source.stat.CreateHeal(1000d);
            Check(packet.Amount == 1300d && packet.BaseAmount == 1000d && packet.SourceId == source.stat.EntityId,
                "heal snapshot applies Scale percentages then flat once without fire damage bonuses");
            int events = 0, damageEvents = 0;
            target.stat.HealReceived += result => { events++; Check(result.HealthAfter == target.stat.GetCurrent(PNContentIds.Health), "heal observers see committed HP"); };
            target.stat.DamageReceived += result => damageEvents++;
            var result = target.stat.ReceiveHeal(packet);
            Check(result.AppliedHeal == 650d && result.HealthBefore == 400d && result.HealthAfter == 1050d && result.Overheal == 0d,
                "heal uses receiving Mitigation without DEF and returns actual HP gain");
            Check(events == 1 && damageEvents == 0 && target.stat.IFrameRemaining == 0d, "heal has its own event and leaves iframe untouched");
            target.stat.SetCurrent(PNContentIds.Health, 1950d);
            result = target.stat.ReceiveHeal(packet);
            Check(result.AppliedHeal == 50d && result.Overheal == 600d && result.HealthAfter == 2000d, "heal caps at maximum and records overheal");
            result = target.stat.ReceiveHeal(packet);
            Check(result.AppliedHeal == 0d && result.Overheal == 650d && result.Status == PNHealStatus.NoMissingHealth,
                "full-health target reports zero applied heal");
            target.stat.SetCurrent(PNContentIds.Health, 0d);
            result = target.stat.ReceiveHeal(packet);
            Check(result.Status == PNHealStatus.DeadTarget && result.AppliedHeal == 0d && result.Overheal == 0d && target.stat.IsDead,
                "heal does not resurrect or misreport rejected heal as overheal");
            target.stat.SetCurrent(PNContentIds.Health, 400d);
            target.stat.SetSource(Buff, new[] { new EveStatModifier(PNContentIds.Heal, -2d, 1, PNContentIds.Mitigation) });
            Check(target.stat.ReceiveHeal(packet).Status == PNHealStatus.ZeroAmount, "receiving modifiers can suppress healing completely");
            target.stat.RemoveSource(Buff);
            Reject(() => source.stat.CreateHeal(-1d), "negative base heal rejected");
            Reject(() => source.stat.CreateHeal(double.NaN), "NaN heal rejected");
            Reject(() => new PNHeal(Guid.Empty, source.stat.EntityId, 1d), "empty heal session rejected");
            Reject(() => new PNHeal(source.stat.SessionId, default, 1d), "invalid heal source rejected");
            Reject(() => target.stat.ReceiveHeal(new PNHeal(Guid.NewGuid(), source.stat.EntityId, 1d)), "foreign heal session rejected");
            Reject(() => target.stat.ReceiveHeal(null), "null heal packet rejected");
            Reject(() => target.stat.SetSource(Buff, new[] { new EveStatModifier(PNContentIds.Heal, 10d, 0, PNContentIds.Mitigation, EveId.Parse("test:missing_flat")) }),
                "incoming heal rejects unknown flat definition");
            source.stat.SetSource(Buff, new[] { new EveStatModifier(PNContentIds.Heal, double.MaxValue, 0) });
            double before = target.stat.GetCurrent(PNContentIds.Health);
            Reject(() => source.stat.SendHeal(target.stat, double.MaxValue), "heal overflow rejected before HP mutation");
            Check(target.stat.GetCurrent(PNContentIds.Health) == before, "rejected heal preserves HP");
            source.stat.SetSource(Buff, new[] { new EveStatModifier(PNContentIds.Heal, .2d, 1), new EveStatModifier(PNContentIds.Heal, 100d, 0) });
            source.skill.CreateAction(Skill, out var context, new[] { target.stat, target.stat, other.stat }).Dispose();
            using (var operation = (PNHealOperation)hub.Operations().Get(EveId.Parse("base:heal")).Create())
            {
                operation.Execute(context, context.Data.Action.Perform as JObject is JObject payload ? new JObject { ["value"] = payload["value"] } : null);
                Check(operation.Heal.BaseAmount == 850d && operation.Heal.Amount == 1120d && operation.Results.Count == 2,
                    "registered heal operation explicitly sums components and deduplicates targets");
                Check(operation.Results.All(value => ReferenceEquals(value.Heal, operation.Heal)), "heal operation shares one sender snapshot");
                Reject(() => operation.Execute(context, JObject.Parse("{\"value\":{\"amount\":1}}")), "heal operation cannot replay");
            }
            var separate = (PNSeparateValuesOperation)hub.Operations().Get(Separate).Create();
            separate.Execute(context, JObject.Parse("{\"value\":[{\"amount\":500,\"statId\":\"base:attack_power\"},{\"amount\":50,\"statId\":\"base:defense\"}]}"));
            Check(separate.First == 500d && separate.Second == 250d, "another registered operation keeps components separate instead of summing");
            separate.Dispose();
            using (var damage = (PNDamageAction)source.skill.CreateAction(Skill, out var damageContext, new[] { other.stat }))
            {
                damage.Start(damageContext); damage.Perform(damageContext);
                Check(damage.Damage.Amount == 850d, "damage action explicitly sums same components without applying heal modifiers");
            }
            source.stat.SetCurrent(PNContentIds.Health, 1500d);
            Check(source.stat.SendHeal(source.stat, 100d).AppliedHeal == 220d, "self-heal applies sending and receiving selectors independently");
            var delayed = source.stat.CreateHeal(100d);
            source.loader.Unload(); target.stat.SetCurrent(PNContentIds.Health, 100d);
            Check(target.stat.ReceiveHeal(delayed).AppliedHeal == 220d, "delayed heal retains outgoing amount after source unload");
            target.loader.Unload(); target.loader.Load(hub); target.stat.SetCurrent(PNContentIds.Health, 100d);
            int beforeEvents = events;
            target.stat.ReceiveHeal(delayed);
            Check(events == beforeEvents, "unload clears heal event subscriptions");
        }
        finally { source.loader.Unload(); target.loader.Unload(); other.loader.Unload(); EveRegistrySession.End(); }
    }

    private static void SessionAndOverrides()
    {
        var hub = Hub(); Start(hub, EveSessionRole.Client);
        var source = Entity(5201);
        try { source.loader.Load(hub); Reject(() => source.stat.CreateHeal(1d), "client cannot create authoritative heal");
            Reject(() => source.stat.ReceiveHeal(new PNHeal(EveRegistrySession.Context.SessionId, source.stat.EntityId, 1d)), "client cannot receive authoritative heal"); }
        finally { source.loader.Unload(); EveRegistrySession.End(); }
        hub = Hub(true); Start(hub, EveSessionRole.Offline);
        var sender = Entity(5202, new PNHealOverrideStat()); var receiver = Entity(5203);
        try
        {
            sender.loader.Load(hub); receiver.loader.Load(hub); receiver.stat.SetCurrent(PNContentIds.Health, 100d);
            Check(sender.stat.SendHeal(receiver.stat, 100d).AppliedHeal == 220d,
                "heal respects both selected Scale calculator and PNStat Calculate override");
            sender.skill.CreateAction(Skill, out var context, new[] { receiver.stat }).Dispose();
            using (var operation = (PNSelectedHealOperation)hub.Operations().Get(EveId.Parse("base:heal")).Create())
            {
                operation.Execute(context, JObject.Parse("{\"value\":{\"amount\":10}}"));
                Check(operation.Selected && operation.Heal.Amount == 40d, "heal operation respects registered selected implementation");
            }
            sender.stat.SetCurrent(PNContentIds.Health, 0d);
            Reject(() => sender.stat.CreateHeal(1d), "dead sender cannot create new heal");
        }
        finally { sender.loader.Unload(); receiver.loader.Unload(); EveRegistrySession.End(); }
    }

    private static void ReferenceValidation()
    {
        var hub = new EveRegistryHub(); var module = new PNGameplayContentModule();
        new PNContentModule().Register(hub); module.Register(hub);
        hub.Stats.Register(new EveStatDefinition(EveId.Parse("addon:stat"), EveStatKind.Number));
        var data = JObject.Parse("{\"action\":{\"id\":\"base:damage_action\",\"perform\":{\"value\":[{\"amount\":1},{\"amount\":100,\"statId\":\"addon:stat\"}]}}}");
        Reject(() => module.ValidateReferences(Skill, data, "project_n", Array.Empty<string>(), hub), "later value component enforces mod dependency");
        module.ValidateReferences(Skill, data, "project_n", new[] { "addon" }, hub);
        Check(true, "declared dependency allows later value stat reference");
        var phases = new[] { "start", "exit", "cancel" };
        foreach (var phase in phases)
        {
            var phaseData = JObject.Parse("{\"action\":{\"id\":\"base:damage_action\",\"" + phase + "\":{\"value\":[{\"amount\":1},{\"amount\":1,\"statId\":\"addon:stat\"}]}}}");
            Reject(() => module.ValidateReferences(Skill, phaseData, "project_n", Array.Empty<string>(), hub), "all phase value references enforce dependency");
        }
        hub.Skills.Register(new EveSkillDefinition(Skill, JObject.Parse("{\"action\":{\"id\":\"base:damage_action\",\"perform\":{\"value\":[{\"amount\":1},{\"amount\":1,\"statId\":\"addon:unknown\"}],\"tags\":[\"base:true\"]}}}")));
        hub.Seal();
        Reject(() => module.ValidateContent(hub), "startup validation rejects unknown stat in later component");
        Reject(() => PNHealSettings.Parse(JObject.Parse("{\"value\":[{\"amount\":1},{\"amount\":1,\"statId\":\"addon:unknown\"}]}" )).ValidateContent(hub),
            "heal payload exposes startup validation for owning content modules");
    }

    private static void OperationSafety()
    {
        var hub = Hub(); Start(hub, EveSessionRole.Offline);
        var source = Entity(5301); var first = Entity(5302); var second = Entity(5303); var failing = Entity(5304, new PNFailingHealReceiver());
        try
        {
            source.loader.Load(hub); first.loader.Load(hub); second.loader.Load(hub); failing.loader.Load(hub);
            first.stat.SetCurrent(PNContentIds.Health, 100d); second.stat.SetCurrent(PNContentIds.Health, 100d);
            var invalid = new PNStat(); invalid.Initialize(hub.Characters.Get(Hero), hub);
            source.skill.CreateAction(Skill, out var invalidContext, new[] { first.stat, invalid }).Dispose();
            using (var operation = (PNHealOperation)hub.Operations().Get(PNGameplayContentModule.HealOperation).Create())
            {
                Reject(() => operation.Execute(invalidContext, JObject.Parse("{\"value\":{\"amount\":10}}")), "later invalid target rejects heal operation before delivery");
                Check(first.stat.GetCurrent(PNContentIds.Health) == 100d && operation.Heal == null, "invalid target list leaves earlier HP and packet unchanged");
            }
            invalid.Unload();
            source.skill.CreateAction(Skill, out var failContext, new[] { first.stat, failing.stat }).Dispose();
            using (var operation = (PNHealOperation)hub.Operations().Get(PNGameplayContentModule.HealOperation).Create())
            {
                Reject(() => operation.Execute(failContext, JObject.Parse("{\"value\":{\"amount\":10}}")), "later receiver override can fail after an earlier heal commit");
                Check(operation.Results.Count == 1 && first.stat.GetCurrent(PNContentIds.Health) == 110d, "partial operation failure retains successful result");
                Reject(() => operation.Execute(failContext, JObject.Parse("{\"value\":{\"amount\":10}}")), "partial heal cannot replay");
                Check(first.stat.GetCurrent(PNContentIds.Health) == 110d, "failed retry does not duplicate healing");
            }
            int observed = 0;
            first.stat.HealReceived += value => throw new Exception("expected observer probe");
            first.stat.HealReceived += value => observed++;
            Check(source.stat.SendHeal(first.stat, 10d).AppliedHeal == 10d && observed == 1 && first.stat.GetCurrent(PNContentIds.Health) == 120d,
                "observer error leaves committed HP and later listeners intact");
            source.stat.SetSource(Buff, new[] { new EveStatModifier(PNContentIds.Heal, .2d, 1) });
            first.stat.HealReceived += value => source.stat.SetSource(Buff, new[] { new EveStatModifier(PNContentIds.Heal, 9d, 1) });
            source.skill.CreateAction(Skill, out var context, new[] { first.stat, second.stat }).Dispose();
            using (var operation = (PNHealOperation)hub.Operations().Get(PNGameplayContentModule.HealOperation).Create())
            {
                operation.Execute(context, JObject.Parse("{\"value\":{\"amount\":100}}"));
                Check(operation.Results.All(value => value.AppliedHeal == 120d) && second.stat.GetCurrent(PNContentIds.Health) == 220d,
                    "sender modifier changes inside first callback cannot rescale later targets");
            }
            var packet = source.stat.CreateHeal(10d);
            second.stat.SetSource(Buff, new[] { new EveStatModifier(PNContentIds.Heal, .5d, 1, PNContentIds.Mitigation) });
            Check(second.stat.ReceiveHeal(packet).AppliedHeal == 150d, "receiving modifiers are evaluated at delivery time");
            Check(second.stat.ReceiveHeal(new PNHeal(source.stat.SessionId, source.stat.EntityId, 0d)).AppliedHeal == 0d,
                "zero heal packet remains zero without incoming flats");
            using (var operation = (PNHealOperation)hub.Operations().Get(PNGameplayContentModule.HealOperation).Create())
            {
                operation.Cancel(); Reject(() => operation.Execute(context, JObject.Parse("{\"value\":{\"amount\":1}}")), "canceled operation cannot execute");
            }
        }
        finally { source.loader.Unload(); first.loader.Unload(); second.loader.Unload(); failing.loader.Unload(); EveRegistrySession.End(); }
    }

    private static EveRegistryHub Hub(bool selected = false)
    {
        var hub = new EveRegistryHub(); new PNContentModule().Register(hub); new PNGameplayContentModule().Register(hub);
        hub.Operations().Register(new PNSkillOperationDefinition(Separate, typeof(PNSeparateValuesOperation)));
        if (selected)
        {
            var id = EveId.Parse("addon:heal_scale"); hub.Calculations().Register(new PNStatCalculationDefinition(id, typeof(PNHealSelectedScale)));
            hub.Calculations().RequestOverride(EveId.Parse("base:scale"), id, "addon", "1", new[] { "project_n" });
            var operationId = EveId.Parse("addon:heal_operation");
            hub.Operations().Register(new PNSkillOperationDefinition(operationId, typeof(PNSelectedHealOperation)));
            hub.Operations().RequestOverride(EveId.Parse("base:heal"), operationId, "addon", "1", new[] { "project_n" });
        }
        hub.Skills.Register(new EveSkillDefinition(Skill, JObject.Parse("{\"action\":{\"id\":\"base:damage_action\",\"perform\":{\"value\":" + Parts + ",\"tags\":[\"base:true\"]}}}")));
        hub.GetOrCreate<EveCharacterDefinition>().Register(new EveCharacterDefinition(Hero, new[] {
            new EveInitialStat(PNContentIds.Health, 2000d, 2000d), new EveInitialStat(PNContentIds.AttackPower, 100d), new EveInitialStat(PNContentIds.Defense, 500d)
        }, new[] { Skill }));
        hub.Seal(); return hub;
    }
    private static void Start(EveRegistryHub hub, EveSessionRole role)
    { EveRegistrySession.Begin(); EveRegistrySession.Start(hub, role, EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:heal")); }
    private static (EveTestLoader loader, PNStat stat, PNSkill skill) Entity(long id, PNStat stat = null)
    {
        var loader = new EveTestLoader(); stat = stat ?? new PNUnprotectedDamageTestStat(); var skill = new PNSkill(); var identity = new EveEntityIdentity();
        identity.Assign(new EveEntityId(id), Hero, 0); MonoBehaviour[] components = { loader, stat, skill, identity };
        foreach (var component in components) component.Components = components; return (loader, stat, skill);
    }
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException || error is FormatException || error is InvalidOperationException || error is KeyNotFoundException || error is OverflowException || error is NotSupportedException)
        { Check(true, name); return; }
        throw new Exception("Expected rejection: " + name);
    }
}
