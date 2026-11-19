#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Tàn Ảnh's shadow as a summon (Docs/StrikeBlastPlan.md U1; the character `project_n:starreaper_shadow`, its numbers in the character's `shadow` block, schema PNStarreaperCombatContent.ValidateShadowInput).
// A copy of the Starreaper drawn as a rig puppet under a violet overlay, standing where the retreat began. It has no health and no collider. `delay` seconds after it appeared it locks the
// direction toward the best target within `aimRadius` and shoots an arrow; `gap` seconds later it shoots a second one turned `side` * `sideAngle` degrees away from the first, then dissolves and
// removes itself. With no target at the first shot it dissolves without shooting.
// It is an entity of its own: a PNBehaviorSkill runs this behavior on the shadow's entity clock (a Buff that slows or stops the entity moves the whole shadow) and only the authority runs it. Each arrow is
// the shadow's registered skill `starreaper_echo_shot` (a projectile_action over create_projectile); its damage is read from the shadow's own attack power, which it inherits from the Starreaper when it
// is made (PNSummonInherit). Its target rule is an ordinary entity's: Aggro within its own target layers, nearest.
// It is INDEPENDENT of its owner (KeepWhenOwnerGone is true): the owner's death or a Lorica break change nothing.
[DisallowMultipleComponent]
public sealed class PNStarreaperShadowSummon : PNEnemyBehavior, IPNSummonOwnerGone
{
    private PNSummon summon; private PNStat stat;
    private PNTargetRule rule; private EveEntityId ownerId; private PNProjectileManager projectiles; private PNCombatSource source;
    private float delay, gap, sideAngle, aimRadius, age, dissolveAge; private int side; private bool started, locked, secondShot, dissolving;
    private Vector3 position, direction;
    private PNStarreaperEchoVisual echo;
    public bool IsRunning => started && summon != null && summon.IsActive && !summon.IsRetiring;
    public bool Started => started;
    public bool Locked => locked;
    public bool IsDissolving => dissolving;
    public float Age => age;
    public int Shots { get; private set; }
    public override IEnumerable<EveId> SkillIds => new[] { PNStarreaperCombatContent.EchoShot };
    public bool KeepWhenOwnerGone(PNSummonOwnerGone reason) => true;
    public void Release() => Reset();                            // what OnDisable / OnDestroy do; an Edit Mode fixture calls it itself (Edit Mode runs neither)
    protected override void OnDisable() { try { base.OnDisable(); } finally { Reset(); } }
    private void OnDestroy() => Reset();
    private void Reset()
    {
        echo?.Dispose(); echo = null; summon = null; stat = null; rule = default; projectiles = null; source = null;
        started = locked = secondShot = dissolving = false; age = dissolveAge = 0; Shots = 0; direction = Vector3.zero;
    }
    public void RenderPreview(Camera camera) { echo?.Render(camera); }

    // One step of `seconds` of entity time (the runner calls it; a fixture calls PNBehaviorSkill.Advance).
    public override void Tick(float seconds)
    {
        if (!(seconds > 0f) || !started || summon == null || !summon.IsActive || summon.IsRetiring) return;
        if (dissolving)
        {
            dissolveAge += seconds; echo?.Advance(age + dissolveAge, seconds);
            if (dissolveAge >= PNStarreaperEchoVisual.DissolveSeconds) summon.Retire();
            return;
        }
        age += seconds; echo?.Advance(age, seconds);
        if (!locked && age >= delay)
        {
            locked = true;
            var target = PNTargetSelector.Select(projectiles.Targets.Acquire(GetComponent<EveEntityIdentity>().World), new PNTargetQuery
            { Origin = position, Range = aimRadius, LayerMask = stat.TargetLayers.Mask, Rule = rule, Exclude = ownerId, Planar = true });
            var toward = target != null ? target.transform.position - position : Vector3.zero; toward.y = 0;
            if (toward.sqrMagnitude < 1e-8f) { Dissolve(); return; }                 // nobody to shoot at: the shadow just fades away
            direction = toward.normalized; Fire(direction);
        }
        if (locked && !secondShot && age >= delay + gap)
        {
            secondShot = true;
            var turned = PNStarreaperPlanner.Rotate(direction.x, direction.z, side * sideAngle);
            Fire(new Vector3(turned.X, 0, turned.Z)); Dissolve();
        }
    }
    // First tick of the runner: read the character's block and the owner's source. No owner source at all (unloaded before the shadow existed): nothing to shoot for, retire.
    public override void Enter()
    {
        summon = GetComponent<PNSummon>(); stat = GetComponent<PNStat>(); source = summon != null ? summon.OwnerSource : null; var owner = source?.LoadedSource;
        if (summon == null || !summon.IsActive || summon.IsRetiring || owner == null || stat == null) { summon?.Retire(); return; }
        var registry = EveRegistrySession.Context.Registry;
        var block = (registry.Characters.Get(stat.CharacterId).Data as JObject)?["shadow"] ?? throw new InvalidOperationException("Character " + stat.CharacterId + " has no shadow block.");
        PNStarreaperCombatContent.ValidateShadowInput(block); var o = (JObject)block;
        delay = o["delay"].Value<float>(); gap = o["gap"].Value<float>(); sideAngle = o["sideAngle"].Value<float>(); aimRadius = o["aimRadius"].Value<float>();
        side = owner.GetComponent<PNStarreaperSkill>()?.TakeShadowSide() ?? 1;
        ownerId = owner.EntityId; rule = PNTargetRules.For(stat); position = transform.position; age = 0; Shots = 0;
        projectiles = PNProjectileManager.Resolve(EveRegistrySession.Context, gameObject.scene);
        echo?.Dispose(); echo = new PNStarreaperEchoVisual(transform, registry, EveId.Parse(o["appearance"].Value<string>()));
        var ownerActor = owner.GetComponent<PNVisualBody>()?.Actor;
        echo.Begin(ownerActor != null && ownerActor.FacingLeft, delay, gap);
        started = true;
    }
    public override void Exit(PNEnemyBehaviorExit reason) { }
    private void Dissolve() { dissolving = true; dissolveAge = 0; echo?.Dissolve(); }
    // The arrow is the shadow's registered skill: the runner admits it (authority only, the shadow's own control rights) and the registered Action creates the projectile.
    private void Fire(Vector3 heading)
    {
        if (Context == null || !Context.IsCurrent || !Context.Owner.CanExecuteBehavior) return;
        var admission = Context.TryCast(PNStarreaperCombatContent.EchoShot, heading, PNEnemyActionExecution.Instant, out _, null, null, position + Vector3.up * .2f);
        if (admission != PNEnemyCastAdmission.Started) return;
        echo?.Shot(heading); Shots++;
    }
}
#else
public sealed class PNStarreaperShadowSummon { }
#endif
