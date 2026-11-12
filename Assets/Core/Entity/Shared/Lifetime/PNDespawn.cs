using System;
using System.Collections.Generic;
using EveEngine;

// EN: The pure rules of the despawn service (Docs/SandboxPlan.md, step G4): the settings, the per-role override, the hold counter and the one step of the timer. Nothing here knows Unity.
//     The service (Assets/Game/Entity/Lifetime/PNDespawnService.cs) measures the distance to the nearest living player every `Interval` seconds and calls `PNDespawnRule.Step`.
//       - Within `Near` of a living player the timer is reset.      - Beyond `Far` the timer runs; at `Seconds` the entity is removed.
//       - Between `Near` and `Far` the timer is neither reset nor run (a short hysteresis, so an enemy standing at the edge of a player's view is not removed and not saved by a single step).
//       - A held or exempt entity never runs and is reset.           - With no living player in the world nothing runs (a world that nobody has entered yet loses nobody).
// VI: Luật thuần của dịch vụ hết hạn (G4): cấu hình, ghi đè theo vai trò, bộ đếm giữ và một bước của đồng hồ. Trong tầm A thì đặt lại, ngoài B thì đếm, giữa A và B thì giữ nguyên.
public static class PNDespawnLimits
{
    public const float MaxDistance = 1000f, MaxSeconds = 3600f, MinInterval = .1f, MaxInterval = 5f, MaxLeash = 1000f;
}

public sealed class PNDespawnSettings
{
    public static readonly PNDespawnSettings Default = new PNDespawnSettings(25f, 35f, 10f, .5f);
    public float Near { get; }
    public float Far { get; }
    public float Seconds { get; }
    public float Interval { get; }

    public PNDespawnSettings(float near, float far, float seconds, float interval)
    {
        Finite(near, 0f, PNDespawnLimits.MaxDistance, "near"); Finite(far, 0f, PNDespawnLimits.MaxDistance, "far");
        Finite(seconds, 0f, PNDespawnLimits.MaxSeconds, "seconds"); Finite(interval, PNDespawnLimits.MinInterval, PNDespawnLimits.MaxInterval, "interval");
        if (near <= 0f || seconds <= 0f) throw new FormatException("despawn near and seconds must be positive.");
        if (far < near) throw new FormatException("despawn far must not be smaller than near.");
        Near = near; Far = far; Seconds = seconds; Interval = interval;
    }

    internal static void Finite(float value, float min, float max, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max) throw new FormatException("despawn " + name + " must be a number in [" + min + ", " + max + "].");
    }

    // The world's settings with a role's override on top (a field the override does not name keeps the world's value).
    public PNDespawnSettings With(PNDespawnOverride over)
        => over == null ? this : new PNDespawnSettings(over.Near ?? Near, over.Far ?? Far, over.Seconds ?? Seconds, Interval);
}

// The `despawn` object of an entity role's data (all optional): exempt keeps the entity out of the service, near/far/seconds replace the world's values, leash is the radius around the home it may stray (0 = no home).
public sealed class PNDespawnOverride
{
    public bool Exempt { get; }
    public float? Near { get; }
    public float? Far { get; }
    public float? Seconds { get; }
    public float Leash { get; }

    public PNDespawnOverride(bool exempt, float? near, float? far, float? seconds, float leash)
    {
        if (near.HasValue) PNDespawnSettings.Finite(near.Value, 0f, PNDespawnLimits.MaxDistance, "near");
        if (far.HasValue) PNDespawnSettings.Finite(far.Value, 0f, PNDespawnLimits.MaxDistance, "far");
        if (seconds.HasValue) PNDespawnSettings.Finite(seconds.Value, 0f, PNDespawnLimits.MaxSeconds, "seconds");
        PNDespawnSettings.Finite(leash, 0f, PNDespawnLimits.MaxLeash, "leash");
        if (near.HasValue && near.Value <= 0f || seconds.HasValue && seconds.Value <= 0f) throw new FormatException("despawn near and seconds must be positive.");
        if (near.HasValue && far.HasValue && far.Value < near.Value) throw new FormatException("despawn far must not be smaller than near.");
        Exempt = exempt; Near = near; Far = far; Seconds = seconds; Leash = leash;
    }
}

public enum PNDespawnVerdict { Keep, Running, Expire }

public struct PNDespawnState
{
    public float Timer;
    public bool Running;
}

public static class PNDespawnRule
{
    // `distance` is the flat distance to the nearest living player (NaN or infinity when there is none).
    public static PNDespawnVerdict Step(ref PNDespawnState state, float distance, bool exempt, bool held, float delta, PNDespawnSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0f) throw new ArgumentOutOfRangeException(nameof(delta));
        if (exempt || held) { state = default(PNDespawnState); return PNDespawnVerdict.Keep; }
        if (float.IsNaN(distance) || float.IsInfinity(distance)) return state.Running ? PNDespawnVerdict.Running : PNDespawnVerdict.Keep;   // nobody to measure against: paused
        if (distance <= settings.Near) { state = default(PNDespawnState); return PNDespawnVerdict.Keep; }
        if (distance <= settings.Far) return state.Running ? PNDespawnVerdict.Running : PNDespawnVerdict.Keep;
        state.Running = true; state.Timer += delta;
        return state.Timer >= settings.Seconds ? PNDespawnVerdict.Expire : PNDespawnVerdict.Running;
    }
}

// EN: The holds on one entity: `Acquire(owner)` returns a handle, the entity is held while at least one handle lives. A handle ends when it is disposed or when its owner is gone (`isGone` says so; the
//     Unity side passes "a destroyed UnityEngine.Object"). Counting only: what a held entity may do is the lifetime component's business.
// VI: Các "giữ" trên một thực thể. Còn giữ khi còn ít nhất một handle; handle hết khi bị Dispose hoặc chủ đã mất.
public sealed class PNHoldSet
{
    public const int MaxHolds = 64;
    private readonly List<Hold> holds = new List<Hold>();
    private readonly Func<object, bool> isGone;

    public PNHoldSet(Func<object, bool> isGone = null) { this.isGone = isGone ?? (owner => false); }

    public PNHold Acquire(object owner)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        Prune();
        if (holds.Count >= MaxHolds) throw new InvalidOperationException("An entity has at most " + MaxHolds + " lifetime holds.");
        var hold = new Hold(owner); holds.Add(hold);
        return new PNHold(this, hold);
    }

    public int Count { get { Prune(); return holds.Count; } }
    public bool IsHeld => Count > 0;

    private void Prune() { for (int i = holds.Count - 1; i >= 0; i--) if (holds[i].Released || isGone(holds[i].Owner)) holds.RemoveAt(i); }
    internal void Release(Hold hold) { hold.Released = true; holds.Remove(hold); }
    public void Clear() { foreach (var hold in holds) hold.Released = true; holds.Clear(); }

    internal sealed class Hold { public readonly object Owner; public bool Released; public Hold(object owner) { Owner = owner; } }
}

public sealed class PNHold : IDisposable
{
    private readonly PNHoldSet set;
    private readonly PNHoldSet.Hold hold;
    internal PNHold(PNHoldSet set, PNHoldSet.Hold hold) { this.set = set; this.hold = hold; }
    public bool IsAlive => !hold.Released;
    public void Dispose() { if (!hold.Released) set.Release(hold); }
}

public static class PNDespawnIds
{
    // The world feature that gives a world the despawn service (the vocabulary lives in Core so the content module can register it).
    public static readonly EveId Feature = EveId.Parse("project_n:despawn");
}
