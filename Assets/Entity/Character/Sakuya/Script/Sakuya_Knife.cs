using UnityEngine;
using System.Collections.Generic;

public class SakuyaKnife : BulletObject
{
    private Hitbox hitbox;

    public int FlipDirect { get; set; } = 1;
    private float lastHitTime = 0;

    protected override void Awake()
    {
        base.Awake();
        hitbox       = GetComponent<BoxHitbox>();
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
        EventManager.Player.PlayerFlipCall.Get(nameChannel).Invoke(this);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        EventManager.Player.OnAttackEnd.Get(nameChannel).Invoke(this, null);
    }

    protected override void CustomMovement()
    {
        rb.linearVelocity = new Vector2(speed * FlipDirect * FixedDeltaTime, 0.0f);
    }

    public override void SendDamage()
    {
        try
        {
            var enemies = hitbox.detectObject(EnableDamage);
            foreach (var enemy in enemies)
            {
                enemy.GetComponent<Stat>().TakeDamage(type, damage[0], critRate, critDamage);

                var em = enemy.GetComponent<StatusEffectManager>();
                if (em == null) continue;
                em.AddEffect(EffectType.Burn);

                
                /*
                em.AddStack(new BurnEffect(), stat, duration: 5f, tickInterval: 0.5f, maxStacks: 5);
                em.AddStack(new PoisonEffect(), stat, duration: 5f, tickInterval: 1.0f, maxStacks: 5);
                em.AddStack(new BleedEffect(), stat, duration: 5f, tickInterval: 0.8f, maxStacks: 3);

                em.Apply(new SlowEffect(), stat, duration: 3f);
                em.Apply(new StunEffect(), stat, duration: 2f);

                em.AddMilestoneStack(new FreezeEffect(), stat, threshold: 5, triggerDuration: 2f,
                    stackDuration: 8f);
                em.AddMilestoneStack(new ShockEffect(), stat, threshold: 3, triggerDuration: 1f,
                    stackDuration: 6f);
                */
            }
        }
        finally
        {
            
        }
    }
}