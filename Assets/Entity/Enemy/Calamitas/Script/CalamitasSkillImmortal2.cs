using System;
using System.Collections.Generic;
using UnityEngine;

public class CalamitasSkillImmortal2 : MonoBehaviour
{
    // ── Callback ──────────────────────────────────────────────────
    public Action OnComplete;
    
    [Header("Boss Attack (Hellfire)")]
    [SerializeField] private float hellfireInterval = 1f;
    [SerializeField] private float hellfireFanInterval = 5f;
    [SerializeField] private int hellfireFanExtraPairs = 1;
    [SerializeField] private float hellfireFanAngleStep = 20f;
    
    private float hellfireTimer;
    private float hellfireFanTimer;

    private EasyPoolingList hellfirePool; 
    private EasyPoolingList arrowPool; 
    private List<float> dmgHellfire = new List<float> { 280f };

    // ── Minion Prefabs ────────────────────────────────────────────
    [Header("Minion Prefabs")]
    [SerializeField] private GameObject cataclysmPrefab;
    [SerializeField] private GameObject catastrophePrefab;

    // ── Scheduling ────────────────────────────────────────────────
    [Header("Scheduling")]
    [SerializeField] private float chainAttackInterval = 4.5f; 
    [SerializeField] private float skillCompleteDelay  = 1f;
    [SerializeField] private float dashDelay           = 0.5f;
    [SerializeField] private float uniqueSkillTimeout  = 8f;
    [SerializeField] private float sharedDashTimeout   = 6f;    

    // ── Cataclysm skill config ────────────────────────────────────
    [Header("Cataclysm Skill")]
    [SerializeField] private int cataclysmChainCount = 6;      
    [SerializeField] private int cataclysmDashCount  = 3;

    // ── Catastrophe skill config ──────────────────────────────────
    [Header("Catastrophe Skill")]
    [SerializeField] private int catastropheChainCount = 4;

    // ── Orbit ─────────────────────────────────────────────────────
    [Header("Orbit")]
    [SerializeField] private float bossOrbitRadius = 6f;

    // ── Components ────────────────────────────────────────────────
    private Stat      bossStat;
    private Move      bossMove;
    private TimeScale timeScale;
    private Transform playerTransform;

    // ── Arrow pool ────────────────────────────────────────────────
    private EasyPoolingList condemnArrowPool;

    // ── Minion refs ───────────────────────────────────────────────
    private SupremeCataclysm   cataclysm;
    private SupremeCatastrophe catastrophe;

    // ── FSM ───────────────────────────────────────────────────────
    private enum S
    {
        Idle,
        ChainAttack,
        WaitChainAttack,
        UniqueSkill,
        WaitUniqueSkill,
        SharedDash,
        WaitDash,
        SpawnObject,
        WaitComplete,
    }

    private S     cur;
    private float timer;
    private int   cycleStep;
    private int   uniqueSkillIndex;
    private int   minionDeadCount;
    private bool  phaseEnding;

    private bool cataclysmDashDone;
    private bool catastropheDashDone;

