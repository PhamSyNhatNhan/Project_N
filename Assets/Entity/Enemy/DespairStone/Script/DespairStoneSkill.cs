using System.Collections.Generic;
using UnityEngine;

public class DespairStoneSkill : EnemySkill
{
    // ── Phase ─────────────────────────────────────────────────────
    private enum Phase { Idle, Chase, ChaseIdle, Spin, Dash, Rest }
    private Phase curPhase = Phase.Idle;

    // ── Config ────────────────────────────────────────────────────
    private float chaseRange        = 8f;
    private float dashRange         = 2.5f;
    private float spinDuration      = 1.5f;
    private float dashSpeed         = 500f;
    private float dashTime          = 0.3f;
    private float idleDuration      = 3.5f;
    private float chaseMaxDuration  = 2.5f;
    private float chaseIdleDuration = 1.5f;
    private List<float> dashDamage  = new List<float> { 300f };

    // ── Runtime ───────────────────────────────────────────────────
    private float     phaseTimer;
    private float     chaseTimer;
    private Transform playerTransform;
    private Stat      enemyStat;
    private Move      enemyMove;
    private string    entityKey;

    [Header("Setup")]
    [SerializeField] private LayerMask playerLayer;

    private Hitbox     hitbox;
    private GhostTrail ghostTrail;
    private IdleEffect idleEffect;
    private readonly HashSet<Stat> hitTargets = new HashSet<Stat>();

    private int   dashCount    = 0;
    private int   maxDashCount = 1;
    private float dashTimer    = 0f;

    private float DeltaTime => timeScale.DeltaTime;

