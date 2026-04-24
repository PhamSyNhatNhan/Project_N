using UnityEngine;

public class CalamitasSuicideBomberDemonHostile : BulletObject
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Config")]
    [SerializeField] private GameObject Explosion;

    // ── State ─────────────────────────────────────────────────────
    private Hitbox         hitbox;
    private GameObject     explosion;
    private SpriteRenderer sr;
    private bool           isBulletMode = false;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        hitbox = GetComponent<Hitbox>();
        sr     = GetComponent<SpriteRenderer>()
              ?? GetComponentInChildren<SpriteRenderer>();
        InitExplosion();
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        UpdateFlip();
        SendDamage();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        isBulletMode = false;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        EasyModeChange(BulletMoveMode.Custom);
        isBulletMode = false;
    }

    private void ChangeMove()
    {
        isBulletMode = true;
        EasyModeChange(BulletMoveMode.Homing);
    }

    // ── Flip ──────────────────────────────────────────────────────
    private void UpdateFlip()
    {
        if (sr == null) return;
        sr.flipY = MoveDir.x < 0f;
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
        if (!isBulletMode) return;

        var enemies = hitbox.detectObject(EnableDamage);
        if (enemies == null || enemies.Count == 0) return;

        explosion.transform.position = transform.position;
        explosion.transform.rotation = transform.rotation;
        explosion.GetComponent<ProjectileObject>().SetUp(type, damage, stat, critRate, critDamage);
        explosion.SetActive(true);
        gameObject.SetActive(false);
    }
}