using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Gura's Skill slot (Docs/GuraCharacter.md 4.2). Outside Burst it has two forms, picked once per press (`cast_form`, a decision, so the owner's guess and the authority take the same one): an enemy nearer than
//     `formRange` (to the edge of its body) gets the near form (`meleeActionDataId`, the vortex); a farther one, or none within `reach`, gets the throw (`actionDataId`). A throw that hits an enemy which is still
//     alive goes on, in the same cast, with the rush (`rushActionDataId`, a strike that walks to it); a miss, a kill or no right to move ends the cast. The next-strike flag of these two forms is not
//     given here but by their hits (skill data `flagOnHit`). Each form has its own cooldown (the near one `meleeCooldownAbilityId`) and the slot answers only when BOTH are ready. In Burst the slot is the Skill Burst (`inBurst`): one Action, and `afterStateId` is raised when it ends.
//     Profile data {cooldownAbilityId, meleeCooldownAbilityId, actionDataId, meleeActionDataId, rushActionDataId, formRange, reach, afterStateId, inBurst}. It has no network code.
// VI: O Skill cua Gura. Ngoai Burst co hai dang chon mot lan luc bam: quai gan thi xoay nuoc, quai xa thi nem dinh ba, trung quai con song thi lao toi. Trong Burst la Skill Burst.
public sealed class GuraCastBehavior : PNSkillBehavior
{
    public const string DecisionPoint = "cast_target";
    public const string FormPoint = "cast_form";
    private readonly PNSkillBehaviorContext context;
    private readonly EveId cooldownId, meleeCooldownId, throwId, meleeId, rushId, afterId, burstCooldownId, burstActionId;
    private readonly float reach, formRange;
    private PNSkillAction action; private PNSkillActionContext actionContext; private bool casting, inBurst; private PNStat stat;
    public static int Casts, Cancels, Melees, Throws, Rushes;
    public bool IsCasting => casting;

    public GuraCastBehavior(PNSkillBehaviorContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        var data = PNBuffContentBatch.Object(context.Profile.Data);
        cooldownId = EveId.Parse(PNBuffContentBatch.String(data, "cooldownAbilityId")); meleeCooldownId = EveId.Parse(PNBuffContentBatch.String(data, "meleeCooldownAbilityId")); throwId = EveId.Parse(PNBuffContentBatch.String(data, "actionDataId"));
        meleeId = EveId.Parse(PNBuffContentBatch.String(data, "meleeActionDataId")); rushId = EveId.Parse(PNBuffContentBatch.String(data, "rushActionDataId"));
        reach = (float)PNBuffContentBatch.Number(data, "reach"); formRange = (float)PNBuffContentBatch.Number(data, "formRange");
        afterId = EveId.Parse(PNBuffContentBatch.String(data, "afterStateId"));
        var burst = (JObject)data["inBurst"]; burstCooldownId = EveId.Parse(PNBuffContentBatch.String(burst, "cooldownAbilityId")); burstActionId = EveId.Parse(PNBuffContentBatch.String(burst, "actionDataId"));
    }
    public static void Validate(JToken data, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(data); PNBuffContentBatch.Fields(obj, "cooldownAbilityId", "meleeCooldownAbilityId", "actionDataId", "meleeActionDataId", "rushActionDataId", "formRange", "reach", "afterStateId", "inBurst");
        foreach (var key in new[] { "cooldownAbilityId", "meleeCooldownAbilityId", "actionDataId", "meleeActionDataId", "rushActionDataId", "afterStateId" }) EveId.Parse(PNBuffContentBatch.String(obj, key));
        double r = PNBuffContentBatch.Number(obj, "reach"); if (!(r > 0d && r <= 50d)) throw new FormatException("gura_cast reach must be in (0, 50].");
        double near = PNBuffContentBatch.Number(obj, "formRange"); if (!(near >= 0d && near <= r)) throw new FormatException("gura_cast formRange must be in [0, reach].");
        var burst = PNBuffContentBatch.Object(obj["inBurst"]); PNBuffContentBatch.Fields(burst, "cooldownAbilityId", "actionDataId"); EveId.Parse(PNBuffContentBatch.String(burst, "cooldownAbilityId")); EveId.Parse(PNBuffContentBatch.String(burst, "actionDataId"));
    }
    public static void ValidateReferences(PNContentReferenceContext context, JToken data)
    {
        var obj = PNBuffContentBatch.Object(data);
        foreach (var key in new[] { "actionDataId", "meleeActionDataId", "rushActionDataId" }) context.Require<EveSkillDefinition>(obj[key], key);
        context.Require<EveSkillDefinition>(PNBuffContentBatch.Object(obj["inBurst"])["actionDataId"], "inBurst.actionDataId");
    }

