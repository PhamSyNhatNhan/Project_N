#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum PNMotionActorCollision { BlockAll, IgnoreEnemyBodies, IgnoreAllActors }

// Implementation helper shared by registered trajectory/contact Actions, not a selected Action itself.
public sealed class PNMotionMotor : IDisposable
{
    private readonly PNMove move;
    private readonly CapsuleCollider capsule;
    private readonly PNMotionActorCollision actors;
    private readonly bool vertical;
    private RaycastHit[] hits = new RaycastHit[32];
    private PNMoveExecution execution;
    private IDisposable physics;
    private bool disposed;
    public Vector3 Position => move.Body.position;
    public bool CanWrite => !disposed && execution?.CanWrite == true;
    public PNMotionOutcome Outcome { get; private set; }
    public PNMotionMotor(PNMove move, EveEngine.EveId permission, PNMotionActorCollision actors, bool vertical, Action cancelled)
    {
        this.move = move ?? throw new ArgumentNullException(nameof(move)); this.actors = actors; this.vertical = vertical;
        if (!Enum.IsDefined(typeof(PNMotionActorCollision), actors) || move.NetworkRole != PNMoveNetworkRole.Local || !move.CanWriteMotion) throw new InvalidOperationException("Trajectory motion requires a local authoritative live body.");
        capsule = move.GetComponent<CapsuleCollider>(); if (capsule == null || capsule.direction != 1) throw new InvalidOperationException("Upright capsule body required.");
        try { execution = move.RegisterMotion(permission, cancelled); physics = move.AcquireMotionPhysics(execution, vertical); }
        catch { Dispose(); throw; }
    }
    public bool Step(Vector3 displacement)
    {
        PNNavigationSettings.Position(displacement);
        if (!CanWrite) return false;
        float distance = displacement.magnitude; if (distance < .000001f) return true;
        Vector3 direction = displacement / distance; var bounds = capsule.bounds;
        float radius = PNAgentGeometry.FootprintRadius(capsule) * .95f;
        float half = Mathf.Max(0, bounds.extents.y - radius - .03f);
        int count = PNAgentPhysicsQuery.CapsuleCast(move.gameObject.scene.GetPhysicsScene(), bounds.center + Vector3.up * half, bounds.center - Vector3.up * half,
            radius, direction, ref hits, distance + .03f, ~0);
        if (count == hits.Length) { Outcome = PNMotionOutcome.BudgetExceeded; return false; }
        float allowed = distance;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(move.transform) || Vector3.Dot(direction, hit.normal) >= -.001f) continue;
            var actor = hit.collider.GetComponentInParent<PNStat>();
            if (actor != null && (actors == PNMotionActorCollision.IgnoreAllActors || actors == PNMotionActorCollision.IgnoreEnemyBodies && actor is PNEnemyStat)) continue;
            if (move.GetComponent<PNEnemyStat>() != null && (actor is PNEnemyStat || Physics.GetIgnoreLayerCollision(capsule.gameObject.layer, hit.collider.gameObject.layer))) continue;
            allowed = Mathf.Min(allowed, Mathf.Max(0, hit.distance - .03f));
        }
        var position = move.Body.position + direction * allowed;
        move.Body.position = position; move.transform.position = position;
        if (vertical) { var velocity = move.Body.linearVelocity; velocity.y = 0; move.Body.linearVelocity = velocity; }
        Physics.SyncTransforms();
        if (allowed + .0001f < distance) { Outcome = PNMotionOutcome.Blocked; return false; }
        return true;
    }
    public void Dispose()
    {
        if (disposed) return;
        physics?.Dispose(); physics = null; execution?.Dispose(); execution = null; disposed = true;
    }
}
#endif
