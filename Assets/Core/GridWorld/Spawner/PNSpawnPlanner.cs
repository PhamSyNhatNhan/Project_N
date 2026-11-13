using System;
using System.Collections.Generic;

// EN: Where a monster may appear (Docs/SandboxPlan.md, step G9), the pure part of the spawner module `project_n:spawner`. A spot is a cell in the ring [RingMin, RingMax) metres around a living player that
//     (1) a body can stand on (walkable with room), (2) lies in the same connected area as that player (a bounded flood fill over the cells, so nothing appears behind a wall or on an island), (3) is farther
//     than SafeDistance from EVERY living player, and (4) is not under a spawn block: a zone of the map or a placed thing with `spawnBlock` and a safety radius (a monster that `ignoresSafety` skips this one rule).
//     Zones and things also scale the rate (`rateScale`, product of every one that covers the spot): the planner returns the scale, the service uses it as the chance. Pure C#, no Unity: the smoke suite checks it.
// VI: Nơi quái được xuất hiện (G9), phần thuần của module `project_n:spawner`: ô trong vòng [RingMin, RingMax) m quanh người chơi còn sống, đứng được, cùng vùng liên thông với người chơi (đổ loang giới hạn),
//     cách MỌI người chơi còn sống hơn SafeDistance, và không nằm dưới `spawnBlock` (vùng của map hoặc vật đặt có bán kính an toàn; quái `ignoresSafety` bỏ qua luật này). Vùng/vật cũng chỉnh tốc độ qua `rateScale`.
public static class PNSpawnerIds
{
    // The world feature that gives a grid world the spawner service (idle until a mode configures it).
    public static readonly EveEngine.EveId Feature = EveEngine.EveId.Parse("project_n:spawner");
}

public sealed class PNSpawnSettings
{
    public float RingMin = 18f, RingMax = 25f, SafeDistance = 18f, Interval = 2f;
    public int PerPlayerBudget = 6, WorldBudget = 24, Attempts = 16;
    // Metres around a player within which an enemy counts for that player's budget.
    public float BudgetRadius = 40f;

    public void Validate()
    {
        bool Bad(float v, float min, float max) => float.IsNaN(v) || v < min || v > max;
        if (Bad(RingMin, 1, 200) || Bad(RingMax, 1, 200) || RingMax <= RingMin) throw new ArgumentException("The spawn ring is 1 to 200 metres and RingMax is more than RingMin.");
        if (Bad(SafeDistance, 0, 200) || SafeDistance > RingMax) throw new ArgumentException("SafeDistance is 0 to the outer ring.");
        if (Bad(Interval, .1f, 600)) throw new ArgumentException("Interval is 0.1 to 600 seconds.");
        if (Bad(BudgetRadius, 1, 400)) throw new ArgumentException("BudgetRadius is 1 to 400 metres.");
        if (PerPlayerBudget < 0 || PerPlayerBudget > 256 || WorldBudget < 0 || WorldBudget > 4096 || Attempts < 1 || Attempts > 256) throw new ArgumentException("A budget or the attempts are out of range.");
    }
}

// What one kind of monster needs to appear: its entity, its character, a weight among the entries, whether it ignores spawn blocks, and the clearance (cells) it needs around the spot.
public sealed class PNSpawnEntry
{
    public string Entity, Character;
    public double Weight = 1;
    public bool IgnoresSafety;
    public int Clearance;
}

// A circle on the map that blocks or scales spawning: a placed thing's safety radius.
public readonly struct PNSpawnSource
{
    public readonly float X, Z, Radius;
    public readonly bool Block;
    public readonly double Scale;
    public PNSpawnSource(float x, float z, float radius, bool block, double scale) { X = x; Z = z; Radius = radius; Block = block; Scale = scale; }
}

public sealed class PNSpawnPlanner
{
    private readonly PNGridWorldData data;
    private readonly IPNNavigationSource nav;
    private bool[] reach = new bool[0];
    private int windowX0, windowZ0, windowW, windowD;
    private uint mask;
    private readonly Stack<int> stack = new Stack<int>();

