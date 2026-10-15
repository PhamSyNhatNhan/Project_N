using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static EveSessionSmokeTests;

public class PNProbeAction : PNSkillAction
{
    public int Calls;
    public override void Start(PNSkillActionContext context) { Calls++; }
    public override void Perform(PNSkillActionContext context)
    {
        using (var operation = CreateOperation(context, EveId.Parse("test:operation")))
            operation.Execute(context, context.Data.Action.Perform);
        Calls++;
    }
    public override void Exit(PNSkillActionContext context) { Calls++; }
    public override void Cancel(PNSkillActionContext context) { Calls++; }
}
public class PNProbeOperation : PNSkillOperation
{
    public override void Execute(PNSkillActionContext context, JToken data) { }
}
public class PNProbeModule : PNContentModule
{
    public override void Register(EveRegistryHub hub)
    {
        base.Register(hub);
        hub.Actions().Register(new PNSkillActionDefinition(EveId.Parse("test:action"), typeof(PNProbeAction)));
        hub.Operations().Register(new PNSkillOperationDefinition(EveId.Parse("test:operation"), typeof(PNProbeOperation)));
    }
}

public static class PNSkillTests
{
    public static void Run()
    {
        var payload = JObject.Parse("{\"skill\":{\"combo\":3},\"action\":{\"id\":\"test:action\",\"start\":{},\"perform\":{\"speed\":12},\"exit\":{},\"cancel\":{}}}");
        var parsed = PNSkillData.Parse(payload);
        payload["action"]["perform"]["speed"] = 99;
        var returned = parsed.Action.Perform;
        returned["speed"] = 100;
        Check(parsed.Action.Perform["speed"].Value<int>() == 12, "Core payload isolates source and read aliases");
        Check(PNSkillData.Parse(null) == null, "Core allows data-less skills");
        Check(PNSkillData.Parse(JObject.Parse("{\"action\":{\"id\":\"test:action\"}}" )).Action.Cancel == null,
            "omitted hook payloads normalize to null");
        Reject(() => PNSkillData.Parse(new JArray()), "Core rejects non-object envelopes");
        Reject(() => PNSkillData.Parse(JObject.Parse("{\"action\":{\"id\":\"test:action\",\"cancle\":{}}}")), "Core rejects misspelled hook fields");
        Reject(() => new PNSkillActionDefinition(EveId.Parse("test:invalid"), typeof(PNSkillAction)), "abstract action cannot register");
        Reject(() => new PNSkillOperationDefinition(EveId.Parse("test:invalid"), typeof(PNProbeAction)), "wrong operation type cannot register");

        string json = "{\"schemaVersion\":1,\"characters\":[{\"id\":\"test:hero\",\"skill\":[{\"id\":\"test:shot\",\"data\":" +
            parsedToJson() + "}]}]}";
        var hub = new EveContentBootstrap().Build(new[] { new PNProbeModule() },
            new[] { new EveContentDocument("core.json", json, "test") });
        var compatibility = EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:core");
        compatibility.Validate(EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:core"));
        Check(hub.Actions().IsSealed && hub.Operations().IsSealed, "execution registries seal with content");
        Check(!ReferenceEquals(hub.Operations().Get(EveId.Parse("test:operation")).Create(),
            hub.Operations().Get(EveId.Parse("test:operation")).Create()), "operations are fresh instances");
        Reject(() => hub.Actions().Register(new PNSkillActionDefinition(EveId.Parse("test:late"), typeof(PNProbeAction))), "no late action registration");
        EveRegistrySession.Begin();
        EveRegistrySession.Start(hub, EveSessionRole.Offline, compatibility);
        try
        {
            var identity = new EveEntityIdentity();
            identity.Assign(new EveEntityId(809), EveId.Parse("test:hero"), 0);
            var loader = new EveTestLoader();
            var skill = new PNSkill();
            MonoBehaviour[] components = { identity, loader, skill };
            foreach (var component in components) component.Components = components;
            Reject(() => skill.CreateAction(EveId.Parse("test:shot"), out _), "cannot execute before Ready");
            loader.Load(hub);
            var first = (PNProbeAction)skill.CreateAction(EveId.Parse("test:shot"), out var context);
            Check(!context.Direction.HasValue, "generic action does not require a direction");
            using (var aimed = skill.CreateAction(EveId.Parse("test:shot"), out var aimedContext,
                new Vector3(.4f, 0f, .8f)))
                Check(System.Math.Abs(aimedContext.Direction.Value.x - .4472136f) < .0001f &&
                    System.Math.Abs(aimedContext.Direction.Value.z - .8944272f) < .0001f,
                    "skill invocation snapshots normalized explicit direction");
            Reject(() => skill.CreateAction(EveId.Parse("test:shot"), out _, Vector3.zero),
                "explicit direction rejects zero");
            Reject(() => skill.CreateAction(EveId.Parse("test:shot"), out _, new Vector3(float.NaN, 0f, 1f)),
                "explicit direction rejects NaN");
            Reject(() => skill.CreateAction(EveId.Parse("test:shot"), out _, new Vector3(1f, float.PositiveInfinity, 0f)),
                "explicit direction rejects infinity");
            using (var aimed = skill.CreateAction(EveId.Parse("test:shot"), out var largeContext,
                new Vector3(float.MaxValue, 0f, float.MaxValue)))
                Check(System.Math.Abs(largeContext.Direction.Value.x - .7071068f) < .0001f,
                    "large finite aim vectors normalize without float overflow");
            var second = (PNProbeAction)skill.CreateAction(EveId.Parse("test:shot"), out _);
            Check(!ReferenceEquals(first, second) && first.Calls == 0, "creating actions does not transition hooks or share state");
            first.Start(context); first.Perform(context); first.Exit(context);
            Check(first.Calls == 3 && second.Calls == 0, "skill caller drives hooks and action resolves registered operations");
            first.Dispose(); second.Dispose();
            loader.Unload();
            Reject(() => skill.CreateAction(EveId.Parse("test:shot"), out _), "unload blocks further action creation");
        }
        finally { EveRegistrySession.End(); }

        var badHub = new EveContentBootstrap().Build(new[] { new PNProbeModule() },
            new[] { new EveContentDocument("bad.json", json.Replace("test:action", "test:missing"), "test") });
        EveRegistrySession.Begin();
        EveRegistrySession.Start(badHub, EveSessionRole.Offline,
            EveContentCompatibility.Create(badHub, Array.Empty<EveContentDocument>(), "test:core"));
        try
        {
            var identity = new EveEntityIdentity();
            identity.Assign(new EveEntityId(810), EveId.Parse("test:hero"), 0);
            var loader = new EveTestLoader();
            var skill = new PNSkill();
            MonoBehaviour[] components = { identity, loader, skill };
            foreach (var component in components) component.Components = components;
            Reject(() => loader.Load(badHub), "unknown action blocks entity Ready");
            Check(!skill.IsReady && loader.State == EveEntityLoadState.Failed, "Core parser failure rolls back entity");
        }
        finally { EveRegistrySession.End(); }
    }

    private static string parsedToJson() => "{\"skill\":{\"combo\":3},\"action\":{\"id\":\"test:action\",\"start\":{},\"perform\":{\"speed\":12},\"exit\":{},\"cancel\":{}}}";
}
