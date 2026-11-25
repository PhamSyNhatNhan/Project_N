using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Gura's Ultimate slot (Docs/GuraCharacter.md 4.3, 4.4). One cast is a short chain of registered Actions run one after the other over several frames (`Advance`):
//       outside Burst  tap  -> [shark]                    (the water shark lunges up to 1.5 m, 1.5 m blast); gives BurstStack, cooldown 25 s
//                      hold -> [dive, shark]              (the dive: up to 4 s, untouchable and still able to move; releasing or the time running out calls the shark)
//       inside Burst   tap or hold -> ends Burst, then [leap, fangs]  (a leap, one projectile per Fang, then the 4.5 m shark); every Fang is spent
//     The dive's immunity is a Buff the dive skill's own `effects` apply and take back (no code here). One decision per cast (`ultimate_target`: the nearest enemy in reach).
//     Profile data {cooldownAbilityId, reach, shark, dive, gain{burst,wave,cut}, inBurst{leap, fangs, fangId, burstStateId}}. It has no network code.
// VI: O Ultimate cua Gura: mot cast la chuoi Action dang ky chay lan luot qua Advance. Ngoai Burst: tap = ca map; giu = lan (bat tu, van di chuyen), tha/het gio thi ca map.
//     Trong Burst: ket thuc Burst, nhay, ban moi Rang mot vien roi ca map 4.5 m.
public sealed class GuraUltimateBehavior : PNSkillBehavior
{
    public const string DecisionPoint = "ultimate_target";
    private readonly PNSkillBehaviorContext context;
    private readonly EveId cooldownId, sharkId, diveId, leapId, fangsId, fangCounterId, burstStateId;
    private readonly float reach; private readonly int gainBurst, gainWave; private readonly double gainCut;
    private readonly Queue<EveId> phases = new Queue<EveId>();
    private PNSkillAction action; private PNSkillActionContext actionContext; private EveId current; private bool casting, released, diving;
    private PNStat target; private Vector3 direction; private PNStat stat;
    public static int Casts, Dives, BurstCasts, Cancels;
    public bool IsCasting => casting;
    public bool IsDiving => casting && diving;

    public GuraUltimateBehavior(PNSkillBehaviorContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        var data = PNBuffContentBatch.Object(context.Profile.Data);
        cooldownId = EveId.Parse(PNBuffContentBatch.String(data, "cooldownAbilityId")); sharkId = EveId.Parse(PNBuffContentBatch.String(data, "shark")); diveId = EveId.Parse(PNBuffContentBatch.String(data, "dive"));
        reach = (float)PNBuffContentBatch.Number(data, "reach");
        var gain = (JObject)data["gain"]; gainBurst = PNBuffContentBatch.Integer(gain, "burst"); gainWave = PNBuffContentBatch.Integer(gain, "wave"); gainCut = PNBuffContentBatch.Number(gain, "cut");
        var burst = (JObject)data["inBurst"]; leapId = EveId.Parse(PNBuffContentBatch.String(burst, "leap")); fangsId = EveId.Parse(PNBuffContentBatch.String(burst, "fangs"));
        fangCounterId = EveId.Parse(PNBuffContentBatch.String(burst, "fangId")); burstStateId = EveId.Parse(PNBuffContentBatch.String(burst, "burstStateId"));
    }
    public static void Validate(JToken data, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(data); PNBuffContentBatch.Fields(obj, "cooldownAbilityId", "reach", "shark", "dive", "gain", "inBurst");
        EveId.Parse(PNBuffContentBatch.String(obj, "cooldownAbilityId")); EveId.Parse(PNBuffContentBatch.String(obj, "shark")); EveId.Parse(PNBuffContentBatch.String(obj, "dive"));
        double r = PNBuffContentBatch.Number(obj, "reach"); if (!(r > 0d && r <= 50d)) throw new FormatException("gura_ultimate reach must be in (0, 50].");
        var gain = PNBuffContentBatch.Object(obj["gain"]); PNBuffContentBatch.Fields(gain, "burst", "wave", "cut"); PNBuffContentBatch.Integer(gain, "burst"); PNBuffContentBatch.Integer(gain, "wave"); PNBuffContentBatch.Number(gain, "cut");
        var burst = PNBuffContentBatch.Object(obj["inBurst"]); PNBuffContentBatch.Fields(burst, "leap", "fangs", "fangId", "burstStateId");
        foreach (var key in new[] { "leap", "fangs", "fangId", "burstStateId" }) EveId.Parse(PNBuffContentBatch.String(burst, key));
    }
    public static void ValidateReferences(PNContentReferenceContext context, JToken data)
    {
        var obj = PNBuffContentBatch.Object(data); var burst = PNBuffContentBatch.Object(obj["inBurst"]);
        context.Require<EveSkillDefinition>(obj["shark"], "shark"); context.Require<EveSkillDefinition>(obj["dive"], "dive");
        context.Require<EveSkillDefinition>(burst["leap"], "inBurst.leap"); context.Require<EveSkillDefinition>(burst["fangs"], "inBurst.fangs");
    }