    public PNSpawnPlanner(PNGridWorldData data, IPNNavigationSource nav)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.nav = nav ?? throw new ArgumentNullException(nameof(nav));
    }

    public static List<PNSpawnSource> SourcesFrom(PNGridPlacements placements, float cellSize)
    {
        var list = new List<PNSpawnSource>();
        if (placements == null) return list;
        foreach (var thing in placements.All)
        {
            var s = thing.Definition.Settings;
            if (s.SafetyRadius <= 0 || !s.SpawnBlock && Math.Abs(s.SpawnRateScale - 1) < 1e-9) continue;
            var r = thing.Region;
            list.Add(new PNSpawnSource((r.X0 + r.X1 + 1) * .5f * cellSize, (r.Z0 + r.Z1 + 1) * .5f * cellSize, (float)s.SafetyRadius, s.SpawnBlock, s.SpawnRateScale));
        }
        return list;
    }

    // Marks the cells a body can reach from the player's cell inside a window around it (4-neighbour flood fill). Call once per player per round.
    // `mask` is the tag mask of the locomotion of the body to spawn (G12b: a flyer reaches cells a walker cannot); 0 means the default walker.
    public void Prepare(float playerX, float playerZ, float radius, uint mask = 0)
    {
        this.mask = mask == 0 ? nav.GroundMask : mask;
        int cx = (int)Math.Floor(playerX / data.CellSize), cz = (int)Math.Floor(playerZ / data.CellSize), r = (int)Math.Ceiling(radius / data.CellSize) + 2;
        windowX0 = Math.Max(0, cx - r); windowZ0 = Math.Max(0, cz - r);
        windowW = Math.Min(data.Width - 1, cx + r) - windowX0 + 1; windowD = Math.Min(data.Depth - 1, cz + r) - windowZ0 + 1;
        if (windowW <= 0 || windowD <= 0) { windowW = windowD = 0; return; }
        if (reach.Length < windowW * windowD) reach = new bool[windowW * windowD]; else Array.Clear(reach, 0, windowW * windowD);
        if (!data.InBounds(cx, cz)) return;
        // The player may stand where the strict test fails (against a wall): start from the nearest walkable cell within two cells.
        int sx = -1, sz = -1;
        for (int d = 0; d <= 2 && sx < 0; d++)
            for (int dz = -d; dz <= d && sx < 0; dz++) for (int dx = -d; dx <= d; dx++)
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == d && Inside(cx + dx, cz + dz) && nav.WalkableFor(cx + dx, cz + dz, this.mask)) { sx = cx + dx; sz = cz + dz; break; }
        if (sx < 0) return;
        stack.Clear(); stack.Push(sz * data.Width + sx); reach[(sz - windowZ0) * windowW + (sx - windowX0)] = true;
        while (stack.Count > 0)
        {
            int at = stack.Pop(), x = at % data.Width, z = at / data.Width;
            Visit(x + 1, z); Visit(x - 1, z); Visit(x, z + 1); Visit(x, z - 1);
        }
    }
    private bool Inside(int x, int z) => x >= windowX0 && z >= windowZ0 && x < windowX0 + windowW && z < windowZ0 + windowD;
    private void Visit(int x, int z)
    {
        if (!Inside(x, z)) return;
        int i = (z - windowZ0) * windowW + (x - windowX0);
        if (reach[i] || !nav.WalkableFor(x, z, mask)) return;
        reach[i] = true; stack.Push(z * data.Width + x);
    }
    public bool Reachable(int x, int z) => Inside(x, z) && reach[(z - windowZ0) * windowW + (x - windowX0)];

    // Tries up to Attempts random spots in the ring around the prepared player. `players` are ALL living players (metres, flat). On success returns the spot (the cell's centre) and the rate scale there.
    public bool TryPick(PNSpawnSettings settings, PNSpawnEntry entry, float playerX, float playerZ, IReadOnlyList<(float x, float z)> players, PNGridMapMeta meta, IReadOnlyList<PNSpawnSource> sources,
        PNGridRandom random, out float x, out float z, out double scale)
    {
        x = z = 0; scale = 1;
        if (settings == null || entry == null || players == null || random == null) throw new ArgumentNullException(settings == null ? nameof(settings) : entry == null ? nameof(entry) : players == null ? nameof(players) : nameof(random));
        float safe2 = settings.SafeDistance * settings.SafeDistance;
        for (int attempt = 0; attempt < settings.Attempts; attempt++)
        {
            double angle = random.NextDouble() * Math.PI * 2, radius = settings.RingMin + random.NextDouble() * (settings.RingMax - settings.RingMin);
            float px = playerX + (float)(Math.Cos(angle) * radius), pz = playerZ + (float)(Math.Sin(angle) * radius);
            int cx = (int)Math.Floor(px / data.CellSize), cz = (int)Math.Floor(pz / data.CellSize);
            if (!data.InBounds(cx, cz) || !Reachable(cx, cz) || data.Clearance(cx, cz) < entry.Clearance) continue;
            float sx = (cx + .5f) * data.CellSize, sz = (cz + .5f) * data.CellSize;
            bool near = false;
            foreach (var p in players) { float dx = p.x - sx, dz = p.z - sz; if (dx * dx + dz * dz <= safe2) { near = true; break; } }
            if (near) continue;
            double rate = 1; bool blocked = false;
            if (meta != null)
                foreach (var zone in meta.Zones)
                    if (zone.Region.Contains(cx, cz)) { if (zone.SpawnBlock) blocked = true; rate *= zone.RateScale; }
            if (sources != null)
                foreach (var source in sources)
                {
                    float dx = source.X - sx, dz = source.Z - sz;
                    if (dx * dx + dz * dz > source.Radius * source.Radius) continue;
                    if (source.Block) blocked = true;
                    rate *= source.Scale;
                }
            if (entry.IgnoresSafety) { blocked = false; rate = 1; }
            if (blocked) continue;
            x = sx; z = sz; scale = rate; return true;
        }
        return false;
    }

    // Chooses an entry by weight; null when there is none.
    public static PNSpawnEntry Choose(IReadOnlyList<PNSpawnEntry> entries, PNGridRandom random)
    {
        if (entries == null || entries.Count == 0) return null;
        double total = 0; foreach (var e in entries) total += Math.Max(0, e.Weight);
        if (total <= 0) return null;
        double pick = random.NextDouble() * total;
        foreach (var e in entries) { pick -= Math.Max(0, e.Weight); if (pick < 0) return e; }
        return entries[entries.Count - 1];
    }
}
