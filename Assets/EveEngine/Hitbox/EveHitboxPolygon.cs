using System;
using System.Collections.Generic;
using UnityEngine;

namespace EveEngine
{
    // EN: Validate a simple outline and ear-clip it; convex hull cooking alone would fill concave notches.
    // VI: Kiểm tra đường bao đơn và chia tam giác; convex hull trực tiếp sẽ lấp phần lõm.
    internal static class EveHitboxPolygon
    {
        internal sealed class Result { internal List<Vector2> Points; internal int[] Triangles; }
        internal static Result Build(IEnumerable<Vector2> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var points = new List<Vector2>();
            foreach (var p in source)
            {
                EveHitboxShape.Finite(p.x); EveHitboxShape.Finite(p.y);
                points.Add(p);
                if (points.Count > 64) throw new ArgumentException("Polygon supports 3..64 points.");
            }
            int n = points.Count;
            if (n < 3) throw new ArgumentException("Polygon supports 3..64 points.");
            double minX = points[0].x, maxX = minX, minY = points[0].y, maxY = minY;
            foreach (var p in points)
            { minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x); minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y); }
            double span = Math.Max(maxX - minX, maxY - minY);
            double epsilon = span * span * 1e-10;
            double area = 0d;
            for (int i = 0; i < n; i++)
            {
                var a = points[i]; var b = points[(i + 1) % n];
                // Relative coordinates avoid cancellation when a small outline is far from the pose origin.
                area += ((double)a.x - minX) * ((double)b.y - minY) - ((double)b.x - minX) * ((double)a.y - minY);
                if (Math.Abs(Cross(points[(i + n - 1) % n], a, b)) <= epsilon)
                    throw new ArgumentException("Polygon cannot have duplicate or collinear adjacent vertices.");
                for (int j = i + 1; j < n; j++)
                {
                    double dx = (double)a.x - points[j].x, dy = (double)a.y - points[j].y;
                    if (dx * dx + dy * dy <= epsilon) throw new ArgumentException("Polygon vertices must be distinct; closure is implicit.");
                    if (j == i + 1 || (i == 0 && j == n - 1)) continue;
                    if (Intersects(a, b, points[j], points[(j + 1) % n], epsilon))
                        throw new ArgumentException("Polygon edges cannot intersect or touch nonadjacent edges.");
                }
            }
            if (Math.Abs(area) <= epsilon) throw new ArgumentException("Polygon area must be nonzero.");
            if (area < 0d) points.Reverse();
            var remaining = new List<int>();
            for (int i = 0; i < n; i++) remaining.Add(i);
            var triangles = new List<int>();
            while (remaining.Count > 3)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i], c = remaining[(i + 1) % remaining.Count];
                    if (Cross(points[a], points[b], points[c]) <= epsilon) continue;
                    bool occupied = false;
                    foreach (int p in remaining)
                    {
                        if (p == a || p == b || p == c) continue;
                        if (Cross(points[a], points[b], points[p]) >= -epsilon && Cross(points[b], points[c], points[p]) >= -epsilon &&
                            Cross(points[c], points[a], points[p]) >= -epsilon) { occupied = true; break; }
                    }
                    if (occupied) continue;
                    triangles.AddRange(new[] { a, b, c }); remaining.RemoveAt(i); clipped = true; break;
                }
                if (!clipped) throw new ArgumentException("Polygon cannot be triangulated; remove near-degenerate vertices.");
            }
            triangles.AddRange(remaining);
            return new Result { Points = points, Triangles = triangles.ToArray() };
        }
        private static double Cross(Vector2 a, Vector2 b, Vector2 c)
            => ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);
        private static bool Intersects(Vector2 a, Vector2 b, Vector2 c, Vector2 d, double epsilon)
        {
            double x = Cross(a, b, c), y = Cross(a, b, d), z = Cross(c, d, a), w = Cross(c, d, b);
            if (((x > epsilon && y < -epsilon) || (x < -epsilon && y > epsilon)) &&
                ((z > epsilon && w < -epsilon) || (z < -epsilon && w > epsilon))) return true;
            return (Math.Abs(x) <= epsilon && OnSegment(a, b, c)) || (Math.Abs(y) <= epsilon && OnSegment(a, b, d)) ||
                (Math.Abs(z) <= epsilon && OnSegment(c, d, a)) || (Math.Abs(w) <= epsilon && OnSegment(c, d, b));
        }
        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
            => p.x >= Math.Min(a.x, b.x) && p.x <= Math.Max(a.x, b.x) && p.y >= Math.Min(a.y, b.y) && p.y <= Math.Max(a.y, b.y);
    }
}
