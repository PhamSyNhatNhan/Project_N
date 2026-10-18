using System;
using System.Collections.Generic;
using System.Linq;

public enum PNZoomQuantity { Distance, Lens }

public enum PNZoomUnit { Scale = 0, Distance = 1, FieldOfView = 2, OrthographicSize = 3 }

// One zoom request. Scale > 1 zooms out (further / wider), < 1 zooms in. Either a factor applied on top of the player base, an absolute
// scale (override), or an absolute value in real units (camera distance, field of view in degrees, orthographic size) that the director
// converts with the live rig's authored value. Duration 0 means until released; Blend is the ease-in/out time in seconds shaped by Ease.
// Clock chooses the camera clock (stops at time scale 0) or real time.
public readonly struct PNZoomRequest
{
    public const float HardMinimumScale = PNCameraProfile.HardZoomMinimum, HardMaximumScale = PNCameraProfile.HardZoomMaximum;
    // The number: a scale for PNZoomUnit.Scale, otherwise a value in the unit.
    public readonly float Scale;
    public readonly bool Absolute;
    public readonly float Duration, Blend;
    public readonly int Priority;
    public readonly PNZoomUnit Unit;
    public readonly PNCameraEase Ease;
    public readonly PNCameraClock Clock;
    public PNZoomRequest(float scale, bool absolute = false, float duration = 0f, float blend = .15f, int priority = 0, PNZoomUnit unit = PNZoomUnit.Scale, PNCameraEase ease = PNCameraEase.Linear, PNCameraClock clock = PNCameraClock.Camera)
    {
        if (!Enum.IsDefined(typeof(PNZoomUnit), unit)) throw new ArgumentOutOfRangeException(nameof(unit));
        float min = unit == PNZoomUnit.Scale ? HardMinimumScale : unit == PNZoomUnit.Distance ? .5f : unit == PNZoomUnit.FieldOfView ? 1f : .1f;
        float max = unit == PNZoomUnit.Scale ? HardMaximumScale : unit == PNZoomUnit.Distance ? 1000f : unit == PNZoomUnit.FieldOfView ? 170f : 1000f;
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale < min || scale > max) throw new ArgumentOutOfRangeException(nameof(scale));
        if (float.IsNaN(duration) || duration < 0 || duration > PNCameraProfile.HardTimeMaximum) throw new ArgumentOutOfRangeException(nameof(duration));
        if (float.IsNaN(blend) || blend < 0 || blend > PNCameraProfile.HardTimeMaximum) throw new ArgumentOutOfRangeException(nameof(blend));
        if (!PNCameraEasing.IsDefined(ease) || !PNCameraEasing.IsDefined(clock)) throw new ArgumentOutOfRangeException(nameof(ease));
        Scale = scale; Absolute = absolute || unit != PNZoomUnit.Scale; Duration = duration; Blend = blend; Priority = priority; Unit = unit; Ease = ease; Clock = clock;
    }
    public static PNZoomRequest Factor(float scale, float duration = 0f, float blend = .15f, int priority = 0, PNCameraEase ease = PNCameraEase.Linear, PNCameraClock clock = PNCameraClock.Camera) => new PNZoomRequest(scale, false, duration, blend, priority, PNZoomUnit.Scale, ease, clock);
    public static PNZoomRequest To(float scale, float duration = 0f, float blend = .15f, int priority = 0, PNCameraEase ease = PNCameraEase.Linear, PNCameraClock clock = PNCameraClock.Camera) => new PNZoomRequest(scale, true, duration, blend, priority, PNZoomUnit.Scale, ease, clock);
    // Camera distance in world units (composer rigs; refused with NotSupported elsewhere).
    public static PNZoomRequest ToDistance(float units, float duration = 0f, float blend = .15f, int priority = 0, PNCameraEase ease = PNCameraEase.Linear, PNCameraClock clock = PNCameraClock.Camera) => new PNZoomRequest(units, true, duration, blend, priority, PNZoomUnit.Distance, ease, clock);
    // Vertical field of view in degrees (perspective rigs).
    public static PNZoomRequest ToFieldOfView(float degrees, float duration = 0f, float blend = .15f, int priority = 0, PNCameraEase ease = PNCameraEase.Linear, PNCameraClock clock = PNCameraClock.Camera) => new PNZoomRequest(degrees, true, duration, blend, priority, PNZoomUnit.FieldOfView, ease, clock);
    // Orthographic half-height in world units (orthographic rigs).
    public static PNZoomRequest ToOrthographicSize(float size, float duration = 0f, float blend = .15f, int priority = 0, PNCameraEase ease = PNCameraEase.Linear, PNCameraClock clock = PNCameraClock.Camera) => new PNZoomRequest(size, true, duration, blend, priority, PNZoomUnit.OrthographicSize, ease, clock);
    // The same request expressed as a scale, given the authored value of the rig it applies to (distance units / FOV degrees / ortho size).
    public PNZoomRequest AsScale(float authoredValue)
    {
        if (Unit == PNZoomUnit.Scale) return this;
        if (!(authoredValue > 0f) || float.IsInfinity(authoredValue)) throw new ArgumentOutOfRangeException(nameof(authoredValue));
        float scale = Scale / authoredValue;
        scale = scale < HardMinimumScale ? HardMinimumScale : scale > HardMaximumScale ? HardMaximumScale : scale;
        return new PNZoomRequest(scale, true, Duration, Blend, Priority, PNZoomUnit.Scale, Ease, Clock);
    }
}

