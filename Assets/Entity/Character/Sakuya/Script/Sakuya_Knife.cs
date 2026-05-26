using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Projectile của Sakuya — kế thừa BulletObject.
/// Khi trúng địch: gây damage, fire OnProjectileHit kèm PwsAmount, disable.
/// SetUp() có overload nhận pwsOnHit để mỗi loại dao hồi PWS khác nhau.
/// </summary>
public class SakuyaKnife : BulletObject
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Config")]
    [SerializeField] private GameObject Explosion;

    // ── State ─────────────────────────────────────────────────────
    private GameObject explosion;
    protected int        pwsOnHit = 4;

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

        // Fire event để SakuyaSkill cộng PWS
        EventManager.Projectile.OnProjectileHit
            .Get(nameChannel)
            .Invoke(this, new SakuyaHitData { PwsAmount = pwsOnHit });

        // Explosion tại vị trí địch, lệch theo hướng bay
        if (explosion != null)
        {
            explosion.transform.position = (Vector2)other.transform.position + MoveDir * 0.3f;
            explosion.transform.rotation = transform.rotation;
            explosion.SetActive(true);
        }

        gameObject.SetActive(false);
    }

    // ── SetUp Overloads ───────────────────────────────────────────
    public void SetUp(string nameChannel, DamageType type, List<float> damage,
                      float critRate, float critDamage, float attackSpeed,
                      float iFrameDuration, int pwsOnHit)
    {
        base.SetUp(nameChannel, type, damage, critRate, critDamage, attackSpeed, iFrameDuration);
        this.pwsOnHit = pwsOnHit;
    }

    public void SetUp(DamageType type, List<float> damage,
                      float critRate, float critDamage,
                      float iFrameDuration, int pwsOnHit)
    {
        base.SetUp(type, damage, critRate, critDamage, iFrameDuration);
        this.pwsOnHit = pwsOnHit;
    }

    public void SetUp(DamageType type, List<float> damage, Stat stat,
                      float critRate, float critDamage,
                      float iFrameDuration, int pwsOnHit)
    {
        base.SetUp(type, damage, stat, critRate, critDamage, iFrameDuration);
        this.pwsOnHit = pwsOnHit;
    }
}