    public override void Input(PNSkillInputEvent input)
    {
        // Releasing a held press ends the dive; a tap or a hold that has lasted long enough starts the cast.
        if (input.Interaction == PNSkillInteraction.Hold && input.Phase == PNSkillInputPhase.Canceled) { if (casting && diving) released = true; return; }
        if (input.Phase != PNSkillInputPhase.Performed || casting) return;
        if (input.Interaction == PNSkillInteraction.Tap) Cast(false);
        else if (input.Interaction == PNSkillInteraction.Hold) Cast(true);
    }

    // Public so a fixture can cast without input.
    public bool Cast(bool hold)
    {
        if (casting) return false;
        var abilities = context.Abilities; bool burst = GuraResources.InBurst(abilities);
        var fangs = abilities.GetCounter(fangCounterId);
        var cooldown = abilities.GetCooldown(cooldownId);
        // Outside Burst the Ultimate has its cooldown; inside it has none, only the Fangs it needs.
        if (burst ? fangs.Current < 1 : !cooldown.CanConsume()) return false;
        if (!context.TryBegin()) return false;
        casting = true; released = false; diving = false; bool failed = true;
        try
        {
            stat = context.Skill.GetComponent<PNStat>();
            target = context.Skill.CurrentInvocation.Choose<PNStat>(DecisionPoint, () => Nearest(), null, reach);
            direction = AimAt(target);
            phases.Clear();
            if (burst) { phases.Enqueue(leapId); phases.Enqueue(fangsId); }
            else { if (hold) phases.Enqueue(diveId); phases.Enqueue(sharkId); }
            if (!burst && !cooldown.TryConsume()) throw new InvalidOperationException("Ultimate cooldown changed during the cast.");
            bool authority = StartNext(true);
            if (burst) { GuraResources.End(abilities, burstStateId); GuraResources.End(abilities, PNGuraContent.ChainState); GuraResources.End(abilities, PNGuraContent.Chain6); GuraResources.End(abilities, PNGuraContent.Chain12); BurstCasts++; }
            context.Commit(target != null ? new[] { target } : null, direction);
            if (authority) Casts++;
            failed = false; return true;
        }
        finally { if (failed) { try { action?.Cancel(actionContext); } catch { } Finish(true); } }
    }

    // Starts the next phase; true when this machine has the authority's view of the cast.
    private bool StartNext(bool first)
    {
        current = phases.Dequeue(); DisposeAction();
        action = context.CreateAction(current, out actionContext, target != null && target.IsReady ? new[] { target } : Array.Empty<PNStat>(), direction);
        (action as IPNSkillMotionAction)?.BindMovementOwner(() => context.Skill.CancelBindingExecution());
        context.Skill.GetComponent<PNPlayerMove>()?.FaceSkillDirection(new Vector2(direction.x, direction.z));
        action.Start(actionContext); action.Perform(actionContext);
        diving = current.Equals(diveId); if (diving) Dives++;
                if (current.Equals(fangsId)) context.Abilities.GetCounter(fangCounterId).SetAmount(0);
        return actionContext.Mode == PNSkillMode.Authority;
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
        // The dive ends when the player lets go: the dive is taken back (its immunity with it) and the shark follows.
        if (diving && released && !done)
        {
            try { action.Cancel(actionContext); } finally { DisposeAction(); }
            released = false; diving = false; StartNext(false); return;
        }
        if (!done) return;
        try { action.Exit(actionContext); } finally { DisposeAction(); }
        if (phases.Count > 0) { diving = false; StartNext(false); return; }
        Finish(false);
    }

    public override void Cancel()
    {
        if (!casting) return;
        Cancels++;
        try { action?.Cancel(actionContext); } finally { DisposeAction(); casting = false; phases.Clear(); }
    }
    private void Finish(bool cancelled) { var owner = casting; DisposeAction(); casting = false; diving = false; phases.Clear(); if (owner) context.Complete(cancelled); }
    private void DisposeAction() { try { action?.Dispose(); } finally { action = null; actionContext = null; } }
    public override void Dispose() { DisposeAction(); casting = false; phases.Clear(); }
}
