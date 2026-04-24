using System.Collections.Generic;
using UnityEngine;

public class Boss1Skill : EnemySkill
{
    // ── Phase ─────────────────────────────────────────────────────
    private enum Phase { Idle, Attack1, Attack2, Attack3 }

    private static readonly Phase[] Rotation =
        { Phase.Attack1, Phase.Attack2, Phase.Attack3 };
    private int rotIndex = 0;

    // ── Config General ────────────────────────────────────────────
    [Header("General")]
    [SerializeField] private float idleDuration = 1.5f;

    // ── Attack 1 — Drop Rain ──────────────────────────────────────
    [Header("Attack1 - Drop Rain")]
    [SerializeField] private GameObject dropBulletPrefab1;
    [SerializeField] private GameObject dropBulletPrefab2;
    [SerializeField] [Range(0f, 1f)]
    private float    drop2Chance       = 0.3f;
    [SerializeField] private float   spawnInterval     = 0.3f;  // giây giữa mỗi viên
    [SerializeField] private Vector2 spawnZoneCenter   = Vector2.zero; // offset từ boss
    [SerializeField] private Vector2 spawnZoneSize     = new Vector2(12f, 4f); // X và Y

    // ── Attack 2 — Burst ──────────────────────────────────────────
    [Header("Attack2 - Burst")]
    [SerializeField] private GameObject  attack2Prefab;
    [SerializeField] private Transform[] attack2SpawnPoints;

    // ── Attack 3 — Dual Shot ──────────────────────────────────────
    [Header("Attack3 - Dual Shot")]
    [SerializeField] private GameObject attack3Prefab;

    // ── State Flags ───────────────────────────────────────────────
    private bool isAttack1 = false;
    private bool isAttack2 = false;
    private bool isAttack3 = false;

    // ── Runtime ───────────────────────────────────────────────────
    private Phase     curPhase = Phase.Idle;
    private float     phaseTimer;
    private Stat      enemyStat;
    private Transform playerTransform;
    private Animator  animator;

    private List<float> dmg1 = new List<float>();
    private List<float> dmg2 = new List<float>();
    private List<float> dmg3 = new List<float>();
    private DamageType  type1, type2Enum, type3;

    private readonly EasyPoolingList dropPool1 = new EasyPoolingList();
    private readonly EasyPoolingList dropPool2 = new EasyPoolingList();
    private readonly EasyPoolingList atk2Pool  = new EasyPoolingList();
    private readonly EasyPoolingList atk3Pool  = new EasyPoolingList();

    private readonly List<DropBullet> pendingDrops = new List<DropBullet>();
    private bool  isSpawning  = false;
    private float spawnTimer  = 0f;

