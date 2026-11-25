#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum PNMotionActorCollision { BlockAll, IgnoreEnemyBodies, IgnoreAllActors }

// EN: The one service that moves a body for an Action (Docs/BodyMotionPlan.md, M1). It is the only place that knows
//     - the machine's ROLE for that body: a machine that writes the pose (a Local body, or the predicted Owner of a player) runs a motor; the host's view of a client's player
//       (RemoteOwnerHost) and an observer only run the clock (`ShowsOnly`), the owner's machine writes the pose and sends it;
//     - the exclusive motion registration and the physics lease (gravity off for a vertical trajectory, handed back with a zero vertical speed);
//     - the capsule sweep with the actor collision policy and the contact skin (`Clip`, shared by the planar and the trajectory paths).
//     Actions describe a path and call `Step`; they never read the role, never write a Rigidbody. `PNSkillPlanarMotion` (PNMotionAction: Dash, Eve) and the trajectory Action (Hop, curves, orbits) both stand on it.
// VI: Dich vu duy nhat dua than di chuyen cho mot Action: biet vai tro cua may doi voi than (ghi pose hay chi chay dong ho), dang ky chuyen dong doc quyen, lease vat ly va quet capsule theo chinh sach va cham.
public sealed class PNBodyMotion : IDisposable
{
    // Skin kept between the capsule and what it touches. The trajectory path keeps .03; the planar path keeps its own .02 (an unchanged feel for Dash and Eve).
    public const float TrajectorySkin = .03f, PlanarSkin = .02f;

    // True when this machine does not write the body's pose and only shows the run: a replay (`Observed` mode), the host's view of a client's player, or an observer.
    // The owner's machine writes the pose and sends it, so the clock alone runs here and the Action reads no position.
    public static bool ShowsOnly(PNMove move, PNSkillMode mode)
        => mode == PNSkillMode.Observed || move != null && (move.NetworkRole == PNMoveNetworkRole.RemoteOwnerHost || move.NetworkRole == PNMoveNetworkRole.Observer);

    // True for a body whose pose the host sends to its owner's machine to run (the host does not write it): the host runs the clock of a voluntary motion there.
    public static bool IsRemoteOwnerBody(PNMove move) => move != null && move.NetworkRole == PNMoveNetworkRole.RemoteOwnerHost;

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

    // `vertical`: the path moves in y itself, so gravity is off while the lease is held. `cancelled`: what to do when the move drops the motion (a correction, a lost permission).
    public PNBodyMotion(PNMove move, EveEngine.EveId permission, PNMotionActorCollision actors, bool vertical, Action cancelled)
    {
        this.move = move ?? throw new ArgumentNullException(nameof(move)); this.actors = actors; this.vertical = vertical;
        if (!Enum.IsDefined(typeof(PNMotionActorCollision), actors) || (move.NetworkRole != PNMoveNetworkRole.Local && move.NetworkRole != PNMoveNetworkRole.Owner) || !move.CanWriteMotion)
            throw new InvalidOperationException("Body motion requires a live body this machine writes (local, or the predicted owner).");
        capsule = move.GetComponent<CapsuleCollider>(); if (capsule == null || capsule.direction != 1) throw new InvalidOperationException("Upright capsule body required.");
        try { execution = move.RegisterMotion(permission, cancelled); physics = move.AcquireMotionPhysics(execution, vertical); }
        catch { Dispose(); throw; }
    }

    public bool Step(Vector3 displacement)
    {
        PNNavigationSettings.Position(displacement);
        if (!CanWrite) return false;
        float distance = displacement.magnitude; if (distance < .000001f) return true;
        Vector3 direction = displacement / distance;
        float allowed = Clip(move.gameObject.scene, capsule, move, direction, distance, actors, TrajectorySkin, ref hits, out bool overflow);
        if (overflow) { Outcome = PNMotionOutcome.BudgetExceeded; return false; }
        var position = move.Body.position + direction * allowed;
        move.Body.position = position; move.transform.position = position;
        if (vertical) { var velocity = move.Body.linearVelocity; velocity.y = 0; move.Body.linearVelocity = velocity; }
        Physics.SyncTransforms();
        if (allowed + .0001f < distance) { Outcome = PNMotionOutcome.Blocked; return false; }
        return true;
    }

    // How far `capsule` may travel along `direction` (at most `distance`) before it touches something that blocks it under `actors`. `overflow`: more things met than the buffer holds (the caller reports it).
    // The shared sweep: both the planar motion and the trajectories call it, so the skin, the "moving away from it" rule and the enemy-body rule exist once.
    public static float Clip(Scene scene, CapsuleCollider capsule, Component owner, Vector3 direction, float distance, PNMotionActorCollision actors, float skin, ref RaycastHit[] hits, out bool overflow)
    {
        overflow = false;
        var bounds = capsule.bounds;
        float radius = PNAgentGeometry.FootprintRadius(capsule) * .95f;
        float half = Mathf.Max(0, bounds.extents.y - radius - skin);
        int count = PNAgentPhysicsQuery.CapsuleCast(scene.GetPhysicsScene(), bounds.center + Vector3.up * half, bounds.center - Vector3.up * half,
            radius, direction, ref hits, distance + .03f, ~0);
        if (count == hits.Length) { overflow = true; return 0f; }
        bool enemyBody = owner.GetComponent<PNEnemyStat>() != null;
        float allowed = distance;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(owner.transform) || Vector3.Dot(direction, hit.normal) >= -.001f) continue;
            var actor = hit.collider.GetComponentInParent<PNStat>();
            if (actor != null && (actors == PNMotionActorCollision.IgnoreAllActors || actors == PNMotionActorCollision.IgnoreEnemyBodies && actor is PNEnemyStat)) continue;
            if (enemyBody && (actor is PNEnemyStat || Physics.GetIgnoreLayerCollision(capsule.gameObject.layer, hit.collider.gameObject.layer))) continue;
            allowed = Mathf.Min(allowed, Mathf.Max(0, hit.distance - .03f));
        }
        return allowed;
    }

    public void Dispose()
    {
        if (disposed) return;
        physics?.Dispose(); physics = null; execution?.Dispose(); execution = null; disposed = true;
    }
}
#endif
