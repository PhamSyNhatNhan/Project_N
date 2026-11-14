using System;

// Pure planar geometry of the blast shapes (no Unity types), so the standalone smoke suite can check it.
public static class PNBlastGeometry
{
    // A filled circle (centre, radius) against an axis-aligned footprint on XZ, boundary included.
    public static bool CircleIntersectsBox(float cx, float cz, float radius, float minX, float minZ, float maxX, float maxZ)
    {
        if (!(radius > 0f) || float.IsInfinity(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
        double dx = cx < minX ? minX - cx : cx > maxX ? cx - maxX : 0d, dz = cz < minZ ? minZ - cz : cz > maxZ ? cz - maxZ : 0d;
        return dx * dx + dz * dz <= (double)radius * radius + 1e-9;
    }
    // A filled regular polygon (`sides` 3..16, circumradius `radius`, first vertex at +z turned by `degrees`) against an axis-aligned footprint on XZ, boundary included (SAT).
    public static bool PolygonIntersectsBox(float cx, float cz, float radius, int sides, float degrees, float minX, float minZ, float maxX, float maxZ)
    {
        if (!(radius > 0f) || float.IsInfinity(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
        if (sides < 3 || sides > 16) throw new ArgumentOutOfRangeException(nameof(sides));
        var vx = new double[sides]; var vz = new double[sides]; double turn = degrees * Math.PI / 180d;
        for (int i = 0; i < sides; i++)
        {
            double a = turn + i * 2d * Math.PI / sides;           // 0 is +z, like the triangle of the Starweaver
            vx[i] = cx + radius * Math.Sin(a); vz[i] = cz + radius * Math.Cos(a);
        }
        if (!Axis(1, 0, vx, vz, minX, minZ, maxX, maxZ) || !Axis(0, 1, vx, vz, minX, minZ, maxX, maxZ)) return false;
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            if (!Axis(-(vz[j] - vz[i]), vx[j] - vx[i], vx, vz, minX, minZ, maxX, maxZ)) return false;
        }
        return true;
    }
    private static bool Axis(double x, double z, double[] vx, double[] vz, float minX, float minZ, float maxX, float maxZ)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < vx.Length; i++) { double d = x * vx[i] + z * vz[i]; if (d < lo) lo = d; if (d > hi) hi = d; }
        double mid = x * (minX + maxX) * .5 + z * (minZ + maxZ) * .5, extent = Math.Abs(x) * (maxX - minX) * .5 + Math.Abs(z) * (maxZ - minZ) * .5;
        return hi + 1e-6 >= mid - extent && lo - 1e-6 <= mid + extent;
    }
}
