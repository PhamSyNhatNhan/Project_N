using System.Collections.Generic;
using UnityEngine;

public class EasyProjectileObject4 : ProjectileObject
{
    private Hitbox hitbox;
    private readonly HashSet<Stat> hitTargets = new HashSet<Stat>();

    protected override void Awake()
    {
        base.Awake();
        hitbox = GetComponent<Hitbox>();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        hitTargets.Clear();
    }

    public override void SendDamage()
    {
        List<GameObject> Enemy = hitbox.detectObject(EnableDamage, 0);

        for (int i = 0; i < Enemy.Count; i++)
        {
            var stat = Enemy[i].GetComponent<Stat>();
            if (stat == null || hitTargets.Contains(stat)) continue;

            hitTargets.Add(stat);
            stat.TakeDamage(type, damage[0], critRate, critDamage);
        }
    }
    
    public void SendDamage2()
    {
        List<GameObject> Enemy = hitbox.detectObject(EnableDamage, 1);

        for (int i = 0; i < Enemy.Count; i++)
        {
            var stat = Enemy[i].GetComponent<Stat>();
            if (stat == null || hitTargets.Contains(stat)) continue;

            hitTargets.Add(stat);
            stat.TakeDamage(type, damage[0], critRate, critDamage);
        }
    }
    
    public void SendDamage3()
    {
        List<GameObject> Enemy = hitbox.detectObject(EnableDamage, 2);

        for (int i = 0; i < Enemy.Count; i++)
        {
            var stat = Enemy[i].GetComponent<Stat>();
            if (stat == null || hitTargets.Contains(stat)) continue;

            hitTargets.Add(stat);
            stat.TakeDamage(type, damage[0], critRate, critDamage);
        }
    }
    
    public void SendDamage4()
    {
        List<GameObject> Enemy = hitbox.detectObject(EnableDamage, 3);

        for (int i = 0; i < Enemy.Count; i++)
        {
            var stat = Enemy[i].GetComponent<Stat>();
            if (stat == null || hitTargets.Contains(stat)) continue;

            hitTargets.Add(stat);
            stat.TakeDamage(type, damage[0], critRate, critDamage);
        }
    }
}
