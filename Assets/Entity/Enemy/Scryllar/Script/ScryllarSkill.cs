using System.Collections.Generic;
using UnityEngine;

public class ScryllarSkill : EnemySkill
{
    // ── Phase ─────────────────────────────────────────────────────
    private enum Phase
    {
        Idle,
        DashMove, DashRest,
        Slash1Move, Slash1Wait,
        Slash2Move, Slash2Wait,
        Slash3Wait
    }
    private Phase curPhase = Phase.Idle;

    // ── Config ────────────────────────────────────────────────────
    [Header("Config")]
    [SerializeField] private float attackRange  = 3f;
    [SerializeField] private float idleDuration = 2.5f;

    [Header("Dash")]
    [SerializeField] private float dashSpeedMultiplier = 5f;
    [SerializeField] private float dashTime            = 0.25f;
    [SerializeField] private float dashRestTime        = 0.5f;
    [SerializeField] private int   maxDashCount        = 2;

    [Header("Slash")]
    [SerializeField] private GameObject slashPrefab1;
    [SerializeField] private GameObject slashPrefab2;
    [SerializeField] private GameObject slashPrefab3;
    [SerializeField] private float      slash1Time            = 0.3f;
    [SerializeField] private float      slash1DashTime        = 0.2f;
    [SerializeField] private float      slash1SpeedMultiplier = 3f;
    [SerializeField] private float      slash2Time            = 0.3f;
    [SerializeField] private float      slash2DashTime        = 0.2f;
    [SerializeField] private float      slash2SpeedMultiplier = 3f;
    [SerializeField] private float      slash3Time            = 0.3f;
    [SerializeField] private Vector2    slash1Offset          = Vector2.zero;
    [SerializeField] private Vector2    slash2Offset          = Vector2.zero;
    [SerializeField] private Vector2    slash3Offset          = Vector2.zero;

    // ── Runtime ───────────────────────────────────────────────────
    private float      phaseTimer;
    private Transform  playerTransform;
    private Stat       enemyStat;
    private Move       enemyMove;
    private GhostTrail ghostTrail;
    private string     entityKey;
    private bool       facingRight = true;
    private int        dashCount   = 0;

    private List<float>  slashDmgList;
    private DamageType   slashDmgType;

    private EasyPoolingList slash1Pool = new EasyPoolingList();
    private EasyPoolingList slash2Pool = new EasyPoolingList();
    private EasyPoolingList slash3Pool = new EasyPoolingList();

