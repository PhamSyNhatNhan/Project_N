using System;
using System.Collections.Generic;
using UnityEngine;

namespace EveEngine
{
    // EN: The currently running special movement, separate from ordinary directional movement.
    // VI: Kiểu di chuyển đặc biệt đang chạy, tách khỏi di chuyển theo hướng thông thường.
    public enum EveMoveMode { None, Direction, Target, TargetWithStopDistance, Teleport }

    // EN: Why a special movement command ended.
    // VI: Lý do một lệnh di chuyển đặc biệt kết thúc.
    public enum EveMoveEndReason { Completed, TimedOut, Cancelled }

    // EN: Immutable result for one special movement command.
    // VI: Kết quả bất biến của một lệnh di chuyển đặc biệt.
    public readonly struct EveMoveResult
    {
        public int CommandId { get; }
        public EveMoveMode Mode { get; }
        public EveMoveEndReason Reason { get; }

        public EveMoveResult(int commandId, EveMoveMode mode, EveMoveEndReason reason)
        {
            CommandId = commandId;
            Mode = mode;
            Reason = reason;
        }
    }

    // EN: Shared planar command and lifecycle logic for 2D and 3D movement bodies.
    // VI: Logic lệnh trên mặt phẳng và vòng đời dùng chung cho body di chuyển 2D và 3D.
    public abstract class EveMove : MonoBehaviour, EveIEntityLoadStep
    {
        protected EveEntityLoader loader;
        protected EveEntityLoadContext loadContext;
        protected EveStat stat;

        private Vector2 moveDirection;
        private Vector2 moveSnap = Vector2.right;
        private int facingDirection = 1;
        private bool canMove = true;
        private bool canFlip = true;
        private bool isMoving;
        private EveMoveMode mode;
        private int commandId;
        private int nextCommandId;
        private Vector2 commandDirection;
        private Vector2 targetPosition;
        private float commandSpeed;
        private float remainingTime;
        private float stopDistance;

        public virtual int LoadOrder => 10;
        public virtual IEnumerable<Type> Dependencies => new[] { typeof(EveStat) };
        protected virtual bool CanExecuteMovement => loadContext != null && loadContext.CanMutate;
        public virtual bool IsReady => loadContext != null && CanExecuteMovement && loader != null && loader.IsReady;
        public virtual EveMoveMode Mode => mode;
        public virtual bool IsMoving => isMoving;
        public virtual Vector2 MoveDirection => moveDirection;
        public virtual Vector2 MoveSnap => moveSnap;
        public virtual int FacingDirection => facingDirection;
        public virtual bool CanFlip { get => canFlip; set => canFlip = value; }
        public bool BaseCanFlip => canFlip;
        public bool BaseCanMove => canMove;
        public virtual bool CanMove
        {
            get => canMove;
            set
            {
                if (canMove == value) return;
                canMove = value;
                if (!value) StopMovement();
            }
        }
        public virtual float CurrentMoveSpeed => ResolveMoveSpeed();
        protected abstract bool HasBody { get; }
        protected abstract void LoadBody();
        protected abstract void UnloadBody();
        protected abstract Vector2 PlanarPosition { get; set; }
        protected abstract Vector2 PlanarVelocity { get; set; }

        // EN: Subscribe to results while the entity is loaded; Unload clears subscribers.
        // VI: Đăng ký kết quả khi entity đang nạp; Unload xóa các listener.
        public event Action<EveMoveResult> CommandEnded;
        public event Action PoseRelocated;

        public virtual void Load(EveEntityLoadContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (loadContext != null) throw new InvalidOperationException("EveMove is already loaded.");
            if (GetComponents<EveMove>().Length != 1)
                throw new InvalidOperationException("An entity must have exactly one EveMove variant.");
            loader = GetComponent<EveEntityLoader>();
            if (loader == null) throw new InvalidOperationException("EveMove requires EveEntityLoader.");
            stat = GetComponent<EveStat>();
            if (stat == null || !stat.IsInitialized) throw new InvalidOperationException("EveMove requires initialized EveStat.");
            if (!stat.Set.TryGetBase(EveBuiltInContentModule.MoveSpeed, out _))
                throw new InvalidOperationException("EveMove requires a base move-speed stat.");
            RequireNonNegativeFinite(ResolveMoveSpeed(), nameof(CurrentMoveSpeed));
            LoadBody();
            if (!HasBody) throw new InvalidOperationException("EveMove requires a physics body.");
            loadContext = context;
            ResetState();
            ApplyVelocity(Vector2.zero);
        }

        public virtual void Unload()
        {
            StopMovement();
            CommandEnded = null;
            PoseRelocated = null;
            loadContext = null;
            loader = null;
            stat = null;
            UnloadBody();
            ResetState();
        }

        protected virtual void OnDisable() => StopMovement();

        // EN: Read the current movement value so stat modifiers take effect on the next physics step.
        // VI: Đọc tốc độ hiện tại để modifier có hiệu lực ở bước vật lý tiếp theo.
        protected virtual float ResolveMoveSpeed() => PhysicsSpeed(stat.Evaluate(EveBuiltInContentModule.MoveSpeed));
        protected static float PhysicsSpeed(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value > float.MaxValue || value < -float.MaxValue)
                throw new OverflowException("Movement speed is outside the physics float range.");
            return (float)value;
        }
        protected virtual float TickDeltaTime => Time.fixedDeltaTime;

