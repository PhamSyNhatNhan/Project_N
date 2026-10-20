#if UNITY_5_3_OR_NEWER
using UnityEngine;

// Pure box math for the projectile body: an oriented box (aligned to the heading) against axis-aligned target boxes.
// Uses fixed scratch arrays (main thread only) so the per-pair test does not allocate.
public static class PNProjectileGeometry
{
    private static readonly float[] a = new float[3], b = new float[3], tw = new float[3], t = new float[3], r = new float[9], abs = new float[9];
    // Orthonormal frame with forward = heading; up is the world Y axis unless the heading is nearly vertical.
    public static void Frame(Vector3 forward, out Vector3 right, out Vector3 up, out Vector3 fwd)
    {
        fwd = forward.sqrMagnitude < 1e-12f ? Vector3.forward : forward.normalized;
        var reference = Mathf.Abs(fwd.y) > .999f ? Vector3.right : Vector3.up;
        right = Vector3.Cross(reference, fwd).normalized; up = Vector3.Cross(fwd, right);
    }
    // Separating-axis test (15 axes) between an oriented box (half extents along right/up/forward) and a world-aligned box.
    // Touching counts as overlap.
    public static bool ObbOverlapsAabb(Vector3 center, Vector3 right, Vector3 up, Vector3 forward, Vector3 half, Vector3 boxCenter, Vector3 boxExtents)
    {
        a[0] = half.x; a[1] = half.y; a[2] = half.z; b[0] = boxExtents.x; b[1] = boxExtents.y; b[2] = boxExtents.z;
        var world = boxCenter - center; tw[0] = world.x; tw[1] = world.y; tw[2] = world.z;
        for (int i = 0; i < 3; i++)
        {
            var u = i == 0 ? right : i == 1 ? up : forward;
            r[i * 3] = u.x; r[i * 3 + 1] = u.y; r[i * 3 + 2] = u.z;
            abs[i * 3] = Mathf.Abs(u.x) + 1e-6f; abs[i * 3 + 1] = Mathf.Abs(u.y) + 1e-6f; abs[i * 3 + 2] = Mathf.Abs(u.z) + 1e-6f;
            t[i] = Vector3.Dot(world, u);
        }
        for (int i = 0; i < 3; i++)
            if (Mathf.Abs(t[i]) > a[i] + b[0] * abs[i * 3] + b[1] * abs[i * 3 + 1] + b[2] * abs[i * 3 + 2]) return false;
        for (int j = 0; j < 3; j++)
            if (Mathf.Abs(tw[j]) > a[0] * abs[j] + a[1] * abs[3 + j] + a[2] * abs[6 + j] + b[j]) return false;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                int i1 = (i + 1) % 3, i2 = (i + 2) % 3, j1 = (j + 1) % 3, j2 = (j + 2) % 3;
                float ra = a[i1] * abs[i2 * 3 + j] + a[i2] * abs[i1 * 3 + j], rb = b[j1] * abs[i * 3 + j2] + b[j2] * abs[i * 3 + j1];
                if (Mathf.Abs(t[i2] * r[i1 * 3 + j] - t[i1] * r[i2 * 3 + j]) > ra + rb) return false;
            }
        return true;
    }
    public static Vector3 ClosestPoint(Vector3 point, Vector3 boxCenter, Vector3 boxExtents)
        => new Vector3(Mathf.Clamp(point.x, boxCenter.x - boxExtents.x, boxCenter.x + boxExtents.x), Mathf.Clamp(point.y, boxCenter.y - boxExtents.y, boxCenter.y + boxExtents.y),
            Mathf.Clamp(point.z, boxCenter.z - boxExtents.z, boxCenter.z + boxExtents.z));
    // Distance from a point to the segment from..to, and the parameter (0..1) of its closest point.
    public static float DistanceToSegment(Vector3 point, Vector3 from, Vector3 to, out float parameter)
    {
        var seg = to - from; float length2 = seg.sqrMagnitude;
        parameter = length2 < 1e-12f ? 0f : Mathf.Clamp01(Vector3.Dot(point - from, seg) / length2);
        return (point - (from + seg * parameter)).magnitude;
    }
}
#endif
