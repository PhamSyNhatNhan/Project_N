using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Gura's Attack slot (Docs/GuraCharacter.md 4.1): four strikes in a row. Profile data {comboStepId, comboWindowId, nextFourId, reach, steps[4]}: the combo counter says which strike is next while the combo
//     window (a timed State) lasts; the next-strike flag (`nextFourId`) makes the next press the fourth strike straight away and is spent by it. Each strike is a registered `combo_strike_action` (its numbers are the skill
//     data). The behavior makes one decision per press (`attack_target`: the nearest enemy in reach, `invocation.Choose`), runs the strike over several frames (`Advance`, scaled by attack speed) and ends the cast when
//     the strike ends. It has no network code: the owner's guess advances through the binding layer's predicted-behavior path (H1) and the authority replays the same.
// VI: O Attack cua Gura: bon nhat chem lien tiep. Moi lan bam co mot quyet dinh (muc tieu gan nhat trong tam), Action chay nhieu khung qua Advance.
public sealed class GuraAttackBehavior : PNSkillBehavior
{
    public const string DecisionPoint = "attack_target";
    public const int Steps = 4;
    private readonly PNSkillBehaviorContext context;
    private readonly EveId stepId, windowId, nextFourId;
    private readonly EveId[] steps = new EveId[Steps];
    private readonly float reach;
    private PNSkillAction action; private PNSkillActionContext actionContext; private bool casting;
    private PNStat stat;
    public static int Casts, NoTarget, Cancels;
    public bool IsCasting => casting;

    public GuraAttackBehavior(PNSkillBehaviorContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        var data = PNBuffContentBatch.Object(context.Profile.Data);
        stepId = EveId.Parse(PNBuffContentBatch.String(data, "comboStepId")); windowId = EveId.Parse(PNBuffContentBatch.String(data, "comboWindowId")); nextFourId = EveId.Parse(PNBuffContentBatch.String(data, "nextFourId"));
        reach = (float)PNBuffContentBatch.Number(data, "reach");
        var list = (JArray)data["steps"]; for (int i = 0; i < Steps; i++) steps[i] = EveId.Parse(list[i].Value<string>());
    }
    public static void Validate(JToken data, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(data); PNBuffContentBatch.Fields(obj, "comboStepId", "comboWindowId", "nextFourId", "reach", "steps");
        EveId.Parse(PNBuffContentBatch.String(obj, "comboStepId")); EveId.Parse(PNBuffContentBatch.String(obj, "comboWindowId")); EveId.Parse(PNBuffContentBatch.String(obj, "nextFourId"));
        double r = PNBuffContentBatch.Number(obj, "reach"); if (!(r > 0d && r <= 50d)) throw new FormatException("gura_attack reach must be in (0, 50].");
        if (!(obj["steps"] is JArray list) || list.Count != Steps) throw new FormatException("gura_attack needs " + Steps + " step skills.");
        foreach (var item in list) { if (item.Type != JTokenType.String) throw new FormatException("gura_attack steps are skill IDs."); EveId.Parse(item.Value<string>()); }
    }
    public static void ValidateReferences(PNContentReferenceContext context, JToken data)
    {
        var obj = PNBuffContentBatch.Object(data);
        foreach (var item in (JArray)obj["steps"]) context.Require<EveSkillDefinition>(item, "steps");
    }

    public override void Input(PNSkillInputEvent input)
    {
        // A tap presses at once (Started); a held press repeats (Performed). Releasing ends nothing: the strike in flight finishes.
        bool press = input.Interaction == PNSkillInteraction.Tap ? input.Phase == PNSkillInputPhase.Started : input.Interaction == PNSkillInteraction.Repeat && input.Phase == PNSkillInputPhase.Performed;
        if (press) Cast();
    }

    // Public so a fixture can strike without input.
    public bool Cast()
    {
        // A press during the recovery of a strike (all its hits done) cuts the recovery short and starts the next strike; during the hits it is ignored.
        if (casting)
        {
            if (!(action is PNComboStrikeAction strike && strike.IsRecovering)) return false;
            context.Skill.CancelBindingExecution();
        }
        var abilities = context.Abilities; var step = abilities.GetCounter(stepId);
        if (!context.TryBegin()) return false;
        casting = true; bool failed = true;
        try
        {
            stat = context.Skill.GetComponent<PNStat>();
            var target = context.Skill.CurrentInvocation.Choose<PNStat>(DecisionPoint, () => Nearest(), null, reach);
            // The next strike: the flag makes it the fourth at once; otherwise the combo goes on while the window lasts and starts over after it.
            bool flagged = IsOn(abilities, nextFourId);
            int index = flagged ? Steps - 1 : IsOn(abilities, windowId) ? Math.Min(step.Current, Steps - 1) : 0;
            if (target == null) NoTarget++;
            Vector3 direction = AimAt(target);
            action = context.CreateAction(steps[index], out actionContext, target != null ? new[] { target } : Array.Empty<PNStat>(), direction);
            (action as IPNSkillMotionAction)?.BindMovementOwner(() => context.Skill.CancelBindingExecution());
            context.Skill.GetComponent<PNPlayerMove>()?.FaceSkillDirection(new Vector2(direction.x, direction.z));
            action.Start(actionContext); action.Perform(actionContext);
            if (flagged) foreach (var on in abilities.Find(nextFourId)) if (on is PNAbilityState s && s.IsActive) s.End();
            step.SetAmount((index + 1) % Steps);
            abilities.GrantState(windowId);
            context.Commit(target != null ? new[] { target } : null, direction);
            if (actionContext.Mode == PNSkillMode.Authority) Casts++;
            failed = false;
            return true;
        }
        finally { if (failed) { try { action?.Cancel(actionContext); } catch { } Finish(true); } }
    }

    // Reads work on the owner's guess too (a replica); the states are only written through the collection's own grant, which a guess may do.
    private static bool IsOn(PNAbilityCollection abilities, EveId id)
    {
        foreach (var found in abilities.Find(id)) if (found is PNAbilityState state && state.IsActive) return true;
        return false;
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
        // The strike's clock is attack speed (a normal attack): the registered speed stat, 100 = normal.
        double speed = context.Skill.CurrentInvocation?.Metadata.AffectedByAttackSpeed == true && stat != null ? Math.Max(0d, stat.Evaluate(PNContentIds.AttackSpeed) / 100d) : 1d;
        bool done;
        try { done = action.AdvanceAuxiliary(delta * (float)speed); }
        catch { try { action.Cancel(actionContext); } catch { } Finish(true); throw; }
        if (!done) return;
        try { action.Exit(actionContext); } finally { Finish(false); }
    }

    // Cancelled by the binding layer (an interruption, death, a changed binding, the body motion lost): the strike is taken back.
    public override void Cancel()
    {
        if (!casting) return;
        Cancels++;
        try { action?.Cancel(actionContext); } finally { DisposeAction(); casting = false; }
    }
    private void Finish(bool cancelled)
    {
        var owner = casting; DisposeAction(); casting = false;
        if (owner) context.Complete(cancelled);
    }
    private void DisposeAction() { try { action?.Dispose(); } finally { action = null; actionContext = null; } }
    public override void Dispose() { DisposeAction(); casting = false; }
}
