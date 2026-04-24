using System.Collections.Generic;
using UnityEngine;

public class EasyBulletWithoutExplosionObject : BulletObject
{
    // ── State ─────────────────────────────────────────────────────
    private Hitbox hitbox;
    private readonly HashSet<Stat> hitTargets = new HashSet<Stat>();


    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        hitbox = GetComponent<Hitbox>();
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        SendDamage();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        hitTargets.Clear();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
    }

    // ── Damage ────────────────────────────────────────────────────
    public override void SendDamage()
    {
        List<GameObject> Enemy = hitbox.detectObject(EnableDamage);

        for (int i = 0; i < Enemy.Count; i++)
        {
            var stat = Enemy[i].GetComponent<Stat>();
            if (stat == null || hitTargets.Contains(stat)) continue;

            hitTargets.Add(stat);
            stat.TakeDamage(type, damage[0], critRate, critDamage);
        }
    }
}
