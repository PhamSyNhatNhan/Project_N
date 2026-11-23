using System;
using EveEngine;

// EN: Neutral wire contract of the spawned-object sync (Docs/SpawnedObjectSyncPlan.md section 4.2, stage O2). The authority writes three kinds of record about a projectile,
//     an area or a blast; a client builds a view-only copy from them. Nothing here knows Mirror (Assets/Coop carries the same records in O3) and nothing here carries damage,
//     source stats or a live entity: only what is needed to draw the object the way the authority flies it. Records are immutable and validated when made.
// VI: Hợp đồng truyền trung lập của đồng bộ vật thể sinh ra: authority ghi ba loại bản ghi về đạn / vùng / blast, client dựng bản hình từ chúng. Không biết Mirror, không mang
//     sát thương hay chỉ số nguồn.
public enum PNObjectKind : byte { Projectile = 1, Area = 2, Blast = 3 }

public enum PNObjectRecordType : byte { Spawn = 1, Retarget = 2, End = 3 }

public static class PNObjectSyncLimits
{
    // Launch input of a definition, as JSON text. Longer inputs are refused (the object then is not synced, the authority counts it).
    public const int InputChars = 512;
    public const int CastKeyChars = 64;
    // Live replicas one client keeps; a Spawn above it is dropped and counted.
    public const int MaxLive = 2048;
    // Hard life of a replica whose Spawn carried no LifeHint, and the longest age a Spawn may declare (seconds).
    public const double MaxReplicaSeconds = 120d;
    // How far the host's progress may run ahead of a replica when it is built (seconds); the rest is skipped, not simulated.
    public const double MaxCatchUpSeconds = 2d;
    public const double MaxFactor = 1000d;
    public const float MaxCoordinate = 1e6f;
    // Ended ids a client remembers so a Retarget or a Spawn that arrives after the End is recognised as stale.
    public const int EndedMemory = 512;
    // Smallest time between two Retarget records of one object (seconds of the host clock) and the most Retarget records one Flush sends.
    public const double RetargetInterval = .1d;
    public const int RetargetsPerFlush = 64;
}

// A world as the wire names it: its instance and its incarnation (the same pair a world's identity uses elsewhere).
public readonly struct PNObjectWorld : IEquatable<PNObjectWorld>
{
    public Guid Instance { get; }
    public ulong Incarnation { get; }
    public PNObjectWorld(Guid instance, ulong incarnation) { Instance = instance; Incarnation = incarnation; }
    public bool IsValid => Instance != Guid.Empty;
    public bool Equals(PNObjectWorld other) => Instance == other.Instance && Incarnation == other.Incarnation;
    public override bool Equals(object obj) => obj is PNObjectWorld other && Equals(other);
    public override int GetHashCode() => Instance.GetHashCode() * 31 + Incarnation.GetHashCode();
    public override string ToString() => Instance.ToString("N").Substring(0, 8) + "#" + Incarnation;
    public static PNObjectWorld Of(EveWorldContext world) => world == null ? default : new PNObjectWorld(world.InstanceId, world.Incarnation);
}

