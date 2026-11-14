#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// What the three Starreaper Actions share: where the owner looks and which tags a spawned damage carries.
internal static class PNStarreaperCast
{
    public static readonly EveId CreateProjectile = EveId.Parse("project_n:create_projectile"), CreateArea = EveId.Parse("project_n:create_area"), CreateRain = EveId.Parse("project_n:starreaper_rain_blasts"), CreateSummon = EveId.Parse("project_n:create_summon");
    // The cast's first target (the behavior passes the one it fights), or null.
    public static PNStat Target(PNSkillActionContext c) { if (c.Targets == null) return null; foreach (var t in c.Targets) if (t != null && t.IsReady && !t.IsDead) return t; return null; }
    public static Vector3 Planar(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.zero; }
    // The planar direction to look/aim: toward the target while there is one, otherwise the cast's own direction.
    public static Vector3 Aim(PNSkillActionContext c, PNStat target)
    {
        var d = target != null ? Planar(target.transform.position - c.Skill.transform.position) : Vector3.zero;
        return d.sqrMagnitude > 0f ? d : Planar(c.Direction ?? Vector3.forward);
    }
    public static void Face(PNSkillActionContext c, PNVisualBody body, Vector3 direction)
    {
        if (direction.sqrMagnitude < 1e-8f) return;
        c.Skill.GetComponent<PNEnemyMove>()?.TryFaceSkill(new Vector2(direction.x, direction.z));
        var actor = body != null ? body.Actor : null;
        if (actor != null) { var camera = PNViewCamera.Current; actor.SetFacingLeft(Vector3.Dot(direction, camera != null ? camera.transform.right : Vector3.right) < 0); }
    }
    // The tags a damage made by this cast carries: the cast's own (category and perform tags) plus the area's registered ones, once each.
    public static JArray Tags(PNSkillActionContext c, JToken registered) =>
        new JArray(c.EffectiveTags().Concat(PNSkillEffect.Ids(registered)).Select(x => x.ToString()).Distinct());
}

// Xuyên Tinh: the owner stands still and aims at the target for `windup` seconds (the bow turns, the string is drawn), then shoots one arrow; the registered create_projectile payload
// re-aims at the best target at that moment, so the arrow goes where the target is at the shot, not where it was when the aim began.
public sealed class PNStarreaperPierceAction : PNSkillAction
{
    private PNSkillActionContext context; private float age, windup, recovery; private bool fired, ended;
    private PNStarreaperPresentation presentation; private PNVisualBody body; private PNSkillOperation operation; private PNStat target;
    public override bool HasAuxiliaryTimeline => true;
    public override void Start(PNSkillActionContext c)
    {
        if (context != null || ended) throw new InvalidOperationException("Fresh cast required");
        PNStarreaperCombatContent.ValidatePierce(c.Data.Action, c.Registry);
        context = c; var p = c.Data.Action.Perform; windup = p["windup"].Value<float>(); recovery = p["recovery"].Value<float>();
        c.BeginAction();
        presentation = c.Skill.GetComponent<PNStarreaperPresentation>(); body = c.Skill.GetComponent<PNVisualBody>();
        target = PNStarreaperCast.Target(c); var aim = PNStarreaperCast.Aim(c, target);
        body?.PlayAction("pierce_charge", windup + recovery); body?.Actor?.SetPhase(0); PNStarreaperCast.Face(c, body, aim); presentation?.BeginDraw(windup, aim, true);
    }
    public override void Perform(PNSkillActionContext c) { }
    public override bool AdvanceAuxiliary(float dt)
    {
        if (ended) return true;
        age += dt;
        if (!fired)
        {
            if (target != null && (!target.IsReady || target.IsDead)) target = null;
            var aim = PNStarreaperCast.Aim(context, target); PNStarreaperCast.Face(context, body, aim);
            presentation?.SetAim(aim); presentation?.SetAge(age); body?.Actor?.SetPhase(Mathf.Clamp01(age/windup));
            if (age >= windup)
            {
                fired = true; presentation?.Tick(0); presentation?.Release(); body?.PlayAction("pierce_release", recovery); body?.Actor?.SetPhase(0);
                operation = CreateOperation(context, PNStarreaperCast.CreateProjectile); operation.Execute(context, context.Data.Action.Perform["shot"]);
                presentation?.EndPose();
            }
        }
        if(fired)body?.Actor?.SetPhase(Mathf.Clamp01((age-windup)/recovery));
        return age >= windup + recovery;
    }
    public override void Exit(PNSkillActionContext c) { try { c.CompleteAction(); } finally { Dispose(); } }
    public override void Cancel(PNSkillActionContext c) { try { c.CancelEffects(); operation?.Cancel(); } finally { Dispose(); } }
    public override void Dispose()
    {
        if (ended) return; ended = true;
        presentation?.EndPose(); presentation = null; body?.EndAction(); body = null; operation?.Dispose(); operation = null; target = null; context = null;
    }
}

