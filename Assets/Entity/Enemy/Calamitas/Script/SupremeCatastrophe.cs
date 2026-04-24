using System;
using System.Collections.Generic;
using UnityEngine;

public class SupremeCatastrophe : EnemySkill
{
    // ── Callbacks (Boss quản lý) ───────────────────────────────────
    public Action OnTeleSkillComplete;
    public Action OnChainAttackComplete;

    // ── Bullet Prefabs ────────────────────────────────────────────
    [Header("Bullet Prefabs")]
    [SerializeField] private GameObject slash1Prefab;
    [SerializeField] private GameObject slash2Prefab;
    [SerializeField] private GameObject bombPrefab;

    // ── Orbit ─────────────────────────────────────────────────────
    [Header("Orbit")]
    [SerializeField] private float orbitRadius    = 5f;
    [SerializeField] private float orbitMoveSpeed = 5f;
    [SerializeField] private float orbitSnapDist  = 0.05f;

    // ── Tele Skill ────────────────────────────────────────────────
    [Header("Tele Skill")]
    [SerializeField] private float teleOffsetDistance = 2.5f;    // Bán kính tele quanh player
    [SerializeField] private float teleDelay          = 0.2f;
    [SerializeField] private int   teleRepeatCount    = 3;
    [SerializeField] private float teleReturnDelay    = 0.5f;

    // ── Components ────────────────────────────────────────────────
    private Stat      minionStat;
    private Move      minionMove;
    private Animator  animator;

    // ── Player ────────────────────────────────────────────────────
    private Transform playerTransform;

    // ── Pools ─────────────────────────────────────────────────────
    private readonly EasyPoolingList slash1Pool = new EasyPoolingList();
    private readonly EasyPoolingList slash2Pool = new EasyPoolingList();
    private readonly EasyPoolingList bombPool   = new EasyPoolingList();
    private EasyPoolingList          arrowPool;

    // ── Damage ────────────────────────────────────────────────────
    private List<float> dmgSlash1 = new List<float> { 280f };
    private List<float> dmgSlash2 = new List<float> { 420f };
    private List<float> dmgArrow  = new List<float> { 200f };
    private List<float> dmgBomb   = new List<float> { 350f };

    // ── FSM ───────────────────────────────────────────────────────
    private enum S
    {
        Orbit,
        BasicAttack,
        ChainAttack,
        TeleMove,
        TeleAttack,
        TeleDelay,
        TeleReturn,
    }

    private S     cur;
    private float timer;

    // ── State data ────────────────────────────────────────────────
    private int orbitSide = 1;

    private int chainCount;
    private int chainDone;
    private int chainGeneration;  

    private int teleLeft;
    private int teleStepIdx;       

    // ── Vị trí tele quanh player ──────────────────────────────────
    // Đa hướng xung quanh player để skill đổi vị trí thấy rõ
    private static readonly Vector2[] TeleDirs =
    {
        new Vector2( 1f,   0f),
        new Vector2(-1f,   0f),
        new Vector2( 0.7f, 0.7f),
        new Vector2(-0.7f, 0.7f),
        new Vector2( 0.7f,-0.7f),
        new Vector2(-0.7f,-0.7f),
    };

    // ── Ring Arrow ────────────────────────────────────────────────
    [Header("Ring Arrow")]
    [SerializeField] private int ringArrowCount = 12;

    // ── entityKey ─────────────────────────────────────────────────
    private string entityKey;

    private float DeltaTime => timeScale.DeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        minionStat = GetComponent<Stat>();
        minionMove = GetComponent<Move>();
        timeScale  = GetComponent<TimeScale>();
        animator   = GetComponent<Animator>()
                  ?? GetComponentInChildren<Animator>();

