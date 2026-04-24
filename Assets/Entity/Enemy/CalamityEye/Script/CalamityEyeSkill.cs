using System.Collections.Generic;
using UnityEngine;

public class CalamityEyeSkill : EnemySkill
{
    // ── Phase ─────────────────────────────────────────────────────
    private enum Phase { Idle, Orbit, Dash, Reset }
    private Phase curPhase = Phase.Idle;

    // ── Config ────────────────────────────────────────────────────
    [Header("Config")]
    [SerializeField] private float idleDuration = 1f;

    [Header("Orbit")]
    [SerializeField] private float orbitRadius  = 4f;
    [SerializeField] private float orbitSpeed   = 120f;  // độ/giây
    [SerializeField] private float dashCooldown = 2f;

    [Header("Dash")]
    [SerializeField] private float dashSpeed = 800f;
    [SerializeField] private float dashTime  = 0.2f;

    [Header("Reset")]
    [SerializeField] private float resetSpeed = 500f;

    [Header("Damage")]
    [SerializeField] private LayerMask playerLayer;

    // ── Runtime ───────────────────────────────────────────────────
    private float     phaseTimer;
    private float     orbitAngle;
    private Transform playerTransform;
    private Stat      enemyStat;
    private Move      enemyMove;
    private Hitbox    hitbox;
    private string    entityKey;

    private List<float> dmgList;
    private DamageType  dmgType;
    private readonly HashSet<Stat> hitTargets = new HashSet<Stat>();
    private Vector2     resetTarget;

    private float DeltaTime => timeScale.DeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        enemyStat = GetComponent<Stat>();
        enemyMove = GetComponent<Move>();
        hitbox    = GetComponent<Hitbox>();
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
    }

    // ── ILoadable ─────────────────────────────────────────────────
    public override void ApplyData()
    {
        base.ApplyData();
        skillData.TryGetValue("CalamityEyeDash", out var data);
        dmgList      = data?.damage?.Count > 0 ? data.damage : new List<float> { 400f };
        dmgType      = data != null && !string.IsNullOrEmpty(data.damageType)
            ? System.Enum.Parse<DamageType>(data.damageType)
            : DamageType.Physical;
        if (data != null)
        {
            orbitRadius  = data.Get<float>("orbitRadius",  orbitRadius);
            orbitSpeed   = data.Get<float>("orbitSpeed",   orbitSpeed);
            dashSpeed    = data.Get<float>("dashSpeed",    dashSpeed);
            dashTime     = data.Get<float>("dashTime",     dashTime);
            dashCooldown = data.Get<float>("dashCooldown", dashCooldown);
            resetSpeed   = data.Get<float>("resetSpeed",   resetSpeed);
        }
    }

    // ── Reset ─────────────────────────────────────────────────────
    private void ResetState()
    {
        curPhase   = Phase.Idle;
        phaseTimer = idleDuration;
        orbitAngle = 0f;
        hitTargets.Clear();

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
            case Phase.Idle:  UpdateIdle();  break;
            case Phase.Orbit: UpdateOrbit(); break;
            case Phase.Dash:  UpdateDash();  break;
            case Phase.Reset: UpdateReset(); break;
        }
    }

    // ── Phase Handlers ────────────────────────────────────────────
    private void UpdateIdle()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer > 0f || playerTransform == null) return;

        // Tính góc ban đầu từ vị trí hiện tại so với player
        Vector2 dir = (Vector2)transform.position - (Vector2)playerTransform.position;
        orbitAngle  = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        EnterPhase(Phase.Orbit);
    }

    private void UpdateOrbit()
    {
        if (playerTransform == null) { EnterPhase(Phase.Idle); return; }

        // Tăng góc theo thời gian
        orbitAngle += orbitSpeed * DeltaTime;

        // Tính vị trí mục tiêu trên vòng tròn
        float   rad       = orbitAngle * Mathf.Deg2Rad;
        Vector2 orbitPos  = (Vector2)playerTransform.position
                          + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * orbitRadius;

        // Bay đến vị trí orbit
        enemyMove.TargetPosition = orbitPos;
        enemyMove.MoveToXY(enemyMove.CurMoveSpeed, 0.05f);

        // Đếm cooldown dash
        phaseTimer -= DeltaTime;
        if (phaseTimer <= 0f)
            EnterPhase(Phase.Dash);
    }

    private void UpdateDash()
    {
        phaseTimer -= DeltaTime;

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
                    float dmg = enemyStat.CaculateDamage(dmgType, dmgList.Count > 0 ? dmgList[0] : 400f);
                    hitStat.TakeDamage(dmgType, dmg, enemyStat.CurCritRate, enemyStat.CurCritDamage);
                }
            }
        }

        if (phaseTimer <= 0f)
            EnterPhase(Phase.Reset);
    }

    private void UpdateReset()
    {
        enemyMove.TargetPosition = resetTarget;
        enemyMove.MoveToXY(resetSpeed, 0.1f);

        if (Vector2.Distance(transform.position, resetTarget) <= 0.3f)
            EnterPhase(Phase.Orbit);
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

            case Phase.Orbit:
                enemyMove.CanMove = true;
                phaseTimer = dashCooldown;
                break;

            case Phase.Dash:
                hitTargets.Clear();
                enemyMove.CanMove = true;
                if (playerTransform != null)
                {
                    Vector2 dashDir = ((Vector2)playerTransform.position - (Vector2)transform.position).normalized;
                    enemyMove.MoveSnap = dashDir;
                    enemyMove.MoveTo(dashSpeed, dashTime);
                }
                phaseTimer = dashTime;
                break;

            case Phase.Reset:
                enemyMove.CanMove = true;
                if (playerTransform != null)
                {
                    Vector2 dir    = (Vector2)transform.position - (Vector2)playerTransform.position;
                    float   angle  = Mathf.Atan2(dir.y, dir.x) * Mathf.Deg2Rad;
                    resetTarget    = (Vector2)playerTransform.position
                                   + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * orbitRadius;
                    orbitAngle     = angle * Mathf.Rad2Deg;
                }
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

    private void RotateToPlayer()
    {
        if (playerTransform == null) return;
        Vector2   dir    = (Vector2)playerTransform.position - (Vector2)transform.position;
        float     angle  = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        if (playerTransform == null) return;
        Gizmos.color = new Color(1f, 0f, 0f, 0.15f);
        Gizmos.DrawSphere(playerTransform.position, orbitRadius);
        Gizmos.color = new Color(1f, 0f, 0f, 0.9f);
        Gizmos.DrawWireSphere(playerTransform.position, orbitRadius);
    }
}