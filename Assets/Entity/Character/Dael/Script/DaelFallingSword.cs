using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Projectile của Dael — kế thừa BulletObject.
/// SetUp() có overload nhận pwsOnHit để mỗi loại dao hồi PWS khác nhau.
/// </summary>
public class DaelFallingSword : BulletObject
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Config")]
    [SerializeField] private GameObject Explosion;
    [SerializeField] private ShakeData  shakeData = new ShakeData(0.3f, 1f, 0.2f, 0.1f);

    // ── State ─────────────────────────────────────────────────────
    private GameObject explosion;
    public int FlipDirect { get; set; } = 1;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        InitExplosion();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        EventManager.Player.PlayerFlipCall.Get(nameChannel).Invoke(this);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        EventManager.Player.OnAttackEnd.Get(nameChannel).Invoke(this, null);
    }

    // ── Init ──────────────────────────────────────────────────────
    private void InitExplosion()
    {
        if (Explosion == null) return;
        explosion = Instantiate(Explosion);
        explosion.transform.SetParent(null);
        explosion.SetActive(false);
    }

    // ── Movement ──────────────────────────────────────────────────
    protected override void CustomMovement()
    {
        rb.linearVelocity = new Vector2(speed * FlipDirect * FixedDeltaTime, 0.0f);
    }

    // ── Trigger ───────────────────────────────────────────────────
    protected virtual void OnTriggerEnter2D(Collider2D other)
    {
        if ((EnableDamage.value & (1 << other.gameObject.layer)) == 0) return;

        var enemyStat = other.GetComponent<Stat>();
        if (enemyStat == null) return;

        SendDamage(enemyStat, other);
    }

    // ── SendDamage ────────────────────────────────────────────────
    public override void SendDamage() { }

    private void SendDamage(Stat enemyStat, Collider2D other)
    {
        enemyStat.TakeDamage(type, damage[0], critRate, critDamage, iFrameDuration);

        EventManager.Gm.OnCameraShake.Get().Invoke(this, shakeData);

        if (explosion != null)
        {
            explosion.transform.position = (Vector2)other.transform.position + MoveDir * 0.3f;
            explosion.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            explosion.SetActive(true);
        }

        gameObject.SetActive(false);
    }
}