    private float DeltaTime => timeScale.DeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        enemyStat  = GetComponent<Stat>();
        enemyMove  = GetComponent<Move>();
        ghostTrail = GetComponent<GhostTrail>();
        slash1Pool.SetPrefab(slashPrefab1);
        slash2Pool.SetPrefab(slashPrefab2);
        slash3Pool.SetPrefab(slashPrefab3);
        entityKey = $"{enemyStat.NameCharacter}_{enemyStat.GetInstanceID()}";
    }

    protected override void Start()
    {
        base.Start();
        ResetState();
    }

    private void OnEnable()
    {
        if (enemyMove == null || enemyMove.Rb == null) return;
        enemyMove.ResetAll();
        ResetState();
    }

    private void OnDisable()
    {
        if (enemyStat != null) enemyStat.CanDamge = true;
        if (enemyMove  != null) enemyMove.ResetAll();
        ghostTrail?.StopTrail();
    }

    // ── ILoadable ─────────────────────────────────────────────────
    public override void ApplyData()
    {
        base.ApplyData();
        skillData.TryGetValue("ScryllarSlash", out var data);
        slashDmgList = data?.damage != null && data.damage.Count >= 3
            ? data.damage
            : new List<float> { 300f, 300f, 550f };
        slashDmgType = data != null && !string.IsNullOrEmpty(data.damageType)
            ? System.Enum.Parse<DamageType>(data.damageType)
            : DamageType.Physical;
    }

    // ── Reset ─────────────────────────────────────────────────────
    private void ResetState()
    {
        curPhase    = Phase.Idle;
        phaseTimer  = idleDuration;
        dashCount   = 0;
        facingRight = true;

        transform.rotation = Quaternion.identity;
        ghostTrail?.StopTrail();

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

        switch (curPhase)
        {
            case Phase.Idle:       UpdateIdle();       break;
            case Phase.DashMove:   UpdateDashMove();   break;
            case Phase.DashRest:   UpdateDashRest();   break;
            case Phase.Slash1Move: UpdateSlash1Move(); break;
            case Phase.Slash1Wait: UpdateSlash1Wait(); break;
            case Phase.Slash2Move: UpdateSlash2Move(); break;
            case Phase.Slash2Wait: UpdateSlash2Wait(); break;
            case Phase.Slash3Wait: UpdateSlash3Wait(); break;
        }
    }

    // ── Phase Handlers ────────────────────────────────────────────
    private void UpdateIdle()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer > 0f) return;
        if (playerTransform == null) return;

        if (DistToPlayer() <= attackRange)
            EnterPhase(Phase.Slash1Move);
        else
        {
            dashCount = 0;
            EnterPhase(Phase.DashMove);
        }
    }

    private void UpdateDashMove()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer > 0f) return;

        ghostTrail?.StopTrail();
        enemyMove.CanMove = false;
        enemyMove.ResetAll();
        EnterPhase(Phase.DashRest);
    }

    private void UpdateDashRest()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer > 0f) return;

        dashCount++;
        if (DistToPlayer() <= attackRange)
            EnterPhase(Phase.Slash1Move);
        else if (dashCount < maxDashCount)
            EnterPhase(Phase.DashMove);
        else
            EnterPhase(Phase.Idle);
    }

    private void UpdateSlash1Move()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer > 0f) return;

        enemyMove.CanMove = false;
        enemyMove.ResetAll();
        EnterPhase(Phase.Slash1Wait);
    }

    private void UpdateSlash1Wait()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer > 0f) return;
        EnterPhase(Phase.Slash2Move);
    }

    private void UpdateSlash2Move()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer > 0f) return;

        enemyMove.CanMove = false;
        enemyMove.ResetAll();
        EnterPhase(Phase.Slash2Wait);
    }

    private void UpdateSlash2Wait()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer > 0f) return;
        EnterPhase(Phase.Slash3Wait);
    }

    private void UpdateSlash3Wait()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer > 0f) return;
        EnterPhase(Phase.Idle);
    }

    // ── Enter Phase ───────────────────────────────────────────────
    private void EnterPhase(Phase phase)
    {
        curPhase = phase;

        switch (phase)
        {
            case Phase.Idle:
                dashCount = 0;
                enemyMove.ResetAll();
                enemyMove.CanMove = true;
                phaseTimer = idleDuration;
                ghostTrail?.StopTrail();
                break;

            case Phase.DashMove:
                if (playerTransform == null) { EnterPhase(Phase.Idle); return; }
                FlipToPlayer();
                enemyMove.CanMove = true;
                ghostTrail?.StartTrail();
                Vector2 dashDir = ((Vector2)playerTransform.position - (Vector2)transform.position).normalized;
                enemyMove.MoveSnap = dashDir;
                enemyMove.MoveTo(enemyMove.CurMoveSpeed * dashSpeedMultiplier, dashTime);
                phaseTimer = dashTime;
                break;

            case Phase.DashRest:
                phaseTimer = dashRestTime;
                break;

            case Phase.Slash1Move:
                dashCount = 0;
                ghostTrail?.StopTrail();
                SpawnSlash(slash1Pool, slashDmgList[0], slashDmgType, slash1Offset);
                MoveToPlayer(slash1SpeedMultiplier, slash1DashTime);
                phaseTimer = slash1DashTime;
                break;

            case Phase.Slash1Wait:
                phaseTimer = slash1Time;
                break;

            case Phase.Slash2Move:
                SpawnSlash(slash2Pool, slashDmgList[1], slashDmgType, slash2Offset);
                MoveToPlayer(slash2SpeedMultiplier, slash2DashTime);
                phaseTimer = slash2DashTime;
                break;

            case Phase.Slash2Wait:
                phaseTimer = slash2Time;
                break;

            case Phase.Slash3Wait:
                SpawnSlash(slash3Pool, slashDmgList[2], slashDmgType, slash3Offset);
                phaseTimer = slash3Time;
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void SpawnSlash(EasyPoolingList pool, float damage, DamageType dmgType, Vector2 offset)
    {
        GameObject go = pool.GetGameObject();
        if (go == null) return;

        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
        go.transform.localRotation = Quaternion.identity;

        var projectile = go.GetComponent<ProjectileObject>();
        if (projectile != null)
            projectile.SetUp(dmgType, new List<float> { damage }, enemyStat,
                             enemyStat.CurCritRate, enemyStat.CurCritDamage);

        go.SetActive(true);
    }

    private void MoveToPlayer(float speedMul, float time)
    {
        if (playerTransform == null) return;
        FlipToPlayer();
        Vector2 dir = ((Vector2)playerTransform.position - (Vector2)transform.position).normalized;
        enemyMove.CanMove  = true;
        enemyMove.MoveSnap = dir;
        enemyMove.MoveTo(enemyMove.CurMoveSpeed * speedMul, time);
    }

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

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0f, 0f, 0.15f);
        Gizmos.DrawSphere(transform.position, attackRange);
        Gizmos.color = new Color(1f, 0f, 0f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}