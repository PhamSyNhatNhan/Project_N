using UnityEngine;

/// <summary>
/// Xoáy ra ngoài — radius tăng dần, kế thừa damage logic từ EasyBulletWithoutExplosionResetHitObject
/// </summary>
public class BrimstoneSpiralOut : EasyBulletWithoutExplosionResetHitObject
{
    [Header("Spiral Out")]
    [SerializeField] private float angularSpeed   = 180f;  // độ/giây
    [SerializeField] private float expandSpeed    = 3f;    // radius tăng/giây
    [SerializeField] private float maxRadius      = 10f;   // giới hạn vòng
    [SerializeField] private float timeMultiplier = 1f;    // tốc độ tổng thể, giảm để chậm lại

    private float   currentAngle;
    private float   currentRadius;
    private Vector2 origin;

    protected override void Awake()
    {
        base.Awake();
        moveMode = BulletMoveMode.Custom;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        currentRadius = 0f;
    }

    public void Init(Vector2 spawnOrigin, float startAngleDeg)
    {
        origin        = spawnOrigin;
        currentAngle  = startAngleDeg;
        currentRadius = 0f;
    }

    protected override void CustomMovement()
    {
        currentAngle  += angularSpeed * FixedDeltaTime * timeMultiplier;
        currentRadius += expandSpeed  * FixedDeltaTime * timeMultiplier;

        if (currentRadius >= maxRadius)
        {
            gameObject.SetActive(false);
            return;
        }

        float   rad     = currentAngle * Mathf.Deg2Rad;
        Vector2 nextPos = origin + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * currentRadius;

        rb.linearVelocity = Vector2.zero;
        rb.MovePosition(nextPos);
    }

    // ── Gizmo ─────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.15f);
        Gizmos.DrawSphere(transform.position, maxRadius);
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, maxRadius);
    }
}