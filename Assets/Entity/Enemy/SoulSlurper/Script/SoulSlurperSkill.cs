using System.Collections.Generic;
using UnityEngine;

public class SoulSlurperSkill : EnemySkill
{
    // ── Phase ─────────────────────────────────────────────────────
    private enum Phase
    {
        Idle,
        MoveIn, MoveOut,
        AttackFire, AttackWait
    }
    private Phase curPhase = Phase.Idle;

    // ── Config ────────────────────────────────────────────────────
    [Header("Config")]
    [SerializeField] private float innerRange      = 3f;
    [SerializeField] private float outerRange      = 7f;
    [SerializeField] private float idleDuration    = 2.5f;
    [SerializeField] private float moveMaxDuration = 2.5f;

    [Header("Attack")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private int        bulletCount    = 3;
    [SerializeField] private float      bulletInterval = 0.5f;

    [Header("Rotation")]
    [SerializeField] private float rotateSpeed = 0f;   // 0 = tức thì, > 0 = mượt (độ/giây)

    // ── Runtime ───────────────────────────────────────────────────
    private float     phaseTimer;
    private Transform playerTransform;
    private Stat      enemyStat;
    private Move      enemyMove;

    private List<float> dmgList;
    private DamageType  dmgType;
    private int         bulletsFired;

    private EasyPoolingList bulletPool = new EasyPoolingList();

    private float DeltaTime => timeScale.DeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        enemyStat = GetComponent<Stat>();
        enemyMove = GetComponent<Move>();
        bulletPool.SetPrefab(bulletPrefab);
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
    }

    // ── ILoadable ─────────────────────────────────────────────────
    public override void ApplyData()
    {
        base.ApplyData();
        skillData.TryGetValue("SoulSlurperBullet", out var data);
        dmgList = data?.damage?.Count > 0 ? data.damage : new List<float> { 300f };
        dmgType = data != null && !string.IsNullOrEmpty(data.damageType)
            ? System.Enum.Parse<DamageType>(data.damageType)
            : DamageType.Magic;
    }

    // ── Reset ─────────────────────────────────────────────────────
    private void ResetState()
    {
        curPhase      = Phase.Idle;
        phaseTimer    = idleDuration;
        bulletsFired  = 0;

        transform.rotation = Quaternion.identity;

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
        RotateToPlayer();

        switch (curPhase)
        {
            case Phase.Idle:        UpdateIdle();        break;
            case Phase.MoveIn:      UpdateMoveIn();      break;
            case Phase.MoveOut:     UpdateMoveOut();     break;
            case Phase.AttackFire:  UpdateAttackFire();  break;
            case Phase.AttackWait:  UpdateAttackWait();  break;
        }
    }

    // ── Phase Handlers ────────────────────────────────────────────
    private void UpdateIdle()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer > 0f) return;
        if (playerTransform == null) return;

        float dist = DistToPlayer();
        if (dist < innerRange)
            EnterPhase(Phase.MoveOut);
        else if (dist > outerRange)
            EnterPhase(Phase.MoveIn);
        else
            EnterPhase(Phase.AttackFire);
    }

    private void UpdateMoveIn()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer <= 0f) { EnterPhase(Phase.Idle); return; }

        if (DistToPlayer() <= outerRange)
        {
            enemyMove.ResetAll();
            EnterPhase(Phase.Idle);
            return;
        }

        enemyMove.TargetPosition = playerTransform.position;
        enemyMove.MoveToXY(enemyMove.CurMoveSpeed, 0.1f);
    }

    private void UpdateMoveOut()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer <= 0f) { EnterPhase(Phase.AttackFire); return; }

        if (DistToPlayer() >= innerRange)
        {
            enemyMove.ResetAll();
            EnterPhase(Phase.AttackFire);
            return;
        }

        if (playerTransform != null)
        {
            Vector2 awayDir = ((Vector2)transform.position - (Vector2)playerTransform.position).normalized;
            enemyMove.MoveSnap = awayDir;
            enemyMove.MoveTo(enemyMove.CurMoveSpeed, 0.1f);
        }
    }

    private void UpdateAttackFire()
    {
        FireBullet();
        bulletsFired++;
        if (bulletsFired >= bulletCount)
        {
            bulletsFired = 0;
            EnterPhase(Phase.Idle);
        }
        else
        {
            EnterPhase(Phase.AttackWait);
        }
    }

    private void UpdateAttackWait()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer <= 0f) EnterPhase(Phase.AttackFire);
    }

    // ── Enter Phase ───────────────────────────────────────────────
    private void EnterPhase(Phase phase)
    {
        curPhase = phase;

        switch (phase)
        {
            case Phase.Idle:
                enemyMove.ResetAll();
                enemyMove.CanMove = true;
                phaseTimer = idleDuration;
                break;

            case Phase.MoveIn:
            case Phase.MoveOut:
                enemyMove.CanMove = true;
                phaseTimer = moveMaxDuration;
                break;

            case Phase.AttackFire:
                enemyMove.ResetAll();
                enemyMove.CanMove = false;
                break;

            case Phase.AttackWait:
                phaseTimer = bulletInterval;
                break;
        }
    }

    // ── Fire ──────────────────────────────────────────────────────
    private void FireBullet()
    {
        if (bulletPrefab == null || playerTransform == null) return;

        GameObject go = bulletPool.GetGameObject();
        if (go == null) return;

        go.transform.position = transform.position;
        go.transform.rotation = Quaternion.identity;
        go.transform.SetParent(null);

        var bullet = go.GetComponent<BulletObject>();
        if (bullet != null)
        {
            bullet.SetUp(dmgType, dmgList, enemyStat,
                         enemyStat.CurCritRate, enemyStat.CurCritDamage);

            float angle = Mathf.Atan2(
                playerTransform.position.y - transform.position.y,
                playerTransform.position.x - transform.position.x) * Mathf.Rad2Deg;
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

    private void RotateToPlayer()
    {
        if (playerTransform == null) return;

        Vector2 dir   = (Vector2)playerTransform.position - (Vector2)transform.position;
        float   angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Quaternion target = Quaternion.Euler(0f, 0f, angle);

        transform.rotation = rotateSpeed <= 0f
            ? target
            : Quaternion.RotateTowards(transform.rotation, target, rotateSpeed * DeltaTime);
    }

    private float DistToPlayer()
        => playerTransform != null
            ? Vector2.Distance(transform.position, playerTransform.position)
            : float.MaxValue;

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
        Gizmos.DrawSphere(transform.position, innerRange);
        Gizmos.color = new Color(0f, 1f, 0f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, innerRange);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f);
        Gizmos.DrawSphere(transform.position, outerRange);
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, outerRange);
    }
}