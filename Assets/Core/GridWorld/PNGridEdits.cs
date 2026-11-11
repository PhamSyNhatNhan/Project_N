using System;
using System.Collections.Generic;

// EN: The gateway for changing the cells of a grid world (Docs/SandboxPlan.md, step G3). Every change after the world was built goes through here: a batch of cell edits is validated (inside the world, a known ground
//     type, a bounded size and extent), queued, and applied on the authority a limited number of cells per tick. The cells that changed are grouped into SMALL regions (clusters of nearby cells, never one bounding box
//     around everything: a box around two far edits measured 411 ms against 15 ms for the clusters) so the clearance field, the graph and the meshes only look at what changed. Pure C#, no Unity: the smoke suite checks it.
//     Only the authority applies edits; a client sends a command (project_n:grid_place / grid_break) and later receives the result as cells or chunks (step G8).
// VI: Cổng thay đổi ô của world lưới (G3). Mọi thay đổi sau khi world dựng đều qua đây: lô sửa ô được kiểm, xếp hàng, áp ở authority với ngân sách ô mỗi tick; ô đổi được gom thành các vùng NHỎ (cụm ô gần nhau,
//     không một hộp bao chung) để trường khoảng thoáng, đồ thị và mesh chỉ nhìn phần đổi.
public static class PNGridEditLimits
{
    // Cells in one batch, extent of one batch (cells along either side), cells applied per tick, cells waiting in the queue, the side of a clustering tile, and the largest raw height.
    public const int MaxBatchCells = 4096, MaxBatchExtent = 129, MaxCellsPerTick = 1024, MaxQueuedCells = 65536, ClusterTile = 8, MaxHeightRaw = 4000;
}

public enum PNGridEditKind : byte { Solid = 0, Ground = 1, Height = 2 }

// One change of one cell. Solid: Value 0 or 1. Ground: Value is the palette index (it sets the whole 2 m ground cell the navigation cell lies in). Height: Value is the raw height (steps of 0.25 m).
public readonly struct PNGridEdit
{
    public readonly PNGridEditKind Kind;
    public readonly int X, Z, Value;
    public PNGridEdit(PNGridEditKind kind, int x, int z, int value) { Kind = kind; X = x; Z = z; Value = value; }
}

public sealed class PNGridEditBatch
{
    private readonly List<PNGridEdit> edits = new List<PNGridEdit>();
    public int Count => edits.Count;
    public IReadOnlyList<PNGridEdit> Edits => edits;
    public PNGridEditBatch AddSolid(int x, int z, bool value) { edits.Add(new PNGridEdit(PNGridEditKind.Solid, x, z, value ? 1 : 0)); return this; }
    public PNGridEditBatch AddGround(int x, int z, int paletteIndex) { edits.Add(new PNGridEdit(PNGridEditKind.Ground, x, z, paletteIndex)); return this; }
    public PNGridEditBatch AddHeight(int x, int z, int raw) { edits.Add(new PNGridEdit(PNGridEditKind.Height, x, z, raw)); return this; }
    public PNGridEditBatch AddSolidRegion(PNGridRegion region, bool value)
    { for (int z = region.Z0; z <= region.Z1; z++) for (int x = region.X0; x <= region.X1; x++) AddSolid(x, z, value); return this; }
}

public readonly struct PNGridEditSubmit
{
    public readonly bool Accepted;
    public readonly int Queued;
    public readonly string Reason;
    public PNGridEditSubmit(bool accepted, int queued, string reason) { Accepted = accepted; Queued = queued; Reason = reason; }
}

// Groups cells into clusters: cells whose 8x8 tiles touch (also diagonally) belong to the same cluster; each cluster gets the bounding box of its OWN cells.
public static class PNGridClusters
{
    public static List<PNGridRegion> Group(IReadOnlyList<(int x, int z)> cells, int tile = PNGridEditLimits.ClusterTile)
    {
        var result = new List<PNGridRegion>();
        if (cells == null || cells.Count == 0) return result;
        var tileIndex = new Dictionary<long, int>(); var parent = new List<int>();
        var tileOf = new int[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            long key = Key(Div(cells[i].x, tile), Div(cells[i].z, tile));
            if (!tileIndex.TryGetValue(key, out int t)) { t = parent.Count; parent.Add(t); tileIndex[key] = t; }
            tileOf[i] = t;
        }
        foreach (var pair in tileIndex)
        {
            int tx = (int)(pair.Key >> 32), tz = (int)(pair.Key & 0xffffffffL);
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                if ((dx | dz) == 0) continue;
                if (tileIndex.TryGetValue(Key(tx + dx, tz + dz), out int other)) Union(parent, pair.Value, other);
            }
        }
        var boxes = new Dictionary<int, int[]>();
        for (int i = 0; i < cells.Count; i++)
        {
            int root = Find(parent, tileOf[i]); var c = cells[i];
            if (!boxes.TryGetValue(root, out var box)) boxes[root] = new[] { c.x, c.z, c.x, c.z };
            else { if (c.x < box[0]) box[0] = c.x; if (c.z < box[1]) box[1] = c.z; if (c.x > box[2]) box[2] = c.x; if (c.z > box[3]) box[3] = c.z; }
        }
        foreach (var box in boxes.Values) result.Add(new PNGridRegion(box[0], box[1], box[2], box[3]));
        return result;
    }
    private static int Div(int value, int by) => value >= 0 ? value / by : -((-value + by - 1) / by);
    private static long Key(int x, int z) => ((long)x << 32) | (uint)z;
    private static int Find(List<int> parent, int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
    private static void Union(List<int> parent, int a, int b) { a = Find(parent, a); b = Find(parent, b); if (a != b) parent[b] = a; }
}

