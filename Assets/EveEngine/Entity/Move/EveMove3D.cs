using System;
using UnityEngine;
#if UNITY_5_3_OR_NEWER
using UnityEngine.SceneManagement;
#endif

namespace EveEngine
{
    public enum EveVerticalMotion { Grounded, Rising, Falling }

    // EN: Rigidbody movement on XZ with optional physical jumping and ground sensing.
    // VI: Di chuyển Rigidbody trên XZ, hỗ trợ nhảy vật lý và dò mặt đất.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class EveMove3D : EveMove
    {
        [SerializeField] private LayerMask groundLayers = ~0;
        [SerializeField] private float maxGroundSlope = 50f;
        [SerializeField] private float groundProbeDistance = 0.1f;
        [SerializeField] private float groundProbeRadius = 0.2f;

        private const float ProbeStartAboveFeet = 0.05f;
        private const float VerticalVelocityEpsilon = 0.01f;
        private RaycastHit[] groundHits = new RaycastHit[16];
        private Collider bodyCollider;
        private bool canJump = true;
        private bool isGrounded;
        private bool awaitingGroundDeparture;
        private EveVerticalMotion verticalMotion = EveVerticalMotion.Falling;

        protected Rigidbody body;
        public virtual Rigidbody Body => body;
        public virtual bool CanJump { get => canJump; set => canJump = value; }
        public bool BaseCanJump => canJump;
        public virtual bool IsGrounded => isGrounded;
        public virtual EveVerticalMotion VerticalMotion => verticalMotion;
        protected bool AwaitingGroundDeparture => awaitingGroundDeparture;
        protected void RestoreLocomotionView(bool grounded, byte vertical, bool awaiting)
        { isGrounded=grounded; verticalMotion=(EveVerticalMotion)vertical; awaitingGroundDeparture=awaiting; }
        public virtual float CurrentJumpSpeed => ResolveJumpSpeed();
        public virtual LayerMask GroundLayers { get => groundLayers; set => groundLayers = value; }
        public virtual float MaxGroundSlope
        {
            get => maxGroundSlope;
            set { RequireRange(value, 0f, 90f, nameof(value)); maxGroundSlope = value; }
        }
        public virtual float GroundProbeDistance
        {
            get => groundProbeDistance;
            set { RequireNonNegative(value, nameof(value)); groundProbeDistance = value; }
        }
        public virtual float GroundProbeRadius
        {
            get => groundProbeRadius;
            set { RequirePositive(value, nameof(value)); groundProbeRadius = value; }
        }
        public event Action JumpStarted;
        public event Action Landed;
        protected override bool HasBody => body != null;

        public override void Load(EveEntityLoadContext context)
        {
            RequireRange(maxGroundSlope, 0f, 90f, nameof(maxGroundSlope));
            RequireNonNegative(groundProbeDistance, nameof(groundProbeDistance));
            RequirePositive(groundProbeRadius, nameof(groundProbeRadius));
            base.Load(context);
            if (!stat.Set.TryGetBase(EveBuiltInContentModule.JumpSpeed, out _))
                throw new InvalidOperationException("EveMove3D requires a base jump-speed stat.");
            RequirePositive(ResolveJumpSpeed(), nameof(CurrentJumpSpeed));
            canJump = true;
            awaitingGroundDeparture = false;
            isGrounded = ProbeGround();
            verticalMotion = isGrounded ? EveVerticalMotion.Grounded : EveVerticalMotion.Falling;
        }

        public override void Unload()
        {
            base.Unload();
            JumpStarted = null;
            Landed = null;
            bodyCollider = null;
            canJump = true;
            isGrounded = false;
            awaitingGroundDeparture = false;
            verticalMotion = EveVerticalMotion.Falling;
        }

        protected override void FixedUpdate()
        {
            if (!IsReady || !HasBody) return;
            if (MovementTimeScale > 0f) RefreshGroundState();
            base.FixedUpdate();
        }

        // EN: The 3D variant requires a jump-speed value; 2D movement does not read it.
        // VI: Bản 3D cần giá trị tốc độ nhảy; Move 2D không đọc chỉ số này.
        protected virtual float ResolveJumpSpeed() => PhysicsSpeed(stat.Evaluate(EveBuiltInContentModule.JumpSpeed));

        public virtual bool TryJump()
        {
            if (!IsReady || !HasBody || !CanJump || !isGrounded) return false;
            float speed = CurrentJumpSpeed;
            RequireNonNegative(speed, nameof(CurrentJumpSpeed));
            if (speed == 0f) return false;
            Vector3 velocity = body.linearVelocity;
            body.linearVelocity = new Vector3(velocity.x, speed, velocity.z);
            isGrounded = false;
            awaitingGroundDeparture = true;
            verticalMotion = EveVerticalMotion.Rising;
            Notify(JumpStarted);
            return true;
        }

        private void RefreshGroundState()
        {
            bool groundBelow = ProbeGround();
            float verticalSpeed = body.linearVelocity.y;
            if (!groundBelow) awaitingGroundDeparture = false;
            bool groundedNow = groundBelow && !awaitingGroundDeparture &&
                verticalSpeed <= VerticalVelocityEpsilon;
            bool landedNow = groundedNow && !isGrounded;
            isGrounded = groundedNow;
            verticalMotion = groundedNow ? EveVerticalMotion.Grounded :
                verticalSpeed > VerticalVelocityEpsilon ? EveVerticalMotion.Rising : EveVerticalMotion.Falling;
            if (landedNow) Notify(Landed);
        }

        private bool ProbeGround()
        {
            if (body == null || bodyCollider == null) return false;
            Bounds bounds = bodyCollider.bounds;
            Vector3 origin = new Vector3(bounds.center.x,
                bounds.min.y + groundProbeRadius + ProbeStartAboveFeet, bounds.center.z);
            float distance = groundProbeDistance + ProbeStartAboveFeet;
            int count;
            bool saturated;
            do
            {
#if UNITY_5_3_OR_NEWER
                count = gameObject.scene.GetPhysicsScene().SphereCast(origin,groundProbeRadius,Vector3.down,groundHits,distance,groundLayers,QueryTriggerInteraction.Ignore);
#else
                count = Physics.SphereCastNonAlloc(origin, groundProbeRadius, Vector3.down,
                    groundHits, distance, groundLayers, QueryTriggerInteraction.Ignore);
#endif
                saturated = count == groundHits.Length;
                if (saturated) Array.Resize(ref groundHits, groundHits.Length * 2);
            } while (saturated);
            float minimumNormalY = Mathf.Cos(maxGroundSlope * Mathf.Deg2Rad);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundHits[i];
                if (hit.collider != null && hit.collider.attachedRigidbody != body &&
                    hit.normal.y >= minimumNormalY) return true;
            }
            return false;
        }

