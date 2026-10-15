using System;
using System.Collections.Generic;
using EveEngine;
using static EveSessionSmokeTests;

// EN: Example code-mod calculator with its own formula instead of the built-in group multiplication.
// VI: Bộ tính mẫu của mod code dùng công thức riêng thay cho nhân nhóm mặc định.
public class PNTestCustomCalculation : PNStatCalculation
{
    public override double Calculate(PNStatCalculationContext context)
    {
        double result = context.BaseValue;
        foreach (var modifier in context.Modifiers) result += modifier.Value * 10d;
        return result;
    }
}

// EN: A second implementation makes changes to executable calculation identity detectable by coop.
// VI: Triển khai thứ hai giúp coop phát hiện thay đổi định danh logic đường tính.
public class PNTestChangedCalculation : PNTestCustomCalculation { }
public class PNCurrentAwareCalculation : PNStatCalculation
{
    public override double Calculate(PNStatCalculationContext context) => context.BaseValue + context.Stats.GetCurrent(PNContentIds.Health);
}
public class PNExternalAwareCalculation : PNStatCalculation
{
    public static double Offset;
    public override double Calculate(PNStatCalculationContext context) => context.BaseValue + Offset;
}
public class PNMutableCalculationModifier : EveStatModifier
{
    public double MutableValue;
    public override double Value => MutableValue;
    public PNMutableCalculationModifier(EveId id, double value) : base(id, value, 0) { MutableValue = value; }
}

public class PNFlatOverrideStatSet : PNStatSet
{
    public bool ThrowOnCalculate;
    public int Calls;
    public PNFlatOverrideStatSet(EveRegistryHub hub) : base(hub) { }
    public override double Calculate(EveId calculationId, double baseValue, params EveId[] ids)
    {
        Calls++;
        if (ThrowOnCalculate) throw new InvalidOperationException("Probe failure before base Calculate.");
        return base.Calculate(calculationId, baseValue, ids) * 2d;
    }
}

public class PNFlatNestedCalculation : PNScaleCalculation
{
    public override double Calculate(PNStatCalculationContext context)
    {
        double own = base.Calculate(context);
        if (context.BaseValue == 99d) return own;
        return own + context.Stats.Calculate(PNContentModule.Scale, 99d, PNContentModule.Fire);
    }
}

// EN: Exercises shared source storage, calculation isolation, resource policies and mod extension.
// VI: Kiểm tra lưu chung theo nguồn, cô lập đường tính, policy resource và mở rộng mod.
public static class PNStatCalculationTests
{
    private static readonly EveId Source = EveId.Parse("test:mixed_source");
    private static readonly EveId Custom = EveId.Parse("addon:custom_calculation");

