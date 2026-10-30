using System;

// EN: Pure maths and state of one puppet string (no Unity types, so the smoke suite can run it). PNVisualStringParams is a spec flattened to numbers so two of them can be blended
//     (a colour change, a shape change); PNVisualStringPath samples the line in a plane (x right, y up, metres, origin at the tie); PNVisualStringState is the little state machine
//     Hidden / Lowering / Attached / Cut with the blend towards a new target. Nothing here allocates after construction.
// VI: Toán và trạng thái thuần của một sợi dây: thông số làm phẳng để pha trộn, lấy mẫu đường dây trong mặt phẳng, và máy trạng thái Hidden / Lowering / Attached / Cut.

public struct PNVisualStringParams
{
    public float LeaveX, LeaveY, LeaveDistance, Curve, Slack, SwayAmplitude, SwayFrequency, WidthRoot, WidthTop, Length, OffsetX, OffsetY, Lean, Alpha, FadeTop;
    public PNVisualColor Core, Halo, Knot; public int Segments;
    public static PNVisualStringParams From(PNVisualStringSpec s)
    {
        var sh = s.Shape; var c = s.Color;
        return new PNVisualStringParams
        {
            LeaveX = sh.LeaveX, LeaveY = sh.LeaveY, LeaveDistance = sh.LeaveDistance, Curve = sh.Curve, Slack = sh.Slack, SwayAmplitude = sh.SwayAmplitude, SwayFrequency = sh.SwayFrequency,
            WidthRoot = sh.WidthRoot, WidthTop = sh.WidthTop, Length = sh.Length, Segments = sh.Segments, OffsetX = s.OffsetX, OffsetY = s.OffsetY, Lean = s.Lean,
            Core = c.Core, Halo = c.Halo, Knot = c.Knot, Alpha = c.Alpha, FadeTop = c.FadeTop,
        };
    }
    private static float L(float a, float b, float t) => a + (b - a) * t;
    private static PNVisualColor L(PNVisualColor a, PNVisualColor b, float t) => new PNVisualColor(L(a.R, b.R, t), L(a.G, b.G, t), L(a.B, b.B, t), L(a.A, b.A, t));
    // Every number moves towards `b`; the segment count and the leave direction jump to the target's at the half way point (the count cannot be in between).
    public static PNVisualStringParams Lerp(in PNVisualStringParams a, in PNVisualStringParams b, float t)
    {
        if (t <= 0f) return a; if (t >= 1f) return b;
        float lx = L(a.LeaveX, b.LeaveX, t), ly = L(a.LeaveY, b.LeaveY, t), n = (float)Math.Sqrt(lx * lx + ly * ly); if (n < 1e-4f) { lx = b.LeaveX; ly = b.LeaveY; n = 1f; }
        return new PNVisualStringParams
        {
            LeaveX = lx / n, LeaveY = ly / n, LeaveDistance = L(a.LeaveDistance, b.LeaveDistance, t), Curve = L(a.Curve, b.Curve, t), Slack = L(a.Slack, b.Slack, t),
            SwayAmplitude = L(a.SwayAmplitude, b.SwayAmplitude, t), SwayFrequency = L(a.SwayFrequency, b.SwayFrequency, t), WidthRoot = L(a.WidthRoot, b.WidthRoot, t), WidthTop = L(a.WidthTop, b.WidthTop, t),
            Length = L(a.Length, b.Length, t), OffsetX = L(a.OffsetX, b.OffsetX, t), OffsetY = L(a.OffsetY, b.OffsetY, t), Lean = L(a.Lean, b.Lean, t), Alpha = L(a.Alpha, b.Alpha, t), FadeTop = L(a.FadeTop, b.FadeTop, t),
            Core = L(a.Core, b.Core, t), Halo = L(a.Halo, b.Halo, t), Knot = L(a.Knot, b.Knot, t), Segments = t < .5f ? a.Segments : b.Segments,
        };
    }
}

public static class PNVisualStringPath
{
    public const int MaxRows = PNVisualStringLimits.MaxSegments + 1;
    // Samples the part of the line between t0 and t1 (0 = the tie, 1 = the top) into `rows` points (rows >= 2). `height` is the distance up to the top of the view (used when the shape has no fixed length);
    // `left` mirrors the leave direction, the lean, the offset and the bow. Returns the number of points written. Allocation free.
    public static int Sample(in PNVisualStringParams p, float height, bool left, float time, float seed, float t0, float t1, int rows, float[] x, float[] y, float[] width)
    {
        if (rows < 2) rows = 2; if (rows > MaxRows) rows = MaxRows;
        float side = left ? -1f : 1f, h = p.Length > 0f ? p.Length : Math.Max(height, .05f);
        float ox = p.OffsetX * side, oy = p.OffsetY;
        float lx = p.LeaveX * side, ly = p.LeaveY;
        float p1x = lx * (p.LeaveDistance * 2f + h * .18f), p1y = ly * (p.LeaveDistance * 2f + h * .18f);
        float p3x = p.Lean * side * h * .15f, p3y = h, p2x = p3x, p2y = p3y - h * .35f;
        for (int k = 0; k < rows; k++)
        {
            float t = t0 + (t1 - t0) * k / (rows - 1), u = 1f - t;
            float b0 = u * u * u, b1 = 3f * u * u * t, b2 = 3f * u * t * t, b3 = t * t * t;
            float px = b1 * p1x + b2 * p2x + b3 * p3x, py = b1 * p1y + b2 * p2y + b3 * p3y;
            float sine = (float)Math.Sin(Math.PI * t);
            px += p.Curve * h * .06f * sine * side;                                   // a bow
            px += p.Slack * (float)Math.Sin(Math.PI * 2.0 * t * 1.5) * (1f - t) * side;          // a little slack
            px += p.SwayAmplitude * (float)Math.Sin(2.0 * Math.PI * p.SwayFrequency * time + seed + t * 4.0) * t * side;   // a sway that grows towards the top
            x[k] = ox + px; y[k] = oy + py; width[k] = p.WidthRoot + (p.WidthTop - p.WidthRoot) * t;
        }
        return rows;
    }
}

