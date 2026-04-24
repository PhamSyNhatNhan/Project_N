using System;
using UnityEngine;

[RequireComponent(typeof(TimeScale))]
public class Move : MonoBehaviour, ILoadable<MoveData>
{
    protected Rigidbody2D rb;
    protected Stat        stat;
    protected TimeScale   timeScale;

    protected bool canMove   = true;
    protected bool isMove    = false;

    protected int  flipDirect = 1;
    protected bool canFlip    = true;

    // Hướng di chuyển cuối
    protected Vector2 moveSnap = Vector2.right;

    // Target dùng cho MoveToXY / MoveToXYLimitDistance / TransformToXY
    protected Vector2 targetPosition = Vector2.zero;

    // Kiểm soát di chuyển đặc biệt
    private bool isMoveTo = false;

    // MoveTo — di chuyển theo moveSnap
    private bool  isMoveToDefault  = false;
    private float velocityMoveTo   = 500f;
    private float timeMoveTo       = 0.15f;

    // MoveToXY — di chuyển đến tọa độ đích
    private bool    isMoveToXY       = false;
    private Vector2 targetXY         = Vector2.zero;
    private float   velocityMoveToXY = 500f;
    private float   timeMoveToXY     = 0.15f;

    // MoveToXYLimitDistance — di chuyển đến gần đích rồi dừng
    private bool    isMoveToXYLimit     = false;
    private Vector2 targetXYLimit       = Vector2.zero;
    private float   velocityMoveToLimit = 500f;
    private float   timeMoveToLimit     = 0.15f;
    private float   stopDistance        = 0.5f;

    // TransformToXY — dịch chuyển tức thời sau delay
    private bool    isTransformToXY   = false;
    private Vector2 targetTransformXY = Vector2.zero;
    private float   transformDelay    = 0f;

    // ── Move Speed ────────────────────────────────────────────────
    [Header("MoveSpeed")]
    [SerializeField] protected float baseMoveSpeed = 5f;
    protected float curMoveSpeed;

    // ── Raw data cache — subclass đọc extraFields trong ApplyData ─
    protected MoveData rawData;

    // DeltaTime luôn hỏi TimeScale
    protected float FixedDeltaTime => timeScale.FixDeltaTime;

    private string entityKey => stat != null
        ? $"{stat.NameCharacter}_{stat.GetInstanceID()}"
        : gameObject.name;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected virtual void Awake()
    {
        rb        = GetComponent<Rigidbody2D>();
        stat      = GetComponent<Stat>();
        timeScale = GetComponent<TimeScale>();
    }

    protected virtual void Start()
    {
        // Nếu không có EntityLoader thì tự init
        if (!GetComponent<EntityLoader>())
            ApplyData();
    }

    protected virtual void Update() { }

    protected virtual void FixedUpdate()
    {
        Movement();
        SnapMove();
    }

    // ── ILoadable ─────────────────────────────────────────────────
    public void LoadRawData(MoveData data)
    {
        rawData      = data;
        baseMoveSpeed = data.baseMoveSpeed;
    }

    public virtual void ApplyData()
    {
        curMoveSpeed = baseMoveSpeed;
    }

    // ── Movement Core ─────────────────────────────────────────────
    protected virtual void Movement()
    {
        if (!canMove)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (isMoveTo)
        {
            if      (isMoveToDefault)  HandleMoveToDefault();
            else if (isMoveToXY)       HandleMoveToXY();
            else if (isMoveToXYLimit)  HandleMoveToXYLimit();
            else if (isTransformToXY)  HandleTransformToXY();
        }
        else
        {
            MovementSetUp();
        }
    }

    protected virtual void MovementSetUp() { }

    // ── Handlers ──────────────────────────────────────────────────

    private void HandleMoveToDefault()
    {
        timeMoveTo -= FixedDeltaTime;

        rb.linearVelocity = new Vector2(
            moveSnap.x * velocityMoveTo * FixedDeltaTime,
            moveSnap.y * velocityMoveTo * FixedDeltaTime
        );

        if (timeMoveTo <= 0f)
        {
            ResetMoveTo();
            EventManager.Entity.OnEntityMoveToEnd
                .Get(entityKey)
                .Invoke(this, null);
        }
    }

    private void HandleMoveToXY()
    {
        timeMoveToXY -= FixedDeltaTime;

        Vector2 direction = (targetXY - (Vector2)transform.position).normalized;
        float   distance  = Vector2.Distance(transform.position, targetXY);

        rb.linearVelocity = direction * velocityMoveToXY * FixedDeltaTime;

        bool arrived = distance      <= 0.1f;
        bool timeUp  = timeMoveToXY  <= 0f;

        if (arrived || timeUp)
        {
            if (arrived) rb.linearVelocity = Vector2.zero;
            ResetMoveTo();
            EventManager.Entity.OnEntityMoveToEnd
                .Get(entityKey)
                .Invoke(this, null);
        }
    }

