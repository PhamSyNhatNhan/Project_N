using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static EveSessionSmokeTests;

public class PNFailingDamageReceiver : PNStat
{
    public override PNDamageResult ReceiveDamage(PNDamage damage) => throw new InvalidOperationException("test receiver failure");
}

// EN: Checks action-owned value parsing and the live value -> bonus snapshot -> receiver pipeline.
// VI: Kiểm tra value thuộc action và luồng giá trị hiện tại -> chụp bonus -> phía nhận.
public static class PNSkillValueTests
{
    private static readonly EveId Hero = EveId.Parse("test:value_hero");
    private static readonly EveId Scaled = EveId.Parse("test:scaled_damage");
    private static readonly EveId Raw = EveId.Parse("test:raw_damage");
    private static readonly EveId MissingValue = EveId.Parse("test:missing_value");
    private static readonly EveId Negative = EveId.Parse("test:negative_damage");
    private static readonly EveId MissingStat = EveId.Parse("test:missing_stat");
    private static readonly EveId Energy = EveId.Parse("test:energy");
    private static readonly EveId Buff = EveId.Parse("test:value_buff");

    public static void Run()
    {
        PNHealTests.Run();
        var raw = PNSkillValue.Parse(JObject.Parse("{\"amount\":500}"));
        Check(raw.Evaluate(null) == 500d && !raw.StatId.IsValid, "omitted statId yields raw value");
        Check(new PNSkillValue(-20d).Evaluate(null) == -20d, "generic value permits finite signed values");
        foreach (string bad in new[] { "null", "[]", "500", "{}", "{\"amount\":null}", "{\"amount\":\"500\"}",
            "{\"amount\":true}", "{\"amount\":1,\"statId\":null}", "{\"amount\":1,\"statId\":5}",
            "{\"amount\":1,\"statId\":\"\"}", "{\"amount\":1,\"ammount\":5}", "{\"amount\":1e400}" })
            Reject(() => PNSkillValue.Parse(JToken.Parse(bad)), "malformed value rejected");
        Reject(() => new PNSkillValue(double.NaN), "NaN value rejected");
        Reject(() => new PNSkillValue(double.PositiveInfinity), "infinite value rejected");

        var hub = Hub();
        var standalone = new PNStat();
        standalone.Initialize(hub.Characters.Get(Hero), hub);
        var scaled = new PNSkillValue(500d, PNContentModule.AttackPower);
        Check(scaled.Evaluate(standalone) == 500d, "500 amount with stat means 500 percent");
        standalone.SetCurrent(PNContentModule.Health, 40d);
        Check(new PNSkillValue(50d, PNContentModule.Health).Evaluate(standalone) == 1000d,
            "resource scaling uses evaluated maximum rather than current HP");
        Reject(() => scaled.Evaluate(null), "scaled value requires caster stats");
        Reject(() => scaled.Evaluate(new PNStat()), "scaled value requires initialized stats");
        Reject(() => new PNSkillValue(100d, Energy).Evaluate(standalone), "registered but absent entity stat rejected");
        Reject(() => new PNSkillValue(100d, PNContentModule.Fire).Evaluate(standalone), "tag ID cannot act as a scaling stat");
        standalone.SetSource(Buff, new[] { new EveStatModifier(PNContentModule.AttackPower, 1d, 1) });
        Check(scaled.Evaluate(standalone) == 1000d, "stat scale reads current buffs instead of parsing-time value");
        var huge = new PNStatSet(hub);
        huge.AddBase(new EveBaseStat(PNContentModule.AttackPower, double.MaxValue));
        var hugeCaster = new PNStat();
        hugeCaster.Initialize(huge);
        Reject(() => new PNSkillValue(500d, PNContentModule.AttackPower).Evaluate(hugeCaster), "scaled value overflow rejected");
        standalone.Unload();

        EveRegistrySession.Begin();
        EveRegistrySession.Start(hub, EveSessionRole.Offline,
            EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:value"));
        var source = Entity(4001);
        var first = Entity(4002);
        var second = Entity(4003);
        try
        {
            source.loader.Load(hub);
            first.loader.Load(hub);
            second.loader.Load(hub);
            Check(hub.Actions().Get(PNGameplayContentModule.DamageAction).Create() is PNDamageAction,
                "game module registers the concrete damage action");
            var targets = new List<PNStat> { first.stat, first.stat, second.stat };
            var action = (PNDamageAction)source.skill.CreateAction(Scaled, out var context, targets);
            targets.Clear();
            Check(context.Targets.Count == 3, "action context copies supplied targets");
            Reject(() => ((IList<PNStat>)context.Targets)[0] = second.stat, "context targets are read-only");
            Reject(() => source.skill.CreateAction(Raw, out _, new PNStat[] { null }), "null explicit targets rejected");
            Reject(() => action.Perform(context), "damage action cannot perform before Start");
            action.Start(context);
            Check(action.Damage == null && first.stat.GetCurrent(PNContentModule.Health) == 2000d,
                "Start validates payload but does not capture value or damage HP");
            Reject(() => action.Start(context), "double Start rejected");
            Reject(() => action.Exit(context), "Exit before Perform rejected");
            source.stat.SetSource(Buff, new[] {
                new EveStatModifier(PNContentModule.AttackPower, 1d, 1),
                new EveStatModifier(PNContentModule.Fire, .2d, 1, PNContentModule.Scale)
            });
            first.stat.SetSource(Buff, new[] { new EveStatModifier(PNContentModule.Fire, -.2d, 1, PNContentModule.Mitigation) });
            first.stat.DamageReceived += result => source.stat.SetSource(Buff, new[] {
                new EveStatModifier(PNContentModule.Fire, 9d, 1, PNContentModule.Scale)
            });
            action.Perform(context);
            Check(action.Damage.Amount == 1200d && action.Results.Count == 2,
                "Perform evaluates 500 percent of buffed ATK and snapshots outgoing tags once");
            Check(Near(first.stat.GetCurrent(PNContentModule.Health), 1520d) &&
                Near(second.stat.GetCurrent(PNContentModule.Health), 1400d),
                "each unique explicit target receives its own DEF/resistance calculation once");
            Check(ReferenceEquals(action.Results[0].Damage, action.Results[1].Damage) &&
                action.Results[1].Damage.Amount == 1200d,
                "sender buff changes during delivery cannot double-scale later targets");
            Reject(() => action.Perform(context), "double Perform cannot repeat committed hits");
            action.Exit(context);
            Reject(() => action.Perform(context), "ended damage action cannot perform again");
            source.stat.RemoveSource(Buff);

            var rawAction = (PNDamageAction)source.skill.CreateAction(Raw, out var rawContext, new[] { second.stat });
            rawAction.Start(rawContext);
            rawAction.Perform(rawContext);
            Check(rawAction.Damage.Amount == 10d && rawAction.Results[0].AppliedDamage == 10d,
                "raw action sends raw amount through the existing damage flow");
            rawAction.Exit(rawContext);
            var prepared = (PNDamageAction)source.skill.CreateAction(Raw, out var preparedContext);
            prepared.Start(preparedContext);
            prepared.Perform(preparedContext);
            Check(prepared.Damage.Amount == 10d && prepared.Results.Count == 0,
                "action without targets can prepare a snapshot for delayed delivery");
            var captured = prepared.Damage;
            prepared.Exit(preparedContext);
            Check(second.stat.ReceiveDamage(captured).AppliedDamage == 10d, "captured action packet is usable after Exit");

            var cancelled = (PNDamageAction)source.skill.CreateAction(Raw, out var cancelledContext, new[] { second.stat });
            cancelled.Start(cancelledContext);
            double health = second.stat.GetCurrent(PNContentModule.Health);
            cancelled.Cancel(cancelledContext);
            cancelled.Cancel(cancelledContext);
            Reject(() => cancelled.Perform(cancelledContext), "cancel before Perform blocks delivery");
            Check(second.stat.GetCurrent(PNContentModule.Health) == health && cancelled.Damage == null,
                "cancelled unperformed action does not mutate HP");
            foreach (var id in new[] { MissingValue, Negative, MissingStat })
            {
                var invalid = (PNDamageAction)source.skill.CreateAction(id, out var invalidContext, new[] { second.stat });
                if (id == MissingValue) Reject(() => invalid.Start(invalidContext), "only damage action requires a value");
                else
                {
                    invalid.Start(invalidContext);
                    Reject(() => invalid.Perform(invalidContext), "invalid damage value cannot deliver");
                }
                invalid.Cancel(invalidContext);
            }
            Check(second.stat.GetCurrent(PNContentModule.Health) == health, "value validation failures leave target HP unchanged");

            var wrongContext = (PNDamageAction)source.skill.CreateAction(Raw, out var originalContext);
            wrongContext.Start(originalContext);
            source.skill.CreateAction(Raw, out var replacementContext).Dispose();
            Reject(() => wrongContext.Perform(replacementContext), "action cannot switch invocation context");
            wrongContext.Cancel(originalContext);
            var notLoaded = new PNStat();
            notLoaded.Initialize(hub.Characters.Get(Hero), hub);
            var badTarget = (PNDamageAction)source.skill.CreateAction(Raw, out var badTargetContext, new[] { first.stat, notLoaded });
            badTarget.Start(badTargetContext);
            double firstHealth = first.stat.GetCurrent(PNContentModule.Health);
            Reject(() => badTarget.Perform(badTargetContext), "invalid later target is rejected before any target receives damage");
            Check(first.stat.GetCurrent(PNContentModule.Health) == firstHealth && badTarget.Damage == null,
                "prevalidating every target prevents partial damage on invalid target lists");
            badTarget.Cancel(badTargetContext);
            notLoaded.Unload();
            var failing = Entity(4004, new PNFailingDamageReceiver());
            try
            {
                failing.loader.Load(hub);
                var partial = (PNDamageAction)source.skill.CreateAction(Raw, out var partialContext, new[] { first.stat, failing.stat });
                partial.Start(partialContext);
                Reject(() => partial.Perform(partialContext), "receiver override failure is reported after earlier target commits");
                Check(partial.Results.Count == 1 && first.stat.GetCurrent(PNContentModule.Health) == firstHealth - 10d,
                    "partial receiver failure retains successful hit results");
                Reject(() => partial.Perform(partialContext), "partially delivered invocation cannot be repeated");
                Check(first.stat.GetCurrent(PNContentModule.Health) == firstHealth - 10d, "failed retry does not double apply the first hit");
                partial.Cancel(partialContext);
            }
            finally { failing.loader.Unload(); }
            var unloading = (PNDamageAction)source.skill.CreateAction(Raw, out var unloadingContext, new[] { second.stat });
            unloading.Start(unloadingContext);
            source.loader.Unload();
            Reject(() => unloading.Perform(unloadingContext), "unloaded caster cannot evaluate or deliver an action");
            unloading.Cancel(unloadingContext);
        }
        finally { source.loader.Unload(); first.loader.Unload(); second.loader.Unload(); EveRegistrySession.End(); }
    }

    private static EveRegistryHub Hub()
    {
        var hub = new EveRegistryHub();
        new PNContentModule().Register(hub);
        new PNGameplayContentModule().Register(hub);
        hub.Stats.Register(new EveStatDefinition(Energy, EveStatKind.Number));
        var skills = hub.GetOrCreate<EveSkillDefinition>();
        skills.Register(new EveSkillDefinition(Scaled, Data("{\"value\":{\"amount\":500,\"statId\":\"project_n:attack_power\"},\"tags\":[\"project_n:physical\",\"project_n:fire\"]}")));
        skills.Register(new EveSkillDefinition(Raw, Data("{\"value\":{\"amount\":10},\"tags\":[\"project_n:true\"]}")));
        skills.Register(new EveSkillDefinition(MissingValue, Data("{\"tags\":[\"project_n:true\"]}")));
        skills.Register(new EveSkillDefinition(Negative, Data("{\"value\":{\"amount\":-10},\"tags\":[\"project_n:true\"]}")));
        skills.Register(new EveSkillDefinition(MissingStat, Data("{\"value\":{\"amount\":100,\"statId\":\"test:energy\"},\"tags\":[\"project_n:true\"]}")));
        hub.GetOrCreate<EveCharacterDefinition>().Register(new EveCharacterDefinition(Hero, new[] {
            new EveInitialStat(PNContentModule.Health, 2000d, 2000d),
            new EveInitialStat(PNContentModule.AttackPower, 100d),
            new EveInitialStat(PNContentModule.Defense, 500d)
        }, new[] { Scaled, Raw, MissingValue, Negative, MissingStat }));
        hub.Seal();
        return hub;
    }

    private static JObject Data(string perform) => JObject.Parse("{\"action\":{\"id\":\"project_n:damage_action\",\"perform\":" + perform + "}}");

    private static (EveTestLoader loader, PNStat stat, PNSkill skill) Entity(long id, PNStat stat = null)
    {
        var loader = new EveTestLoader();
        stat = stat ?? new PNUnprotectedDamageTestStat();
        var skill = new PNSkill();
        var identity = new EveEntityIdentity();
        identity.Assign(new EveEntityId(id), Hero, 0);
        MonoBehaviour[] components = { loader, stat, skill, identity };
        foreach (var component in components) component.Components = components;
        return (loader, stat, skill);
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < .001d;
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (ArgumentException) { Check(true, name); return; }
        catch (FormatException) { Check(true, name); return; }
        catch (InvalidOperationException) { Check(true, name); return; }
        catch (KeyNotFoundException) { Check(true, name); return; }
        catch (NotSupportedException) { Check(true, name); return; }
        catch (OverflowException) { Check(true, name); return; }
        throw new Exception("Expected rejection: " + name);
    }
}
