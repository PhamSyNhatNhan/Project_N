#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Behavior of a pet summon (Docs/SummonPlan.md S5), driven by the character's `pet` block (PNPetSettings): it hovers near its owner; once `interval` seconds
//     of entity time have passed since its last shot and a target is within `searchRadius`, it stands still for `aimStop` seconds and shoots once. A PNBehaviorSkill runs it on the
//     pet's entity clock (authority only); the shot is the pet's registered skill (Eve2PetAttack, a projectile_action over create_projectile) and its damage is read from the pet's own
//     stats and modifiers, which the pet inherits from its owner when it is made (PNSummonInherit). Target rule: an ordinary entity's (Aggro within its own `combat.targetLayers`,
//     nearest). The pet has no hit volume and no Health: nothing aims at it. Its hover movement is still a Rigidbody velocity (Drive).
// VI: Hành vi của pet: lơ lửng gần chủ; đủ `interval` giây và có mục tiêu trong `searchRadius` thì đứng yên `aimStop` giây rồi bắn; phát bắn là skill đăng ký, damage đọc từ chỉ số
//     của pet (kế thừa từ chủ lúc tạo). Chỉ máy có quyền chạy (qua PNBehaviorSkill).
[DisallowMultipleComponent]
public sealed class PNPetShooterBehavior : PNEnemyBehavior
{
    public enum PetState { Hover, Aim }
    private PNPetSettings settings;
    private PNSummon summon;
    private PNStat stat;
    private PNTargetRule rule;
    private double sinceShot, aimLeft, searchWait;
    private PNStat aimTarget;
    public override IEnumerable<EveId> SkillIds => new[] { PNSummonGameplayContent.Eve2PetAttack };
    public PetState State { get; private set; }
    // Shots fired since this pet was created (for fixtures and the Player review).
    public int Shots { get; private set; }
    public PNPetSettings Settings => settings;

    private Rigidbody body, ownerBody;
    private PNMove move;
    protected override void OnDisable() { try { base.OnDisable(); } finally { Reset(); } }
    private void Reset()
    {
        if (body != null && !body.isKinematic) body.linearVelocity = Vector3.zero;
        if (move != null && move.IsReady) move.SetMoveDirection(Vector2.zero);
        body = null; ownerBody = null; move = null; side = 1f; settings = null; summon = null; stat = null; sinceShot = 0d; aimLeft = 0d; searchWait = 0d; aimTarget = null; State = PetState.Hover; Shots = 0; }

    // First tick of the runner: read the character's block. A pet with no block is a content error.
    public override void Enter()
    {
        summon = GetComponent<PNSummon>(); stat = GetComponent<PNStat>();
        settings = PNPetSettings.Read(EveRegistrySession.Context.Registry.Characters.Get(stat.CharacterId));
        if (settings == null) throw new InvalidOperationException("Character " + stat.CharacterId + " has no pet block.");
        rule = PNTargetRules.For(stat); State = PetState.Hover; sinceShot = 0d; aimLeft = 0d; searchWait = 0d; aimTarget = null; Shots = 0;
    }
    public override void Exit(PNEnemyBehaviorExit reason) { }

    // One step of `delta` seconds of entity time (the runner calls it; a fixture calls PNBehaviorSkill.Advance).
    public override void Tick(float delta)
    {
        double seconds = delta;
        if (seconds <= 0d || settings == null || summon == null || !summon.IsActive || summon.IsRetiring) return;
        if (State == PetState.Hover)
        {
            sinceShot += seconds;
            if (sinceShot < settings.Interval) return;
            searchWait -= seconds; if (searchWait > 0d) return;
            searchWait = .1d; aimTarget = FindTarget();
            if (aimTarget != null) { State = PetState.Aim; aimLeft = settings.AimStop; if (aimLeft > 0d) return; }
            else return;
        }
        aimLeft -= seconds;
        if (aimLeft > 0d) return;
        Fire();
    }

    // Movement is a velocity on the pet's Rigidbody (no collider, no gravity, interpolated), set once per physics step: toward the spot at `followRate` per second
    // of entity time (the spring the old position lerp approximated), and zero while aiming or when the entity clock is stopped. The timers stay in Tick.
    private void FixedUpdate() => Drive(Time.fixedDeltaTime);

