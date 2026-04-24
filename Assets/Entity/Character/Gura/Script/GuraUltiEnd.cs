using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GuraUltiEnd : ProjectileObject
{
    private CircleHitbox hitbox;

    protected override void Awake()
    {
        base.Awake();
        hitbox = GetComponent<CircleHitbox>();
    }

    public override void SendDamage()
    {
        List<GameObject> Enemy = hitbox.detectObject(EnableDamage);

        for (int i = 0; i < Enemy.Count; i++)
        {
            Enemy[i].GetComponent<Stat>().TakeDamage(type, damage[0], critRate, critDamage);
        }
    }
}
