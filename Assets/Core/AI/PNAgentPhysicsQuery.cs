#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// Caller-owned arrays retain their high-water capacity. Saturation remains conservative
// at the finite limit; a full initial buffer is not evidence of an actual obstacle.
public static class PNAgentPhysicsQuery
{
    public const int MaximumAutomaticCapacity = 256;
    private static void RequireBuffer<T>(T[] buffer)
    { if (buffer == null || buffer.Length == 0) throw new ArgumentException("A nonempty owned query buffer is required."); }
    private static bool Grow<T>(ref T[] buffer)
    {
        if (buffer == null || buffer.Length == 0) throw new ArgumentException("A nonempty owned query buffer is required.");
        if (buffer.Length >= MaximumAutomaticCapacity) return false;
        Array.Resize(ref buffer, Math.Min(MaximumAutomaticCapacity, buffer.Length * 2)); return true;
    }
    public static int CapsuleCast(PhysicsScene scene, Vector3 top, Vector3 bottom, float radius, Vector3 direction,
        ref RaycastHit[] hits, float distance, int mask)
    {
        RequireBuffer(hits);
        int count;
        do { count = scene.CapsuleCast(top, bottom, radius, direction, hits, distance, mask, QueryTriggerInteraction.Ignore); }
        while (count == hits.Length && Grow(ref hits));
        return count;
    }
    public static int SphereCast(PhysicsScene scene, Vector3 origin, float radius, Vector3 direction,
        ref RaycastHit[] hits, float distance, int mask)
    {
        RequireBuffer(hits);
        int count;
        do { count = scene.SphereCast(origin, radius, direction, hits, distance, mask, QueryTriggerInteraction.Ignore); }
        while (count == hits.Length && Grow(ref hits));
        return count;
    }
    public static int Raycast(PhysicsScene scene, Vector3 origin, Vector3 direction, ref RaycastHit[] hits, float distance, int mask)
    {
        RequireBuffer(hits);
        int count;
        do { count = scene.Raycast(origin, direction, hits, distance, mask, QueryTriggerInteraction.Ignore); }
        while (count == hits.Length && Grow(ref hits));
        return count;
    }
    public static int OverlapCapsule(PhysicsScene scene, Vector3 top, Vector3 bottom, float radius, ref Collider[] hits, int mask)
    {
        RequireBuffer(hits);
        int count;
        do { count = scene.OverlapCapsule(top, bottom, radius, hits, mask, QueryTriggerInteraction.Ignore); }
        while (count == hits.Length && Grow(ref hits));
        return count;
    }
}
#endif
