using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// A short creation action: payload {operationId, data} like the area and projectile actions. It starts the action context, so the skill's own OnActionStart /
// OnComplete effects run around the summon; the summon itself is an independent entity afterwards.
public class PNSummonAction : PNSkillAction
{
    private PNCreateSummonOperation operation;
    private bool started, performed, disposed;
    // What the summon did (Spawning, Rejected, QueueFull, ...), kept after the action exits.
    public PNSummonStatus Status { get; private set; } = PNSummonStatus.Pending;
    public static EveId Validate(JToken payload, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(payload); PNBuffContentBatch.Fields(obj, "operationId", "data");
        var id = EveId.Parse(PNBuffContentBatch.String(obj, "operationId")); var definition = hub.Operations().GetEffective(id);
        if (!typeof(PNCreateSummonOperation).IsAssignableFrom(definition.ImplementationType)) throw new FormatException("Summon action requires a create-summon compatible operation.");
        definition.ValidatePayload(obj["data"], hub); return id;
    }
    public override void Start(PNSkillActionContext context)
    {
        if (started || disposed) throw new InvalidOperationException("Fresh action context required.");
        Validate(context.Data.Action.Perform, context.Registry); started = true; context.BeginAction();
    }
    public override void Perform(PNSkillActionContext context)
    {
        if (!started || performed || disposed) throw new InvalidOperationException("Summon action is not performable."); performed = true;
        var payload = (JObject)context.Data.Action.Perform;
        operation = (PNCreateSummonOperation)CreateOperation(context, Validate(payload, context.Registry));
        try { operation.Execute(context, payload["data"]); Status = operation.Status; } catch { Dispose(); throw; }
    }
    public override void Exit(PNSkillActionContext context) { context.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext context) { try { context.CancelEffects(); operation?.Cancel(); } finally { Dispose(); } }
    public override void Dispose() { if (disposed) return; disposed = true; operation?.Dispose(); operation = null; }
}
