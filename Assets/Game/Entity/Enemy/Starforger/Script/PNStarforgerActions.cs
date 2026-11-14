#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// What the Starforger Actions share: where the owner looks, which tags a spawned damage carries, and which Buffs are control.
internal static class PNStarforgerCast
{
    public static PNStat Target(PNSkillActionContext c) { if (c.Targets == null) return null; foreach (var t in c.Targets) if (t != null && t.IsReady && !t.IsDead) return t; return null; }
    public static Vector3 Planar(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.zero; }
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
    public static JArray Tags(PNSkillActionContext c, JToken registered) => new JArray(c.EffectiveTags().Concat(PNSkillEffect.Ids(registered)).Select(x => x.ToString()).Distinct());
    // Every registered Buff that locks control permissions (a stun: the Lorica break and every profile of the control template). Taken from the payloads, so a control Buff added later is covered.
    public static EveId[] ControlBuffIds(EveRegistryHub hub)
    {
        var ids = new List<EveId>();
        foreach (var pair in hub.Buffs().Snapshot()) if (pair.Value.Payload is JObject o && o["permissionIds"] != null) ids.Add(pair.Key);
        return ids.ToArray();
    }
}

// Tạo sao: the owner stands still for `cast` seconds, then makes one star (a dependent summon, see PNStarforgerStarSummon) in the first free slot around it. A stun cancels the cast before the star exists;
// a star that already exists is not touched. With every slot taken nothing is made (the behavior does not cast then).
public sealed class PNStarforgerStarAction : PNSkillAction
{
    private PNSkillActionContext context; private float age, cast, recovery; private bool made, ended;
    private PNStarforgerPresentation presentation; private PNVisualBody body; private PNStat target;
    public override bool HasAuxiliaryTimeline => true;
    public override void Start(PNSkillActionContext c)
    {
        if (context != null || ended) throw new InvalidOperationException("Fresh cast required");
        PNStarforgerCombatContent.ValidateStar(c.Data.Action, c.Registry);
        context = c; var p = c.Data.Action.Perform; cast = p["cast"].Value<float>(); recovery = p["recovery"].Value<float>();
        c.BeginAction();
        presentation = c.Skill.GetComponent<PNStarforgerPresentation>(); body = c.Skill.GetComponent<PNVisualBody>();
        target = PNStarforgerCast.Target(c); PNStarforgerCast.Face(c, body, PNStarforgerCast.Aim(c, target));
        body?.PlayAction("cast_star", cast + recovery);body?.Actor?.SetPhase(0); presentation?.BeginStar(cast,recovery);
    }
    public override void Perform(PNSkillActionContext c) { }
    public override bool AdvanceAuxiliary(float dt)
    {
        if (ended) return true;
        age += dt; presentation?.SetAge(age);body?.Actor?.SetPhase(Mathf.Clamp01(age/(cast+recovery)));
        if (!made && age >= cast) { made = true; Make(); }
        return age >= cast + recovery;
    }
    private void Make()
    {
        var c = context; var p = c.Data.Action.Perform; var caster = c.Skill.GetComponent<PNStat>(); var skill = c.Skill.GetComponent<PNStarforgerSkill>();
        int max = p["maxStars"].Value<int>(); int slot = PNStarforgerPlanner.FirstFreeSlot(skill != null ? skill.UsedSlots(max) : new bool[max], max);
        if (slot < 0) return;                                      // all slots taken meanwhile: nothing is made
        var offset = PNStarforgerPlanner.SlotOffset(slot, p["orbit"].Value<float>(), p["slotDegrees"].Value<float>());
        var origin = caster.transform.position; var delta = new Vector3(offset.X, 0, offset.Z); float length = delta.magnitude;
        // a wall between the owner and the slot shortens the offset so the star never hangs inside it
        var hits = new RaycastHit[8]; int n = caster.gameObject.scene.GetPhysicsScene().Raycast(origin + Vector3.up * .2f, delta / length, hits, length, LayerMask.GetMask("Wall"), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) length = Mathf.Min(length, Mathf.Max(0f, hits[i].distance - .15f));
        var spot = origin + delta.normalized * length;
        // The star is a summon of the caster in this slot's group (one per slot; a second one for a taken slot is refused), hung at the spot as an offset from the caster.
        var summon = (JObject)p["summon"].DeepClone(); summon["group"] = PNStarforgerCombatContent.SlotGroup(slot); summon["limit"] = 1; summon["whenFull"] = "reject";
        var shift = spot - origin; summon["offset"] = new JArray(shift.x, shift.y, shift.z);
        using (var create = CreateOperation(c, PNStarforgerCombatContent.CreateSummon)) create.Execute(c, summon);
    }
    public override void Exit(PNSkillActionContext c) { try { c.CompleteAction(); } finally { Dispose(); } }
    public override void Cancel(PNSkillActionContext c) { try { c.CancelEffects(); } finally { Dispose(); } }      // a star already made belongs to the manager and stays
    public override void Dispose()
    {
        if (ended) return; ended = true;
        presentation?.EndPose(); presentation = null; body?.EndAction(); body = null; target = null; context = null;
    }
}