public readonly struct PNObjectVector
{
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public PNObjectVector(float x, float y, float z) { X = x; Y = y; Z = z; }
    public bool IsFinite => Ok(X) && Ok(Y) && Ok(Z);
    public float SqrLength => X * X + Y * Y + Z * Z;
    private static bool Ok(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= PNObjectSyncLimits.MaxCoordinate;
    public override string ToString() => "(" + X + ", " + Y + ", " + Z + ")";
}

// One record. `Type` says which fields count:
//   Spawn: Kind, Id, World, DefinitionId, Input, Position, Direction, Target, Owner, Follow, CastKey/CastIndex, Speed/Scale/Lifetime factors, Stamp, Age, LifeHint.
//   Retarget: Kind, Id, World, Target, Position, Direction, Stamp (the position and heading correct the drift once).
//   End: Kind, Id, World, Reason, Position (the impact point), Direction (the impact normal, zero = unknown), Target (entity hit, 0 = none).
// Ids are numbers the authority gives out, unique in one play; entity ids are the host entity ids (0 = none). Stamp is the host's unscaled clock, Age the object's own progress
// in seconds at that stamp (0 for a new object, more for a baseline), LifeHint the seconds the object is expected to live (0 = unknown).
public sealed class PNObjectRecord
{
    public PNObjectRecordType Type { get; private set; }
    public PNObjectKind Kind { get; private set; }
    public long Id { get; private set; }
    public PNObjectWorld World { get; private set; }
    public EveId DefinitionId { get; private set; }
    public string Input { get; private set; }
    public PNObjectVector Position { get; private set; }
    public PNObjectVector Direction { get; private set; }
    public long Target { get; private set; }
    public long Owner { get; private set; }
    public long Follow { get; private set; }
    public string CastKey { get; private set; }
    public int CastIndex { get; private set; }
    public double Speed { get; private set; } = 1d;
    public double Scale { get; private set; } = 1d;
    public double Lifetime { get; private set; } = 1d;
    public double Stamp { get; private set; }
    public float Age { get; private set; }
    public float LifeHint { get; private set; }
    // Layer mask the real object targets (plan 4.4c): a copy that decides its own player's hits needs to know whether the object can hurt that player. 0 = unknown / none.
    public int TargetMask { get; private set; }
    public byte Reason { get; private set; }

    private PNObjectRecord() { }

    public static PNObjectRecord Spawn(PNObjectKind kind, long id, PNObjectWorld world, EveId definition, string input, PNObjectVector position, PNObjectVector direction, long target,
        long owner, long follow, string castKey, int castIndex, double speed, double scale, double lifetime, double stamp, float age, float lifeHint, int targetMask = 0)
    {
        var record = new PNObjectRecord
        {
            Type = PNObjectRecordType.Spawn, Kind = kind, Id = id, World = world, DefinitionId = definition, Input = string.IsNullOrEmpty(input) ? null : input, Position = position, Direction = direction,
            Target = target, Owner = owner, Follow = follow, CastKey = string.IsNullOrEmpty(castKey) ? null : castKey, CastIndex = castIndex, Speed = speed, Scale = scale, Lifetime = lifetime,
            Stamp = stamp, Age = age, LifeHint = lifeHint, TargetMask = targetMask
        };
        record.Require(); return record;
    }
    public static PNObjectRecord Retarget(PNObjectKind kind, long id, PNObjectWorld world, long target, PNObjectVector position, PNObjectVector direction, double stamp)
    {
        var record = new PNObjectRecord { Type = PNObjectRecordType.Retarget, Kind = kind, Id = id, World = world, Target = target, Position = position, Direction = direction, Stamp = stamp };
        record.Require(); return record;
    }
    // Position is where it ended (the impact point when there was one), Direction the surface normal at the impact (zero = unknown), Target the entity it hit (0 = none).
    public static PNObjectRecord End(PNObjectKind kind, long id, PNObjectWorld world, byte reason, PNObjectVector position, PNObjectVector normal = default, long target = 0)
    {
        var record = new PNObjectRecord { Type = PNObjectRecordType.End, Kind = kind, Id = id, World = world, Reason = reason, Position = position, Direction = normal, Target = target };
        record.Require(); return record;
    }

    // The same Spawn with a newer state (the baseline of a late observer): current position, heading, target, stamp and age.
    public PNObjectRecord WithState(PNObjectVector position, PNObjectVector direction, long target, double stamp, float age)
    {
        if (Type != PNObjectRecordType.Spawn) throw new InvalidOperationException("Only a Spawn carries a full state.");
        var copy = (PNObjectRecord)MemberwiseClone(); copy.Position = position; copy.Direction = direction; copy.Target = target; copy.Stamp = stamp; copy.Age = age; copy.Require(); return copy;
    }

    // Rough size on a compact wire, for the packet budget and the measurements of O6.
    public int EstimatedBytes => Type == PNObjectRecordType.Spawn ? 100 + (Input?.Length ?? 0) + (CastKey?.Length ?? 0) + DefinitionId.ToString().Length
        : Type == PNObjectRecordType.Retarget ? 56 : 52;

    public bool TryValidate(out string error)
    {
        try { Require(); error = null; return true; }
        catch (FormatException problem) { error = problem.Message; return false; }
    }

    private void Require()
    {
        if (!Enum.IsDefined(typeof(PNObjectRecordType), Type)) throw new FormatException("Unknown object record type.");
        if (!Enum.IsDefined(typeof(PNObjectKind), Kind)) throw new FormatException("Unknown object kind.");
        if (Id <= 0) throw new FormatException("An object id is a positive number.");
        if (!World.IsValid) throw new FormatException("An object record names its world.");
        if (!Position.IsFinite) throw new FormatException("Object position must be finite and within range.");
        if (!Direction.IsFinite) throw new FormatException("Object direction must be finite and within range.");
        if (Type == PNObjectRecordType.End) return;
        if (Type == PNObjectRecordType.Retarget) { Finite(Stamp, "stamp"); return; }
        if (!DefinitionId.IsValid) throw new FormatException("A spawn record names a definition.");
        if (Direction.SqrLength < 1e-8f) throw new FormatException("A spawn direction cannot be zero.");
        if (Input != null && Input.Length > PNObjectSyncLimits.InputChars) throw new FormatException("Spawn input is longer than " + PNObjectSyncLimits.InputChars + " characters.");
        if (CastKey != null && CastKey.Length > PNObjectSyncLimits.CastKeyChars) throw new FormatException("Cast key is longer than " + PNObjectSyncLimits.CastKeyChars + " characters.");
        if (CastIndex < 0 || CastIndex > 255) throw new FormatException("Cast index must be 0..255.");
        foreach (var factor in new[] { Speed, Scale, Lifetime })
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor < 0d || factor > PNObjectSyncLimits.MaxFactor) throw new FormatException("Object factors must be finite and within 0.." + PNObjectSyncLimits.MaxFactor + ".");
        Finite(Stamp, "stamp");
        if (TargetMask < 0) throw new FormatException("Target mask must not be negative.");
        if (float.IsNaN(Age) || float.IsInfinity(Age) || Age < 0f || Age > PNObjectSyncLimits.MaxReplicaSeconds) throw new FormatException("Object age must be within 0.." + PNObjectSyncLimits.MaxReplicaSeconds + " seconds.");
        if (float.IsNaN(LifeHint) || float.IsInfinity(LifeHint) || LifeHint < 0f || LifeHint > PNObjectSyncLimits.MaxReplicaSeconds) throw new FormatException("Life hint must be within 0.." + PNObjectSyncLimits.MaxReplicaSeconds + " seconds.");
    }
    private static void Finite(double value, string name)
    { if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("Object " + name + " must be finite."); }
}
