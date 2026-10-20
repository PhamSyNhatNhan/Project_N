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
    public PNBuffSource Source { get; }
    // Damage computed once for the volley (null when the projectile deals none). The crit roll happens per hit.
    public PNPreparedDamage Damage { get; }
    // Frozen OnHit effects (null when it carries none).
    public PNSkillHitSnapshot HitPlan { get; }
    // Layers the projectile can target / hit; required, so a player projectile never hits players and vice versa.
    public int TargetMask { get; }
    // Launch-time factors from the projectile_* modifiers (1 = unchanged).
    public double SpeedFactor { get; }
    public double ScaleFactor { get; }
    public double LifetimeFactor { get; }
    public PNProjectileLaunch(EveId projectileId, JToken input, PNBuffSource source, int targetMask, PNPreparedDamage damage = null, PNSkillHitSnapshot hitPlan = null,
        double speedFactor = 1d, double scaleFactor = 1d, double lifetimeFactor = 1d)
    {
        if (!projectileId.IsValid) throw new ArgumentException("Projectile ID required.", nameof(projectileId));
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (targetMask == 0) throw new ArgumentException("A target layer mask is required.", nameof(targetMask));
        foreach (var factor in new[] { speedFactor, scaleFactor, lifetimeFactor })
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor < 0d) throw new ArgumentOutOfRangeException(nameof(speedFactor), "Modifier factors must be finite and nonnegative.");
        if (damage != null && (damage.SessionId != source.SessionId || damage.SourceId != source.EntityId)) throw new ArgumentException("Prepared damage belongs to another source.", nameof(damage));
        ProjectileId = projectileId; this.input = input?.DeepClone(); Source = source; TargetMask = targetMask; Damage = damage; HitPlan = hitPlan;
        SpeedFactor = speedFactor; ScaleFactor = scaleFactor; LifetimeFactor = lifetimeFactor;
    }
    // Reads the four projectile_* modifiers from the source snapshot (Scale route over a base of 1: +20% = 1.2).
    public static PNProjectileLaunch FromSource(EveId projectileId, JToken input, PNBuffSource source, int targetMask, PNPreparedDamage damage = null, PNSkillHitSnapshot hitPlan = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var snapshot = source.Snapshot ?? throw new ArgumentException("Source has no combat snapshot.", nameof(source));
        return new PNProjectileLaunch(projectileId, input, source, targetMask, damage, hitPlan, snapshot.Calculate(1d, PNContentIds.ProjectileSpeed),
            snapshot.Calculate(1d, PNContentIds.ProjectileScale), snapshot.Calculate(1d, PNContentIds.ProjectileLifetime));
    }
}
