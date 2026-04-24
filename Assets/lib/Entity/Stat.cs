using System;
using System.Collections;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class Stat : MonoBehaviour, ILoadable<StatData>
{
    [Header("Name")] 
    [SerializeField] private String nameCharacter;
    
    [Header("Health")]
    [SerializeField] private float baseHealth = 5000.0f;
    [SerializeField] private float maxHealth;
    [SerializeField] private float curHealth;
    [SerializeField] private float bonusFlatHealth;
    [SerializeField] private float bonusMultiplierHealth;

    // ── Persistence — chỉ dùng cho Player, reset sau ApplyData ───
    private float _savedCurHealth = -1f;
    
    [Header("Defense")]
    [SerializeField] private float baseDefense = 100.0f;
    [SerializeField] private float maxDefense;
    [SerializeField] private float curDefense;
    [SerializeField] private float bonusFlatDefense;
    [SerializeField] private float bonusMultiplierDefense;
    
    [Header("Resistant")]
    [SerializeField] private float baseResistantPhysical = 0.0f;
    [SerializeField] private float maxResistantPhysical;
    [SerializeField] private float curResistantPhysical;
    [SerializeField] private float bonusFlatResistantPhysical;
    [SerializeField] private float bonusMultiplierResistantPhysical;
    
    [SerializeField] private float baseResistantMagic = 0.0f;
    [SerializeField] private float maxResistantMagic;
    [SerializeField] private float curResistantMagic;
    [SerializeField] private float bonusFlatResistantMagic;
    [SerializeField] private float bonusMultiplierResistantMagic;
    
    [Header("Attack")]
    [SerializeField] private float baseAttack = 0.0f;
    [SerializeField] private float maxAttack;
    [SerializeField] private float curAttack;
    [SerializeField] private float bonusFlatAttack;
    [SerializeField] private float bonusMultiplierAttack;
    
    [Header("AttackSpeed")] //Tinh theo %
    [SerializeField] private float baseAttackSpeed = 100.0f;
    [SerializeField] private float maxAttackSpeed;
    [SerializeField] private float curAttackSpeed;
    [SerializeField] private float bonusMultiplierAttackSpeed;
    
    [Header("BonusDamage")] //Tinh theo %
    [SerializeField] private float baseBonusDamage = 0.0f;
    [SerializeField] private float maxBonusDamage;
    [SerializeField] private float curBonusDamage;
    [SerializeField] private float bonusMultiplierBonusDamage;
    
    [Header("BonusPhysical")] //Tinh theo %
    [SerializeField] private float baseBonusPhysical = 0.0f;
    [SerializeField] private float maxBonusPhysical;
    [SerializeField] private float curBonusPhysical;
    [SerializeField] private float bonusMultiplierBonusPhysical;
    
    [Header("BonusMagic")] //Tinh theo %
    [SerializeField] private float baseBonusMagic = 0.0f;
    [SerializeField] private float maxBonusMagic;
    [SerializeField] private float curBonusMagic;
    [SerializeField] private float bonusMultiplierBonusMagic;
    
    [Header("MultiplierDamageBonus")] //Tinh theo %
    [SerializeField] private float baseMultiplierDamageBonus = 0.0f;
    [SerializeField] private float maxMultiplierDamageBonus;
    [SerializeField] private float curMultiplierDamageBonus;
    [SerializeField] private float bonusMultiplierMultiplierDamageBonus;
    
    [Header("MultiplierDamageTaken")] //Tinh theo %
    [SerializeField] private float baseMultiplierDamageTaken = 0.0f;
    [SerializeField] private float maxMultiplierDamageTaken;
    [SerializeField] private float curMultiplierDamageTaken;
    [SerializeField] private float bonusMultiplierMultiplierDamageTaken;
    
    [Header("CritRate")] //Tinh theo %
    [SerializeField] private float baseCritRate = 5.0f;
    [SerializeField] private float maxCritRate;
    [SerializeField] private float curCritRate;
    [SerializeField] private float bonusMultiplierCritRate;
    
    [Header("CritDamage")] //Tinh theo %
    [SerializeField] private float baseCritDamage = 50.0f;
    [SerializeField] private float maxCritDamage;
    [SerializeField] private float curCritDamage;
    [SerializeField] private float bonusMultiplierCritDamage;

    // ── Buff / Debuff tạm thời (StatBonus) ───────────────────────────
    // Không dùng SerializeField vì đây là runtime data, không cần lưu trong Inspector
    protected StatBonus buffHealth                  = new StatBonus();
    protected StatBonus buffDefense                 = new StatBonus();
    protected StatBonus buffResistantPhysical       = new StatBonus();
    protected StatBonus buffResistantMagic          = new StatBonus();
    protected StatBonus buffAttack                  = new StatBonus();
    protected StatBonus buffAttackSpeed             = new StatBonus();
    protected StatBonus buffBonusDamage             = new StatBonus();
    protected StatBonus buffBonusPhysical           = new StatBonus();
    protected StatBonus buffBonusMagic              = new StatBonus();
    protected StatBonus buffMultiplierDamageBonus   = new StatBonus();
    protected StatBonus buffMultiplierDamageTaken   = new StatBonus();
    protected StatBonus buffCritRate                = new StatBonus();
    protected StatBonus buffCritDamage              = new StatBonus();

    [Header("DamageCaculation")] 
    [SerializeField] private bool canDamge = true;
    private float lastDamageTime  = -100.0f;
    [SerializeField] private float iFrame = 0.02f;
    [SerializeField] private GameObject popupTextPrefab;
    protected EasyPoolingList popupTextPool = new EasyPoolingList();

    protected string entityKey => $"{nameCharacter}_{GetInstanceID()}";

    protected StatData rawStatData;
    
    public event System.Func<float, DamageType, float> OnBeforeTakeDamage;

    public event System.Action<float, DamageType> OnAfterTakeDamage;


    protected virtual void Awake()
    {
        popupTextPool.SetPrefab(popupTextPrefab);
    }

    protected virtual void Start()
    {
        if (!GetComponent<EntityLoader>())
            setStartStat();
    }

    // ── ILoadable ─────────────────────────────────────────────────
    public void LoadRawData(StatData data)
    {
        rawStatData           = data;
        baseHealth            = data.baseHealth;
        baseDefense           = data.baseDefense;
        baseAttack            = data.baseAttack;
        baseAttackSpeed       = data.baseAttackSpeed;
        baseCritRate          = data.baseCritRate;
        baseCritDamage        = data.baseCritDamage;
        baseResistantPhysical = data.baseResistantPhysical;
        baseResistantMagic    = data.baseResistantMagic;
    }

    public virtual void ApplyData()
    {
        setStartStat();
    }

    /// <summary>
    /// Gọi TRƯỚC ApplyData() để restore HP từ tầng trước.
    /// Chỉ dùng cho Player — Enemy không cần.
    /// </summary>
    public void LoadPersistenceHealth(float health)
    {
        _savedCurHealth = health;
    }

    protected virtual void setStartStat()
    {
        // max* = chỉ số cố định (base + bonus equipment)
        maxHealth            = baseHealth            * (1.0f + (bonusMultiplierHealth / 100.0f))            + bonusFlatHealth;
        maxDefense           = baseDefense           * (1.0f + (bonusMultiplierDefense / 100.0f))           + bonusFlatDefense;
        maxResistantPhysical = baseResistantPhysical * (1.0f + (bonusMultiplierResistantPhysical / 100.0f)) + bonusFlatResistantPhysical;
        maxResistantMagic    = baseResistantMagic    * (1.0f + (bonusMultiplierResistantMagic / 100.0f))    + bonusFlatResistantMagic;
        maxAttack            = baseAttack            * (1.0f + (bonusMultiplierAttack / 100.0f))            + bonusFlatAttack;
        maxAttackSpeed                 = baseAttackSpeed             + bonusMultiplierAttackSpeed;
        maxBonusDamage                 = baseBonusDamage             + bonusMultiplierBonusDamage;
        maxBonusPhysical               = baseBonusPhysical           + bonusMultiplierBonusPhysical;
        maxBonusMagic                  = baseBonusMagic              + bonusMultiplierBonusMagic;
        maxMultiplierDamageBonus       = baseMultiplierDamageBonus   + bonusMultiplierMultiplierDamageBonus;
        maxMultiplierDamageTaken       = baseMultiplierDamageTaken   + bonusMultiplierMultiplierDamageTaken;
        maxCritRate                    = baseCritRate                + bonusMultiplierCritRate;
        maxCritDamage                  = baseCritDamage              + bonusMultiplierCritDamage;

        // cur* = max* + buff/debuff tạm thời từ StatBonus
        // ── Health: restore từ save nếu có, ngược lại full HP ────
        float buffedMaxHealth = buffHealth.GetFinalValue(maxHealth);
        curHealth = (_savedCurHealth > 0f)
            ? Mathf.Min(_savedCurHealth, buffedMaxHealth)
            : buffedMaxHealth;
        _savedCurHealth = -1f; // reset sau khi dùng, tránh ảnh hưởng RecalculateStat

        curDefense               = buffDefense.GetFinalValue(maxDefense);
        curResistantPhysical     = buffResistantPhysical.GetFinalValue(maxResistantPhysical);
        curResistantMagic        = buffResistantMagic.GetFinalValue(maxResistantMagic);
        curAttack                = buffAttack.GetFinalValue(maxAttack);
        curAttackSpeed           = buffAttackSpeed.GetFinalValue(maxAttackSpeed);
        curBonusDamage           = buffBonusDamage.GetFinalValue(maxBonusDamage);
        curBonusPhysical         = buffBonusPhysical.GetFinalValue(maxBonusPhysical);
        curBonusMagic            = buffBonusMagic.GetFinalValue(maxBonusMagic);
        curMultiplierDamageBonus = buffMultiplierDamageBonus.GetFinalValue(maxMultiplierDamageBonus);
        curMultiplierDamageTaken = buffMultiplierDamageTaken.GetFinalValue(maxMultiplierDamageTaken);
        curCritRate              = buffCritRate.GetFinalValue(maxCritRate);
        curCritDamage            = buffCritDamage.GetFinalValue(maxCritDamage);
    }

    public virtual void RecalculateStat()
    {
        float oldBuffedMax  = buffHealth.GetFinalValue(maxHealth);
        float healthPercent = oldBuffedMax > 0f ? curHealth / oldBuffedMax : 1f;
        setStartStat();
        float newBuffedMax = buffHealth.GetFinalValue(maxHealth);
        curHealth = newBuffedMax * healthPercent;
        EventManager.Entity.OnEntityHealthChanged.Get(entityKey).Invoke(this, newBuffedMax > 0f ? curHealth / newBuffedMax : 0f);
    }

    /// <summary>
    /// Khởi tạo chỉ số với hệ số scale (dùng cho quái ở tầng cao).
    /// Ghi đè các bonusMultiplier rồi recalculate toàn bộ stat.
    /// </summary>
    /// <param name="config">Cấu hình scale theo từng nhóm chỉ số.</param>
    public virtual void Initialize(ScalingConfig config)
    {
        bonusMultiplierHealth                += config.healthMultiplier;
        bonusMultiplierDefense               += config.defenseMultiplier;
        bonusMultiplierResistantPhysical     += config.resistantPhysicalMultiplier;
        bonusMultiplierResistantMagic        += config.resistantMagicMultiplier;
        bonusMultiplierAttack                += config.attackMultiplier;
        bonusMultiplierAttackSpeed           += config.attackSpeedMultiplier;
        bonusMultiplierBonusDamage           += config.bonusDamageMultiplier;
        bonusMultiplierBonusPhysical         += config.bonusPhysicalMultiplier;
        bonusMultiplierBonusMagic            += config.bonusMagicMultiplier;
        bonusMultiplierCritRate              += config.critRateMultiplier;
        bonusMultiplierCritDamage            += config.critDamageMultiplier;
        bonusMultiplierMultiplierDamageBonus += config.multiplierDamageBonusMultiplier;
        bonusMultiplierMultiplierDamageTaken += config.multiplierDamageTakenMultiplier;

        setStartStat();
    }

    public virtual void TakeDamage(DamageType type , float damage, float _critRate, float _critDamage)
    {
        //Debug.Log(entityKey + " take dmg");
        if ((Time.time >= lastDamageTime + iFrame) && canDamge)
        {
            lastDamageTime = Time.time;
            float tmpDmgTake = 0.0f;

            if (type == DamageType.True)
            {
                tmpDmgTake = damage * (1.0f + curMultiplierDamageTaken / 100f);
            }
            else if (type == DamageType.Physical)
            {
                tmpDmgTake = damage * (CaculateResistant(type)) * (CaculateDefense()) *
                             (1.0f + curMultiplierDamageTaken / 100f);
            }
            else if (type == DamageType.Magic)
            {
                tmpDmgTake = damage * (CaculateResistant(type)) *
                             (1.0f + curMultiplierDamageTaken / 100f);
            }

            bool isCrit = false;
            if (UnityEngine.Random.Range(0f, 100f) <= _critRate)
            {
                isCrit = true;
                tmpDmgTake *= (1.0f + _critDamage / 100.0f);
            }

            // Nhân hệ số ngẫu nhiên 0.8 - 1.2
            tmpDmgTake *= UnityEngine.Random.Range(0.8f, 1.2f);

            if (popupTextPrefab)
                PopupTextShow(type, tmpDmgTake, isCrit);

            // Hook trước khi trừ HP — buff có thể modify damage
            if (OnBeforeTakeDamage != null)
                foreach (System.Func<float, DamageType, float> hook in OnBeforeTakeDamage.GetInvocationList())
                    tmpDmgTake = hook(tmpDmgTake, type);

            tmpDmgTake = Mathf.Max(0f, tmpDmgTake);
            curHealth -= tmpDmgTake;

            // Hook sau khi trừ HP
            OnAfterTakeDamage?.Invoke(tmpDmgTake, type);

            float buffedMaxHealth = buffHealth.GetFinalValue(maxHealth);
            EventManager.Entity.OnEntityHealthChanged.Get(entityKey).Invoke(this, buffedMaxHealth > 0f ? curHealth / buffedMaxHealth : 0f);
            if (curHealth <= 0) OnDead();
        }
    }

    protected virtual void OnDead()
    {
        EventManager.Entity.OnEntityDead.Get(entityKey).Invoke(this, null);
        Destroy(gameObject);
    }

    protected virtual void OnDestroy()
    {
        popupTextPool.ClearPool();
    }

    protected virtual float CaculateResistant(DamageType type)
    {
        if (type == DamageType.Physical)
        {
            if ((curResistantPhysical / 100.0f) < 0)
            {
                return 1.0f - ((curResistantPhysical / 100.0f) * 0.5f);
            }
            else if ((curResistantPhysical / 100.0f) >= 0.0f && (curResistantPhysical / 100.0f) <= 0.3f)
            {
                return 1.0f - (curResistantPhysical / 100.0f);
            }
            else
            {
                return 0.3f + (1.0f / (4.0f * (curResistantPhysical / 100.0f) + 1.0f));
            }
        }
        else if (type == DamageType.Magic)
        {
            if ((curResistantMagic / 100.0f) < 0)
            {
                return 1.0f - ((curResistantMagic / 100.0f) * 0.5f);
            }
            else if ((curResistantMagic / 100.0f) >= 0.0f && (curResistantMagic / 100.0f) <= 0.7f)
            {
                return 1.0f - (curResistantMagic / 100.0f);
            }
            else
            {
                return 0.7f + (1.0f / (4.0f * (curResistantMagic / 100.0f) + 1.0f));
            }
        }
        else
        {
            return 0.0f;
        }
    }

    protected virtual float CaculateDefense()
    {
        return 1.0f - curDefense / (curDefense * 2.0f + 500);
    }
    

    private void PopupTextShow(DamageType type, float damage, bool isCrit)
    {
        GameObject popText = popupTextPool.GetGameObject();
        if (popText == null) return;

        // Set position trước, parent null sau để tránh bị flip theo nhân vật
        popText.transform.position   = transform.position;
        popText.transform.rotation   = Quaternion.identity;
        //popText.transform.localScale = Vector3.one;
        popText.transform.SetParent(null);

        // Truyền TimeScale trước khi active
        popText.GetComponent<PopupText>()?.SetTimeScale(GetComponent<TimeScale>());
        popText.SetActive(true);

        TextMeshPro tm = popText.GetComponent<TextMeshPro>();
        if (tm == null) return;

        Color mainColor = type switch
        {
            DamageType.True     => Color.white,
            DamageType.Physical => Color.cyan,
            DamageType.Magic    => Color.magenta,
            _                   => Color.white
        };

        Color nonCritColor = type switch
        {
            DamageType.True     => new Color(0.85f, 0.85f, 0.85f),
            DamageType.Physical => new Color(0f,    0.6f,  0.6f),
            DamageType.Magic    => new Color(0.6f,  0f,    0.6f),
            _                   => new Color(0.85f, 0.85f, 0.85f)
        };

        tm.color    = isCrit ? mainColor : nonCritColor;
        tm.fontSize = isCrit ? 60 : 50;
        tm.text     = isCrit
            ? "\u2728" + Mathf.RoundToInt(damage)
            : Mathf.RoundToInt(damage).ToString();
    }
    
    public float CaculateDamage(DamageType type, float damage)
    {
        float tmpDamage = 0.0f;
        
        if (type == DamageType.True)
        {
            tmpDamage = damage * (1.0f + curBonusDamage / 100.0f) * (1.0f + curMultiplierDamageBonus / 100.0f);
        }
        else if (type == DamageType.Physical)
        {
            tmpDamage = damage * (1.0f + (curBonusDamage + curBonusPhysical) / 100.0f) * (1.0f + curMultiplierDamageBonus / 100.0f);
        }
        else if (type == DamageType.Magic)
        {
            tmpDamage = damage * (1.0f + (curBonusDamage + curBonusMagic) / 100.0f) * (1.0f + curMultiplierDamageBonus / 100.0f);
        }
        
        return tmpDamage;
    }


    public float BaseHealth
    {
        get => baseHealth;
        set => baseHealth = value;
    }

    public float MaxHealth
    {
        get => maxHealth;
        set => maxHealth = value;
    }

    public float CurHealth
    {
        get => curHealth;
        set => curHealth = value;
    }

    public float BonusFlatHealth
    {
        get => bonusFlatHealth;
        set => bonusFlatHealth = value;
    }

    public float BonusMultiplierHealth
    {
        get => bonusMultiplierHealth;
        set => bonusMultiplierHealth = value;
    }
    
    public float BaseDefense
    {
        get => baseDefense;
        set => baseDefense = value;
    }

    public float MaxDefense
    {
        get => maxDefense;
        set => maxDefense = value;
    }

    public float CurDefense
    {
        get => curDefense;
        set => curDefense = value;
    }

    public float BonusFlatDefense
    {
        get => bonusFlatDefense;
        set => bonusFlatDefense = value;
    }

    public float BonusMultiplierDefense
    {
        get => bonusMultiplierDefense;
        set => bonusMultiplierDefense = value;
    }

    public float BaseResistantPhysical
    {
        get => baseResistantPhysical;
        set => baseResistantPhysical = value;
    }

    public float MaxResistantPhysical
    {
        get => maxResistantPhysical;
        set => maxResistantPhysical = value;
    }

    public float CurResistantPhysical
    {
        get => curResistantPhysical;
        set => curResistantPhysical = value;
    }

    public float BonusFlatResistantPhysical
    {
        get => bonusFlatResistantPhysical;
        set => bonusFlatResistantPhysical = value;
    }

    public float BonusMultiplierResistantPhysical
    {
        get => bonusMultiplierResistantPhysical;
        set => bonusMultiplierResistantPhysical = value;
    }

    public float BaseResistantMagic
    {
        get => baseResistantMagic;
        set => baseResistantMagic = value;
    }

    public float MaxResistantMagic
    {
        get => maxResistantMagic;
        set => maxResistantMagic = value;
    }

    public float CurResistantMagic
    {
        get => curResistantMagic;
        set => curResistantMagic = value;
    }

    public float BonusFlatResistantMagic
    {
        get => bonusFlatResistantMagic;
        set => bonusFlatResistantMagic = value;
    }

    public float BonusMultiplierResistantMagic
    {
        get => bonusMultiplierResistantMagic;
        set => bonusMultiplierResistantMagic = value;
    }

    public float BaseAttack
    {
        get => baseAttack;
        set => baseAttack = value;
    }

    public float MaxAttack
    {
        get => maxAttack;
        set => maxAttack = value;
    }

    public float CurAttack
    {
        get => curAttack;
        set => curAttack = value;
    }

    public float BonusFlatAttack
    {
        get => bonusFlatAttack;
        set => bonusFlatAttack = value;
    }

    public float BonusMultiplierAttack
    {
        get => bonusMultiplierAttack;
        set => bonusMultiplierAttack = value;
    }

    public float BaseBonusPhysical
    {
        get => baseBonusPhysical;
        set => baseBonusPhysical = value;
    }

    public float MaxBonusPhysical
    {
        get => maxBonusPhysical;
        set => maxBonusPhysical = value;
    }

    public float CurBonusPhysical
    {
        get => curBonusPhysical;
        set => curBonusPhysical = value;
    }

    public float BonusMultiplierBonusPhysical
    {
        get => bonusMultiplierBonusPhysical;
        set => bonusMultiplierBonusPhysical = value;
    }

    public float BaseBonusMagic
    {
        get => baseBonusMagic;
        set => baseBonusMagic = value;
    }

    public float MaxBonusMagic
    {
        get => maxBonusMagic;
        set => maxBonusMagic = value;
    }

    public float CurBonusMagic
    {
        get => curBonusMagic;
        set => curBonusMagic = value;
    }

    public float BonusMultiplierBonusMagic
    {
        get => bonusMultiplierBonusMagic;
        set => bonusMultiplierBonusMagic = value;
    }

    public float BaseMultiplierDamageBonus
    {
        get => baseMultiplierDamageBonus;
        set => baseMultiplierDamageBonus = value;
    }

    public float MaxMultiplierDamageBonus
    {
        get => maxMultiplierDamageBonus;
        set => maxMultiplierDamageBonus = value;
    }

    public float CurMultiplierDamageBonus
    {
        get => curMultiplierDamageBonus;
        set => curMultiplierDamageBonus = value;
    }

    public float BonusMultiplierMultiplierDamageBonus
    {
        get => bonusMultiplierMultiplierDamageBonus;
        set => bonusMultiplierMultiplierDamageBonus = value;
    }

    public float BaseMultiplierDamageTaken
    {
        get => baseMultiplierDamageTaken;
        set => baseMultiplierDamageTaken = value;
    }

    public float MaxMultiplierDamageTaken
    {
        get => maxMultiplierDamageTaken;
        set => maxMultiplierDamageTaken = value;
    }

    public float CurMultiplierDamageTaken
    {
        get => curMultiplierDamageTaken;
        set => curMultiplierDamageTaken = value;
    }

    public float BonusMultiplierMultiplierDamageTaken
    {
        get => bonusMultiplierMultiplierDamageTaken;
        set => bonusMultiplierMultiplierDamageTaken = value;
    }

    public float BaseCritRate
    {
        get => baseCritRate;
        set => baseCritRate = value;
    }

    public float MaxCritRate
    {
        get => maxCritRate;
        set => maxCritRate = value;
    }

    public float CurCritRate
    {
        get => curCritRate;
        set => curCritRate = value;
    }

    public float BonusMultiplierCritRate
    {
        get => bonusMultiplierCritRate;
        set => bonusMultiplierCritRate = value;
    }

    public float BaseCritDamage
    {
        get => baseCritDamage;
        set => baseCritDamage = value;
    }

    public float MaxCritDamage
    {
        get => maxCritDamage;
        set => maxCritDamage = value;
    }

    public float CurCritDamage
    {
        get => curCritDamage;
        set => curCritDamage = value;
    }

    public float BonusMultiplierCritDamage
    {
        get => bonusMultiplierCritDamage;
        set => bonusMultiplierCritDamage = value;
    }

    public float BaseBonusDamage
    {
        get => baseBonusDamage;
        set => baseBonusDamage = value;
    }

    public float MaxBonusDamage
    {
        get => maxBonusDamage;
        set => maxBonusDamage = value;
    }

    public float CurBonusDamage
    {
        get => curBonusDamage;
        set => curBonusDamage = value;
    }

    public float BonusMultiplierBonusDamage
    {
        get => bonusMultiplierBonusDamage;
        set => bonusMultiplierBonusDamage = value;
    }

    public bool CanDamge
    {
        get => canDamge;
        set => canDamge = value;
    }

    public float BaseAttackSpeed
    {
        get => baseAttackSpeed;
        set => baseAttackSpeed = value;
    }

    public float MaxAttackSpeed
    {
        get => maxAttackSpeed;
        set => maxAttackSpeed = value;
    }

    public float CurAttackSpeed
    {
        get => curAttackSpeed;
        set 
        {
            curAttackSpeed = value;
            if (EventManager.Player.OnPlayerAttackSpeedChange != null)
            {
                EventManager.Player.OnPlayerAttackSpeedChange.Get(nameCharacter).Invoke(this, curAttackSpeed);
            }
        }
    }

    public float BonusMultiplierAttackSpeed
    {
        get => bonusMultiplierAttackSpeed;
        set => bonusMultiplierAttackSpeed = value;
    }
    
    public String NameCharacter
    {
        get => nameCharacter;
        set => nameCharacter = value;
    }

    // ── StatBonus Properties ──────────────────────────────────────────
    public StatBonus BuffHealth                => buffHealth;
    public StatBonus BuffDefense               => buffDefense;
    public StatBonus BuffResistantPhysical     => buffResistantPhysical;
    public StatBonus BuffResistantMagic        => buffResistantMagic;
    public StatBonus BuffAttack                => buffAttack;
    public StatBonus BuffAttackSpeed           => buffAttackSpeed;
    public StatBonus BuffBonusDamage           => buffBonusDamage;
    public StatBonus BuffBonusPhysical         => buffBonusPhysical;
    public StatBonus BuffBonusMagic            => buffBonusMagic;
    public StatBonus BuffMultiplierDamageBonus => buffMultiplierDamageBonus;
    public StatBonus BuffMultiplierDamageTaken => buffMultiplierDamageTaken;
    public StatBonus BuffCritRate              => buffCritRate;
    public StatBonus BuffCritDamage            => buffCritDamage;
}