    private float DeltaTime => timeScale.DeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        bossStat  = GetComponent<Stat>();
        bossMove  = GetComponent<Move>();
        timeScale = GetComponent<TimeScale>();
    }

    private void OnEnable()
    {
        minionDeadCount  = 0;
        phaseEnding      = false;
        cycleStep        = 0;
        uniqueSkillIndex = 0;

        FindPlayer();
        SpawnMinions();
        EnterBossOrbit();
        Enter(S.Idle);

        EventManager.Entity.OnEntityDead
            .Get("CalamitasImmortal2Minion")
            .AddListener(OnMinionDead);
        
        hellfireTimer = hellfireInterval;
        hellfireFanTimer = hellfireFanInterval;
    }

    private void OnDisable()
    {
        bossMove.ResetAll();
        bossMove.CanMove = true;

        EventManager.Entity.OnEntityDead
            .Get("CalamitasImmortal2Minion")
            .RemoveListener(OnMinionDead);
    }

    private void Update()
    {
        FacePlayer();
        UpdateBossAttacks();
        UpdateBossOrbit();
        UpdateBossOrbit();

        switch (cur)
        {
            case S.Idle:             UpdateIdle();            break;
            case S.WaitChainAttack:  UpdateWaitChainAttack(); break;
            case S.WaitUniqueSkill:  UpdateWaitUniqueSkill(); break;
            case S.WaitDash:         UpdateWaitDash();        break;
            case S.SpawnObject:      UpdateSpawnObject();     break;
            case S.WaitComplete:     UpdateWaitComplete();    break;
        }
    }

    // ── Enter ─────────────────────────────────────────────────────
    private void Enter(S state)
    {
        cur = state;
        switch (state)
        {
            case S.Idle:
                timer = 0.5f;
                break;

            case S.ChainAttack:
                TriggerBothChainAttack();
                Enter(S.WaitChainAttack);
                return;

            case S.WaitChainAttack:
                timer = chainAttackInterval;
                break;

            case S.UniqueSkill:
                TriggerUniqueSkill();
                Enter(S.WaitUniqueSkill);
                return;

            case S.WaitUniqueSkill:
                timer = uniqueSkillTimeout;
                break;

            case S.SharedDash:
                cataclysmDashDone   = false;
                catastropheDashDone = false;
                TriggerBothDash();
                Enter(S.WaitDash);
                return;

            case S.WaitDash:
                timer = sharedDashTimeout;   // Thêm timeout tránh kẹt vĩnh viễn
                break;

            case S.SpawnObject:
                timer = dashDelay;
                break;

            case S.WaitComplete:
                timer = skillCompleteDelay;
                break;
        }
    }

    // ── Update methods ────────────────────────────────────────────
    private void UpdateIdle()
    {
        timer -= DeltaTime;
        if (timer > 0f) return;

        // Chu kỳ: 0-Chain, 1-Chain, 2-Unique, 3-Chain, 4-SharedDash
        switch (cycleStep)
        {
            case 0:
            case 1:
            case 3:
                Enter(S.ChainAttack);
                break;
            case 2:
                Enter(S.UniqueSkill);
                break;
            case 4:
                Enter(S.SharedDash);
                break;
        }

        cycleStep = (cycleStep + 1) % 5;
    }

    private void UpdateWaitChainAttack()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.Idle);
    }

    private void UpdateWaitUniqueSkill()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.WaitComplete);
    }

    private void UpdateWaitDash()
    {
        timer -= DeltaTime;
        if ((cataclysmDashDone && catastropheDashDone) || timer <= 0f)
            Enter(S.SpawnObject);
    }

    private void UpdateSpawnObject()
    {
        timer -= DeltaTime;
        if (timer > 0f) return;

        cataclysm?.TriggerSpawnObject();
        catastrophe?.TriggerSpawnObject();
        Enter(S.WaitComplete);
    }

    private void UpdateWaitComplete()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.Idle);
    }

    // ── Trigger helpers ───────────────────────────────────────────
    private void TriggerBothChainAttack()
    {
        // Ép minion về Orbit để đảm bảo skill không bị reject
        cataclysm?.ForceReturnToOrbit();
        catastrophe?.ForceReturnToOrbit();

        cataclysm?.TriggerChainAttack(false, cataclysmChainCount);
        catastrophe?.TriggerChainAttack(catastropheChainCount);
    }

    private void TriggerBothDash()
    {
        cataclysm?.ForceReturnToOrbit();
        catastrophe?.ForceReturnToOrbit();

        SwapMinionSides();

        cataclysm?.TriggerDashSkill();
        catastrophe?.TriggerDashArrow();
        catastropheDashDone = true; 
    }

    private void TriggerUniqueSkill()
    {
        if (uniqueSkillIndex % 2 == 0)
        {
            // Cataclysm turn
            cataclysm?.ForceReturnToOrbit();
            bool doChain = UnityEngine.Random.value > 0.5f;
            if (doChain)
                cataclysm?.TriggerChainAttack(false, cataclysmChainCount);
            else
                cataclysm?.TriggerDashSkill();
        }
        else
        {
            // Catastrophe turn
            catastrophe?.ForceReturnToOrbit();
            bool doChain = UnityEngine.Random.value > 0.5f;
            if (doChain)
                catastrophe?.TriggerChainAttack(catastropheChainCount);
            else
                catastrophe?.TriggerTeleSkill();
        }
        uniqueSkillIndex++;
    }

    // ── Callbacks ─────────────────────────────────────────────────
    /// <summary>
    /// Dash của Cataclysm phục vụ 2 ngữ cảnh:
    /// - SharedDash: flag cataclysmDashDone để đồng bộ với catastrophe
    /// - UniqueSkill: tiến sang WaitComplete
    /// </summary>
    private void OnCataclysmDashComplete()
    {
        if (cur == S.WaitDash)
            cataclysmDashDone = true;
        else if (cur == S.WaitUniqueSkill)
            Enter(S.WaitComplete);
    }

    /// <summary>
    /// Chain / Tele của minion (chỉ quan tâm trong UniqueSkill phase).
    /// </summary>
    private void OnUniqueSkillComplete()
    {
        if (cur == S.WaitUniqueSkill)
            Enter(S.WaitComplete);
    }

    // ── Boss orbit ────────────────────────────────────────────────
    private void EnterBossOrbit() => bossMove.CanMove = true;

    private void UpdateBossOrbit()
    {
        if (playerTransform == null) return;
        int bossSide = cataclysm != null ? -cataclysm.OrbitSide : 1;
        Vector2 target = (Vector2)playerTransform.position + new Vector2(bossOrbitRadius * bossSide, 0f);
        Vector2 delta  = target - (Vector2)transform.position;
        float   dist   = delta.magnitude;
        if (dist < 0.05f) { bossMove.Rb.linearVelocity = Vector2.zero; return; }
        float speed = Mathf.Min(dist * 5f, 4f);
        bossMove.Rb.linearVelocity = delta.normalized * speed;
    }

    // ── Spawn minions ─────────────────────────────────────────────
    private void SpawnMinions()
    {
        if (cataclysmPrefab != null)
        {
            var go = Instantiate(cataclysmPrefab);
            cataclysm = go.GetComponent<SupremeCataclysm>();
            if (cataclysm != null)
            {
                cataclysm.LoadData(playerTransform, condemnArrowPool);
                cataclysm.SetOrbitSide(1);
                
                cataclysm.OnDashSkillComplete   = OnCataclysmDashComplete;
                cataclysm.OnChainAttackComplete = OnUniqueSkillComplete;
                go.SetActive(true);
            }
        }

        if (catastrophePrefab != null)
        {
            var go = Instantiate(catastrophePrefab);
            catastrophe = go.GetComponent<SupremeCatastrophe>();
            if (catastrophe != null)
            {
                catastrophe.LoadData(playerTransform, condemnArrowPool);
                catastrophe.SetOrbitSide(-1);
                catastrophe.OnTeleSkillComplete   = OnUniqueSkillComplete;
                catastrophe.OnChainAttackComplete = OnUniqueSkillComplete;
                go.SetActive(true);
            }
        }
    }
    
    private void SwapMinionSides()
    {
        if (cataclysm != null && catastrophe != null)
        {
            // Tráo đổi OrbitSide của 2 minion
            int tempSide = cataclysm.OrbitSide;
            cataclysm.SetOrbitSide(catastrophe.OrbitSide);
            catastrophe.SetOrbitSide(tempSide);
        }
    }

    private void OnMinionDead(Component sender, object data)
    {
        minionDeadCount++;
        if (minionDeadCount < 2) return;
        if (phaseEnding) return;
        phaseEnding = true;
        enabled     = false;
        OnComplete?.Invoke();
    }

    public void LoadData(EasyPoolingList arrowPool) => condemnArrowPool = arrowPool;

    private void FacePlayer()
    {
        if (playerTransform == null) return;
        float toPlayerX = playerTransform.position.x - transform.position.x;
        int wantedFlip = toPlayerX >= 0f ? 1 : -1;
        if (wantedFlip != bossMove.FlipDirect)
        {
            bossMove.FlipDirect = wantedFlip;
            transform.rotation = Quaternion.Euler(0f, wantedFlip == 1 ? 0f : 180f, 0f);
        }
    }
    
    private void UpdateBossAttacks()
    {
        if (playerTransform == null || hellfirePool == null) return;

        hellfireTimer -= DeltaTime;
        hellfireFanTimer -= DeltaTime;

        if (hellfireFanTimer <= 0f)
        {
            hellfireFanTimer = hellfireFanInterval;
            hellfireTimer = hellfireInterval; 
            FireHellfire(AngleToPlayer(), hellfireFanExtraPairs, hellfireFanAngleStep);
        }
        else if (hellfireTimer <= 0f)
        {
            hellfireTimer = hellfireInterval;
            FireHellfire(AngleToPlayer(), 0, 0f);
        }
    }

    private void FireHellfire(float baseAngle, int pairs, float step)
    {
        SpawnBullet(hellfirePool, dmgHellfire, transform.position, baseAngle);
        for (int i = 1; i <= pairs; i++)
        {
            SpawnBullet(hellfirePool, dmgHellfire, transform.position, baseAngle + step * i);
            SpawnBullet(hellfirePool, dmgHellfire, transform.position, baseAngle - step * i);
        }
    }

    private void SpawnBullet(EasyPoolingList pool, List<float> dmg, Vector2 pos, float angle)
    {
        var obj = pool.GetGameObject();
        if (obj == null) return;
        obj.transform.position = pos;
        obj.SetActive(true);
        var b = obj.GetComponent<BulletObject>();
        if (b != null)
        {
            b.SetUp(DamageType.Magic, dmg, bossStat, bossStat.CurCritRate, bossStat.CurCritDamage);
            b.EasyModeChange(BulletMoveMode.Angle, angle);
        }
    }

    private float AngleToPlayer()
    {
        Vector2 d = ((Vector2)playerTransform.position - (Vector2)transform.position).normalized;
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

    public void LoadData(EasyPoolingList arrow, EasyPoolingList hf, List<float> hfDmg)
    {
        arrowPool = arrow;
        hellfirePool = hf;
        if (hfDmg != null) dmgHellfire = hfDmg;
    }
    
    private void FindPlayer()
    {
        var go = GameObject.FindWithTag("Player");
        if (go != null) playerTransform = go.transform;
    }
}