    public static void Run()
    {
        var currentSet = new PNStatSet(Hub(typeof(PNCurrentAwareCalculation)), Custom);
        currentSet.AddBase(new EveResourceStat(PNContentIds.Health, 100, 40, EveMaxChangePolicy.PreserveCurrent));
        currentSet.AddBase(new EveBaseStat(PNContentIds.AttackPower, 10));
        Check(currentSet.Evaluate(PNContentIds.AttackPower) == 50, "live custom Evaluate reads Current");
        var oldCopy = currentSet.CaptureCopy();
        Check(oldCopy.Evaluate(PNContentIds.AttackPower) == 50, "custom calculator capture sees resource Current");
        currentSet.SetCurrent(PNContentIds.Health, 120);
        Check(currentSet.CaptureCopy().Evaluate(PNContentIds.AttackPower) == 130, "custom current-dependent capture preserves Current above raw base maximum");
        currentSet.SetCurrent(PNContentIds.Health, 20);
        Check(currentSet.Evaluate(PNContentIds.AttackPower) == 30, "custom Evaluate updates after Current changes without manual invalidation");
        Check(currentSet.CaptureCopy().Evaluate(PNContentIds.AttackPower) == 30 && oldCopy.Evaluate(PNContentIds.AttackPower) == 50, "resource write refreshes custom capture and preserves old snapshot isolation");
        var hub = Hub();
        var external = new PNStatSet(Hub(typeof(PNExternalAwareCalculation)), Custom);
        external.AddBase(new EveBaseStat(PNContentIds.AttackPower, 10));
        PNExternalAwareCalculation.Offset = 2;
        Check(external.Evaluate(PNContentIds.AttackPower) == 12, "custom Evaluate reads external formula state");
        PNExternalAwareCalculation.Offset = 5;
        Check(external.Evaluate(PNContentIds.AttackPower) == 15, "custom Evaluate does not cache external state");
        var varying = new PNStatSet(hub);
        var mutableSet = new PNStatSet(hub); mutableSet.AddBase(new EveBaseStat(PNContentIds.AttackPower, 10));
        var mutableModifier = new PNMutableCalculationModifier(PNContentIds.AttackPower, 2);
        mutableSet.SetSource(Source, new[] { mutableModifier });
        Check(mutableSet.Evaluate(PNContentIds.AttackPower) == 12, "custom mutable modifier initially evaluates");
        mutableModifier.MutableValue = 5;
        Check(mutableSet.Evaluate(PNContentIds.AttackPower) == 15, "custom mutable modifier is never hidden by numeric/preparation cache");
        varying.SetSource(Source, new[] { new EveStatModifier(PNContentIds.Fire, .2, 1, PNContentIds.Scale) });
        Check(Near(varying.Calculate(PNContentIds.Scale, 10, PNContentIds.Fire), 12) && Near(varying.Calculate(PNContentIds.Scale, 20, PNContentIds.Fire), 24), "prepared selection reuses inputs while amount changes");
        varying.SetSource(Source, new[] { new EveStatModifier(PNContentIds.Fire, .5, 1, PNContentIds.Scale) });
        Check(Near(varying.Calculate(PNContentIds.Scale, 20, PNContentIds.Fire), 30) && varying.Calculate(PNContentIds.Scale, 20, PNContentIds.Ice) == 20, "source and tag changes select fresh preparation");
        varying.RemoveSource(Source);
        Check(varying.Calculate(PNContentIds.Scale, 20, PNContentIds.Fire) == 20, "removal invalidates prepared modifiers");
        var calculations = hub.Calculations();
        Check(calculations.Count == 3 && calculations.IsSealed &&
            calculations.Get(PNContentModule.Scale).Create() is PNScaleCalculation &&
            calculations.Get(PNContentModule.Mitigation).Create() is PNMitigationCalculation,
            "scale, mitigation and mod calculation register and seal");
        Check(!ReferenceEquals(calculations.Get(Custom).Create(), calculations.Get(Custom).Create()),
            "calculation factories create independent instances");
        Reject(() => new PNStatCalculationDefinition(default, typeof(PNScaleCalculation)), "invalid calculation ID rejected");
        Reject(() => new PNStatCalculationDefinition(Custom, typeof(PNStatCalculation)), "abstract calculation rejected");
        Reject(() => new PNStatCalculationDefinition(Custom, typeof(PNStatSet)), "wrong calculation implementation rejected");
        Reject(() => calculations.Register(new PNStatCalculationDefinition(EveId.Parse("addon:late"), typeof(PNScaleCalculation))),
            "sealed calculation registry rejects late additions");
        Reject(() => new PNStatSet(new EveRegistryHub()), "stat set requires sealed calculation content");

        var set = new PNStatSet(hub);
        set.AddBase(new EveBaseStat(PNContentModule.AttackPower, 100d));
        set.AddBase(new EveResourceStat(PNContentModule.Health, 100d, 40d, EveMaxChangePolicy.PreserveRatio));
        set.SetSource(Source, new[] {
            new EveStatModifier(PNContentModule.AttackPower, 50d, 0),
            new EveStatModifier(PNContentModule.AttackPower, 0.2d, 1),
            new EveStatModifier(PNContentModule.AttackPower, -0.8d, 1, PNContentModule.Mitigation),
            new EveStatModifier(PNContentModule.Fire, 0.2d, 1, PNContentModule.Scale),
            new EveStatModifier(PNContentModule.Fire, 0.1d, 1, PNContentModule.Scale),
            new EveStatModifier(PNContentModule.Physical, 0.1d, 1, PNContentModule.Scale),
            new EveStatModifier(PNContentModule.Fire, 0.5d, 2, PNContentModule.Scale),
            new EveStatModifier(PNContentModule.Fire, -0.2d, 1, PNContentModule.Mitigation),
            new EveStatModifier(PNContentModule.Fire, -0.1d, 1, PNContentModule.Mitigation),
            new EveStatModifier(PNContentModule.Physical, -0.2d, 2, PNContentModule.Mitigation),
            new EveStatModifier(PNContentModule.Fire, 3d, 1, Custom),
            new EveStatModifier(PNContentModule.Health, -0.5d, 1, PNContentModule.Mitigation)
        });
        Check(Near(set.Evaluate(PNContentModule.AttackPower), 170d), "Evaluate includes legacy scale modifiers but excludes mitigation");
        Check(Near(set.Calculate(PNContentModule.Scale, 100d, PNContentModule.Fire), 195d),
            "scale accepts raw values and element IDs without a base stat");
        Check(Near(set.Calculate(PNContentModule.Scale, 100d, PNContentModule.Physical, PNContentModule.Fire), 210d),
            "selected damage type and element share groups before multiplying");
        Check(Near(set.Calculate(PNContentModule.Mitigation, 100d, PNContentModule.Fire, PNContentModule.Physical), 56d),
            "mitigation reads only its routed contributions");
        Check(Near(set.Calculate(PNContentModule.Scale, 100d, PNContentModule.Fire, PNContentModule.Physical, PNContentModule.Fire), 210d),
            "duplicate and reordered IDs do not double apply modifiers");
        Check(Near(set.Calculate(Custom, 100d, PNContentModule.Fire), 130d), "code mod registers a new ID with independent logic");
        Check(set.GetMax(PNContentModule.Health) == 100d && set.GetCurrent(PNContentModule.Health) == 40d,
            "incoming health modifiers do not change resource maximum or current");
        Check(set.SourceSnapshot()[Source].Count == 12, "all calculation routes share one source map");
        Check(Near(set.Calculate(PNContentModule.Scale, 100d, PNContentModule.Ice), 100d) &&
            Near(set.Calculate(PNContentModule.Mitigation, 100d, PNContentModule.Ice), 100d),
            "absent modifiers preserve the supplied raw value");

        Reject(() => set.SetSource(Source, new[] { new EveStatModifier(PNContentModule.Fire, 1d, 1, EveId.Parse("addon:unknown")) }),
            "unknown calculation rejected before replacing source");
        Reject(() => set.SetSource(Source, new[] { new EveStatModifier(EveId.Parse("addon:unknown"), 1d, 1) }),
            "unknown contribution ID rejected");
        Reject(() => set.SetSource(Source, new[] { new EveStatModifier(PNContentModule.Fire, 1d, 1, Custom, PNContentIds.After) }),
            "percentage groups reject contribution ID");
        Reject(() => set.SetSource(Source, new[] { new EveStatModifier(PNContentModule.Defense, 1d, 0) }),
            "group 0 requires a base stat present on this entity");
        Reject(() => set.SetSource(Source, new[] { new EveStatModifier(PNContentModule.AttackPower, 1d, 0, PNContentModule.Mitigation, EveId.Parse("test:unknown_flat")) }),
            "mitigation rejects unknown contribution");
        Check(Near(set.Evaluate(PNContentModule.AttackPower), 170d) && set.GetCurrent(PNContentModule.Health) == 40d &&
            set.SourceSnapshot()[Source].Count == 12, "rejected source mutations preserve data and cached values");
        Reject(() => set.Calculate(EveId.Parse("addon:unknown"), 100d, PNContentModule.Fire), "unknown calculation cannot execute");
        Reject(() => set.Calculate(Custom, double.NaN, PNContentModule.Fire), "non-finite raw input rejected");
        Reject(() => set.Calculate(Custom, 100d, Array.Empty<EveId>()), "empty calculation selection rejected");
        Reject(() => set.Calculate(Custom, 100d, EveId.Parse("addon:unknown")), "unknown calculation input ID rejected");

        set.SetSource(Source, new[] {
            new EveStatModifier(PNContentModule.Health, 50d, 0, PNContentModule.Scale),
            new EveStatModifier(PNContentModule.Health, -0.5d, 1, PNContentModule.Mitigation)
        });
        Check(set.GetMax(PNContentModule.Health) == 150d && set.GetCurrent(PNContentModule.Health) == 60d &&
            set.Evaluate(PNContentModule.AttackPower) == 100d, "source replacement updates scale/resources and invalidates cache");
        set.RemoveSource(Source);
        Check(set.GetMax(PNContentModule.Health) == 100d && set.GetCurrent(PNContentModule.Health) == 40d &&
            set.Calculate(PNContentModule.Mitigation, 100d, PNContentModule.Fire) == 100d,
            "removing a source removes every route and restores resource policy");
        set.SetSource(Source, new[] { new EveStatModifier(PNContentModule.Fire, -2d, 1, PNContentModule.Mitigation) });
        Check(set.Calculate(PNContentModule.Mitigation, 100d, PNContentModule.Fire) == 0d,
            "built-in mitigation bounds excessive resistance at zero");
        var clone = new EveStatModifier(PNContentModule.Fire, -0.2d, 1, PNContentModule.Mitigation).Clone();
        Check(clone.CalculationId == PNContentModule.Mitigation && clone.Id == PNContentModule.Fire && clone.Group == 1 && Near(clone.Value, -0.2d),
            "modifier copying preserves calculation routing");
        var engine = new EveStatSet(hub.Stats);
        Reject(() => engine.SetSource(Source, new[] { new EveStatModifier(PNContentModule.AttackPower, 0.2d, 1, PNContentModule.Scale) }),
            "plain engine stat set does not silently apply game calculation routes");

        var fingerprint = EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:calculation");
        Check(fingerprint.ContentHashes.ContainsKey("registry:PNStatCalculationDefinition"), "calculation implementations participate in content hashes");
        var changed = Hub(typeof(PNTestChangedCalculation));
        Reject(() => fingerprint.Validate(EveContentCompatibility.Create(changed, Array.Empty<EveContentDocument>(), "test:calculation")),
            "coop rejects changed calculation implementation under the same ID");
        SnapshotRouting();
        FlatScaleRules();
        FlatOverrideRules();
    }