        // EN: Games opt into a clock by overriding this hook; the engine requires no time component.
        // VI: Game nối đồng hồ bằng điểm mở này; engine không yêu cầu component thời gian.
        protected virtual float ResolveTimeScale() => 1f;
        public virtual float MovementTimeScale
        {
            get { float value = ResolveTimeScale(); RequireNonNegativeFinite(value, nameof(MovementTimeScale)); return value; }
        }

        protected virtual void FixedUpdate()
        {
            if (!IsReady || !HasBody) return;
            if (MovementTimeScale == 0f)
            {
                ApplyVelocity(Vector2.zero);
                isMoving = false;
                return;
            }
            if (!CanMove)
            {
                ApplyVelocity(Vector2.zero);
                return;
            }

            float deltaTime = TickDeltaTime * MovementTimeScale;
            RequirePositiveFinite(deltaTime, nameof(TickDeltaTime));
            if (mode == EveMoveMode.None)
            {
                float speed = CurrentMoveSpeed;
                RequireNonNegativeFinite(speed, nameof(CurrentMoveSpeed));
                MovementSetUp(moveDirection, speed);
            }
            else
            {
                TickCommand(deltaTime);
            }
            TrackMotion();
        }

        // EN: Direction magnitude up to one supports analog input; the game decides where input comes from.
        // VI: Độ lớn hướng tối đa một hỗ trợ input analog; game quyết định nguồn input.
        public virtual void SetMoveDirection(Vector2 direction)
        {
            RequireFinite(direction, nameof(direction));
            moveDirection = Vector2.ClampMagnitude(direction, 1f);
        }

        public virtual void SetMoveSnap(Vector2 direction)
        {
            RequireFinite(direction, nameof(direction));
            if (direction.sqrMagnitude <= 0f) throw new ArgumentException("Direction cannot be zero.", nameof(direction));
            moveSnap = direction.normalized;
        }

        protected virtual void MovementSetUp(Vector2 direction, float speed) => ApplyVelocity(direction * speed);
        protected virtual void ApplyVelocity(Vector2 velocity) => PlanarVelocity = velocity * MovementTimeScale;

        // EN: The next command replaces the previous one and reports its cancellation.
        // VI: Lệnh mới thay lệnh cũ và thông báo lệnh cũ đã bị hủy.
        public virtual int MoveTo(float speed, float duration)
        {
            ValidateCommand(speed, duration);
            return BeginCommand(EveMoveMode.Direction, moveSnap, default, speed, duration, 0f);
        }

        public virtual int MoveToPoint(Vector2 target, float speed, float maxDuration)
        {
            RequireFinite(target, nameof(target));
            ValidateCommand(speed, maxDuration);
            return BeginCommand(EveMoveMode.Target, default, target, speed, maxDuration, 0f);
        }

        public virtual int MoveToPointWithStopDistance(Vector2 target, float speed, float maxDuration, float distance = 0.5f)
        {
            RequireFinite(target, nameof(target));
            RequireNonNegativeFinite(distance, nameof(distance));
            ValidateCommand(speed, maxDuration);
            return BeginCommand(EveMoveMode.TargetWithStopDistance, default, target, speed, maxDuration, distance);
        }

        public virtual int TeleportToPoint(Vector2 target, float delay)
        {
            RequireFinite(target, nameof(target));
            RequireNonNegativeFinite(delay, nameof(delay));
            EnsureCanStart();
            int id = BeginCommand(EveMoveMode.Teleport, default, target, 0f, delay, 0f);
            if (delay == 0f && MovementTimeScale > 0f) FinishTeleport();
            return id;
        }

        public virtual bool CancelCommand()
        {
            if (mode == EveMoveMode.None) return false;
            EndCommand(EveMoveEndReason.Cancelled);
            return true;
        }

        public virtual void StopMovement()
        {
            moveDirection = Vector2.zero;
            CancelCommand();
            if (HasBody) ApplyVelocity(Vector2.zero);
            isMoving = false;
        }

        // EN: Games may call this from player or enemy logic; movement does not choose targets itself.
        // VI: Game có thể gọi từ logic player hoặc enemy; Move không tự chọn mục tiêu.
        public virtual void FaceDirection(Vector2 direction)
        {
            RequireFinite(direction, nameof(direction));
            if (!CanFlip) return;
            ApplyFacing(direction);
        }

        protected void ApplyFacing(Vector2 direction)
        {
            RequireFinite(direction, nameof(direction));
            if (direction.x == 0f) return;
            int desired = direction.x > 0f ? 1 : -1;
            if (desired != facingDirection) Flip();
        }

        // Simulation correction bypasses admission permissions, not entity lifecycle.
        protected void RestoreFacing(int facing)
        { if (facing != 1 && facing != -1) throw new ArgumentOutOfRangeException(nameof(facing)); facingDirection = facing; }

