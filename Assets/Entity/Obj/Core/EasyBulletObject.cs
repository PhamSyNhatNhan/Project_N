using UnityEngine;

public class EasyBulletObject : BulletObject
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Config")]
    [SerializeField] private GameObject Explosion;

    // ── State ─────────────────────────────────────────────────────
    private Hitbox hitbox;
    protected GameObject explosion;

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
    }

    protected override void OnDisable()
    {
        base.OnDisable();
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
