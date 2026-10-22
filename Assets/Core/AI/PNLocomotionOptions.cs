#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

// Immutable request data. Disabled limits preserve the existing immediate follower.
public sealed class PNLocomotionOptions
{
    public float StopDistance { get; }
    public float SlowDistance { get; }
    public float SpeedFraction { get; }
    public bool AccelerationEnabled { get; }
    public float Acceleration { get; }
    public bool BrakingEnabled { get; }
    public float Braking { get; }
    public bool TurnLimitEnabled { get; }
    public float TurnRate { get; }
    public bool MaximumSpeedEnabled { get; }
    public float MaximumSpeed { get; }
    public bool IsImmediate => SlowDistance == 0 && !AccelerationEnabled && !BrakingEnabled && !TurnLimitEnabled && !MaximumSpeedEnabled && SpeedFraction == 1;

    public PNLocomotionOptions(float stopDistance, float slowDistance = 0, float speedFraction = 1,
        bool accelerationEnabled = false, float acceleration = 8, bool brakingEnabled = false,
        float braking = 12, bool turnLimitEnabled = false, float turnRate = 360, bool maximumSpeedEnabled = false, float maximumSpeed = 1)
    {
        PNAgentGeometry.Nonnegative(stopDistance, nameof(stopDistance));
        PNAgentGeometry.Nonnegative(slowDistance, nameof(slowDistance));
        PNAgentGeometry.Nonnegative(speedFraction, nameof(speedFraction));
        if (speedFraction > 1) throw new ArgumentOutOfRangeException(nameof(speedFraction));
        PNNavigationSettings.Positive(acceleration, nameof(acceleration));
        PNNavigationSettings.Positive(braking, nameof(braking));
        PNNavigationSettings.Positive(turnRate, nameof(turnRate));
        PNNavigationSettings.Positive(maximumSpeed, nameof(maximumSpeed)); MaximumSpeedEnabled = maximumSpeedEnabled; MaximumSpeed = maximumSpeed;
        StopDistance = stopDistance; SlowDistance = slowDistance; SpeedFraction = speedFraction;
        AccelerationEnabled = accelerationEnabled; Acceleration = acceleration;
        BrakingEnabled = brakingEnabled; Braking = braking; TurnLimitEnabled = turnLimitEnabled; TurnRate = turnRate;
    }
    public static PNLocomotionOptions LegacyImmediate(float stopDistance) => new PNLocomotionOptions(stopDistance);
    public PNLocomotionOptions WithStopDistance(float stopDistance) => new PNLocomotionOptions(stopDistance, SlowDistance, SpeedFraction,
        AccelerationEnabled, Acceleration, BrakingEnabled, Braking, TurnLimitEnabled, TurnRate, MaximumSpeedEnabled, MaximumSpeed);
    public PNLocomotionOptions WithMaximumSpeed(float maximumSpeed) => new PNLocomotionOptions(StopDistance, SlowDistance, SpeedFraction,
        AccelerationEnabled, Acceleration, BrakingEnabled, Braking, TurnLimitEnabled, TurnRate, true, maximumSpeed);
}

public enum PNDistanceMetric { CenterXZ, FootprintGapXZ }
public static class PNAgentGeometry
{
    public static void Nonnegative(float value, string name)
    { if (float.IsNaN(value) || float.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(name); }
    public static float CenterDistance(Vector3 a, Vector3 b)
    { PNNavigationSettings.Position(a); PNNavigationSettings.Position(b); return new Vector2(a.x - b.x, a.z - b.z).magnitude; }
    public static float Distance(Vector3 a, float radiusA, Vector3 b, float radiusB, PNDistanceMetric metric)
    {
        Nonnegative(radiusA, nameof(radiusA)); Nonnegative(radiusB, nameof(radiusB));
        if (!Enum.IsDefined(typeof(PNDistanceMetric), metric)) throw new ArgumentOutOfRangeException(nameof(metric));
        float center = CenterDistance(a, b);
        return metric == PNDistanceMetric.CenterXZ ? center : Mathf.Max(0, center - radiusA - radiusB);
    }
    public static float FootprintRadius(CapsuleCollider body)
    {
        if (body == null || body.direction != 1) throw new ArgumentException("An upright capsule body is required.", nameof(body));
        Vector3 scale = body.transform.lossyScale;
        float result = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        PNNavigationSettings.Positive(result, nameof(body)); return result;
    }
    public static Vector2 Planar(Vector3 value) => new Vector2(value.x, value.z);
}
#endif
