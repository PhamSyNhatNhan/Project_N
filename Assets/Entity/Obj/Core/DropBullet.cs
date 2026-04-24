using UnityEngine;

public class DropBullet : BulletObject
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Config")]
    private bool isActivated = false;
    [SerializeField] private GameObject Explosion;

    // ── State ─────────────────────────────────────────────────────
    private Hitbox hitbox;
    private GameObject explosion;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        hitbox = GetComponent<Hitbox>();
        InitExplosion();
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        SendDamage();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        isActivated = false;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        isActivated = false;
    }

    protected override void CustomMovement()
    {
        if (!isActivated)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        rb.linearVelocity = Vector2.down * speed * FixedDeltaTime;
    }

    // ── Public API ────────────────────────────────────────────────
    public void Activate()
    {
        isActivated = true;
    }

    // ── Explosion ─────────────────────────────────────────────────
    private void InitExplosion()
    {
        explosion = Instantiate(Explosion);
        explosion.transform.SetParent(null);
        explosion.SetActive(false);
    }

    // ── Damage ────────────────────────────────────────────────────
    public override void SendDamage()
    {
        var enemies = hitbox.detectObject(EnableDamage);
        if (enemies == null || enemies.Count == 0) return;

        explosion.transform.position = transform.position;
        explosion.transform.rotation = transform.rotation;
        explosion.GetComponent<ProjectileObject>().SetUp(type, damage, stat, critRate, critDamage);
        explosion.SetActive(true);
        gameObject.SetActive(false);
    }
}