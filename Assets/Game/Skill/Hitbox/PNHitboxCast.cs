using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// EN: One action/projectile invocation owns hit repetition; detector and resolver remain stateless.
// VI: Một lần action/projectile sở hữu lịch trúng; detector/resolver không giữ lịch damage.
public sealed class PNHitboxCast : IDisposable
{
    private EveHitboxQuery query;
    private readonly PhysicsScene world;
    private readonly PNHitboxTargets resolver = new PNHitboxTargets();
    private readonly List<PNStat> candidates = new List<PNStat>();
    private readonly Dictionary<EveEntityId, double> lastHits = new Dictionary<EveEntityId, double>();
    private readonly Guid sessionId;
    private readonly double? rehitDelay;
    private double lastTime = -1d;
    private bool disposed;
    private bool queried;
    private bool querying;
    // null = once per cast; positive seconds = repeat interval using the caller's monotonic cast clock.
    public PNHitboxCast(EveHitboxShape shape, double? rehitDelay = null)
        : this(shape, Physics.defaultPhysicsScene, rehitDelay) { }
    public PNHitboxCast(EveHitboxShape shape, PhysicsScene world, double? rehitDelay = null)
    {
        if (!EveRegistrySession.IsReady) throw new InvalidOperationException("A hitbox cast requires an active session.");
        if (rehitDelay.HasValue && (double.IsNaN(rehitDelay.Value) || double.IsInfinity(rehitDelay.Value) || rehitDelay.Value <= 0d))
            throw new ArgumentOutOfRangeException(nameof(rehitDelay));
        sessionId = EveRegistrySession.Context.SessionId;
        this.rehitDelay = rehitDelay;
        this.world = world;
        query = new EveHitboxQuery(shape, world);
    }
    // Geometry changes never reset this invocation's committed hit history.
    public void SetShape(EveHitboxShape shape)
    {
        CheckTime(Math.Max(0d, lastTime));
        if (querying) throw new InvalidOperationException("Cannot change shape during target resolution.");
        if (shape == null) throw new ArgumentNullException(nameof(shape));
        var old = query.Shape;
        bool same = old.Kind == shape.Kind && old.Size == shape.Size && old.Radius == shape.Radius && old.Height == shape.Height &&
            old.InnerRadius == shape.InnerRadius && old.Angle == shape.Angle && old.Segments == shape.Segments && old.Points.Count == shape.Points.Count;
        for (int i = 0; same && i < old.Points.Count; i++) same = old.Points[i] == shape.Points[i];
        if (same) return;
        var replacement = new EveHitboxQuery(shape, world);
        query.Dispose(); query = replacement; queried = false; candidates.Clear();
    }
    // EN: Query eligible entities without consuming hits. Commit only after a successful hit effect.
    // VI: Dò mục tiêu hợp lệ chưa ghi nhận trúng. Commit sau khi hiệu ứng hit thành công.
    public int Query(EveHitboxPose pose, double castTime, List<PNStat> results, PNStat source = null,
        int layerMask = ~0, QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore, Predicate<PNStat> filter = null)
    {
        CheckTime(castTime);
        if (querying) throw new InvalidOperationException("Hitbox casts cannot reenter.");
        if (results == null) throw new ArgumentNullException(nameof(results));
        queried = false;
        candidates.Clear();
        results.Clear();
        querying = true;
        try
        {
            resolver.Query(query, pose, candidates, source, layerMask, triggers, filter);
            foreach (var target in candidates)
                if (!lastHits.TryGetValue(target.EntityId, out var previous) ||
                    (rehitDelay.HasValue && castTime - previous >= rehitDelay.Value)) results.Add(target);
            lastTime = castTime;
            queried = true;
            return results.Count;
        }
        finally { querying = false; }
    }
    public void CommitHit(PNStat target, double castTime)
    {
        CheckTime(castTime);
        if (querying) throw new InvalidOperationException("Cannot commit during target filtering.");
        if (!queried || target == null || !target.IsReady || target.SessionId != sessionId || !candidates.Contains(target))
            throw new ArgumentException("Commit requires a target from this cast's latest query.");
        if (castTime != lastTime) throw new ArgumentException("Commit must use the latest query time.");
        if (lastHits.TryGetValue(target.EntityId, out var previous) &&
            (!rehitDelay.HasValue || castTime - previous < rehitDelay.Value))
            throw new InvalidOperationException("Target is not eligible for another hit.");
        lastHits[target.EntityId] = castTime;
    }
    private void CheckTime(double time)
    {
        if (disposed) throw new ObjectDisposedException(nameof(PNHitboxCast));
        if (!EveRegistrySession.IsReady || EveRegistrySession.Context.SessionId != sessionId)
            throw new InvalidOperationException("Hitbox cast belongs to an ended session.");
        if (double.IsNaN(time) || double.IsInfinity(time) || time < 0d || time < lastTime)
            throw new ArgumentOutOfRangeException(nameof(time));
    }
    public void Dispose()
    {
        if (disposed) return;
        if (querying) throw new InvalidOperationException("Cannot dispose during target filtering.");
        disposed = true; query.Dispose(); candidates.Clear(); lastHits.Clear();
    }
}