    private static void FlatOverrideRules()
    {
        var set = new PNFlatOverrideStatSet(Hub());
        set.AddBase(new EveBaseStat(PNContentModule.AttackPower, 100d));
        set.SetSource(Source, new[] { new EveStatModifier(PNContentModule.Fire, 10d, 0) });
        Check(set.Evaluate(PNContentModule.AttackPower, PNContentModule.Fire) == 200d && set.Calls == 1,
            "Evaluate retains PNStatSet Calculate override while excluding damage flat");
        Check(set.Calculate(PNContentModule.Scale, 100d, PNContentModule.Fire) == 220d,
            "raw Calculate override receives late flat result");
        set.ThrowOnCalculate = true;
        Reject(() => set.Evaluate(PNContentModule.Fire), "Evaluate propagates override failure");
        set.ThrowOnCalculate = false;
        Check(set.Calculate(PNContentModule.Scale, 100d, PNContentModule.Fire) == 220d,
            "failed Evaluate restores raw calculation mode");
        var hub = new EveRegistryHub();
        new PNContentModule().Register(hub);
        var nested = EveId.Parse("addon:nested_scale");
        hub.Calculations().Register(new PNStatCalculationDefinition(nested, typeof(PNFlatNestedCalculation)));
        hub.Calculations().RequestOverride(EveId.Parse("base:scale"), nested, "addon", "1", new[] { "project_n" });
        hub.Seal();
        var nestedSet = new PNStatSet(hub);
        nestedSet.AddBase(new EveBaseStat(PNContentModule.AttackPower, 100d));
        nestedSet.SetSource(Source, new[] { new EveStatModifier(PNContentModule.Fire, 10d, 0) });
        Check(nestedSet.Evaluate(PNContentModule.AttackPower, PNContentModule.Fire) == 209d,
            "selected Scale override can perform nested raw calculations without leaking Evaluate mode");
    }

