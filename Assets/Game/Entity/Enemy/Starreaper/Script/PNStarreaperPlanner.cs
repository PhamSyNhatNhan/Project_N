using System;
using System.Collections.Generic;

// Pure geometry and choices of the Starreaper's skills (no Unity types, no physics), so the smoke suite can check them:
// where the arrows of the rain land, how a shadow's second arrow is turned, and whether a retreat has room. Callers pass the random source.
public static class PNStarreaperPlanner
{
    public struct Point { public float X, Z; public Point(float x, float z) { X = x; Z = z; } }

    // A random number of landing points (minCount..maxCount) inside the disc (centerX, centerZ, radius), spread out: each is at least `minSpacing` from the others.
    // A point that cannot be placed after a bounded number of tries takes the best of its candidates, so the count is always met.
    public static List<Point> RainPoints(float centerX, float centerZ, float radius, float minSpacing, int minCount, int maxCount, Random random)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));
        if (!(radius > 0f) || minSpacing < 0f || minCount < 1 || maxCount < minCount) throw new ArgumentOutOfRangeException(nameof(radius));
        int count = random.Next(minCount, maxCount + 1);
        var points = new List<Point>(count);
        for (int i = 0; i < count; i++)
        {
            Point best = default; float bestGap = -1f;
            for (int attempt = 0; attempt < 48; attempt++)
            {
                double angle = random.NextDouble() * Math.PI * 2, distance = radius * Math.Sqrt(random.NextDouble());
                var candidate = new Point(centerX + (float)(Math.Cos(angle) * distance), centerZ + (float)(Math.Sin(angle) * distance));
                float gap = NearestGap(points, candidate);
                if (gap >= minSpacing) { best = candidate; bestGap = gap; break; }
                if (gap > bestGap) { best = candidate; bestGap = gap; }
            }
            points.Add(best);
        }
        return points;
    }

    private static float NearestGap(List<Point> points, Point candidate)
    {
        float nearest = float.MaxValue;
        foreach (var p in points) { float dx = p.X - candidate.X, dz = p.Z - candidate.Z; nearest = Math.Min(nearest, (float)Math.Sqrt(dx * dx + dz * dz)); }
        return nearest;
    }

    // The planar direction (x, z) turned by `degrees` about the vertical axis (positive turns from +x toward +z).
    public static Point Rotate(float x, float z, float degrees)
    {
        double radians = degrees * Math.PI / 180d, c = Math.Cos(radians), s = Math.Sin(radians);
        return new Point((float)(x * c - z * s), (float)(x * s + z * c));
    }

    // The shadow's second arrow leaves `side` * angle away from the first; the side alternates between uses (side is +1 or -1).
    public static int NextSide(int side) => side >= 0 ? -1 : 1;

    // A retreat of `distance` along (dx, dz) needs `free` clear length; a small margin keeps the body off the wall.
    public static bool HasRoom(float free, float distance, float margin = .05f) => free >= distance + margin;
}
