using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;
using static EveSessionSmokeTests;

// EN: Exposes owner destruction to test recovery after its GameObject is gone.
// VI: Cho phép mô phỏng hủy owner để kiểm tra phục hồi khi GameObject đã mất.
public class EveCleanupTestRuntime : EveGameRuntime
{
    public void DestroyOwner() => OnDestroy();
}

public class EveCountingBaseStat : EveBaseStat
{
    public int Reads { get; private set; }
    public override double Value { get { Reads++; return base.Value; } }
    public EveCountingBaseStat(EveId id, double value) : base(id, value) { }
}

public class EveCountingStatSet : EveStatSet
{
    public int EvaluateCalls { get; private set; }
    public EveCountingStatSet(EveRegistry<EveStatDefinition> definitions) : base(definitions) { }
    public override double Evaluate(IEnumerable<EveId> statIds)
    {
        EvaluateCalls++;
        return base.Evaluate(statIds);
    }
}

// EN: Regression checks for session-bound snapshots, immutable lists and retained failed cleanup.
// VI: Kiểm tra snapshot gắn với phiên, danh sách bất biến và giữ lại cleanup lỗi.
public static class EveDataSafetyTests
{
    public static void Run()
    {
        StatCache();
        ProjectStatBounds();
        SnapshotSessions();
        DefinitionLists();
        CleanupRecovery(false);
        CleanupRecovery(true);
    }

    private static void StatCache()
    {
        EveId attack = EveId.Parse("test:attack");
        EveId bonus = EveId.Parse("test:bonus");
        EveId source = EveId.Parse("test:buff");
        var registry = new EveRegistry<EveStatDefinition>();
        registry.Register(new EveStatDefinition(attack, EveStatKind.Number));
        registry.Register(new EveStatDefinition(bonus, EveStatKind.Number));
        registry.Seal();
        var set = new EveCountingStatSet(registry);
        var baseAttack = new EveCountingBaseStat(attack, 100d);
        set.AddBase(baseAttack);

        Check(set.Evaluate(attack) == 100d && set.Evaluate(attack) == 100d && baseAttack.Reads == 1,
            "repeated single-stat reads use cached value");
        Check(set.EvaluateCalls == 2, "single-ID fast path preserves derived Evaluate override");
        set.AddBase(new EveBaseStat(bonus, 10d));
        Check(set.Evaluate(attack) == 100d && baseAttack.Reads == 2,
            "adding a base stat invalidates cached values");
        Check(set.Evaluate(attack, bonus) == 110d && set.Evaluate(bonus, attack, attack) == 110d &&
            baseAttack.Reads == 3, "combined cache ignores ID order and duplicates");

        set.SetSource(source, new[] { new EveStatModifier(attack, 0.2d, 1) });
        Check(set.Evaluate(attack) == 120d && set.Evaluate(attack, bonus) == 132d,
            "adding a source invalidates single and combined values");
        set.SetSource(source, new[] { new EveStatModifier(attack, 0.5d, 1) });
        Check(set.Evaluate(attack) == 150d, "replacing a source invalidates cached values");
        set.RemoveSource(source);
        Check(set.Evaluate(attack) == 100d && set.Evaluate(attack, bonus) == 110d,
            "removing a source restores cached expressions to their base values");

        EveId a = EveId.Parse("test:a");
        EveId b = EveId.Parse("test:b");
        EveId c = EveId.Parse("test:c");
        var orderRegistry = new EveRegistry<EveStatDefinition>();
        orderRegistry.Register(new EveStatDefinition(a, EveStatKind.Number));
        orderRegistry.Register(new EveStatDefinition(b, EveStatKind.Number));
        orderRegistry.Register(new EveStatDefinition(c, EveStatKind.Number));
        orderRegistry.Seal();
        var ordered = new EveStatSet(orderRegistry);
        ordered.AddBase(new EveBaseStat(a, 1e20d));
        ordered.AddBase(new EveBaseStat(b, -1e20d));
        ordered.AddBase(new EveBaseStat(c, 1d));
        Check(ordered.Evaluate(a, b, c) == ordered.Evaluate(c, b, a),
            "combined base sum is independent of requested ID order");

        var hub = new EveRegistryHub();
        bool missingRejected = false;
        try { hub.Get<EveStatDefinition>(); }
        catch (KeyNotFoundException) { missingRejected = true; }
        Check(missingRejected && hub.Snapshot().Count == 0, "read access does not create a missing registry");
        var writable = hub.GetOrCreate<EveStatDefinition>();
        hub.Seal();
        Check(ReferenceEquals(hub.Get<EveStatDefinition>(), writable), "sealed registry remains readable");
        Reject(() => hub.GetOrCreate<EveStatDefinition>(), "sealed hub rejects registration access");
    }

    private static void SnapshotSessions()
    {
        var runtime = new EveHostTestRuntime(); runtime.Initialize();
        var oldSession = EveRegistrySession.Context;
        var first = Entity();
        oldSession.Coop.SpawnHost(() => first.loader, EveId.Parse("project_n:eve"));
        first.stat.SetCurrent(PNContentModule.Health, 15);
        var snapshot = first.stat.EndFloorAndCaptureSnapshot();
        Check(snapshot.SessionId == oldSession.SessionId, "floor snapshot records active session");
        runtime.EndSession();
        Reject(() => first.stat.EndFloorAndCaptureSnapshot(), "cannot capture snapshot from ended session");

        runtime.Initialize();
        var newSession = EveRegistrySession.Context;
        var second = Entity();
        newSession.Coop.SpawnHost(() => second.loader, EveId.Parse("project_n:eve"));
        Check(second.identity.Id == snapshot.EntityId && second.stat.CharacterId == snapshot.CharacterId,
            "host ID and character can repeat across sessions");
        Reject(() => second.stat.RestoreFloorSnapshot(snapshot), "snapshot from old session rejected despite matching IDs");
        Check(second.stat.GetCurrent(PNContentModule.Health) == 40 && second.stat.Set.SourceSnapshot().Count == 0,
            "rejected stale snapshot leaves target untouched");
        runtime.EndSession();
    }

