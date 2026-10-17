using EveEngine;
using UnityEngine;

// EN: Core opts 3D movement into the optional entity clock, including gravity and suspended motion.
// VI: Core nối di chuyển 3D với đồng hồ tùy chọn, bao gồm trọng lực và chuyển động đang tạm dừng.
public partial class PNMove : EveMove3D, EveIEntityLoadPreparation
{
    private PNControlManager controls;
    public void PrepareEntityLoad()
    { if (GetComponent<PNControlManager>() == null) gameObject.AddComponent<PNControlManager>(); }
    public override System.Collections.Generic.IEnumerable<System.Type> Dependencies => new[] { typeof(EveStat), typeof(PNControlManager) };
    private bool Allowed(EveId id) => NetworkAllows(id) && (IsPredictedOwner || controls == null || !controls.IsReady || controls.State.IsAllowed(id));
    public override bool CanMove { get => BaseCanMove && Allowed(PNControlIds.Move); set => base.CanMove = value; }
    public override bool CanFlip { get => BaseCanFlip && Allowed(PNControlIds.FaceControl); set => base.CanFlip = value; }
    public override bool CanJump { get => BaseCanJump && Allowed(PNControlIds.Jump); set => base.CanJump = value; }
    public virtual bool TryFaceSkill(Vector2 direction)
    {
        if (!BaseCanFlip || !Allowed(PNControlIds.FaceSkill)) return false;
        ApplyFacing(direction); return true;
    }
    private void ControlChanged() { if (!CanMove) StopMovement(); }
    public virtual Vector2 LastMoveDirection { get; protected set; } = Vector2.right;

    // EN: Keep continuous planar facing when input stops or an action locks movement.
    // VI: Giu huong lien tuc tren mat phang khi ngung input hoac action khoa di chuyen.
    public override void SetMoveDirection(Vector2 direction)
    {
        base.SetMoveDirection(direction);
        if (MoveDirection.sqrMagnitude > 0f) LastMoveDirection = MoveDirection.normalized;
    }

    private EveTimeScale clock;
    private float previousScale = 1f;
    private bool frozen;
    private Vector3 savedVelocity;
    private Vector3 savedAngularVelocity;
    private RigidbodyConstraints savedConstraints;
    private bool savedGravity;

    public override void Load(EveEntityLoadContext context)
    {
        controls = GetComponent<PNControlManager>();
        if (controls == null || !controls.IsInitialized) throw new System.InvalidOperationException("Movement requires loaded controls.");
        controls.State.Changed += ControlChanged;
        clock = GetComponent<EveTimeScale>();
        if (clock != null) ConfigureTimeScale(clock);
        previousScale = 1f;
        base.Load(context);
        EnsureMovementHold();
    }

    protected virtual void ConfigureTimeScale(EveTimeScale timeScale) { }
    protected override float ResolveTimeScale() => IsPredictedOwner ? NetworkScale : clock != null && clock.isActiveAndEnabled ? clock.Scale : 1f;
    protected override float ResolveMoveSpeed() => IsPredictedOwner ? NetworkMoveSpeed : base.ResolveMoveSpeed();
    protected override float ResolveJumpSpeed() => (IsPredictedOwner ? NetworkJumpSpeed : base.ResolveJumpSpeed()) * MovementTimeScale;

    protected override void FixedUpdate()
    {
        NetworkFixedBoundary();
        if (!IsReady || body == null) return;
        float scale = MovementTimeScale;
        SyncPhysicsScale(scale);
        base.FixedUpdate();
        // EN: v scales by s and gravity by s², preserving the jump arc as time changes.
        // VI: v nhân s và trọng lực nhân s², giữ quỹ đạo nhảy khi thay đổi thời gian.
        if (scale > 0f && body.useGravity && !body.isKinematic && scale != 1f)
            body.AddForce(Physics.gravity * (scale * scale - 1f), ForceMode.Acceleration);
    }

    public override bool TryJump()
    {
        if (!IsReady || body == null || MovementTimeScale == 0f) return false;
        float scale = MovementTimeScale;
        SyncPhysicsScale(scale);
        return base.TryJump();
    }

    private void SyncPhysicsScale(float scale)
    {
        if (scale == 0f)
        {
            if (!frozen)
            {
                savedVelocity = body.linearVelocity / previousScale;
                savedAngularVelocity = body.angularVelocity / previousScale;
                savedConstraints = body.constraints;
                savedGravity = body.useGravity;
                frozen = true;
            }
            body.constraints = RigidbodyConstraints.FreezeAll;
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            return;
        }
        if (frozen)
        {
            body.constraints = savedConstraints;
            body.useGravity = savedGravity;
            body.linearVelocity = savedVelocity * scale;
            body.angularVelocity = savedAngularVelocity * scale;
            frozen = false;
        }
        else if (scale != previousScale)
        {
            body.linearVelocity *= scale / previousScale;
            body.angularVelocity *= scale / previousScale;
        }
        previousScale = scale;
    }

    private void RestorePhysics()
    {
        if (body != null && frozen)
        {
            body.constraints = savedConstraints;
            body.useGravity = savedGravity;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        else if (body != null && previousScale != 1f)
        {
            body.linearVelocity /= previousScale;
            body.angularVelocity /= previousScale;
        }
        frozen = false;
        previousScale = 1f;
    }

    protected override void OnDisable() { if(managedMovement){movementHeld=true;initialMovementApplied=false;EnsureMovementHold();}DetachNetwork(); RestorePhysics(); base.OnDisable(); }
    public override void Unload()
    {
        ReleaseNetworkMovement();
        if (controls != null && controls.IsReady) controls.State.Changed -= ControlChanged;
        controls = null;
        RestorePhysics();
        base.Unload();
        clock = null;
        LastMoveDirection = Vector2.right;
    }
}
