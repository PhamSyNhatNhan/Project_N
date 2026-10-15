using System;
using System.Linq;
using EveEngine;
using UnityEngine;
using static EveSessionSmokeTests;

public sealed class PNCriticalStatProbe : PNStat
{
    public override double DefaultIFrameDuration => 0d; // Crit checks isolate sender snapshots.
    public double Roll;
    public int Rolls;
    protected override double NextCriticalRoll() { Rolls++; return Roll; }
}

public static class PNCriticalDamageTests
{
    public static void Run()
    {
        var hero = EveId.Parse("test:crit_hero");
        var hub = new EveRegistryHub(); new PNContentModule().Register(hub);
        hub.GetOrCreate<EveCharacterDefinition>().Register(new EveCharacterDefinition(hero, new[] {
            new EveInitialStat(PNContentIds.Health, 1000000000000000d),
            new EveInitialStat(PNContentIds.AttackPower, 100d), new EveInitialStat(PNContentIds.Defense, 0d)
        })); hub.Seal();
        Check(hub.Stats.ResolveIdentity(EveId.Parse("base:attack_speed")) == PNContentIds.AttackSpeed,
            "public attack speed binds to the registered stat");
        var explicitSpeed = new PNStatSet(hub);
        explicitSpeed.LoadCharacter(new EveCharacterDefinition(EveId.Parse("test:slow_hero"), new[] {
            new EveInitialStat(EveId.Parse("base:attack_speed"), 50d)
        }));
        Check(explicitSpeed.Evaluate(PNContentIds.AttackSpeed) == 50d,
            "explicit character attack speed retains its direct percentage value");
        Check(hub.Stats.ResolveIdentity(EveId.Parse("base:crit_rate")) == PNContentIds.CritRate &&
            hub.Stats.ResolveIdentity(EveId.Parse("base:crit_damage")) == PNContentIds.CritDamage, "public crit IDs bind to registered stats");
        EveRegistrySession.Begin();
        EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:critical"));
        var source = Entity(980001); var first = Entity(980002); var second = Entity(980003);
        void Configure(double rate, double damage = 150d) => source.stat.SetSource(EveId.Parse("test:crit_config"), new[] {
            new EveStatModifier(EveId.Parse("base:crit_rate"), rate, 0),
            new EveStatModifier(EveId.Parse("base:crit_damage"), damage - 150d, 0)
        });
        try
        {
            source.loader.Load(hub); first.loader.Load(hub); second.loader.Load(hub);
            Check(hub.Stats.ResolveIdentity(EveId.Parse("base:cooldown_reduction")) == PNContentIds.CooldownReduction &&
                source.stat.Evaluate(EveId.Parse("base:cooldown_reduction")) == 0d,
                "cooldown reduction has public binding and defaults to zero");
            var cdrSource = EveId.Parse("test:cdr");
            source.stat.SetSource(cdrSource, new[] { new EveStatModifier(EveId.Parse("base:cooldown_reduction"), -20d, 0) });
            Check(source.stat.Evaluate(PNContentIds.CooldownReduction) == -20d,
                "negative cooldown reduction remains signed for longer cooldowns");
            source.stat.SetSource(cdrSource, new[] { new EveStatModifier(PNContentIds.CooldownReduction, 150d, 0) });
            Check(source.stat.Evaluate(PNContentIds.CooldownReduction) == 150d,
                "cooldown reduction retains values above 100 for consumer clamping");
            source.stat.SetSource(cdrSource, Array.Empty<EveStatModifier>());
            Check(source.stat.Evaluate(EveId.Parse("base:attack_speed")) == 100d,
                "legacy character defaults to 100 percent attack speed");
            var speedSource = EveId.Parse("test:attack_speed");
            source.stat.SetSource(speedSource, new[] {
                new EveStatModifier(EveId.Parse("base:attack_speed"), -50d, 0),
                new EveStatModifier(EveId.Parse("base:attack_speed"), .2d, 1)
            });
            Check(source.stat.Evaluate(PNContentIds.AttackSpeed) == 70d,
                "attack speed combines percentage points with fractional group bonuses");
            source.stat.SetSource(speedSource, new[] { new EveStatModifier(PNContentIds.AttackSpeed, -150d, 0) });
            Check(source.stat.Evaluate(PNContentIds.AttackSpeed) == 0d,
                "attack speed can reach zero but evaluates nonnegative");
            source.stat.SetSource(speedSource, Array.Empty<EveStatModifier>());
            Check(source.stat.Evaluate(PNContentIds.CritRate) == 0d && source.stat.Evaluate(PNContentIds.CritDamage) == 150d,
                "legacy character defaults to zero rate and 150 percent critical damage");
            first.stat.SetCurrent(PNContentIds.Health, 200000000d);
            for (int i = 0; i < 10; i++)
            {
                var hit = source.stat.SendDamage(first.stat, 10d, PNContentIds.True);
                Check(hit.CalculatedDamage == 10d && hit.AppliedDamage == 10d, "double health loses exactly ten points at 200M");
            }
            Check(first.stat.GetCurrent(PNContentIds.Health) == 199999900d, "ten hits preserve exact HP total");
            first.stat.SetCurrent(PNContentIds.Health, 1000000000000d);
            var small = source.stat.SendDamage(first.stat, .25d, PNContentIds.True);
            Check(small.AppliedDamage == .25d, "fractional damage survives trillion-point health");
            Check(PNSkillValue.Parse(Newtonsoft.Json.Linq.JObject.Parse("{\"amount\":200000001.25}")).Amount == 200000001.25d,
                "skill JSON retains values beyond float precision");
            Check(EveStatNumber.Format(999999999999999.75d, System.Globalization.CultureInfo.InvariantCulture) == "999999999999999.75",
                "large double formatter preserves represented fractional digits");
            Check(EveStatNumber.Format(25.756d, System.Globalization.CultureInfo.InvariantCulture) == "25.76",
                "crit percentages display at most two decimal places");
            Configure(-25.75d); var negative = source.stat.CreateDamage(10d, PNContentIds.True);
            Check(source.stat.Evaluate(PNContentIds.CritRate) == -25.75d && negative.CritRate == -25.75d && !negative.IsCritical && source.stat.Rolls == 0,
                "negative crit rate remains signed and never rolls");
            Configure(25.75d); source.stat.Roll = .25749d;
            var critical = source.stat.CreateDamage(10d, PNContentIds.True);
            Check(critical.IsCritical && critical.Amount == 15d && critical.CritRate == 25.75d && critical.CritDamage == 150d,
                "percentage rate and total crit multiplier produce the correct snapshot");
            source.stat.Roll = .2575d;
            Check(!source.stat.CreateDamage(10d, PNContentIds.True).IsCritical, "roll at the probability boundary is noncritical");
            Configure(25.756d); source.stat.Roll = .257559d;
            Check(source.stat.CreateDamage(10d, PNContentIds.True).IsCritical, "crit calculations retain more than two decimal places");
            Configure(125d, 200d); int rolls = source.stat.Rolls;
            var guaranteed = source.stat.CreateDamage(10d, PNContentIds.True);
            Check(guaranteed.IsCritical && guaranteed.Amount == 20d && guaranteed.CritRate == 125d && source.stat.Rolls == rolls,
                "rate over 100 is guaranteed without overwriting the stat or drawing randomness");
            var disabled = source.stat.CreateDamage(10d, false, PNContentIds.True);
            Check(!disabled.CanCrit && !disabled.IsCritical && disabled.Amount == 10d && source.stat.Rolls == rolls,
                "explicit noncritical damage bypasses crit");
            Configure(0d, 100d);
            var a = first.stat.ReceiveDamage(guaranteed); var b = second.stat.ReceiveDamage(guaranteed);
            Check(a.IsCritical && b.IsCritical && a.CalculatedDamage == 20d && b.CalculatedDamage == 20d,
                "one packet retains crit state across targets and later sender buffs");
            Configure(100d, 150d);
            first.stat.SetSource(EveId.Parse("test:def"), new[] { new EveStatModifier(PNContentIds.Defense, 500d, 0) });
            var defended = source.stat.SendDamage(first.stat, 100d, PNContentIds.Physical);
            Check(defended.CalculatedDamage == 75d && defended.IsCritical, "critical multiplier applies before receiver defense");
            bool overflow = false;
            try { source.stat.CreateDamage(double.MaxValue, PNContentIds.True); } catch (OverflowException) { overflow = true; }
            Check(overflow, "critical multiplication rejects nonfinite damage");
            Configure(1d); source.stat.Roll = double.NaN;
            Reject(() => source.stat.CreateDamage(10d, PNContentIds.True), "invalid custom crit random draw rejected");
            Check(new PNSlashHitSettingsTest().Check(), "slash supports explicit canCrit=false and rejects malformed toggles");
        }
        finally { source.loader.Unload(); first.loader.Unload(); second.loader.Unload(); EveRegistrySession.End(); }

        (EveTestLoader loader, PNCriticalStatProbe stat) Entity(long id)
        {
            var loader = new EveTestLoader(); var stat = new PNCriticalStatProbe(); var identity = new EveEntityIdentity();
            identity.Assign(new EveEntityId(id), hero, 0);
            MonoBehaviour[] components = { loader, stat, identity };
            foreach (var component in components) component.Components = components;
            return (loader, stat);
        }
    }
    private sealed class PNSlashHitSettingsTest
    {
        public bool Check()
        {
            var data = Newtonsoft.Json.Linq.JObject.Parse("{\"value\":{\"amount\":10},\"canCrit\":false,\"hitbox\":{}}");
            if (PNSlashHitSettings.Parse(data).CanCrit) return false;
            data["canCrit"] = "false";
            try { PNSlashHitSettings.Parse(data); return false; } catch (FormatException) { return true; }
        }
    }
}
