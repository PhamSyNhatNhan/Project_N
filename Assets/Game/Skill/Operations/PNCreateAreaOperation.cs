using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class PNCreateAreaOperation : PNSkillOperation
{
    private bool executed;
#if UNITY_5_3_OR_NEWER
    public PNAreaReceipt Result { get; private set; }
#endif
    public static void Validate(JToken token, EveRegistryHub hub)
    {
        var data = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(data, "areaId", "input", "followCaster");
        hub.Instances().GetEffectiveKind<PNAreaDefinition>(EveId.Parse(PNBuffContentBatch.String(data, "areaId"))).ValidateInput(data["input"]);
        PNBuffContentBatch.Bool(data, "followCaster");
    }
    public override void Execute(PNSkillActionContext context, JToken token)
    {
        if (executed || context == null || !context.Skill.IsReady || !context.Position.HasValue) throw new InvalidOperationException("Fresh Ready position context required.");
        Validate(token, context.Registry); executed = true;
#if UNITY_5_3_OR_NEWER
        var data = (JObject)token; var caster = context.Skill.GetComponent<PNStat>();
        var manager = PNAreaManager.Resolve(EveRegistrySession.Context, caster.gameObject.scene);
        if (!ReferenceEquals(manager.Session.Registry, context.Registry)) throw new InvalidOperationException("Area operation session mismatch.");
        Result = manager.Create(new PNAreaCreateRequest(EveId.Parse(PNBuffContentBatch.String(data, "areaId")), data["input"],
            PNCombatSource.Capture(caster), caster.gameObject.scene, context.Position.Value, caster,
            PNBuffContentBatch.Bool(data, "followCaster") ? caster : null));
        if (Result.Status == PNAreaStatus.Failed) throw new InvalidOperationException("Area creation failed.", Result.Error);
#else
        // Standalone runner validates invocation/payload only; it has no Unity area runtime.
#endif
    }
    public override void Dispose() { executed = true; }
}