        protected virtual void Flip()
        {
            facingDirection = -facingDirection;
        }

        private void TickCommand(float deltaTime)
        {
            if (mode == EveMoveMode.Teleport)
            {
                ApplyVelocity(Vector2.zero);
                if (remainingTime <= 0f) FinishTeleport();
                else remainingTime -= deltaTime;
                return;
            }

            if (mode != EveMoveMode.Direction)
            {
                float gap = Vector2.Distance(PlanarPosition, targetPosition) - stopDistance;
                if (gap <= (mode == EveMoveMode.Target ? 0.1f : 0.0001f))
                {
                    ApplyVelocity(Vector2.zero);
                    EndCommand(EveMoveEndReason.Completed);
                    return;
                }
            }
            if (remainingTime <= 0f)
            {
                ApplyVelocity(Vector2.zero);
                EndCommand(mode == EveMoveMode.Direction ? EveMoveEndReason.Completed : EveMoveEndReason.TimedOut);
                return;
            }

            Vector2 direction = mode == EveMoveMode.Direction
                ? commandDirection : (targetPosition - PlanarPosition).normalized;
            float speed = commandSpeed;
            speed *= Math.Min(1f, remainingTime / deltaTime);
            if (mode != EveMoveMode.Direction)
            {
                float availableDistance = Math.Max(0f, Vector2.Distance(PlanarPosition, targetPosition) - stopDistance);
                speed = Math.Min(speed, availableDistance / deltaTime);
            }
            ApplyVelocity(direction * speed);
            remainingTime -= deltaTime;
        }

        private void FinishTeleport()
        {
            if (mode != EveMoveMode.Teleport) return;
            int relocatedCommand = commandId;
            ApplyVelocity(Vector2.zero);
            PlanarPosition = targetPosition;
            foreach(Action observer in PoseRelocated?.GetInvocationList() ?? Array.Empty<Delegate>())
                try { observer(); } catch(Exception error) { Debug.LogException(error,this); }
            if(mode==EveMoveMode.Teleport && commandId==relocatedCommand)EndCommand(EveMoveEndReason.Completed);
        }

        private int BeginCommand(EveMoveMode newMode, Vector2 direction, Vector2 target, float speed, float duration, float distance)
        {
            EnsureCanStart();
            EveMoveResult? cancelled = mode == EveMoveMode.None
                ? (EveMoveResult?)null
                : new EveMoveResult(commandId, mode, EveMoveEndReason.Cancelled);
            mode = newMode;
            commandId = ++nextCommandId;
            int startedId = commandId;
            commandDirection = direction;
            targetPosition = target;
            commandSpeed = speed;
            remainingTime = duration;
            stopDistance = distance;
            ApplyVelocity(Vector2.zero);
            isMoving = false;
            if (cancelled.HasValue) NotifyCommandEnded(cancelled.Value);
            return startedId;
        }

        private void EndCommand(EveMoveEndReason reason)
        {
            int endedId = commandId;
            EveMoveMode endedMode = mode;
            mode = EveMoveMode.None;
            commandId = 0;
            ApplyVelocity(Vector2.zero);
            isMoving = false;
            NotifyCommandEnded(new EveMoveResult(endedId, endedMode, reason));
        }

        private void NotifyCommandEnded(EveMoveResult result)
        {
            if (CommandEnded == null) return;
            foreach (Action<EveMoveResult> handler in CommandEnded.GetInvocationList())
                try { handler(result); }
                catch (Exception error) { Debug.LogException(error, this); }
        }

        private void TrackMotion()
        {
            Vector2 velocity = PlanarVelocity;
            isMoving = velocity.sqrMagnitude > 0f;
            if (isMoving) moveSnap = velocity.normalized;
        }

        private void ResetState()
        {
            moveDirection = Vector2.zero;
            moveSnap = Vector2.right;
            facingDirection = 1;
            canMove = true;
            canFlip = true;
            isMoving = false;
            mode = EveMoveMode.None;
            commandId = 0;
            nextCommandId = 0;
            commandDirection = Vector2.zero;
            targetPosition = Vector2.zero;
            commandSpeed = 0f;
            remainingTime = 0f;
            stopDistance = 0f;
        }

        private void EnsureCanStart()
        {
            if (!IsReady || !HasBody) throw new InvalidOperationException("EveMove is not ready.");
            if (!CanMove) throw new InvalidOperationException("Movement is locked.");
        }

        private void ValidateCommand(float speed, float duration)
        {
            RequirePositiveFinite(speed, nameof(speed));
            RequirePositiveFinite(duration, nameof(duration));
            EnsureCanStart();
        }

        private static void RequireFinite(Vector2 value, string name)
        {
            if (!Finite(value.x) || !Finite(value.y)) throw new ArgumentOutOfRangeException(name);
        }

        private static void RequireNonNegativeFinite(float value, string name)
        {
            if (!Finite(value) || value < 0f) throw new ArgumentOutOfRangeException(name);
        }

        private static void RequirePositiveFinite(float value, string name)
        {
            if (!Finite(value) || value <= 0f) throw new ArgumentOutOfRangeException(name);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
