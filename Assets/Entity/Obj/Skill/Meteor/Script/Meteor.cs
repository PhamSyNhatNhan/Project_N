using UnityEngine;

public class Meteor : BulletObject
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Meteor")]
    private float lastHitTime = 0;
    [SerializeField] private ShakeData shakeData = new ShakeData(0.3f, 1f, 0.2f, 0.1f);
    [SerializeField] private GameObject explosionPrefab;
    private bool canExplode = false;

    // ── State ─────────────────────────────────────────────────────
    private Hitbox hitbox;
    private GhostTrail ghostTrail;
    private GameObject explosion;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        hitbox     = GetComponent<Hitbox>();
        ghostTrail = GetComponent<GhostTrail>();

        explosion = Instantiate(explosionPrefab);
        explosion.transform.SetParent(null);
        explosion.SetActive(false);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ghostTrail?.StartTrail();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        ghostTrail?.StopTrail();

        if (canExplode)
        {
            explosion.transform.position = transform.position;
            explosion.GetComponent<ProjectileObject>().SetUp(type, damage, stat, critRate, critDamage);
            explosion.SetActive(true);

            EventManager.Gm.OnCameraShake.Get().Invoke(this, shakeData);
        }
    }

    protected override void Update()
    {
        base.Update();
        lastHitTime += DeltaTime;
        if (lastHitTime > 0.05f)
        {
            SendDamage();
            lastHitTime = 0.0f;
        }
        if(traveledDist >= (maxDistance - 0.5f)) canExplode = true;
    }

    // ── Damage ────────────────────────────────────────────────────
    public override void SendDamage()
    {
        var enemies = hitbox.detectObject(EnableDamage);
        if (enemies == null || enemies.Count == 0) return;

        canExplode = true;
        gameObject.SetActive(false);
    }
}