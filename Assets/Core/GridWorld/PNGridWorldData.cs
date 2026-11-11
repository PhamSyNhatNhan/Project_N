using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Cell data of a grid world (Docs/SandboxPlan.md, step G1). Pure C# on purpose: no Unity, no registry, so the smoke suite checks it. The cells are the single truth of the ground:
//     what is solid, how high the floor is and which ground type lies where. What can be walked on, how much room a cell has and what the mesh looks like are derived from them, never stored twice.
//     A world is `Width x Depth` cells of `CellSize` metres (multiples of one chunk of 32 cells). The ground TYPE is kept on a coarser cell (2 m, so 4x4 navigation cells at 0.5 m).
// VI: Dữ liệu ô của world lưới (SandboxPlan G1). Thuần C#: ô là sự thật duy nhất của nền (đặc, độ cao, loại đất); đi được, khoảng thoáng và mesh đều là dẫn xuất.
public static class PNGridWorldLimits
{
    public const int ChunkCells = 32, MinCells = 32, MaxCells = 2048, ClearanceCap = 8, MaxPalette = 255, MaxMapIdLength = 128;
    public const float MinCellSize = .25f, MaxCellSize = 1f, GroundMeters = 2f, HeightStep = .25f, WallHeight = 2f;
}

// An inclusive rectangle of cells.
public readonly struct PNGridRegion
{
    public readonly int X0, Z0, X1, Z1;
    public PNGridRegion(int x0, int z0, int x1, int z1) { X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; }
    public bool IsValid => X1 >= X0 && Z1 >= Z0;
    public int Width => X1 - X0 + 1;
    public int Depth => Z1 - Z0 + 1;
    public static PNGridRegion Cell(int x, int z) => new PNGridRegion(x, z, x, z);
    public PNGridRegion Expand(int cells) => new PNGridRegion(X0 - cells, Z0 - cells, X1 + cells, Z1 + cells);
    public PNGridRegion Clip(int width, int depth) => new PNGridRegion(Math.Max(X0, 0), Math.Max(Z0, 0), Math.Min(X1, width - 1), Math.Min(Z1, depth - 1));
    public bool Contains(int x, int z) => x >= X0 && x <= X1 && z >= Z0 && z <= Z1;
    public override string ToString() => "[" + X0 + "," + Z0 + " .. " + X1 + "," + Z1 + "]";
}

// What a world is made of before it has any content: size, cell size, which map fills it and a seed. Strict JSON, bounded, no unknown fields (it is what a portable provider keeps in a save).
public sealed class PNGridWorldDescriptor
{
    public int Width { get; }
    public int Depth { get; }
    public float CellSize { get; }
    public string Map { get; }
    public long Seed { get; }

    public PNGridWorldDescriptor(int width, int depth, float cellSize, string map, long seed)
    {
        if (width < PNGridWorldLimits.MinCells || width > PNGridWorldLimits.MaxCells || width % PNGridWorldLimits.ChunkCells != 0 ||
            depth < PNGridWorldLimits.MinCells || depth > PNGridWorldLimits.MaxCells || depth % PNGridWorldLimits.ChunkCells != 0)
            throw new ArgumentException("A grid world is " + PNGridWorldLimits.MinCells + " to " + PNGridWorldLimits.MaxCells + " cells on a side, in whole chunks of " + PNGridWorldLimits.ChunkCells + ".");
        if (float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize < PNGridWorldLimits.MinCellSize || cellSize > PNGridWorldLimits.MaxCellSize)
            throw new ArgumentException("A cell is " + PNGridWorldLimits.MinCellSize + " to " + PNGridWorldLimits.MaxCellSize + " metres.");
        float factor = PNGridWorldLimits.GroundMeters / cellSize;
        if (Math.Abs(factor - Math.Round(factor)) > 1e-4f) throw new ArgumentException("The cell size must divide the ground cell (" + PNGridWorldLimits.GroundMeters + " m) evenly.");
        if (string.IsNullOrEmpty(map) || map.Length > PNGridWorldLimits.MaxMapIdLength || !EveId.TryParse(map, out _)) throw new ArgumentException("A map id of the form namespace:name is required.");
        Width = width; Depth = depth; CellSize = cellSize; Map = map; Seed = seed;
    }

