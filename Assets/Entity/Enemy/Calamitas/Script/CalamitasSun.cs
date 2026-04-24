using System.Collections.Generic;
using UnityEngine;

public class CalamitasSun : BulletObject
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Config")]
    [SerializeField] private GameObject explosionPrefab;

    [Header("Hit Cooldown")]
    [SerializeField] private float hitCooldown = 1f;

    private bool fistSpawn = true;

    [Header("Ring Burst")]
    [SerializeField] private GameObject ringBulletPrefab;
    [SerializeField] private int        ringCount       = 6;
    [SerializeField] private float      ringInterval    = 0.5f;
    [SerializeField] private float      ringSpawnOffset = 0.5f; 
    

    // ── State ─────────────────────────────────────────────────────
    private Hitbox           hitbox;
    private GameObject       explosion;

    // key = Stat, value = thời gian có thể hit lại
    private readonly Dictionary<Stat, float> hitCooldowns = new Dictionary<Stat, float>();
    private readonly EasyPoolingList          ringPool     = new EasyPoolingList();

    private float ringTimer;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        hitbox = GetComponent<Hitbox>();

        if (explosionPrefab != null)
        {
            explosion = Instantiate(explosionPrefab);
            explosion.transform.SetParent(null);
            explosion.SetActive(false);
        }

        if (ringBulletPrefab != null) ringPool.SetPrefab(ringBulletPrefab);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        hitCooldowns.Clear();
        ringTimer = ringInterval;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        SpawnExplosion();
    }

    protected override void Update()
    {
        base.Update();

        // Giảm cooldown của từng target
        var keys = new List<Stat>(hitCooldowns.Keys);
        foreach (var s in keys)
        {
            hitCooldowns[s] -= DeltaTime;
            if (hitCooldowns[s] <= 0f)
                hitCooldowns.Remove(s);
        }

        // Ring định kỳ
        ringTimer -= DeltaTime;
        if (ringTimer <= 0f)
        {
            ringTimer = ringInterval;
            SpawnRingBurst();
        }
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        SendDamage();
    }

    // ── Damage ────────────────────────────────────────────────────
    public override void SendDamage()
    {
        var enemies = hitbox.detectObject(EnableDamage);
        if (enemies == null || enemies.Count == 0) return;

        foreach (var enemy in enemies)
        {
            var s = enemy.GetComponent<Stat>();
            if (s == null) continue;
            if (hitCooldowns.ContainsKey(s)) continue;

            hitCooldowns[s] = hitCooldown;
            s.TakeDamage(type, damage.Count > 0 ? damage[0] : 100f, critRate, critDamage);
        }
    }

    // ── Explosion ─────────────────────────────────────────────────
    private void SpawnExplosion()
    {
        if (fistSpawn)
        {
            fistSpawn = false;
            return;
        }
        
        if (explosion == null) return;
        explosion.transform.position = transform.position;
        explosion.transform.rotation = transform.rotation;
        explosion.GetComponent<ProjectileObject>()
                 ?.SetUp(type, damage, stat, critRate, critDamage);
        explosion.SetActive(true);
    }

    // ── Ring Burst ────────────────────────────────────────────────
    private void SpawnRingBurst()
    {
        if (ringBulletPrefab == null) return;

        float step    = 360f / ringCount;
        float halfDmg = damage != null && damage.Count > 0 ? damage[0] * 0.5f : 100f;
        var   dmgList = new List<float> { halfDmg };

        for (int i = 0; i < ringCount; i++)
        {
            var obj = ringPool.GetGameObject();
            if (obj == null) continue;

            float   angle     = step * i;
            float   rad       = angle * Mathf.Deg2Rad;
            Vector2 offset    = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * ringSpawnOffset;
            obj.transform.position = (Vector2)transform.position + offset;
            obj.SetActive(true);

            var b = obj.GetComponent<BulletObject>();
            if (b == null) continue;

            b.SetUp(type, dmgList, stat, critRate, critDamage);
            b.EasyModeChange(BulletMoveMode.Angle, angle);
        }
    }

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, ringSpawnOffset);
    }
}