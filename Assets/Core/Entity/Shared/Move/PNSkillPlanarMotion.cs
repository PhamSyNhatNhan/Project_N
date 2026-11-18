using System;
using EveEngine;
using UnityEngine;
#if UNITY_5_3_OR_NEWER
using UnityEngine.SceneManagement;
#endif

// Gates body ownership separately from the Action's elapsed timeline.
internal sealed class PNSkillPlanarMotion : IDisposable
{
    private readonly PNSkillActionContext context;
    private readonly PNSkill root;
    private readonly PNMove move;
    private readonly Vector3 direction;
    private readonly bool ignoreEntities;
    private PNMoveExecution movementExecution;
    private PNSkillOperation movementLock;
#if UNITY_5_3_OR_NEWER
    private RaycastHit[] sweepHits;
    private Rigidbody movingBody;
    private IDisposable physics;
    private RigidbodyInterpolation previousInterpolation;
    private bool bodyPrepared;
#endif

    private bool TimelineOnly => context.Mode == PNSkillMode.Observed || move?.NetworkRole == PNMoveNetworkRole.Observer;
    private bool RemoteHost => move?.NetworkRole == PNMoveNetworkRole.RemoteOwnerHost;
    private bool CanWritePose => !TimelineOnly && !RemoteHost && PNWorldGameplay.CanExecute(root) &&
        (move == null || movementExecution?.CanWrite == true);

    internal PNSkillPlanarMotion(PNSkillActionContext context, PNMotionSettings settings, Vector3 direction, bool hasOwner)
    {
        this.context = context;
        root = context.Skill;
        move = root.GetComponent<PNMove>();
        this.direction = direction;
        ignoreEntities = settings.IgnoreEntities;
        if (TimelineOnly) return;
        if (!root.Controls.IsAllowed(settings.PermissionId))
            throw new InvalidOperationException("Motion permission denied: " + settings.PermissionId);
        if (move != null && !hasOwner)
            throw new InvalidOperationException("Motion on PNMove requires its execution owner before Start.");
    }

    internal void Begin(EveId permission, Action cancel, Func<PNSkillOperation> createLock)
    {
        if (TimelineOnly) return;
        if (move != null) movementExecution = move.RegisterMotion(permission, cancel);
        movementLock = createLock();
        movementLock.Execute(context, null);
#if UNITY_5_3_OR_NEWER
        if (CanWritePose) PrepareBody();
#endif
    }

    internal bool CanAdvance => PNWorldGameplay.CanExecute(root) &&
        (TimelineOnly || ((move == null || movementExecution?.IsValid == true) && (RemoteHost || CanWritePose)));

#if UNITY_5_3_OR_NEWER
    private void PrepareBody()
    {
        if (bodyPrepared) return;
        movingBody = root.GetComponent<Rigidbody>();
        if (movingBody != null)
        {
            if (move != null)
                physics = move.AcquireMotionPhysics(movementExecution, disableGravity: false);
            else
            {
                previousInterpolation = movingBody.interpolation;
                movingBody.interpolation = RigidbodyInterpolation.None;
            }
            root.transform.position = movingBody.position;
            Physics.SyncTransforms();
        }
        bodyPrepared = true;
    }
#endif

    internal void Step(float step)
    {
        if (!CanWritePose) return;
#if UNITY_5_3_OR_NEWER
        PrepareBody();
        var capsule = root.GetComponent<CapsuleCollider>();
        if (capsule != null && step > 0)
        {
            var bounds = capsule.bounds;
            float r = Math.Max(.02f, Math.Min(bounds.extents.x, bounds.extents.z) * .95f);
            // EN: Leave vertical contact skin: a floor-touching cast can report -direction at distance zero.
            // VI: Chừa khe tiếp xúc dọc; cast chạm sàn có thể trả pháp tuyến -direction và khoảng cách 0.
            float half = Math.Max(0, bounds.extents.y - r - .02f);
            if (sweepHits == null) sweepHits = new RaycastHit[16];
            int count;
            do
            {
                count = root.gameObject.scene.GetPhysicsScene().CapsuleCast(bounds.center + Vector3.up * half,
                    bounds.center - Vector3.up * half, r, direction, sweepHits, step + .03f, ~0, QueryTriggerInteraction.Ignore);
                if (count < sweepHits.Length) break;
                Array.Resize(ref sweepHits, sweepHits.Length * 2);
            } while (true);
            for (int i = 0; i < count; i++)
            {
                var hit = sweepHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(root.transform)) continue;
                if (ignoreEntities && hit.collider.GetComponentInParent<PNStat>() != null) continue;
                // Physical enemy bodies may overlap; this does not disable hurtbox queries.
                if (root.GetComponent<PNEnemyStat>() != null && (hit.collider.GetComponentInParent<PNEnemyStat>() != null ||
                    Physics.GetIgnoreLayerCollision(root.gameObject.layer, hit.collider.gameObject.layer))) continue;
                if (Vector3.Dot(direction, hit.normal) >= -.001f) continue;
                step = Math.Min(step, Math.Max(0, hit.distance - .03f));
            }
        }
        var position = (movingBody != null ? movingBody.position : root.transform.position) + direction * step;
        if (movingBody != null) movingBody.position = position;
        root.transform.position = position;
        Physics.SyncTransforms();
#else
        root.transform.position += direction * step;
#endif
    }

    public void Dispose()
    {
#if UNITY_5_3_OR_NEWER
        if (physics != null) physics.Dispose();
        else if (bodyPrepared && movingBody != null) movingBody.interpolation = previousInterpolation;
        physics = null;
        movingBody = null;
        bodyPrepared = false;
#endif
        movementExecution?.Dispose();
        movementExecution = null;
        movementLock?.Dispose();
        movementLock = null;
    }
}