public sealed class PNGridEditor
{
    private readonly PNGridWorldData data;
    private readonly Queue<PNGridEdit> queue = new Queue<PNGridEdit>();
    private readonly List<(int x, int z)> applied = new List<(int, int)>();
    public PNGridWorldData Data => data;
    public int QueuedCells => queue.Count;
    public long AppliedCells { get; private set; }
    // The first release of the grid navigation supports a flat floor only, so height edits are refused until it does.
    public bool AllowHeight { get; set; }

    public PNGridEditor(PNGridWorldData data) { this.data = data ?? throw new ArgumentNullException(nameof(data)); }

    // Validates the whole batch first; a batch with any bad edit is refused as a whole (nothing is queued). Cells that the world edge or a value limit rule out are bad.
    public PNGridEditSubmit Submit(PNGridEditBatch batch)
    {
        if (batch == null) throw new ArgumentNullException(nameof(batch));
        if (batch.Count == 0) return new PNGridEditSubmit(true, 0, null);
        if (batch.Count > PNGridEditLimits.MaxBatchCells) return Refuse("A batch holds at most " + PNGridEditLimits.MaxBatchCells + " cells.");
        if (queue.Count + batch.Count > PNGridEditLimits.MaxQueuedCells) return Refuse("The edit queue is full.");
        int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
        foreach (var e in batch.Edits)
        {
            if (!data.InBounds(e.X, e.Z)) return Refuse("Cell (" + e.X + "," + e.Z + ") is outside the world.");
            switch (e.Kind)
            {
                case PNGridEditKind.Solid: if (e.Value != 0 && e.Value != 1) return Refuse("A solid edit is 0 or 1."); break;
                case PNGridEditKind.Ground: if (e.Value < 0 || e.Value >= data.Palette.Count) return Refuse("Ground type " + e.Value + " is not in the palette."); break;
                case PNGridEditKind.Height:
                    if (!AllowHeight) return Refuse("Height edits are not supported yet (grid navigation is flat only).");
                    if (Math.Abs(e.Value) > PNGridEditLimits.MaxHeightRaw) return Refuse("Height " + e.Value + " is out of range."); break;
                default: return Refuse("Unknown edit kind.");
            }
            if (e.X < x0) x0 = e.X; if (e.X > x1) x1 = e.X; if (e.Z < z0) z0 = e.Z; if (e.Z > z1) z1 = e.Z;
        }
        if (x1 - x0 + 1 > PNGridEditLimits.MaxBatchExtent || z1 - z0 + 1 > PNGridEditLimits.MaxBatchExtent) return Refuse("A batch spans at most " + PNGridEditLimits.MaxBatchExtent + " cells on a side.");
        foreach (var e in batch.Edits) queue.Enqueue(e);
        return new PNGridEditSubmit(true, batch.Count, null);
    }
    private static PNGridEditSubmit Refuse(string reason) => new PNGridEditSubmit(false, 0, reason);

    // Applies up to `budget` queued edits (never more than PNGridEditLimits.MaxCellsPerTick) and returns the clusters of cells that changed in `regions`; the clearance field is already recomputed for them.
    // Returns the number of edits taken from the queue.
    public int Tick(int budget, List<PNGridRegion> regions)
    {
        if (regions == null) throw new ArgumentNullException(nameof(regions));
        budget = Math.Min(budget, PNGridEditLimits.MaxCellsPerTick);
        if (budget <= 0 || queue.Count == 0) return 0;
        applied.Clear(); int taken = 0; bool solidChanged = false;
        while (taken < budget && queue.Count > 0)
        {
            var e = queue.Dequeue(); taken++;
            switch (e.Kind)
            {
                case PNGridEditKind.Solid:
                    if (data.IsSolid(e.X, e.Z) == (e.Value != 0)) continue;
                    data.SetSolid(e.X, e.Z, e.Value != 0, false); solidChanged = true; break;
                case PNGridEditKind.Ground:
                    int gx = e.X / data.GroundFactor, gz = e.Z / data.GroundFactor;
                    if (data.GroundCell(gx, gz) == e.Value) continue;
                    data.SetGroundCell(gx, gz, (ushort)e.Value);
                    // The whole 2 m ground cell changed, so the cluster must cover all its navigation cells.
                    applied.Add((gx * data.GroundFactor, gz * data.GroundFactor));
                    applied.Add(((gx + 1) * data.GroundFactor - 1, (gz + 1) * data.GroundFactor - 1)); continue;
                case PNGridEditKind.Height:
                    if (data.HeightRaw(e.X, e.Z) == e.Value) continue;
                    data.SetHeightRaw(e.X, e.Z, (short)e.Value); break;
            }
            applied.Add((e.X, e.Z));
        }
        AppliedCells += taken;
        var found = PNGridClusters.Group(applied);
        if (solidChanged) foreach (var region in found) data.RecomputeClearance(region);
        regions.AddRange(found);
        return taken;
    }
}
