using System;
using System.Collections.Generic;

// Blend/ease shape shared by zoom, lock-on, cinematic blends and shake envelopes.
public enum PNCameraEase { EaseInOut = 0, Cut = 1, Linear = 2, EaseIn = 3, EaseOut = 4, HardIn = 5, HardOut = 6 }

// Which clock advances a request. Camera = the Brain's clock (stops while time scale is 0, like gameplay). Real = unscaled time
// (keeps running while paused or during a hitstop). Hitstop/slow-motion themselves belong to the time-scale systems, not the camera.
public enum PNCameraClock { Camera = 0, Real = 1 }

public static class PNCameraEasing
{
    // t is clamped to 0..1. Cut jumps at the first moment after 0.
    public static float Evaluate(PNCameraEase ease, float t)
    {
        if (float.IsNaN(t)) return 0f;
        t = t < 0f ? 0f : t > 1f ? 1f : t;
        switch (ease)
        {
            case PNCameraEase.Cut: return t > 0f ? 1f : 0f;
            case PNCameraEase.Linear: return t;
            case PNCameraEase.EaseIn: return t * t;
            case PNCameraEase.EaseOut: return 1f - (1f - t) * (1f - t);
            case PNCameraEase.HardIn: return t * t * t;
            case PNCameraEase.HardOut: { float u = 1f - t; return 1f - u * u * u; }
            default: return t * t * (3f - 2f * t);
        }
    }
    public static bool IsDefined(PNCameraEase ease) => Enum.IsDefined(typeof(PNCameraEase), ease);
    public static bool IsDefined(PNCameraClock clock) => Enum.IsDefined(typeof(PNCameraClock), clock);
}

// Plain point so path maths stays Unity-free (and testable in the standalone smoke).
public readonly struct PNPoint
{
    public readonly float X, Y, Z;
    public PNPoint(float x, float y, float z) { X = x; Y = y; Z = z; }
    public bool IsFinite => !(float.IsNaN(X + Y + Z) || float.IsInfinity(X + Y + Z));
    public static PNPoint Lerp(PNPoint a, PNPoint b, float t) => new PNPoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
}

// Position along a list of waypoints for a normalized parameter 0..1 (equal time per segment). Catmull-Rom through the points, endpoints doubled.
public static class PNCameraPath
{
    public const int MaximumPoints = 64;
    public static PNPoint Sample(IReadOnlyList<PNPoint> points, float t, bool smooth = true)
    {
        if (points == null || points.Count == 0) throw new ArgumentException("A path needs points.", nameof(points));
        if (points.Count == 1) return points[0];
        t = float.IsNaN(t) ? 0f : t < 0f ? 0f : t > 1f ? 1f : t;
        int segments = points.Count - 1;
        float scaled = t * segments; int index = (int)scaled; if (index >= segments) index = segments - 1;
        float local = scaled - index;
        if (!smooth) return PNPoint.Lerp(points[index], points[index + 1], local);
        var p0 = points[Math.Max(index - 1, 0)]; var p1 = points[index]; var p2 = points[index + 1]; var p3 = points[Math.Min(index + 2, points.Count - 1)];
        return new PNPoint(Spline(p0.X, p1.X, p2.X, p3.X, local), Spline(p0.Y, p1.Y, p2.Y, p3.Y, local), Spline(p0.Z, p1.Z, p2.Z, p3.Z, local));
    }
    private static float Spline(float a, float b, float c, float d, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return .5f * (2f * b + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (-a + 3f * b - 3f * c + d) * t3);
    }
}
