using UnityEngine;

/// <summary>
/// Xoáy vào trong — radius giảm dần về tâm, kế thừa damage logic từ EasyBulletWithoutExplosionResetHitObject
/// </summary>
public class BrimstoneSpiralIn : EasyBulletWithoutExplosionResetHitObject
{
    [Header("Spiral In")]
    [SerializeField] private float angularSpeed    = -180f;
    [SerializeField] private float shrinkSpeed     = 3f;
    [SerializeField] private float startRadius     = 8f;
    [SerializeField] private float timeMultiplier  = 1f;   // tốc độ tổng thể, giảm để chậm lại

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
        currentRadius = startRadius;
    }

    public void Init(Vector2 center, float startAngleDeg)
    {
        origin        = center;
        currentAngle  = startAngleDeg;
        currentRadius = startRadius;

        float rad          = currentAngle * Mathf.Deg2Rad;
        transform.position = origin + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * currentRadius;
    }

    protected override void CustomMovement()
    {
        currentAngle  += angularSpeed * FixedDeltaTime * timeMultiplier;
        currentRadius -= shrinkSpeed  * FixedDeltaTime * timeMultiplier;

        if (currentRadius <= 0f)
        {
            gameObject.SetActive(false);
            return;
        }

        float   rad      = currentAngle * Mathf.Deg2Rad;
        Vector2 nextPos  = origin + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * currentRadius;

        rb.linearVelocity = Vector2.zero;
        rb.MovePosition(nextPos);
    }

    // ── Gizmo ─────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.15f);
        Gizmos.DrawSphere(transform.position, startRadius);
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, startRadius);
    }
}