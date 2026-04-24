using UnityEngine;
using System.Collections.Generic;

public class InfernoBullet : BulletObject
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Inferno")]
    private float lastHitTime = 0;
    [SerializeField] private GameObject InfernoBulletExplosion;

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

    protected override void Update()
    {
        base.Update();
        lastHitTime += DeltaTime;
        if (lastHitTime > 0.05f)
        {
            SendDamage();
            lastHitTime = 0.0f;
        }
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
        explosion = Instantiate(InfernoBulletExplosion);
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