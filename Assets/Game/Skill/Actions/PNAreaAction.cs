#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// A short creation action transfers the running area to the session manager.
public class PNAreaAction : PNSkillAction
{
    private PNCreateAreaOperation operation;
    private bool started, performed, disposed;
    public PNAreaReceipt Result { get; private set; }
    public static EveId Validate(JToken payload, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(payload); PNBuffContentBatch.Fields(obj, "operationId", "data");
        var id = EveId.Parse(PNBuffContentBatch.String(obj, "operationId")); var definition = hub.Operations().GetEffective(id);
        if (!typeof(PNCreateAreaOperation).IsAssignableFrom(definition.ImplementationType)) throw new FormatException("Area action requires a create-area compatible operation.");
        definition.ValidatePayload(obj["data"], hub); return id;
    }
    public override void Start(PNSkillActionContext context)
    {
        if (started || disposed || !context.Position.HasValue) throw new InvalidOperationException("Fresh position action context required.");
        Validate(context.Data.Action.Perform, context.Registry); started = true; context.BeginAction();
    }
    public override void Perform(PNSkillActionContext context)
    {
        if (!started || performed || disposed) throw new InvalidOperationException("Area action is not performable."); performed = true;
        var payload = (JObject)context.Data.Action.Perform;
        operation = (PNCreateAreaOperation)CreateOperation(context, Validate(payload, context.Registry));
        try { operation.Execute(context, payload["data"]); Result = operation.Result; } catch { Dispose(); throw; }
    }
    public override void Exit(PNSkillActionContext context) { context.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext context) { try { context.CancelEffects(); operation?.Cancel(); } finally { Dispose(); } }
    public override void Dispose() { if (disposed) return; disposed = true; operation?.Dispose(); operation = null; }
}
#endif