// Mưa Tinh: gathers for `windup` seconds (stun cancels it here), then shoots one arrow into the sky and hands the rest to the rain Operation, which creates one owner-dependent Cylinder Blast per landing (each owns its warning, its flight and its explosion).
// Once the blasts exist the cast no longer matters: stunning the owner afterwards does not stop the rain; the owner's death does (the blasts depend on their owner).
public sealed class PNStarreaperRainAction : PNSkillAction
{
    private PNSkillActionContext context; private float age, windup, recovery; private bool fired, ended;
    private PNStarreaperPresentation presentation; private PNVisualBody body; private PNSkillOperation operation; private PNStat target;
    public override bool HasAuxiliaryTimeline => true;
    public override void Start(PNSkillActionContext c)
    {
        if (context != null || ended) throw new InvalidOperationException("Fresh cast required");
        PNStarreaperCombatContent.ValidateRain(c.Data.Action, c.Registry);
        if (!c.Position.HasValue) throw new InvalidOperationException("Rain position required");
        context = c; var p = c.Data.Action.Perform; windup = p["windup"].Value<float>(); recovery = p["recovery"].Value<float>();
        c.BeginAction();
        presentation = c.Skill.GetComponent<PNStarreaperPresentation>(); body = c.Skill.GetComponent<PNVisualBody>();
        target = PNStarreaperCast.Target(c); var aim = PNStarreaperCast.Aim(c, target);
        body?.PlayAction("rain_charge", windup + recovery); body?.Actor?.SetPhase(0); PNStarreaperCast.Face(c, body, aim);
        presentation?.BeginDraw(windup, Vector3.up * .99f + aim * .14f, true, true);
    }
    public override void Perform(PNSkillActionContext c) { }
    public override bool AdvanceAuxiliary(float dt)
    {
        if (ended) return true;
        age += dt;
        if (!fired)
        {
            if (target != null && (!target.IsReady || target.IsDead)) target = null;
            var aim = PNStarreaperCast.Aim(context, target); PNStarreaperCast.Face(context, body, aim);
            presentation?.SetAim(Vector3.up * .99f + aim * .14f); presentation?.SetAge(age); body?.Actor?.SetPhase(Mathf.Clamp01(age / windup));
            if (age >= windup)
            {
                fired = true; presentation?.Tick(0); presentation?.Release();
                body?.PlayAction("rain_release", recovery); body?.Actor?.SetPhase(0);
                var input = (JObject)context.Data.Action.Perform["rain"].DeepClone();
                input["tags"] = PNStarreaperCast.Tags(context, input["tags"]);
                operation = CreateOperation(context, PNStarreaperCast.CreateRain); operation.Execute(context, input);
                presentation?.EndPose();
            }
        }
        if (fired) body?.Actor?.SetPhase(Mathf.Clamp01((age - windup) / recovery));
        return age >= windup + recovery;
    }
    public override void Exit(PNSkillActionContext c) { try { c.CompleteAction(); } finally { Dispose(); } }
    public override void Cancel(PNSkillActionContext c) { try { c.CancelEffects(); } finally { Dispose(); } }      // the blasts, once created, belong to the manager and depend on the owner, not on this cast
    public override void Dispose()
    {
        if (ended) return; ended = true;
        presentation?.EndPose(); presentation = null; body?.EndAction(); body = null; operation?.Dispose(); operation = null; target = null; context = null;
    }
}

