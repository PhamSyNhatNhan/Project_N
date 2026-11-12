using System;
using System.Collections.Generic;
#if UNITY_5_3_OR_NEWER
using System.Runtime.CompilerServices;
using EveEngine;
using UnityEngine;
#endif

// EN: The chunk index of entities (Docs/SandboxPlan.md, steps G4 and G5). `PNSpatialBuckets` is the pure part: items put in square buckets by their flat position, and a query that visits only the buckets a circle
//     touches. `PNWorldEntityIndex` is the Unity part: ONE index per world, refilled from the world's entity list at most every `RefreshSeconds`, shared by target finding, the despawn service and (later)
//     the spawner and the safe zones, so that N enemies looking for a target cost one pass over the entities, not N. The index is a CANDIDATE list: a consumer still checks readiness, life and its own
//     rules, because an entity can die or unload between two refreshes.
// VI: Chỉ mục thực thể theo mảnh. Phần thuần: ô vuông theo vị trí phẳng, truy vấn chỉ ghé ô mà vòng tròn chạm. Phần Unity: MỘT chỉ mục mỗi world, làm lại từ danh sách thực thể tối đa mỗi RefreshSeconds,
//     dùng chung cho tìm mục tiêu, dịch vụ hết hạn... Chỉ mục là danh sách ỨNG VIÊN: nơi dùng vẫn kiểm sẵn sàng/còn sống.
public sealed class PNSpatialBuckets
{
    public const float DefaultBucket = 8f;
    public const int MaxItems = 1 << 20;
    private readonly float size;
    private readonly Dictionary<long, List<int>> buckets = new Dictionary<long, List<int>>();
    private readonly Stack<List<int>> spare = new Stack<List<int>>();
    private float[] xs = new float[64], zs = new float[64];
    private int count;

    public PNSpatialBuckets(float bucketSize = DefaultBucket)
    {
        if (float.IsNaN(bucketSize) || float.IsInfinity(bucketSize) || bucketSize < 1f || bucketSize > 1024f) throw new ArgumentOutOfRangeException(nameof(bucketSize));
        size = bucketSize;
    }

    public int Count => count;
    public int BucketCount => buckets.Count;

    public void Clear()
    {
        foreach (var list in buckets.Values) { list.Clear(); spare.Push(list); }
        buckets.Clear(); count = 0;
    }

    private static long Key(int bx, int bz) => ((long)bx << 32) | (uint)bz;
    private int Cell(float value) => (int)Math.Floor(value / size);

    // Adds an item; returns its slot (the number to give back to `Position`). Slots are consecutive from 0 after a Clear.
    public int Add(float x, float z)
    {
        if (float.IsNaN(x) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(z)) throw new ArgumentOutOfRangeException(nameof(x), "An indexed position must be finite.");
        if (count >= MaxItems) throw new InvalidOperationException("Too many indexed items.");
        if (count == xs.Length) { Array.Resize(ref xs, count * 2); Array.Resize(ref zs, count * 2); }
        xs[count] = x; zs[count] = z;
        long key = Key(Cell(x), Cell(z));
        if (!buckets.TryGetValue(key, out var list)) { list = spare.Count > 0 ? spare.Pop() : new List<int>(8); buckets.Add(key, list); }
        list.Add(count);
        return count++;
    }

    public float X(int slot) => xs[slot];
    public float Z(int slot) => zs[slot];

    // Appends the slots whose flat distance to (x, z) is at most `radius`. Returns how many were appended.
    public int Query(float x, float z, float radius, List<int> result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0f) throw new ArgumentOutOfRangeException(nameof(radius));
        int before = result.Count; float r2 = radius * radius;
        int x0 = Cell(x - radius), x1 = Cell(x + radius), z0 = Cell(z - radius), z1 = Cell(z + radius);
        // A huge circle over few buckets: walk the buckets that exist instead of the cells of the box.
        if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > buckets.Count * 4L)
        {
            foreach (var pair in buckets)
            {
                int bx = (int)(pair.Key >> 32), bz = (int)(pair.Key & 0xffffffff);
                if (bx < x0 || bx > x1 || bz < z0 || bz > z1) continue;
                Collect(pair.Value, x, z, r2, result);
            }
            return result.Count - before;
        }
        for (int bx = x0; bx <= x1; bx++)
            for (int bz = z0; bz <= z1; bz++)
                if (buckets.TryGetValue(Key(bx, bz), out var list)) Collect(list, x, z, r2, result);
        return result.Count - before;
    }

    private void Collect(List<int> list, float x, float z, float r2, List<int> result)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int slot = list[i]; float dx = xs[slot] - x, dz = zs[slot] - z;
            if (dx * dx + dz * dz <= r2) result.Add(slot);
        }
    }
}

#if UNITY_5_3_OR_NEWER
public sealed class PNWorldEntityIndex
{
    public const float RefreshSeconds = .1f;
    // A query reaches this far past its radius: the indexed positions are up to RefreshSeconds old, so a mover near the edge is still a candidate (the consumer measures the live distance).
    public const float PositionSlack = 1f;
    private static readonly ConditionalWeakTable<EveWorldContext, PNWorldEntityIndex> indexes = new ConditionalWeakTable<EveWorldContext, PNWorldEntityIndex>();
    private readonly EveWorldContext world;
    private readonly PNSpatialBuckets buckets = new PNSpatialBuckets();
    private readonly List<PNStat> stats = new List<PNStat>();
    private readonly List<int> slots = new List<int>();
    private float refreshedAt = float.NegativeInfinity;
    private int refreshedFrame = -1;
    public int Refreshes { get; private set; }
    public int Count => stats.Count;

    private PNWorldEntityIndex(EveWorldContext world) { this.world = world; }

    // The index of a world (made on first use; the table holds it only as long as the world exists).
    public static PNWorldEntityIndex For(EveWorldContext world)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        return indexes.GetValue(world, w => new PNWorldEntityIndex(w));
    }

    // Refills the index unless it was refilled less than RefreshSeconds ago (game time) or in this frame. `force` refills at once (a test, or a consumer that needs the exact set).
    public void Refresh(bool force = false)
    {
        int frame = Time.frameCount; float now = Time.unscaledTime;
        if (!force && (frame == refreshedFrame || now - refreshedAt < RefreshSeconds)) return;
        refreshedFrame = frame; refreshedAt = now; Refreshes++;
        buckets.Clear(); stats.Clear();
        foreach (var loader in world.Session.Entities.Snapshot(world))
        {
            if (loader == null) continue;
            var stat = loader.GetComponent<PNStat>(); if (stat == null) continue;
            var p = stat.transform.position;
            if (float.IsNaN(p.x) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.z)) continue;
            buckets.Add(p.x, p.z); stats.Add(stat);
        }
    }

    // Appends the candidates whose flat distance to `center` is at most `radius` (the positions are those of the last refresh; the stats are the live objects). Refreshes first when due.
    public void Near(Vector3 center, float radius, List<PNStat> result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        Refresh();
        slots.Clear(); buckets.Query(center.x, center.z, radius + PositionSlack, slots);
        // Slot order is the world's entity order, so a rule that breaks a tie by entity id sees the same order as a full scan.
        slots.Sort();
        for (int i = 0; i < slots.Count; i++) { var stat = stats[slots[i]]; if (stat != null) result.Add(stat); }
    }

    // Every candidate of the world, in the world's entity order.
    public void All(List<PNStat> result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        Refresh();
        for (int i = 0; i < stats.Count; i++) if (stats[i] != null) result.Add(stats[i]);
    }
}
#endif