public enum PNVisualStringPhase { Hidden, Lowering, Attached, Cut }

public sealed class PNVisualStringState
{
    public const float DefaultLowerSeconds = .45f, CutSeconds = .6f;
    public bool InUse { get; private set; }
    public PNVisualStringPhase Phase { get; private set; } = PNVisualStringPhase.Hidden;
    public float PhaseTime { get; private set; }
    public float PhaseDuration { get; private set; } = 1f;
    public string Socket { get; private set; } = "";
    public PNVisualStringParams Current { get; private set; }
    public float Seed { get; private set; }
    public float Blend { get; private set; } = 1f;
    private PNVisualStringParams from, to; private float blendTime, blendDuration;

    public void Begin(PNVisualStringSpec spec, float seed, PNVisualStringPhase phase)
    {
        InUse = true; Socket = spec.Socket; to = from = Current = PNVisualStringParams.From(spec); Blend = 1f; blendTime = blendDuration = 0f; Seed = seed; SetPhase(phase, 1f);
    }
    public void Release() { InUse = false; Phase = PNVisualStringPhase.Hidden; PhaseTime = 0f; }
    private void SetPhase(PNVisualStringPhase phase, float duration) { Phase = phase; PhaseTime = 0f; PhaseDuration = duration > 0f ? duration : 1e-4f; }
    // Retargets towards a new spec; `seconds` 0 jumps. The socket changes at once.
    public void Retarget(PNVisualStringSpec spec, float seconds)
    {
        var target = PNVisualStringParams.From(spec); Socket = spec.Socket;
        if (seconds <= 0f) { to = from = Current = target; Blend = 1f; blendDuration = 0f; return; }
        from = Current; to = target; Blend = 0f; blendTime = 0f; blendDuration = seconds;
    }
    public void Retarget(in PNVisualStringParams target, float seconds)
    {
        if (seconds <= 0f) { to = from = Current = target; Blend = 1f; blendDuration = 0f; return; }
        from = Current; to = target; Blend = 0f; blendTime = 0f; blendDuration = seconds;
    }
    public PNVisualStringParams Target => to;
    public void Show() { if (InUse) SetPhase(PNVisualStringPhase.Attached, 1f); }
    public void Lower(float seconds) { if (InUse) SetPhase(PNVisualStringPhase.Lowering, seconds <= 0f ? DefaultLowerSeconds : seconds); }
    public void Cut() { if (InUse && Phase != PNVisualStringPhase.Hidden && Phase != PNVisualStringPhase.Cut) SetPhase(PNVisualStringPhase.Cut, CutSeconds); }
    public void Hide() { if (InUse) SetPhase(PNVisualStringPhase.Hidden, 1f); }
    public void Advance(float dt)
    {
        if (!InUse || dt <= 0f) return;
        PhaseTime += dt;
        if (Phase == PNVisualStringPhase.Lowering && PhaseTime >= PhaseDuration) SetPhase(PNVisualStringPhase.Attached, 1f);
        else if (Phase == PNVisualStringPhase.Cut && PhaseTime >= PhaseDuration) SetPhase(PNVisualStringPhase.Hidden, 1f);
        if (Blend < 1f)
        {
            blendTime += dt; Blend = blendDuration <= 0f ? 1f : Math.Min(1f, blendTime / blendDuration);
            float e = Blend * Blend * (3f - 2f * Blend); Current = PNVisualStringParams.Lerp(from, to, e);
            if (Blend >= 1f) { Current = to; from = to; }
        }
    }
    private static float Ease(float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return t * t * (3f - 2f * t); }
    // The visible part of the line (0 = the tie, 1 = the top), an alpha multiplier, and whether the knot shows. Hidden returns false.
    public bool Visible(out float t0, out float t1, out float alpha, out bool knot)
    {
        t0 = 0f; t1 = 1f; alpha = 1f; knot = true;
        switch (Phase)
        {
            case PNVisualStringPhase.Attached: return true;
            case PNVisualStringPhase.Lowering: { float p = Ease(PhaseTime / PhaseDuration); t0 = 1f - p; knot = p >= .999f; alpha = .35f + .65f * p; return p > .001f; }
            case PNVisualStringPhase.Cut: { float p = Ease(PhaseTime / PhaseDuration); t0 = p; knot = false; alpha = 1f - .6f * p; return p < .999f; }
            default: return false;
        }
    }
}