    private float DeltaTime => timeScale.DeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        enemyStat = GetComponent<Stat>();
        animator  = GetComponent<Animator>();
    }

    protected override void Start()
    {
        base.Start();
        InitPools();
        EnterPhase(Phase.Idle);
    }

    private void OnEnable()
    {
        ResetState();
    }

    private void OnDisable()
    {
        pendingDrops.Clear();
        isAttack1 = isAttack2 = isAttack3 = false;
    }

    // ── ILoadable ─────────────────────────────────────────────────
    public override void ApplyData()
    {
        base.ApplyData();
        LoadSkillData("Boss1Attack1", ref dmg1, ref type1);
        LoadSkillData("Boss1Attack2", ref dmg2, ref type2Enum);
        LoadSkillData("Boss1Attack3", ref dmg3, ref type3);
    }

    private void LoadSkillData(string id, ref List<float> dmgOut, ref DamageType typeOut)
    {
        if (!skillData.TryGetValue(id, out var data)) return;
        dmgOut  = data.damage?.Count > 0 ? data.damage : new List<float> { 300f };
        typeOut = System.Enum.TryParse<DamageType>(data.damageType, out var t)
                  ? t : DamageType.Physical;
    }

    // ── Pools ─────────────────────────────────────────────────────
    private void InitPools()
    {
        if (dropBulletPrefab1)  dropPool1.SetPrefab(dropBulletPrefab1);
        if (dropBulletPrefab2)  dropPool2.SetPrefab(dropBulletPrefab2);
        if (attack2Prefab)      atk2Pool.SetPrefab(attack2Prefab);
        if (attack3Prefab)      atk3Pool.SetPrefab(attack3Prefab);
    }

    // ── Reset ─────────────────────────────────────────────────────
    private void ResetState()
    {
        pendingDrops.Clear();
        isAttack1 = isAttack2 = isAttack3 = false;
        curPhase   = Phase.Idle;
        phaseTimer = idleDuration;
    }

    // ── Update ────────────────────────────────────────────────────
    protected override void Update()
    {
        base.Update();
        FindPlayer();

        if (curPhase == Phase.Idle)   UpdateIdle();
        if (curPhase == Phase.Attack1) UpdateAttack1();
    }

    // ── Idle ──────────────────────────────────────────────────────
    private void UpdateIdle()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer > 0f) return;
        EnterPhase(Rotation[rotIndex]);
    }

    // ── Attack1 Tick Spawn ────────────────────────────────────────
    private void UpdateAttack1()
    {
        if (!isSpawning) return;
        spawnTimer -= DeltaTime;
        if (spawnTimer > 0f) return;
        spawnTimer = spawnInterval;
        SpawnOneDrop();
    }

    // ── Enter Phase ───────────────────────────────────────────────
    private void EnterPhase(Phase phase)
    {
        curPhase = phase;

        switch (phase)
        {
            case Phase.Idle:
                isAttack1 = isAttack2 = isAttack3 = false;
                animator?.SetBool("isAttack1", false);
                animator?.SetBool("isAttack2", false);
                animator?.SetBool("isAttack3", false);
                phaseTimer = idleDuration;
                break;

            case Phase.Attack1:
                isAttack1  = true;
                isSpawning = true;
                spawnTimer = 0f;          // spawn ngay lập tức viên đầu
                pendingDrops.Clear();
                animator?.SetBool("isAttack1", true);
                break;

            case Phase.Attack2:
                isAttack2 = true;
                animator?.SetBool("isAttack2", true);
                break;

            case Phase.Attack3:
                isAttack3 = true;
                animator?.SetBool("isAttack3", true);
                break;
        }
    }

    // ── Animation Events ─────────────────────────────────────────
    /// <summary>Animation Attack1 gọi → dừng spawn, Activate toàn bộ drop đang chờ</summary>
    public void OnAttack1AnimEvent()
    {
        isSpawning = false;
        foreach (var drop in pendingDrops)
            if (drop != null) drop.Activate();
        pendingDrops.Clear();
    }

    /// <summary>Animation Attack2 gọi → Spawn projectile tại từng SpawnPoint</summary>
    public void OnAttack2AnimEvent()
    {
        if (attack2SpawnPoints == null || attack2SpawnPoints.Length == 0) return;

        foreach (var point in attack2SpawnPoints)
        {
            if (point == null) continue;

            GameObject obj = atk2Pool.GetGameObject();
            if (obj == null) continue;

            obj.transform.position = point.position;
            obj.transform.rotation = point.rotation;

            var proj = obj.GetComponent<ProjectileObject>();
            if (proj != null)
                proj.SetUp(type2Enum, dmg2, enemyStat,
                           enemyStat != null ? enemyStat.CurCritRate   : 5f,
                           enemyStat != null ? enemyStat.CurCritDamage : 50f);

            obj.SetActive(true);
        }
    }

    /// <summary>Animation Attack3 gọi → Spawn 2 viên đối xứng qua trục Y theo hướng player</summary>
    public void OnAttack3AnimEvent()
    {
        if (playerTransform == null) return;

        Vector2 toPlayer = ((Vector2)playerTransform.position
                           - (Vector2)transform.position).normalized;
        Vector2 mirrored = new Vector2(-toPlayer.x, toPlayer.y);

        SpawnDualShot(toPlayer);
        SpawnDualShot(mirrored);
    }

    /// <summary>Animation bất kỳ gọi khi kết thúc → về Idle, chuyển attack tiếp theo</summary>
    public void EndAttack()
    {
        rotIndex = (rotIndex + 1) % Rotation.Length;
        EnterPhase(Phase.Idle);
    }

    // ── Spawn Helpers ─────────────────────────────────────────────
    private void SpawnOneDrop()
    {
        Vector2 zoneWorldCenter = (Vector2)transform.position + spawnZoneCenter;

        Vector2 pos = new Vector2(
            zoneWorldCenter.x + Random.Range(-spawnZoneSize.x * 0.5f, spawnZoneSize.x * 0.5f),
            zoneWorldCenter.y + Random.Range(-spawnZoneSize.y * 0.5f, spawnZoneSize.y * 0.5f)
        );

        bool useType2 = Random.value < drop2Chance;
        var  pool     = useType2 ? dropPool2 : dropPool1;
        var  dmgData  = useType2
                        ? (dmg1.Count > 1 ? new List<float> { dmg1[1] } : dmg1)
                        : new List<float> { dmg1.Count > 0 ? dmg1[0] : 400f };

        GameObject obj = pool.GetGameObject();
        if (obj == null) return;

        obj.transform.position = pos;
        obj.transform.rotation = Quaternion.identity;

        var drop = obj.GetComponent<DropBullet>();
        if (drop == null) { obj.SetActive(false); return; }

        drop.SetUp(type1, dmgData, enemyStat,
                   enemyStat != null ? enemyStat.CurCritRate   : 5f,
                   enemyStat != null ? enemyStat.CurCritDamage : 50f);
        obj.SetActive(true);
        pendingDrops.Add(drop);
    }

    private void SpawnDualShot(Vector2 dir)
    {
        GameObject obj = atk3Pool.GetGameObject();
        if (obj == null) return;

        obj.transform.position = transform.position;
        obj.SetActive(true); // OnEnable chạy trước → InitMoveDir

        float angle  = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        var   bullet = obj.GetComponent<BulletObject>();
        if (bullet != null)
        {
            bullet.SetUp(type3, dmg3, enemyStat,
                         enemyStat != null ? enemyStat.CurCritRate   : 5f,
                         enemyStat != null ? enemyStat.CurCritDamage : 50f);
            bullet.EasyModeChange(BulletMoveMode.Angle, angle); // override sau OnEnable
        }
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void FindPlayer()
    {
        if (playerTransform != null) return;
        var go = GameObject.FindWithTag("Player");
        if (go != null) playerTransform = go.transform;
    }

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        Vector3 center = transform.position
                       + new Vector3(spawnZoneCenter.x, spawnZoneCenter.y, 0f);
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f);
        Gizmos.DrawCube(center, new Vector3(spawnZoneSize.x, spawnZoneSize.y, 0f));
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
        Gizmos.DrawWireCube(center, new Vector3(spawnZoneSize.x, spawnZoneSize.y, 0f));
    }
}