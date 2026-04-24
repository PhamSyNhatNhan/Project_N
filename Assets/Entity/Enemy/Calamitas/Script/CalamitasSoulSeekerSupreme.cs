using System.Collections.Generic;
using UnityEngine;

public class CalmitasSoulSeekerSupreme : EasyBulletWithoutExplosionResetHitObject
{
    // ── Orbit ─────────────────────────────────────────────────────
    [Header("Orbit")]
    [SerializeField] private Transform orbitCenter;
    [SerializeField] private float     orbitRadius = 3f;
    [SerializeField] private float     orbitSpeed  = 90f;
    private float currentAngle;

    // ── Attack ────────────────────────────────────────────────────
    [Header("Attack")]
    [SerializeField] private BulletObject agletBulletPrefab;
    [SerializeField] private Transform    player;
    [SerializeField] private float        attackInterval = 1.5f;
    private float           attackTimer;
    private EasyPoolingList bulletPool = new EasyPoolingList();

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        moveMode = BulletMoveMode.Custom;
        bulletPool.SetPrefab(agletBulletPrefab.gameObject);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        attackTimer = attackInterval;

        if (orbitCenter != null)
        {
            Vector2 offset = (Vector2)transform.position - (Vector2)orbitCenter.position;
            currentAngle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
        }
    }

    // ── Overrides ─────────────────────────────────────────────────
    protected override void CustomInitDir()
    {
        moveDir = Vector2.zero;
    }

    protected override void CustomMovement()
    {
        if (orbitCenter == null) return;

        currentAngle += orbitSpeed * FixedDeltaTime;

        float   rad        = currentAngle * Mathf.Deg2Rad;
        Vector2 desiredPos = (Vector2)orbitCenter.position
                             + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * orbitRadius;

        Vector2 delta = desiredPos - rb.position;
        rb.linearVelocity = delta / FixedDeltaTime;

        moveDir = new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad)) * Mathf.Sign(orbitSpeed);
        FacePlayer();
    }

    protected override void Update()
    {
        base.Update();
        HandleAttackTimer();
    }

    // ── Face Player ───────────────────────────────────────────────
    private void FacePlayer()
    {
        if (player == null) return;
        Vector2 toPlayer = (Vector2)player.position - (Vector2)transform.position;
        if (toPlayer == Vector2.zero) return;

        Vector3 scale = transform.localScale;
        scale.x = toPlayer.x < 0f ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
        transform.localScale = scale;

        transform.rotation = Quaternion.identity;
    }

    // ── Attack ────────────────────────────────────────────────────
    private void HandleAttackTimer()
    {
        if (agletBulletPrefab == null || player == null) return;

        attackTimer -= DeltaTime;
        if (attackTimer > 0f) return;

        attackTimer = attackInterval;
        ShootAtPlayer();
    }

    private void ShootAtPlayer()
    {
        GameObject obj = bulletPool.GetGameObject();
        if (obj == null) return;

        obj.transform.position = transform.position;
        obj.transform.rotation = Quaternion.identity;

        if (obj.TryGetComponent(out BulletObject bullet))
        {
            bullet.SetUp(nameChannel, type, new List<float>(damage), critRate, critDamage, attackSpeed);
            bullet.EnableDamage = EnableDamage;
        }

        obj.SetActive(true);

        Vector2 toPlayer = (Vector2)player.position - (Vector2)transform.position;
        float   aimAngle = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg;
        bullet.EasyModeChange(BulletMoveMode.Angle, aimAngle);
    }

    // ── Helpers ───────────────────────────────────────────────────
    public void SetOrbitCenter(Transform center) => orbitCenter = center;
    public void SetPlayer(Transform t)            => player      = t;

    // ── Gizmos ────────────────────────────────────────────────────
    protected override void OnDrawGizmos()
    {
        base.OnDrawGizmos();

        if (orbitCenter == null) return;

        Gizmos.color = new Color(1f, 0.4f, 0f, 0.2f);
        DrawCircle(orbitCenter.position, orbitRadius, 48);
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.8f);
        DrawCircle(orbitCenter.position, orbitRadius, 48);

        Gizmos.color = new Color(1f, 0.4f, 0f, 0.5f);
        Gizmos.DrawLine(transform.position, orbitCenter.position);

        if (player != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, player.position);
        }
    }

    private static void DrawCircle(Vector3 center, float r, int segs)
    {
        for (int i = 0; i < segs; i++)
        {
            float a0 = i       / (float)segs * Mathf.PI * 2f;
            float a1 = (i + 1) / (float)segs * Mathf.PI * 2f;
            Gizmos.DrawLine(center + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0)) * r,
                            center + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1)) * r);
        }
    }
}