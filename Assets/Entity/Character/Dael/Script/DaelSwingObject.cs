using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Melee swing object cho Dael.
/// Object cha: rotate Z (collider follow).
/// Object con SwordVisual: Sprite + Trail.
/// Dùng unscaledDeltaTime → immune với timestop.
/// OnDisable fire EventManager.Projectile.OnSlashEnd với DaelSlashData.
/// </summary>
public class DaelSwingObject : SkillObject
{
    // ── Config ────────────────────────────────────────────────────
    [SerializeField] private float      swingSpeed        = 360f;
    [SerializeField] private float      totalArc          = 120f;
    [SerializeField] private float      startArcPercent   = 0.33f;
    [SerializeField] private DamageType defaultDamageType = DamageType.Physical;
    [SerializeField] private float      defaultDamage     = 100f;
    [SerializeField] private float      defaultCritRate   = 0f;
    [SerializeField] private float      defaultCritDamage = 0f;
    [SerializeField] private LayerMask  enableDamage;

    [Header("Visual")]
    [SerializeField] private Transform      swordVisual;
    [SerializeField] private SpriteRenderer swordRenderer;

    [Header("Tilt Clamp")]
    [SerializeField] private float maxTiltAngle = 60f;

    [Header("Color Depth (Fake 3D)")]
    [SerializeField] private Color farColor  = new Color(0.5f, 0.5f, 0.6f, 1f);
    [SerializeField] private Color nearColor = Color.white;

    // ── Runtime ───────────────────────────────────────────────────
    private string              entityKey;
    private DamageType          damageType;
    private List<float>         damage       = new List<float>();
    private float               critRate;
    private float               critDamage;
    private float               attackSpeed  = 100f;
    private bool                isSlash3;
    private bool                isSlash3Hit1;
    private string              slashId;
    private int                 flipDirect     = 1;
    private int                 swingDirection = 1;

    private float               sweptAngle;
    private float               _totalArcOverride = -1f;
    private float               startAngle;
    private float               startRotX;
    private float               startRotY;
    private int                 hitCount;
    private HashSet<GameObject> hitObjects = new HashSet<GameObject>();

    // ── Lifecycle ─────────────────────────────────────────────────
    private void OnEnable()
    {
        hitCount   = 0;
        sweptAngle = 0f;
        hitObjects.Clear();

        if (damage.Count == 0)
        {
            damageType = defaultDamageType;
            damage     = new List<float> { defaultDamage };
            critRate   = defaultCritRate;
            critDamage = defaultCritDamage;
        }

        Vector3 euler = transform.rotation.eulerAngles;
        transform.rotation = Quaternion.Euler(euler.x, euler.y, startAngle);

        if (swordVisual != null)
            swordVisual.localRotation = Quaternion.identity;

        if (swordRenderer != null)
            swordRenderer.color = farColor;

        UpdateFlip();
    }

    private void OnDisable()
    {
        EventManager.Projectile.OnSlashEnd
            .Get(entityKey)
            .Invoke(this, new DaelSlashData
            {
                HitCount     = hitCount,
                IsSlash3     = isSlash3,
                IsSlash3Hit1 = isSlash3Hit1,
                SlashId      = slashId
            });
    }

    private void Update()
    {
        float delta = swingSpeed * (attackSpeed / 100f) * Time.unscaledDeltaTime;
        float dir   = (flipDirect >= 0 ? -1f : 1f) * swingDirection;

        transform.Rotate(0f, 0f, dir * delta);
        sweptAngle += delta;

        float t     = Mathf.Clamp01(sweptAngle / totalArc);
        float curve = Mathf.Sin(t * Mathf.PI);

        if (swordVisual != null)
        {
            float amp  = Mathf.Min(maxTiltAngle, 60f);
            float ampX = Mathf.Clamp(startRotX, -amp, amp);
            float ampY = Mathf.Clamp(startRotY, -amp, amp);
            swordVisual.localRotation = Quaternion.Euler(ampX * curve, ampY * curve, 0f);
        }

        if (swordRenderer != null)
            swordRenderer.color = Color.Lerp(farColor, nearColor, curve);

        float arcToUse = _totalArcOverride > 0f ? _totalArcOverride : totalArc;
        if (sweptAngle >= arcToUse)
            gameObject.SetActive(false);
    }

    // ── Trigger ───────────────────────────────────────────────────
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (((1 << other.gameObject.layer) & enableDamage) == 0) return;
        if (hitObjects.Contains(other.gameObject)) return;

        var targetStat = other.GetComponent<Stat>();
        if (targetStat == null) return;

        hitObjects.Add(other.gameObject);
        hitCount++;

        float dmg = damage.Count > 0 ? damage[0] : 0f;
        targetStat.TakeDamage(damageType, dmg, critRate, critDamage, iFrameDuration);
    }

    // ── Setup ─────────────────────────────────────────────────────
    public void SetUp(string entityKey, DamageType damageType, List<float> damage,
                      float critRate, float critDamage, float iFrameDuration,
                      float attackSpeed, float centerAngle, int flipDirect,
                      float _rotX = 0f, float _rotY = 0f,
                      bool isSlash3 = false, bool isSlash3Hit1 = false, string slashId = "",
                      int swingDirection = 1, float totalArcOverride = -1f)
    {
        this.entityKey      = entityKey;
        this.damageType     = damageType;
        this.damage         = damage;
        this.critRate       = critRate;
        this.critDamage     = critDamage;
        this.iFrameDuration = iFrameDuration;
        this.attackSpeed    = attackSpeed;
        this.flipDirect     = flipDirect;
        this.isSlash3       = isSlash3;
        this.isSlash3Hit1   = isSlash3Hit1;
        this.slashId        = slashId;
        this.swingDirection = swingDirection >= 0 ? 1 : -1;
        startRotX = _rotX;
        startRotY = _rotY;

        float arc    = totalArcOverride > 0f ? totalArcOverride : totalArc;
        float arcOffset = arc * startArcPercent * swingDirection;
        startAngle = centerAngle + arcOffset;
        if (totalArcOverride > 0f)
        {
            // Override totalArc runtime cho swing này
            _totalArcOverride = totalArcOverride;
        }
        else
        {
            _totalArcOverride = -1f;
        }
    }

    // ── Flip ──────────────────────────────────────────────────────
    private void UpdateFlip()
    {
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * (flipDirect >= 0 ? 1f : -1f);
        transform.localScale = scale;
    }
}