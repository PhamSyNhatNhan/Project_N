using UnityEngine;

public class CalamitasHellblast2 : EasyBulletWithoutExplosionResetHitObject
{
    [Header("Sine Wave")]
    [SerializeField] private float amplitude  = 1.5f;
    [SerializeField] private float frequency  = 0.8f;

    private Vector2 forwardDir = Vector2.right; 
    private Vector2 perpDir;
    private float   t;

    // ── Setup (gọi trước khi Enable) ──────────────────────────────
    public void SetForwardDir(Vector2 dir)
    {
        forwardDir = dir.normalized;
    }

    // ── BulletObject overrides ────────────────────────────────────
    protected override void OnEnable()
    {
        moveMode = BulletMoveMode.Custom;
        base.OnEnable(); 
    }

    protected override void CustomInitDir()
    {
        t       = 0f;
        perpDir = new Vector2(-forwardDir.y, forwardDir.x);
        moveDir = forwardDir;
    }

    protected override void CustomMovement()
    {
        t += FixedDeltaTime;

        float perpSpeed = amplitude * 2f * Mathf.PI * frequency
                          * Mathf.Cos(2f * Mathf.PI * frequency * t);

        Vector2 velocity = forwardDir * speed + perpDir * perpSpeed;

        rb.linearVelocity = velocity * FixedDeltaTime;

        if (velocity != Vector2.zero)
            moveDir = velocity.normalized;

        RotateTo();
    }
}