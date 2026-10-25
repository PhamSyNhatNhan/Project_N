#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Per-projectile spawn data. Position/direction are finite; direction is normalized on construction.
// VI: Dữ liệu sinh từng viên. Vị trí/hướng hữu hạn; hướng được chuẩn hoá.
public sealed class PNProjectileSpawn
{
    public Scene Scene { get; }
    public Vector3 Position { get; }
    public Vector3 Direction { get; }
    // Optional initial target (homing); validated whenever it is used.
    public PNStat Target { get; }
    public PNProjectileSpawn(Scene scene, Vector3 position, Vector3 direction, PNStat target = null)
    {
        if (!Finite(position)) throw new ArgumentException("Finite projectile position required.", nameof(position));
        if (!Finite(direction)) throw new ArgumentException("Finite projectile direction required.", nameof(direction));
        float length = direction.magnitude;
        if (length < 1e-6f) throw new ArgumentException("Projectile direction cannot be zero.", nameof(direction));
        Scene = scene; Position = position; Direction = direction / length; Target = target;
    }
    internal static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
}

public readonly struct PNProjectileHandle
{
    internal readonly PNProjectileManager Manager;
    public long InstanceId { get; }
    internal PNProjectileHandle(PNProjectileManager manager, long id) { Manager = manager; InstanceId = id; }
    public bool IsAlive => Manager != null && Manager.Contains(this);
    public bool End() => Manager != null && Manager.End(this);
}

// Created / Rejected (cap reached with every object active, closing session, unavailable world) / Failed (bad request or cleanup fault).
public sealed class PNProjectileReceipt
{
    public PNProjectileStatus Status { get; internal set; } = PNProjectileStatus.Pending;
    public PNProjectileHandle Handle { get; internal set; }
    public Exception Error { get; internal set; }
}

public sealed class PNProjectileSnapshot
{
    public PNProjectileHandle Handle { get; }
    public EveId Id { get; }
    public EveId DefinitionId { get; }
    public bool IsRunning { get; }
    public Vector3 Position { get; }
    public Vector3 Forward { get; }
    public Vector3 HalfExtents { get; }
    public EveWorldContext World { get; }
    // EN: Layers the projectile targets and the shooter entity captured at launch, and whether gameplay clearing (Destroy) may end it without bypass.
    //     None / default when the launch is gone. An enemy projectile is one that targets the Player layer.
    // VI: Các layer đạn nhắm tới và entity người bắn chụp lúc bắn, và việc xoá do gameplay (Destroy) có kết thúc được nó khi không bypass hay không.
    public PNTargetLayers TargetLayers { get; }
    public EveEntityId SourceId { get; }
    public bool Destroyable { get; }
    internal PNProjectileSnapshot(PNProjectileHandle handle, EveId id, EveId definitionId, PNProjectileInstance instance)
    { Handle = handle; Id = id; DefinitionId = definitionId; IsRunning = instance.IsRunning; Position = instance.Position; Forward = instance.Forward; HalfExtents = instance.HalfExtents; World = instance.World;
      TargetLayers = instance.Launch?.TargetLayers ?? PNTargetLayers.None; SourceId = instance.Launch?.SourceId ?? default; Destroyable = instance.Destroyable; }
}

// A contact found while a projectile moved along one step. Target == null means an environment hit (wall/ground).
public readonly struct PNProjectileContact
{
    public PNStat Target { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    // Position along the step, 0 = where the step began, 1 = where it ended; the earliest contact comes first.
    public float Fraction { get; }
    public PNProjectileContact(PNStat target, Vector3 point, Vector3 normal, float fraction) { Target = target; Point = point; Normal = normal; Fraction = fraction; }
}

public sealed class PNProjectileContacts
{
    internal readonly List<PNProjectileContact> targets = new List<PNProjectileContact>();
    public IReadOnlyList<PNProjectileContact> Targets => targets;
    public bool HasEnvironment { get; internal set; }
    public PNProjectileContact Environment { get; internal set; }
    public bool IsEmpty => targets.Count == 0 && !HasEnvironment;
    internal void Clear() { targets.Clear(); HasEnvironment = false; Environment = default; }
}

// Optional: an entity implements it to say which box other projectiles test against. Default: the root non-trigger collider bounds.
public interface PNIProjectileHitVolume
{
    bool TryGetHitVolume(out Vector3 center, out Vector3 extents);
}

// EN: Target variable held by a projectile: identity + load generation + weak reference, checked at use.
// VI: Biến target trên đạn: định danh + thế hệ + tham chiếu yếu, kiểm khi dùng.
public readonly struct PNProjectileTarget
{
    private readonly WeakReference<PNStat> stat;
    private readonly long generation;
    public EveEntityId Id { get; }
    public bool HasValue => stat != null;
    public PNProjectileTarget(PNStat target)
    {
        var loader = target != null ? target.GetComponent<EveEntityLoader>() : null;
        if (target == null || loader == null || loader.LoadContext == null) { stat = null; generation = 0; Id = default; return; }
        stat = new WeakReference<PNStat>(target); generation = loader.Generation; Id = target.EntityId;
    }
    // Ready, alive, still the same loaded entity generation, active, and in the given world.
    public bool TryGet(EveWorldContext world, out PNStat target)
    {
        target = null;
        if (stat == null || !stat.TryGetTarget(out var candidate) || candidate == null || !candidate.IsReady || !candidate.gameObject.activeInHierarchy) return false;
        var loader = candidate.GetComponent<EveEntityLoader>();
        if (loader == null || !loader.IsReady || loader.Generation != generation || candidate.EntityId != Id) return false;
        if (!ReferenceEquals(candidate.GetComponent<EveEntityIdentity>()?.World, world) || !PNWorldGameplay.CanExecute(candidate)) return false;
        if (PNTargetCandidates.IsDead(candidate)) return false;
        target = candidate; return true;
    }
}

// Outcome of one applied hit: the damage the target received (null when the launch carries none) and the frozen OnHit report (null without a plan).
public sealed class PNProjectileHitResult
{
    public PNStat Target { get; internal set; }
    public PNDamageResult Damage { get; internal set; }
    public PNSkillHitReport Effects { get; internal set; }
    public int HitIndex { get; internal set; }
}
#endif
