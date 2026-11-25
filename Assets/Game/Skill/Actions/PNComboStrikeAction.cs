using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// EN: `project_n:combo_strike_action` (Docs/GuraCharacter.md 4.1): one melee strike of a combo, as a registered timeline. Every number is the registered payload:
//       duration (s)             the whole Action, recovery included
//       radius, innerRadius, angle (degrees), thickness, height   the sector each hit sweeps in front of the caster (`melee_scale` sizes radius/thickness, not the angle)
//       step {at, duration, distance, stopDistance, retreat, permissionId, actorCollision}   optional body motion toward the first target: it ends where it should stand `stopDistance` from the target,
//            never more than `distance` and, only when `retreat` is true, it also backs away when the caster stands closer than `stopDistance`; it starts `at` seconds in and takes `duration`
//       step.height (m, optional): the body also rises and falls in a parabola over the step (a jump); the motion then writes the vertical too
//       hits [{at, value, iframeDuration?}] (none is allowed: a phase that only lasts, such as a dive)   each its own melee hitbox (own damage capture, own hit index for the claim ticket) opened `at` seconds in
//       canCrit, tags
//     The behavior supplies the aim (Direction) and the target (Targets, may be empty: the strike then happens in place). The Action has no network code: the melee hitbox, the body motion and the damage service
//     decide by role (Authority sweeps, Predicted reports, Observed nothing); the Action advances through `AdvanceAuxiliary` like every timeline Action.
// VI: Mot nhat chem cua combo dang timeline: moi so lieu la payload dang ky. Hanh vi cap huong ngam va muc tieu; Action khong co ma mang.
public sealed class PNComboStrikeAction : PNSkillAction, IPNSkillMotionAction
{
    public const int MaxHits = 8;
    public static void Validate(PNSkillActionData data, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(data.Perform);
        PNBuffContentBatch.Fields(p, "duration", "radius", "innerRadius", "angle", "thickness", "height", "step", "hits", "canCrit", "tags");
        Positive(p, "duration", 10); Positive(p, "radius", 20); Positive(p, "angle", 360); Positive(p, "thickness", 10);
        if (p["innerRadius"] != null && !(PNBuffContentBatch.Number(p, "innerRadius") >= 0d && PNBuffContentBatch.Number(p, "innerRadius") < PNBuffContentBatch.Number(p, "radius"))) throw new FormatException("innerRadius must be in [0, radius).");
        if (p["height"] != null) { double h = PNBuffContentBatch.Number(p, "height"); if (!(h >= 0d && h <= 5d)) throw new FormatException("height must be in [0, 5]."); }
        if (p["canCrit"]?.Type != JTokenType.Boolean) throw new FormatException("canCrit required.");
        double duration = PNBuffContentBatch.Number(p, "duration");
        if (p["step"] != null)
        {
            var step = PNBuffContentBatch.Object(p["step"]);
            PNBuffContentBatch.Fields(step, "at", "duration", "distance", "stopDistance", "retreat", "height", "permissionId", "actorCollision");
            if (step["height"] != null) { double hh = PNBuffContentBatch.Number(step, "height"); if (!(hh >= 0d && hh <= 10d)) throw new FormatException("step.height must be in [0, 10]."); }
            if (!(PNBuffContentBatch.Number(step, "at") >= 0d)) throw new FormatException("step.at must be nonnegative.");
            Positive(step, "duration", 5); Positive(step, "distance", 10);
            if (!(PNBuffContentBatch.Number(step, "stopDistance") >= 0d)) throw new FormatException("step.stopDistance must be nonnegative.");
            if (step["retreat"] != null && step["retreat"].Type != JTokenType.Boolean) throw new FormatException("step.retreat must be a boolean.");
            if (step["permissionId"]?.Type != JTokenType.String || step["actorCollision"]?.Type != JTokenType.String) throw new FormatException("Step permission and actor collision required.");
            if (PNBuffContentBatch.Number(step, "at") + PNBuffContentBatch.Number(step, "duration") > duration + 1e-6) throw new FormatException("The step must fit inside the Action duration.");
#if UNITY_5_3_OR_NEWER
            hub.ControlPermissions().Get(EveId.Parse(step["permissionId"].Value<string>()));
            if (!Enum.TryParse(step["actorCollision"].Value<string>(), false, out PNMotionActorCollision collision) || !Enum.IsDefined(typeof(PNMotionActorCollision), collision)) throw new FormatException("Unknown step actor collision.");
#endif
        }
        if (!(p["hits"] is JArray hits) || hits.Count > MaxHits) throw new FormatException("At most " + MaxHits + " hits.");
        double last = -1d;
        foreach (var token in hits)
        {
            var hit = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(hit, "at", "value", "iframeDuration");
            double at = PNBuffContentBatch.Number(hit, "at");
            if (!(at >= 0d && at <= duration) || at < last) throw new FormatException("Hit times must be ordered and inside the Action duration.");
            last = at;
#if UNITY_5_3_OR_NEWER
            PNDamage.ParseIFrameDuration(hit);
#endif
            var values = PNSkillValues.Parse(hit["value"]); values.ValidateStats(hub); values.ValidateNonnegativeAmounts();
        }
        new PNStatSet(hub).ResolveDamageType(data.PerformTags);
    }
    private static void Positive(JObject p, string key, double maximum)
    { double n = PNBuffContentBatch.Number(p, key); if (double.IsNaN(n) || double.IsInfinity(n) || n <= 0 || n > maximum) throw new FormatException("Invalid combo strike number: " + key); }

#if UNITY_5_3_OR_NEWER
    private PNSkillActionContext context;
    private Action cancelOwner;
    private float age, duration, radius, innerRadius, angle, thickness, height;
    private float stepHeight, stepHeightDone; private float stepAt, stepSeconds, stepDistance, stopDistance, stepAmount, stepDone; private bool retreat, stepStarted, stepClosed, stepOpen;
    private EveId stepPermission; private PNMotionActorCollision stepCollision; private bool hasStep;
    private Vector3 stepDirection, aim;
    private PNBodyMotion motor;
    private float[] hitAt; private PNSkillMeleeHitbox[] boxes; private bool[] swept;
    private PNStat caster; private bool ended;
    public override bool HasAuxiliaryTimeline => true;
    // True once every hit has been swept: the rest of the Action is recovery, which the next press may cut short (a combo's next strike), so a machine a few frames behind never refuses it.
    public bool IsRecovering { get { if (swept == null) return false; foreach (var done in swept) if (!done) return false; return true; } }
    public void BindMovementOwner(Action cancel) { if (context != null || ended) throw new InvalidOperationException("Bind before Start."); cancelOwner = cancel ?? throw new ArgumentNullException(nameof(cancel)); }

