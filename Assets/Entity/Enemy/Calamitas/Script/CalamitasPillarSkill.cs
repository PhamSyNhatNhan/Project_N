using System.Collections.Generic;
using UnityEngine;

public class CalamitasPillarSkill : EnemySkill
{
    // ── Prefab ────────────────────────────────────────────────────
    [Header("Ring Bullet")]
    [SerializeField] private Transform  spawnTransform;   // điểm spawn bullet, assign trên prefab

    // ── Base config ───────────────────────────────────────────────
    [Header("Base")]
    [SerializeField] private int   baseBulletCount = 8;
    [SerializeField] private float baseFireRate    = 2f;

    // ── Escalation: deadCount = 1 (4 → 3 cột) ────────────────────
    [Header("Escalation 1 — thêm bullet")]
    [SerializeField] private int esc1BulletAdd = 4;

    // ── Escalation: deadCount = 2 (3 → 2 cột) ────────────────────
    [Header("Escalation 2 — burst đôi")]
    [SerializeField] private float burstDelay = 0.15f;

    // ── Escalation: deadCount = 3 (2 → 1 cột) ────────────────────
    [Header("Escalation 3 — tăng tất cả")]
    [SerializeField] private int   esc3BulletAdd = 6;
    [SerializeField] private float esc3FireRate  = 0.8f;

    // ── Runtime ───────────────────────────────────────────────────
    private Stat     pillarStat;
    private Animator amt;
    private int      deadCount;
    private int   curBulletCount;
    private float curFireRate;
    private float fireTimer;

    // Burst state (deadCount >= 2)
    private bool  waitingBurst;
    private float burstTimer;

    // Pool do CalamitasSkillImmortal0 sở hữu, truyền vào qua SetBulletPool
    private EasyPoolingList ringPool;

    private float DeltaTime => timeScale.DeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        pillarStat = GetComponent<Stat>();
        amt        = GetComponent<Animator>();
    }

    protected override void Start()
    {
        base.Start();
        ResetState();
    }

    // Gọi từ CalamitasSkillImmortal0 sau khi spawn pillar
    public void SetBulletPool(EasyPoolingList pool) => ringPool = pool;

    private void OnEnable()
    {
        ResetState();
        EventManager.Entity.OnEntityDead
            .Get("CalamitasPillar")
            .AddListener(OnAnyPillarDead);
    }

    private void OnDisable()
    {
        EventManager.Entity.OnEntityDead
            .Get("CalamitasPillar")
            .RemoveListener(OnAnyPillarDead);
    }

    // ── Update ────────────────────────────────────────────────────
    protected override void Update()
    {
        base.Update();
        UpdateShooting();
    }

    private void UpdateShooting()
    {
        // Đang chờ burst lần 2
        if (waitingBurst)
        {
            burstTimer -= DeltaTime;
            if (burstTimer <= 0f)
            {
                waitingBurst = false;
                FireRing();
            }
            return;
        }

        fireTimer -= DeltaTime;
        if (fireTimer > 0f) return;

        fireTimer = curFireRate;
        FireRing();

        // deadCount >= 2: bắn thêm 1 lần sau burstDelay
        if (deadCount >= 2)
        {
            waitingBurst = true;
            burstTimer   = burstDelay;
        }
    }

    // ── Fire ──────────────────────────────────────────────────────
    private void FireRing()
    {
        float step = 360f / curBulletCount;
        for (int i = 0; i < curBulletCount; i++)
            SpawnRingBullet(step * i);
    }

    private void SpawnRingBullet(float angle)
    {
        if (ringPool == null) return;
        GameObject obj = ringPool.GetGameObject();
        if (obj == null) return;

        Vector2 spawnPos = spawnTransform != null
            ? (Vector2)spawnTransform.position
            : (Vector2)transform.position;

        obj.transform.position = spawnPos;
        obj.SetActive(true);

        var bullet = obj.GetComponent<BulletObject>();
        if (bullet == null) return;

        bullet.SetUp(
            DamageType.Magic,
            GetDamageList("CalamitasPillarRing"),
            pillarStat,
            pillarStat.CurCritRate,
            pillarStat.CurCritDamage
        );
        bullet.EasyModeChange(BulletMoveMode.Angle, angle);
    }

    // ── Escalation ────────────────────────────────────────────────
    private void OnAnyPillarDead(Component sender, object data)
    {
        // Bỏ qua event của chính mình
        if (sender is Stat s && s == pillarStat) return;

        deadCount++;

        switch (deadCount)
        {
            case 1: // 4 → 3: thêm bullet
                curBulletCount = baseBulletCount + esc1BulletAdd;
                break;

            case 2: // 3 → 2: burst + isPhase2
                amt?.SetBool("isPhase1", false);
                amt?.SetBool("isPhase2", true);
                break;

            case 3: // 2 → 1: tăng tất cả + isPhase3
                curBulletCount += esc3BulletAdd;
                curFireRate     = esc3FireRate;
                amt?.SetBool("isPhase2", false);
                amt?.SetBool("isPhase3", true);
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void ResetState()
    {
        deadCount      = 0;
        curBulletCount = baseBulletCount;
        curFireRate    = baseFireRate;
        fireTimer      = curFireRate;
        waitingBurst   = false;
        burstTimer     = 0f;
        amt?.SetBool("isPhase1", false);
        amt?.SetBool("isPhase2", false);
        amt?.SetBool("isPhase3", false);
    }

    private List<float> GetDamageList(string id)
    {
        if (skillData.TryGetValue(id, out var data) && data.damage?.Count > 0)
            return data.damage;
        return new List<float> { 100f };
    }

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.4f);
    }
}