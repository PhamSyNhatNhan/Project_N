#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Quet sao: two sweeps of one sector in front of the Starkeeper, the second a beat late and re-aimed at the target's current place (within maxReaimDegrees of the first)
// so a player who dodged early is caught. Every number is the registered payload; the Action only runs the timeline.
public sealed class PNStarkeeperSweepAction : PNSkillAction, IPNSkillMotionAction
{
    // each sweep carries the Starkeeper forward toward the target: during the `step.duration` seconds that END at the hit it moves up to `step.distance` (never closer than `stopDistance`)
    private Action cancelOwner; private PNMotionMotor motor; private float stepDistance, stepSeconds, stopDistance, stepAmount, stepDone; private EveId stepPermission; private PNMotionActorCollision stepCollision;
    private bool stepOpen; private Vector3 stepDirection; private readonly bool[] stepped = new bool[2];
    public void BindMovementOwner(Action cancel) { if (context != null || ended) throw new InvalidOperationException("Bind before Start."); cancelOwner = cancel ?? throw new ArgumentNullException(nameof(cancel)); }
    private PNSkillActionContext context; private float age, windup, gap, reaimAt, recovery, maxReaim, radius, angle, thickness;
    private readonly PNHitboxCast[] casts = new PNHitboxCast[2]; private readonly double[] raw = new double[2]; private readonly double?[] iframe = new double?[2];
    private PNStat caster; private PNCombatSourceSnapshot source; private bool canCrit, reaimed, ended; private readonly bool[] swept = new bool[2];
    private Vector3 first, second; private PNStarkeeperPresentation presentation; private PNVisualBody body; private PNStarkeeperTelegraph telegraph;
    private readonly List<PNStat> hits = new List<PNStat>();
    public override bool HasAuxiliaryTimeline => true;
    public override void Start(PNSkillActionContext c)
    {
        if (context != null || ended) throw new InvalidOperationException("Fresh cast required");
        PNStarkeeperCombatContent.ValidateSweep(c.Data.Action, c.Registry);
        if (!c.Direction.HasValue) throw new InvalidOperationException("Sweep direction required");
        var p = (JObject)c.Data.Action.Perform;
        windup = p["windup"].Value<float>(); gap = p["gap"].Value<float>(); reaimAt = p["reaimAt"].Value<float>(); recovery = p["recovery"].Value<float>(); maxReaim = p["maxReaimDegrees"].Value<float>();
        radius = p["radius"].Value<float>(); angle = p["angle"].Value<float>(); thickness = p["thickness"].Value<float>(); canCrit = p["canCrit"].Value<bool>();
        var step = p["step"]; stepDistance = step["distance"].Value<float>(); stepSeconds = step["duration"].Value<float>(); stopDistance = step["stopDistance"].Value<float>();
        stepPermission = EveId.Parse(step["permissionId"].Value<string>()); stepCollision = (PNMotionActorCollision)Enum.Parse(typeof(PNMotionActorCollision), step["actorCollision"].Value<string>());
        caster = c.Skill.GetComponent<PNStat>();
        for (int i = 0; i < 2; i++) { foreach (var v in PNSkillValues.Parse(p["hits"][i]["value"]).Components) raw[i] += v.Evaluate(caster); iframe[i] = PNDamage.ParseIFrameDuration((JObject)p["hits"][i]); }
        source = caster.CaptureCombatSource();
        first = Flat(c.Direction.Value, Vector3.forward); second = first;
        var world = c.Skill.gameObject.scene.GetPhysicsScene();
        context = c;
        try
        {
            for (int i = 0; i < 2; i++) casts[i] = new PNHitboxCast(EveHitboxShape.Sector(radius, thickness, angle), world);
            c.BeginAction();
            body = c.Skill.GetComponent<PNVisualBody>(); body?.PlayAction("star_sweep", windup + gap + recovery);
            presentation = c.Skill.GetComponent<PNStarkeeperPresentation>(); presentation?.BeginSweep(windup, gap, first);
            telegraph = PNStarkeeperTelegraph.Sector(c.Skill.transform.position, first, radius, angle, c.Skill.gameObject.scene);
        }
        catch { Dispose(); throw; }
    }
    public override void Perform(PNSkillActionContext c) { }
    private static Vector3 Flat(Vector3 v, Vector3 fallback) { v.y = 0; return v.sqrMagnitude < 1e-6f ? fallback : v.normalized; }
    private void Sweep(int index, Vector3 direction)
    {
        swept[index] = true;
        if (!EveRegistrySession.Context.HasAuthority) return;
        var tags = context.Data.Action.PerformTags.ToArray();
        var pose = EveHitboxPose.Planar(context.Skill.transform.position + Vector3.up * .3f, direction);
        casts[index].Query(pose, age, hits, caster, ~0, QueryTriggerInteraction.Ignore, target => caster.Targets(target) && !target.IsDead && PNWorldGameplay.CanTarget(context.Skill, target));
        foreach (var target in hits.ToArray())
        {
            if (ended) break;
            context.ReceiveHit(target, target.transform.position, caster.CreateDamage(raw[index], canCrit, iframe[index], tags), raw[index], source, index);
            if (!ended && target.IsReady) casts[index].CommitHit(target, age);
        }
    }
    private PNStat FirstTarget()
    {
        foreach (var target in context.Targets ?? Array.Empty<PNStat>()) if (target != null && target.IsReady && !target.IsDead) return target;
        return null;
    }
    // Drives the forward step of sweep `index` (hit at `hitTime`): opens it when the window starts, moves by the fraction of the window that has passed, closes it at the hit.
    private void Step(int index, float hitTime)
    {
        if (stepped[index] || cancelOwner == null) return;
        float from = hitTime - stepSeconds;
        if (age < from) return;
        if (!stepOpen)
        {
            var target = FirstTarget(); var self = context.Skill.transform.position;
            if (target == null || !context.Skill.Controls.IsAllowed(stepPermission)) { stepped[index] = true; return; }
            var toward = target.transform.position - self; toward.y = 0; float distance = toward.magnitude;
            stepAmount = Mathf.Min(stepDistance, Mathf.Max(0f, distance - stopDistance)); stepDone = 0f;
            if (stepAmount < .001f || distance < 1e-4f) { stepped[index] = true; return; }
            stepDirection = toward / distance;
            try { motor = new PNMotionMotor(context.Skill.GetComponent<PNMove>(), stepPermission, stepCollision, false, cancelOwner); }
            catch (InvalidOperationException) { motor = null; stepped[index] = true; return; }     // no local motion rights (a replica or a held body): the sweep still happens in place
            stepOpen = true;
        }
        float fraction = Mathf.Clamp01((age - from) / stepSeconds);
        if (motor != null && motor.CanWrite && fraction > stepDone) { motor.Step(stepDirection * (stepAmount * (fraction - stepDone))); stepDone = fraction; }
        if (fraction >= 1f || motor == null || !motor.CanWrite) CloseStep(index);
    }
    private void CloseStep(int index) { stepped[index] = true; stepOpen = false; motor?.Dispose(); motor = null; }
    private Vector3 Reaim()
    {
        foreach (var target in context.Targets ?? Array.Empty<PNStat>())
        {
            if (target == null || !target.IsReady || target.IsDead) continue;
            var want = Flat(target.transform.position - context.Skill.transform.position, first);
            float turn = Mathf.Clamp(Vector3.SignedAngle(first, want, Vector3.up), -maxReaim, maxReaim);
            return Quaternion.AngleAxis(turn, Vector3.up) * first;
        }
        return first;
    }
    public override bool AdvanceAuxiliary(float dt)
    {
        if (ended) return true;
        age += dt;
        telegraph?.SetPhase(Mathf.Clamp01(age / windup)); presentation?.SetAge(age);
        if (!swept[0]) Step(0, windup);
        if (!swept[0] && age >= windup) { Sweep(0, first); telegraph?.Hide(); }
        if (!reaimed && age >= windup + reaimAt)
        {
            reaimed = true; second = Reaim(); presentation?.SetAim(second); telegraph?.Show(second);
            var actor = body != null ? body.Actor : null; var camera = PNViewCamera.Current;
            if (actor != null) actor.SetFacingLeft(Vector3.Dot(second, camera != null ? camera.transform.right : Vector3.right) < 0);
        }
        if (!swept[1] && reaimed) Step(1, windup + gap);
        if (!swept[1] && age >= windup + gap) { Sweep(1, second); telegraph?.Hide(); }
        return age >= windup + gap + recovery;
    }
    public override void Exit(PNSkillActionContext c) { try { c.CompleteAction(); } finally { Dispose(); } }
    public override void Cancel(PNSkillActionContext c) { try { c.CancelEffects(); } finally { Dispose(); } }
    public override void Dispose()
    {
        if (ended) return; ended = true;
        foreach (var cast in casts) cast?.Dispose();
        motor?.Dispose(); motor = null;
        telegraph?.Dispose(); telegraph = null; presentation?.EndPose(); presentation = null; body?.EndAction(); body = null; context = null;
    }
}
#else
// Standalone validates registered schemas; Unity-only rendering/physics execute in native checks.
public sealed class PNStarkeeperSweepAction : PNSkillAction
{
    public override void Start(PNSkillActionContext c) { PNStarkeeperCombatContent.ValidateSweep(c.Data.Action, c.Registry); }
    public override void Perform(PNSkillActionContext c) { }
    public override void Exit(PNSkillActionContext c) { }
    public override void Cancel(PNSkillActionContext c) { }
}
public sealed class PNStarkeeperStarPointAction : PNSkillAction
{
    public override void Start(PNSkillActionContext c) { PNStarkeeperCombatContent.ValidateStarPoint(c.Data.Action, c.Registry); }
    public override void Perform(PNSkillActionContext c) { }
    public override void Exit(PNSkillActionContext c) { }
    public override void Cancel(PNSkillActionContext c) { }
}
#endif
