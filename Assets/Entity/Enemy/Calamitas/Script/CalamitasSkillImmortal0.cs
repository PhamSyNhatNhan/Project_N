using System;
using System.Collections.Generic;
using UnityEngine;

public class CalamitasSkillImmortal0 : MonoBehaviour
{
    // ── Prefabs ───────────────────────────────────────────────────
    [Header("Prefabs")]
    [SerializeField] private GameObject pillarPrefab;

    // ── Grid ──────────────────────────────────────────────────────
    [Header("Grid")]
    [SerializeField] private int   gridCount   = 13;
    [SerializeField] private float gridOffsetX = 6f;
    [SerializeField] private float gridOffsetY = 4f;

    // ── Stage 1 ───────────────────────────────────────────────────
    [Header("Stage 1 — Hellblast2 Rain")]
    [SerializeField] private float stage1Duration   = 10f;
    [SerializeField] private float spawnInterval    = 0.2f;
    [SerializeField] private int   spawnPerInterval = 5;

    // ── Stage 2 — Pillars ─────────────────────────────────────────
    [Header("Stage 2 — Pillars")]
    [SerializeField] private float pillarOffsetX = 5f;
    [SerializeField] private float pillarOffsetY = 3f;

    // ── Stage 2 — Hellfire ────────────────────────────────────────
    [Header("Stage 2 — Hellfire")]
    [SerializeField] private float hellfireInterval   = 1.5f;
    [SerializeField] private int   hellfireExtraPairs = 1;
    [SerializeField] private float hellfireAngleStep  = 15f;

    // ── Callback cho CalamitasSkill ───────────────────────────────
    public Action OnComplete;

    // ── Components ────────────────────────────────────────────────
    private Stat      bossStat;
    private Move      bossMove;
    private TimeScale timeScale;
    private Transform playerTransform;

    // ── Damage — nhận từ CalamitasSkill qua LoadData ──────────────
    private List<float> dmgHellblast = new List<float> { 200f };
    private List<float> dmgHellfire  = new List<float> { 250f };

    // ── Pools — nhận từ CalamitasSkill qua LoadData ───────────────
    private EasyPoolingList hellblast2Pool;
    private EasyPoolingList hellFirePool;
    private EasyPoolingList ringBulletPool;

    // ── FSM ───────────────────────────────────────────────────────
    private enum Stage { Stage1, Stage2, Cleanup }
    private Stage curStage;

    private float stageTimer;
    private float spawnTimer;
    private int   pillarDeadCount;
    private float hellfireTimer;
    private float cleanupTimer;

    [Header("Cleanup")]
    [SerializeField] private float cleanupDelay = 1.5f;

    private readonly List<GameObject> activePillars = new List<GameObject>();

    // ── Spawn points ──────────────────────────────────────────────
    private struct SpawnPoint
    {
        public Vector2 position;
        public Vector2 direction;
    }
    private readonly List<SpawnPoint> spawnPoints = new List<SpawnPoint>();

