using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// Creates one blast at the context position (Docs/StrikeBlastPlan.md section 5). `dependency` is Independent, Owner or Action (see PNBlastDependency): the manager enforces Owner;
// an Action-dependent blast is ended by the creating Action through `Result.Handle` when the cast is cancelled or ends.
public class PNCreateBlastOperation : PNSkillOperation
{
    private bool executed;
#if UNITY_5_3_OR_NEWER
    public PNBlastReceipt Result { get; private set; }
#endif
    public static PNBlastDependency ParseDependency(JObject data)
    {
        var text = PNBuffContentBatch.String(data, "dependency");
        if (!Enum.TryParse(text, false, out PNBlastDependency dependency) || !Enum.IsDefined(typeof(PNBlastDependency), dependency)) throw new FormatException("Unknown blast dependency: " + text);
        return dependency;
    }
    public static void Validate(JToken token, EveRegistryHub hub)
    {
        var data = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(data, "blastId", "dependency", "input");
        ParseDependency(data);
        hub.Instances().GetEffectiveKind<PNBlastDefinition>(EveId.Parse(PNBuffContentBatch.String(data, "blastId"))).ValidateInput(data["input"]);
    }
    public override void Execute(PNSkillActionContext context, JToken token)
    {
        if (executed || context == null || !context.Skill.IsReady || !context.Position.HasValue) throw new InvalidOperationException("Fresh Ready position context required.");
        Validate(token, context.Registry); executed = true;
        var admission = PNSkillObjectSpawn.Admit(context);
#if UNITY_5_3_OR_NEWER
        // The shooter's guess: drawn only, named by the cast; `Result` is the guess's receipt, so an Action-dependent blast is ended by the Action the same way as the real one.
        if (admission == PNSkillSpawnAdmission.Predicted) { Result = PNSkillObjectSpawn.CreateBlastGuess(context, token); return; }
#endif
        if (admission != PNSkillSpawnAdmission.Authority) return;
#if UNITY_5_3_OR_NEWER
        Result = PNSkillObjectSpawn.CreateBlast(context, token);
        if (Result.Status == PNBlastStatus.Failed) throw new InvalidOperationException("Blast creation failed.", Result.Error);
#else
        // Standalone validates invocation/payload only; it has no Unity blast runtime.
#endif
    }
    public override void Dispose() { executed = true; }
}
