#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;

// A short action that fires a volley through create_projectile (same shape as PNAreaAction). It starts the action context, so the
// skill's OnHit effects can be frozen into the volley at Perform.
public class PNProjectileAction : PNSkillAction
{
    private PNCreateProjectileOperation operation;
    private bool started, performed, disposed;
    private IReadOnlyList<PNProjectileReceipt> results;
    // The receipts of the volley, kept after the action exits.
    public IReadOnlyList<PNProjectileReceipt> Results => results;
    public static EveId Validate(JToken payload, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(payload); PNBuffContentBatch.Fields(obj, "operationId", "data");
        var id = EveId.Parse(PNBuffContentBatch.String(obj, "operationId")); var definition = hub.Operations().GetEffective(id);
        if (!typeof(PNCreateProjectileOperation).IsAssignableFrom(definition.ImplementationType)) throw new FormatException("Projectile action requires a create-projectile compatible operation.");
        definition.ValidatePayload(obj["data"], hub); return id;
    }
    public override void Start(PNSkillActionContext context)
    {
        if (started || disposed || !context.Direction.HasValue) throw new InvalidOperationException("Fresh direction action context required.");
        Validate(context.Data.Action.Perform, context.Registry); started = true; context.BeginAction();
    }
    public override void Perform(PNSkillActionContext context)
    {
        if (!started || performed || disposed) throw new InvalidOperationException("Projectile action is not performable."); performed = true;
        var payload = (JObject)context.Data.Action.Perform;
        operation = (PNCreateProjectileOperation)CreateOperation(context, Validate(payload, context.Registry));
        try { operation.Execute(context, payload["data"]); results = operation.Results; } catch { Dispose(); throw; }
    }
    public override void Exit(PNSkillActionContext context) { context.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext context) { try { context.CancelEffects(); operation?.Cancel(); } finally { Dispose(); } }
    public override void Dispose() { if (disposed) return; disposed = true; operation?.Dispose(); operation = null; }
}
#endif
