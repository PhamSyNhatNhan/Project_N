using System;
using EveEngine;
using System.Linq;
using System.Collections.Generic;

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
    // G12b: the graph has one node per cell and the node's tag is the terrain of the cell (land 0). `Passable` is what the node's walkability is: inside the world, not solid, room for the body. Which terrains a body may
    // cross is a tag mask per request (`MaskOf` a locomotion); `Walkable` and `Blocks` without a mask are the view of the default walker (land only).
    int Tag(int x, int z);
    bool Passable(int x, int z);
    uint GroundMask { get; }
    uint MaskOf(PNLocomotionDefinition locomotion);
    bool WalkableFor(int x, int z, uint mask);
    bool BlocksFor(int x, int z, uint mask);
    bool Walkable(int x, int z);
    bool Blocks(int x, int z);
}

// The same source seen by a body that stands on the terrains of `mask` (what the path refinement keeps a radius away from is the cells that body cannot cross).
public sealed class PNNavigationView : IPNNavigationSource
{
    private readonly IPNNavigationSource source; private readonly uint mask;
    public PNNavigationView(IPNNavigationSource source, uint mask) { this.source = source ?? throw new ArgumentNullException(nameof(source)); this.mask = mask; }
    public PNNavigationProfile Profile => source.Profile;
    public int Width => source.Width;
    public int Depth => source.Depth;
    public float CellSize => source.CellSize;
    public float OriginX => source.OriginX;
    public float OriginZ => source.OriginZ;
    public bool IsFlat => source.IsFlat;
    public int Influence => source.Influence;
    public int Tag(int x, int z) => source.Tag(x, z);
    public bool Passable(int x, int z) => source.Passable(x, z);
    public uint GroundMask => mask;
    public uint MaskOf(PNLocomotionDefinition locomotion) => source.MaskOf(locomotion);
    public bool WalkableFor(int x, int z, uint other) => source.WalkableFor(x, z, other);
    public bool BlocksFor(int x, int z, uint other) => source.BlocksFor(x, z, other);
    public bool Walkable(int x, int z) => source.WalkableFor(x, z, mask);
    public bool Blocks(int x, int z) => source.BlocksFor(x, z, mask);
}

// The navigation source of a grid world: cells from PNGridWorldData, the terrain tag of every ground type from the palette.
public sealed class PNGridNavigationSource : IPNNavigationSource
{
    private readonly PNGridWorldData data;
    private readonly byte[] groundTag;
    private readonly IReadOnlyDictionary<EveId, int> terrainTags;
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
    // The default walker stands on land only (tag 0).
    public uint GroundMask => 1u;

    // `groundWalkable[i]` says whether palette entry i can be walked on (land, tag 0, or not, tag 1). A world with terrains passes `groundTag[i]` (the tag of the terrain of palette entry i) and `terrainTags` (terrain id to
    // tag, `PNTerrainLayers.Tags`) instead, so a body of another locomotion can be given its mask.
    public PNGridNavigationSource(PNGridWorldData data, bool[] groundWalkable, PNNavigationProfile profile, float originX = 0, float originZ = 0, byte[] groundTag = null, IReadOnlyDictionary<EveId, int> terrainTags = null)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        if (groundWalkable == null || groundWalkable.Length != data.Palette.Count) throw new ArgumentException("One walkability flag per palette entry is required.", nameof(groundWalkable));
        if (groundTag != null && groundTag.Length != data.Palette.Count) throw new ArgumentException("One terrain tag per palette entry is required.", nameof(groundTag));
        if (groundTag != null) foreach (var tag in groundTag) if (tag > 31) throw new ArgumentException("A terrain tag is at most 31.", nameof(groundTag));
        this.groundTag = groundTag != null ? (byte[])groundTag.Clone() : groundWalkable.Select(x => x ? (byte)0 : (byte)1).ToArray();
        this.terrainTags = terrainTags ?? new Dictionary<EveId, int> { [PNTerrainIds.Land] = 0 };
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        OriginX = originX; OriginZ = originZ;
        // Cells are walls or free; a body fits a free cell when its radius is within half a cell. A bigger body needs free cells around the one it stands in: one more cell of clearance per cell of extra radius.
        float extra = profile.Radius - data.CellSize * .5f;
        neededClearance = extra <= 1e-4f ? 1 : (int)Math.Ceiling(extra / data.CellSize - 1e-4f) + 1;
        if (neededClearance >= PNGridWorldLimits.ClearanceCap) throw new ArgumentException("The profile is too big for the clearance field (" + neededClearance + " cells).", nameof(profile));
    }

    public int Tag(int x, int z) => data.InBounds(x, z) ? groundTag[data.GroundAt(x, z)] : 0;
    public uint MaskOf(PNLocomotionDefinition locomotion) => locomotion == null ? GroundMask : PNTerrainLayers.TagMask(locomotion, terrainTags);
    public bool Passable(int x, int z) => data.InBounds(x, z) && !data.IsSolid(x, z) && data.Clearance(x, z) >= neededClearance;
    public bool BlocksFor(int x, int z, uint mask) => !data.InBounds(x, z) || data.IsSolid(x, z) || (mask >> groundTag[data.GroundAt(x, z)] & 1u) == 0;
    public bool WalkableFor(int x, int z, uint mask) => !BlocksFor(x, z, mask) && data.Clearance(x, z) >= neededClearance;
    public bool Blocks(int x, int z) => BlocksFor(x, z, GroundMask);
    public bool Walkable(int x, int z) => WalkableFor(x, z, GroundMask);
}
