using System;
using System.Collections.Generic;

// Camera tuning as data passed in through the API (never registry content, so it does not enter the compatibility hash).
// Zoom is expressed as a scale relative to the rig's own neutral value: 1 = as authored, above 1 = further away / wider, below 1 = closer.
// Layers: game default -> mode -> world -> player; a later layer overrides an earlier one field by field and the result is always
// inside hard limits.
public sealed class PNCameraProfile
{
    public const float HardZoomMinimum = 0.1f, HardZoomMaximum = 10f, HardSmoothMaximum = 100f, HardTimeMaximum = 30f;

    public float DistanceScaleMin { get; }
    public float DistanceScaleMax { get; }
    public float LensScaleMin { get; }
    public float LensScaleMax { get; }
    public float BaseScale { get; }
    public float SmoothSpeed { get; }
    public float ShakeGain { get; }
    public int MaximumRequests { get; }
    public float LockOnMinimumDistance { get; }
    public float LockOnMaximumDistance { get; }
    public float LockOnFramingSize { get; }
    public float LockOnDamping { get; }
    public float LockOnBlendSeconds { get; }

    // Distance 3.0..7.0 around the authored 4.5 -> 0.667..1.556; lens (FOV 30..50 around 38) -> 0.79..1.32; smoothing 8 per second.
    public static readonly PNCameraProfile Default = new PNCameraProfile(3f / 4.5f, 7f / 4.5f, 30f / 38f, 50f / 38f, 1f, 8f, 1f, 32, 1f, 12f, .8f, 1f, .25f);

    public PNCameraProfile(float distanceScaleMin, float distanceScaleMax, float lensScaleMin, float lensScaleMax, float baseScale, float smoothSpeed, float shakeGain,
        int maximumRequests, float lockOnMinimumDistance, float lockOnMaximumDistance, float lockOnFramingSize, float lockOnDamping, float lockOnBlendSeconds)
    {
        Range(distanceScaleMin, distanceScaleMax, nameof(distanceScaleMin)); Range(lensScaleMin, lensScaleMax, nameof(lensScaleMin));
        Finite(baseScale, HardZoomMinimum, HardZoomMaximum, nameof(baseScale));
        Finite(smoothSpeed, .01f, HardSmoothMaximum, nameof(smoothSpeed)); Finite(shakeGain, 0, 2, nameof(shakeGain));
        if (maximumRequests < 1 || maximumRequests > 256) throw new ArgumentOutOfRangeException(nameof(maximumRequests));
        Finite(lockOnMinimumDistance, 0, 1000, nameof(lockOnMinimumDistance)); Finite(lockOnMaximumDistance, 0, 1000, nameof(lockOnMaximumDistance));
        if (lockOnMinimumDistance > lockOnMaximumDistance) throw new ArgumentException("Lock-on minimum distance exceeds maximum.");
        Finite(lockOnFramingSize, .05f, 1.5f, nameof(lockOnFramingSize)); Finite(lockOnDamping, 0, 20, nameof(lockOnDamping)); Finite(lockOnBlendSeconds, 0, HardTimeMaximum, nameof(lockOnBlendSeconds));
        DistanceScaleMin = distanceScaleMin; DistanceScaleMax = distanceScaleMax; LensScaleMin = lensScaleMin; LensScaleMax = lensScaleMax; BaseScale = baseScale;
        SmoothSpeed = smoothSpeed; ShakeGain = shakeGain; MaximumRequests = maximumRequests; LockOnMinimumDistance = lockOnMinimumDistance; LockOnMaximumDistance = lockOnMaximumDistance;
        LockOnFramingSize = lockOnFramingSize; LockOnDamping = lockOnDamping; LockOnBlendSeconds = lockOnBlendSeconds;
        // The authored value (scale 1) and the base must be inside the allowed range so the zoom never starts out of bounds.
        if (baseScale < distanceScaleMin || baseScale > distanceScaleMax || baseScale < lensScaleMin || baseScale > lensScaleMax) throw new ArgumentException("Base zoom must lie inside both zoom ranges.");
    }

    private static void Range(float min, float max, string name)
    {
        Finite(min, HardZoomMinimum, HardZoomMaximum, name); Finite(max, HardZoomMinimum, HardZoomMaximum, name);
        if (min > max) throw new ArgumentException("Zoom minimum exceeds maximum: " + name);
    }
    private static void Finite(float value, float min, float max, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max) throw new ArgumentOutOfRangeException(name);
    }

    public float ClampDistanceScale(float scale) => Clamp(scale, DistanceScaleMin, DistanceScaleMax);
    public float ClampLensScale(float scale) => Clamp(scale, LensScaleMin, LensScaleMax);
    private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;

    public PNCameraProfile With(PNCameraProfileLayer layer) => layer == null ? this : new PNCameraProfile(
        layer.DistanceScaleMin ?? DistanceScaleMin, layer.DistanceScaleMax ?? DistanceScaleMax, layer.LensScaleMin ?? LensScaleMin, layer.LensScaleMax ?? LensScaleMax,
        layer.BaseScale ?? BaseScale, layer.SmoothSpeed ?? SmoothSpeed, layer.ShakeGain ?? ShakeGain, layer.MaximumRequests ?? MaximumRequests,
        layer.LockOnMinimumDistance ?? LockOnMinimumDistance, layer.LockOnMaximumDistance ?? LockOnMaximumDistance, layer.LockOnFramingSize ?? LockOnFramingSize,
        layer.LockOnDamping ?? LockOnDamping, layer.LockOnBlendSeconds ?? LockOnBlendSeconds);
}

// Optional overrides of one source (mode, world or player). Unset fields keep the earlier layer.
public sealed class PNCameraProfileLayer
{
    public float? DistanceScaleMin, DistanceScaleMax, LensScaleMin, LensScaleMax, BaseScale, SmoothSpeed, ShakeGain, LockOnMinimumDistance, LockOnMaximumDistance, LockOnFramingSize, LockOnDamping, LockOnBlendSeconds;
    public int? MaximumRequests;
}

public enum PNCameraProfileSource { Game = 0, Mode = 1, World = 2, Player = 3 }

// The layered profile. Resolve applies Game -> Mode -> World -> Player and throws if the result would leave the hard limits, leaving the
// previous profile in effect (a bad layer is refused, never partially applied).
public sealed class PNCameraProfileStack
{
    private readonly PNCameraProfileLayer[] layers = new PNCameraProfileLayer[4];
    public PNCameraProfile Current { get; private set; } = PNCameraProfile.Default;
    public void Set(PNCameraProfileSource source, PNCameraProfileLayer layer)
    {
        int index = (int)source;
        if (index < 0 || index > 3) throw new ArgumentOutOfRangeException(nameof(source));
        var previous = layers[index]; layers[index] = layer;
        try { Current = Resolve(); }
        catch { layers[index] = previous; throw; }
    }
    public void Clear(PNCameraProfileSource source) { layers[(int)source] = null; Current = Resolve(); }
    private PNCameraProfile Resolve()
    {
        var profile = PNCameraProfile.Default;
        foreach (var layer in layers) profile = profile.With(layer);
        return profile;
    }
}
