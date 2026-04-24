using System.Collections.Generic;
using UnityEngine;

public class RenegadeWarlockSkill : EnemySkill
{
    // ── Phase ─────────────────────────────────────────────────────
    private enum Phase
    {
        Idle, Move,
        BulletFire, BulletWait,
        MeteorFire, MeteorWait
    }
    private Phase curPhase = Phase.Idle;

    // ── Config ────────────────────────────────────────────────────
    [Header("Config")]
    [SerializeField] private float attackRange        = 8f;
    [SerializeField] private float idleDuration       = 2.5f;
    [SerializeField] private float moveMaxDuration    = 2.5f;
    [SerializeField] private float invincibleDuration = 2f;

    [Header("Bullet")]
    [SerializeField] private GameObject infernoBulletPrefab;
    [SerializeField] private int        bulletWaveCount    = 6;
    [SerializeField] private float      bulletWaveInterval = 0.5f;
    [SerializeField] private float      bulletAngleOffset  = 30f;

    [Header("Meteor")]
    [SerializeField] private GameObject[] meteorPrefabs       = new GameObject[5];
    [SerializeField] private float        meteorInterval      = 0.5f;
    [SerializeField] private float        meteorDeathInterval = 0.1f;
    [SerializeField] private float        meteorSpawnHeight   = 6f;
    [SerializeField] private float        meteorSpawnOffsetX  = 3f;

    // ── Runtime ───────────────────────────────────────────────────
    private float     phaseTimer;
    private float     invincibleTimer; // chạy độc lập, không dùng curPhase
    private Transform playerTransform;
    private Stat      enemyStat;
    private Move      enemyMove;
    private Animator  animator;
    private string    entityKey;
    private bool      facingRight = true;

    // Bullet state
    private int        bulletWaveIndex;
    private float      mainBulletAngle;
    private List<float> bulletDmgList;
    private DamageType  bulletDmgType;

    // Meteor state
    private int       meteorIndex;
    private bool      isMeteorDeathSave;
    private float     meteorStep;
    private SkillData meteorSkillData;

    private EasyPoolingList   bulletPool  = new EasyPoolingList();
    private EasyPoolingList[] meteorPools;

