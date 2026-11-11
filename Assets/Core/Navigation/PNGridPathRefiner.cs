using System;
using System.Collections.Generic;

public readonly struct PNGridPoint
{
    public readonly float X, Z;
    public PNGridPoint(float x, float z) { X = x; Z = z; }
    public override string ToString() => "(" + X.ToString("0.###") + "," + Z.ToString("0.###") + ")";
}

// EN: Path refinement on a grid world (Docs/SandboxPlan.md, step G2). A string-pulled path hugs the corners of the free cells, so a body with a radius touches the walls and gets stuck there. Measured on a physical capsule
//     (Docs/Sandbox.md 5b point 10): a corner margin of radius + the follower's corner tolerance takes a 0.5 m grid from 5 of 12 routes to 10 of 12; what is left are segments that cut the corner of an obstacle that is not a
//     waypoint. This class does both steps from the cells alone: (1) every inner waypoint is moved until it is at least `margin` from every blocked cell, (2) every segment that still passes closer than `radius` to a
//     blocked cell gets a waypoint put beside that cell's nearest corner, `margin` away. The first and last point are never moved. Pure and deterministic: the same path gives the same result.
// VI: Tinh chỉnh đường trên world lưới: (1) mọi điểm gấp khúc bên trong bị đẩy ra cách ô chặn tối thiểu `margin`; (2) đoạn nào còn lướt gần ô chặn hơn `radius` thì chèn một điểm cạnh góc gần nhất, cách `margin`.
public static class PNGridPathRefiner
{
    private const float Epsilon = 1e-3f;

