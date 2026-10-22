#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;

// A reusable instant heal wrapper around the selected existing heal Operation.
public sealed class PNAgentHealAction : PNSkillAction
{
    private PNSkillActionContext context;
    private PNSkillOperation operation;
    private bool performed, ended;
    public static void Validate(PNSkillActionData data, EveRegistryHub hub)
    { hub.Operations().Get(EveId.Parse("base:heal")).ValidatePayload(data.Perform, hub); }
    public override void Start(PNSkillActionContext value)
    {
        if (context != null || ended || value == null) throw new InvalidOperationException("Invalid heal Start.");
        Validate(value.Data.Action, value.Registry); context = value; context.BeginAction();
    }
    public override void Perform(PNSkillActionContext value)
    {
        if (!ReferenceEquals(context, value) || performed || ended) throw new InvalidOperationException("Invalid heal Perform.");
        performed = true; operation = CreateOperation(context, EveId.Parse("base:heal")); operation.Execute(context, context.Data.Action.Perform);
    }
    public override void Exit(PNSkillActionContext value) { if (!performed) throw new InvalidOperationException("Heal not performed."); value.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext value) { try { value.CancelEffects(); operation?.Cancel(); } finally { Dispose(); } }
    public override void Dispose() { if (ended) return; operation?.Dispose(); operation = null; ended = true; }
}
#endif
