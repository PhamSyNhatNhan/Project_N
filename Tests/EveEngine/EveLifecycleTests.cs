using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using EveEngine;
using UnityEngine;
using static EveSessionSmokeTests;

// EN: Controllable step for failure, dependency and rollback checks.
// VI: Bước có thể điều khiển để kiểm tra lỗi, phụ thuộc và hoàn tác.
public class EveProbeStep : MonoBehaviour, EveIEntityLoadStep
{
    public Type[] Requires = Array.Empty<Type>();
    public Action OnLoad = () => { };
    public Action OnUnload = () => { };
    public int LoadOrder => 0;
    public IEnumerable<Type> Dependencies => Requires;
    public void Load(EveEntityLoadContext context) => OnLoad();
    public void Unload() => OnUnload();
}
// EN: Second distinct module type for dependency graphs.
// VI: Loại module thứ hai để kiểm tra đồ thị phụ thuộc.
public class EveOtherProbeStep : EveProbeStep { }
// EN: Host test runtime using the actual game content configuration.
// VI: Runtime host kiểm thử dùng cấu hình nội dung game thật.
public class EveHostTestRuntime : EveGameRuntime { public override EveSessionRole Role => EveSessionRole.Host; }
// EN: Client test runtime using the actual game content configuration.
// VI: Runtime client kiểm thử dùng cấu hình nội dung game thật.
public class EveClientTestRuntime : EveGameRuntime { public override EveSessionRole Role => EveSessionRole.Client; }
// EN: Runtime that fails once to test recovery without a stranded owner.
// VI: Runtime lỗi một lần để kiểm tra phục hồi mà không kẹt owner.
public class EveFailureTestRuntime : EveGameRuntime
{
    public bool Fail = true;
    protected override IEnumerable<EveContentDocument> ReadContentDocuments()
    {
        if (Fail) throw new FormatException("Intentional content failure.");
        return base.ReadContentDocuments();
    }
}
// EN: Regression checks for the new session, loader and transport-independent coop contracts.
// VI: Kiểm tra hồi quy hợp đồng phiên, loader và coop độc lập transport mới.
public static class EveLifecycleTests
{
    private static void Attach(EveEntityLoader loader, params MonoBehaviour[] extra)
    {
        var components = loader.Components.Concat(extra).ToArray();
        foreach (var component in components) component.Components = components;
    }