// Pure zoom composition. The target is recomputed from the base every frame (never accumulated onto the previous result), always lies
// inside the profile range, and smoothing converges without overshoot.
public static class PNZoomComposer
{
    // One active modifier with its current ease weight 0..1.
    public readonly struct Term
    {
        public readonly PNZoomRequest Request; public readonly float Weight; public readonly long Order;
        public Term(PNZoomRequest request, float weight, long order) { Request = request; Weight = weight < 0 ? 0 : weight > 1 ? 1 : weight; Order = order; }
    }

    // baseScale: player's base; terms: active modifiers; cinematic: optional override (also weighted); returns the clamped target scale.
    public static float Target(float baseScale, IEnumerable<Term> terms, float? cinematic, float cinematicWeight, PNZoomQuantity quantity, PNCameraProfile profile)
    {
        float Clamp(float value) => quantity == PNZoomQuantity.Distance ? profile.ClampDistanceScale(value) : profile.ClampLensScale(value);
        float value1 = Clamp(float.IsNaN(baseScale) ? profile.BaseScale : baseScale);
        // Multiplicative modifiers apply in (priority, registration) order; the highest-priority absolute one, if any, replaces the product.
        Term? absolute = null;
        foreach (var term in terms.OrderBy(t => t.Request.Priority).ThenBy(t => t.Order))
        {
            if (term.Request.Absolute) { if (absolute == null || term.Request.Priority >= absolute.Value.Request.Priority) absolute = term; continue; }
            value1 *= 1f + (term.Request.Scale - 1f) * term.Weight;
        }
        if (absolute.HasValue) value1 = value1 + (absolute.Value.Request.Scale - value1) * absolute.Value.Weight;
        if (cinematic.HasValue) value1 = value1 + (cinematic.Value - value1) * (cinematicWeight < 0 ? 0 : cinematicWeight > 1 ? 1 : cinematicWeight);
        return Clamp(value1);
    }

    // Exponential approach with the profile speed; frame-rate independent, never overshoots, snaps when within a negligible distance.
    public static float Smooth(float current, float target, float deltaTime, float speed)
    {
        if (float.IsNaN(current)) return target;
        if (deltaTime <= 0) return current;
        float amount = 1f - (float)Math.Exp(-speed * deltaTime);
        float next = current + (target - current) * amount;
        return Math.Abs(target - next) < 1e-4f ? target : next;
    }

    // Ease weight for a timed request: rises over Blend, holds, falls over Blend before Duration ends (Duration 0 = no end).
    public static float Weight(float age, float duration, float blend, PNCameraEase ease = PNCameraEase.Linear)
    {
        float up = blend <= 0 ? 1f : Math.Min(1f, age / blend);
        if (duration <= 0) return PNCameraEasing.Evaluate(ease, up);
        float remaining = duration - age;
        float down = blend <= 0 ? (remaining > 0 ? 1f : 0f) : Math.Max(0f, Math.Min(1f, remaining / blend));
        return PNCameraEasing.Evaluate(ease, Math.Min(up, down));
    }
}
