using System;

// Pure geometry and choices of the Starforger's skills (no Unity types, no physics), so the smoke suite can check them: the three star slots around the owner, which slot a new star takes,
// when a slot's first shot comes, and how far the jump goes. Callers measure walls themselves and pass the free length.
public static class PNStarforgerPlanner
{
    public struct Point { public float X, Z; public Point(float x, float z) { X = x; Z = z; } }

    // Slot k sits k * `step` degrees around the owner (+x toward +z), `radius` away.
    public static Point SlotOffset(int slot, float radius, float stepDegrees = 120f)
    {
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        double a = slot * stepDegrees * Math.PI / 180d;
        return new Point((float)(Math.Cos(a) * radius), (float)(Math.Sin(a) * radius));
    }

    // The first slot not in use, or -1 when every one of `maxStars` slots is taken.
    public static int FirstFreeSlot(bool[] used, int maxStars)
    {
        if (used == null) throw new ArgumentNullException(nameof(used));
        for (int i = 0; i < maxStars; i++) if (i >= used.Length || !used[i]) return i;
        return -1;
    }

    // The first shot of a star in `slot`: `first` seconds after it appeared plus the slot's stagger, so the stars do not fire together.
    public static float FirstShot(float first, float stagger, int slot) => first + slot * stagger;

    // The jump goes to the target's spot but never farther than `maxRange` and never into a wall: `free` is the clear length measured along the jump (a body-width margin is kept).
    // Returns 0 when there is no room at all.
    public static float JumpDistance(float toTarget, float maxRange, float free, float margin = .15f)
    {
        float distance = Math.Min(toTarget, maxRange);
        return Math.Max(0f, Math.Min(distance, free - margin));
    }

    // The jump is worth taking only when it lands somewhere: it must cover at least `minimum` (so a target at the feet is hit by a short hop, not skipped).
    public static bool CanJump(float distance, float minimum = .05f) => distance >= minimum;
}
