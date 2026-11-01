using System;

// EN: The spring chain of Docs/VisualRigRuntimeImplementationPlan.md R1, pure maths (no Unity, no allocation). One link = the end of a bone that trails the bone's animated pose like a mass on a
//     spring: it keeps its distance from the joint (Verlet, a fixed step of 1/120 s, at most 8 steps a frame), is pulled to the animated end (stiffness), slowed (damping), dragged down (gravity),
//     left behind when the whole body moves (inertia) and may not leave the animated direction by more than `limit` degrees. The result is an angle: the signed turn from the animated direction
//     to the trailing one, in degrees, positive = clockwise on the y-down canvas (the same convention as the Rot track). Deterministic: the same inputs and the same dt sequence give the same numbers.
//     Space: unmirrored art px, y down. A gap longer than MaxGap seconds, a teleport or a first call puts the end back on its animated place.
// VI: Mot mat xich lo xo cua xuong: dau mut cua xuong bi tre sau tu the chuyen dong. Toan thuan, khong cap phat.
public struct PNVisualSpring
{
    public float TipX, TipY, PrevX, PrevY, LastPx, LastPy, LastTx, LastTy, Theta; public bool Ready;
}

public static class PNVisualDynamics
{
    public const float Step = 1f / 120f, MaxGap = .25f; public const int MaxSteps = 8;
    private const float Rad = (float)(Math.PI / 180.0), Deg = (float)(180.0 / Math.PI);

    // P = the joint (px), T = where the end is in the animated pose (px), dt = seconds since the last evaluation, bodyDx/Dy = how far the body moved meanwhile (px, y down).
    public static float Advance(ref PNVisualSpring s, PNVisualDynamicsEntry e, float px, float py, float tx, float ty, float dt, float bodyDx, float bodyDy, bool reset)
    {
        float length = (float)Math.Sqrt((tx - px) * (tx - px) + (ty - py) * (ty - py));
        if (!(length > 1e-4f)) { s.Theta = 0f; return 0f; }
        if (reset || !s.Ready || dt > MaxGap)
        {
            s.TipX = tx; s.TipY = ty; s.PrevX = tx; s.PrevY = ty; s.LastPx = px; s.LastPy = py; s.LastTx = tx; s.LastTy = ty; s.Ready = true; s.Theta = 0f; return 0f;
        }
        if (dt > 0f)
        {
            float shiftX = -bodyDx * e.Inertia, shiftY = -bodyDy * e.Inertia;                     // the body moved, the end stays where it was in the world
            s.TipX += shiftX; s.TipY += shiftY; s.PrevX += shiftX; s.PrevY += shiftY;
            int n = (int)Math.Ceiling(dt / Step); if (n < 1) n = 1; if (n > MaxSteps) n = MaxSteps; float h = dt / n, keep = 1f - Math.Min(1f, e.Damping * h), limit = e.Limit * Rad;
            for (int k = 1; k <= n; k++)
            {
                float u = (float)k / n, pkx = s.LastPx + (px - s.LastPx) * u, pky = s.LastPy + (py - s.LastPy) * u, tkx = s.LastTx + (tx - s.LastTx) * u, tky = s.LastTy + (ty - s.LastTy) * u;
                float vx = (s.TipX - s.PrevX) * keep, vy = (s.TipY - s.PrevY) * keep;
                float ax = e.Stiffness * (tkx - s.TipX), ay = e.Stiffness * (tky - s.TipY) + e.Gravity;
                float nx = s.TipX + vx + ax * h * h, ny = s.TipY + vy + ay * h * h;
                float rest = (float)Math.Sqrt((tkx - pkx) * (tkx - pkx) + (tky - pky) * (tky - pky));
                float dx = nx - pkx, dy = ny - pky, d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d < 1e-4f) { dx = tkx - pkx; dy = tky - pky; d = rest; }
                float angle = (float)Math.Atan2(dy, dx), animated = (float)Math.Atan2(tky - pky, tkx - pkx), turn = Wrap(angle - animated);
                if (turn > limit) turn = limit; else if (turn < -limit) turn = -limit;
                angle = animated + turn;
                s.PrevX = s.TipX; s.PrevY = s.TipY; s.TipX = pkx + (float)Math.Cos(angle) * rest; s.TipY = pky + (float)Math.Sin(angle) * rest;      // the end keeps its distance from the joint
            }
            s.LastPx = px; s.LastPy = py; s.LastTx = tx; s.LastTy = ty;
        }
        s.Theta = Wrap((float)Math.Atan2(s.TipY - py, s.TipX - px) - (float)Math.Atan2(ty - py, tx - px)) * Deg;
        return s.Theta;
    }

    // An angle in radians brought into (-pi, pi].
    public static float Wrap(float radians)
    {
        const float Pi = (float)Math.PI;
        while (radians > Pi) radians -= 2f * Pi; while (radians <= -Pi) radians += 2f * Pi; return radians;
    }
}