    public JObject ToJson() => new JObject { ["width"] = Width, ["depth"] = Depth, ["cell"] = CellSize, ["map"] = Map, ["seed"] = Seed };

    public static PNGridWorldDescriptor Parse(JToken token)
    {
        if (!(token is JObject obj)) throw new FormatException("A grid world descriptor is a JSON object.");
        foreach (var property in obj.Properties())
            if (property.Name != "width" && property.Name != "depth" && property.Name != "cell" && property.Name != "map" && property.Name != "seed")
                throw new FormatException("Unknown grid world descriptor field: " + property.Name);
        foreach (var required in new[] { "width", "depth", "cell", "map", "seed" })
            if (obj[required] == null || obj[required].Type == JTokenType.Null) throw new FormatException("Grid world descriptor field missing: " + required);
        if (obj["width"].Type != JTokenType.Integer || obj["depth"].Type != JTokenType.Integer || obj["seed"].Type != JTokenType.Integer ||
            (obj["cell"].Type != JTokenType.Float && obj["cell"].Type != JTokenType.Integer) || obj["map"].Type != JTokenType.String)
            throw new FormatException("Grid world descriptor field has the wrong type.");
        try { return new PNGridWorldDescriptor((int)obj["width"], (int)obj["depth"], (float)obj["cell"], (string)obj["map"], (long)obj["seed"]); }
        catch (ArgumentException error) { throw new FormatException(error.Message); }
        catch (OverflowException error) { throw new FormatException(error.Message); }
    }
}

public sealed class PNGridWorldData
{
    public int Width { get; }
    public int Depth { get; }
    public float CellSize { get; }
    public int ChunksX { get; }
    public int ChunksZ { get; }
    // Navigation cells along one side of a ground cell.
    public int GroundFactor { get; }
    public int GroundColumns { get; }
    public int GroundRows { get; }
    public IReadOnlyList<string> Palette { get; }
    public int SolidCount { get; private set; }

    private readonly ushort[] ground;
    private readonly short[] height;
    private readonly byte[] solid;
    private readonly byte[] clearance;
    private readonly int[] chunkVersion;