        entityKey = $"{minionStat.NameCharacter}_{minionStat.GetInstanceID()}";
    }

    protected override void Start()
    {
        base.Start();
        if (slash1Prefab) slash1Pool.SetPrefab(slash1Prefab);
        if (slash2Prefab) slash2Pool.SetPrefab(slash2Prefab);
        if (bombPrefab)   bombPool.SetPrefab(bombPrefab);
    }

    private void OnEnable()
    {
        minionMove.ResetAll();
        minionMove.CanMove  = true;
        minionStat.CanDamge = true;
        Enter(S.Orbit);

        EventManager.Entity.OnEntityDead
            .Get(entityKey)
            .AddListener(OnSelfDead);
    }

    private void OnDisable()
    {
        minionStat.CanDamge = true;
        minionMove.ResetAll();

        EventManager.Entity.OnEntityDead
            .Get(entityKey)
            .RemoveListener(OnSelfDead);
    }

    protected override void Update()
    {
        base.Update();
        FacePlayer();

        switch (cur)
        {
            case S.Orbit:       /* orbit in FixedUpdate */ break;
            case S.BasicAttack: /* animation driven */ break;
            case S.ChainAttack: /* animation driven */ break;
            case S.TeleMove:    /* vào TeleAttack ngay trong Enter */ break;
            case S.TeleAttack:  /* animation driven */ break;
            case S.TeleDelay:   UpdateTeleDelay();  break;
            case S.TeleReturn:  UpdateTeleReturn(); break;
        }
    }

    private void FixedUpdate()
    {
        if (cur == S.Orbit || cur == S.ChainAttack)
        {
            UpdateOrbit();
        }
    }

    // ── Enter ─────────────────────────────────────────────────────
    private void Enter(S state)
    {
        cur = state;
        switch (state)
        {
            case S.Orbit:
                minionMove.CanMove = true;
                break;

            case S.BasicAttack:
                minionMove.CanMove = false;
                minionMove.ResetAll();
                break;

            case S.ChainAttack:
                minionMove.CanMove = true;
                chainDone = 0;
                chainGeneration++;
                break;

            case S.TeleMove:
                minionMove.CanMove = false;
                minionMove.ResetAll();
                DoTeleportNearPlayer();
                Enter(S.TeleAttack);
                return;

            case S.TeleAttack:
                animator?.SetBool("isAttack", true);
                break;

            case S.TeleDelay:
                timer = teleDelay;
                break;

            case S.TeleReturn:
                minionMove.CanMove = true; 
    
                timer = teleReturnDelay;
                if (playerTransform != null)
                {
                    Vector2 returnTarget = (Vector2)playerTransform.position
                                           + new Vector2(orbitRadius * orbitSide, 0f);
                    minionMove.TargetPosition = returnTarget;
                    minionMove.MoveToXY(orbitMoveSpeed * 60f, teleReturnDelay);
                }
                break;
        }
    }

    // ── Update methods ────────────────────────────────────────────
    private void UpdateOrbit()
    {
        if (playerTransform == null) return;
        Vector2 target = (Vector2)playerTransform.position + new Vector2(orbitRadius * orbitSide, 0f);
        Vector2 delta  = target - (Vector2)transform.position;
        float   dist   = delta.magnitude;
        if (dist < orbitSnapDist)
        {
            minionMove.Rb.linearVelocity = Vector2.zero;
            return;
        }
        float speed = Mathf.Min(dist * 5f, orbitMoveSpeed);
        minionMove.Rb.linearVelocity = delta.normalized * speed;
    }

    private void UpdateTeleDelay()
    {
        timer -= DeltaTime;
        if (timer <= 0f)
        {
            teleLeft--;
            if (teleLeft > 0)
                Enter(S.TeleMove);
            else
                Enter(S.TeleReturn);
        }
    }

    private void UpdateTeleReturn()
    {
        timer -= DeltaTime;
        if (timer <= 0f)
        {
            if (playerTransform != null)
                transform.position = (Vector2)playerTransform.position
                                   + new Vector2(orbitRadius * orbitSide, 0f);
            minionMove.ResetAll();
            Enter(S.Orbit);
            OnTeleSkillComplete?.Invoke();
        }
    }

    // ── Animation Events ──────────────────────────────────────────
    public void OnSpawnSlash1()
    {
        if (playerTransform == null) return;
        Vector2 dir   = GetDirToPlayer();
        float   angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        SpawnBullet(slash1Pool, dmgSlash1, transform.position, angle);
    }

    public void OnSpawnSlash2()
    {
        if (playerTransform == null) return;
        Vector2 dir   = GetDirToPlayer();
        float   angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        SpawnBullet(slash2Pool, dmgSlash2, transform.position, angle);
    }

    public void OnAttackAnimEnd()
    {
        switch (cur)
        {
            case S.BasicAttack:
                ResetAttackBools();
                Enter(S.Orbit);
                break;

            case S.ChainAttack:
                chainDone++;
                if (chainDone >= chainCount)
                {
                    int finishedGen = chainGeneration;
                    ResetAttackBools();
                    Enter(S.Orbit);
                    if (finishedGen == chainGeneration)
                        OnChainAttackComplete?.Invoke();
                }
                else
                    animator?.SetBool("isAttack", true);
                break;

            case S.TeleAttack:
                ResetAttackBools();
                Enter(S.TeleDelay);
                break;
        }
    }

    private void ResetAttackBools()
    {
        if (animator == null) return;
        animator.SetBool("isAttack", false);
    }

    // ── Public API (Boss gọi) ─────────────────────────────────────

    public int OrbitSide => orbitSide;

    public void SetOrbitSide(int side)
    {
        orbitSide = side;
    }

    public void TriggerBasicAttack()
    {
        if (cur != S.Orbit) return;
        Enter(S.BasicAttack);
        animator?.SetBool("isAttack", true);
    }

    public void TriggerChainAttack(int count)
    {
        if (cur != S.Orbit) return;
        chainCount = count;
        Enter(S.ChainAttack);
        animator?.SetBool("isAttack", true);
    }

    /// <summary>
    /// Tele 3 lần, mỗi lần random 1 hướng khác quanh player
    /// </summary>
    public void TriggerTeleSkill()
    {
        if (cur != S.Orbit) return;
        teleLeft    = teleRepeatCount;
        teleStepIdx = UnityEngine.Random.Range(0, TeleDirs.Length);
        Enter(S.TeleMove);
    }

    public void TriggerDashArrow()
    {
        SpawnRingArrow();
    }

    public void TriggerSpawnObject()
    {
        if (bombPool == null) return;
        var obj = bombPool.GetGameObject();
        if (obj == null) return;
        obj.transform.position = transform.position;
        obj.SetActive(true);
        var b = obj.GetComponent<BulletObject>();
        if (b != null)
            b.SetUp(DamageType.Magic, dmgBomb, minionStat,
                    minionStat.CurCritRate, minionStat.CurCritDamage);
    }

    /// <summary>
    /// Cho phép boss ép minion về Orbit để không bị reject skill kế tiếp
    /// </summary>
    public void ForceReturnToOrbit()
    {
        chainGeneration++;
        ResetAttackBools();
        minionMove.ResetAll();
        minionMove.CanMove = true;
        Enter(S.Orbit);
    }

    // ── LoadData ──────────────────────────────────────────────────
    public void LoadData(Transform player, EasyPoolingList condemnArrowPool)
    {
        playerTransform = player;
        arrowPool       = condemnArrowPool;
    }

    public override void ApplyData()
    {
        base.ApplyData();
        dmgSlash1 = GetDamageList("SupremeCatastropheSlash1");
        dmgSlash2 = GetDamageList("SupremeCatastropheSlash2");
        dmgArrow  = GetDamageList("SupremeCatastropheArrow");
        dmgBomb   = GetDamageList("SupremeCatastropheBomb");
    }

    private List<float> GetDamageList(string id)
    {
        if (skillData.TryGetValue(id, out var data) && data.damage?.Count > 0)
            return data.damage;
        return new List<float> { 200f };
    }

    // ── Ring Arrow ────────────────────────────────────────────────
    private void SpawnRingArrow()
    {
        if (arrowPool == null) return;
        float step = 360f / ringArrowCount;
        for (int i = 0; i < ringArrowCount; i++)
        {
            var obj = arrowPool.GetGameObject();
            if (obj == null) continue;
            obj.transform.position = transform.position;
            obj.SetActive(true);
            var b = obj.GetComponent<BulletObject>();
            if (b == null) continue;
            b.SetUp(DamageType.Magic, dmgArrow, minionStat,
                    minionStat.CurCritRate, minionStat.CurCritDamage);
            b.EasyModeChange(BulletMoveMode.Angle, step * i);
        }
    }

    // ── Tele helper ───────────────────────────────────────────────
    /// <summary>
    /// Teleport đến 1 trong nhiều hướng khác nhau quanh player.
    /// Mỗi lần gọi, index dịch sang hướng tiếp theo → 3 lần tele = 3 vị trí khác nhau.
    /// </summary>
    private void DoTeleportNearPlayer()
    {
        if (playerTransform == null) return;

        Vector2 dir    = TeleDirs[teleStepIdx % TeleDirs.Length];
        // Skip 1 index cho đa dạng hơn mỗi session (vd 0→1, 2→3, 4→5)
        teleStepIdx   += UnityEngine.Random.Range(1, 3);

        Vector2 offset = dir * teleOffsetDistance;
        Vector2 newPos = (Vector2)playerTransform.position + offset;

        transform.position = newPos;
        if (minionMove.Rb != null)
            minionMove.Rb.position = newPos;
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void SpawnBullet(EasyPoolingList pool, List<float> dmg, Vector2 pos, float angle)
    {
        if (pool == null) return;
        var obj = pool.GetGameObject();
        if (obj == null) return;
        obj.transform.position = pos;
        obj.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        obj.SetActive(true);
        var b = obj.GetComponent<BulletObject>();
        if (b == null) return;
        b.SetUp(DamageType.Magic, dmg, minionStat,
                minionStat.CurCritRate, minionStat.CurCritDamage);
        b.EasyModeChange(BulletMoveMode.Forward);
    }

    private Vector2 GetDirToPlayer()
    {
        if (playerTransform == null) return transform.right;
        return ((Vector2)playerTransform.position - (Vector2)transform.position).normalized;
    }

    private void FacePlayer()
    {
        if (playerTransform == null) return;
        float toPlayerX  = playerTransform.position.x - transform.position.x;
        int   wantedFlip = toPlayerX >= 0f ? 1 : -1;
        if (wantedFlip != minionMove.FlipDirect)
        {
            minionMove.FlipDirect = wantedFlip;
            transform.Rotate(0f, 180f, 0f);
        }
    }

    private void OnSelfDead(Component sender, object data)
    {
        EventManager.Entity.OnEntityDead
            .Get("CalamitasImmortal2Minion")
            .Invoke(this, null);
    }

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        if (playerTransform != null)
        {
            Vector2 orbitPos = (Vector2)playerTransform.position
                             + new Vector2(orbitRadius * orbitSide, 0f);
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.8f);
            Gizmos.DrawWireSphere(orbitPos, 0.3f);
            Gizmos.DrawLine(transform.position, orbitPos);
        }

        // Vẽ các vị trí tele khả dĩ
        if (playerTransform != null)
        {
            Gizmos.color = new Color(1f, 0.2f, 0.8f, 0.6f);
            for (int i = 0; i < TeleDirs.Length; i++)
            {
                Vector2 telePos = (Vector2)playerTransform.position
                                + TeleDirs[i] * teleOffsetDistance;
                Gizmos.DrawWireSphere(telePos, 0.25f);
            }
        }
    }
}