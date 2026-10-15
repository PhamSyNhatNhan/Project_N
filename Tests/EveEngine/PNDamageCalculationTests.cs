using System;
using System.Collections.Generic;
using EveEngine;
using static EveSessionSmokeTests;

// EN: Verifies signed DEF, damage-type factors, tagged resistance and damage calculation without HP mutation.
// VI: Kiểm tra DEF có dấu, hệ số loại damage, kháng theo tag và tính damage không đổi HP.
public static class PNDamageCalculationTests
{
    public static void Run()
    {
        var hub = new EveRegistryHub();
        new PNContentModule().Register(hub);
        var customType = EveId.Parse("addon:quarter_defense");
        hub.DamageTypes().Register(new PNDamageTypeDefinition(customType, 0.25d));
        hub.Tags().Register(new PNTagDefinition(customType));
        hub.Seal();
        var calculator = new PNDamageCalculation();
        var physical = hub.DamageTypes().Get(PNContentModule.Physical);
        var magic = hub.DamageTypes().Get(PNContentModule.Magic);
        var trueDamage = hub.DamageTypes().Get(PNContentModule.True);
        Check(PNDamageCalculation.DefenseConstant == 500d && physical.DefenseFactor == 1d &&
            magic.DefenseFactor == 0.5d && trueDamage.DefenseFactor == 0d,
            "K=500 and physical/magic/true factors are registered");
        foreach (double defense in new[] { -1000d, -500d, 0d, 500d, 1000d })
        {
            double expected = (2d / Math.PI) * Math.Atan(defense / 500d);
            Check(Math.Abs(calculator.CalculateDefenseCoefficient(defense) - expected) < 1e-12,
                "signed defense coefficient follows the single atan formula");
            Check(Near(calculator.Calculate(100d, defense, physical), (double)(100d * (1d - expected))),
                "damage subtracts signed defense reduction");
        }
        Check(Near(calculator.Calculate(100d, 1000d, magic), 50d) &&
            Near(calculator.Calculate(100d, -1000d, magic), 150d), "magic halves positive and negative final defense");
        Check(calculator.Calculate(100d, 1000d, trueDamage) == 100d &&
            calculator.Calculate(100d, -1000d, trueDamage) == 100d, "true ignores both signs of defense");
        Check(calculator.Calculate(0d, -500d, physical) == 0d, "zero damage stays zero with negative defense");
        Check(calculator.Calculate(100d, double.MaxValue, physical) >= 0d &&
            calculator.Calculate(100d, -double.MaxValue, physical) <= 200d, "extreme finite defense stays bounded");
        Check(Near(calculator.Calculate(100d, 2000d, hub.DamageTypes().Get(customType)), 50d),
            "mod damage types use their registered defense factor");

        var set = new PNStatSet(hub);
        set.AddBase(new EveBaseStat(PNContentModule.Defense, 500d));
        set.AddBase(new EveBaseStat(PNContentModule.AttackPower, 100d));
        set.AddBase(new EveResourceStat(PNContentModule.Health, 100d, 40d, EveMaxChangePolicy.PreserveRatio));
        Check(set.Evaluate(PNContentModule.Defense) == 500d &&
            Near(set.CalculateDamage(100d, PNContentModule.Physical), 50d), "incoming damage evaluates final defense");
        var defenseSource = EveId.Parse("test:defense_debuff");
        set.SetSource(defenseSource, new[] {
            new EveStatModifier(PNContentModule.Defense, -1000d, 0),
            new EveStatModifier(PNContentModule.AttackPower, -200d, 0),
            new EveStatModifier(PNContentModule.Health, -200d, 0)
        });
        Check(set.Evaluate(PNContentModule.Defense) == -500d &&
            Near(set.CalculateDamage(100d, PNContentModule.Physical), 150d),
            "flat debuff crosses zero and cached DEF remains signed");
        Check(set.Evaluate(PNContentModule.AttackPower) == 0d && set.GetMax(PNContentModule.Health) == 0d,
            "allowing negative DEF does not allow negative ATK or HP");
        set.RemoveSource(defenseSource);
        Check(set.Evaluate(PNContentModule.Defense) == 500d &&
            Near(set.CalculateDamage(100d, PNContentModule.Physical), 50d), "removing DEF debuff invalidates cached damage inputs");
        set.SetSource(defenseSource, new[] { new EveStatModifier(PNContentModule.Defense, 1d, 1) });
        Check(set.Evaluate(PNContentModule.Defense) == 1000d &&
            Near(set.CalculateDamage(100d, PNContentModule.Magic), 50d), "magic factor applies after DEF percentage buffs");
        set.RemoveSource(defenseSource);
        set.SetCurrent(PNContentModule.Health, 40d);

        var resistanceSource = EveId.Parse("test:tagged_resistance");
        set.SetSource(resistanceSource, new[] {
            new EveStatModifier(PNContentModule.Fire, -0.2d, 1, PNContentModule.Mitigation),
            new EveStatModifier(PNContentModule.Physical, -0.1d, 1, PNContentModule.Mitigation),
            new EveStatModifier(PNContentModule.Fire, -0.1d, 2, PNContentModule.Mitigation),
            new EveStatModifier(PNContentModule.Fire, 0.5d, 1, PNContentModule.Scale)
        });
        Check(Near(set.CalculateDamage(100d, PNContentModule.Physical, PNContentModule.Fire), 31.5d),
            "DEF applies before grouped tag resistance and incoming damage ignores outgoing bonuses");
        Check(Near(set.CalculateDamage(100d, PNContentModule.Fire, PNContentModule.Physical, PNContentModule.Fire), 31.5d),
            "damage tags are order independent and duplicates do not double apply resistance");
        Check(Near(set.CalculateDamage(100d, PNContentModule.True, PNContentModule.Fire), 72d),
            "true still receives elemental resistance");
        Check(Near(set.CalculateDamage(100d, PNContentModule.Physical, PNContentModule.Ice), 45d),
            "unselected elemental resistance does not leak into damage");
        Check(set.GetCurrent(PNContentModule.Health) == 40d && set.GetMax(PNContentModule.Health) == 100d,
            "damage calculations do not mutate resource current or maximum");
        set.RemoveSource(resistanceSource);
        set.SetSource(defenseSource, new[] { new EveStatModifier(PNContentModule.Defense, -1000d, 0) });
        set.SetSource(resistanceSource, new[] { new EveStatModifier(PNContentModule.Fire, -0.2d, 1, PNContentModule.Mitigation) });
        Check(Near(set.CalculateDamage(100d, PNContentModule.Physical, PNContentModule.Fire), 120d),
            "negative defense amplification combines with elemental resistance");
        var negativeBase = new PNStatSet(hub);
        negativeBase.AddBase(new EveBaseStat(PNContentModule.Defense, -500d));
        Check(negativeBase.Evaluate(PNContentModule.Defense) == -500d &&
            Near(negativeBase.CalculateDamage(100d, PNContentModule.Physical), 150d), "negative base DEF is supported");
        var noDefense = new PNStatSet(hub);
        Check(noDefense.CalculateDamage(100d, PNContentModule.Physical) == 100d, "missing DEF behaves as zero");

        Reject(() => set.CalculateDamage(-1d, PNContentModule.Physical), "negative raw damage rejected");
        Reject(() => set.CalculateDamage(double.NaN, PNContentModule.Physical), "NaN damage rejected");
        Reject(() => set.CalculateDamage(double.PositiveInfinity, PNContentModule.Physical), "infinite damage rejected");
        Reject(() => set.CalculateDamage(100d, (EveId[])null), "null tags rejected");
        Reject(() => set.CalculateDamage(100d), "missing damage type rejected");
        Reject(() => set.CalculateDamage(100d, PNContentModule.Fire), "element alone is not a damage type");
        Reject(() => set.CalculateDamage(100d, PNContentModule.Physical, PNContentModule.Magic), "conflicting damage types rejected");
        Reject(() => set.CalculateDamage(100d, PNContentModule.Physical, EveId.Parse("addon:unknown")), "unknown tag rejected");
        Reject(() => calculator.CalculateDefenseCoefficient(double.NaN), "non-finite DEF rejected");
        Reject(() => calculator.CalculateDefenseCoefficient(500d, -1d), "negative DEF factor rejected");
        Reject(() => calculator.Calculate(100d, 0d, null), "null damage definition rejected");
        Reject(() => calculator.Calculate(double.MaxValue, -500d, physical), "damage amplification overflow rejected");
        Reject(() => new PNDamageTypeDefinition(customType, double.NaN), "NaN registry factor rejected");
        Reject(() => new PNDamageTypeDefinition(customType, double.PositiveInfinity), "infinite registry factor rejected");
        Reject(() => new PNDamageTypeDefinition(customType, -1d), "negative registry factor rejected");

        var first = FactorHub(1d);
        var changed = FactorHub(0.5d);
        var fingerprint = EveContentCompatibility.Create(first, Array.Empty<EveContentDocument>(), "test:defense");
        Reject(() => fingerprint.Validate(EveContentCompatibility.Create(changed, Array.Empty<EveContentDocument>(), "test:defense")),
            "coop rejects changed defense factor under the same damage type ID");
    }

    private static EveRegistryHub FactorHub(double factor)
    {
        var hub = new EveRegistryHub();
        hub.GetOrCreate<PNDamageTypeDefinition>().Register(new PNDamageTypeDefinition(PNContentModule.Physical, factor));
        hub.Seal();
        return hub;
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 0.001d;

    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (ArgumentException) { Check(true, name); return; }
        catch (InvalidOperationException) { Check(true, name); return; }
        catch (KeyNotFoundException) { Check(true, name); return; }
        catch (OverflowException) { Check(true, name); return; }
        throw new Exception("Expected rejection: " + name);
    }
}
