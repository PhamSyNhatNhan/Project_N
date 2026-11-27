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
//       `step` is one such object, or an array of up to MaxSteps of them in time order without overlap (approach, then step back). A later step still measures from where the
//            target was last seen when it is gone by then.
//       recoveryCancelAt (s, optional): the earliest age at which the next press may cut the recovery short; the last hit and the end of the last step come first in any case
//       hits [{at, value, iframeDuration?, yawDegrees?}]   each opens a separate melee hitbox at its own age; yawDegrees rotates the sector relative to cast aim (default 0)
//       lockMovement (optional)  blocks manual controls through recovery; scripted motion keeps its own permission
//       hits[].radius, hits[].innerRadius, hits[].angle, hits[].thickness (optional)   that hit's own sector; a missing one is the strike's
//       guard {at, duration, buffId} (optional)   a registered Buff on the caster for that part of the strike (its apply input takes the duration), given through the context's Buff door; a cancel
//            is cleaned up by the skill data's own OnCancel remove
//       canCrit, tags
//     The behavior supplies the aim (Direction) and the target (Targets, may be empty: the strike then happens in place). The Action has no network code: the melee hitbox, the body motion and the damage service
//     decide by role (Authority sweeps, Predicted reports, Observed nothing); the Action advances through `AdvanceAuxiliary` like every timeline Action.
// VI: Mot nhat chem cua combo dang timeline: moi so lieu la payload dang ky. Hanh vi cap huong ngam va muc tieu; Action khong co ma mang.
public sealed class PNComboStrikeAction : PNSkillAction, IPNSkillMotionAction
{
    public const int MaxHits = 8, MaxSteps = 4;
    public static void Validate(PNSkillActionData data, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(data.Perform);
        PNBuffContentBatch.Fields(p, "duration", "radius", "innerRadius", "angle", "thickness", "height", "step", "hits", "canCrit", "tags", "visualState", "lockMovement", "recoveryCancelAt", "guard");
        if (p["lockMovement"] != null && p["lockMovement"].Type != JTokenType.Boolean)
            throw new FormatException("lockMovement must be a boolean.");
        if (p["visualState"] != null && p["visualState"].Type != JTokenType.String)
            throw new FormatException("visualState must be a string.");
        Positive(p, "duration", 10); Positive(p, "radius", 20); Positive(p, "angle", 360); Positive(p, "thickness", 10);
        if (p["innerRadius"] != null && !(PNBuffContentBatch.Number(p, "innerRadius") >= 0d && PNBuffContentBatch.Number(p, "innerRadius") < PNBuffContentBatch.Number(p, "radius"))) throw new FormatException("innerRadius must be in [0, radius).");
        if (p["height"] != null) { double h = PNBuffContentBatch.Number(p, "height"); if (!(h >= 0d && h <= 5d)) throw new FormatException("height must be in [0, 5]."); }
        if (p["canCrit"]?.Type != JTokenType.Boolean) throw new FormatException("canCrit required.");
        double duration = PNBuffContentBatch.Number(p, "duration");
        if (p["recoveryCancelAt"] != null)
        {
            double cancel = PNBuffContentBatch.Number(p, "recoveryCancelAt");
            if (!(cancel >= 0d && cancel <= duration)) throw new FormatException("recoveryCancelAt must be inside the Action duration.");
        }
        if (p["step"] != null)
        {
            var steps = Steps(p["step"]);
            if (steps.Count < 1 || steps.Count > MaxSteps) throw new FormatException("step must hold 1 to " + MaxSteps + " motions.");
            double free = 0d;
            foreach (var token in steps)
            {
                var step = PNBuffContentBatch.Object(token);
                PNBuffContentBatch.Fields(step, "at", "duration", "distance", "stopDistance", "retreat", "height", "permissionId", "actorCollision");
                if (step["height"] != null) { double hh = PNBuffContentBatch.Number(step, "height"); if (!(hh >= 0d && hh <= 10d)) throw new FormatException("step.height must be in [0, 10]."); }
                if (!(PNBuffContentBatch.Number(step, "at") >= 0d)) throw new FormatException("step.at must be nonnegative.");
                Positive(step, "duration", 5); Positive(step, "distance", 10);
                if (!(PNBuffContentBatch.Number(step, "stopDistance") >= 0d)) throw new FormatException("step.stopDistance must be nonnegative.");
                if (step["retreat"] != null && step["retreat"].Type != JTokenType.Boolean) throw new FormatException("step.retreat must be a boolean.");
                if (step["permissionId"]?.Type != JTokenType.String || step["actorCollision"]?.Type != JTokenType.String) throw new FormatException("Step permission and actor collision required.");
                double at = PNBuffContentBatch.Number(step, "at"), end = at + PNBuffContentBatch.Number(step, "duration");
                if (at < free - 1e-6) throw new FormatException("Steps must be in time order without overlap.");
                if (end > duration + 1e-6) throw new FormatException("The step must fit inside the Action duration.");
                free = end;
#if UNITY_5_3_OR_NEWER
                hub.ControlPermissions().Get(EveId.Parse(step["permissionId"].Value<string>()));
                if (!Enum.TryParse(step["actorCollision"].Value<string>(), false, out PNMotionActorCollision collision) || !Enum.IsDefined(typeof(PNMotionActorCollision), collision)) throw new FormatException("Unknown step actor collision.");
#endif
            }
        }
        if (p["guard"] != null)
        {
            var guard = PNBuffContentBatch.Object(p["guard"]); PNBuffContentBatch.Fields(guard, "at", "duration", "buffId");
            double from = PNBuffContentBatch.Number(guard, "at");
            if (!(from >= 0d && from <= duration)) throw new FormatException("guard.at must be inside the Action duration.");
            Positive(guard, "duration", 10); EveId.Parse(PNBuffContentBatch.String(guard, "buffId"));
        }
        if (!(p["hits"] is JArray hits) || hits.Count > MaxHits) throw new FormatException("At most " + MaxHits + " hits.");
        double last = -1d;
        foreach (var token in hits)
        {
            var hit = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(hit, "at", "value", "iframeDuration", "yawDegrees", "radius", "innerRadius", "angle", "thickness");
            if (hit["radius"] != null) Positive(hit, "radius", 20);
            if (hit["angle"] != null) Positive(hit, "angle", 360);
            if (hit["thickness"] != null) Positive(hit, "thickness", 10);
            if (hit["innerRadius"] != null)
            {
                double inner = PNBuffContentBatch.Number(hit, "innerRadius");
                if (!(inner >= 0d && inner < PNBuffContentBatch.Number(hit["radius"] != null ? hit : p, "radius"))) throw new FormatException("Hit innerRadius must be in [0, radius).");
            }
            double at = PNBuffContentBatch.Number(hit, "at");
            if (!(at >= 0d && at <= duration) || at < last) throw new FormatException("Hit times must be ordered and inside the Action duration.");
            if (hit["yawDegrees"] != null)
            {
                double yaw = PNBuffContentBatch.Number(hit, "yawDegrees");
                if (!(yaw >= -180d && yaw <= 180d)) throw new FormatException("Hit yawDegrees must be in [-180, 180].");
            }
            last = at;
#if UNITY_5_3_OR_NEWER
            PNDamage.ParseIFrameDuration(hit);
#endif
            var values = PNSkillValues.Parse(hit["value"]); values.ValidateStats(hub); values.ValidateNonnegativeAmounts();
        }
        new PNStatSet(hub).ResolveDamageType(data.PerformTags);
    }
    // `step` as a list, whether the data wrote one motion or several.
    internal static JArray Steps(JToken step) => step is JArray list ? list : step == null ? new JArray() : new JArray(step.DeepClone());
    private static void Positive(JObject p, string key, double maximum)
    { double n = PNBuffContentBatch.Number(p, key); if (double.IsNaN(n) || double.IsInfinity(n) || n <= 0 || n > maximum) throw new FormatException("Invalid combo strike number: " + key); }

#if UNITY_5_3_OR_NEWER
    internal const string StepEndFact = "step_end";
    internal bool PresentFirstStepEnd { get; set; }
    private PNSkillActionContext context;
    private Action cancelOwner;
    private float age, duration, radius, innerRadius, angle, thickness, height;
    private struct StepData { public float At, Seconds, Distance, StopDistance, Height; public bool Retreat; public EveId Permission; public PNMotionActorCollision Collision; }
    private StepData[] steps; private int stepIndex; private bool stepStarted;
    private float stepAmount, stepDone, cancelAt;
    private Vector3 stepDirection, aim, focus; private PNStat followed; private bool hasFocus;
    private PNBodyMotion motor;
    private PNSkillOperation movementLock;
    private float[] hitAt; private PNSkillMeleeHitbox[] boxes; private bool[] swept;
    private struct Shape { public float Radius, Inner, Angle, Thickness; }
    private Shape[] shapes; private float guardAt, guardSeconds; private EveId guardBuff; private bool guardPending;
    private Vector3[] hitDirections;
    private PNStat caster; private bool ended;
    public override bool HasAuxiliaryTimeline => true;
    // True once every hit has been swept: the rest of the Action is recovery, which the next press may cut short (a combo's next strike), so a machine a few frames behind never refuses it.
    // A step still to be walked, and `recoveryCancelAt` when the data names one, hold the cut back as well: a strike whose way out is part of it is not recovering yet.
    public bool IsRecovering
    {
        get
        {
            if (swept == null || age < cancelAt) return false;
            foreach (var done in swept) if (!done) return false;
            return cancelOwner == null || steps == null || stepIndex >= steps.Length;
        }
    }
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
        var motions = Steps(p["step"]);
        steps = new StepData[motions.Count];
        for (int i = 0; i < steps.Length; i++)
        {
            var step = (JObject)motions[i];
            steps[i] = new StepData
            {
                At = step["at"].Value<float>(), Seconds = step["duration"].Value<float>(), Distance = step["distance"].Value<float>(), StopDistance = step["stopDistance"].Value<float>(),
                Retreat = step["retreat"] != null && step["retreat"].Value<bool>(), Height = step["height"] == null ? 0f : step["height"].Value<float>(),
                Permission = EveId.Parse(step["permissionId"].Value<string>()), Collision = (PNMotionActorCollision)Enum.Parse(typeof(PNMotionActorCollision), step["actorCollision"].Value<string>())
            };
        }
        cancelAt = p["recoveryCancelAt"]?.Value<float>() ?? 0f;
        caster = c.Skill.GetComponent<PNStat>();
        aim = Flat(c.Direction.Value, Vector3.right);
        context = c;
        try
        {
            c.BeginAction();
            if (p["lockMovement"]?.Value<bool>() == true)
            {
                movementLock = CreateOperation(c, EveId.Parse("base:lock_movement"));
                movementLock.Execute(c, null);
            }
            var hits = (JArray)p["hits"]; hitAt = new float[hits.Count]; boxes = new PNSkillMeleeHitbox[hits.Count]; swept = new bool[hits.Count];
            hitDirections = new Vector3[hits.Count]; shapes = new Shape[hits.Count];
            var guard = p["guard"] as JObject; guardPending = guard != null;
            if (guardPending) { guardAt = guard["at"].Value<float>(); guardSeconds = guard["duration"].Value<float>(); guardBuff = EveId.Parse(guard["buffId"].Value<string>()); }
            // One hitbox per hit, each with its own damage capture taken now, so Buffs gained during the strike change none of its hits.
            for (int i = 0; i < hits.Count; i++)
            {
                hitAt[i] = hits[i]["at"].Value<float>();
                hitDirections[i] = HitDirection(aim, hits[i]);
                boxes[i] = new PNSkillMeleeHitbox(c, caster, PNSkillValues.Parse(hits[i]["value"]), PNDamage.ParseIFrameDuration((JObject)hits[i]), canCrit, i);
                float inner = hits[i]["innerRadius"]?.Value<float>() ?? innerRadius;
                shapes[i] = new Shape
                {
                    Radius = boxes[i].ScaleLength(hits[i]["radius"]?.Value<float>() ?? radius), Thickness = boxes[i].ScaleLength(hits[i]["thickness"]?.Value<float>() ?? thickness),
                    Inner = inner > 0f ? boxes[i].ScaleLength(inner) : 0f, Angle = hits[i]["angle"]?.Value<float>() ?? angle
                };
            }
            if (boxes.Length > 0) { radius = boxes[0].ScaleLength(radius); thickness = boxes[0].ScaleLength(thickness); if (innerRadius > 0f) innerRadius = boxes[0].ScaleLength(innerRadius); }
            else if (c.Registry.Actions().Get(c.Data.Action.Id).PresentationScaleTag.HasValue) c.CommitPresentationScale(1d);
        }
        catch { Dispose(); throw; }
    }
    public override void Perform(PNSkillActionContext c) { }
    private static Vector3 Flat(Vector3 v, Vector3 fallback) { v.y = 0; return v.sqrMagnitude < 1e-6f ? fallback : v.normalized; }
    internal static Vector3 HitDirection(Vector3 aim, JToken hit) =>
        Quaternion.AngleAxis(hit["yawDegrees"]?.Value<float>() ?? 0f, Vector3.up) * aim;
    private PNStat FirstTarget()
    {
        foreach (var target in context.Targets ?? Array.Empty<PNStat>()) if (target != null && target.IsReady && !target.IsDead) return target;
        return null;
    }
    private void Sweep(int index)
    {
        swept[index] = true;
        var origin = context.Skill.transform.position + Vector3.up * height;
        var shape = shapes[index];
        boxes[index].Open(EveHitboxShape.Sector(shape.Radius, shape.Thickness, shape.Angle, shape.Inner), origin, shape.Radius, 0, (point, outcome) => { },
            target => !target.IsDead && PNWorldGameplay.CanTarget(context.Skill, target));
        boxes[index].ReportTime = age;
        boxes[index].Sample(EveHitboxPose.Planar(origin, hitDirections[index]), age);
    }
    // One body motion at a time, in the order of the list: toward (or away from) the target, spread over [At, At + Seconds]; the end point is clamped by distance and by where
    // the caster should stand. The target is chosen before the first step starts and kept from then on, so a step back is measured from the one that was struck.
    private void Step()
    {
        if (steps == null || cancelOwner == null) return;
        if (stepIndex == 0 && !stepStarted) followed = FirstTarget();
        if (followed != null && followed.IsReady && !followed.IsDead) { focus = followed.transform.position; hasFocus = true; }
        else followed = null;
        // A frame long enough to pass one step goes on with the next in the same call.
        while (stepIndex < steps.Length && age >= steps[stepIndex].At)
        {
            var step = steps[stepIndex];
            if (!stepStarted)
            {
                stepStarted = true;
                var self = context.Skill.transform.position;
                bool known = followed != null || stepIndex > 0 && hasFocus;
                if (!context.Skill.Controls.IsAllowed(step.Permission) || !known && step.Height <= 0f) { CloseStep(); continue; }
                // A jump with no target rises and falls on the spot, facing the aim.
                var toward = known ? focus - self : aim; toward.y = 0; float distance = toward.magnitude;
                // Standing on the target itself leaves only the aim to step back from it.
                if (distance < 1e-4f) { if (!step.Retreat) { CloseStep(); continue; } toward = aim; distance = 0f; }
                float gap = known ? distance - step.StopDistance : 0f;
                stepAmount = gap >= 0f ? Mathf.Min(step.Distance, gap) : step.Retreat ? -Mathf.Min(step.Distance, -gap) : 0f;
                if (Mathf.Abs(stepAmount) < .001f && step.Height <= 0f) { CloseStep(); continue; }
                stepDirection = toward.normalized * (stepAmount < 0f ? -1f : 1f); stepAmount = Mathf.Abs(stepAmount); stepDone = 0f;
                try { motor = new PNBodyMotion(context.Skill.GetComponent<PNMove>(), step.Permission, step.Collision, step.Height > 0f, cancelOwner); }
                catch (InvalidOperationException) { motor = null; CloseStep(); continue; }     // no local motion rights (a replica or a held body): the strike still happens in place
                if (PresentFirstStepEnd && stepIndex == 0 && context.CanPresent)
                {
                    var hits = new RaycastHit[32];
                    var capsule = context.Skill.GetComponent<CapsuleCollider>();
                    float travel = PNBodyMotion.Clip(capsule.gameObject.scene, capsule, context.Skill,
                        stepDirection, stepAmount, step.Collision, PNBodyMotion.TrajectorySkin, ref hits, out _);
                    Vector3 end = motor.Position + stepDirection * travel;
                    context.Present(StepEndFact, end.x, end.y, end.z);
                }
            }
            float fraction = Mathf.Clamp01((age - step.At) / step.Seconds);
            if (motor != null && motor.CanWrite && fraction > stepDone)
            {
                float arc = step.Height > 0f ? step.Height * (4f * fraction * (1f - fraction) - 4f * stepDone * (1f - stepDone)) : 0f;
                motor.Step(stepDirection * (stepAmount * (fraction - stepDone)) + Vector3.up * arc); stepDone = fraction;
            }
            if (fraction >= 1f || motor == null || !motor.CanWrite) CloseStep();
            else break;
        }
    }
    private void CloseStep() { stepIndex++; stepStarted = false; stepAmount = 0f; stepDone = 0f; motor?.Dispose(); motor = null; }
    public override bool AdvanceAuxiliary(float dt)
    {
        if (ended) return true;
        age += dt;
        if (guardPending && age >= guardAt)
        {
            guardPending = false;
            var buffs = context.Skill.GetComponent<PNBuffManager>();
            if (buffs != null && buffs.IsReady) context.ApplyBuff(buffs, guardBuff, caster, new JObject { ["duration"] = (double)guardSeconds });
        }
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
        motor?.Dispose(); motor = null; followed = null;
        movementLock?.Dispose(); movementLock = null;
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
