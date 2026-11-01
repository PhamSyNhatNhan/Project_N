using System;

// EN: The inverse kinematics of Docs/VisualRigRuntimeImplementationPlan.md R2, pure maths (no Unity, no allocation): one bone that turns to point its tip at a goal, or two bones (an arm, a leg, a
//     tentacle's first links) whose angles are solved with the law of cosines so the tip of the second reaches the goal. Space: art px, y down; angles in degrees, positive = clockwise on the y-down canvas
//     (the same convention as the Rot track). The solver returns the turn to ADD to each bone's own rotation; a goal out of reach stretches the chain straight towards it (no NaN, no flip).
// VI: IK cho mot hoac hai xuong: tinh goc can cong them vao xoay cua xuong de dau mut cham dich. Toan thuan.
public static class PNVisualIk
{
    private const float Deg = (float)(180.0 / Math.PI);

    // A bone at (ax, ay) whose tip is now at (tx, ty): the turn that makes it point at the goal (gx, gy).
    public static float OneBone(float ax, float ay, float tx, float ty, float gx, float gy)
    {
        if (!(Dist(ax, ay, tx, ty) > 1e-4f) || !(Dist(ax, ay, gx, gy) > 1e-4f)) return 0f;
        return PNVisualDynamics.Wrap((float)Math.Atan2(gy - ay, gx - ax) - (float)Math.Atan2(ty - ay, tx - ax)) * Deg;
    }

    // Two bones: the first from a to the joint b, the second from b to its tip t (all in the current pose); the goal g. bendPositive: the second bone turns clockwise relative to the first (the joint
    // sits on the counter-clockwise side of the line a-g). Returns false (and zero turns) when a bone has no length.
    public static bool TwoBone(float ax, float ay, float bx, float by, float tx, float ty, float gx, float gy, bool bendPositive, out float turnA, out float turnB)
    {
        turnA = turnB = 0f;
        float l1 = Dist(ax, ay, bx, by), l2 = Dist(bx, by, tx, ty);
        if (!(l1 > 1e-4f) || !(l2 > 1e-4f)) return false;
        float dx = gx - ax, dy = gy - ay, dist = (float)Math.Sqrt(dx * dx + dy * dy);
        float min = Math.Abs(l1 - l2) + 1e-3f, max = (l1 + l2) * .999999f, d = dist < min ? min : dist > max ? max : dist;
        float phi = dist > 1e-5f ? (float)Math.Atan2(dy, dx) : (float)Math.Atan2(by - ay, bx - ax);
        float cosA = (l1 * l1 + d * d - l2 * l2) / (2f * l1 * d); if (cosA > 1f) cosA = 1f; else if (cosA < -1f) cosA = -1f;
        float alpha = (float)Math.Acos(cosA);
        float want = phi + (bendPositive ? -alpha : alpha), current = (float)Math.Atan2(by - ay, bx - ax), da = PNVisualDynamics.Wrap(want - current);
        float cs = (float)Math.Cos(da), sn = (float)Math.Sin(da);
        float bx2 = ax + (bx - ax) * cs - (by - ay) * sn, by2 = ay + (bx - ax) * sn + (by - ay) * cs, tx2 = ax + (tx - ax) * cs - (ty - ay) * sn, ty2 = ay + (tx - ax) * sn + (ty - ay) * cs;
        float gxx = ax + (float)Math.Cos(phi) * d, gyy = ay + (float)Math.Sin(phi) * d;
        float db = PNVisualDynamics.Wrap((float)Math.Atan2(gyy - by2, gxx - bx2) - (float)Math.Atan2(ty2 - by2, tx2 - bx2));
        turnA = da * Deg; turnB = db * Deg; return true;
    }

    private static float Dist(float ax, float ay, float bx, float by) => (float)Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
}
