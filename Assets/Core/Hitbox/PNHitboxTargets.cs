using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// EN: Geometry returns colliders; Core resolves nearest entity boundary and deduplicates identity.
// VI: Hình học trả collider; Core tìm ranh giới entity gần nhất rồi gộp theo ID.
public sealed class PNHitboxTargets
{
    private readonly List<Collider> colliders = new List<Collider>();
    private readonly HashSet<EveEntityId> seen = new HashSet<EveEntityId>();
    private bool resolving;
    public int Query(EveHitboxQuery query, EveHitboxPose pose, List<PNStat> results,
        PNStat source = null, int layerMask = ~0, QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore,
        Predicate<PNStat> filter = null, bool includeDead = false)
    {
        if (query == null || results == null) throw new ArgumentNullException();
        if (resolving) throw new InvalidOperationException("Target resolution cannot reenter.");
        if (!EveRegistrySession.IsReady) throw new InvalidOperationException("Hitbox entities require a Ready session.");
        var session = EveRegistrySession.Context;
        if (source != null && !IsCurrent(source, session)) throw new ArgumentException("Source is not a current Ready entity.");
        resolving = true; results.Clear(); seen.Clear();
        try
        {
            query.Query(pose, colliders, layerMask, triggers);
            foreach (var collider in colliders)
            {
                PNStat target = Resolve(collider);
                if (target == null || !IsCurrent(target, session) || !PNWorldGameplay.CanExecute(target) || (source != null && (!PNWorldGameplay.CanTarget(source,target)||target.EntityId == source.EntityId)) ||
                    (!includeDead && target.IsDead) || !seen.Add(target.EntityId)) continue;
                if (filter == null || filter(target)) results.Add(target);
            }
            // Stable order independent of PhysX result order; caller may sort by distance if needed.
            results.Sort((a, b) => a.EntityId.Value.CompareTo(b.EntityId.Value));
            return results.Count;
        }
        finally { colliders.Clear(); seen.Clear(); resolving = false; }
    }
    private static PNStat Resolve(Collider collider)
    {
        for (var current = collider.transform; current != null; current = current.parent)
        {
            // Do not accidentally attribute a nested entity's colliders to its parent's stats.
            if (current.TryGetComponent<EveEntityIdentity>(out _)) return current.GetComponent<PNStat>();
        }
        return null;
    }
    private static bool IsCurrent(PNStat stat, EveSessionContext session)
    {
        var identity = stat.GetComponent<EveEntityIdentity>();
        var loader = stat.GetComponent<EveEntityLoader>();
        return stat.IsReady && stat.gameObject.activeInHierarchy && identity != null && identity.HasId &&
            identity.SessionId == session.SessionId && loader != null && loader.IsReady &&
            session.Entities.TryGet(identity.Id, out var registered) && ReferenceEquals(registered, loader);
    }
}
