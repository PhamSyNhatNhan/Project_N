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

// Tạo sao: the owner stands still for `cast` seconds, then makes one star (a dependent area, see PNStarforgerStarArea) in the first free slot around it. A stun cancels the cast before the star exists;
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
        var star = (JObject)p["star"]; var input = (JObject)star["input"].DeepClone(); input["slot"] = slot; input["tags"] = PNStarforgerCast.Tags(c, input["tags"]);
        var scene = caster.gameObject.scene; var manager = PNAreaManager.Resolve(EveRegistrySession.Context, scene);
        if (!ReferenceEquals(manager.Session.Registry, c.Registry)) throw new InvalidOperationException("Star session mismatch.");
        var receipt = manager.Create(new PNAreaCreateRequest(PNStarforgerCombatContent.StarArea, input, PNCombatSource.Capture(caster), scene, spot, caster));
        if (receipt.Status == PNAreaStatus.Failed) throw new InvalidOperationException("Star creation failed.", receipt.Error);
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
        telegraph = PNStarforgerTelegraph.Create(caster.gameObject.scene, landing, p["hit"]["radius"].Value<float>(), crouch + hopSeconds);
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
            hitOperation = CreateOperation(cast, PNStarforgerCombatContent.SlamHit); hitOperation.Execute(cast, cast.Data.Action.Perform["hit"]);
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

// The slam's damage: everything of the caster's TargetLayers whose body overlaps the circle (`radius`, within `height` of the landing) takes the damage prepared once from the caster's stats.
public sealed class PNStarforgerSlamHitOperation : PNSkillOperation
{
    private bool executed;
    public override void Execute(PNSkillActionContext c, JToken data)
    {
        if (executed) throw new InvalidOperationException("Slam already spent"); executed = true;
        if (!EveRegistrySession.Context.HasAuthority) return;
        var center = c.Position.Value; float radius = data["radius"].Value<float>(), height = data["height"].Value<float>();
        var caster = c.Skill.GetComponent<PNStat>(); double amount = 0; foreach (var v in PNSkillValues.Parse(data["value"]).Components) amount += v.Evaluate(caster);
        var capture = caster.CaptureCombatSource(); var tags = c.EffectiveTags().Concat(data["tags"].Select(x => EveId.Parse(x.Value<string>()))).Distinct().ToArray();
        var prepared = capture.PrepareDamage(amount, data["canCrit"].Value<bool>(), 0, tags);
        foreach (var entity in c.World.Session.Entities.Snapshot(c.World))
        {
            var target = entity.GetComponent<PNStat>(); if (target == null || !target.IsReady || target.IsDead || !caster.Targets(target) || !PNWorldGameplay.CanTarget(c.Skill, target)) continue;
            var col = target.GetComponent<Collider>(); if (col == null) continue; var b = col.bounds;
            if (b.max.y < center.y - height || b.min.y > center.y + height) continue;
            // the nearest point of the body's box to the centre must be inside the circle
            float dx = Mathf.Max(b.min.x - center.x, 0f, center.x - b.max.x), dz = Mathf.Max(b.min.z - center.z, 0f, center.z - b.max.z);
            if (dx * dx + dz * dz > radius * radius) continue;
            c.ReceiveHit(target, target.transform.position, capture.CreateDamage(prepared), amount, capture);
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
public sealed class PNStarforgerStarArea { }
#endif
