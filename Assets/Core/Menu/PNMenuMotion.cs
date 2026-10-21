using System;

// EN: Timing and easing of the menu screens, copied from the pause menu (Docs/ModeMenuPlan.md): items rise 16 px on curve (.22,.72,.18,1)
//     over 0.56 s with alpha t*1.8 and a 0.07 s stagger; they leave in 0.26 s with a 10 px lift and scale .92, reverse stagger 0.045 s;
//     the surface fades in over 0.3 s and out from 0.308 s over 0.252 s (total 0.58 s). Pure maths so it can be tested without Unity.
// VI: Thời gian và đường cong chuyển động của menu, sao chép từ pause; hàm thuần để kiểm thử.
public static class PNMenuMotion
{
    public const float EnterDuration = .56f, EnterStagger = .07f, FirstEnterDelay = .26f, EnterRise = 16f;
    public const float ExitDuration = .26f, ExitStagger = .045f, ExitLift = 10f, ExitScale = .92f;
    public const float SurfaceFadeIn = .3f, SurfaceFadeOutStart = .308f, SurfaceFadeOutDuration = .252f, CloseTotal = .58f;

    public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    public static float SmoothStep(float t) { t = Clamp01(t); return t * t * (3 - 2 * t); }

    private static float Bezier(float t, float a, float b) => 3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;

    // CSS-style cubic-bezier easing: y for a given x (time).
    public static float Curve(float t, float x1, float y1, float x2, float y2)
    {
        t = Clamp01(t); float lo = 0, hi = 1;
        for (int i = 0; i < 12; i++) { float m = (lo + hi) * .5f; if (Bezier(m, x1, x2) < t) lo = m; else hi = m; }
        return Bezier((lo + hi) * .5f, y1, y2);
    }

    public static float EnterDelay(int index, float baseDelay) => baseDelay + index * EnterStagger;
    public static float ExitDelay(int index, int count) => (count - 1 - index) * ExitStagger;
    public static float EnterTotal(int count, float baseDelay) => count <= 0 ? 0 : EnterDelay(count - 1, baseDelay) + EnterDuration;
    public static float ExitTotal(int count) => count <= 0 ? 0 : ExitDelay(0, count) + ExitDuration;

    // Item entering: alpha 0 -> 1 (t*1.8, clamped) and a rise from +16 px to 0.
    public static void Enter(float elapsed, float delay, out float alpha, out float y)
    {
        float t = Clamp01((elapsed - delay) / EnterDuration), eased = Curve(t, .22f, .72f, .18f, 1f);
        alpha = Clamp01(t * 1.8f); y = EnterRise * (1 - eased);
    }

    // Item leaving, starting from the values it had when the exit began.
    public static void Exit(float elapsed, float delay, float fromAlpha, float fromY, float fromScale, out float alpha, out float y, out float scale)
    {
        float t = Clamp01((elapsed - delay) / ExitDuration), p = SmoothStep(t);
        scale = fromScale + (fromScale * ExitScale - fromScale) * p;
        alpha = fromAlpha * (1 - Clamp01(t * 1.3f));
        y = fromY + (-ExitLift - fromY) * p;
    }

    // Background orbit (ring 0 = the small one with the travelling star, ring 1 = the large one): grows in from a flat sliver over 0.76 s
    // (delays .09 / .17 s) and collapses out through x .85 / y .12 to a sliver (delays .06 / .03 s, 0.56 s in total), as in the pause menu.
    public const float OrbitInDuration = .76f, OrbitOutTotal = .56f;
    public static void Orbit(bool entering, float elapsed, int ring, out float scaleX, out float scaleY, out float alpha)
    {
        if (ring < 0 || ring > 1) throw new ArgumentOutOfRangeException(nameof(ring));
        if (entering)
        {
            float t = Clamp01((elapsed - (ring == 0 ? .09f : .17f)) / OrbitInDuration), e = Curve(t, .22f, .72f, .18f, 1f);
            float fx = ring == 0 ? .16f : .12f, fy = ring == 0 ? .04f : .03f;
            scaleX = fx + (1 - fx) * e; scaleY = fy + (1 - fy) * e; alpha = Clamp01(t * 1.8f);
        }
        else
        {
            float delay = ring == 0 ? .06f : .03f, p = Curve((elapsed - delay) / (OrbitOutTotal - delay), .55f, 0f, .25f, 1f);
            scaleX = p < .4f ? 1 + (.85f - 1) * (p / .4f) : .85f + (.04f - .85f) * ((p - .4f) / .6f);
            scaleY = p < .4f ? 1 + (.12f - 1) * (p / .4f) : .12f + (.015f - .12f) * ((p - .4f) / .6f);
            alpha = 1 - SmoothStep((p - .76f) / .24f);
        }
    }

    // Dial background of the character screen: grows from .23 over 0.76 s after 0.23 s (the pause dial), leaves like the large orbit.
    public static void Dial(bool entering, float elapsed, out float scaleX, out float scaleY, out float alpha)
    {
        if (!entering) { Orbit(false, elapsed, 1, out scaleX, out scaleY, out alpha); return; }
        float t = Clamp01((elapsed - .23f) / OrbitInDuration), e = Curve(t, .22f, .72f, .18f, 1f);
        scaleX = scaleY = .23f + (1 - .23f) * e; alpha = Clamp01(t * 1.8f);
    }

    public static float SurfaceIn(float elapsed) => Clamp01(elapsed / SurfaceFadeIn);
    public static float SurfaceOut(float fromAlpha, float elapsed) => fromAlpha * (1 - SmoothStep((elapsed - SurfaceFadeOutStart) / SurfaceFadeOutDuration));
}
