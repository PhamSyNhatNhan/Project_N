using System;
using System.Collections.Generic;
using UnityEngine;

public class GuraSpecAttackEnd : ProjectileObject
{
    private EllipseHitbox hitbox;
    private int numberAttack = 0;
    
    protected override void Awake()
    {
        base.Awake();
        hitbox = GetComponent<EllipseHitbox>();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        numberAttack = 0;
    }

    public override void SendDamage()
    {
        List<GameObject> Enemy = hitbox.detectObject(EnableDamage, numberAttack);

        for (int i = 0; i < Enemy.Count; i++)
        {
            try
            {
                Enemy[i].GetComponent<Stat>().TakeDamage(type, damage[numberAttack], critRate, critDamage);
            }
            catch (Exception e)
            {
                Enemy[i].GetComponent<Stat>().TakeDamage(type, damage[damage.Count - 1], critRate, critDamage);
            }
        }

        numberAttack += 1;
        if (numberAttack > 2) numberAttack = 2;
    }
}
