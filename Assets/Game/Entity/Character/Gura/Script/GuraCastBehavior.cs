using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Gura's single-action slots (the Skill slot's trident, Docs/GuraCharacter.md 4.2): on a press it checks the cooldown Ability, makes ONE decision (`cast_target`: the nearest enemy in reach, `invocation.Choose`),
//     runs the registered Action over several frames (`Advance`) and ends the cast when the Action ends; the cooldown is spent when the cast starts. On the end it raises the optional `afterStateId` state (Gura's
//     next-strike flag). Profile data {cooldownAbilityId, actionDataId, reach, afterStateId?}. It has no network code: the owner's guess advances through the binding layer's predicted-behavior path and the authority replays the same.
// VI: Behavior cho cac o mot Action cua Gura (Skill = dinh ba): kiem cooldown, mot quyet dinh muc tieu, chay Action nhieu khung, het thi cap co `afterStateId`.
public sealed class GuraCastBehavior : PNSkillBehavior
{
    public const string DecisionPoint = "cast_target";
    private readonly PNSkillBehaviorContext context;
    private readonly EveId cooldownId, actionId, afterId, burstCooldownId, burstActionId;
    private readonly int gainBurst, gainWave; private readonly double gainCut;
    private readonly float reach;
    private PNSkillAction action; private PNSkillActionContext actionContext; private bool casting; private PNStat stat;
    public static int Casts, Cancels;
    public bool IsCasting => casting;

    public GuraCastBehavior(PNSkillBehaviorContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        var data = PNBuffContentBatch.Object(context.Profile.Data);
        cooldownId = EveId.Parse(PNBuffContentBatch.String(data, "cooldownAbilityId")); actionId = EveId.Parse(PNBuffContentBatch.String(data, "actionDataId"));
        reach = (float)PNBuffContentBatch.Number(data, "reach");
        afterId = data["afterStateId"] == null ? default : EveId.Parse(PNBuffContentBatch.String(data, "afterStateId"));
        var gain = (JObject)data["gain"]; gainBurst = PNBuffContentBatch.Integer(gain, "burst"); gainWave = PNBuffContentBatch.Integer(gain, "wave"); gainCut = PNBuffContentBatch.Number(gain, "cut");
        var inBurst = (JObject)data["inBurst"]; burstCooldownId = EveId.Parse(PNBuffContentBatch.String(inBurst, "cooldownAbilityId")); burstActionId = EveId.Parse(PNBuffContentBatch.String(inBurst, "actionDataId"));
    }
    public static void Validate(JToken data, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(data); PNBuffContentBatch.Fields(obj, "cooldownAbilityId", "actionDataId", "reach", "afterStateId", "gain", "inBurst");
        EveId.Parse(PNBuffContentBatch.String(obj, "cooldownAbilityId")); EveId.Parse(PNBuffContentBatch.String(obj, "actionDataId"));
        double r = PNBuffContentBatch.Number(obj, "reach"); if (!(r > 0d && r <= 50d)) throw new FormatException("gura_cast reach must be in (0, 50].");
        if (obj["afterStateId"] != null) EveId.Parse(PNBuffContentBatch.String(obj, "afterStateId"));
        var gain = PNBuffContentBatch.Object(obj["gain"]); PNBuffContentBatch.Fields(gain, "burst", "wave", "cut"); PNBuffContentBatch.Integer(gain, "burst"); PNBuffContentBatch.Integer(gain, "wave"); PNBuffContentBatch.Number(gain, "cut");
        var burst = PNBuffContentBatch.Object(obj["inBurst"]); PNBuffContentBatch.Fields(burst, "cooldownAbilityId", "actionDataId"); EveId.Parse(PNBuffContentBatch.String(burst, "cooldownAbilityId")); EveId.Parse(PNBuffContentBatch.String(burst, "actionDataId"));
    }
    public static void ValidateReferences(PNContentReferenceContext context, JToken data)
    { var obj = PNBuffContentBatch.Object(data); context.Require<EveSkillDefinition>(obj["actionDataId"], "actionDataId"); context.Require<EveSkillDefinition>(PNBuffContentBatch.Object(obj["inBurst"])["actionDataId"], "inBurst.actionDataId"); }

    public override void Input(PNSkillInputEvent input) { if (input.Interaction == PNSkillInteraction.Tap && input.Phase == PNSkillInputPhase.Started) Cast(); }

    public bool Cast()
    {
        if (casting) return false;
        // In Burst the Skill is the Skill Burst: its own action and its own cooldown.
        bool burst = GuraResources.InBurst(context.Abilities);
        var cooldown = context.Abilities.GetCooldown(burst ? burstCooldownId : cooldownId); var useAction = burst ? burstActionId : actionId;
        if (!cooldown.CanConsume() || !context.TryBegin()) return false;
        casting = true; bool failed = true;
        try
        {
            stat = context.Skill.GetComponent<PNStat>();
            var target = context.Skill.CurrentInvocation.Choose<PNStat>(DecisionPoint, () => Nearest(), null, reach);
            Vector3 direction = AimAt(target);
            action = context.CreateAction(useAction, out actionContext, target != null ? new[] { target } : Array.Empty<PNStat>(), direction);
            context.Skill.GetComponent<PNPlayerMove>()?.FaceSkillDirection(new Vector2(direction.x, direction.z));
            action.Start(actionContext); action.Perform(actionContext);
            if (!cooldown.TryConsume()) throw new InvalidOperationException("Cast cooldown changed during the cast.");
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
    private Vector3 AimAt(PNStat target)
    {
#if UNITY_5_3_OR_NEWER
        var self = context.Skill.transform.position;
        if (target != null) { var toward = target.transform.position - self; toward.y = 0; if (toward.sqrMagnitude > 1e-6f) return toward.normalized; }
        var move = context.Skill.GetComponent<PNMove>(); var last = move != null ? move.LastMoveDirection : Vector2.right;
        return last.sqrMagnitude < 1e-6f ? Vector3.right : new Vector3(last.x, 0f, last.y).normalized;
#else
        return Vector3.right;
#endif
    }

    public override void Advance(float delta)
    {
        if (!casting || action == null) return;
        bool done = false;
        try
        {
            using var frame = context.Skill.BeginActionExecutionFrame(delta);
            if (delta == 0f) { frame.Hold(actionContext); done = action.AdvanceAuxiliary(0f); }
            else frame.Advance(actionContext, delta, 1d, step => done = action.AdvanceAuxiliary(step));
        }
        catch { try { action.Cancel(actionContext); } catch { } Finish(true); throw; }
        if (!done) return;
        try { action.Exit(actionContext); } finally { if (afterId.IsValid) context.Abilities.GrantState(afterId); Finish(false); }
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