    public static void Run()
    {
        var failed = new EveFailureTestRuntime();
        Reject(() => failed.Initialize(), "bootstrap failure reported");
        Check(EveRegistrySession.State == EveSessionState.Failed && EveRegistrySession.LastError != null, "session failure state retained");
        var runtime = new EveGameRuntime();
        int readyAfterFailure = 0;
        using (EveEventManager.Global.Subscribe<EveRuntimeReady>(EveRuntimeEventIds.Ready, (_, __) => throw new Exception("listener failure")))
        using (EveEventManager.Global.Subscribe<EveRuntimeReady>(EveRuntimeEventIds.Ready, (_, __) => readyAfterFailure++))
            runtime.Initialize();
        Check(EveRegistrySession.IsReady && readyAfterFailure == 1, "different runtime retries and all readiness listeners run");
        var session = EveRegistrySession.Context;
        EveMoveTests.Run(session);
        EveTimeScaleTests.Run(session);

        var entity = Entity();
        var trace = new List<string>();
        bool failLoad = true;
        var probe = new EveProbeStep
        {
            Requires = new[] { typeof(EveTestStep) },
            OnLoad = () => { trace.Add("load"); if (failLoad) throw new InvalidOperationException("step failure"); },
            OnUnload = () => { Check(entity.stat.IsInitialized, "reverse rollback keeps stat until dependent unload"); trace.Add("unload"); }
        };
        Attach(entity.loader, probe);
        Reject(() => entity.loader.Load(session.Registry), "partial load throws");
        Check(!entity.stat.IsReady && entity.loader.State == EveEntityLoadState.Failed && session.Entities.Count == 0,
            "failed step and prior stat rolled back, directory released");
        Check(trace.SequenceEqual(new[] { "load", "unload" }), "failing step itself receives unload");
        failLoad = false;
        entity.loader.Load(session.Registry);
        Check(entity.loader.IsReady && entity.step.Calls == 2, "clean retry succeeds after rollback");
        entity.loader.Unload();
        Check(!entity.stat.IsReady && entity.loader.State == EveEntityLoadState.Idle, "explicit unload resets stats");

        var missing = Entity();
        Attach(missing.loader, new EveProbeStep { Requires = new[] { typeof(EveOtherProbeStep) } });
        Reject(() => missing.loader.Load(session.Registry), "missing dependency");
        Check(!missing.stat.IsReady, "dependency validation precedes mutation");
        var cyclic = Entity();
        Attach(cyclic.loader, new EveProbeStep { Requires = new[] { typeof(EveOtherProbeStep) } },
            new EveOtherProbeStep { Requires = new[] { typeof(EveTestStep), typeof(EveProbeStep) } });
        Reject(() => cyclic.loader.Load(session.Registry), "ambiguous dependency rejected");
        var selfCycle = Entity();
        Attach(selfCycle.loader, new EveOtherProbeStep { Requires = new[] { typeof(EveOtherProbeStep) } });
        Reject(() => selfCycle.loader.Load(session.Registry), "self cycle rejected");

        var cleanup = Entity();
        bool failCleanup = true;
        Attach(cleanup.loader, new EveProbeStep
        {
            Requires = new[] { typeof(EveStat) },
            OnLoad = () => throw new InvalidOperationException("load"),
            OnUnload = () => { if (failCleanup) throw new InvalidOperationException("cleanup"); }
        });
        Reject(() => cleanup.loader.Load(session.Registry), "cleanup failure retained");
        Check(!cleanup.stat.IsReady && cleanup.loader.LastError is AggregateException, "remaining rollback continues after cleanup error");
        Reject(() => cleanup.loader.Load(session.Registry), "cannot retry with unfinished cleanup");
        failCleanup = false;
        cleanup.loader.Unload();
        Check(cleanup.loader.State == EveEntityLoadState.Idle, "failed cleanup can be retried explicitly");

        var first = Entity(); first.identity.Assign(new EveEntityId(100)); first.loader.Load(session.Registry);
        var duplicate = Entity(); duplicate.identity.Assign(new EveEntityId(100));
        Reject(() => duplicate.loader.Load(session.Registry), "duplicate entity rejected");
        Check(session.Entities.TryGet(first.identity.Id, out var found) && ReferenceEquals(found, first.loader), "duplicate rollback preserves original registration");
        first.loader.Disable();
        runtime.EndSession();
        Check(!first.stat.IsReady && !session.IsActive && session.Entities.Count == 0, "session end also unloads disabled entities");
        runtime.Initialize();
        Check(EveRegistrySession.Context.SessionId != session.SessionId, "same runtime can start a fresh session");
        Reject(() => first.loader.Load(EveRegistrySession.Current), "old identity rejected across sessions");
        first.loader.Unload(); first.identity.ResetIdentity(); first.loader.Load(EveRegistrySession.Current);
        Check(first.loader.IsReady, "explicit reset permits pooled entity in new session");
        Check(!session.IsActive, "old context stays invalid");
        Fingerprints(EveRegistrySession.Current);
        runtime.EndSession();
        Coop();
    }

    private static void Fingerprints(EveRegistryHub registry)
    {
        var a = new[] { new EveContentDocument("C:/a.json", "{\"a\":1,\"b\":[2,3]}", "test") };
        var b = new[] { new EveContentDocument("F:/b.json", "{ \"b\": [2,3], \"a\": 1 }", "test") };
        var first = EveContentCompatibility.Create(registry, a, "rules1");
        first.Validate(EveContentCompatibility.Create(registry, b, "rules1"));
        Check(true, "fingerprint ignores absolute paths whitespace and property order");
        first.Validate(JsonConvert.DeserializeObject<EveContentCompatibility>(JsonConvert.SerializeObject(first)));
        Check(true, "compatibility survives wire serialization");
        var changed = new[] { new EveContentDocument("b", "{\"a\":2,\"b\":[2,3]}", "test") };
        Reject(() => first.Validate(EveContentCompatibility.Create(registry, changed, "rules1")), "changed content rejected");
        Reject(() => first.Validate(EveContentCompatibility.Create(registry, a, "rules2")), "changed executable rules version rejected");
        Reject(() => first.Validate(new EveContentCompatibility(2, "rules1", first.ContentHashes.ToDictionary(p => p.Key, p => p.Value))), "protocol mismatch rejected");
        var otherRegistry = new EveContentBootstrap().Build(Array.Empty<EveIContentModule>(), Array.Empty<EveContentDocument>());
        Reject(() => first.Validate(EveContentCompatibility.Create(otherRegistry, a, "rules1")), "code-registered definitions included in hash");
        var m1 = new[] { new EveContentDocument("m", "{\"version\":\"1.0\"}", "empty_pack", true) };
        var m2 = new[] { new EveContentDocument("m", "{\"version\":\"2.0\"}", "empty_pack", true) };
        Reject(() => EveContentCompatibility.Create(registry, m1, "1").Validate(EveContentCompatibility.Create(registry, m2, "1")), "empty pack manifest version included");
    }