    public override void Start(PNSkillActionContext c)
    {
        if (context != null || ended) throw new InvalidOperationException("Fresh cast required");
        Validate(c.Data.Action, c.Registry);
        if (!c.Direction.HasValue) throw new InvalidOperationException("Combo strike direction required");
        var p = (JObject)c.Data.Action.Perform;
        duration = p["duration"].Value<float>(); radius = p["radius"].Value<float>(); angle = p["angle"].Value<float>(); thickness = p["thickness"].Value<float>();
        innerRadius = p["innerRadius"] == null ? 0f : p["innerRadius"].Value<float>(); height = p["height"] == null ? .3f : p["height"].Value<float>();
        bool canCrit = p["canCrit"].Value<bool>();
        if (p["step"] is JObject step)
        {
            hasStep = true; stepAt = step["at"].Value<float>(); stepSeconds = step["duration"].Value<float>(); stepDistance = step["distance"].Value<float>(); stopDistance = step["stopDistance"].Value<float>();
            retreat = step["retreat"] != null && step["retreat"].Value<bool>(); stepHeight = step["height"] == null ? 0f : step["height"].Value<float>();
            stepPermission = EveId.Parse(step["permissionId"].Value<string>()); stepCollision = (PNMotionActorCollision)Enum.Parse(typeof(PNMotionActorCollision), step["actorCollision"].Value<string>());
        }
        caster = c.Skill.GetComponent<PNStat>();
        aim = Flat(c.Direction.Value, Vector3.right);
        context = c;
        try
        {
            c.BeginAction();
            var hits = (JArray)p["hits"]; hitAt = new float[hits.Count]; boxes = new PNSkillMeleeHitbox[hits.Count]; swept = new bool[hits.Count];
            // One hitbox per hit, each with its own damage capture taken now, so Buffs gained during the strike change none of its hits.
            for (int i = 0; i < hits.Count; i++)
            {
                hitAt[i] = hits[i]["at"].Value<float>();
                boxes[i] = new PNSkillMeleeHitbox(c, caster, PNSkillValues.Parse(hits[i]["value"]), PNDamage.ParseIFrameDuration((JObject)hits[i]), canCrit, i);
            }
            radius = boxes[0].ScaleLength(radius); thickness = boxes[0].ScaleLength(thickness); if (innerRadius > 0f) innerRadius = boxes[0].ScaleLength(innerRadius);
        }
        catch { Dispose(); throw; }
    }
    public override void Perform(PNSkillActionContext c) { }
    private static Vector3 Flat(Vector3 v, Vector3 fallback) { v.y = 0; return v.sqrMagnitude < 1e-6f ? fallback : v.normalized; }
    private PNStat FirstTarget()
    {
        foreach (var target in context.Targets ?? Array.Empty<PNStat>()) if (target != null && target.IsReady && !target.IsDead) return target;
        return null;
    }
    private void Sweep(int index)
    {
        swept[index] = true;
        var origin = context.Skill.transform.position + Vector3.up * height;
        boxes[index].Open(EveHitboxShape.Sector(radius, thickness, angle, innerRadius), origin, radius, 0, (point, outcome) => { },
            target => !target.IsDead && PNWorldGameplay.CanTarget(context.Skill, target));
        boxes[index].ReportTime = age;
        boxes[index].Sample(EveHitboxPose.Planar(origin, aim), age);
    }
    // The body motion toward (or away from) the first target, spread over [stepAt, stepAt + stepSeconds]; the end point is clamped by distance and by where the caster should stand.
    private void Step()
    {
        if (!hasStep || stepClosed || cancelOwner == null || age < stepAt) return;
        if (!stepStarted)
        {
            stepStarted = true;
            var target = FirstTarget(); var self = context.Skill.transform.position;
            if (!context.Skill.Controls.IsAllowed(stepPermission) || target == null && stepHeight <= 0f) { stepClosed = true; return; }
            // A jump with no target rises and falls on the spot, facing the aim.
            var toward = target != null ? target.transform.position - self : aim; toward.y = 0; float distance = toward.magnitude;
            if (distance < 1e-4f) { stepClosed = true; return; }
            float gap = target != null ? distance - stopDistance : 0f;
            stepAmount = gap >= 0f ? Mathf.Min(stepDistance, gap) : retreat ? -Mathf.Min(stepDistance, -gap) : 0f;
            if (Mathf.Abs(stepAmount) < .001f && stepHeight <= 0f) { stepClosed = true; return; }
            stepDirection = toward / distance * (stepAmount < 0f ? -1f : 1f); stepAmount = Mathf.Abs(stepAmount); stepDone = 0f; stepHeightDone = 0f;
            try { motor = new PNBodyMotion(context.Skill.GetComponent<PNMove>(), stepPermission, stepCollision, stepHeight > 0f, cancelOwner); }
            catch (InvalidOperationException) { motor = null; stepClosed = true; return; }     // no local motion rights (a replica or a held body): the strike still happens in place
            stepOpen = true;
        }
        float fraction = Mathf.Clamp01((age - stepAt) / stepSeconds);
        if (motor != null && motor.CanWrite && fraction > stepDone)
        {
            float arc = stepHeight > 0f ? stepHeight * (4f * fraction * (1f - fraction) - 4f * stepDone * (1f - stepDone)) : 0f;
            motor.Step(stepDirection * (stepAmount * (fraction - stepDone)) + Vector3.up * arc); stepDone = fraction;
        }
        if (fraction >= 1f || motor == null || !motor.CanWrite) CloseStep();
    }
    private void CloseStep() { stepClosed = true; stepOpen = false; motor?.Dispose(); motor = null; }
    public override bool AdvanceAuxiliary(float dt)
    {
        if (ended) return true;
        age += dt;
        Step();
        for (int i = 0; i < boxes.Length; i++) if (!swept[i] && age >= hitAt[i]) Sweep(i);
        return age >= duration;
    }
    public override void Exit(PNSkillActionContext c) { try { c.CompleteAction(); } finally { Dispose(); } }
    public override void Cancel(PNSkillActionContext c) { try { c.CancelEffects(); } finally { Dispose(); } }
    public override void Dispose()
    {
        if (ended) return; ended = true;
        if (boxes != null) foreach (var box in boxes) box?.Dispose();
        motor?.Dispose(); motor = null; stepOpen = false;
        context = null;
    }
#else
    // Standalone validates registered schemas; Unity-only physics and motion run in native checks.
    public override void Start(PNSkillActionContext c) { Validate(c.Data.Action, c.Registry); }
    public override void Perform(PNSkillActionContext c) { }
    public override void Exit(PNSkillActionContext c) { }
    public override void Cancel(PNSkillActionContext c) { }
    public void BindMovementOwner(Action cancel) { }
    public bool IsRecovering => false;
#endif
}
