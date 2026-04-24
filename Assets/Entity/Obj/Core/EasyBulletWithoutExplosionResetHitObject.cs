using System.Collections.Generic;
using UnityEngine;

public class EasyBulletWithoutExplosionResetHitObject : BulletObject
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Reset Hit")]
    [SerializeField] private float reHitDelay = 1f; 

    // ── State ─────────────────────────────────────────────────────
    private Hitbox hitbox;
    private float  scaledTime;
    private readonly Dictionary<Stat, float> hitCooldowns = new Dictionary<Stat, float>();

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        hitbox = GetComponent<Hitbox>();
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        scaledTime += FixedDeltaTime;
        SendDamage();
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
    }

    // ── Damage ────────────────────────────────────────────────────
    public override void SendDamage()
    {
        List<GameObject> enemies = hitbox.detectObject(EnableDamage);
        if (enemies == null || enemies.Count == 0) return;

        for (int i = 0; i < enemies.Count; i++)
        {
            var stat = enemies[i].GetComponent<Stat>();
            if (stat == null) continue;

            if (hitCooldowns.TryGetValue(stat, out float readyAt) && scaledTime < readyAt)
                continue;

            hitCooldowns[stat] = scaledTime + reHitDelay;
            stat.TakeDamage(type, damage[0], critRate, critDamage);
        }
    }
}