    private static void FlatScaleRules()
    {
        var set = new PNStatSet(Hub());
        set.AddBase(new EveBaseStat(PNContentModule.AttackPower, 100d));
        set.AddBase(new EveBaseStat(PNContentModule.Defense, 0d));
        set.AddBase(new EveResourceStat(PNContentModule.Health, 100d, 40d, EveMaxChangePolicy.PreserveRatio));
        set.SetSource(Source, new[] {
            new EveStatModifier(PNContentModule.AttackPower, 50d, 0),
            new EveStatModifier(PNContentModule.AttackPower, .2d, 1),
            new EveStatModifier(PNContentModule.Fire, .2d, 1),
            new EveStatModifier(PNContentModule.Physical, .4d, 1),
            new EveStatModifier(PNContentModule.Fire, 100d, 0),
            new EveStatModifier(PNContentModule.Defense, -5d, 0),
            new EveStatModifier(PNContentModule.Health, 50d, 0)
        });
        Check(Near(set.Calculate(PNContentModule.Scale, 900d, PNContentModule.Fire, PNContentModule.Physical), 1540d),
            "same-group percentages sum before late elemental flat");
        Check(Near(set.Calculate(PNContentModule.Scale, 100d, PNContentModule.AttackPower, PNContentModule.Fire), 290d),
            "default stat and tag flats both follow percentages");
        var helperContext = new PNStatCalculationContext(set, PNContentModule.Scale, 100d,
            new[] { PNContentModule.Fire }, new[] {
                new EveStatModifier(PNContentModule.Fire, 10d, 0), new EveStatModifier(PNContentModule.Fire, .2d, 1) });
        Check(Near(helperContext.ApplyGroups(true), 130d) && Near(helperContext.ApplyScaleGroups(), 130d),
            "both grouped helpers use default after placement");
        Check(Near(set.Evaluate(PNContentModule.AttackPower, PNContentModule.Fire), 190d) && set.Evaluate(PNContentModule.Fire) == 0d,
            "combined and tag-only Evaluate exclude damage flats but retain percentages");
        Check(set.Evaluate(PNContentModule.Defense) == -5d, "zero-valued base accepts signed stat flat");
        Check(set.GetMax(PNContentModule.Health) == 150d && set.GetCurrent(PNContentModule.Health) == 60d,
            "damage flats preserve resource ratio and base flat policy");
        Reject(() => set.SetSource(Source, new[] {
            new EveStatModifier(PNContentModule.Health, 500d, 0),
            new EveStatModifier(PNContentModule.Fire, 500d, 0),
            new EveStatModifier(PNContentIds.CritDamage, 50d, 0)
        }), "registered stat lacking entity base cannot become late flat in mixed source");
        Check(set.GetMax(PNContentModule.Health) == 150d && set.GetCurrent(PNContentModule.Health) == 60d &&
            Near(set.Calculate(PNContentModule.Scale, 900d, PNContentModule.Fire, PNContentModule.Physical), 1540d),
            "invalid mixed replacement leaves resource and late damage source intact");
        Check(Near(set.Calculate(EveId.Parse("base:scale"), 900d, EveId.Parse("base:fire"), PNContentModule.Fire, PNContentModule.Physical), 1540d),
            "flat aliases and duplicate selected IDs apply once");
        Check(Near(set.Calculate(PNContentModule.Scale, 100d, PNContentModule.Ice), 100d), "unselected flat cannot leak");
        var other = EveId.Parse("test:flat_second");
        set.SetSource(other, new[] { new EveStatModifier(PNContentModule.Fire, 20d, 0), new EveStatModifier(PNContentModule.Fire, .5d, 2) });
        Check(Near(set.Calculate(PNContentModule.Scale, 900d, PNContentModule.Fire, PNContentModule.Physical), 2280d),
            "different percentage groups multiply before summing all late flats");
        set.RemoveSource(other);
        Check(Near(set.Calculate(PNContentModule.Scale, 900d, PNContentModule.Fire, PNContentModule.Physical), 1540d) &&
            Near(set.Evaluate(PNContentModule.AttackPower, PNContentModule.Fire), 190d), "source removal refreshes stat caches and raw calculations");
        set.SetSource(other, new[] { new EveStatModifier(PNContentModule.Fire, -5000d, 0) });
        Check(set.Calculate(PNContentModule.Scale, 100d, PNContentModule.Fire) == 0d, "negative late flat clamps final Scale at zero");
        set.RemoveSource(other);
        set.SetSource(other, new[] { new EveStatModifier(PNContentModule.Fire, double.MaxValue, 0), new EveStatModifier(PNContentModule.Fire, double.MaxValue, 0) });
        try { set.Calculate(PNContentModule.Scale, 100d, PNContentModule.Fire); throw new Exception("Expected flat overflow."); }
        catch (OverflowException) { Check(true, "late flat overflow rejected"); }
        Check(Near(set.Evaluate(PNContentModule.AttackPower, PNContentModule.Fire), 190d), "even overflowing damage flats do not enter stat evaluation");
        set.RemoveSource(other);
        set.RemoveSource(Source);
        Check(set.Evaluate(PNContentModule.AttackPower, PNContentModule.Fire) == 100d && set.GetCurrent(PNContentModule.Health) == 40d,
            "removing mixed flat source restores cache and resource values");
    }