    public override void Input(PNSkillInputEvent input) { if (input.Interaction == PNSkillInteraction.Tap && input.Phase == PNSkillInputPhase.Started) Cast(); }

    public bool Cast()
    {
        if (casting) return false;
        // In Burst the Skill is the Skill Burst: its own action and its own cooldown.
        inBurst = GuraResources.InBurst(context.Abilities);
        var cooldown = context.Abilities.GetCooldown(inBurst ? burstCooldownId : cooldownId);
        // Outside Burst the slot waits for both forms' cooldowns, whichever form the press would take; the form taken spends its own.
        var meleeCooldown = inBurst ? null : context.Abilities.GetCooldown(meleeCooldownId);
        if (!cooldown.CanConsume() || meleeCooldown != null && !meleeCooldown.CanConsume() || !context.TryBegin()) return false;
        casting = true; bool failed = true;
        try
        {
            stat = context.Skill.GetComponent<PNStat>();
            var target = context.Skill.CurrentInvocation.Choose<PNStat>(DecisionPoint, () => Nearest(), null, reach);
            Vector3 direction = AimAt(target);
            var useAction = burstActionId;
            if (!inBurst)
            {
                bool melee = context.Skill.CurrentInvocation.Choose<bool>(FormPoint, () => target != null && Gap(target) <= formRange);
                useAction = melee ? meleeId : throwId;
                if (melee) { Melees++; cooldown = meleeCooldown; } else Throws++;
            }
            Begin(useAction, target, direction);
            if (!cooldown.TryConsume()) throw new InvalidOperationException("Cast cooldown changed during the cast.");
            context.Commit(target != null ? new[] { target } : null, direction);
            if (actionContext.Mode == PNSkillMode.Authority) Casts++;
            failed = false; return true;
        }
        finally { if (failed) { try { action?.Cancel(actionContext); } catch { } Finish(true); } }
    }

    private void Begin(EveId actionDataId, PNStat target, Vector3 direction)
    {
        action = context.CreateAction(actionDataId, out actionContext, target != null ? new[] { target } : Array.Empty<PNStat>(), direction);
        (action as IPNSkillMotionAction)?.BindMovementOwner(() => context.Skill.CancelBindingExecution());
        context.Skill.GetComponent<PNPlayerMove>()?.FaceSkillDirection(new Vector2(direction.x, direction.z));
        action.Start(actionContext); action.Perform(actionContext);
    }

    // Distance on the ground from the caster to the edge of the target's body (a large enemy standing close is close).
    private float Gap(PNStat target)
    {
#if UNITY_5_3_OR_NEWER
        var delta = target.transform.position - context.Skill.transform.position; delta.y = 0f;
        var body = target.GetComponent<CapsuleCollider>();
        float radius = body != null ? body.radius * Mathf.Max(Mathf.Abs(body.transform.lossyScale.x), Mathf.Abs(body.transform.lossyScale.z)) : 0f;
        return Mathf.Max(0f, delta.magnitude - radius);
#else
        return 0f;
#endif
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
        PNStat struck = null;
#if UNITY_5_3_OR_NEWER
        struck = (action as PNGuraTridentAction)?.Struck;
#endif
        bool failed = true;
        try
        {
            action.Exit(actionContext);
            if (CanRush(struck))
            {
                DisposeAction();
                Begin(rushId, struck, AimAt(struck));
                Rushes++; failed = false; return;
            }
            failed = false;
        }
        finally { if (failed) { try { action?.Cancel(actionContext); } catch { } Finish(true); } }
        // The Skill Burst gives its flag when it ends; the two forms of the Skill give theirs by hitting.
        if (inBurst) context.Abilities.GrantState(afterId);
        Finish(false);
    }

    // A rush needs something the trident hit that is still there to walk to, and the right to move (a rooted body stays, and gets no flag).
    private bool CanRush(PNStat struck) =>
        struck != null && struck.IsReady && !struck.IsDead && context.Skill.Controls.IsAllowed(PNControlIds.VoluntaryMotion);

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