// Tàn Ảnh: the shadow area appears the moment the retreat starts, at the old spot; the owner steps back (the registered straight CurveXZ trajectory: it stops at a wall and never
// passes an obstacle), and right after the retreat shoots the Xuyên Tinh arrow at the target. The shadow shoots its own two arrows later and does not depend on this cast or its owner.
public sealed class PNStarreaperAfterimageAction : PNAgentTrajectoryAction
{
    private PNSkillActionContext cast; private float recovery, recoverAge, retreat; private bool shot, ended;
    private PNStarreaperPresentation presentation; private PNVisualBody body; private PNSkillOperation operation; private PNStat target;
    protected override JToken MotionPayload(PNSkillActionContext value) => value.Data.Action.Perform["motion"];
    public override void Start(PNSkillActionContext c)
    {
        if (ended) throw new InvalidOperationException("Fresh cast required");
        PNStarreaperCombatContent.ValidateAfterimage(c.Data.Action, c.Registry);
        base.Start(c);                                           // reads the motion schema, locks movement, checks the floor along the retreat
        cast = c; var p = c.Data.Action.Perform; recovery = p["recovery"].Value<float>(); retreat = p["motion"]["duration"].Value<float>();
        presentation = c.Skill.GetComponent<PNStarreaperPresentation>(); body = c.Skill.GetComponent<PNVisualBody>();
        target = PNStarreaperCast.Target(c); PNStarreaperCast.Face(c, body, PNStarreaperCast.Aim(c, target));    // it keeps facing the target while stepping back
        body?.PlayAction("dash", retreat + .1f);
    }
    public override void Perform(PNSkillActionContext c)
    {
        base.Perform(c);
        if (Outcome == PNMotionOutcome.Running) { presentation?.BeginRetreat(retreat); CreateShadow(c); }
    }
    private void CreateShadow(PNSkillActionContext c)
    {
        // The shadow is a summon of the caster at the old spot (group of one: a new shadow replaces the old). The summon asks the owner's skill for its side when it starts.
        using (var summon = CreateOperation(c, PNStarreaperCast.CreateSummon)) summon.Execute(c, c.Data.Action.Perform["summon"]);
    }
    public override bool AdvanceAuxiliary(float dt)
    {
        if (ended) return true;
        if (!shot)
        {
            if (!base.AdvanceAuxiliary(dt)) return false;
            // the retreat is over (finished, or stopped by a wall); it could not start at all when the floor check failed: then there is no shot
            if (Outcome == PNMotionOutcome.InvalidLanding || Outcome == PNMotionOutcome.BudgetExceeded || Outcome == PNMotionOutcome.Cancelled) return true;
            shot = true;
            if (target != null && (!target.IsReady || target.IsDead)) target = null;
            var aim = PNStarreaperCast.Aim(cast, target); PNStarreaperCast.Face(cast, body, aim);
            body?.PlayAction("shoot", recovery); presentation?.BeginDraw(recovery, aim); presentation?.Release();
            operation = CreateOperation(cast, PNStarreaperCast.CreateProjectile); operation.Execute(cast, cast.Data.Action.Perform["shot"]);
            return recovery <= 0f;
        }
        recoverAge += dt; presentation?.SetAge(recoverAge);
        return recoverAge >= recovery;
    }
    public override void Dispose()
    {
        if (!ended) { ended = true; presentation?.EndPose(); presentation = null; body?.EndAction(); body = null; operation?.Dispose(); operation = null; target = null; cast = null; }
        base.Dispose();
    }
}
#else
// Standalone validates registered schemas; Unity-only rendering/physics execute in native checks.
public sealed class PNStarreaperPierceAction : PNSkillAction
{
    public override void Start(PNSkillActionContext c) { PNStarreaperCombatContent.ValidatePierce(c.Data.Action, c.Registry); }
    public override void Perform(PNSkillActionContext c) { }
    public override void Exit(PNSkillActionContext c) { }
    public override void Cancel(PNSkillActionContext c) { }
}
public sealed class PNStarreaperRainAction : PNSkillAction
{
    public override void Start(PNSkillActionContext c) { PNStarreaperCombatContent.ValidateRain(c.Data.Action, c.Registry); }
    public override void Perform(PNSkillActionContext c) { }
    public override void Exit(PNSkillActionContext c) { }
    public override void Cancel(PNSkillActionContext c) { }
}
public sealed class PNStarreaperAfterimageAction : PNSkillAction
{
    public override void Start(PNSkillActionContext c) { PNStarreaperCombatContent.ValidateAfterimage(c.Data.Action, c.Registry); }
    public override void Perform(PNSkillActionContext c) { }
    public override void Exit(PNSkillActionContext c) { }
    public override void Cancel(PNSkillActionContext c) { }
}
public sealed class PNStarreaperProjectile { }
public sealed class PNStarreaperRainBlast { }
public sealed class PNStarreaperRainOperation : PNSkillOperation
{
    public override void Execute(PNSkillActionContext c, Newtonsoft.Json.Linq.JToken data) { PNStarreaperCombatContent.ValidateRainInput(data); }
}
#endif