        private void Notify(Action listeners)
        {
            if (listeners == null) return;
            foreach (Action handler in listeners.GetInvocationList())
                try { handler(); }
                catch (Exception error) { Debug.LogException(error, this); }
        }

        private static void RequirePositive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequireNonNegative(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequireRange(float value, float min, float max, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                throw new ArgumentOutOfRangeException(name);
        }

        protected override void LoadBody()
        {
            body = GetComponent<Rigidbody>();
            if (body == null) throw new InvalidOperationException("EveMove3D requires Rigidbody.");
            bodyCollider = GetComponent<Collider>();
        }

        protected override void UnloadBody()
        {
            if (body != null) body.linearVelocity = Vector3.zero;
            body = null;
            bodyCollider = null;
        }

        protected override Vector2 PlanarPosition
        {
            get => new Vector2(body.position.x, body.position.z);
            set => body.position = new Vector3(value.x, body.position.y, value.y);
        }

        protected override Vector2 PlanarVelocity
        {
            get => new Vector2(body.linearVelocity.x, body.linearVelocity.z);
            set => body.linearVelocity = new Vector3(value.x, body.linearVelocity.y, value.y);
        }

        public virtual int MoveToXZ(Vector2 target, float speed, float maxDuration) =>
            MoveToPoint(target, speed, maxDuration);

        public virtual int MoveToXZLimitDistance(Vector2 target, float speed, float maxDuration, float distance = 0.5f) =>
            MoveToPointWithStopDistance(target, speed, maxDuration, distance);

        public virtual int TransformToXZ(Vector2 target, float delay) => TeleportToPoint(target, delay);
    }
}
