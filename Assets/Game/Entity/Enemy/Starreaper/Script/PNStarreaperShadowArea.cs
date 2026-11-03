#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

// Tàn Ảnh's shadow (registered `project_n:starreaper_shadow`, schema PNStarreaperCombatContent.ValidateShadowInput). A copy of the Starreaper drawn as a rig puppet under a violet overlay, standing
// where the retreat began. It has no health and no collider. `delay` seconds after it appeared it locks the direction toward the best target within `aimRadius` and shoots an arrow; `gap` seconds
// later it shoots a second one turned `side` * `sideAngle` degrees away from the first, and vanishes. With no target at the first shot it vanishes without shooting.
// It is independent of its owner: it keeps what it needs from the creation request (the combat snapshot, the prepared damage, the target rule), so the owner's death or a Lorica break change
// nothing. The damage is prepared once at creation from the owner's stats, like a projectile's.
[DefaultExecutionOrder(200)]
public sealed class PNStarreaperShadowArea : PNAreaInstance
{
    private static readonly Color Violet = new Color(.50f, .20f, 1.30f);
    private PNCombatSource source; private PNPreparedDamage prepared; private PNTargetRule rule; private EveEntityId ownerId; private PNProjectileManager projectiles;
    private float delay, gap, sideAngle, aimRadius, age; private int side; private bool locked, secondShot;
    private Vector3 position, direction;
    private PNStarreaperEchoVisual echo;private float retirementAge;
    public override float RetirementDuration => PNStarreaperEchoVisual.DissolveSeconds;

    protected override void Initialize(PNAreaCreateRequest context)
    {
        var input = (JObject)context.Input; var owner = context.Owner ?? throw new ArgumentException("The shadow needs its owner's stats.");
        delay = input["delay"].Value<float>(); gap = input["gap"].Value<float>(); sideAngle = input["sideAngle"].Value<float>(); aimRadius = input["aimRadius"].Value<float>(); side = input["side"].Value<int>();
        source = context.Source; ownerId = owner.EntityId; rule = PNTargetRules.For(owner); position = context.Position;
        double amount = 0d; foreach (var component in PNSkillValues.Parse(input["value"]).Components) amount += component.Evaluate(owner);
        var tags = PNSkillEffect.Ids(input["tags"]).Concat(new[] { PNContentIds.ProjectileDamage }).Distinct().ToArray();
        prepared = (source.Snapshot ?? throw new InvalidOperationException("The shadow needs a combat snapshot.")).PrepareDamage(amount, input["canCrit"].Value<bool>(), null, tags);
        projectiles = PNProjectileManager.Resolve(Manager.Session, gameObject.scene);
        age = 0; locked = secondShot = false; direction = Vector3.zero;
        echo?.Dispose();echo=new PNStarreaperEchoVisual(transform,Manager.Session.Registry,EveId.Parse(input["appearance"].Value<string>()));
        var ownerActor=owner.GetComponent<PNVisualBody>()?.Actor;
        echo.Begin(ownerActor!=null&&ownerActor.FacingLeft,delay,gap);retirementAge=0;
    }
    public void RenderPreview(Camera camera){echo?.Render(camera);}
    public override void Advance(float gameDelta)
    {
        if (!IsRunning) return;
        age += gameDelta;echo?.Advance(age,gameDelta);
        if (!locked && age >= delay)
        {
            locked = true;
            var target = PNTargetSelector.Select(projectiles.Targets.Acquire(World), new PNTargetQuery
            { Origin = position, Range = aimRadius, LayerMask = source.TargetLayers.Mask, Rule = rule, Exclude = ownerId, Planar = true });
            var toward = target != null ? target.transform.position - position : Vector3.zero; toward.y = 0;
            if (toward.sqrMagnitude < 1e-8f) { Finish(PNAreaEndReason.Expired); return; }                // nobody to shoot at: the shadow just fades away
            direction = toward.normalized;
            
            Fire(direction);
        }
        if (locked && !secondShot && age >= delay + gap)
        {
            secondShot = true;
            var turned = PNStarreaperPlanner.Rotate(direction.x, direction.z, side * sideAngle);
            Fire(new Vector3(turned.X, 0, turned.Z));
            Finish(PNAreaEndReason.Expired);
        }
    }
    private void Fire(Vector3 heading)
    {
        echo?.Shot(heading);
        var launch = PNProjectileLaunch.FromSource(PNStarreaperCombatContent.EchoArrow, new JObject { ["echoVisual"]=true }, source, source.TargetLayers, prepared, null, rule);
        var receipt = projectiles.Create(launch, new PNProjectileSpawn(gameObject.scene, position + Vector3.up * .2f, heading));
        if (receipt.Status == PNProjectileStatus.Failed) throw new InvalidOperationException("Shadow arrow creation failed.", receipt.Error);
    }
    protected override void Cleanup()
    {
        retirementAge=0;
        if(EndReason==PNAreaEndReason.Expired)echo?.Dissolve();else echo?.Stop();
    }
    public override void AdvanceRetirement(float gameDelta){retirementAge+=gameDelta;echo?.Advance(age+retirementAge,gameDelta);}
    public override void OnPoolRelease(){echo?.Stop();source=null;prepared=null;projectiles=null;base.OnPoolRelease();}
    public override void DisposePooledResources(){echo?.Dispose();echo=null;}
}
#endif
