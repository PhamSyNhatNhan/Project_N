#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;

// EN: Per-world candidate list shared by projectile contact and target selection (PNTargetSelector). Membership (which loaded, ready,
//     living entities have a hit box) is refreshed at a limited rate by PNTargetIndex; the boxes themselves are re-read every tick, and
//     an entity that died or unloaded is dropped immediately. It is only a coarse filter for contact and the source for target search.
// VI: Danh sách ứng viên theo world dùng chung cho va chạm đạn và chọn mục tiêu (PNTargetSelector). Thành viên làm mới theo nhịp giới
//     hạn bởi PNTargetIndex; hộp được đọc lại mỗi tick và thực thể đã chết/unload bị loại ngay.
public sealed class PNTargetCandidates
{
    public struct Candidate
    {
        public PNStat Stat; public Collider Collider; public PNIProjectileHitVolume Volume; public Vector3 Center, Extents; public float Radius; public int Layer;
        // Read at membership refresh (and by UpdateAggro for queries outside the tick) so target selection needs no per-candidate stat lookup.
        public long EntityId; public double Aggro;
    }
    private Candidate[] items = new Candidate[64];
    private int count;
    // Direct array access for the per-tick contact loops (valid up to Count; do not keep it across ticks).
    public Candidate[] RawItems => items;
    public int Count => count;
    // Dead check that tolerates entities without a Health stat (they count as alive).
    public static bool IsDead(PNStat stat) { try { return stat.IsDead; } catch (InvalidOperationException) { return false; } }
    // Ready, active, executable, alive.
    public static bool IsLive(PNStat stat) => stat != null && stat.IsReady && stat.gameObject.activeInHierarchy && PNWorldGameplay.CanExecute(stat) && !IsDead(stat);
    // Membership refresh: rebuilds the list from the world's entities.
    public void Refresh(EveSessionContext session, EveWorldContext world)
    {
        count = 0;
        if (session == null || !session.IsActive) return;
        IReadOnlyList<EveEntityLoader> loaders = world != null ? session.Entities.Snapshot(world) : session.Entities.Snapshot();
        for (int i = 0; i < loaders.Count; i++)
        {
            var loader = loaders[i];
            if (loader == null || loader.LoadContext == null || !ReferenceEquals(loader.LoadContext.Identity.World, world)) continue;
            var stat = loader.GetComponent<PNStat>();
            if (!IsLive(stat)) continue;
            var candidate = new Candidate { Stat = stat, Layer = stat.gameObject.layer, EntityId = stat.EntityId.Value };
            stat.TryGetComponent<PNIProjectileHitVolume>(out candidate.Volume);
            if (candidate.Volume == null)
            {
                var collider = stat.GetComponent<Collider>();
                if (collider == null || !collider.enabled || collider.isTrigger) collider = stat.GetComponentsInChildren<Collider>().FirstOrDefault(x => x.enabled && !x.isTrigger);
                candidate.Collider = collider;
            }
            if (Read(ref candidate)) { candidate.Aggro = PNTargetSelector.AggroOf(stat); if (count == items.Length) Array.Resize(ref items, count * 2); items[count++] = candidate; }
        }
    }
    // Per-tick update: re-reads each box and drops entities that are no longer live.
    public void UpdateBounds()
    {
        int write = 0;
        for (int i = 0; i < count; i++)
        {
            var candidate = items[i];
            if (!IsLive(candidate.Stat) || !Read(ref candidate)) continue;
            items[write++] = candidate;
        }
        count = write; Rebuild();
    }
    // Re-reads every candidate's aggro stat (PNTargetIndex.Acquire calls it, so a query outside the tick sees an aggro change at once).
    public void UpdateAggro() { for (int i = 0; i < count; i++) items[i].Aggro = PNTargetSelector.AggroOf(items[i].Stat); }
    private static bool Read(ref Candidate candidate)
    {
        if (candidate.Volume != null) { if (!candidate.Volume.TryGetHitVolume(out candidate.Center, out candidate.Extents)) return false; candidate.Radius = candidate.Extents.magnitude; return true; }
        if (candidate.Collider == null || !candidate.Collider.enabled) return false;
        var bounds = candidate.Collider.bounds; candidate.Center = bounds.center; candidate.Extents = bounds.extents; candidate.Radius = candidate.Extents.magnitude; return true;
    }
    // Hit box of one entity (declared volume first, otherwise the root non-trigger collider bounds).
    public static bool TryGetHitVolume(PNStat stat, out Vector3 center, out Vector3 extents)
    {
        center = default; extents = default;
        if (stat == null) return false;
        var candidate = new Candidate();
        stat.TryGetComponent<PNIProjectileHitVolume>(out candidate.Volume);
        if (candidate.Volume == null)
        {
            var collider = stat.GetComponent<Collider>();
            if (collider == null || !collider.enabled || collider.isTrigger) collider = stat.GetComponentsInChildren<Collider>().FirstOrDefault(x => x.enabled && !x.isTrigger);
            candidate.Collider = collider;
        }
        if (!Read(ref candidate)) return false;
        center = candidate.Center; extents = candidate.Extents; return true;
    }
    // ---- spatial hash over the candidate centers (XZ plane): a projectile only examines candidates near its step -------------------
    private const int Buckets = 256;
    private readonly int[] head = new int[Buckets];
    private int[] next = new int[64], stamp = new int[64], found = new int[64];
    private float inverseCell = .25f, maxRadius; private int queryStamp;
    private void Rebuild()
    {
        for (int i = 0; i < Buckets; i++) head[i] = -1;
        if (next.Length < count) { next = new int[items.Length]; stamp = new int[items.Length]; found = new int[items.Length]; }
        maxRadius = 0f;
        for (int i = 0; i < count; i++) if (items[i].Radius > maxRadius) maxRadius = items[i].Radius;
        inverseCell = 1f / Mathf.Max(4f, maxRadius * 2f);
        for (int i = 0; i < count; i++)
        {
            int bucket = Bucket(Mathf.FloorToInt(items[i].Center.x * inverseCell), Mathf.FloorToInt(items[i].Center.z * inverseCell));
            next[i] = head[bucket]; head[bucket] = i;
        }
    }
    private static int Bucket(int x, int z) => ((x * 73856093) ^ (z * 19349663)) & (Buckets - 1);
    // Candidate indices whose center may lie within `reach` (plus the largest candidate radius) of the box [min, max] in XZ. The result array is
    // reused by the next query. Falls back to every candidate when the range spans many cells.
    public int Query(float minX, float minZ, float maxX, float maxZ, float reach, out int[] indices)
    {
        indices = found;
        float margin = reach + maxRadius;
        int x0 = Mathf.FloorToInt((minX - margin) * inverseCell), x1 = Mathf.FloorToInt((maxX + margin) * inverseCell);
        int z0 = Mathf.FloorToInt((minZ - margin) * inverseCell), z1 = Mathf.FloorToInt((maxZ + margin) * inverseCell);
        long cells = (long)(x1 - x0 + 1) * (z1 - z0 + 1);
        if (cells > 64) { for (int i = 0; i < count; i++) found[i] = i; return count; }
        if (++queryStamp == int.MaxValue) { Array.Clear(stamp, 0, stamp.Length); queryStamp = 1; }
        int n = 0;
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
                for (int i = head[Bucket(x, z)]; i >= 0; i = next[i])
                    if (stamp[i] != queryStamp) { stamp[i] = queryStamp; found[n++] = i; }
        return n;
    }
    // Nearest live candidate (distance to the box center) within the radius on the given layers; null when none.
    public PNStat FindNearest(Vector3 position, float radius, int layerMask)
    {
        PNStat best = null; float bestDistance = radius * radius;
        for (int i = 0; i < count; i++)
        {
            var candidate = items[i];
            if (((layerMask >> candidate.Layer) & 1) == 0) continue;
            float distance = (candidate.Center - position).sqrMagnitude;
            if (distance > bestDistance || !IsLive(candidate.Stat)) continue;
            best = candidate.Stat; bestDistance = distance;
        }
        return best;
    }
}
#endif
