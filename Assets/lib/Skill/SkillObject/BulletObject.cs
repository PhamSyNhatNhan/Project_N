using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base class cho tất cả projectile dạng bullet — có Rigidbody2D, Animator, TimeScale.
/// Hỗ trợ 5 chế độ di chuyển: Forward, Angle, Target, Homing, Custom.
/// Disable theo Timer hoặc Distance.
/// Override SendDamage() để xử lý damage trong subclass.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(TimeScale))]
public class BulletObject : SkillObject
{
    [Header("Info")]
    protected string      nameChannel = "";
    [SerializeField] protected DamageType  type        = DamageType.True;
    [SerializeField] protected List<float> damage      = new List<float> { 1000.0f };
    [SerializeField] protected float       critRate    = 0.0f;
    [SerializeField] protected float       critDamage  = 0.0f;
    [SerializeField] protected float       attackSpeed = 100.0f;

    // ── Disable ───────────────────────────────────────────────────
    [Header("Disable")]
    [SerializeField] protected BulletDisableMode disableMode = BulletDisableMode.Timer;
    [SerializeField] protected float             liveTime    = 3.0f;
    [SerializeField] protected float             maxDistance = 10.0f;

    protected float   liveTimeSub  = 0.0f;
    protected float   traveledDist = 0.0f;
    private   Vector2 lastPosition;

    // ── Movement ──────────────────────────────────────────────────
    [Header("Movement")]
    [SerializeField] protected BulletMoveMode moveMode = BulletMoveMode.Forward;
    [SerializeField] protected float          speed    = 10.0f;
    [SerializeField] protected float          angle    = 0.0f;
    protected                  Vector2        moveDir  = Vector2.right;

    // ── Homing ────────────────────────────────────────────────────
    /// <summary>
    /// Homing — bullet tự động điều chỉnh hướng về target mỗi frame.
    /// homingStrength: tốc độ xoay về target (cao = xoay nhanh, thấp = xoay chậm).
    /// homingRadius: bán kính tìm target khi chưa có hoặc target chết.
    /// findNewIfTargetDead: tự tìm target mới nếu target hiện tại không còn active.
    /// Target mode: chỉ snap hướng 1 lần lúc spawn, không đổi hướng theo target.
    /// </summary>
    [Header("Homing")]
    [SerializeField] protected float     homingStrength      = 5.0f;
    [SerializeField] protected float     homingRadius        = 10.0f;
    [SerializeField] protected bool      findNewIfTargetDead = true;
    protected                  Transform homingTarget;
    private                    Vector2   targetSnapPosition;

    // ── Damage ────────────────────────────────────────────────────
    [Header("Damage")]
    [SerializeField] protected LayerMask enableDamage;

    // ── Components ────────────────────────────────────────────────
    protected Animator    amt;
    protected Rigidbody2D rb;
    protected TimeScale   timeScale;

    protected float DeltaTime      => timeScale.DeltaTime;
    protected float FixedDeltaTime => timeScale.FixDeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        objectSkillType  = ObjectSkillType.bullet;
        amt              = GetComponent<Animator>();
        rb               = GetComponent<Rigidbody2D>();
        timeScale        = GetComponent<TimeScale>();
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    protected virtual void OnEnable()
    {
        liveTimeSub  = liveTime;
        traveledDist = 0f;
        lastPosition = transform.position;
        InitMoveDir();
    }

    protected virtual void OnDisable()
    {
        timeScale?.RemoveAllModifiers();
        rb.linearVelocity = Vector2.zero;
        homingTarget      = null;
    }

    protected virtual void Update()      { CheckDisable(); }
    protected virtual void FixedUpdate() { Movement(); TrackDistance(); }

    // ── Init Direction ────────────────────────────────────────────
    private void InitMoveDir()
    {
        switch (moveMode)
        {
            case BulletMoveMode.Forward:
                moveDir = transform.right * Mathf.Sign(transform.localScale.x);
                break;
            case BulletMoveMode.Angle:
                moveDir = AngleToDir(angle);
                break;
            case BulletMoveMode.Target:
                if (homingTarget == null) FindNearestTarget();
                if (homingTarget != null)
                {
                    targetSnapPosition = homingTarget.position;
                    moveDir = (targetSnapPosition - (Vector2)transform.position).normalized;
                }
                else
                    moveDir = transform.right * Mathf.Sign(transform.localScale.x);
                break;
            case BulletMoveMode.Homing:
                if (homingTarget == null) FindNearestTarget();
                moveDir = homingTarget != null
                    ? ((Vector2)homingTarget.position - (Vector2)transform.position).normalized
                    : transform.right * Mathf.Sign(transform.localScale.x);
                break;
            case BulletMoveMode.Custom:
                CustomInitDir();
                break;
        }
        RotateToMoveDir();
    }

    // ── EasyModeChange ────────────────────────────────────────────
    public void EasyModeChange(BulletMoveMode newMode)
    { moveMode = newMode; InitMoveDir(); }

    public void EasyModeChange(BulletMoveMode newMode, Transform target)
    { homingTarget = target; moveMode = newMode; InitMoveDir(); }

    public void EasyModeChange(BulletMoveMode newMode, float angleDeg)
    { angle = angleDeg; moveMode = newMode; InitMoveDir(); }

