using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(TimeScale))]
public class ProjectileObject : SkillObject
{
    protected String nameChannel = "";
    
    protected DamageType type = DamageType.True;
    protected List<float> damage = new List<float> { 100.0f };
    protected float critRate = 0.0f;
    protected float critDamage = 0.0f;
    protected float attackSpeed = 100.0f;
    
    [SerializeField] private bool canSpeedUp = false;
    [SerializeField] protected LayerMask enableDamage;

    private Animator amt;

    protected virtual void Awake()
    {
        objectSkillType = ObjectSkillType.bullet;
        amt = GetComponent<Animator>();
    }

    protected virtual void OnEnable()
    {
        if(canSpeedUp)
            EventManager.Player.OnPlayerAttackSpeedChange.Get(nameChannel).AddListener((component, data) => ChangeAttackSpeed(float.Parse(data.ToString())));
    }

    protected virtual void OnDisable()
    {
        if(canSpeedUp)
            EventManager.Player.OnPlayerAttackSpeedChange.Get(nameChannel).RemoveListener((component, data) => ChangeAttackSpeed(float.Parse(data.ToString())));
        
    }

    private void ChangeAttackSpeed(float _attackSpeed)
    {
        if (canSpeedUp)
        {
           attackSpeed = _attackSpeed;
           amt.speed = attackSpeed / 100.0f; 
        }
    }
    
    private void ChangeAttackSpeed()
    {
        if(canSpeedUp)
            amt.speed = attackSpeed / 100.0f;
    }

    public void SetUp(String nameChannel,DamageType type, List<float> damage, float critRate, float critDamage, float attackSpeed)
    {
        this.nameChannel = nameChannel;
        this.type = type;
        this.damage = damage;
        this.critRate = critRate;
        this.critDamage = critDamage;
        this.attackSpeed = attackSpeed;
        ChangeAttackSpeed();
    }
    
    public void SetUp(DamageType type, List<float> damage, float critRate, float critDamage, float attackSpeed)
    {
        this.type = type;
        this.damage = damage;
        this.critRate = critRate;
        this.critDamage = critDamage;
        this.attackSpeed = attackSpeed;
        ChangeAttackSpeed();
    }
    
    public void SetUp(DamageType type, List<float> damage, float critRate, float critDamage)
    {
        this.type = type;
        this.damage = damage;
        this.critRate = critRate;
        this.critDamage = critDamage;
    }
    
    public void SetUp(DamageType type, List<float> damage, Stat stat, float critRate, float critDamage)
    {
        this.type = type;
        this.damage = damage;
        this.critRate = critRate;
        this.critDamage = critDamage;
        this.stat = stat;
    }
    
    public void SetUp(String nameChannel)
    {
        this.nameChannel = nameChannel;
    }

    public virtual void SendDamage(){}
    
    public LayerMask EnableDamage
    {
        get => enableDamage;
        set => enableDamage = value;
    }
    
}
