using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;

public enum PNProjectileStatus { Pending, Created, Rejected, Failed }
public enum PNProjectileEndReason { Requested, Expired, Environment, Hit, Destroyed, OwnerUnavailable, Disabled, SceneUnloaded, SessionEnded, RuntimeError }

// EN: Everything captured when a volley is fired, shared by all its projectiles and immutable afterwards. It keeps no live
//     skill, entity or invocation, so a projectile outlives its source.
// VI: Mọi thứ chụp lúc bắn, dùng chung cho cả loạt và bất biến. Không giữ skill/entity/invocation sống.
public sealed class PNProjectileLaunch
{
    private readonly JToken input;
    public EveId ProjectileId { get; }
    public JToken Input => input?.DeepClone();
    public PNCombatSource Source { get; }
    // Entity ID of the shooter captured with the source; it survives the shooter.
    public EveEntityId SourceId => Source.EntityId;
    // Damage computed once for the volley (null when the projectile deals none). The crit roll happens per hit.
    public PNPreparedDamage Damage { get; }
    // Frozen OnHit effects (null when it carries none).
    public PNSkillHitSnapshot HitPlan { get; }
    // Layers the projectile can target / hit (the shooter's TargetLayers at launch plus the volley's own); required and not empty.
    public PNTargetLayers TargetLayers { get; }
    // Launch-time factors from the projectile_* modifiers (1 = unchanged).
    public double SpeedFactor { get; }
    public double ScaleFactor { get; }
    public double LifetimeFactor { get; }
    // How the projectile picks a target on its own (homing): the shooter's rule at launch (the local player's Settings mode, Nearest for a
    // remote player, Aggro for enemies and system sources). default = Autotarget / Nearest.
    public PNTargetRule TargetRule { get; }
    public PNProjectileLaunch(EveId projectileId, JToken input, PNCombatSource source, PNTargetLayers targetLayers, PNPreparedDamage damage = null, PNSkillHitSnapshot hitPlan = null,
        double speedFactor = 1d, double scaleFactor = 1d, double lifetimeFactor = 1d, PNTargetRule targetRule = default)
    {
        if (!projectileId.IsValid) throw new ArgumentException("Projectile ID required.", nameof(projectileId));
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (targetLayers == null || targetLayers.IsEmpty) throw new ArgumentException("Target layers are required.", nameof(targetLayers));
        foreach (var factor in new[] { speedFactor, scaleFactor, lifetimeFactor })
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor < 0d) throw new ArgumentOutOfRangeException(nameof(speedFactor), "Modifier factors must be finite and nonnegative.");
        if (damage != null && (damage.SessionId != source.SessionId || damage.SourceId != source.EntityId)) throw new ArgumentException("Prepared damage belongs to another source.", nameof(damage));
        ProjectileId = projectileId; this.input = input?.DeepClone(); Source = source; TargetLayers = targetLayers; Damage = damage; HitPlan = hitPlan;
        SpeedFactor = speedFactor; ScaleFactor = scaleFactor; LifetimeFactor = lifetimeFactor; TargetRule = targetRule;
    }
    // Reads the four projectile_* modifiers from the source snapshot (Scale route over a base of 1: +20% = 1.2).
    public static PNProjectileLaunch FromSource(EveId projectileId, JToken input, PNCombatSource source, PNTargetLayers targetLayers, PNPreparedDamage damage = null, PNSkillHitSnapshot hitPlan = null,
        PNTargetRule targetRule = default)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var snapshot = source.Snapshot ?? throw new ArgumentException("Source has no combat snapshot.", nameof(source));
        return new PNProjectileLaunch(projectileId, input, source, targetLayers, damage, hitPlan, snapshot.Calculate(1d, PNContentIds.ProjectileSpeed),
            snapshot.Calculate(1d, PNContentIds.ProjectileScale), snapshot.Calculate(1d, PNContentIds.ProjectileLifetime), targetRule);
    }
}