    // ── Components ────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        enemyStat  = GetComponent<Stat>();
        enemyMove  = GetComponent<Move>();
        hitbox     = GetComponent<Hitbox>();
        ghostTrail = GetComponent<GhostTrail>();
        idleEffect = GetComponent<IdleEffect>();
        entityKey  = $"{enemyStat.NameCharacter}_{enemyStat.GetInstanceID()}";
    }

    protected override void Start()
    {
        base.Start();
        ResetState();
    }

    private void OnEnable()
    {
        if (enemyMove == null || enemyMove.Rb == null) return;
        ResetState();
    }

    private void OnDisable()
    {
        if (enemyStat != null) enemyStat.CanDamge = true;
        if (enemyMove  != null) enemyMove.ResetAll();
        ghostTrail?.StopTrail();
        idleEffect?.SetActive(false);
    }

    // ── Reset ─────────────────────────────────────────────────────
    private void ResetState()
    {
        curPhase     = Phase.Idle;
        phaseTimer   = 1.5f;
        chaseTimer   = 0f;
        dashCount    = 0;
        maxDashCount = 1;
        hitTargets.Clear();

        ghostTrail?.StopTrail();
        idleEffect?.SetActive(true);
        transform.rotation = Quaternion.identity;

        if (enemyMove != null && enemyMove.Rb != null)
        {
            enemyMove.ResetAll();
            enemyMove.CanMove = true;
        }
    }

    // ── ILoadable ─────────────────────────────────────────────────
    public override void ApplyData()
    {
        base.ApplyData();

        if (skillData.TryGetValue("DespairDash", out var data))
        {
            chaseRange        = data.Get<float>("chaseRange",        8f);
            dashRange         = data.Get<float>("dashRange",         2.5f);
            spinDuration      = data.Get<float>("spinDuration",      1.5f);
            dashSpeed         = data.Get<float>("dashSpeed",         500f);
            dashTime          = data.Get<float>("dashTime",          0.3f);
            idleDuration      = data.Get<float>("idleDuration",      3.5f);
            chaseMaxDuration  = data.Get<float>("chaseMaxDuration",  2.5f);
            chaseIdleDuration = data.Get<float>("chaseIdleDuration", 1.5f);
            if (data.damage != null && data.damage.Count > 0)
                dashDamage = data.damage;
        }
    }

    // ── Update ────────────────────────────────────────────────────
    protected override void Update()
    {
        base.Update();
        FindPlayer();

        switch (curPhase)
        {
            case Phase.Idle:      UpdateIdle();      break;
            case Phase.Chase:     UpdateChase();     break;
            case Phase.ChaseIdle: UpdateChaseIdle(); break;
            case Phase.Spin:      UpdateSpin();      break;
            case Phase.Dash:      UpdateDash();      break;
            case Phase.Rest:      UpdateRest();      break;
        }
    }

    // ── Phase Handlers ────────────────────────────────────────────
    private void UpdateIdle()
    {
        if (phaseTimer > 0f) { phaseTimer -= DeltaTime; return; }
        if (playerTransform == null) return;
        if (DistToPlayer() <= chaseRange)
            EnterPhase(Phase.Chase);
    }

    private void UpdateChase()
    {
        if (playerTransform == null) { EnterPhase(Phase.Idle); return; }

        float dist = DistToPlayer();
        if (dist > chaseRange) { EnterPhase(Phase.Idle); return; }
        if (dist <= dashRange) { EnterPhase(Phase.Spin);  return; }

        chaseTimer -= DeltaTime;
        if (chaseTimer <= 0f) { EnterPhase(Phase.ChaseIdle); return; }

        enemyMove.TargetPosition = playerTransform.position;
        enemyMove.MoveToXY(enemyStat.CurAttackSpeed, 0.1f);
    }

    private void UpdateSpin()
    {
        phaseTimer -= DeltaTime;
        transform.Rotate(0f, 0f, 720f * DeltaTime);
        if (phaseTimer <= 0f) EnterPhase(Phase.Dash);
    }

    private void UpdateChaseIdle()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer <= 0f) EnterPhase(Phase.Chase);
    }

    private void UpdateRest()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer <= 0f) EnterPhase(Phase.Idle);
    }

    private void UpdateDash()
    {
        // Detect hit
        if (hitbox != null)
        {
            var hits = hitbox.detectObject(playerLayer);
            if (hits != null)
            {
                foreach (var hit in hits)
                {
                    var hitStat = hit.GetComponent<Stat>();
                    if (hitStat == null || hitStat == enemyStat) continue;
                    if (hitTargets.Contains(hitStat)) continue;
                    hitTargets.Add(hitStat);
                    float dmg = dashDamage.Count > 0 ? dashDamage[0] : 300f;
                    hitStat.TakeDamage(DamageType.Magic, dmg,
                        enemyStat.CurCritRate, enemyStat.CurCritDamage);
                }
            }
        }

        // Kết thúc dash bằng timer
        dashTimer -= DeltaTime;
        if (dashTimer > 0f) return;

        enemyMove.ResetAll();
        hitTargets.Clear();
        dashCount++;

        if (dashCount < maxDashCount && playerTransform != null)
            EnterPhase(Phase.Dash);
        else
        {
            dashCount = 0;
            EnterPhase(Phase.Rest);
        }
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
                ghostTrail?.StopTrail();
                idleEffect?.SetActive(true);
                break;

            case Phase.Chase:
                enemyMove.CanMove = true;
                chaseTimer        = chaseMaxDuration;
                ghostTrail?.StopTrail();
                idleEffect?.SetActive(false);
                break;

            case Phase.ChaseIdle:
                enemyMove.StopMovement();
                enemyMove.CanMove = false;
                phaseTimer        = chaseIdleDuration;
                ghostTrail?.StopTrail();
                idleEffect?.SetActive(true);
                break;

            case Phase.Spin:
                enemyMove.StopMovement();
                enemyMove.CanMove = false;
                phaseTimer        = spinDuration;
                ghostTrail?.StopTrail();
                idleEffect?.SetActive(false);
                break;

            case Phase.Dash:
                if (playerTransform == null) { EnterPhase(Phase.Rest); return; }
                enemyMove.CanMove = true;
                ghostTrail?.StartTrail();
                idleEffect?.SetActive(false);

                if (dashCount == 0)
                    maxDashCount = Random.Range(0f, 1f) < 0.5f ? 2 : 1;

                float curDashTime = dashCount == 1 ? dashTime * 1.5f : dashTime;
                dashTimer         = curDashTime;
                Vector2 dir       = (playerTransform.position - transform.position).normalized;
                enemyMove.TargetPosition = (Vector2)transform.position + dir * (dashSpeed * curDashTime);
                enemyMove.MoveToXY(dashSpeed, curDashTime);
                break;

            case Phase.Rest:
                enemyMove.StopMovement();
                enemyMove.CanMove  = false;
                phaseTimer         = idleDuration;
                transform.rotation = Quaternion.identity;
                ghostTrail?.StopTrail();
                idleEffect?.SetActive(true);
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void FindPlayer()
    {
        if (playerTransform != null) return;
        var player = GameObject.FindWithTag("Player");
        if (player != null) playerTransform = player.transform;
    }

    private float DistToPlayer()
        => playerTransform != null
            ? Vector2.Distance(transform.position, playerTransform.position)
            : float.MaxValue;

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
        Gizmos.DrawSphere(transform.position, chaseRange);
        Gizmos.color = new Color(1f, 1f, 0f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, chaseRange);

        Gizmos.color = new Color(1f, 0f, 0f, 0.15f);
        Gizmos.DrawSphere(transform.position, dashRange);
        Gizmos.color = new Color(1f, 0f, 0f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, dashRange);
    }
}