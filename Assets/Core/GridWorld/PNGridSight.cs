using System;

// EN: Line of sight over the cells (Docs/SandboxPlan.md, step G5): a ray between two flat points is clear when none of the cells it passes through is solid. It replaces the fixed `Wall` layer raycast in a
//     grid world, so what blocks the sight is what the cells say (the same cells that build the wall mesh, the colliders and the navigation). The cell that holds the start point is not tested (a body standing
//     against a wall sees along it, exactly as a physics ray that starts inside a collider does not hit it); the cell that holds the end point is (a target inside a wall is not seen). A solid cell is one that
//     blocks the way; a separate "blocks sight" flag per cell does not exist yet, so every solid cell blocks the sight. Height is ignored: grid worlds are flat.
// VI: Tầm nhìn theo ô: tia giữa hai điểm phẳng thông thoáng khi không ô nào nó đi qua là ô đặc. Thay raycast layer `Wall` cố định trong world lưới. Ô chứa điểm đầu không bị kiểm; ô chứa điểm cuối thì có.
public static class PNGridSight
{
    public const int MaxSteps = 4096;

    public static bool Clear(PNGridWorldData data, float fromX, float fromZ, float toX, float toZ)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (float.IsNaN(fromX) || float.IsNaN(fromZ) || float.IsNaN(toX) || float.IsNaN(toZ) || float.IsInfinity(fromX) || float.IsInfinity(fromZ) || float.IsInfinity(toX) || float.IsInfinity(toZ)) return false;
        float cell = data.CellSize;
        float px = fromX / cell, pz = fromZ / cell, qx = toX / cell, qz = toZ / cell;
        int x = (int)Math.Floor(px), z = (int)Math.Floor(pz), endX = (int)Math.Floor(qx), endZ = (int)Math.Floor(qz);
        if (x == endX && z == endZ) return true;   // same cell: nothing between
        float dx = qx - px, dz = qz - pz;
        int stepX = dx > 0 ? 1 : -1, stepZ = dz > 0 ? 1 : -1;
        // Parametric distance (0..1 along the ray) to the next vertical / horizontal cell border, and between two borders.
        float tMaxX = dx == 0 ? float.PositiveInfinity : ((dx > 0 ? x + 1 : x) - px) / dx;
        float tMaxZ = dz == 0 ? float.PositiveInfinity : ((dz > 0 ? z + 1 : z) - pz) / dz;
        float tDeltaX = dx == 0 ? float.PositiveInfinity : stepX / dx, tDeltaZ = dz == 0 ? float.PositiveInfinity : stepZ / dz;
        for (int step = 0; step < MaxSteps; step++)
        {
            if (tMaxX < tMaxZ) { x += stepX; tMaxX += tDeltaX; }
            else if (tMaxZ < tMaxX) { z += stepZ; tMaxZ += tDeltaZ; }
            else { x += stepX; z += stepZ; tMaxX += tDeltaX; tMaxZ += tDeltaZ; }   // through a corner: both cells change at once
            if (data.IsSolid(x, z)) return false;
            if (x == endX && z == endZ) return true;
        }
        return false;
    }
}
