using System.Collections.Generic;
using UnityEngine;

public class InfernoBulletExplosion : ProjectileObject
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
        List<GameObject> Enemy = hitbox.detectObject(EnableDamage);

        for (int i = 0; i < Enemy.Count; i++)
        {
            var stat = Enemy[i].GetComponent<Stat>();
            if (stat == null || hitTargets.Contains(stat)) continue;

            hitTargets.Add(stat);
            stat.TakeDamage(type, damage[0], critRate, critDamage);
            
            var em = Enemy[i].GetComponent<StatusEffectManager>();
            if (em == null) continue;

            em.AddStack(EffectType.Burn, stat);
        }
    }
    
}
