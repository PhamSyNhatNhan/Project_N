#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Behavior of a pet summon (Docs/SummonPlan.md S5), driven by the character's `pet` block (PNPetSettings): it hovers near its owner; once `interval` seconds
//     of entity time have passed since its last shot and a target is within `searchRadius`, it stands still for `aimStop` seconds and fires one projectile.
//     The shot uses the owner's combat source captured when the pet was summoned (so the owner's whole modifier set counts, once, at that moment), plus the
//     projectile_damage and summon_damage tags. Only the machine with authority runs it. The pet has no hit volume and no Health: nothing aims at it.
// VI: Hành vi của pet: lơ lửng gần chủ; đủ `interval` giây và có mục tiêu trong `searchRadius` thì đứng yên `aimStop` giây rồi bắn một viên, dùng nguồn chiến đấu
//     của chủ chụp lúc triệu hồi. Chỉ máy có quyền chạy.
[DisallowMultipleComponent]
public sealed class PNPetShooterBehavior : MonoBehaviour
{
    public enum PetState { Hover, Aim }
    private PNPetSettings settings;
    private PNSummon summon;
    private PNStat stat;
    private double sinceShot, aimLeft, searchWait;
    private PNStat aimTarget;
    public PetState State { get; private set; }
    // Shots fired since this pet was created, and the last receipt (for fixtures and the Player review).
    public int Shots { get; private set; }
    public PNProjectileReceipt LastShot { get; private set; }
    public PNPetSettings Settings => settings;

    private Rigidbody body, ownerBody;
    private void OnDisable() => Reset();
    private void Reset()
    {
        if (body != null && !body.isKinematic) body.linearVelocity = Vector3.zero;
        body = null; ownerBody = null; side = 1f; settings = null; summon = null; stat = null; sinceShot = 0d; aimLeft = 0d; searchWait = 0d; aimTarget = null; State = PetState.Hover; Shots = 0; LastShot = null; }

    private void Update()
    {
        var scale = GetComponent<EveTimeScale>();
        float factor = scale != null && scale.isActiveAndEnabled ? scale.Scale : 1f;
        if (factor > 0f) Tick(Time.deltaTime * factor);
    }

    // One step of `seconds` of entity time (Update calls it; a fixture calls it by hand).
    public void Tick(double seconds)
    {
        if (seconds <= 0d || !EveRegistrySession.IsReady || !EveRegistrySession.Context.HasAuthority) return;
        var loader = GetComponent<EveEntityLoader>();
        if (loader == null || !loader.IsReady || loader.LoadContext?.CanSimulate != true) return;
        summon = summon ?? GetComponent<PNSummon>();
        if (summon == null || !summon.IsActive || summon.IsRetiring) return;
        stat = stat ?? GetComponent<PNStat>();
        if (settings == null)
        {
            settings = PNPetSettings.Read(EveRegistrySession.Context.Registry.Characters.Get(stat.CharacterId));
            if (settings == null) throw new InvalidOperationException("Character " + stat.CharacterId + " has no pet block.");
        }
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
        var owner = summon.OwnerSource?.LoadedSource;
        if (State == PetState.Aim || owner == null) { body.linearVelocity = Vector3.zero; return; }
        var scale = GetComponent<EveTimeScale>();
        float factor = scale != null && scale.isActiveAndEnabled ? scale.Scale : 1f;
        ownerBody = ownerBody != null && ownerBody.gameObject == owner.gameObject ? ownerBody : owner.GetComponent<Rigidbody>();
        var from = ownerBody != null ? ownerBody.position : owner.transform.position;
        side = NextSide(side, body.position.x - from.x);
        var spot = from + new Vector3(side * (float)Math.Abs(settings.Offset[0]), (float)settings.Offset[1], (float)settings.Offset[2]);
        float rate = Mathf.Min((float)settings.FollowRate * factor, 1f / delta);
        body.linearVelocity = (spot - body.position) * rate;
    }

    private PNStat FindTarget()
    {
        var identity = GetComponent<EveEntityIdentity>();
        var manager = PNProjectileManager.Resolve(EveRegistrySession.Context, gameObject.scene);
        return PNTargetSelector.Select(manager.Targets.Acquire(identity.World), new PNTargetQuery
        {
            Origin = transform.position, Range = (float)settings.SearchRadius, LayerMask = summon.OwnerSource.TargetLayers.Mask, Rule = PNTargetRule.Aggro, Exclude = stat.EntityId, Planar = true
        });
    }

    private void Fire()
    {
        State = PetState.Hover; var target = aimTarget; aimTarget = null;
        // The target may have died or moved away during the stop: look again, and keep the shot ready when there is none.
        if (target == null || !target.IsReady || target.IsDead || PlanarDistance(target) > settings.SearchRadius + .25d) target = FindTarget();
        if (target == null) { sinceShot = settings.Interval; return; }
        var source = summon.OwnerSource;
        var tags = settings.DamageTags.Concat(new[] { PNContentIds.ProjectileDamage, PNContentIds.SummonDamage }).ToArray();
        var prepared = source.Snapshot.PrepareDamage(settings.DamageAmount, settings.CanCrit, null, tags);
        var launch = PNProjectileLaunch.FromSource(settings.ProjectileId, new JObject(), source, source.TargetLayers, prepared, null, PNTargetRule.Aggro);
        var toTarget = target.transform.position - transform.position; toTarget.y = 0f;
        var heading = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : Vector3.forward;
        var manager = PNProjectileManager.Resolve(EveRegistrySession.Context, gameObject.scene);
        LastShot = manager.Create(launch, new PNProjectileSpawn(gameObject.scene, transform.position, heading, target));
        if (LastShot.Status == PNProjectileStatus.Failed) throw new InvalidOperationException("Pet projectile creation failed.", LastShot.Error);
        Shots++; sinceShot = 0d;
    }

    private double PlanarDistance(PNStat other)
    {
        var d = other.transform.position - transform.position; d.y = 0f; return d.magnitude;
    }
}
#endif
