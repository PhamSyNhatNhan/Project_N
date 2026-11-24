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
    // Null on a replica (a view-only copy built from a sync record: no source, no damage, no OnHit plan).
    public PNCombatSource Source { get; }
    public bool IsReplica => Source == null;
    private readonly EveEntityId replicaOwner;
    // Entity ID of the shooter captured with the source; it survives the shooter. A replica carries the owner id of its record.
    public EveEntityId SourceId => Source != null ? Source.EntityId : replicaOwner;
    // Damage computed once for the volley (null when the projectile deals none). The crit roll happens per hit.
    public PNPreparedDamage Damage { get; }
    // Frozen OnHit effects (null when it carries none).
    public PNSkillHitSnapshot HitPlan { get; }
    // Layers the projectile can target / hit (the shooter's TargetLayers at launch plus the volley's own); required and not empty.
    public PNTargetLayers TargetLayers { get; private set; }
    // Launch-time factors from the projectile_* modifiers (1 = unchanged).
    public double SpeedFactor { get; private set; }
    public double ScaleFactor { get; private set; }
    public double LifetimeFactor { get; private set; }
    // How the projectile picks a target on its own (homing): the shooter's rule at launch (the local player's Settings mode, Nearest for a
    // remote player, Aggro for enemies and system sources). default = Autotarget / Nearest.
    public PNTargetRule TargetRule { get; }
    // EN: The cast that made this volley (0 = none) and how the shooter's machine takes part (spawned-object sync O4, plan 4.4a). A prediction is the shooter's own guess of the volley: it deals no damage,
    //     reports the hits it sees as Dealt hit reports, and is matched with the authority's real projectile by (CastKey, index). ShooterDecides marks the authority's real projectile of a shooter who predicts:
    //     it finds no target contacts itself (walls it still finds) and takes the shooter's reported hits instead.
    // VI: Cast tao ra loat dan (0 = khong) va cach may nguoi ban tham gia (O4, plan 4.4a). Vien doan khong gay sat thuong, bao trung len, ghep voi vien that theo (CastKey, so thu tu); ShooterDecides: vien that cua nguoi ban co doan chi nhan cu trung nguoi ban bao.
    public long CastKey { get; private set; }
    public bool IsPrediction { get; private set; }
    public bool ShooterDecides { get; private set; }
    // The launch of the shooter's guess: same source and layers, never damage or an OnHit plan (the caller builds it without them). Its hits go out through the manager's hit-report sink (PNHitReport), not through the launch.
    public PNProjectileLaunch AsPrediction(long castKey)
    {
        if (castKey <= 0) throw new ArgumentOutOfRangeException(nameof(castKey));
        if (Damage != null || HitPlan != null) throw new InvalidOperationException("A prediction carries no damage or OnHit plan.");
        var copy = (PNProjectileLaunch)MemberwiseClone(); copy.CastKey = castKey; copy.IsPrediction = true; return copy;
    }
    // The launch of a real cast, named by its key (records carry it so the shooter's machine can match its guess); `shooterDecides` when that shooter's machine predicts.
    // The same launch with the factors the authority really used (the shooter's guess is corrected to them when its record arrives).
    internal PNProjectileLaunch WithFactors(double speed, double scale, double lifetime)
    { var copy = (PNProjectileLaunch)MemberwiseClone(); copy.SpeedFactor = speed; copy.ScaleFactor = scale; copy.LifetimeFactor = lifetime; return copy; }
    public PNProjectileLaunch WithCast(long castKey, bool shooterDecides)
    {
        var copy = (PNProjectileLaunch)MemberwiseClone(); copy.CastKey = castKey; copy.ShooterDecides = shooterDecides && castKey > 0; return copy;
    }
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
    private PNProjectileLaunch(EveId projectileId, JToken input, EveEntityId owner, double speedFactor, double scaleFactor, double lifetimeFactor)
    {
        if (!projectileId.IsValid) throw new ArgumentException("Projectile ID required.", nameof(projectileId));
        foreach (var factor in new[] { speedFactor, scaleFactor, lifetimeFactor })
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor < 0d) throw new ArgumentOutOfRangeException(nameof(speedFactor), "Modifier factors must be finite and nonnegative.");
        ProjectileId = projectileId; this.input = input?.DeepClone(); TargetLayers = PNTargetLayers.None; replicaOwner = owner;
        SpeedFactor = speedFactor; ScaleFactor = scaleFactor; LifetimeFactor = lifetimeFactor;
    }
    // EN: The launch of a view-only copy (spawned-object sync): the factors the authority computed, no source, damage or OnHit plan. It hits nothing and ends only when told to.
    // VI: Launch của bản hình (đồng bộ vật thể): hệ số do authority tính, không có nguồn, sát thương hay OnHit.
    public static PNProjectileLaunch Replica(EveId projectileId, JToken input, EveEntityId owner, double speedFactor, double scaleFactor, double lifetimeFactor)
        => new PNProjectileLaunch(projectileId, input, owner, speedFactor, scaleFactor, lifetimeFactor);
    // A copy that knows which layers the real object targets (the Spawn record's mask): only its own player's hit is decided here (plan 4.4c); it still deals nothing.
    public PNProjectileLaunch WithReplicaTargets(int mask)
    {
        if (!IsReplica) throw new InvalidOperationException("Only a copy takes the record's targets.");
        var copy = (PNProjectileLaunch)MemberwiseClone(); copy.TargetLayers = PNTargetLayers.FromMask(mask); return copy;
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
