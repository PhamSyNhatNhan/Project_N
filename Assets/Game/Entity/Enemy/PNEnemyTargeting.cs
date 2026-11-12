#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// EN: The shared target finding of enemy behaviors (Docs/SandboxPlan.md, step G5). One object per behavior instance. It carries the rules the Star enemies each repeated: who is a valid hostile (alive, ready, the
//     same world, `Targets`, not aggro-ignored), whether it is seen (a ray at `SightHeight` above the ground, blocked by walls), whether the held target is still kept (valid, seen, within the vision range) and the Aggro
//     rule over the candidates in range (smallest distance minus aggro, then the lower entity id: PNTargetRanking). The candidates come from the world's chunk index (PNWorldEntityIndex), not from a scan of every entity.
//       - Range: the character's registered `body.vision` when it has one, else the behavior's own default (a hand-built entity of a test has no role data).
//       - Sight: in a world with grid cells (PNGridWorldController) the cells decide (PNGridSight); elsewhere the physics ray on the `Wall` layer, exactly as before.
// VI: Tìm mục tiêu dùng chung của hành vi quái: hợp lệ, tầm nhìn (tia ở độ cao SightHeight, tường chặn), giữ mục tiêu cũ, luật Aggro (khoảng cách trừ aggro, hòa thì id thấp hơn). Ứng viên lấy từ chỉ mục theo mảnh của world.
//     Tầm = body.vision của nhân vật, nếu không có thì mặc định của hành vi. Tầm nhìn: world lưới theo ô, nơi khác theo tia vật lý layer `Wall` như trước.
public sealed class PNEnemyTargeting
{
    private static readonly int WallMask = LayerMask.GetMask("Wall");
    private readonly PNEnemyBehaviorContext context;
    private readonly float fallbackRange;
    private readonly RaycastHit[] hits = new RaycastHit[8];
    private readonly List<PNStat> near = new List<PNStat>();
    private PNGridWorldData cells;
    private bool cellsLooked;

    // Height of the sight ray above the feet of both ends.
    public float SightHeight { get; set; } = .2f;
    // Distance on the ground plane (true) or in 3D (false): each Star behavior kept the one it always used.
    public bool Planar { get; set; } = true;

    public PNEnemyTargeting(PNEnemyBehaviorContext context, float fallbackRange)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        if (float.IsNaN(fallbackRange) || float.IsInfinity(fallbackRange) || fallbackRange <= 0f) throw new ArgumentOutOfRangeException(nameof(fallbackRange));
        this.fallbackRange = fallbackRange;
    }

    public float Range
    {
        get { var role = context.Owner.GetComponent<PNEntityRole>(); return role != null && role.Vision > 0f ? role.Vision : fallbackRange; }
    }

    public float Distance(Vector3 point)
    {
        var d = point - context.Owner.transform.position; if (Planar) d.y = 0f;
        return d.magnitude;
    }

    // Valid hostile: not the enemy itself, ready, alive, hostile to it, not aggro-ignored, in the same world and both able to act.
    public bool Valid(PNStat value) => value != null && value != context.Stat && value.IsReady && !value.IsDead && context.Stat.Targets(value) && PNTargetSelector.IsAggroEligible(value) && PNWorldGameplay.CanTarget(context.Owner, value);

    public bool Sight(PNStat target)
    {
        var from = context.Owner.transform.position; var to = target.transform.position;
        var grid = Cells();
        if (grid != null) return PNGridSight.Clear(grid, from.x, from.z, to.x, to.z);
        var start = from + Vector3.up * SightHeight; var d = to + Vector3.up * SightHeight - start;
        if (d.sqrMagnitude < 1e-6f) return true;
        return context.Owner.gameObject.scene.GetPhysicsScene().Raycast(start, d.normalized, hits, d.magnitude, WallMask, QueryTriggerInteraction.Ignore) == 0;
    }

    // The held target stays while it is valid, seen and within the range.
    public bool Keep(PNStat held) => Valid(held) && Sight(held) && Distance(held.transform.position) <= Range;

    // The best valid, seen hostile within the range by the Aggro rule; null when none.
    public PNStat Acquire()
    {
        var world = context.Owner.GetComponent<EveEntityIdentity>()?.World; if (world == null) return null;
        float range = Range;
        near.Clear(); PNWorldEntityIndex.For(world).Near(context.Owner.transform.position, range, near);
        PNStat best = null; var bestScore = default(PNTargetScore);
        foreach (var s in near)
        {
            if (!Valid(s)) continue;
            float distance = Distance(s.transform.position); if (distance > range || !Sight(s)) continue;
            var score = new PNTargetScore { Distance = distance, Aggro = PNTargetSelector.AggroOf(s), EntityId = s.EntityId.Value };
            if (best == null || PNTargetRanking.Better(score, bestScore, PNTargetRule.Aggro)) { best = s; bestScore = score; }
        }
        near.Clear();
        return best;
    }

    // The grid data of the enemy's world, looked up once (null in a world without cells).
    private PNGridWorldData Cells()
    {
        if (cellsLooked) return cells;
        cellsLooked = true;
        var world = context.Owner.GetComponent<EveEntityIdentity>()?.World;
        cells = PNGridWorldController.Find(world)?.Data;
        return cells;
    }
}
#endif
