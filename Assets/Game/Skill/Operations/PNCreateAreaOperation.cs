using System;
using EveEngine;
using Newtonsoft.Json.Linq;

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
        var admission = PNSkillObjectSpawn.Admit(context);
#if UNITY_5_3_OR_NEWER
        if (admission == PNSkillSpawnAdmission.Predicted) { PNSkillObjectSpawn.CreateAreaGuess(context, token); return; }
#endif
        if (admission != PNSkillSpawnAdmission.Authority) return;
#if UNITY_5_3_OR_NEWER
        Result = PNSkillObjectSpawn.CreateArea(context, token);
        if (Result.Status == PNAreaStatus.Failed) throw new InvalidOperationException("Area creation failed.", Result.Error);
#else
        // Standalone runner validates invocation/payload only; it has no Unity area runtime.
#endif
    }
    public override void Dispose() { executed = true; }
}
