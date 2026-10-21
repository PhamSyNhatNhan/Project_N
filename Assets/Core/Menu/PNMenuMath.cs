using System;

// EN: Layout maths of the mode ring (Docs/Mockups/ModeMenuDemo.html v20): N gates on a circle seen from the front, the front one largest,
//     at most five visible (the front, two on each side), the ring turning without ends. `d` is a gate's signed distance from the front
//     in steps. Pure maths so it can be tested without Unity.
// VI: Bố cục vòng thẻ chế độ: N cổng trên một vòng nhìn từ phía trước, tối đa 5 thẻ hiện, quay không có đầu cuối.
public static class PNMenuMath
{
    public const float Step = .62f;               // radians between neighbours
    public const float RadiusInWidths = 1.9f;     // ring radius in gate widths
    public const float RiseOfStage = .1f;         // back gates sit this fraction of the stage height higher
    public const float FullVisibility = 2f, FadeOut = 2.6f;
    public const float SettleRate = 13f;

    public struct Slot
    {
        public float X;          // in gate widths, from the ring centre
        public float Rise;       // 0 (front) .. 1 (back) fraction of RiseOfStage
        public float Scale;
        public float Opacity;
        public float Depth;      // 0..1, 1 = front
        public float Front;      // depth^3, drives art saturation, dimming and glow
        public float Visibility; // 0..1, 0 = hidden and not clickable
        public int Z;
    }

    // Signed distance of gate `index` from position `pos`, wrapped into (-n/2, n/2].
    public static float Wrap(float d, int n)
    {
        if (n < 1) throw new ArgumentOutOfRangeException(nameof(n));
        return d - (float)Math.Round(d / n, MidpointRounding.AwayFromZero) * n;
    }

    public static int Mod(int i, int n) => ((i % n) + n) % n;

    public static Slot Layout(float index, float pos, int n)
    {
        float d = Wrap(index - pos, n), phi = d * Step, cs = (float)Math.Cos(phi), sn = (float)Math.Sin(phi), depth = (cs + 1) / 2;
        float vis = Math.Max(0, Math.Min(1, (FadeOut - Math.Abs(d)) / (FadeOut - FullVisibility)));
        return new Slot
        {
            X = RadiusInWidths * sn, Rise = (1 - cs) / 2, Scale = .5f + .6f * (float)Math.Pow(depth, 5), Depth = depth, Front = (float)Math.Pow(depth, 3),
            Opacity = (.6f + .4f * depth) * vis, Visibility = vis, Z = (int)Math.Round(depth * 100),
        };
    }

    // Pixels one step moves the ring by when dragging.
    public static float DragSpacing(float gateWidth) => gateWidth * RadiusInWidths * (float)Math.Sin(Step);

    // Exponential approach of the position to the integer target (frame-rate independent).
    public static float Settle(float pos, float target, float dt)
    {
        pos += (target - pos) * (1 - (float)Math.Exp(-dt * SettleRate));
        return Math.Abs(target - pos) < .0005f ? target : pos;
    }

    // Where a release at `pos` with velocity `v` (steps per second) comes to rest.
    public static int Fling(float pos, float v) => (int)Math.Round(pos + v * .18f, MidpointRounding.AwayFromZero);

    // The target after a click on gate `index` while the ring rests at `target`: 0 = it is the front gate (pick it), otherwise the shortest turn.
    public static int TurnFor(int index, int target, int n) => (int)Math.Round(Wrap(index - target, n), MidpointRounding.AwayFromZero);

    // Colour in between two accents for a fractional ring position (the aura follows the gate in front).
    public static void Mix(int[] a, int[] b, float t, int[] result)
    { for (int i = 0; i < 3; i++) result[i] = (int)Math.Round(a[i] + (b[i] - a[i]) * Math.Max(0, Math.Min(1, t))); }
}
