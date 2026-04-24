using System.Collections.Generic;
using UnityEngine;

public class CalamitasAcceleratingDarkMagicFlame : BulletObject
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Ring Burst")]
    [SerializeField] private GameObject ringBulletPrefab;
    [SerializeField] private int        ringCount = 6;

    // ── State ─────────────────────────────────────────────────────
    private Hitbox           hitbox;
    private bool             hitDisable; // true = disable do va chạm
    private readonly EasyPoolingList ringPool = new EasyPoolingList();

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        hitbox = GetComponent<Hitbox>();
        if (ringBulletPrefab != null) ringPool.SetPrefab(ringBulletPrefab);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        hitDisable = false;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        SpawnRingBurst();
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

        for (int i = 0; i < enemies.Count; i++)
        {
            var s = enemies[i].GetComponent<Stat>();
            if (s == null) continue;
            s.TakeDamage(type, damage[0], critRate, critDamage);
        }

        hitDisable = true;
        gameObject.SetActive(false);
    }

    // ── Ring Burst ────────────────────────────────────────────────
    private void SpawnRingBurst()
    {
        float step    = 360f / ringCount;
        float halfDmg = damage != null && damage.Count > 0 ? damage[0] * 0.5f : 100f;
        var   dmgList = new List<float> { halfDmg };

        for (int i = 0; i < ringCount; i++)
        {
            var obj = ringPool.GetGameObject();
            if (obj == null) continue;

            obj.transform.position = transform.position;
            obj.SetActive(true);

            var b = obj.GetComponent<BulletObject>();
            if (b == null) continue;

            b.SetUp(type, dmgList, stat, critRate, critDamage);
            b.EasyModeChange(BulletMoveMode.Angle, step * i);
        }
    }
}