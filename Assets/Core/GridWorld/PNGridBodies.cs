using System;

// EN: The cells a body covers and the nearest place where one fits (Docs/SandboxPlan.md, step G8b). A body is a disc of `Radius` metres; the cells it covers are the ones that disc touches, taken as the square around
//     its centre. When a wall is built or blasted onto a body, the live world moves the body to the nearest spot whose cells are all free. Pure C#: the Unity side only reads positions and moves bodies.
// VI: Các ô mà một thân chiếm và chỗ gần nhất nó đứng vừa (G8b). Thân là đĩa bán kính `Radius` mét; ô nó chiếm là hình vuông quanh tâm. Khi tường mọc đè lên thân, world đưa thân tới chỗ trống gần nhất.
public static class PNGridBodies
{
    public const float Radius = .4f;
    // The farthest a body is moved, in cells (8 m on a 0.5 m cell).
    public const int SearchRadius = 16;

    public static PNGridRegion Footprint(PNGridWorldData data, float x, float z)
    {
        float cell = data.CellSize;
        return new PNGridRegion((int)Math.Floor((x - Radius) / cell), (int)Math.Floor((z - Radius) / cell), (int)Math.Floor((x + Radius) / cell), (int)Math.Floor((z + Radius) / cell));
    }

    // True when no cell of the footprint is solid (the edge of the world counts as solid).
    public static bool FootprintFree(PNGridWorldData data, float x, float z)
    {
        var f = Footprint(data, x, z);
        for (int cz = f.Z0; cz <= f.Z1; cz++) for (int cx = f.X0; cx <= f.X1; cx++) if (data.IsSolid(cx, cz)) return false;
        return true;
    }

    public static bool Overlaps(PNGridRegion a, PNGridRegion b) => a.X0 <= b.X1 && a.X1 >= b.X0 && a.Z0 <= b.Z1 && a.Z1 >= b.Z0;

    // The centre of the nearest cell, within SearchRadius cells of (x, z), where a body fits; false when there is none.
    public static bool FindFreeSpot(PNGridWorldData data, float x, float z, out float spotX, out float spotZ)
    {
        spotX = x; spotZ = z; float cell = data.CellSize; int cx = (int)Math.Floor(x / cell), cz = (int)Math.Floor(z / cell);
        float best = float.MaxValue; bool found = false;
        for (int ring = 0; ring <= SearchRadius; ring++)
        {
            // Every cell of this ring is at least (ring - 1) cells away: once something closer than that has been found, the farther rings cannot beat it.
            if (found && (ring - 1) * cell > (float)Math.Sqrt(best)) break;
            for (int dz = -ring; dz <= ring; dz++) for (int dx = -ring; dx <= ring; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue;
                float px = (cx + dx + .5f) * cell, pz = (cz + dz + .5f) * cell;
                if (!FootprintFree(data, px, pz)) continue;
                float d = (px - x) * (px - x) + (pz - z) * (pz - z);
                if (d < best) { best = d; spotX = px; spotZ = pz; found = true; }
            }
        }
        return found;
    }
}
