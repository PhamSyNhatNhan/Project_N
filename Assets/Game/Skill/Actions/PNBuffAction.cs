using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// Skill owns admission/target selection; this registered action executes selected Buff operations.
public class PNBuffAction : PNSkillAction
{
    private PNSkillOperation operation;
    private bool started, performed, disposed;
    public static EveId Validate(JToken payload, EveRegistryHub hub)
    {
        var data = PNBuffContentBatch.Object(payload); PNBuffContentBatch.Fields(data, "operationId", "data");
        var id = EveId.Parse(PNBuffContentBatch.String(data, "operationId")); var definition = hub.Operations().Get(id);
        if (typeof(PNApplyBuffOperation).IsAssignableFrom(definition.ImplementationType)) PNApplyBuffOperation.Validate(data["data"], hub);
        else if (typeof(PNTriggerBuffOperation).IsAssignableFrom(definition.ImplementationType)) PNTriggerBuffOperation.Validate(data["data"], hub, typeof(PNChangeBuffStacksOperation).IsAssignableFrom(definition.ImplementationType));
        else if (typeof(PNRemoveBuffOperation).IsAssignableFrom(definition.ImplementationType)) PNRemoveBuffOperation.Validate(data["data"], hub);
        else throw new FormatException("Buff action requires Buff apply/removal/trigger/stack operation."); return id;
    }
    public override void Start(PNSkillActionContext context)
    { if (started || disposed) throw new InvalidOperationException("Buff action already started or disposed."); Validate(context.Data.Action.Perform, context.Registry); started = true; context.BeginAction(); }
    public override void Perform(PNSkillActionContext context)
    {
        if (!started || performed || disposed) throw new InvalidOperationException("Buff action is not performable."); performed = true;
        var payload = PNBuffContentBatch.Object(context.Data.Action.Perform);
        operation = CreateOperation(context, Validate(payload, context.Registry));
        try { operation.Execute(context, payload["data"]); } catch { Dispose(); throw; }
    }
    public override void Exit(PNSkillActionContext context) { context.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext context) { try { context.CancelEffects(); operation?.Cancel(); } finally { Dispose(); } }
    public override void Dispose() { if (disposed) return; disposed = true; operation?.Dispose(); operation = null; }
}
