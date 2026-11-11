using System;

// EN: What a world gives the navigation instead of a baked cache (Docs/SandboxPlan.md, step G2): the shape of a regular grid and which cells a body of the profile can stand on. Pure data and rules, no Unity, no A*: the lease
//     in PNWorldNavigation builds its graph from this and every other part of the game keeps talking to the lease. `Walkable` is what the graph gets (it can already include room for a big body); `Blocks` is the physical
//     obstacle itself (a wall, water), which the path refinement keeps a body radius away from.
// VI: Thứ world đưa cho điều hướng thay cho cache bake (G2): hình dạng lưới đều và ô nào thân của profile đứng được. `Walkable` đi vào đồ thị; `Blocks` là vật cản thật mà bước tinh chỉnh đường phải giữ cách một bán kính.
public sealed class PNNavigationProfile
{
    public string Id { get; }
    public float Radius { get; }
    public float Height { get; }
    public PNNavigationProfile(string id, float radius, float height)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) throw new ArgumentException("A navigation profile needs an id of at most 64 characters.", nameof(id));
        if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0 || radius > 8) throw new ArgumentOutOfRangeException(nameof(radius));
        if (float.IsNaN(height) || float.IsInfinity(height) || height <= 0 || height > 32) throw new ArgumentOutOfRangeException(nameof(height));
        Id = id; Radius = radius; Height = height;
    }
    // The profile the first enemies share (the capsule of every Star enemy: radius 0.2, height 1.4).
    public static readonly PNNavigationProfile GroundSmall = new PNNavigationProfile("ground-small", .2f, 1.4f);
}

public interface IPNNavigationSource
{
    PNNavigationProfile Profile { get; }
    int Width { get; }
    int Depth { get; }
    float CellSize { get; }
    float OriginX { get; }
    float OriginZ { get; }
    // True while the floor is at height 0 everywhere (the first release of the grid navigation supports only that).
    bool IsFlat { get; }
    // How many cells away from a changed cell `Walkable` of another cell can change (a grid update looks at the changed region grown by this much).
    int Influence { get; }
    bool Walkable(int x, int z);
    bool Blocks(int x, int z);
}

// The navigation source of a grid world: cells from PNGridWorldData, the ground types' walkability from the palette.
public sealed class PNGridNavigationSource : IPNNavigationSource
{
    private readonly PNGridWorldData data;
    private readonly bool[] groundWalkable;
    private readonly int neededClearance;
    public PNNavigationProfile Profile { get; }
    public int Width => data.Width;
    public int Depth => data.Depth;
    public float CellSize => data.CellSize;
    public float OriginX { get; }
    public float OriginZ { get; }
    public bool IsFlat => data.IsFlat();
    public int Influence => neededClearance;
    public PNGridWorldData Data => data;

    // `groundWalkable[i]` says whether palette entry i can be walked on.
    public PNGridNavigationSource(PNGridWorldData data, bool[] groundWalkable, PNNavigationProfile profile, float originX = 0, float originZ = 0)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        if (groundWalkable == null || groundWalkable.Length != data.Palette.Count) throw new ArgumentException("One walkability flag per palette entry is required.", nameof(groundWalkable));
        this.groundWalkable = (bool[])groundWalkable.Clone();
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        OriginX = originX; OriginZ = originZ;
        // Cells are walls or free; a body fits a free cell when its radius is within half a cell. A bigger body needs free cells around the one it stands in: one more cell of clearance per cell of extra radius.
        float extra = profile.Radius - data.CellSize * .5f;
        neededClearance = extra <= 1e-4f ? 1 : (int)Math.Ceiling(extra / data.CellSize - 1e-4f) + 1;
        if (neededClearance >= PNGridWorldLimits.ClearanceCap) throw new ArgumentException("The profile is too big for the clearance field (" + neededClearance + " cells).", nameof(profile));
    }

    public bool Blocks(int x, int z) => !data.InBounds(x, z) || data.IsSolid(x, z) || !groundWalkable[data.GroundAt(x, z)];
    public bool Walkable(int x, int z) => !Blocks(x, z) && data.Clearance(x, z) >= neededClearance;
}