    private static void ProjectStatBounds()
    {
        var hub = new EveRegistryHub();
        new PNContentModule().Register(hub);
        hub.Seal();
        var registry = hub.Stats;
        var game = new PNStatSet(hub);
        var engine = new EveStatSet(registry);
        game.AddBase(new EveBaseStat(PNContentModule.AttackPower, 100d));
        engine.AddBase(new EveBaseStat(PNContentModule.AttackPower, 100d));
        EveId source = EveId.Parse("test:deep_reduction");
        var reductions = new[]
        {
            new EveStatModifier(PNContentModule.AttackPower, -1.5d, 1),
            new EveStatModifier(PNContentModule.AttackPower, -1.5d, 2)
        };
        game.SetSource(source, reductions);
        engine.SetSource(source, reductions);
        Check(game.Evaluate(PNContentModule.AttackPower) == 0d && engine.Evaluate(PNContentModule.AttackPower) == 25d,
            "Core bounds each percentage group while the engine retains its generic formula");
        game.SetSource(source, new[] { new EveStatModifier(PNContentModule.AttackPower, -150d, 0) });
        Check(game.Evaluate(PNContentModule.AttackPower) == 0d, "Core bounds a negative effective base");
        game.SetSource(source, new[] { new EveStatModifier(PNContentModule.AttackPower, 0.2d, 1) });
        Check(game.Evaluate(PNContentModule.AttackPower) == 120d, "Core keeps ordinary positive stat bonuses");
    }

    private static void DefinitionLists()
    {
        var input = new List<EveInitialStat> { new EveInitialStat(PNContentModule.Health, 100) };
        var character = new EveCharacterDefinition(EveId.Parse("test:hero"), input);
        var registry = new EveRegistryHub();
        registry.GetOrCreate<EveCharacterDefinition>().Register(character);
        registry.Seal();
        var before = EveContentCompatibility.Create(registry, Array.Empty<EveContentDocument>(), "1");
        input.Clear();
        Check(character.Stats.Count == 1 && !(character.Stats is List<EveInitialStat>), "definition copies input and hides mutable list");
        var cast = (IList<EveInitialStat>)character.Stats;
        Check(cast.IsReadOnly, "definition exposes an actually read-only collection");
        Reject(() => cast.Clear(), "cannot clear published stats through interface cast");
        Reject(() => cast[0] = new EveInitialStat(PNContentModule.Health, 999), "cannot replace published stat");
        before.Validate(EveContentCompatibility.Create(registry, Array.Empty<EveContentDocument>(), "1"));
        Check(character.Stats[0].BaseValue == 100, "definition and fingerprint remain unchanged");
    }

    private static void CleanupRecovery(bool destroyOwner)
    {
        var runtime = new EveCleanupTestRuntime(); runtime.Initialize();
        var context = EveRegistrySession.Context;
        var faulty = Entity();
        var healthy = Entity();
        bool fails = true;
        int attempts = 0;
        var step = new EveProbeStep
        {
            Requires = new[] { typeof(EveStat) },
            OnUnload = () => { attempts++; if (fails) throw new InvalidOperationException("Intentional cleanup error."); }
        };
        var components = faulty.loader.Components.Concat(new MonoBehaviour[] { step }).ToArray();
        foreach (var component in components) component.Components = components;
        faulty.loader.Load(context.Registry);
        healthy.loader.Load(context.Registry);
        if (destroyOwner) runtime.DestroyOwner();
        else Reject(() => runtime.EndSession(), "failed session cleanup reported to caller");
        Check(EveRegistrySession.State == EveSessionState.Stopping && !EveRegistrySession.IsReady && !context.IsActive,
            "cleanup failure keeps session stopped for gameplay but pending teardown");
        Check(EveRegistrySession.PendingCleanupCount == 1 && context.Entities.Count == 1 && EveRegistrySession.LastError != null,
            "failed loader retained while successful cleanup removed");
        Check(healthy.loader.State == EveEntityLoadState.Idle && !healthy.stat.IsReady,
            "other entities still unload after one failure");
        Reject(() => runtime.Initialize(), "same runtime cannot restart while cleanup pending");
        var replacement = new EveGameRuntime();
        Reject(() => replacement.Initialize(), "another runtime cannot bypass pending cleanup");
        Reject(() => EveRegistrySession.RetryCleanup(), "retry still reports unresolved cleanup");
        Check(EveRegistrySession.PendingCleanupCount == 1 && attempts == 2, "failed retry keeps cleanup reachable");
        fails = false;
        if (destroyOwner) EveRegistrySession.RetryCleanup();
        else runtime.EndSession();
        Check(EveRegistrySession.State == EveSessionState.Stopped && EveRegistrySession.PendingCleanupCount == 0 &&
            context.Entities.Count == 0 && EveRegistrySession.LastError == null && attempts == 3,
            "successful retry finishes retained cleanup");
        replacement.Initialize();
        Check(EveRegistrySession.IsReady && EveRegistrySession.Context.SessionId != context.SessionId,
            "new owner can start only after cleanup succeeds");
        replacement.EndSession();
    }
}
