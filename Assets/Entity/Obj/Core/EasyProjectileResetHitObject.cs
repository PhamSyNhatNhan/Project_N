using System.Collections.Generic;
using UnityEngine;

public class EasyProjectileResetHitObject : ProjectileObject
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Reset Hit")]
    [SerializeField] private float reHitDelay = 1f;

    // ── Runtime ───────────────────────────────────────────────────
    private Hitbox hitbox;
    private float  scaledTime;

    private readonly Dictionary<Stat, float> hitCooldowns = new Dictionary<Stat, float>();

    protected TimeScale timeScale;
    protected float DeltaTime      => timeScale.DeltaTime;
    protected float FixedDeltaTime => timeScale.FixDeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        hitbox    = GetComponent<Hitbox>();
        timeScale = GetComponent<TimeScale>();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        scaledTime = 0f;
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
    }

    // ── Damage — gọi từ Animation Event ──────────────────────────
    public override void SendDamage()
    {
        List<GameObject> enemies = hitbox.detectObject(EnableDamage);
        if (enemies == null || enemies.Count == 0) return;

        foreach (var enemy in enemies)
        {
            var stat = enemy.GetComponent<Stat>();
            if (stat == null) continue;

            if (hitCooldowns.TryGetValue(stat, out float readyAt) && scaledTime < readyAt)
                continue;

            hitCooldowns[stat] = scaledTime + reHitDelay;
            stat.TakeDamage(type, damage[0], critRate, critDamage);
        }
    }

    public TimeScale TimeScale => timeScale;
}