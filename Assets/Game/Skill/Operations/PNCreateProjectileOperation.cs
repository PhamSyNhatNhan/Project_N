using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// Payload of create_projectile (every field required unless noted):
//   projectileId: a registered projectile, input: that projectile's launch input (object),
//   count: 1..3 projectiles per volley, spread: degrees between neighbours of a volley (0..90, around the world up axis),
//   targetLayers (optional): ordered layer names the volley can hit IN ADDITION to the caster's own TargetLayers (for example ["Projectile"]); the union must not be empty,
//   aimRadius (optional, > 0): when present the volley aims at the best live target on those layers within that radius (planar), ranked by
//     the shooter's target rule (PNTargetRules: the local player's auto target setting, Nearest for a remote player, Aggro for enemies; the
//     shooter itself is never chosen), and gives it to every projectile as its initial target; with no target, or when absent, the context
//     direction is used. The same rule is captured in the launch for projectiles that search on their own (homing),
//   pitches (optional): one angle in degrees (-80..80) per projectile, in volley order, tilting its direction upward (positive) or downward,
//   damage (optional): {value: PNSkillValues, canCrit (bool), tags: tag IDs including exactly one damage type, iframeDuration (optional)};
//   absent = the projectile deals no damage. The skill's own category/contribution tags and projectile_damage are added at launch.
public sealed class PNCreateProjectileSettings
{
    public EveId ProjectileId;
    public JToken Input;
    public int Count;
    public PNTargetLayers TargetLayers = PNTargetLayers.None;
    public double Spread;
    public double? AimRadius;
    public double[] Pitches;
    public PNSkillValues Values;
    public bool CanCrit;
    public double? IFrameDuration;
    public EveId[] Tags = Array.Empty<EveId>();
    public static PNCreateProjectileSettings Parse(JToken token)
    {
        var obj = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(obj, "projectileId", "input", "count", "spread", "targetLayers", "damage", "aimRadius", "pitches");
        var result = new PNCreateProjectileSettings { ProjectileId = EveId.Parse(PNBuffContentBatch.String(obj, "projectileId")), Input = obj["input"] };
        if (!(obj["input"] is JObject)) throw new FormatException("create_projectile input must be an object.");
        result.Count = PNBuffContentBatch.Integer(obj, "count"); if (result.Count < 1 || result.Count > 3) throw new FormatException("create_projectile count must be 1..3.");
        result.Spread = PNBuffContentBatch.Number(obj, "spread"); if (result.Spread < 0 || result.Spread > 90) throw new FormatException("create_projectile spread must be 0..90 degrees.");
        result.TargetLayers = PNTargetLayers.Parse(obj["targetLayers"], "create_projectile.targetLayers");
        if (obj["aimRadius"] != null)
        {
            result.AimRadius = PNBuffContentBatch.Number(obj, "aimRadius");
            if (!(result.AimRadius > 0) || result.AimRadius > 1000) throw new FormatException("create_projectile aimRadius must be > 0 and at most 1000.");
        }
        if (obj["pitches"] != null)
        {
            var list = PNBuffContentBatch.Array(obj["pitches"]).ToList();
            if (list.Count != result.Count) throw new FormatException("create_projectile pitches needs one angle per projectile.");
            result.Pitches = new double[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Type != JTokenType.Integer && list[i].Type != JTokenType.Float) throw new FormatException("create_projectile pitch must be a number.");
                double pitch = list[i].Value<double>();
                if (double.IsNaN(pitch) || pitch < -80 || pitch > 80) throw new FormatException("create_projectile pitch must be -80..80 degrees.");
                result.Pitches[i] = pitch;
            }
        }
        if (obj["damage"] != null)
        {
            var damage = PNBuffContentBatch.Object(obj["damage"]); PNBuffContentBatch.Fields(damage, "value", "canCrit", "tags", "iframeDuration");
            result.Values = PNSkillValues.Parse(damage["value"]); result.CanCrit = PNBuffContentBatch.Bool(damage, "canCrit"); result.Tags = PNSkillEffect.Ids(damage["tags"]).ToArray(); if (result.Tags.Length == 0) throw new FormatException("create_projectile damage needs tags."); result.IFrameDuration = PNDamage.ParseIFrameDuration(damage);
        }
        return result;
    }
}