    private float DeltaTime => timeScale.DeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        bossStat  = GetComponent<Stat>();
        bossMove  = GetComponent<Move>();
        timeScale = GetComponent<TimeScale>();
    }

    private void Start()
    {
    }

    private void OnEnable()
    {
        pillarDeadCount   = 0;
        bossMove.CanMove  = false;
        bossStat.CanDamge = false;

        FindPlayer();
        BuildSpawnPoints();
        EnterStage1();

        EventManager.Entity.OnEntityDead
            .Get("CalamitasPillar")
            .AddListener(OnPillarDead);
    }

    private void OnDisable()
    {
        bossMove.CanMove = true;

        EventManager.Entity.OnEntityDead
            .Get("CalamitasPillar")
            .RemoveListener(OnPillarDead);
        hellblast2Pool.ClearPool();
    }

    private void Update()
    {
        switch (curStage)
        {
            case Stage.Stage1:  UpdateStage1();  break;
            case Stage.Stage2:  UpdateStage2();  break;
            case Stage.Cleanup: UpdateCleanup(); break;
        }
    }

    // ── Setup ─────────────────────────────────────────────────────
    public void LoadData(EasyPoolingList hellblast2, EasyPoolingList hellFire,
                         EasyPoolingList ringBullet,
                         List<float> hellblastDmg, List<float> hellFireDmg)
    {
        hellblast2Pool = hellblast2;
        hellFirePool   = hellFire;
        ringBulletPool = ringBullet;
        if (hellblastDmg != null) dmgHellblast = hellblastDmg;
        if (hellFireDmg  != null) dmgHellfire  = hellFireDmg;
    }

    // ══════════════════════════════════════════════════════════════
    // STAGE 1 — Hellblast2 Rain
    // ══════════════════════════════════════════════════════════════
    private void EnterStage1()
    {
        curStage   = Stage.Stage1;
        stageTimer = stage1Duration;
        spawnTimer = 0f;
    }

    private void UpdateStage1()
    {
        stageTimer -= DeltaTime;
        spawnTimer -= DeltaTime;

        if (spawnTimer <= 0f)
        {
            spawnTimer = spawnInterval;
            SpawnHellblast2Batch();
        }

        if (stageTimer <= 0f)
            EnterStage2();
    }

    private void SpawnHellblast2Batch()
    {
        for (int i = 0; i < spawnPerInterval; i++)
        {
            SpawnPoint sp  = spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Count)];
            GameObject obj = hellblast2Pool.GetGameObject();
            if (obj == null) continue;

            obj.transform.position = (Vector2)transform.position + sp.position;

            // SetForwardDir trước SetActive
            var h2 = obj.GetComponent<CalamitasHellblast2>();
            if (h2 != null)
            {
                h2.SetForwardDir(sp.direction);
                h2.SetUp(DamageType.Magic, dmgHellblast,
                         bossStat, bossStat.CurCritRate, bossStat.CurCritDamage);
            }

            obj.SetActive(true);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // STAGE 2 — Pillars + Hellfire
    // ══════════════════════════════════════════════════════════════
    private void EnterStage2()
    {
        curStage      = Stage.Stage2;
        hellfireTimer = hellfireInterval;
        SpawnPillars();
    }

    private void UpdateStage2()
    {
        hellfireTimer -= DeltaTime;
        if (hellfireTimer > 0f) return;

        hellfireTimer = hellfireInterval;
        FireHellfire();
    }

    private void SpawnPillars()
    {
        Vector2[] offsets =
        {
            new Vector2(-pillarOffsetX,  pillarOffsetY),
            new Vector2( pillarOffsetX,  pillarOffsetY),
            new Vector2(-pillarOffsetX, -pillarOffsetY),
            new Vector2( pillarOffsetX, -pillarOffsetY),
        };

        activePillars.Clear();
        foreach (var offset in offsets)
        {
            GameObject obj = Instantiate(pillarPrefab);
            obj.transform.position = (Vector2)transform.position + offset;
            obj.GetComponent<CalamitasPillarSkill>()?.SetBulletPool(ringBulletPool);
            activePillars.Add(obj);
            obj.SetActive(true);
        }
    }

    private void FireHellfire()
    {
        if (playerTransform == null) { FindPlayer(); return; }

        Vector2 toPlayer  = ((Vector2)playerTransform.position - (Vector2)transform.position).normalized;
        float   baseAngle = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg;

        SpawnHellfire(baseAngle);
        for (int i = 1; i <= hellfireExtraPairs; i++)
        {
            SpawnHellfire(baseAngle + hellfireAngleStep * i);
            SpawnHellfire(baseAngle - hellfireAngleStep * i);
        }
    }

    private void SpawnHellfire(float angle)
    {
        GameObject obj = hellFirePool.GetGameObject();
        if (obj == null) return;

        obj.transform.position = transform.position;
        obj.SetActive(true);

        var bullet = obj.GetComponent<BulletObject>();
        if (bullet == null) return;

        bullet.SetUp(DamageType.Magic, dmgHellfire,
                     bossStat, bossStat.CurCritRate, bossStat.CurCritDamage);
        bullet.EasyModeChange(BulletMoveMode.Angle, angle);
    }

    // ── Pillar dead ───────────────────────────────────────────────
    private void OnPillarDead(Component sender, object data)
    {
        pillarDeadCount++;
        if (pillarDeadCount < 4) return;

        // Dừng bắn, chờ cleanup
        curStage      = Stage.Cleanup;
        cleanupTimer  = cleanupDelay;
    }

    private void UpdateCleanup()
    {
        cleanupTimer -= DeltaTime;
        if (cleanupTimer > 0f) return;

        foreach (var p in activePillars)
            if (p != null) Destroy(p);
        activePillars.Clear();

        enabled = false;
        OnComplete?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void BuildSpawnPoints()
    {
        spawnPoints.Clear();
        float stepX = gridCount > 1 ? (gridOffsetX * 2f) / (gridCount - 1) : 0f;
        float stepY = gridCount > 1 ? (gridOffsetY * 2f) / (gridCount - 1) : 0f;

        for (int i = 0; i < gridCount; i++)
        {
            float t = i * stepX - gridOffsetX;
            float u = i * stepY - gridOffsetY;

            spawnPoints.Add(new SpawnPoint { position = new Vector2(t,  gridOffsetY), direction = Vector2.down  });
            spawnPoints.Add(new SpawnPoint { position = new Vector2(t, -gridOffsetY), direction = Vector2.up    });
            spawnPoints.Add(new SpawnPoint { position = new Vector2(-gridOffsetX, u), direction = Vector2.right });
            spawnPoints.Add(new SpawnPoint { position = new Vector2( gridOffsetX, u), direction = Vector2.left  });
        }
    }

    private void FindPlayer()
    {
        var go = GameObject.FindWithTag("Player");
        if (go != null) playerTransform = go.transform;
    }

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        float stepX = gridCount > 1 ? (gridOffsetX * 2f) / (gridCount - 1) : 0f;
        float stepY = gridCount > 1 ? (gridOffsetY * 2f) / (gridCount - 1) : 0f;

        // Grid outline
        Gizmos.color = new Color(0.9f, 0.2f, 0.2f, 0.4f);
        Vector3 tl = transform.position + new Vector3(-gridOffsetX,  gridOffsetY, 0f);
        Vector3 tr = transform.position + new Vector3( gridOffsetX,  gridOffsetY, 0f);
        Vector3 bl = transform.position + new Vector3(-gridOffsetX, -gridOffsetY, 0f);
        Vector3 br = transform.position + new Vector3( gridOffsetX, -gridOffsetY, 0f);
        Gizmos.DrawLine(tl, tr); Gizmos.DrawLine(tr, br);
        Gizmos.DrawLine(br, bl); Gizmos.DrawLine(bl, tl);

        // Grid points
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
        for (int i = 0; i < gridCount; i++)
        {
            float t = i * stepX - gridOffsetX;
            float u = i * stepY - gridOffsetY;
            Gizmos.DrawSphere(transform.position + new Vector3(t,  gridOffsetY, 0f), 0.08f);
            Gizmos.DrawSphere(transform.position + new Vector3(t, -gridOffsetY, 0f), 0.08f);
            Gizmos.DrawSphere(transform.position + new Vector3(-gridOffsetX, u, 0f), 0.08f);
            Gizmos.DrawSphere(transform.position + new Vector3( gridOffsetX, u, 0f), 0.08f);
        }

        // Pillar positions
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.9f);
        Vector2[] po = {
            new Vector2(-pillarOffsetX,  pillarOffsetY), new Vector2( pillarOffsetX,  pillarOffsetY),
            new Vector2(-pillarOffsetX, -pillarOffsetY), new Vector2( pillarOffsetX, -pillarOffsetY),
        };
        foreach (var o in po)
        {
            Vector3 pos = transform.position + new Vector3(o.x, o.y, 0f);
            Gizmos.DrawWireSphere(pos, 0.4f);
            Gizmos.DrawLine(transform.position, pos);
        }
    }
}