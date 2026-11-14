#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Tàn Ảnh's shadow as a summon (Docs/StrikeBlastPlan.md U1; the character `project_n:starreaper_shadow`, its numbers in the character's `shadow` block, schema PNStarreaperCombatContent.ValidateShadowInput).
// A copy of the Starreaper drawn as a rig puppet under a violet overlay, standing where the retreat began. It has no health and no collider. `delay` seconds after it appeared it locks the
// direction toward the best target within `aimRadius` and shoots an arrow; `gap` seconds later it shoots a second one turned `side` * `sideAngle` degrees away from the first, then dissolves and
// removes itself. With no target at the first shot it dissolves without shooting.
// It is INDEPENDENT of its owner (KeepWhenOwnerGone is true): it uses the owner's combat source captured when it was summoned, so the owner's death or a Lorica break change nothing. Its
// timers run on the summon's own entity clock, so a Buff that slows or stops the entity moves the whole shadow. Only the machine with authority runs it.
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class PNStarreaperShadowSummon : MonoBehaviour, IPNSummonOwnerGone
{
    private PNSummon summon; private PNStat stat;
    private PNPreparedDamage prepared; private PNTargetRule rule; private EveEntityId ownerId; private PNProjectileManager projectiles; private PNCombatSource source;
    private float delay, gap, sideAngle, aimRadius, age, dissolveAge; private int side; private bool started, locked, secondShot, dissolving;
    private Vector3 position, direction;
    private PNStarreaperEchoVisual echo;
    public bool IsRunning => started && summon != null && summon.IsActive && !summon.IsRetiring;
    public bool Started => started;
    public bool Locked => locked;
    public bool IsDissolving => dissolving;
    public float Age => age;
    public int Shots { get; private set; }
    public bool KeepWhenOwnerGone(PNSummonOwnerGone reason) => true;
    public void Release() => Reset();                            // what OnDisable / OnDestroy do; an Edit Mode fixture calls it itself (Edit Mode runs neither)
    private void OnDisable() => Reset();
    private void OnDestroy() => Reset();
    private void Reset()
    {
        echo?.Dispose(); echo = null; summon = null; stat = null; prepared = null; rule = default; projectiles = null; source = null;
        started = locked = secondShot = dissolving = false; age = dissolveAge = 0; Shots = 0; direction = Vector3.zero;
    }
    public void RenderPreview(Camera camera) { echo?.Render(camera); }

    private void Update()
    {
        var scale = GetComponent<EveTimeScale>(); float factor = scale != null && scale.isActiveAndEnabled ? scale.Scale : 1f;
        if (factor > 0f) Tick(Time.deltaTime * factor);
    }
    // One step of `seconds` of entity time (Update calls it; a fixture calls it by hand).
    public void Tick(float seconds)
    {
        if (!(seconds > 0f) || !EveRegistrySession.IsReady || !EveRegistrySession.Context.HasAuthority) return;
        var loader = GetComponent<EveEntityLoader>(); if (loader == null || !loader.IsReady || loader.LoadContext?.CanSimulate != true) return;
        summon = summon ?? GetComponent<PNSummon>(); if (summon == null || !summon.IsActive || summon.IsRetiring) return;
        if (!started && !Begin()) return;
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
            { Origin = position, Range = aimRadius, LayerMask = source.TargetLayers.Mask, Rule = rule, Exclude = ownerId, Planar = true });
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
    // First tick: read the character's block, the owner's source and prepare the damage once. No owner at all (unloaded before the shadow existed): nothing to shoot for, retire.
    private bool Begin()
    {
        stat = stat ?? GetComponent<PNStat>(); source = summon.OwnerSource; var owner = source?.LoadedSource;
        if (owner == null || stat == null) { summon.Retire(); return false; }
        var registry = EveRegistrySession.Context.Registry;
        var block = (registry.Characters.Get(stat.CharacterId).Data as JObject)?["shadow"] ?? throw new InvalidOperationException("Character " + stat.CharacterId + " has no shadow block.");
        PNStarreaperCombatContent.ValidateShadowInput(block); var o = (JObject)block;
        delay = o["delay"].Value<float>(); gap = o["gap"].Value<float>(); sideAngle = o["sideAngle"].Value<float>(); aimRadius = o["aimRadius"].Value<float>();
        side = owner.GetComponent<PNStarreaperSkill>()?.TakeShadowSide() ?? 1;
        ownerId = owner.EntityId; rule = PNTargetRules.For(owner); position = transform.position;
        double amount = 0d; foreach (var component in PNSkillValues.Parse(o["value"]).Components) amount += component.Evaluate(owner);
        var tags = PNSkillEffect.Ids(o["tags"]).Concat(new[] { PNContentIds.ProjectileDamage }).Distinct().ToArray();
        prepared = (source.Snapshot ?? throw new InvalidOperationException("The shadow needs a combat snapshot.")).PrepareDamage(amount, o["canCrit"].Value<bool>(), null, tags);
        projectiles = PNProjectileManager.Resolve(EveRegistrySession.Context, gameObject.scene);
        echo?.Dispose(); echo = new PNStarreaperEchoVisual(transform, registry, EveId.Parse(o["appearance"].Value<string>()));
        var ownerActor = owner.GetComponent<PNVisualBody>()?.Actor;
        echo.Begin(ownerActor != null && ownerActor.FacingLeft, delay, gap);
        started = true; return true;
    }
    private void Dissolve() { dissolving = true; dissolveAge = 0; echo?.Dissolve(); }
    private void Fire(Vector3 heading)
    {
        echo?.Shot(heading);
        var launch = PNProjectileLaunch.FromSource(PNStarreaperCombatContent.EchoArrow, new JObject { ["echoVisual"] = true }, source, source.TargetLayers, prepared, null, rule);
        var receipt = projectiles.Create(launch, new PNProjectileSpawn(gameObject.scene, position + Vector3.up * .2f, heading));
        if (receipt.Status == PNProjectileStatus.Failed) throw new InvalidOperationException("Shadow arrow creation failed.", receipt.Error);
        Shots++;
    }
}
#else
public sealed class PNStarreaperShadowSummon { }
#endif