// Nhảy và nện: the owner crouches `crouch` seconds on the spot (the landing ring is shown from the first moment, the stun still cancels this part), then jumps along the registered Hop to the landing
// the behavior chose, invulnerable and immune to control for exactly the flight (the hit hook blocks every damage, a lease refuses every control Buff), every star shoots one bolt toward the landing at
// take-off, and on landing the registered slam Operation hits everything of the owner's target layers in the radius. Then it rests `recovery`. Every way out releases the invulnerability and the lease.
public sealed class PNStarforgerSlamAction : PNAgentTrajectoryAction
{
    private PNSkillActionContext cast; private PNStat caster; private PNStarforgerSkill skill; private PNStarforgerPresentation presentation; private PNVisualBody body;
    private PNStarforgerTelegraph telegraph; private PNSkillOperation hitOperation;
    private PNHitCapture hitCapture; private PNPreparedDamage hitPrepared; private double hitAmount; private float hitRadius, hitHeight;
    private float crouch, recovery, hopSeconds, age, recoverAge; private bool flying, landed, ended; private Vector3 landing;
    private Action<PNDamageGate> block; private PNBuffImmunityLease lease;
    protected override JToken MotionPayload(PNSkillActionContext value) => value.Data.Action.Perform["hop"];
    protected override float MotionDistance(float configured, Vector3 origin, Vector3 anchor) { var d = anchor - origin; d.y = 0; return Mathf.Min(configured, d.magnitude); }
    public override void Start(PNSkillActionContext c)
    {
        if (ended) throw new InvalidOperationException("Fresh cast required");
        PNStarforgerCombatContent.ValidateSlam(c.Data.Action, c.Registry);
        if (!c.Position.HasValue || !c.Direction.HasValue) throw new InvalidOperationException("Landing and direction required");
        base.Start(c);                                           // reads the hop schema, locks movement, checks the floor and the navigation along the jump
        cast = c; landing = c.Position.Value; var p = c.Data.Action.Perform;
        crouch = p["crouch"].Value<float>(); recovery = p["recovery"].Value<float>(); hopSeconds = p["hop"]["duration"].Value<float>();
        caster = c.Skill.GetComponent<PNStat>(); skill = c.Skill.GetComponent<PNStarforgerSkill>();
        presentation = c.Skill.GetComponent<PNStarforgerPresentation>(); body = c.Skill.GetComponent<PNVisualBody>();
        PNStarforgerCast.Face(c, body, PNStarforgerCast.Planar(c.Direction.Value));
        body?.PlayAction("crouch", crouch);body?.Actor?.SetPhase(0); presentation?.BeginSlam(crouch, hopSeconds, recovery);
        // The telegraph is the first committed shape: scale and damage are captured now (melee_scale), and the hit at the landing uses the same numbers.
        if (EveRegistrySession.Context.HasAuthority) hitCapture = PNStarforgerSlamHitOperation.Prepare(c, p["hit"], out hitPrepared, out hitAmount, out hitRadius, out hitHeight);
        else hitRadius = p["hit"]["radius"].Value<float>();
        telegraph = PNStarforgerTelegraph.Create(caster.gameObject.scene, landing, hitRadius, crouch + hopSeconds);
    }
    public override bool AdvanceAuxiliary(float dt)
    {
        if (ended) return true;
        if (!landed)
        {
            if (Outcome != PNMotionOutcome.Running && !flying) return true;          // the floor check failed at Start: no jump, the cast ends
            age += dt; presentation?.SetAge(age); telegraph?.SetTime(age);
            if (age < crouch) { body?.Actor?.SetPhase(Mathf.Clamp01(age/crouch));return false; }
            if (!flying) BeginFlight();
            body?.Actor?.SetPhase(Mathf.Clamp01((age-crouch)/hopSeconds));
            if (!base.AdvanceAuxiliary(dt)) return false;
            Land();
            return recovery <= 0f;
        }
        recoverAge += dt; presentation?.SetAge(age + recoverAge); telegraph?.SetTime(age+recoverAge);body?.Actor?.SetPhase(Mathf.Clamp01(recoverAge/recovery));
        return recoverAge >= recovery;
    }
    private void BeginFlight()
    {
        flying = true;
        block = gate => gate.Block(); if (caster != null) caster.BeforeDamage += block;
        var buffs = cast.Skill.GetComponent<PNBuffManager>();
        if (buffs != null && buffs.IsReady) { var ids = PNStarforgerCast.ControlBuffIds(cast.Registry); if (ids.Length > 0) lease = buffs.Buffs.Immune(ids, null, false); }
        skill?.FireStarsToward(landing);
        body?.PlayAction("air", hopSeconds + .1f);body?.Actor?.SetPhase(0);
    }
    private void EndFlight()
    {
        if (block != null) { if (caster != null) caster.BeforeDamage -= block; block = null; }
        if (lease != null) { lease.Dispose(); lease = null; }
    }
    private void Land()
    {
        landed = true; EndFlight();
        presentation?.Slam(); telegraph?.Impact();
        if (Outcome == PNMotionOutcome.Completed || Outcome == PNMotionOutcome.Blocked)
        {
            body?.PlayAction("slam", recovery);body?.Actor?.SetPhase(0);
            hitOperation = CreateOperation(cast, PNStarforgerCombatContent.SlamHit); if (hitOperation is PNStarforgerSlamHitOperation slam) slam.Bind(hitCapture, hitPrepared, hitAmount, hitRadius, hitHeight); hitOperation.Execute(cast, cast.Data.Action.Perform["hit"]);
        }
    }
    public override void Dispose()
    {
        if (!ended)
        {
            ended = true; EndFlight();
            telegraph?.Dispose(); telegraph = null; presentation?.EndPose(); presentation = null; body?.EndAction(); body = null; hitOperation?.Dispose(); hitOperation = null; cast = null; caster = null; skill = null;
        }
        base.Dispose();
    }
}