    private void HandleMoveToXYLimit()
    {
        timeMoveToLimit -= FixedDeltaTime;

        float distance = Vector2.Distance(transform.position, targetXYLimit);

        if (distance <= stopDistance || timeMoveToLimit <= 0f)
        {
            rb.linearVelocity = Vector2.zero;
            ResetMoveTo();
            EventManager.Entity.OnEntityMoveToEnd
                .Get(entityKey)
                .Invoke(this, null);
            return;
        }

        Vector2 direction = (targetXYLimit - (Vector2)transform.position).normalized;
        rb.linearVelocity = direction * velocityMoveToLimit * FixedDeltaTime;
    }

    private void HandleTransformToXY()
    {
        transformDelay    -= FixedDeltaTime;
        rb.linearVelocity  = Vector2.zero;

        if (transformDelay <= 0f)
        {
            transform.position = targetTransformXY;
            ResetMoveTo();
            EventManager.Entity.OnEntityMoveToEnd
                .Get(entityKey)
                .Invoke(this, null);
        }
    }

    // ── Snap & Utility ────────────────────────────────────────────
    private void SnapMove()
    {
        if (rb.linearVelocity != Vector2.zero)
        {
            isMove   = true;
            moveSnap = rb.linearVelocity.normalized;
        }
        else
        {
            isMove = false;
        }
    }

    private void ResetMoveTo()
    {
        isMoveTo        = false;
        isMoveToDefault = false;
        isMoveToXY      = false;
        isMoveToXYLimit = false;
        isTransformToXY = false;
    }

    public void StopMovement()
    {
        rb.linearVelocity = Vector2.zero;
    }

    /// <summary>
    /// Reset toàn bộ trạng thái di chuyển — dừng velocity + xóa moveTo flags
    /// </summary>
    public void ResetAll()
    {
        ResetMoveTo();
        if (rb != null)
            rb.linearVelocity = Vector2.zero;
    }

    protected virtual void Flipping()
    {
        flipDirect *= -1;
        transform.Rotate(0f, 180f, 0f);
    }

    // ── Public API ────────────────────────────────────────────────

    public virtual void MoveTo(float velocity, float time)
    {
        rb.linearVelocity = Vector2.zero;
        ResetMoveTo();
        isMoveTo        = true;
        isMoveToDefault = true;
        velocityMoveTo  = velocity;
        timeMoveTo      = time;
    }

    public virtual void MoveToXY(float velocity, float time)
    {
        rb.linearVelocity = Vector2.zero;
        ResetMoveTo();
        isMoveTo         = true;
        isMoveToXY       = true;
        targetXY         = targetPosition;
        velocityMoveToXY = velocity;
        timeMoveToXY     = time;
    }

    public virtual void MoveToXYLimitDistance(float velocity, float time, float stopDist = 0.5f)
    {
        rb.linearVelocity   = Vector2.zero;
        ResetMoveTo();
        isMoveTo            = true;
        isMoveToXYLimit     = true;
        targetXYLimit       = targetPosition;
        velocityMoveToLimit = velocity;
        timeMoveToLimit     = time;
        stopDistance         = stopDist;
    }

    public virtual void TranfromToXY(float delay)
    {
        rb.linearVelocity = Vector2.zero;
        ResetMoveTo();
        isMoveTo          = true;
        isTransformToXY   = true;
        targetTransformXY = targetPosition;
        transformDelay    = delay;
    }

    // ── Properties ────────────────────────────────────────────────
    public bool CanMove
    {
        get => canMove;
        set => canMove = value;
    }

    public bool CanFlip
    {
        get => canFlip;
        set => canFlip = value;
    }

    public bool IsMove => isMove;

    public Rigidbody2D Rb
    {
        get => rb;
        set => rb = value;
    }

    public int FlipDirect
    {
        get => flipDirect;
        set => flipDirect = value;
    }

    public Vector2 MoveSnap
    {
        get => moveSnap;
        set => moveSnap = value;
    }

    public float CurMoveSpeed
    {
        get => curMoveSpeed;
        set => curMoveSpeed = value;
    }

    public float BaseMoveSpeed
    {
        get => baseMoveSpeed;
        set => baseMoveSpeed = value;
    }

    public Vector2 TargetPosition
    {
        get => targetPosition;
        set => targetPosition = value;
    }
}