    private static EveRegistryHub Hub(Type customImplementation = null)
    {
        var hub = new EveRegistryHub();
        new PNContentModule().Register(hub);
        hub.Calculations().Register(new PNStatCalculationDefinition(Custom, customImplementation ?? typeof(PNTestCustomCalculation)));
        hub.Seal();
        return hub;
    }

    private static void SnapshotRouting()
    {
        var runtime = new EveGameRuntime();
        var first = Entity();
        var next = Entity();
        runtime.Initialize();
        try
        {
            first.loader.Enable();
            var identity = first.identity.Id;
            first.stat.SetPersistentSource(Source, new[] {
                new EveStatModifier(PNContentModule.Fire, 0.2d, 1, PNContentModule.Scale),
                new EveStatModifier(PNContentModule.Fire, -0.2d, 1, PNContentModule.Mitigation),
                new EveStatModifier(PNContentModule.Fire, 10d, 0, PNContentModule.Scale)
            });
            first.stat.SetSource(EveId.Parse("test:temporary"), new[] {
                new EveStatModifier(PNContentModule.Fire, -0.5d, 2, PNContentModule.Mitigation)
            });
            var snapshot = first.stat.EndFloorAndCaptureSnapshot();
            Check(snapshot.PersistentSources[Source][0].CalculationId == PNContentModule.Scale &&
                snapshot.PersistentSources[Source][1].CalculationId == PNContentModule.Mitigation &&
                Near(first.stat.Calculate(PNContentModule.Mitigation, 100d, PNContentModule.Fire), 80d),
                "floor capture removes temporary routes and preserves persistent calculation IDs");
            first.loader.Unload();
            next.identity.Assign(identity);
            next.loader.Enable();
            next.stat.RestoreFloorSnapshot(snapshot);
            Check(Near(next.stat.Calculate(PNContentModule.Scale, 100d, PNContentModule.Fire), 130d) &&
                Near(next.stat.Calculate(PNContentModule.Mitigation, 100d, PNContentModule.Fire), 80d),
                "floor restore retains separate scale and mitigation contributions");
        }
        finally
        {
            first.loader.Unload();
            next.loader.Unload();
            runtime.EndSession();
        }
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 0.001d;

    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (ArgumentException) { Check(true, name); return; }
        catch (InvalidOperationException) { Check(true, name); return; }
        catch (KeyNotFoundException) { Check(true, name); return; }
        throw new Exception("Expected rejection: " + name);
    }
}
