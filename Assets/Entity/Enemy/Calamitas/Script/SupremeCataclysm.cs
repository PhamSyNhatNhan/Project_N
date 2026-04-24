using System;
using System.Collections.Generic;
using UnityEngine;

public class SupremeCataclysm : EnemySkill
{
    // ── Callbacks (Boss quản lý) ───────────────────────────────────
    public Action OnDashSkillComplete;
    public Action OnChainAttackComplete;

    // ── Orbit ─────────────────────────────────────────────────────
    [Header("Orbit")]
    [SerializeField] private float orbitRadius    = 5f;
    [SerializeField] private float orbitMoveSpeed = 5f;
    [SerializeField] private float orbitSnapDist  = 0.05f;

    // ── Bullet Prefabs ────────────────────────────────────────────
    [Header("Bullet Prefabs")]
    [SerializeField] private GameObject normalBulletPrefab;
    [SerializeField] private GameObject altBulletPrefab;
    [SerializeField] private GameObject bombPrefab;

    // ── Basic Attack ──────────────────────────────────────────────
    [Header("Basic Attack")]
    [SerializeField] private float normalBulletOffset = 0.5f;
    [SerializeField] private float altBulletOffset    = 0.5f;

    // ── Dash Skill ────────────────────────────────────────────────
    [Header("Dash Skill")]
    [SerializeField] private LayerMask  playerLayer;
    [SerializeField] private float      dashVelocity  = 900f;
    [SerializeField] private float      dashTime      = 0.2f;
    [SerializeField] private float      dashMaxDist   = 8f;
    [SerializeField] private float      dashDelay     = 0.4f;
    [SerializeField] private int        dashCount     = 3;

    // ── Components ────────────────────────────────────────────────
    private Stat      minionStat;
    private Move      minionMove;
    private Hitbox    hitbox;
    private Animator  animator;

    // ── Player ────────────────────────────────────────────────────
    private Transform playerTransform;

    // ── Pools & Damage ────────────────────────────────────────────
    private readonly EasyPoolingList normalBulletPool = new EasyPoolingList();
    private readonly EasyPoolingList altBulletPool    = new EasyPoolingList();
    private readonly EasyPoolingList bombPool         = new EasyPoolingList();
    private EasyPoolingList          arrowPool;
    private List<float> dmgNormal = new List<float> { 180f };
    private List<float> dmgAlt    = new List<float> { 300f };
    private List<float> dmgDash   = new List<float> { 400f };
    private List<float> dmgArrow  = new List<float> { 200f };
    private List<float> dmgBomb   = new List<float> { 350f };

    // ── FSM ───────────────────────────────────────────────────────
    private enum S
    {
        Orbit,
        BasicAttack,
        ChainAttack,
        DashDelay,
        Dash,
    }

    private S     cur;
    private float timer;

    // ── State data ────────────────────────────────────────────────
    private int  orbitSide = 1;

    private bool isAltMode;
    private int  chainCount;
    private int  chainDone;
    // Mỗi lần chain mới tăng generation → animation event cũ bị vô hiệu
    private int  chainGeneration;

    private int             dashLeft;
    private Vector2         dashStartPos;
    private readonly HashSet<Stat> dashHits = new HashSet<Stat>();

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
        hitbox     = GetComponent<Hitbox>();
        animator   = GetComponent<Animator>()
                  ?? GetComponentInChildren<Animator>();

