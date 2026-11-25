using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Gura's Burst slot (Docs/GuraCharacter.md 4.4). Needs `cost` BurstStack (125) and no Burst running; spends the cost (the surplus stays), leaps and dives onto the nearest enemy (a registered `combo_strike_action`
//     with a step height; its skill `effects` create the tide area where she lands), then starts the Burst state (15 s) and gives the starting Wave (10). BurstStack does not grow while Burst lasts (GuraResources).
//     Profile data {actionDataId, reach, cost, startWave, burstStateId, stackId, waveId}. One decision per cast (`burst_target`). It has no network code.
// VI: O Burst: can du BurstStack, nhay tu tren xuong, tao vung thuy trieu luc dap (effects cua skill), roi bat dau trang thai Burst va cho san Song.
public sealed class GuraBurstBehavior : PNSkillBehavior
{
    public const string DecisionPoint = "burst_target";
    private readonly PNSkillBehaviorContext context;
    private readonly EveId actionId, stateId, stackId, waveId;
    private readonly float reach; private readonly int cost, startWave;
    private PNSkillAction action; private PNSkillActionContext actionContext; private bool casting; private PNStat stat;
    public static int Casts, Cancels;
    public bool IsCasting => casting;

    public GuraBurstBehavior(PNSkillBehaviorContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        var data = PNBuffContentBatch.Object(context.Profile.Data);
        actionId = EveId.Parse(PNBuffContentBatch.String(data, "actionDataId")); stateId = EveId.Parse(PNBuffContentBatch.String(data, "burstStateId"));
        stackId = EveId.Parse(PNBuffContentBatch.String(data, "stackId")); waveId = EveId.Parse(PNBuffContentBatch.String(data, "waveId"));
        reach = (float)PNBuffContentBatch.Number(data, "reach"); cost = PNBuffContentBatch.Integer(data, "cost"); startWave = PNBuffContentBatch.Integer(data, "startWave");
    }
    public static void Validate(JToken data, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(data); PNBuffContentBatch.Fields(obj, "actionDataId", "reach", "cost", "startWave", "burstStateId", "stackId", "waveId");
        foreach (var key in new[] { "actionDataId", "burstStateId", "stackId", "waveId" }) EveId.Parse(PNBuffContentBatch.String(obj, key));
        double r = PNBuffContentBatch.Number(obj, "reach"); if (!(r > 0d && r <= 50d)) throw new FormatException("gura_burst reach must be in (0, 50].");
        if (PNBuffContentBatch.Integer(obj, "cost") < 1 || PNBuffContentBatch.Integer(obj, "startWave") < 0) throw new FormatException("gura_burst cost/startWave are out of range.");
    }
    public static void ValidateReferences(PNContentReferenceContext context, JToken data) => context.Require<EveSkillDefinition>(PNBuffContentBatch.Object(data)["actionDataId"], "actionDataId");

    public override void Input(PNSkillInputEvent input) { if (input.Interaction == PNSkillInteraction.Tap && input.Phase == PNSkillInputPhase.Started) Cast(); }

    public bool Cast()
    {
        if (casting) return false;
        var abilities = context.Abilities; var stack = abilities.GetCounter(stackId);
        if (GuraResources.InBurst(abilities) || !stack.CanConsume(cost) || !context.TryBegin()) return false;
        casting = true; bool failed = true;
        try
        {
            stat = context.Skill.GetComponent<PNStat>();
            var target = context.Skill.CurrentInvocation.Choose<PNStat>(DecisionPoint, () => Nearest(), null, reach);
            Vector3 direction = AimAt(target);
            action = context.CreateAction(actionId, out actionContext, target != null ? new[] { target } : Array.Empty<PNStat>(), direction);
            (action as IPNSkillMotionAction)?.BindMovementOwner(() => context.Skill.CancelBindingExecution());
            context.Skill.GetComponent<PNPlayerMove>()?.FaceSkillDirection(new Vector2(direction.x, direction.z));
            action.Start(actionContext); action.Perform(actionContext);
            if (!stack.TryConsume(cost)) throw new InvalidOperationException("BurstStack changed during the cast.");
            context.Commit(target != null ? new[] { target } : null, direction);
            if (actionContext.Mode == PNSkillMode.Authority) Casts++;
            failed = false; return true;
        }
        finally { if (failed) { try { action?.Cancel(actionContext); } catch { } Finish(true); } }
    }

    private PNStat Nearest()
    {
#if UNITY_5_3_OR_NEWER
        return PNAutoTarget.For(stat).Resolve(reach, stat.TargetLayers.Mask, PNTargetRule.Autotarget(PNTargetMode.Nearest));
#else
        return null;
#endif
    }
    private Vector3 AimAt(PNStat t)
    {
#if UNITY_5_3_OR_NEWER
        var self = context.Skill.transform.position;
        if (t != null) { var toward = t.transform.position - self; toward.y = 0; if (toward.sqrMagnitude > 1e-6f) return toward.normalized; }
        var move = context.Skill.GetComponent<PNMove>(); var last = move != null ? move.LastMoveDirection : Vector2.right;
        return last.sqrMagnitude < 1e-6f ? Vector3.right : new Vector3(last.x, 0f, last.y).normalized;
#else
        return Vector3.right;
#endif
    }

    public override void Advance(float delta)
    {
        if (!casting || action == null) return;
        bool done;
        try { done = action.AdvanceAuxiliary(delta); }
        catch { try { action.Cancel(actionContext); } catch { } Finish(true); throw; }
        if (!done) return;
        // Landed: the action's own effects made the tide area; the Burst starts now with its first Wave.
        try { action.Exit(actionContext); } finally { DisposeAction(); }
        context.Abilities.GrantState(stateId); context.Abilities.GetCounter(waveId).Add(startWave);
        Finish(false);
    }
    public override void Cancel()
    {
        if (!casting) return;
        Cancels++;
        try { action?.Cancel(actionContext); } finally { DisposeAction(); casting = false; }
    }
    private void Finish(bool cancelled) { var owner = casting; DisposeAction(); casting = false; if (owner) context.Complete(cancelled); }
    private void DisposeAction() { try { action?.Dispose(); } finally { action = null; actionContext = null; } }
    public override void Dispose() { DisposeAction(); casting = false; }
}