// The slam's damage: everything of the caster's TargetLayers whose body touches the cylinder (`radius` x melee_scale, within `height` of the landing) takes the damage prepared once from the caster's stats.
// The cast is captured by the Action when the telegraph appears (PNHitCapture, D6); the Operation only fires it at the landing.
public sealed class PNStarforgerSlamHitOperation : PNSkillOperation
{
    private bool executed;
    public PNHitCapture Capture { get; private set; } private PNPreparedDamage prepared; private double amount; private float radius, height;
    // Reads the registered `hit` payload and captures once: scale from melee_scale, damage prepared with the attack's tags (the Action's effective tags, the payload tags and melee_damage).
    public static PNHitCapture Prepare(PNSkillActionContext c, JToken data, out PNPreparedDamage prepared, out double amount, out float radius, out float height)
    {
        var caster = c.Skill.GetComponent<PNStat>(); amount = 0; foreach (var v in PNSkillValues.Parse(data["value"]).Components) amount += v.Evaluate(caster);
        var tags = c.EffectiveTags().Concat(data["tags"].Select(x => EveId.Parse(x.Value<string>())));
        var capture = PNHitCapture.Melee(caster.CaptureCombatSource(), tags);
        radius = capture.Scale(data["radius"].Value<float>()); height = capture.Scale(data["height"].Value<float>());
        prepared = capture.Prepare(amount, data["canCrit"].Value<bool>(), 0);
        return capture;
    }
    public void Bind(PNHitCapture capture, PNPreparedDamage prepared, double amount, float radius, float height) { Capture = capture; this.prepared = prepared; this.amount = amount; this.radius = radius; this.height = height; }
    public override void Execute(PNSkillActionContext c, JToken data)
    {
        if (executed) throw new InvalidOperationException("Slam already spent"); executed = true;
        if (!EveRegistrySession.Context.HasAuthority) return;
        if (Capture == null) throw new InvalidOperationException("Slam needs the Action's capture.");
        var caster = c.Skill.GetComponent<PNStat>(); var center = c.Position.Value;
        var targets = new List<PNStat>();
        using (var cast = new PNHitboxCast(EveHitboxShape.Cylinder(radius, height * 2f), caster.gameObject.scene.GetPhysicsScene()))
        {
            cast.Query(EveHitboxPose.Planar(center, Vector3.forward), 0d, targets, caster, ~0, QueryTriggerInteraction.Ignore,
                target => target != null && target.IsReady && !target.IsDead && caster.Targets(target) && PNWorldGameplay.CanTarget(c.Skill, target));
            foreach (var target in targets.ToArray())
            {
                if (!target.IsReady || target.IsDead) continue;
                cast.CommitHit(target, 0d);
                c.ReceiveHit(target, target.transform.position, Capture.Damage(prepared), amount, Capture.Source);
            }
        }
    }
}
#else
// Standalone validates registered schemas; Unity-only rendering/physics execute in native checks.
public sealed class PNStarforgerSlamAction : PNSkillAction
{
    public override void Start(PNSkillActionContext c) { PNStarforgerCombatContent.ValidateSlam(c.Data.Action, c.Registry); }
    public override void Perform(PNSkillActionContext c) { }
    public override void Exit(PNSkillActionContext c) { }
    public override void Cancel(PNSkillActionContext c) { }
}
public sealed class PNStarforgerStarAction : PNSkillAction
{
    public override void Start(PNSkillActionContext c) { PNStarforgerCombatContent.ValidateStar(c.Data.Action, c.Registry); }
    public override void Perform(PNSkillActionContext c) { }
    public override void Exit(PNSkillActionContext c) { }
    public override void Cancel(PNSkillActionContext c) { }
}
public sealed class PNStarforgerSlamHitOperation : PNSkillOperation
{ public override void Execute(PNSkillActionContext c, Newtonsoft.Json.Linq.JToken p) { PNStarforgerCombatContent.ValidateSlamHit(p, c.Registry); } }
public sealed class PNStarforgerBoltProjectile { }
#endif
