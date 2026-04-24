using System.Collections.Generic;
using UnityEngine;

public class EasyLaserObject : ProjectileObject
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Laser")]
    [SerializeField] private float damageInterval = 0.5f;  
    [SerializeField] private float reHitDelay     = 1f;    

    // ── Runtime ───────────────────────────────────────────────────
    private Hitbox hitbox;
    private float  damageTimer;
    private float  scaledTime;

    private readonly Dictionary<Stat, float> hitCooldowns = new Dictionary<Stat, float>();
    
    protected TimeScale timeScale;

    protected float DeltaTime      => timeScale.DeltaTime;
    protected float FixedDeltaTime => timeScale.FixDeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        hitbox = GetComponent<Hitbox>();
        timeScale        = GetComponent<TimeScale>();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        damageTimer = 0f;
        scaledTime  = 0f;
        hitCooldowns.Clear();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        timeScale?.RemoveAllModifiers();
    }

    private void Update()
    {
        scaledTime += DeltaTime;
        SendDamage();
    }

    // ── Damage ────────────────────────────────────────────────────
    public override void SendDamage()
    {
        damageTimer -= DeltaTime;
        if (damageTimer > 0f) return;

        List<GameObject> enemies = hitbox.detectObject(EnableDamage);
        if (enemies == null || enemies.Count == 0)
        {
            damageTimer = damageInterval;
            return;
        }

        foreach (var enemy in enemies)
        {
            var stat = enemy.GetComponent<Stat>();
            if (stat == null) continue;

            if (hitCooldowns.TryGetValue(stat, out float readyAt) && scaledTime < readyAt)
                continue;

            hitCooldowns[stat] = scaledTime + reHitDelay;
            stat.TakeDamage(type, damage[0], critRate, critDamage);
        }

        damageTimer = damageInterval;
    }
    
    public TimeScale TimeScale    => timeScale;
}