using System.Collections.Generic;
using UnityEngine;

public class EasyBulletWithoutResetObject : BulletObject
{
    // ── State ─────────────────────────────────────────────────────
    private Hitbox hitbox;

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
            stat.TakeDamage(type, damage[0], critRate, critDamage);
        }
    }
}