    // The pet hovers beside its owner on the side it is already on (the offset's X is a distance, not a direction). It changes side only once it is clearly on the
    // other side (more than SideSwitch metres), so a lagging pet does not flicker between the two.
    public const float SideSwitch = .3f;
    private float side = 1f;
    public static float NextSide(float current, float dx) => current > 0f ? (dx < -SideSwitch ? -1f : 1f) : (dx > SideSwitch ? 1f : -1f);

    public void Drive(float delta)
    {
        if (delta <= 0f || settings == null || summon == null || !summon.IsActive || summon.IsRetiring || !EveRegistrySession.IsReady || !EveRegistrySession.Context.HasAuthority) return;
        body = body != null ? body : GetComponent<Rigidbody>();
        if (body == null || body.isKinematic) return;
        move = move != null ? move : GetComponent<PNMove>();
        // The pet moves through PNMove like every other entity that moves (it is what the move replication carries to the other machines): the planar part is a move direction, the hover
        // height stays a velocity of the body (PNMove leaves it alone and the pet has no gravity). A pet without PNMove (a fixture) keeps the old whole-body velocity.
        bool viaMove = move != null && move.IsReady;
        var owner = summon.OwnerSource?.LoadedSource;
        if (State == PetState.Aim || owner == null) { if (viaMove) move.SetMoveDirection(Vector2.zero); body.linearVelocity = Vector3.zero; return; }
        var scale = GetComponent<EveTimeScale>();
        float factor = scale != null && scale.isActiveAndEnabled ? scale.Scale : 1f;
        ownerBody = ownerBody != null && ownerBody.gameObject == owner.gameObject ? ownerBody : owner.GetComponent<Rigidbody>();
        var from = ownerBody != null ? ownerBody.position : owner.transform.position;
        side = NextSide(side, body.position.x - from.x);
        var spot = from + new Vector3(side * (float)Math.Abs(settings.Offset[0]), (float)settings.Offset[1], (float)settings.Offset[2]);
        var toSpot = spot - body.position;
        if (!viaMove) { body.linearVelocity = toSpot * Mathf.Min((float)settings.FollowRate * factor, 1f / delta); return; }
        // PNMove multiplies its planar velocity by the entity clock itself, so the spring rate here is the unscaled one, held so that one step never overshoots the spot.
        float rate = Mathf.Min((float)settings.FollowRate, 1f / (delta * Mathf.Max(factor, 1e-3f)));
        float speed = move.CurrentMoveSpeed;
        var planar = new Vector2(toSpot.x, toSpot.z) * rate;
        move.SetMoveDirection(speed > 1e-4f ? Vector2.ClampMagnitude(planar / speed, 1f) : Vector2.zero);
        var velocity = body.linearVelocity; body.linearVelocity = new Vector3(velocity.x, toSpot.y * rate * factor, velocity.z);
    }

    private PNStat FindTarget()
    {
        var identity = GetComponent<EveEntityIdentity>();
        var manager = PNProjectileManager.Resolve(EveRegistrySession.Context, gameObject.scene);
        return PNTargetSelector.Select(manager.Targets.Acquire(identity.World), new PNTargetQuery
        {
            Origin = transform.position, Range = (float)settings.SearchRadius, LayerMask = stat.TargetLayers.Mask, Rule = rule, Exclude = stat.EntityId, Planar = true
        });
    }

    private void Fire()
    {
        State = PetState.Hover; var target = aimTarget; aimTarget = null;
        // The target may have died or moved away during the stop: look again, and keep the shot ready when there is none.
        if (target == null || !target.IsReady || target.IsDead || PlanarDistance(target) > settings.SearchRadius + .25d) target = FindTarget();
        if (target == null) { sinceShot = settings.Interval; return; }
        var toTarget = target.transform.position - transform.position; toTarget.y = 0f;
        var heading = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : Vector3.forward;
        if (Context == null || !Context.IsCurrent || !Context.Owner.CanExecuteBehavior) { sinceShot = settings.Interval; return; }
        var admission = Context.TryCast(PNSummonGameplayContent.Eve2PetAttack, heading, PNEnemyActionExecution.Instant, out _, new[] { target }, null, transform.position);
        if (admission != PNEnemyCastAdmission.Started) { sinceShot = settings.Interval; return; }
        Shots++; sinceShot = 0d;
    }

    private double PlanarDistance(PNStat other)
    {
        var d = other.transform.position - transform.position; d.y = 0f; return d.magnitude;
    }
}
#endif
