using System.Collections.Generic;
using UnityEngine;

public class GuraAttack : ProjectileObject
{
    private Hitbox hitbox;

    protected override void Awake()
    {
        base.Awake();
        hitbox = GetComponent<Hitbox>();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        
        if (EventManager.Player.PlayerFlipCall != null)
        {
            EventManager.Player.PlayerFlipCall.Get(nameChannel).Invoke(this);
        } 
    }
    protected override void OnDisable()
    {
        base.OnDisable();

        if (EventManager.Player.OnAttackEnd != null)
        {
            EventManager.Player.OnAttackEnd.Get(nameChannel).Invoke(this, null);
        }
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