    // ── Movement ──────────────────────────────────────────────────
    protected virtual void Movement()
    {
        switch (moveMode)
        {
            case BulletMoveMode.Forward:
            case BulletMoveMode.Angle:
            case BulletMoveMode.Target:
                rb.linearVelocity = moveDir * speed * FixedDeltaTime;
                RotateToMoveDir();
                break;
            case BulletMoveMode.Homing:
                HandleHoming();
                break;
            case BulletMoveMode.Custom:
                CustomMovement();
                break;
        }
    }

    private void HandleHoming()
    {
        if (homingTarget == null || !homingTarget.gameObject.activeInHierarchy)
            if (findNewIfTargetDead) FindNearestTarget();

        if (homingTarget != null)
        {
            Vector2 desired = ((Vector2)homingTarget.position - (Vector2)transform.position).normalized;
            float   t       = 1f - Mathf.Exp(-homingStrength * FixedDeltaTime);
            moveDir = Vector2.Lerp(moveDir, desired, t).normalized;
        }
        rb.linearVelocity = moveDir * speed * FixedDeltaTime;
        RotateToMoveDir();
    }

    // ── Find Nearest Target ───────────────────────────────────────
    private void FindNearestTarget()
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, homingRadius, enableDamage);
        if (hits.Length == 0) { homingTarget = null; return; }

        float     minDist = float.MaxValue;
        Transform nearest = null;
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;
            float d = Vector2.Distance(transform.position, hit.transform.position);
            if (d < minDist) { minDist = d; nearest = hit.transform; }
        }
        homingTarget = nearest;
    }

    // ── Helpers ───────────────────────────────────────────────────
    private static Vector2 AngleToDir(float deg) =>
        new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)).normalized;

    private void RotateToMoveDir()
    {
        if (moveDir == Vector2.zero) return;
        float a = Mathf.Atan2(moveDir.y, moveDir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, a);
    }

    // ── Disable ───────────────────────────────────────────────────
    private void CheckDisable()
    {
        switch (disableMode)
        {
            case BulletDisableMode.Timer:
                liveTimeSub -= DeltaTime;
                if (liveTimeSub <= 0f) gameObject.SetActive(false);
                break;
            case BulletDisableMode.Distance:
                if (traveledDist >= maxDistance) gameObject.SetActive(false);
                break;
        }
    }

    private void TrackDistance()
    {
        traveledDist += Vector2.Distance(transform.position, lastPosition);
        lastPosition  = transform.position;
    }

    // ── Gizmos ────────────────────────────────────────────────────
    protected virtual void OnDrawGizmos()
    {
        if (moveMode == BulletMoveMode.Homing || moveMode == BulletMoveMode.Target)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.15f);
            Gizmos.DrawSphere(transform.position, homingRadius);
            Gizmos.color = new Color(0f, 1f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, homingRadius);
            if (homingTarget != null)
            { Gizmos.color = Color.red; Gizmos.DrawLine(transform.position, homingTarget.position); }
            if (moveMode == BulletMoveMode.Target && targetSnapPosition != Vector2.zero)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(targetSnapPosition, 0.2f);
                Gizmos.DrawLine(transform.position, targetSnapPosition);
            }
        }
        if (moveDir != Vector2.zero)
        { Gizmos.color = Color.green; Gizmos.DrawRay(transform.position, moveDir * 1.5f); }
    }

    // ── Override Points ───────────────────────────────────────────
    protected virtual void CustomMovement() {}
    protected virtual void CustomInitDir()  {}
    public    virtual void SendDamage()     {}

    // ── Setup ─────────────────────────────────────────────────────
    public void SetTarget(Transform target) => homingTarget = target;

    public void SetAngle(float deg)
    { angle = deg; moveDir = AngleToDir(deg); }

    public void SetUp(string nameChannel, DamageType type, List<float> damage,
                      float critRate, float critDamage, float attackSpeed, float iFrameDuration = 0f)
    {
        this.nameChannel    = nameChannel; this.type = type; this.damage = damage;
        this.critRate       = critRate; this.critDamage = critDamage;
        this.attackSpeed    = attackSpeed; this.iFrameDuration = iFrameDuration;
    }

    public void SetUp(DamageType type, List<float> damage,
                      float critRate, float critDamage, float attackSpeed, float iFrameDuration = 0f)
    {
        this.type           = type; this.damage = damage;
        this.critRate       = critRate; this.critDamage = critDamage;
        this.attackSpeed    = attackSpeed; this.iFrameDuration = iFrameDuration;
    }

    public void SetUp(DamageType type, List<float> damage,
                      float critRate, float critDamage, float iFrameDuration = 0f)
    {
        this.type           = type; this.damage = damage;
        this.critRate       = critRate; this.critDamage = critDamage;
        this.iFrameDuration = iFrameDuration;
    }

    public void SetUp(DamageType type, List<float> damage, Stat stat,
                      float critRate, float critDamage, float iFrameDuration = 0f)
    {
        this.type           = type; this.damage = damage;
        this.critRate       = critRate; this.critDamage = critDamage;
        this.stat           = stat; this.iFrameDuration = iFrameDuration;
    }

    public void SetUp(string nameChannel) => this.nameChannel = nameChannel;

    // ── Properties ────────────────────────────────────────────────
    public void SetSpeed(float newSpeed) => speed = newSpeed;
    public LayerMask EnableDamage { get => enableDamage; set => enableDamage = value; }
    public TimeScale TimeScale    => timeScale;
    public Vector2   MoveDir      => moveDir;
    public float     Speed        => speed;
    protected void   RotateTo()   => RotateToMoveDir();
}