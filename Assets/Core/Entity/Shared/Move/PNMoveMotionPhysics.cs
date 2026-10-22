#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

public partial class PNMove
{
    private MotionPhysicsLease motionPhysics;
    public IDisposable AcquireMotionPhysics(PNMoveExecution execution, bool disableGravity)
    {
        if (loadContext?.CanUse != true || body == null || execution == null || !execution.IsValid || !motionExecutions.Contains(execution) || motionPhysics != null)
            throw new InvalidOperationException("Physics lease requires this move's exclusive live motion.");
        return motionPhysics = new MotionPhysicsLease(this, loadContext, disableGravity);
    }
    private sealed class MotionPhysicsLease : IDisposable
    {
        private PNMove owner;
        private readonly EveEntityLoadContext load;
        private readonly bool gravity;
        private readonly RigidbodyInterpolation interpolation;
        public MotionPhysicsLease(PNMove owner, EveEntityLoadContext load, bool disableGravity)
        {
            this.owner = owner; this.load = load; gravity = owner.frozen ? owner.savedGravity : owner.body.useGravity;
            interpolation = owner.body.interpolation; owner.body.interpolation = RigidbodyInterpolation.None;
            if (disableGravity)
            {
                if (owner.frozen) { owner.savedGravity = false; owner.savedVelocity.y = 0; }
                else { owner.body.useGravity = false; var velocity = owner.body.linearVelocity; velocity.y = 0; owner.body.linearVelocity = velocity; }
            }
        }
        public void Dispose()
        {
            var previous = owner; if (previous == null) return;
            if (ReferenceEquals(previous.motionPhysics, this) && ReferenceEquals(previous.loadContext, load))
            {
                if (previous.body != null)
                {
                    if (previous.frozen) previous.savedGravity = gravity; else previous.body.useGravity = gravity;
                    previous.body.interpolation = interpolation;
                }
                previous.motionPhysics = null;
            }
            owner = null;
        }
    }
}
#endif
