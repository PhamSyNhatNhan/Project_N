using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(TimeScale))]
public class ProjectileObject : SkillObject
{
    protected string      nameChannel = "";
    protected DamageType  type        = DamageType.True;
    protected List<float> damage      = new List<float> { 100.0f };
    protected float       critRate    = 0.0f;
    protected float       critDamage  = 0.0f;
    protected float       attackSpeed = 100.0f;

    [SerializeField] private  bool      canSpeedUp   = false;
    [SerializeField] protected LayerMask enableDamage;

    private Animator amt;

    protected override void Awake()
    {
        base.Awake();
        objectSkillType = ObjectSkillType.bullet;
        amt             = GetComponent<Animator>();
    }

    protected virtual void OnEnable()
    {
        if (canSpeedUp)
            EventManager.Player.OnPlayerAttackSpeedChange
                .Get(nameChannel)
                .AddListener((component, data) => ChangeAttackSpeed(float.Parse(data.ToString())));
    }

    protected virtual void OnDisable()
    {
        if (canSpeedUp)
            EventManager.Player.OnPlayerAttackSpeedChange
                .Get(nameChannel)
                .RemoveListener((component, data) => ChangeAttackSpeed(float.Parse(data.ToString())));
    }

    private void ChangeAttackSpeed(float _attackSpeed)
    {
        if (!canSpeedUp) return;
        attackSpeed = _attackSpeed;
        amt.speed   = attackSpeed / 100.0f;
    }

    private void ChangeAttackSpeed()
    {
        if (canSpeedUp)
            amt.speed = attackSpeed / 100.0f;
    }

    // ── Setup ─────────────────────────────────────────────────────
    public void SetUp(string nameChannel, DamageType type, List<float> damage,
                      float critRate, float critDamage, float attackSpeed, float iFrameDuration = 0f)
    {
        this.nameChannel    = nameChannel;
        this.type           = type;
        this.damage         = damage;
        this.critRate       = critRate;
        this.critDamage     = critDamage;
        this.attackSpeed    = attackSpeed;
        this.iFrameDuration = iFrameDuration;
        ChangeAttackSpeed();
    }

    public void SetUp(DamageType type, List<float> damage,
                      float critRate, float critDamage, float attackSpeed, float iFrameDuration = 0f)
    {
        this.type           = type;
        this.damage         = damage;
        this.critRate       = critRate;
        this.critDamage     = critDamage;
        this.attackSpeed    = attackSpeed;
        this.iFrameDuration = iFrameDuration;
        ChangeAttackSpeed();
    }

    public void SetUp(DamageType type, List<float> damage,
                      float critRate, float critDamage, float iFrameDuration = 0f)
    {
        this.type           = type;
        this.damage         = damage;
        this.critRate       = critRate;
        this.critDamage     = critDamage;
        this.iFrameDuration = iFrameDuration;
    }

    public void SetUp(DamageType type, List<float> damage, Stat stat,
                      float critRate, float critDamage, float iFrameDuration = 0f)
    {
        this.type           = type;
        this.damage         = damage;
        this.critRate       = critRate;
        this.critDamage     = critDamage;
        this.stat           = stat;
        this.iFrameDuration = iFrameDuration;
    }

    public void SetUp(string nameChannel) => this.nameChannel = nameChannel;

    public virtual void SendDamage() {}

    public LayerMask EnableDamage
    {
        get => enableDamage;
        set => enableDamage = value;
    }
}