        entityKey = $"{minionStat.NameCharacter}_{minionStat.GetInstanceID()}";
    }

    protected override void Start()
    {
        base.Start();
        if (normalBulletPrefab) normalBulletPool.SetPrefab(normalBulletPrefab);
        if (altBulletPrefab)    altBulletPool.SetPrefab(altBulletPrefab);
        if (bombPrefab)         bombPool.SetPrefab(bombPrefab);
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
            case S.DashDelay:   UpdateDashDelay(); break;
            case S.Dash:        UpdateDash();      break;
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
                chainGeneration++;            // Mỗi chain session có ID riêng
                break;

            case S.DashDelay:
                minionMove.ResetAll();
                minionMove.CanMove = false;
                timer = dashDelay;
                break;

            case S.Dash:
                dashStartPos = transform.position;
                dashHits.Clear();
    
                minionMove.CanMove = true; 
    
                SpawnRingArrow();

                if (playerTransform != null)
                {
                    Vector2 dir = ((Vector2)playerTransform.position - dashStartPos).normalized;
                    minionMove.MoveSnap = dir;
                    minionMove.MoveTo(dashVelocity, dashTime);
                }
                timer = dashTime;
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

    private void UpdateDashDelay()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.Dash);
    }

    private void UpdateDash()
    {
        // Check hit player
        if (hitbox != null)
        {
            var hits = hitbox.detectObject(playerLayer);
            if (hits != null)
                foreach (var hit in hits)
                {
                    var s = hit.GetComponent<Stat>();
                    if (s == null || s == minionStat || dashHits.Contains(s)) continue;
                    dashHits.Add(s);
                    s.TakeDamage(DamageType.Magic,
                        dmgDash.Count > 0 ? dmgDash[0] : 400f,
                        minionStat.CurCritRate, minionStat.CurCritDamage);
                }
        }

        timer -= DeltaTime;
        float traveled = Vector2.Distance(transform.position, dashStartPos);
        bool  timeUp   = timer <= 0f;
        bool  tooFar   = traveled >= dashMaxDist;

        if (!timeUp && !tooFar) return;

        if (tooFar)
        {
            Vector2 offset = (Vector2)transform.position - dashStartPos;
            if (offset.sqrMagnitude > 0.0001f)
            {
                Vector2 clampedPos = dashStartPos + offset.normalized * dashMaxDist;
                transform.position = clampedPos;
                if (minionMove.Rb != null) minionMove.Rb.position = clampedPos;
            }
        }

        minionMove.ResetAll();
        dashLeft--;
        if (dashLeft > 0)
            Enter(S.DashDelay);
        else
        {
            Enter(S.Orbit);
            OnDashSkillComplete?.Invoke();
        }
    }

    // ── Animation Events ──────────────────────────────────────────
    public void OnSpawnNormalBullet()
    {
        if (normalBulletPool == null) return;
        Vector2 dir    = GetDirToPlayer();
        Vector2 offset = dir * normalBulletOffset;
        SpawnBullet(normalBulletPool, dmgNormal,
                    (Vector2)transform.position + offset,
                    Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
    }

    public void OnSpawnAltBullet()
    {
        if (altBulletPool == null) return;
        Vector2 dir    = GetDirToPlayer();
        Vector2 offset = dir * altBulletOffset;
        SpawnBullet(altBulletPool, dmgAlt,
                    (Vector2)transform.position + offset,
                    Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
    }

    /// <summary>
    /// Gọi từ animation event — có cơ chế guard bằng chainGeneration
    /// để event animation của chain cũ không nhảy sang đếm cho chain mới.
    /// </summary>
    public void OnAttackAnimEnd()
    {
        if (cur == S.ChainAttack)
        {
            chainDone++;
            if (chainDone >= chainCount)
            {
                int finishedGen = chainGeneration;
                ResetAttackBools();
                Enter(S.Orbit);
                // Chỉ báo complete nếu chưa bị force-interrupt bởi session mới
                if (finishedGen == chainGeneration)
                    OnChainAttackComplete?.Invoke();
                return;
            }
            TriggerAttackAnim(isAltMode);
        }
        else if (cur == S.BasicAttack)
        {
            ResetAttackBools();
            Enter(S.Orbit);
        }
    }

    private void ResetAttackBools()
    {
        if (animator == null) return;
        animator.SetBool("isAttack",    false);
        animator.SetBool("isAttackAlt", false);
    }

    // ── Public API (Boss gọi) ─────────────────────────────────────

    public int OrbitSide => orbitSide;

    public void SetOrbitSide(int side)
    {
        orbitSide = side;
    }

    public void TriggerBasicAttack(bool useAlt)
    {
        if (cur != S.Orbit) return;
        isAltMode = useAlt;
        Enter(S.BasicAttack);
        TriggerAttackAnim(useAlt);
    }

    public void TriggerChainAttack(bool useAlt, int count)
    {
        if (cur != S.Orbit) return;
        isAltMode  = useAlt;
        chainCount = count;
        Enter(S.ChainAttack);
        TriggerAttackAnim(useAlt);
    }

    /// <summary>
    /// Vào DashDelay trước để có wind-up cho người chơi né được dash đầu tiên.
    /// </summary>
    public void TriggerDashSkill()
    {
        if (cur != S.Orbit) return;
        dashLeft = dashCount;
        Enter(S.DashDelay);
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

    public void TriggerDashArrow()
    {
        SpawnRingArrow();
    }

    /// <summary>
    /// Boss có thể ép minion về Orbit trước khi trigger skill mới,
    /// tránh bị reject vì đang ở state khác. chainGeneration++ vô hiệu
    /// hoá các animation event pending của chain cũ.
    /// </summary>
    public void ForceReturnToOrbit()
    {
        chainGeneration++;
        ResetAttackBools();
        minionMove.ResetAll();
        minionMove.CanMove = true;
        dashHits.Clear();
        dashLeft = 0;
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
        dmgNormal = GetDamageList("SupremeCataclysmNormal");
        dmgAlt    = GetDamageList("SupremeCataclysmAlt");
        dmgDash   = GetDamageList("SupremeCataclysmDash");
        dmgArrow  = GetDamageList("SupremeCataclysmArrow");
        dmgBomb   = GetDamageList("SupremeCataclysmBomb");
    }

    private List<float> GetDamageList(string id)
    {
        if (skillData.TryGetValue(id, out var data) && data.damage?.Count > 0)
            return data.damage;
        return new List<float> { 200f };
    }

    // ── Ring Arrow ───────────────────────────────────────────────
    [Header("Ring Arrow")]
    [SerializeField] private int ringArrowCount = 12;
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

    // ── Helpers ───────────────────────────────────────────────────
    private void TriggerAttackAnim(bool useAlt)
    {
        if (animator == null) return;
        animator.SetBool("isAttack",    !useAlt);
        animator.SetBool("isAttackAlt",  useAlt);
    }

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
        if (playerTransform == null || cur == S.Dash) return;
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
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, dashMaxDist);

        if (playerTransform != null)
        {
            Vector2 orbitPos = (Vector2)playerTransform.position
                             + new Vector2(orbitRadius * orbitSide, 0f);
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireSphere(orbitPos, 0.3f);
            Gizmos.DrawLine(transform.position, orbitPos);
        }
    }
}