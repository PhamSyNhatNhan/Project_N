using System;
using EveEngine;
using System.Linq;
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
    private readonly int[] gainBurst = new int[Steps], gainWave = new int[Steps]; private readonly double[] gainCut = new double[Steps];
    private readonly EveId freeId; private readonly int freeBurst, freeWave;
    private readonly double[] milestones; private readonly double fangPerMilestone;
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
        var gains = (JArray)data["gains"];
        for (int i = 0; i < Steps; i++) { var g = (JObject)gains[i]; gainBurst[i] = PNBuffContentBatch.Integer(g, "burst"); gainWave[i] = PNBuffContentBatch.Integer(g, "wave"); gainCut[i] = PNBuffContentBatch.Number(g, "cut"); }
        freeId = EveId.Parse(PNBuffContentBatch.String(data, "freeSkillId")); var free = (JObject)data["free"]; freeBurst = PNBuffContentBatch.Integer(free, "burst"); freeWave = PNBuffContentBatch.Integer(free, "wave");
        var chain = (JObject)data["chain"]; milestones = ((JArray)chain["milestones"]).Select(x => x.Value<double>()).ToArray(); fangPerMilestone = PNBuffContentBatch.Number(chain, "fang");
    }
    public static void Validate(JToken data, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(data); PNBuffContentBatch.Fields(obj, "comboStepId", "comboWindowId", "nextFourId", "reach", "steps", "gains", "chain", "freeSkillId", "free");
        EveId.Parse(PNBuffContentBatch.String(obj, "comboStepId")); EveId.Parse(PNBuffContentBatch.String(obj, "comboWindowId")); EveId.Parse(PNBuffContentBatch.String(obj, "nextFourId"));
        double r = PNBuffContentBatch.Number(obj, "reach"); if (!(r > 0d && r <= 50d)) throw new FormatException("gura_attack reach must be in (0, 50].");
        if (!(obj["steps"] is JArray list) || list.Count != Steps) throw new FormatException("gura_attack needs " + Steps + " step skills.");
        foreach (var item in list) { if (item.Type != JTokenType.String) throw new FormatException("gura_attack steps are skill IDs."); EveId.Parse(item.Value<string>()); }
        if (!(obj["gains"] is JArray gains) || gains.Count != Steps) throw new FormatException("gura_attack needs one gain entry per step.");
        foreach (var token in gains) { var g = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(g, "burst", "wave", "cut"); if (PNBuffContentBatch.Integer(g, "burst") < 0 || PNBuffContentBatch.Integer(g, "wave") < 0 || PNBuffContentBatch.Number(g, "cut") < 0d) throw new FormatException("gura_attack gains are not negative."); }
        EveId.Parse(PNBuffContentBatch.String(obj, "freeSkillId")); var freeGain = PNBuffContentBatch.Object(obj["free"]); PNBuffContentBatch.Fields(freeGain, "burst", "wave"); PNBuffContentBatch.Integer(freeGain, "burst"); PNBuffContentBatch.Integer(freeGain, "wave");
        var chain = PNBuffContentBatch.Object(obj["chain"]); PNBuffContentBatch.Fields(chain, "milestones", "fang");
        if (!(chain["milestones"] is JArray ms) || ms.Count != 3) throw new FormatException("gura_attack chain needs three milestones."); PNBuffContentBatch.Number(chain, "fang");
    }
    public static void ValidateReferences(PNContentReferenceContext context, JToken data)
    {
        var obj = PNBuffContentBatch.Object(data);
        foreach (var item in (JArray)obj["steps"]) context.Require<EveSkillDefinition>(item, "steps");
        context.Require<EveSkillDefinition>(obj["freeSkillId"], "freeSkillId");
    }

    public override void Input(PNSkillInputEvent input)
    {
        // A tap presses at once (Started); a held press repeats (Performed). Releasing ends nothing: the strike in flight finishes.
        bool held = input.Interaction == PNSkillInteraction.Repeat && input.Phase == PNSkillInputPhase.Performed;
        bool press = input.Interaction == PNSkillInteraction.Tap ? input.Phase == PNSkillInputPhase.Started : held;
        if (press) Cast(held);
    }

    // Public so a fixture can strike without input.
    public bool Cast(bool held = false)
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
            // A held strike in Burst spends Wave (the chain); the one that would reach the last milestone is the free Skill Burst instead of a strike.
            var waveCounter = abilities.GetCounter(PNGuraContent.WaveStack); var chainCount = abilities.GetCounter(PNGuraContent.ChainCount);
            bool chainStrike = held && GuraResources.InBurst(abilities) && waveCounter.Current > 0;
            bool final = chainStrike && (IsOn(abilities, PNGuraContent.ChainState) ? chainCount.Current : 0) + 1 >= (int)milestones[2];
            action = context.CreateAction(final ? freeId : steps[index], out actionContext, target != null ? new[] { target } : Array.Empty<PNStat>(), direction);
            (action as IPNSkillMotionAction)?.BindMovementOwner(() => context.Skill.CancelBindingExecution());
            context.Skill.GetComponent<PNPlayerMove>()?.FaceSkillDirection(new Vector2(direction.x, direction.z));
            action.Start(actionContext); action.Perform(actionContext);
            if (flagged) foreach (var on in abilities.Find(nextFourId)) if (on is PNAbilityState s && s.IsActive) s.End();
            step.SetAmount((index + 1) % Steps);
            Reward(abilities, index, chainStrike, final, flagged, actionContext.Mode == PNSkillMode.Authority);
            abilities.GrantState(windowId);
            context.Commit(target != null ? new[] { target } : null, direction);
            if (actionContext.Mode == PNSkillMode.Authority) Casts++;
            failed = false;
            return true;
        }
        finally { if (failed) { try { action?.Cancel(actionContext); } catch { } Finish(true); } }
    }

    // The strike's reward. In Burst a HELD strike spends one Wave (the chain): the count reaches 6, 12, 18 (each +Fang, the first two keep the bonus state alive, the last gives the free Skill Burst); any other strike
    // gives the table's BurstStack / Wave. The chain lives while its short State is refreshed; when it lapses the milestone states (the bonuses) lapse with it.
    private void Reward(PNAbilityCollection abilities, int index, bool chainStrike, bool final, bool flagged, bool authority)
    {
        var wave = abilities.GetCounter(PNGuraContent.WaveStack);
        if (!(chainStrike && wave.TryConsume(1)))
        {
            // The flagged fourth strike needs no hit: it pays when it is cast (and its hits pay nothing more). Any other strike pays per hit (PNGuraSkill.OnHitConfirmed).
            if (flagged) { GuraResources.Gain(abilities, gainBurst[index], gainWave[index], gainCut[index], authority); context.Skill.GetComponent<PNGuraSkill>()?.Prepaid(actionContext); }
            return;
        }
        var count = abilities.GetCounter(PNGuraContent.ChainCount);
        if (!GuraResources.IsOn(abilities, PNGuraContent.ChainState)) { count.SetAmount(0); GuraResources.End(abilities, PNGuraContent.Chain6); GuraResources.End(abilities, PNGuraContent.Chain12); }
        int n = count.Current + 1; count.SetAmount(n);
        abilities.GrantState(PNGuraContent.ChainState);
        var fang = abilities.GetCounter(PNGuraContent.FangStack);
        if (n >= (int)milestones[0]) abilities.GrantState(PNGuraContent.Chain6);
        if (n >= (int)milestones[1]) abilities.GrantState(PNGuraContent.Chain12);
        for (int i = 0; i < milestones.Length; i++) if (n == (int)milestones[i]) fang.Add((int)fangPerMilestone);
        // The last milestone: the free Skill Burst gives what a Skill Burst gives (Wave, the next-strike flag) and no cooldown; the chain starts over.
        if (final) { count.SetAmount(0); GuraResources.End(abilities, PNGuraContent.ChainState); abilities.GrantState(nextFourId); }
    }

    // Reads work on the owner's guess too (a replica); the states are only written through the collection's own grant, which a guess may do.
    private static bool IsOn(PNAbilityCollection abilities, EveId id) => GuraResources.IsOn(abilities, id);

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
        bool done = false;
        try
        {
            using var frame = context.Skill.BeginActionExecutionFrame(delta);
            if (speed == 0d || delta == 0f) { frame.Hold(actionContext); done = action.AdvanceAuxiliary(0f); }
            else frame.Advance(actionContext, delta * (float)speed, speed, step => done = action.AdvanceAuxiliary(step));
        }
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