    public PNGridWorldData(PNGridWorldDescriptor descriptor, IReadOnlyList<string> palette)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (palette == null || palette.Count == 0 || palette.Count > PNGridWorldLimits.MaxPalette) throw new ArgumentException("A world uses 1 to " + PNGridWorldLimits.MaxPalette + " ground types.");
        var copy = new string[palette.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < copy.Length; i++)
        {
            if (string.IsNullOrEmpty(palette[i]) || !seen.Add(palette[i])) throw new ArgumentException("The ground palette needs distinct, non-empty ids.");
            copy[i] = palette[i];
        }
        Palette = Array.AsReadOnly(copy);
        Width = descriptor.Width; Depth = descriptor.Depth; CellSize = descriptor.CellSize;
        ChunksX = Width / PNGridWorldLimits.ChunkCells; ChunksZ = Depth / PNGridWorldLimits.ChunkCells;
        GroundFactor = (int)Math.Round(PNGridWorldLimits.GroundMeters / CellSize);
        GroundColumns = Width / GroundFactor; GroundRows = Depth / GroundFactor;
        ground = new ushort[GroundColumns * GroundRows];
        height = new short[Width * Depth];
        solid = new byte[Width * Depth];
        clearance = new byte[Width * Depth];
        chunkVersion = new int[ChunksX * ChunksZ];
        RecomputeClearance(new PNGridRegion(0, 0, Width - 1, Depth - 1));
    }

    public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Depth;
    private int Index(int x, int z) => z * Width + x;
    private void Require(int x, int z) { if (!InBounds(x, z)) throw new ArgumentOutOfRangeException("cell", "Cell (" + x + "," + z + ") is outside the world."); }

    // ---- ground type (coarse cells) ----
    public ushort GroundCell(int gx, int gz)
    {
        if (gx < 0 || gz < 0 || gx >= GroundColumns || gz >= GroundRows) throw new ArgumentOutOfRangeException("ground cell");
        return ground[gz * GroundColumns + gx];
    }
    public ushort GroundAt(int x, int z) { Require(x, z); return ground[(z / GroundFactor) * GroundColumns + x / GroundFactor]; }
    public void SetGroundCell(int gx, int gz, ushort type)
    {
        if (gx < 0 || gz < 0 || gx >= GroundColumns || gz >= GroundRows) throw new ArgumentOutOfRangeException("ground cell");
        if (type >= Palette.Count) throw new ArgumentOutOfRangeException(nameof(type), "Ground type is not in the palette.");
        int i = gz * GroundColumns + gx; if (ground[i] == type) return;
        ground[i] = type;
        Touch(new PNGridRegion(gx * GroundFactor, gz * GroundFactor, (gx + 1) * GroundFactor - 1, (gz + 1) * GroundFactor - 1));
    }
    public string GroundId(int x, int z) => Palette[GroundAt(x, z)];

    // ---- floor height (steps of PNGridWorldLimits.HeightStep) ----
    public short HeightRaw(int x, int z) { Require(x, z); return height[Index(x, z)]; }
    public float Height(int x, int z) => HeightRaw(x, z) * PNGridWorldLimits.HeightStep;
    public void SetHeightRaw(int x, int z, short raw)
    {
        Require(x, z); int i = Index(x, z); if (height[i] == raw) return;
        height[i] = raw; Touch(PNGridRegion.Cell(x, z));
    }
    // True while every cell is at height 0 (the navigation of the first release only supports a flat floor).
    public bool IsFlat()
    {
        for (int i = 0; i < height.Length; i++) if (height[i] != 0) return false;
        return true;
    }

    // ---- solid cells (a wall, a rock, the footprint of something that blocks) ----
    public bool IsSolid(int x, int z) => InBounds(x, z) ? solid[Index(x, z)] != 0 : true;   // the edge of the world is a wall
    public void SetSolid(int x, int z, bool value)
    {
        Require(x, z); int i = Index(x, z); if ((solid[i] != 0) == value) return;
        solid[i] = value ? (byte)1 : (byte)0; SolidCount += value ? 1 : -1;
        Touch(PNGridRegion.Cell(x, z)); RecomputeClearance(PNGridRegion.Cell(x, z));
    }
    public void FillSolid(PNGridRegion region, bool value)
    {
        var r = region.Clip(Width, Depth); if (!r.IsValid) return;
        bool any = false;
        for (int z = r.Z0; z <= r.Z1; z++) for (int x = r.X0; x <= r.X1; x++)
        {
            int i = Index(x, z); if ((solid[i] != 0) == value) continue;
            solid[i] = value ? (byte)1 : (byte)0; SolidCount += value ? 1 : -1; any = true;
        }
        if (!any) return;
        Touch(r); RecomputeClearance(r);
    }

    // Distance in cells (Chebyshev) from this cell to the nearest solid cell or world edge: 0 on a solid cell, 1 beside a wall, and PNGridWorldLimits.ClearanceCap when there is nothing nearer than that.
    public int Clearance(int x, int z) => InBounds(x, z) ? clearance[Index(x, z)] : 0;

    // Recompute the clearance field around a change. The cells that can change are the changed ones plus `ClearanceCap` around them; to get those right the pass runs over twice that margin.
    public void RecomputeClearance(PNGridRegion changed)
    {
        int cap = PNGridWorldLimits.ClearanceCap;
        var write = changed.Expand(cap).Clip(Width, Depth); var calc = changed.Expand(2 * cap).Clip(Width, Depth);
        if (!write.IsValid || !calc.IsValid) return;
        int w = calc.Width, d = calc.Depth; var buffer = new byte[w * d];
        for (int z = 0; z < d; z++) for (int x = 0; x < w; x++)
        {
            int cx = calc.X0 + x, cz = calc.Z0 + z;
            int edge = Math.Min(Math.Min(cx, cz), Math.Min(Width - 1 - cx, Depth - 1 - cz)) + 1;   // an outside cell is solid, so the border row is 1 away from one
            buffer[z * w + x] = solid[Index(cx, cz)] != 0 ? (byte)0 : (byte)Math.Min(cap, edge);
        }
        for (int z = 0; z < d; z++) for (int x = 0; x < w; x++)
        {
            int best = buffer[z * w + x];
            if (x > 0) best = Math.Min(best, buffer[z * w + x - 1] + 1);
            if (z > 0)
            {
                best = Math.Min(best, buffer[(z - 1) * w + x] + 1);
                if (x > 0) best = Math.Min(best, buffer[(z - 1) * w + x - 1] + 1);
                if (x < w - 1) best = Math.Min(best, buffer[(z - 1) * w + x + 1] + 1);
            }
            buffer[z * w + x] = (byte)Math.Min(best, cap);
        }
        for (int z = d - 1; z >= 0; z--) for (int x = w - 1; x >= 0; x--)
        {
            int best = buffer[z * w + x];
            if (x < w - 1) best = Math.Min(best, buffer[z * w + x + 1] + 1);
            if (z < d - 1)
            {
                best = Math.Min(best, buffer[(z + 1) * w + x] + 1);
                if (x < w - 1) best = Math.Min(best, buffer[(z + 1) * w + x + 1] + 1);
                if (x > 0) best = Math.Min(best, buffer[(z + 1) * w + x - 1] + 1);
            }
            buffer[z * w + x] = (byte)Math.Min(best, cap);
        }
        for (int z = write.Z0; z <= write.Z1; z++) for (int x = write.X0; x <= write.X1; x++)
            clearance[Index(x, z)] = buffer[(z - calc.Z0) * w + (x - calc.X0)];
    }

    // ---- chunks ----
    public int ChunkIndex(int cx, int cz) { if (cx < 0 || cz < 0 || cx >= ChunksX || cz >= ChunksZ) throw new ArgumentOutOfRangeException("chunk"); return cz * ChunksX + cx; }
    public int ChunkCount => chunkVersion.Length;
    public PNGridRegion ChunkRegion(int cx, int cz)
    {
        ChunkIndex(cx, cz);
        return new PNGridRegion(cx * PNGridWorldLimits.ChunkCells, cz * PNGridWorldLimits.ChunkCells, (cx + 1) * PNGridWorldLimits.ChunkCells - 1, (cz + 1) * PNGridWorldLimits.ChunkCells - 1);
    }
    // Every user of the data (the mesh, a save, the network) keeps the version it last used per chunk and compares it with this; nobody has to coordinate who clears a dirty flag.
    public int ChunkVersion(int cx, int cz) => chunkVersion[ChunkIndex(cx, cz)];
    public int ChunkVersionAt(int index) => chunkVersion[index];

    // A change to a cell also changes the chunks beside it (a wall mesh looks at its neighbours), so the region is grown by one cell.
    private void Touch(PNGridRegion region)
    {
        var r = region.Expand(1).Clip(Width, Depth); if (!r.IsValid) return;
        int c = PNGridWorldLimits.ChunkCells;
        for (int cz = r.Z0 / c; cz <= r.Z1 / c; cz++) for (int cx = r.X0 / c; cx <= r.X1 / c; cx++) chunkVersion[cz * ChunksX + cx]++;
    }
}