    private float DeltaTime => timeScale.DeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        enemyStat = GetComponent<Stat>();
        enemyMove = GetComponent<Move>();
        animator  = GetComponent<Animator>();
        bulletPool.SetPrefab(infernoBulletPrefab);
        meteorPools = new EasyPoolingList[meteorPrefabs.Length];
        for (int i = 0; i < meteorPrefabs.Length; i++)
        {
            meteorPools[i] = new EasyPoolingList();
            meteorPools[i].SetPrefab(meteorPrefabs[i]);
        }
        entityKey = $"{enemyStat.NameCharacter}_{enemyStat.GetInstanceID()}";
    }

    protected override void Start()
    {
        base.Start();
    }

    private void OnEnable()
    {
        if (enemyMove == null || enemyMove.Rb == null) return;
        enemyMove.ResetAll();
        if (enemyStat != null) enemyStat.CanDamge = true;
        ResetState();
        Subscribe();
    }

    private void OnDisable()
    {
        if (enemyStat != null) enemyStat.CanDamge = true;
        if (enemyMove  != null) enemyMove.ResetAll();
        SetMoveAnim(false);
        Unsubscribe();
    }

    // ── Subscribe ─────────────────────────────────────────────────
    private void Subscribe()
    {
        if (string.IsNullOrEmpty(entityKey)) return;
        enemyStat.OnBeforeTakeDamage += OnBeforeTakeDamage;
    }

    private void Unsubscribe()
    {
        if (enemyStat != null)
            enemyStat.OnBeforeTakeDamage -= OnBeforeTakeDamage;
    }

    // ── ILoadable ─────────────────────────────────────────────────
    public override void ApplyData()
    {
        base.ApplyData();

        skillData.TryGetValue("RenegadeWarlockInfernoBullet", out var bData);
        bulletDmgList = bData?.damage?.Count > 0 ? bData.damage : new List<float> { 500f };
        bulletDmgType = bData != null && !string.IsNullOrEmpty(bData.damageType)
            ? System.Enum.Parse<DamageType>(bData.damageType) : DamageType.Magic;

        skillData.TryGetValue("RenegadeWarlockMeteor", out meteorSkillData);
    }

    // ── Reset ─────────────────────────────────────────────────────
    private void ResetState()
    {
        curPhase        = Phase.Idle;
        phaseTimer      = idleDuration;
        invincibleTimer = 0f;
        facingRight     = true;

        if (enemyStat != null) enemyStat.CanDamge = true;

        transform.rotation = Quaternion.identity;
        SetMoveAnim(false);

        if (enemyMove != null && enemyMove.Rb != null)
        {
            enemyMove.ResetAll();
            enemyMove.CanMove = true;
        }
    }

    // ── Update ────────────────────────────────────────────────────
    protected override void Update()
    {
        base.Update();
        FindPlayer();
        FlipToPlayer();
        TickInvincible();

        switch (curPhase)
        {
            case Phase.Idle:       UpdateIdle();       break;
            case Phase.Move:       UpdateMove();       break;
            case Phase.BulletFire: UpdateBulletFire(); break;
            case Phase.BulletWait: UpdateBulletWait(); break;
            case Phase.MeteorFire: UpdateMeteorFire(); break;
            case Phase.MeteorWait: UpdateMeteorWait(); break;
        }
    }

    // ── Invincible timer — độc lập với phase ──────────────────────
    private void TickInvincible()
    {
        if (invincibleTimer <= 0f) return;
        invincibleTimer -= Time.deltaTime;
        if (invincibleTimer <= 0f && enemyStat != null)
            enemyStat.CanDamge = true;
    }

    // ── Phase Handlers ────────────────────────────────────────────
    private void UpdateIdle()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer > 0f) return;
        if (playerTransform == null) return;

        if (DistToPlayer() <= attackRange)
            EnterRandomAttack();
        else
            EnterPhase(Phase.Move);
    }

    private void UpdateMove()
    {
        if (playerTransform == null) { EnterPhase(Phase.Idle); return; }

        phaseTimer -= DeltaTime;

        if (DistToPlayer() <= attackRange)
        {
            enemyMove.ResetAll();
            EnterRandomAttack();
            return;
        }

        if (phaseTimer <= 0f) { EnterPhase(Phase.Idle); return; }

        enemyMove.TargetPosition = playerTransform.position;
        enemyMove.MoveToXY(enemyMove.CurMoveSpeed, 0.1f);
    }

    private void UpdateBulletFire()
    {
        float[] angles = { mainBulletAngle, mainBulletAngle + bulletAngleOffset, mainBulletAngle - bulletAngleOffset };
        foreach (float a in angles)
        {
            GameObject go = bulletPool.GetGameObject();
            if (go == null) continue;
            go.transform.position = transform.position;
            go.transform.rotation = Quaternion.identity;
            go.transform.SetParent(null);
            var bullet = go.GetComponent<BulletObject>();
            if (bullet != null)
            {
                bullet.SetUp(bulletDmgType, bulletDmgList, enemyStat,
                             enemyStat.CurCritRate, enemyStat.CurCritDamage);
                bullet.SetAngle(a);
            }
            go.SetActive(true);
        }

        bulletWaveIndex++;
        if (bulletWaveIndex >= bulletWaveCount)
            EnterPhase(Phase.Idle);
        else
            EnterPhase(Phase.BulletWait);
    }

    private void UpdateBulletWait()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer <= 0f) EnterPhase(Phase.BulletFire);
    }

    private void UpdateMeteorFire()
    {
        int count = meteorPrefabs.Length;
        if (meteorIndex >= count) { EnterPhase(Phase.Idle); return; }

        Vector2 basePos = playerTransform != null
            ? (Vector2)playerTransform.position
            : (Vector2)transform.position;

        Vector2 spawnPos;
        if (isMeteorDeathSave)
        {
            float offsetX = -meteorSpawnOffsetX + meteorStep * meteorIndex;
            spawnPos = basePos + new Vector2(offsetX, meteorSpawnHeight);
        }
        else
        {
            spawnPos = basePos + new Vector2(Random.Range(0f, meteorSpawnOffsetX), meteorSpawnHeight);
        }

        SpawnMeteor(meteorIndex, spawnPos, basePos, meteorSkillData);
        meteorIndex++;

        if (meteorIndex >= count)
            EnterPhase(Phase.Idle);
        else
            EnterPhase(Phase.MeteorWait);
    }

    private void UpdateMeteorWait()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer <= 0f) EnterPhase(Phase.MeteorFire);
    }

    // ── Enter Phase ───────────────────────────────────────────────
    private void EnterPhase(Phase phase)
    {
        curPhase = phase;

        switch (phase)
        {
            case Phase.Idle:
                enemyMove.StopMovement();
                enemyMove.CanMove = true;
                phaseTimer = idleDuration;
                SetMoveAnim(false);
                break;

            case Phase.Move:
                enemyMove.CanMove = true;
                phaseTimer = moveMaxDuration;
                SetMoveAnim(true);
                break;

            case Phase.BulletFire:
                enemyMove.StopMovement();
                enemyMove.CanMove = false;
                SetMoveAnim(false);
                if (bulletWaveIndex == 0)
                {
                    mainBulletAngle = playerTransform != null
                        ? Mathf.Atan2(
                            playerTransform.position.y - transform.position.y,
                            playerTransform.position.x - transform.position.x) * Mathf.Rad2Deg
                        : (facingRight ? 0f : 180f);
                }
                break;

            case Phase.BulletWait:
                phaseTimer = bulletWaveInterval;
                break;

            case Phase.MeteorFire:
                enemyMove.StopMovement();
                enemyMove.CanMove = false;
                SetMoveAnim(false);
                if (meteorIndex == 0 && isMeteorDeathSave)
                {
                    int count  = meteorPrefabs.Length;
                    meteorStep = count > 1 ? (meteorSpawnOffsetX * 2f) / (count - 1) : 0f;
                }
                break;

            case Phase.MeteorWait:
                phaseTimer = isMeteorDeathSave ? meteorDeathInterval : meteorInterval;
                break;
        }
    }

    private void StartBulletSequence()
    {
        bulletWaveIndex = 0;
        EnterPhase(Phase.BulletFire);
    }

    private void StartMeteorSequence(bool deathSave)
    {
        meteorIndex       = 0;
        isMeteorDeathSave = deathSave;
        EnterPhase(Phase.MeteorFire);
    }

    private void EnterRandomAttack()
    {
        if (Random.value < 0.5f) StartBulletSequence();
        else                     StartMeteorSequence(false);
    }

    // ── Death Save ────────────────────────────────────────────────
    private float OnBeforeTakeDamage(float damage, DamageType type)
    {
        if (enemyStat.CurHealth - damage > 0f) return damage;

        float clampedDmg = enemyStat.CurHealth - 1f;
        enemyStat.OnBeforeTakeDamage -= OnBeforeTakeDamage;

        enemyStat.CanDamge  = false;
        invincibleTimer     = invincibleDuration; // timer độc lập
        StartMeteorSequence(true);

        return Mathf.Max(0f, clampedDmg);
    }

    // ── Spawn Meteor ──────────────────────────────────────────────
    private void SpawnMeteor(int index, Vector2 spawnPos, Vector2 targetPos, SkillData data)
    {
        if (index >= meteorPools.Length || meteorPools[index] == null) return;
        GameObject go = meteorPools[index].GetGameObject();
        if (go == null) return;

        go.transform.position = spawnPos;
        go.transform.rotation = Quaternion.identity;
        go.transform.SetParent(null);

        var bullet = go.GetComponent<BulletObject>();
        if (bullet != null)
        {
            float dmgValue = data?.damage != null && index < data.damage.Count
                ? data.damage[index] : 500f;
            var dmgType = data != null && !string.IsNullOrEmpty(data.damageType)
                ? System.Enum.Parse<DamageType>(data.damageType) : DamageType.Magic;

            bullet.SetUp(dmgType, new List<float> { dmgValue }, enemyStat,
                         enemyStat.CurCritRate, enemyStat.CurCritDamage);

            float angle = Mathf.Atan2(targetPos.y - spawnPos.y,
                                      targetPos.x - spawnPos.x) * Mathf.Rad2Deg;
            bullet.EasyModeChange(BulletMoveMode.Angle, angle);
        }

        go.SetActive(true);
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void FindPlayer()
    {
        if (playerTransform != null) return;
        var player = GameObject.FindWithTag("Player");
        if (player != null) playerTransform = player.transform;
    }

    private void FlipToPlayer()
    {
        if (playerTransform == null) return;
        bool playerIsRight = playerTransform.position.x >= transform.position.x;
        if (playerIsRight == facingRight) return;
        facingRight = playerIsRight;
        transform.Rotate(0f, 180f, 0f);
    }

    private float DistToPlayer()
        => playerTransform != null
            ? Vector2.Distance(transform.position, playerTransform.position)
            : float.MaxValue;

    private void SetMoveAnim(bool isMove)
        => animator?.SetBool("isMove", isMove);

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f);
        Gizmos.DrawSphere(transform.position, attackRange);
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}