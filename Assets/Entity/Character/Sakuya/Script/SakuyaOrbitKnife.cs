using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Dao orbit xoay quanh player, kế thừa SakuyaKnife.
/// Khi đang xoay: dùng Time.fixedDeltaTime trực tiếp (không bị timestop), không disappear khi trúng.
/// ReleaseOrbit() → EasyModeChange(Homing) → disappear + cộng PWS khi trúng như knife thường.
/// </summary>
public class SakuyaOrbitKnife : SakuyaKnife
{
    // ── Orbit Config ──────────────────────────────────────────────
    [Header("Orbit")]
    [SerializeField] private float orbitSpeed = 180f; // độ/giây

    // ── Runtime ───────────────────────────────────────────────────
    private Transform orbitTarget;
    private float     orbitRadius = 2f;
    private float     orbitAngle  = 0f;
    private bool      isOrbiting  = true;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void OnEnable()
    {
        base.OnEnable();
        isOrbiting = true;
    }

    protected override void FixedUpdate()
    {
        if (isOrbiting)
            OrbitMovement();
        else
            base.FixedUpdate();
    }

    // ── Orbit — Time.fixedDeltaTime trực tiếp, không bị timestop ─
    private void OrbitMovement()
    {
        if (orbitTarget == null) { gameObject.SetActive(false); return; }

        orbitAngle += orbitSpeed * Time.fixedDeltaTime;
        if (orbitAngle >= 360f) orbitAngle -= 360f;

        float   rad    = orbitAngle * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * orbitRadius;
        transform.position = orbitTarget.position + offset;
        transform.rotation = Quaternion.Euler(0f, 0f, orbitAngle);
        rb.linearVelocity  = Vector2.zero;
    }

    // ── Trigger — orbit không disappear, homing thì disappear ────
    protected override void OnTriggerEnter2D(Collider2D other)
    {
        if (isOrbiting)
        {
            // Gây damage nhưng không disappear, không cộng PWS
            if ((EnableDamage.value & (1 << other.gameObject.layer)) == 0) return;
            var enemyStat = other.GetComponent<Stat>();
            if (enemyStat == null) return;
            enemyStat.TakeDamage(type, damage[0], critRate, critDamage, iFrameDuration);
        }
        else
        {
            // Homing → disappear + cộng PWS như SakuyaKnife
            base.OnTriggerEnter2D(other);
        }
    }

    // ── API ───────────────────────────────────────────────────────
    public void SetupOrbit(Transform target, float radius, float startAngle)
    {
        orbitTarget = target;
        orbitRadius = radius;
        orbitAngle  = startAngle;
        isOrbiting  = true;
    }

    public void ReleaseOrbit()
    {
        isOrbiting = false;
        EasyModeChange(BulletMoveMode.Homing);
    }

    public bool IsOrbiting => isOrbiting;
}