    public static List<PNGridPoint> Refine(IList<PNGridPoint> path, IPNNavigationSource source, float radius, float margin, int maxInsertions = 64)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (float.IsNaN(radius) || radius <= 0 || float.IsNaN(margin) || margin < radius) throw new ArgumentException("The margin must be at least the radius.");
        var result = new List<PNGridPoint>(path);
        if (result.Count < 3) { if (result.Count == 2) Insert(result, source, radius, margin, maxInsertions); return result; }
        for (int i = 1; i < result.Count - 1; i++) result[i] = Push(result[i], source, margin);
        Insert(result, source, radius, margin, maxInsertions);
        return result;
    }

    // Distance from `point` to the nearest blocked cell within `reach`, and how far each is. Used by the tests as the oracle of "clear enough".
    public static float Clearance(PNGridPoint point, IPNNavigationSource source, float reach)
    {
        float best = reach;
        Window(point.X - reach, point.Z - reach, point.X + reach, point.Z + reach, source, (cx, cz, rect) =>
        { float d = PointRect(point.X, point.Z, rect, out _, out _); if (d < best) best = d; });
        return best;
    }

    public static float SegmentClearance(PNGridPoint a, PNGridPoint b, IPNNavigationSource source, float reach)
    {
        float best = reach;
        Window(Math.Min(a.X, b.X) - reach, Math.Min(a.Z, b.Z) - reach, Math.Max(a.X, b.X) + reach, Math.Max(a.Z, b.Z) + reach, source, (cx, cz, rect) =>
        { float d = SegmentRect(a, b, rect, out _, out _, out _); if (d < best) best = d; });
        return best;
    }

    private struct Rect { public float X0, Z0, X1, Z1; }

    private static void Window(float x0, float z0, float x1, float z1, IPNNavigationSource source, Action<int, int, Rect> visit)
    {
        float c = source.CellSize;
        int i0 = (int)Math.Floor((x0 - source.OriginX) / c), i1 = (int)Math.Floor((x1 - source.OriginX) / c);
        int j0 = (int)Math.Floor((z0 - source.OriginZ) / c), j1 = (int)Math.Floor((z1 - source.OriginZ) / c);
        for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++)
        {
            if (!source.Blocks(i, j)) continue;
            // the world's edge is a wall too, but a body is never asked to avoid the whole outside: only cells beside the grid count
            if (i < -1 || j < -1 || i > source.Width || j > source.Depth) continue;
            visit(i, j, new Rect { X0 = source.OriginX + i * c, Z0 = source.OriginZ + j * c, X1 = source.OriginX + (i + 1) * c, Z1 = source.OriginZ + (j + 1) * c });
        }
    }

    private static float PointRect(float px, float pz, Rect r, out float nearestX, out float nearestZ)
    {
        nearestX = Math.Max(r.X0, Math.Min(px, r.X1)); nearestZ = Math.Max(r.Z0, Math.Min(pz, r.Z1));
        float dx = px - nearestX, dz = pz - nearestZ; return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private static float PointSegment(float px, float pz, PNGridPoint a, PNGridPoint b, out float t)
    {
        float dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
        t = len2 < 1e-12f ? 0 : Math.Max(0, Math.Min(1, ((px - a.X) * dx + (pz - a.Z) * dz) / len2));
        float qx = a.X + dx * t - px, qz = a.Z + dz * t - pz; return (float)Math.Sqrt(qx * qx + qz * qz);
    }

    // Distance between a segment and a rectangle. `corner` is the rectangle's nearest point, `onSegment` the parameter of the segment's nearest point (0 or 1 = an end point).
    private static float SegmentRect(PNGridPoint a, PNGridPoint b, Rect r, out PNGridPoint corner, out PNGridPoint segmentPoint, out float onSegment)
    {
        // a segment that crosses the rectangle is at distance 0
        float t0 = 0, t1 = 1, dx = b.X - a.X, dz = b.Z - a.Z;
        bool crosses = Clip(-dx, a.X - r.X0, ref t0, ref t1) && Clip(dx, r.X1 - a.X, ref t0, ref t1) && Clip(-dz, a.Z - r.Z0, ref t0, ref t1) && Clip(dz, r.Z1 - a.Z, ref t0, ref t1);
        if (crosses)
        {
            float tm = (t0 + t1) * .5f; segmentPoint = new PNGridPoint(a.X + dx * tm, a.Z + dz * tm); corner = segmentPoint; onSegment = tm; return 0;
        }
        float best = float.MaxValue; corner = default; segmentPoint = default; onSegment = 0;
        float d = PointRect(a.X, a.Z, r, out float nx, out float nz);
        if (d < best) { best = d; corner = new PNGridPoint(nx, nz); segmentPoint = a; onSegment = 0; }
        d = PointRect(b.X, b.Z, r, out nx, out nz);
        if (d < best) { best = d; corner = new PNGridPoint(nx, nz); segmentPoint = b; onSegment = 1; }
        for (int k = 0; k < 4; k++)
        {
            float cx = (k & 1) == 0 ? r.X0 : r.X1, cz = (k & 2) == 0 ? r.Z0 : r.Z1;
            d = PointSegment(cx, cz, a, b, out float t);
            if (d < best) { best = d; corner = new PNGridPoint(cx, cz); segmentPoint = new PNGridPoint(a.X + dx * t, a.Z + dz * t); onSegment = t; }
        }
        return best;
    }

    // Liang-Barsky step.
    private static bool Clip(float p, float q, ref float t0, ref float t1)
    {
        if (Math.Abs(p) < 1e-9f) return q >= 0;
        float r = q / p;
        if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
        else { if (r < t0) return false; if (r < t1) t1 = r; }
        return true;
    }

    // Move a waypoint until it is `margin` from every blocked cell (a few rounds: a doorway has a cell on each side).
    private static PNGridPoint Push(PNGridPoint p, IPNNavigationSource source, float margin)
    {
        for (int round = 0; round < 4; round++)
        {
            float vx = 0, vz = 0, nearest = float.MaxValue; int touching = 0; var cur = p;
            Window(cur.X - margin, cur.Z - margin, cur.X + margin, cur.Z + margin, source, (cx, cz, rect) =>
            {
                float d = PointRect(cur.X, cur.Z, rect, out float nx, out float nz);
                if (d >= margin) return;
                touching++; if (d < nearest) nearest = d;
                float ox, oz;
                if (d > 1e-5f) { ox = (cur.X - nx) / d; oz = (cur.Z - nz) / d; }
                else { ox = cur.X - (rect.X0 + rect.X1) * .5f; oz = cur.Z - (rect.Z0 + rect.Z1) * .5f; float l = (float)Math.Sqrt(ox * ox + oz * oz); if (l > 1e-6f) { ox /= l; oz /= l; } }
                float weight = margin - d; vx += ox * weight; vz += oz * weight;
            });
            if (touching == 0) return p;
            float length = (float)Math.Sqrt(vx * vx + vz * vz);
            if (length < 1e-6f) return p;
            float move = margin - nearest + 1e-3f;
            p = new PNGridPoint(p.X + vx / length * move, p.Z + vz / length * move);
        }
        return p;
    }

    // Put a waypoint beside the corner of every blocked cell a segment passes too close to.
    private static void Insert(List<PNGridPoint> path, IPNNavigationSource source, float radius, float margin, int maxInsertions)
    {
        int inserted = 0;
        for (int guard = 0; guard < 4 * maxInsertions + 8; guard++)
        {
            bool changed = false;
            for (int s = 0; s < path.Count - 1 && !changed; s++)
            {
                var a = path[s]; var b = path[s + 1]; float worst = radius - Epsilon; bool found = false; PNGridPoint cornerAt = default, nearestOnSegment = default; Rect worstRect = default;
                Window(Math.Min(a.X, b.X) - radius, Math.Min(a.Z, b.Z) - radius, Math.Max(a.X, b.X) + radius, Math.Max(a.Z, b.Z) + radius, source, (cx, cz, rect) =>
                {
                    float d = SegmentRect(a, b, rect, out var corner, out var onSegmentPoint, out float t);
                    // too close at an end point: those are the start, the goal or an already pushed waypoint; inserting beside the cell cannot help
                    if (d >= worst || t <= 1e-4f || t >= 1 - 1e-4f) return;
                    worst = d; found = true; cornerAt = corner; nearestOnSegment = onSegmentPoint; worstRect = rect;
                });
                if (!found) continue;
                if (inserted >= maxInsertions) return;
                float ox = nearestOnSegment.X - cornerAt.X, oz = nearestOnSegment.Z - cornerAt.Z, l = (float)Math.Sqrt(ox * ox + oz * oz);
                if (l < 1e-5f) { ox = nearestOnSegment.X - (worstRect.X0 + worstRect.X1) * .5f; oz = nearestOnSegment.Z - (worstRect.Z0 + worstRect.Z1) * .5f; l = (float)Math.Sqrt(ox * ox + oz * oz); }
                if (l < 1e-6f) return;
                var w = new PNGridPoint(cornerAt.X + ox / l * margin, cornerAt.Z + oz / l * margin);
                w = Push(w, source, margin);
                path.Insert(s + 1, w); inserted++; changed = true;
            }
            if (!changed) return;
        }
    }
}