    private static void Coop()
    {
        var runtime = new EveHostTestRuntime(); runtime.Initialize();
        var session = EveRegistrySession.Context;
        var coop = session.Coop;
        var pending = Entity(); pending.loader.Enable();
        Check(!pending.identity.HasId && !pending.loader.IsReady, "network entity waits without allocating local ID");
        pending.loader.Disable();
        Reject(() => pending.loader.Load(session.Registry), "network load requires host ID");
        coop.AdmitPeer(10, session.Compatibility); coop.AdmitPeer(20, session.Compatibility);
        Reject(() => coop.AdmitPeer(10, session.Compatibility), "duplicate admission cannot reset sequence");
        EveId characterId = EveId.Parse("project_n:eve");
        var a = coop.SpawnHost(() => Entity().loader, characterId, 10);
        a = JsonConvert.DeserializeObject<EveEntitySpawnMessage>(JsonConvert.SerializeObject(a));
        var b = coop.SpawnHost(() => Entity().loader, characterId, 20);
        Check(a.EntityId > 0 && b.EntityId > a.EntityId && a.CharacterId == b.CharacterId, "host allocates distinct IDs for same character");
        Check(session.Entities.TryGet(new EveEntityId(a.EntityId), out var entity) && entity.gameObject.activeInHierarchy, "host activates only initialized entity");
        Reject(() => coop.SpawnHost(() => Entity().loader, characterId, 99), "unadmitted owner rejected");
        var active = Entity(); active.loader.gameObject.SetActive(true);
        Reject(() => coop.SpawnHost(() => active.loader, characterId), "active factory rejected");
        int accepted = 0;
        EveId commandId = EveId.Parse("test:command");
        coop.RegisterCommand(commandId, (_, payload) => accepted++);
        var command = new EveEntityCommand(session.SessionId, a.EntityId, 1, commandId.ToString(), "{}");
        Reject(() => coop.HandleCommand(20, command), "other owner rejected");
        Reject(() => coop.HandleCommand(99, command), "unadmitted sender rejected");
        coop.HandleCommand(10, command);
        Check(accepted == 1, "host executes admitted owner's registered command");
        Reject(() => coop.HandleCommand(10, command), "replayed command rejected");
        Reject(() => coop.HandleCommand(10, new EveEntityCommand(Guid.NewGuid(), a.EntityId, 2, commandId.ToString(), "{}")), "stale session command rejected");
        Reject(() => coop.HandleCommand(10, new EveEntityCommand(session.SessionId, a.EntityId, 2, "test:unknown", "{}")), "unknown command rejected");
        coop.RemovePeer(10);
        Reject(() => coop.AdmitPeer(10, session.Compatibility), "reconnect cannot reuse peer ID to replay old commands");
        Reject(() => coop.HandleCommand(10, new EveEntityCommand(session.SessionId, a.EntityId, 2, commandId.ToString(), "{}")), "disconnected peer rejected");
        runtime.EndSession();
        Reject(() => coop.SpawnHost(() => Entity().loader, characterId), "old coop context rejected");

        var client = new EveClientTestRuntime(); client.Initialize();
        var clientSession = EveRegistrySession.Context;
        Reject(() => clientSession.Entities.AllocateHostId(), "client cannot allocate host ID");
        Reject(() => clientSession.Coop.ApplyHostSpawn(a, () => Entity().loader), "spawn before handshake rejected");
        clientSession.Coop.AdmitHost(session.SessionId, session.Compatibility);
        var replica = clientSession.Coop.ApplyHostSpawn(a, () => Entity().loader);
        Check(replica.IsReady && replica.GetComponent<EveEntityIdentity>().Id.Value == a.EntityId, "client loads same host entity identity");
        Reject(() => clientSession.Coop.ApplyHostSpawn(a, () => Entity().loader), "duplicate spawn rejected");
        Reject(() => clientSession.Coop.ApplyHostSpawn(new EveEntitySpawnMessage(Guid.NewGuid(), 900, a.CharacterId, 10), () => Entity().loader), "foreign host session rejected");
        Reject(() => clientSession.Coop.HandleCommand(10, command), "client cannot execute authoritative commands");
        Check(clientSession.Entities.TryGet(new EveEntityId(a.EntityId), out var surviving) && ReferenceEquals(replica, surviving), "duplicate failure leaves original replica intact");
        client.EndSession();
    }
}