// Basic volley of 1-3 projectiles: prepares the damage once for the whole volley, freezes the OnHit effects, and fires. Characters may write
// their own Operation instead; this is only the shared basic form.
public class PNCreateProjectileOperation : PNSkillOperation
{
    private bool executed;
#if UNITY_5_3_OR_NEWER
    // One receipt per projectile of the volley: Created, or Rejected when the projectile cap was reached (a volley may come out short).
    private readonly List<PNProjectileReceipt> results = new List<PNProjectileReceipt>();
    public IReadOnlyList<PNProjectileReceipt> Results => results.AsReadOnly();
#endif
    public static void Validate(JToken token, EveRegistryHub hub)
    {
        var settings = PNCreateProjectileSettings.Parse(token);
        hub.Instances().GetEffectiveKind<PNProjectileDefinition>(settings.ProjectileId).ValidateInput(settings.Input);
        if (settings.Values != null) { settings.Values.ValidateStats(hub); new PNStatSet(hub).ResolveDamageType(settings.Tags); }
    }
    public override void Execute(PNSkillActionContext context, JToken token)
    {
        if (executed || context == null || context.Skill == null || !context.Skill.IsReady || !context.Direction.HasValue) throw new InvalidOperationException("Fresh Ready direction context required.");
        Validate(token, context.Registry); executed = true;
#if UNITY_5_3_OR_NEWER
        // Clients render the action but cannot create authoritative projectiles.
        if (!EveRegistrySession.Context.HasAuthority) return;
        var settings = PNCreateProjectileSettings.Parse(token); var caster = context.Skill.GetComponent<PNStat>();
        if (caster == null || !caster.IsReady) throw new InvalidOperationException("Projectiles need the caster's Ready stats.");
        var source = PNCombatSource.Capture(caster);
        PNPreparedDamage prepared = null;
        if (settings.Values != null)
        {
            // One amount for the whole volley (sum of the components), one prepared template; the crit roll is per hit.
            double amount = 0d; foreach (var component in settings.Values.Components) amount += component.Evaluate(caster);
            var tags = context.EffectiveTags().Concat(settings.Tags).Concat(new[] { PNContentIds.ProjectileDamage }).ToArray();
            prepared = source.Snapshot.PrepareDamage(amount, settings.CanCrit, settings.IFrameDuration, tags);
        }
        var plan = context.CanFreezeOnHit ? context.Skill.Effects.FreezeOnHit(context) : null;
        var launchLayers = source.TargetLayers.Union(settings.TargetLayers);
        if (launchLayers.IsEmpty) throw new InvalidOperationException("create_projectile: the caster targets no layer and the payload adds none.");
        var rule = PNTargetRules.For(caster);
        var launch = PNProjectileLaunch.FromSource(settings.ProjectileId, settings.Input, source, launchLayers, prepared, plan, rule);
        var scene = caster.gameObject.scene;
        var manager = PNProjectileManager.Resolve(EveRegistrySession.Context, scene);
        if (!ReferenceEquals(manager.Session.Registry, context.Registry)) throw new InvalidOperationException("Projectile operation session mismatch.");
        var position = context.Position ?? caster.transform.position;
        var aim = context.Direction.Value; PNStat aimTarget = null;
        if (settings.AimRadius.HasValue)
        {
            aimTarget = PNTargetSelector.Select(manager.Targets.Acquire(context.World), new PNTargetQuery
            {
                Origin = position, Range = (float)settings.AimRadius.Value, LayerMask = launchLayers.Mask, Rule = rule, Exclude = caster.EntityId, Planar = true
            });
            if (aimTarget != null)
            {
                var toTarget = aimTarget.transform.position - position; toTarget.y = 0;
                if (toTarget.sqrMagnitude > 1e-8f) aim = toTarget;
            }
        }
        for (int i = 0; i < settings.Count; i++)
        {
            float angle = (float)((i - (settings.Count - 1) / 2d) * settings.Spread);
            var heading = Quaternion.AngleAxis(angle, Vector3.up) * aim;
            if (settings.Pitches != null && settings.Pitches[i] != 0)
            {
                // Tilt about the horizontal axis perpendicular to the heading: positive pitch raises the nose.
                var side = Vector3.Cross(Vector3.up, heading);
                if (side.sqrMagnitude > 1e-8f) heading = Quaternion.AngleAxis(-(float)settings.Pitches[i], side.normalized) * heading;
            }
            var receipt = manager.Create(launch, new PNProjectileSpawn(scene, position, heading, aimTarget));
            results.Add(receipt);
            if (receipt.Status == PNProjectileStatus.Failed) throw new InvalidOperationException("Projectile creation failed.", receipt.Error);
        }
#else
        // Standalone runner validates invocation/payload only; it has no Unity projectile runtime.
#endif
    }
    public override void Dispose() { executed = true; }
}
