using System.Collections.Generic;
using UnityEngine;

// Who the Starchanter helps and where. Pure selection over the stats it is given (no world access, no physics), so the smoke suite can run it: the callers pass the entities of the world.
// An ally is a living entity with health on the Starchanter's own layer (PNStat.SharesLayer); the Starchanter itself counts where a method says so.
public static class PNStarchanterSupport
{
    // Hurt means at most this fraction of the maximum health (the plan's default; the registered payload does not carry it).
    public const double HurtFraction = .95;
    public static bool IsAlly(PNStat caster, PNStat other) => caster != null && other != null && other.IsReady && !other.IsDead && other.HasHealth && caster.SharesLayer(other);
    // A living hostile of `caster`: one it targets, that body.aggro does not exclude, and that is not on its own layer.
    public static bool IsHostileTo(PNStat caster, PNStat other) => caster != null && other != null && !ReferenceEquals(caster, other) && other.IsReady && !other.IsDead && other.HasHealth && caster.Targets(other) && other.AggroMode != PNAggroMode.Ignored && !caster.SharesLayer(other);
    public static bool IsHurt(PNStat stat) => stat != null && stat.IsReady && !stat.IsDead && stat.HasHealth && stat.GetCurrent(PNContentIds.Health) < stat.GetMax(PNContentIds.Health) * HurtFraction - 1e-9;
    public static float Flat(Vector3 a, Vector3 b) { var d = a - b; d.y = 0f; return d.magnitude; }

    // The hurt ally or the caster itself with the LOWEST absolute current health (ties: the lower entity id), or null when nobody is hurt.
    public static PNStat PickHealTarget(PNStat caster, IEnumerable<PNStat> all)
    {
        PNStat best = null;
        foreach (var s in all)
        {
            if (s == null || !(ReferenceEquals(s, caster) || IsAlly(caster, s)) || !IsHurt(s)) continue;
            if (best == null) { best = s; continue; }
            double a = s.GetCurrent(PNContentIds.Health), b = best.GetCurrent(PNContentIds.Health);
            if (a < b - 1e-9 || System.Math.Abs(a - b) <= 1e-9 && s.EntityId.Value < best.EntityId.Value) best = s;
        }
        return best;
    }

    // Allies within `radius` (planar) of `center`, plus the caster itself wherever it stands inside it.
    public static List<PNStat> Within(PNStat caster, Vector3 center, float radius, IEnumerable<PNStat> all)
    {
        var list = new List<PNStat>();
        foreach (var s in all)
            if (s != null && (ReferenceEquals(s, caster) && s.IsReady && !s.IsDead && s.HasHealth || IsAlly(caster, s)) && Flat(s.transform.position, center) <= radius + 1e-6f) list.Add(s);
        return list;
    }

    // The nearest living ally (never the caster) within `maxDistance`, or null.
    public static PNStat NearestAlly(PNStat caster, IEnumerable<PNStat> all, float maxDistance)
    {
        PNStat best = null; float bestDistance = maxDistance + 1e-6f;
        foreach (var s in all)
        {
            if (!IsAlly(caster, s)) continue;
            float d = Flat(s.transform.position, caster.transform.position);
            if (d < bestDistance - 1e-6f || best != null && System.Math.Abs(d - bestDistance) <= 1e-6f && s.EntityId.Value < best.EntityId.Value) { best = s; bestDistance = d; }
        }
        return best;
    }

    // The nearest living hostile (an entity the caster targets, aggro-eligible, never an ally) within `maxDistance` that also passes `extra` (line of sight, world rules), or null.
    public static PNStat NearestHostile(PNStat caster, IEnumerable<PNStat> all, float maxDistance, System.Func<PNStat, bool> extra = null)
    {
        PNStat best = null; float bestDistance = maxDistance + 1e-6f;
        foreach (var s in all)
        {
            if (s == null || ReferenceEquals(s, caster) || !s.IsReady || s.IsDead || !s.HasHealth || !caster.Targets(s) || s.AggroMode == PNAggroMode.Ignored || caster.SharesLayer(s)) continue;
            float d = Flat(s.transform.position, caster.transform.position); if (d > bestDistance) continue;
            if (extra != null && !extra(s)) continue;
            if (best == null || d < bestDistance - 1e-6f || System.Math.Abs(d - bestDistance) <= 1e-6f && s.EntityId.Value < best.EntityId.Value) { best = s; bestDistance = d; }
        }
        return best;
    }

    // A recipient's heal for one tick: a fraction of ITS OWN maximum health.
    public static double HealAmount(PNStat target, double fraction) => target.GetMax(PNContentIds.Health) * fraction;

    // Where the Starchanter lands next to `ally`: `distance` away on the side it comes from.
    public static Vector3 Landing(Vector3 ally, Vector3 from, float distance)
    {
        var side = from - ally; side.y = 0f; float length = side.magnitude;
        side = length > 1e-3f ? side * (1f / length) : Vector3.right;
        return ally + side * distance;
    }
}
