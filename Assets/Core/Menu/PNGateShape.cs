using System;
using System.Collections.Generic;

// EN: The "gate" outline of a mode card (Docs/Mockups/ModeMenuDemo.html): an elongated hexagon with rounded corners in an 80 x 229 box
//     (ratio 2.86). The path is sampled once into a closed polyline, and a convex strip decomposition (horizontal bands) is provided so the
//     card art can be drawn as a textured mesh with a vertical tint. Pure maths, no Unity types.
// VI: Hình cổng của thẻ chế độ (hộp 80 x 229); đường viền lấy mẫu thành đa giác kín, kèm dải ngang để vẽ ảnh bằng mesh.
public static class PNGateShape
{
    public const float Width = 80f, Height = 229f, Ratio = Height / Width;
    // The demo path: M31.6 11.6 Q40 3 48.4 11.6 L68.6 32.4 Q77 41 77 53 L77 176 Q77 188 68.6 196.6 L48.4 217.4 Q40 226 31.6 217.4 L11.4 196.6 Q3 188 3 176 L3 53 Q3 41 11.4 32.4 Z
    private static readonly float[] Corners =
    {
        // start, control, end of each rounded corner (the straight sides join consecutive corners)
        31.6f, 11.6f, 40f, 3f, 48.4f, 11.6f,
        68.6f, 32.4f, 77f, 41f, 77f, 53f,
        77f, 176f, 77f, 188f, 68.6f, 196.6f,
        48.4f, 217.4f, 40f, 226f, 31.6f, 217.4f,
        11.4f, 196.6f, 3f, 188f, 3f, 176f,
        3f, 53f, 3f, 41f, 11.4f, 32.4f,
    };

    // Closed outline as x0,y0,x1,y1,... (the first point is not repeated). `perCurve` points are taken on each rounded corner.
    public static float[] Outline(int perCurve = 8)
    {
        if (perCurve < 2 || perCurve > 64) throw new ArgumentOutOfRangeException(nameof(perCurve));
        var points = new List<float>();
        for (int c = 0; c < 6; c++)
        {
            float x0 = Corners[c * 6], y0 = Corners[c * 6 + 1], cx = Corners[c * 6 + 2], cy = Corners[c * 6 + 3], x1 = Corners[c * 6 + 4], y1 = Corners[c * 6 + 5];
            for (int i = 0; i <= perCurve; i++)
            {
                float t = i / (float)perCurve, u = 1 - t;
                points.Add(u * u * x0 + 2 * u * t * cx + t * t * x1);
                points.Add(u * u * y0 + 2 * u * t * cy + t * t * y1);
            }
        }
        return points.ToArray();
    }

    // Horizontal bands of the (convex) outline: for each of bands + 1 scanlines, y with the left and right edge x.
    public static void Strips(float[] outline, int bands, out float[] y, out float[] left, out float[] right)
    {
        if (outline == null || outline.Length < 6 || outline.Length % 2 != 0) throw new ArgumentException("Outline needs at least three points.");
        if (bands < 1 || bands > 256) throw new ArgumentOutOfRangeException(nameof(bands));
        y = new float[bands + 1]; left = new float[bands + 1]; right = new float[bands + 1];
        float top = float.MaxValue, bottom = float.MinValue;
        for (int i = 1; i < outline.Length; i += 2) { top = Math.Min(top, outline[i]); bottom = Math.Max(bottom, outline[i]); }
        int n = outline.Length / 2;
        for (int k = 0; k <= bands; k++)
        {
            float yy = top + (bottom - top) * k / bands;
            // The extreme scanlines sit on the flat-ish tips; nudge inward so an edge crossing exists.
            float probe = k == 0 ? yy + 1e-3f : k == bands ? yy - 1e-3f : yy;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                float ax = outline[i * 2], ay = outline[i * 2 + 1], bx = outline[((i + 1) % n) * 2], by = outline[((i + 1) % n) * 2 + 1];
                if ((ay <= probe && by > probe) || (by <= probe && ay > probe))
                {
                    float x = ax + (probe - ay) / (by - ay) * (bx - ax);
                    lo = Math.Min(lo, x); hi = Math.Max(hi, x);
                }
            }
            y[k] = yy; left[k] = lo; right[k] = hi;
        }
    }
}
