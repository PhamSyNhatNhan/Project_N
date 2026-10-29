using System;

// EN: Geometry of a bendable part (a tail, a cloak, a tentacle). The image is cut into strips along an axis; the centre line of the part is a curve whose tangent turns by
//       angle(s) = bend * s / length + sway * sin(2 pi s / length)      (degrees, s = distance from the pivot along the axis, length = image size along the axis)
//     so `bend` is the total turn at the far end (a C shape) and `sway` adds an S shape on top (its peak tangent offset). Rows are placed by marching outwards from the pivot,
//     one strip at a time, so a pivot in the middle of the image bends both halves. With bend = sway = 0 the rows lie on the straight axis and the part is exactly the plain quad.
//     Pure maths, no Unity and no allocation: the caller owns the arrays.
// VI: Hình học của part uốn được (đuôi, áo choàng, xúc tu): ảnh cắt thành các dải dọc theo một trục, đường tâm là đường cong có góc tiếp tuyến theo công thức trên.
public static class PNVisualBend
{
    // Row k of `segments` (0..segments) sits at s = k * length / segments - pivotAlong. Outputs per row (pivot-local art px, y down): the centre point (cx, cy) and the unit vector
    // across the strip (nx, ny). Vertex = centre + across * (distance from the axis); the caller offsets by the image's own across extent.
    public static void Rows(int segments, float length, float pivotAlong, bool axisX, float bend, float sway, float[] cx, float[] cy, float[] nx, float[] ny)
    {
        if (segments < 1 || segments > PNVisualLimits.MaxSegments) throw new ArgumentOutOfRangeException(nameof(segments));
        if (!(length > 0f) || float.IsInfinity(length)) throw new ArgumentOutOfRangeException(nameof(length));
        int rows = segments + 1;
        if (cx.Length < rows || cy.Length < rows || nx.Length < rows || ny.Length < rows) throw new ArgumentException("row arrays are too small.");
        float step = length / segments;
        // the first row at or after the pivot; everything before it marches backwards
        int first = rows; for (int k = 0; k < rows; k++) if (k * step - pivotAlong >= 0f) { first = k; break; }
        float px = 0f, py = 0f, prev = 0f;
        for (int k = first; k < rows; k++)
        {
            float s = k * step - pivotAlong; March(s - prev, (s + prev) * .5f, length, axisX, bend, sway, ref px, ref py); prev = s;
            cx[k] = px; cy[k] = py; Across(Angle(s, length, bend, sway), axisX, out nx[k], out ny[k]);
        }
        px = 0f; py = 0f; prev = 0f;
        for (int k = first - 1; k >= 0; k--)
        {
            float s = k * step - pivotAlong; March(s - prev, (s + prev) * .5f, length, axisX, bend, sway, ref px, ref py); prev = s;
            cx[k] = px; cy[k] = py; Across(Angle(s, length, bend, sway), axisX, out nx[k], out ny[k]);
        }
    }

    // Tangent turn in degrees at distance s from the pivot.
    public static float Angle(float s, float length, float bend, float sway)
        => bend * s / length + sway * (float)Math.Sin(2.0 * Math.PI * s / length);

    // One march of `ds` along the tangent at the middle of the step. Rotation uses the same convention as the Rot track (positive = clockwise on a y-down canvas).
    private static void March(float ds, float mid, float length, bool axisX, float bend, float sway, ref float x, ref float y)
    {
        double rad = Angle(mid, length, bend, sway) * Math.PI / 180.0; float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
        if (axisX) { x += cos * ds; y += sin * ds; } else { x += -sin * ds; y += cos * ds; }
    }

    private static void Across(float degrees, bool axisX, out float nx, out float ny)
    {
        double rad = degrees * Math.PI / 180.0; float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
        if (axisX) { nx = -sin; ny = cos; } else { nx = cos; ny = sin; }
    }
}
