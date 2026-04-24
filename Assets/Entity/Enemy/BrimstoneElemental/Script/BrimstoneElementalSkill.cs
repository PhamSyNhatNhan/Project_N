using System.Collections.Generic;
using UnityEngine;

public class BrimstoneElementalSkill : EnemySkill
{
    // ── Phase flags ───────────────────────────────────────────────
    private bool isBrimrose = false;
    private bool isPhase2   = false;

    [Header("Phase")]
    [SerializeField] private float brimroseThreshold = 0.5f;

    // ── Config Idle ───────────────────────────────────────────────
    [Header("Idle")]
    [SerializeField] private float idleDuration = 1.5f;

    // ── Config Attack1 ────────────────────────────────────────────
    [Header("Attack1 - Phase1")]
    [SerializeField] private GameObject petalPrefab;
    [SerializeField] private GameObject hellblastPrefab;
    [SerializeField] private float      petalOffset        = 1.0f;
    [SerializeField] private float      warningDuration    = 1.5f;
    [SerializeField] private int        baseHellblastCount = 5;
    [SerializeField] private float      spreadAngle        = 45f;
    [SerializeField] private float      bulletInterval     = 0.1f;
    [SerializeField] private int        repeatCount        = 3;

    [Header("Attack1 - Phase2")]
    [SerializeField] private float      p2WarningDuration  = 1.0f;
    [SerializeField] private int        p2RepeatCount      = 5;
    [SerializeField] private float      p2WarningDecrement = 0.1f;

    // ── Config Attack2 ────────────────────────────────────────────
    [Header("Attack2")]
    [SerializeField] private GameObject hellFireballPrefab;
    [SerializeField] private GameObject dartPrefab;
    [SerializeField] private float      atk2TeleportDelay  = 1.0f;
    [SerializeField] private float      atk2StepDelay      = 0.5f;
    [SerializeField] private float      dartLineWarningDur = 0.8f;

    [Header("Attack2 - Phase2")]
    [SerializeField] private float      p2Atk2TeleportDelay = 0.6f;
    [SerializeField] private float      p2Atk2StepDelay     = 0.3f;

    [Header("Attack2 - Hellblast Formation")]
    [SerializeField] private int   hellblastCount       = 6;
    [SerializeField] private float hellblastPerpSpacing = 0.4f;
    [SerializeField] private float hellblastBackSpacing = 0.2f;

    [Header("Attack2 - Fireball")]
    [SerializeField] private int     fireballCircleCount = 8;
    [SerializeField] private Vector2 fireballOffset      = Vector2.zero;

    [Header("Attack2 - Dart Square")]
    [SerializeField] private int   dartSquareCount   = 5;
    [SerializeField] private float dartSquareSpacing = 1.0f;
    [SerializeField] private float dartSquareRadius  = 6f;

    [Header("Attack2 - Dart Line")]
    [SerializeField] private int   dartLineCount   = 10;
    [SerializeField] private float dartLineSpacing = 1.2f;

    // ── Config Attack3 ────────────────────────────────────────────
    [Header("Attack3 - Phase1")]
    [SerializeField] private float      atk3AttackRange    = 5f;
    [SerializeField] private float      atk3AttackInterval = 1.5f;
    [SerializeField] private int        atk3RepeatCount    = 9;
    [SerializeField] private float      atk3WarnDuration   = 1.0f;  
    [SerializeField] private float      atk3HellfireAngle  = 30f;
    [SerializeField] private int        atk3HellfireCount  = 1;
    [SerializeField] private GameObject hellfirePrefab;

    [Header("Attack3 - Phase2")]
    [SerializeField] private float      p2Atk3Interval      = 0.8f;
    [SerializeField] private int        p2Atk3RepeatCount   = 12;
    [SerializeField] private int        p2Atk3HellfireCount = 2;

    // ── Config Attack4 ────────────────────────────────────────────
    [Header("Attack4 - Phase2 Only - Rose Bloom")]
    [SerializeField] private GameObject brimstoneRosePrefab;
    [SerializeField] private float      atk4SpawnInterval = 0.3f;
    [SerializeField] private float      atk4Duration      = 3.0f;
    [SerializeField] private float      atk4SpawnRadius   = 4f;

    // ── Global FSM ────────────────────────────────────────────────
    private enum AttackPhase { Idle, Attack1, Attack2, Attack3, Attack4 }

    private static readonly AttackPhase[] Phase1Rotation =
        { AttackPhase.Attack1, AttackPhase.Attack2, AttackPhase.Attack3 };
    private static readonly AttackPhase[] Phase2Rotation =
        { AttackPhase.Attack1, AttackPhase.Attack2, AttackPhase.Attack3, AttackPhase.Attack4 };

    private AttackPhase curPhase  = AttackPhase.Idle;
    private int         rotIndex  = -1; 
    private float       phaseTimer;

    // ── Attack1 FSM ───────────────────────────────────────────────
    private enum Atk1Phase { Warning, BulletInterval, Idle }
    private Atk1Phase atk1Phase;
    private int       atk1RepeatLeft;
    private float     atk1CurrentWarning;
    private float     atk1Timer;
    private Vector2   blinkTarget;
    private GameObject activePetal;
    private int       bulletsLeft;
    private float     bulletTimer;
    private Vector2   dirToPlayer;

    // ── Attack2 FSM ───────────────────────────────────────────────
    private int   atk2Step;
    private float atk2Timer;

    private GameObject   dartLineWarningObj;
    private LineRenderer dartLineRenderer;
    private GameObject   dartLineWarningObj2;
    private LineRenderer dartLineRenderer2;

    // ── Attack3 FSM ───────────────────────────────────────────────
    private enum Atk3Phase { PetalWarn, AttackInterval }
    private Atk3Phase atk3Phase;
    private float     atk3Timer;
    private int       atk3RepeatLeft;

    // ── Attack4 FSM ───────────────────────────────────────────────
    private float      atk4Timer;
    private float      atk4DurationLeft;
    private float      atk4SpawnTimer;
    private System.Collections.Generic.List<BrimstoneRose> spawnedRoses
        = new System.Collections.Generic.List<BrimstoneRose>();

    // ── Runtime ───────────────────────────────────────────────────
    private BrimroseSkill        brimroseSkill;
    private BrimstonePhase2Circle phase2Circle;
    private Stat                 enemyStat;
    private Move                 enemyMove;
    private Animator             animator;
    private SpriteRenderer       srcRenderer;
    private BrimstoneBlinkShadow blinkShadow;
    private Transform            playerTransform;
    private string entityKey => $"{enemyStat.NameCharacter}_{enemyStat.GetInstanceID()}";

    private readonly EasyPoolingList petalPool        = new EasyPoolingList();
    private readonly EasyPoolingList rosePool         = new EasyPoolingList();
    private readonly EasyPoolingList hellblastPool    = new EasyPoolingList();
    private readonly EasyPoolingList hellFireballPool = new EasyPoolingList();
    private readonly EasyPoolingList dartPool         = new EasyPoolingList();
    private readonly EasyPoolingList hellfirePool     = new EasyPoolingList();

    private float DeltaTime => timeScale.DeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        brimroseSkill = GetComponent<BrimroseSkill>();
        phase2Circle  = GetComponent<BrimstonePhase2Circle>();
        enemyStat     = GetComponent<Stat>();
        enemyMove     = GetComponent<Move>();
        animator      = GetComponent<Animator>();
        srcRenderer   = GetComponent<SpriteRenderer>();
        blinkShadow   = GetComponent<BrimstoneBlinkShadow>();
    }

    protected override void Start()
    {
        base.Start();
        if (petalPrefab)        petalPool.SetPrefab(petalPrefab);
        if (hellblastPrefab)    hellblastPool.SetPrefab(hellblastPrefab);
        if (hellFireballPrefab) hellFireballPool.SetPrefab(hellFireballPrefab);
        if (dartPrefab)         dartPool.SetPrefab(dartPrefab);
        if (hellfirePrefab)     hellfirePool.SetPrefab(hellfirePrefab);
        if (brimstoneRosePrefab) rosePool.SetPrefab(brimstoneRosePrefab);

        InitDartLineWarning();

        brimroseSkill?.LoadData(
            GetDamageList("BrimroseNova"),
            GetDamageList("BrimroseLaser"),
            GetDamageList("BrimroseRingDart"),
            GetDamageList("BrimroseDart"),
            GetDamageList("BrimroseHellblast")
        );

        EnterIdle();
    }

    private void OnEnable()
    {
        isBrimrose = false;
        isPhase2   = false;
        ResetAnimatorBools();
        EventManager.Entity.OnEntityHealthChanged
            .Get(entityKey).AddListener(OnHealthChanged);
    }

    private void OnDisable()
    {
        EventManager.Entity.OnEntityHealthChanged
            .Get(entityKey).RemoveListener(OnHealthChanged);
        if (activePetal != null) activePetal.SetActive(false);
        ClearDartWarnings();
    }

    private void OnDestroy()
    {
        if (dartLineWarningObj  != null) Destroy(dartLineWarningObj);
        if (dartLineWarningObj2 != null) Destroy(dartLineWarningObj2);
    }

    private void OnHealthChanged(Component sender, object data)
    {
        if (isBrimrose || isPhase2) return;
        if ((float)data > brimroseThreshold) return;

        isBrimrose = true;
        enemyStat.CanDamge = false;
        animator?.SetBool("isBrimrose", true);

        // Reset về nhìn phải
        if (enemyMove != null && enemyMove.FlipDirect == -1)
        {
            enemyMove.FlipDirect = 1;
            transform.Rotate(0f, 180f, 0f);
        }

        EnterIdle(); // dừng skill hiện tại
        brimroseSkill?.StartBrimrose(EndBrimrose);
    }

    // ── Update ────────────────────────────────────────────────────
    protected override void Update()
    {
        base.Update();
        FindPlayer();

        if (isBrimrose) return;

        FlipTowardPlayer();

        switch (curPhase)
        {
            case AttackPhase.Idle:    UpdateIdle();    break;
            case AttackPhase.Attack1: UpdateAttack1(); break;
            case AttackPhase.Attack2: UpdateAttack2(); break;
            case AttackPhase.Attack3: UpdateAttack3(); break;
            case AttackPhase.Attack4: UpdateAttack4(); break;
        }
    }

    // ── Flip ──────────────────────────────────────────────────────
    private void FlipTowardPlayer()
    {
        if (playerTransform == null || enemyMove == null) return;
        bool right = playerTransform.position.x > transform.position.x;
        if ( right && enemyMove.FlipDirect == -1) { enemyMove.FlipDirect =  1; transform.Rotate(0f, 180f, 0f); }
        if (!right && enemyMove.FlipDirect ==  1) { enemyMove.FlipDirect = -1; transform.Rotate(0f, 180f, 0f); }
    }

    // ── Idle ──────────────────────────────────────────────────────
    private void EnterIdle()
    {
        curPhase = AttackPhase.Idle;
        ResetAnimatorBools();
        enemyMove?.ResetAll();
        phaseTimer = idleDuration;
    }

    private void UpdateIdle()
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer > 0f) return;

        // Advance rotation: lần đầu (-1) → Attack1, sau đó cycle
        var rot = isPhase2 ? Phase2Rotation : Phase1Rotation;
        rotIndex = (rotIndex + 1) % rot.Length;
        EnterAttack(rot[rotIndex]);
    }

    // ── Enter Attack ──────────────────────────────────────────────
    private void EnterAttack(AttackPhase phase)
    {
        curPhase = phase;

        switch (phase)
        {
            case AttackPhase.Attack1:
                if (isPhase2)
                {
                    atk1RepeatLeft       = p2RepeatCount;
                    atk1CurrentWarning   = p2WarningDuration;
                }
                else
                {
                    atk1RepeatLeft       = repeatCount;
                    atk1CurrentWarning   = warningDuration;
                }
                EnterAtk1Warning();
                break;

            case AttackPhase.Attack2:
                atk2Step  = 0;
                atk2Timer = isPhase2 ? p2Atk2TeleportDelay : atk2TeleportDelay;
                break;

            case AttackPhase.Attack3:
                atk3RepeatLeft = isPhase2 ? p2Atk3RepeatCount : atk3RepeatCount;
                EnterAtk3PetalWarn();
                break;

            case AttackPhase.Attack4:
                spawnedRoses.Clear();
                atk4DurationLeft = atk4Duration;
                atk4SpawnTimer   = 0f;
                break;
        }
    }

    private void FinishAttack()
    {
        EnterIdle();
    }

    // ══════════════════════════════════════════════════════════════
    // ATTACK 1
    // ══════════════════════════════════════════════════════════════
    private void UpdateAttack1()
    {
        switch (atk1Phase)
        {
            case Atk1Phase.Idle:
                atk1Timer -= DeltaTime;
                if (atk1Timer > 0f) return;
                EnterAtk1Warning();
                break;

            case Atk1Phase.Warning:
                atk1Timer -= DeltaTime;
                if (atk1Timer > 0f) return;
                if (activePetal != null) { activePetal.SetActive(false); activePetal = null; }
                enemyMove.TargetPosition = blinkTarget;
                enemyMove.TranfromToXY(0f);
                blinkShadow?.Play(blinkTarget, srcRenderer);
                dirToPlayer = playerTransform != null
                    ? ((Vector2)playerTransform.position - blinkTarget).normalized
                    : Vector2.right;
                bulletsLeft = CalcHellblastCount();
                bulletTimer = 0f;
                // Phase2: giảm warning mỗi lần
                if (isPhase2)
                    atk1CurrentWarning = Mathf.Max(0.1f, atk1CurrentWarning - p2WarningDecrement);
                atk1Phase = Atk1Phase.BulletInterval;
                break;

            case Atk1Phase.BulletInterval:
                bulletTimer -= DeltaTime;
                if (bulletTimer > 0f) return;
                SpawnHellblastRandom();
                bulletsLeft--;
                bulletTimer = bulletInterval;
                if (bulletsLeft > 0) return;
                atk1RepeatLeft--;
                if (atk1RepeatLeft > 0)
                {
                    atk1Phase = Atk1Phase.Idle;
                    atk1Timer = idleDuration;
                }
                else
                {
                    FinishAttack();
                }
                break;
        }
    }

    private void EnterAtk1Warning()
    {
        if (playerTransform == null) return;
        float   a   = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float   d   = Random.Range(0f, petalOffset);
        blinkTarget = (Vector2)playerTransform.position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
        activePetal = petalPool.GetGameObject();
        if (activePetal != null) { activePetal.transform.position = blinkTarget; activePetal.SetActive(true); }
        atk1Phase = Atk1Phase.Warning;
        atk1Timer = atk1CurrentWarning;
    }

    // ══════════════════════════════════════════════════════════════
    // ATTACK 2 (giống nhau cả 2 phase)
    // ══════════════════════════════════════════════════════════════
    private void UpdateAttack2()
    {
        atk2Timer -= DeltaTime;
        if (atk2Timer > 0f) return;
        ExecuteAtk2Step(atk2Step);
    }

    private void ExecuteAtk2Step(int step)
    {
        switch (step)
        {
            case 0:
                if (playerTransform != null)
                {
                    Vector2 pPos = playerTransform.position;
                    activePetal = petalPool.GetGameObject();
                    if (activePetal != null) { activePetal.transform.position = pPos; activePetal.SetActive(true); }
                    animator?.SetBool("isStone", true);
                    enemyMove.TargetPosition = pPos;
                    enemyMove.TranfromToXY(0f);
                    blinkShadow?.Play(pPos, srcRenderer);
                    if (activePetal != null) { activePetal.SetActive(false); activePetal = null; }
                }
                AdvanceAtk2(atk2StepDelay);
                break;

            case 1: case 3:
                SpawnHellblastFormation();
                AdvanceAtk2(atk2StepDelay);
                break;

            case 2:
                if (isPhase2) SpawnDartSquare8(); else SpawnDartSquare4();
                SpawnFireballCircle();
                AdvanceAtk2(atk2StepDelay);
                break;

            case 4:
                SpawnDartLineWarning();
                AdvanceAtk2(dartLineWarningDur);
                break;

            case 5:
                ClearDartWarnings();
                SpawnDartLine();
                SpawnFireballCircle();
                animator?.SetBool("isStone", false);
                FinishAttack();
                break;
        }
    }

    private void AdvanceAtk2(float delay)
    {
        atk2Step++;
        // Phase2 dùng step delay nhỏ hơn, trừ dart line warning giữ nguyên
        atk2Timer = (isPhase2 && delay == atk2StepDelay) ? p2Atk2StepDelay : delay;
    }

    private void SpawnHellblastFormation()
    {
        if (playerTransform == null) return;
        Vector2 toPlayer = ((Vector2)playerTransform.position - (Vector2)transform.position).normalized;
        Vector2 perp     = new Vector2(-toPlayer.y, toPlayer.x);
        Vector2 back     = -toPlayer;
        float   angle    = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg;
        int     half     = hellblastCount / 2;

        for (int i = 0; i < hellblastCount; i++)
        {
            float   perpOffset = (half - 1 - i) * hellblastPerpSpacing;
            int     s          = i < half ? (half - 1 - i) : (i - half);
            float   backOffset = s * hellblastBackSpacing;
            Vector2 spawnPos   = (Vector2)transform.position + perp * perpOffset + back * backOffset;
            SpawnBulletAt(hellblastPool, spawnPos, angle, "BrimstoneAttack2Hellblast");
        }
    }

    private void SpawnFireballCircle()
    {
        Vector2 center = (Vector2)transform.position + fireballOffset;
        for (int i = 0; i < fireballCircleCount; i++)
            SpawnBulletAt(hellFireballPool, center, 360f / fireballCircleCount * i, "BrimstoneAttack2Fireball");
    }

    private void SpawnDartSquare4()
    {
        float[] angles  = { 270f, 90f, 0f, 180f };
        Vector2[] perps = { Vector2.right, Vector2.right, Vector2.up, Vector2.up };
        SpawnDartGroup(angles, perps);
    }

    private void SpawnDartSquare8()
    {
        SpawnDartSquare4();
        float[] diagAngles = { 225f, 45f, 135f, 315f };
        Vector2[] diagPerps =
        {
            new Vector2(-1f,  1f).normalized,
            new Vector2(-1f,  1f).normalized,
            new Vector2( 1f,  1f).normalized,
            new Vector2( 1f,  1f).normalized
        };
        SpawnDartGroup(diagAngles, diagPerps);
    }

    private void SpawnDartGroup(float[] inAngles, Vector2[] perpDirs)
    {
        for (int d = 0; d < inAngles.Length; d++)
        {
            float   inAngle  = inAngles[d];
            Vector2 inDir    = new Vector2(Mathf.Cos(inAngle * Mathf.Deg2Rad), Mathf.Sin(inAngle * Mathf.Deg2Rad));
            float   start    = -(dartSquareCount - 1) * 0.5f * dartSquareSpacing;
            for (int i = 0; i < dartSquareCount; i++)
            {
                float   pOff  = start + i * dartSquareSpacing;
                float   extra = (d % 2 == 1) ? dartSquareSpacing * 0.5f : 0f;
                Vector2 pos   = (Vector2)transform.position + perpDirs[d] * (pOff + extra) - inDir * dartSquareRadius;
                SpawnBulletAt(dartPool, pos, inAngle, "BrimstoneAttack2Dart");
            }
        }
    }

    private void SpawnDartLineWarning()
    {
        if (playerTransform == null) return;
        Vector2 toPlayer = ((Vector2)playerTransform.position - (Vector2)transform.position).normalized;
        Vector2 perp     = new Vector2(-toPlayer.y, toPlayer.x);
        float   half     = (dartLineCount - 1) * dartLineSpacing * 0.5f;
        Vector3 center   = transform.position;

        // Line 1 — luôn có (cả 2 phase)
        dartLineRenderer.SetPosition(0, center - (Vector3)(toPlayer * half));
        dartLineRenderer.SetPosition(1, center + (Vector3)(toPlayer * half));
        dartLineWarningObj.SetActive(true);

        // Line 2 — chỉ phase2
        if (isPhase2)
        {
            dartLineRenderer2.SetPosition(0, center - (Vector3)(perp * half));
            dartLineRenderer2.SetPosition(1, center + (Vector3)(perp * half));
            dartLineWarningObj2.SetActive(true);
        }
        else
        {
            dartLineWarningObj2.SetActive(false);
        }
    }

    private void SpawnDartLine()
    {
        if (playerTransform == null) return;
        Vector2 toPlayer  = ((Vector2)playerTransform.position - (Vector2)transform.position).normalized;
        Vector2 perp      = new Vector2(-toPlayer.y, toPlayer.x);
        float   half      = (dartLineCount - 1) * dartLineSpacing * 0.5f;
        Vector2 lineStart = (Vector2)transform.position - toPlayer * half;

        // Phase1 + Phase2: line theo trục boss→player
        for (int i = 0; i < dartLineCount; i++)
        {
            Vector2 basePos = lineStart + toPlayer * (i * dartLineSpacing);
            SpawnBulletAt(dartPool, basePos + perp * 0.5f,  Mathf.Atan2( perp.y,  perp.x) * Mathf.Rad2Deg, "BrimstoneAttack2Dart");
            SpawnBulletAt(dartPool, basePos - perp * 0.5f,  Mathf.Atan2(-perp.y, -perp.x) * Mathf.Rad2Deg, "BrimstoneAttack2Dart");
        }

        // Phase2 thêm: line vuông góc
        if (isPhase2)
        {
            Vector2 perpStart = (Vector2)transform.position - perp * half;
            for (int i = 0; i < dartLineCount; i++)
            {
                Vector2 basePos = perpStart + perp * (i * dartLineSpacing);
                SpawnBulletAt(dartPool, basePos + toPlayer * 0.5f,  Mathf.Atan2( toPlayer.y,  toPlayer.x) * Mathf.Rad2Deg, "BrimstoneAttack2Dart");
                SpawnBulletAt(dartPool, basePos - toPlayer * 0.5f,  Mathf.Atan2(-toPlayer.y, -toPlayer.x) * Mathf.Rad2Deg, "BrimstoneAttack2Dart");
            }
        }
    }

    private void ClearDartWarnings()
    {
        if (dartLineWarningObj  != null) dartLineWarningObj.SetActive(false);
        if (dartLineWarningObj2 != null) dartLineWarningObj2.SetActive(false);
    }

    // ══════════════════════════════════════════════════════════════
    // ATTACK 3
    // ══════════════════════════════════════════════════════════════
    private void UpdateAttack3()
    {
        if (atk3RepeatLeft <= 0) { FinishAttack(); return; }

        switch (atk3Phase)
        {
            case Atk3Phase.PetalWarn:
                atk3Timer -= DeltaTime;
                if (atk3Timer > 0f) return;
                if (activePetal != null) { activePetal.SetActive(false); activePetal = null; }
                if (playerTransform != null)
                {
                    Vector2 target = playerTransform.position;
                    blinkShadow?.Play(target, srcRenderer);
                    enemyMove.TargetPosition = target;
                    enemyMove.TranfromToXY(0f);
                }
                atk3Phase = Atk3Phase.AttackInterval;
                atk3Timer = isPhase2 ? p2Atk3Interval : atk3AttackInterval;
                break;

            case Atk3Phase.AttackInterval:
                if (playerTransform != null)
                {
                    float dist = Vector2.Distance(transform.position, playerTransform.position);
                    if (dist > atk3AttackRange)
                    {
                        Vector2 target = playerTransform.position;
                        blinkShadow?.Play(target, srcRenderer);
                        enemyMove.TargetPosition = target;
                        enemyMove.TranfromToXY(0f);
                    }
                    else
                    {
                        enemyMove.TargetPosition = playerTransform.position;
                        enemyMove.MoveToXYLimitDistance(enemyMove.CurMoveSpeed, 0.1f, 0f);
                    }
                }
                atk3Timer -= DeltaTime;
                if (atk3Timer > 0f) return;

                FireAtk3Blast();
                atk3RepeatLeft--;
                atk3Timer = isPhase2 ? p2Atk3Interval : atk3AttackInterval;
                break;
        }
    }

    private void EnterAtk3PetalWarn()
    {
        if (playerTransform == null) return;
        Vector2 target = playerTransform.position;
        activePetal = petalPool.GetGameObject();
        if (activePetal != null) { activePetal.transform.position = target; activePetal.SetActive(true); }
        atk3Phase = Atk3Phase.PetalWarn;
        atk3Timer = atk3WarnDuration;
    }

    private void FireAtk3Blast()
    {
        if (playerTransform == null) return;
        Vector2 toPlayer  = ((Vector2)playerTransform.position - (Vector2)transform.position).normalized;
        float   baseAngle = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg;

        SpawnBulletAt(hellblastPool, transform.position, baseAngle, "BrimstoneAttack3Hellblast");

        int activeHellfireCount = isPhase2 ? p2Atk3HellfireCount : atk3HellfireCount;

        for (int i = 1; i <= activeHellfireCount; i++)
        {
            float off = atk3HellfireAngle * i;
            SpawnBulletAt(hellfirePool, transform.position, baseAngle + off, "BrimstoneAttack3Hellfire");
            SpawnBulletAt(hellfirePool, transform.position, baseAngle - off, "BrimstoneAttack3Hellfire");
        }

        float outer = atk3HellfireAngle * (activeHellfireCount + 1);
        SpawnBulletAt(hellFireballPool, transform.position, baseAngle + outer,       "BrimstoneAttack3Hellfire");
        SpawnBulletAt(hellFireballPool, transform.position, baseAngle - outer,       "BrimstoneAttack3Hellfire");
        SpawnBulletAt(hellFireballPool, transform.position, baseAngle + outer * 2f,  "BrimstoneAttack3Hellfire");
        SpawnBulletAt(hellFireballPool, transform.position, baseAngle - outer * 2f,  "BrimstoneAttack3Hellfire");
    }

    // ══════════════════════════════════════════════════════════════
    // ATTACK 4 — Rose Bloom (Phase 2 only)
    // ══════════════════════════════════════════════════════════════
    private void UpdateAttack4()
    {
        atk4DurationLeft -= DeltaTime;

        // Spawn rose mỗi atk4SpawnInterval
        atk4SpawnTimer -= DeltaTime;
        if (atk4SpawnTimer <= 0f)
        {
            atk4SpawnTimer = atk4SpawnInterval;
            SpawnRose();
        }

        // Hết thời gian → activate tất cả rose
        if (atk4DurationLeft <= 0f)
        {
            foreach (var rose in spawnedRoses)
                if (rose != null && rose.gameObject.activeSelf)
                    rose.Activate();
            spawnedRoses.Clear();
            FinishAttack();
        }
    }

    private void SpawnRose()
    {
        float   angle    = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float   dist     = Random.Range(0f, atk4SpawnRadius);
        Vector2 spawnPos = (Vector2)transform.position
                         + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;

        GameObject obj = rosePool.GetGameObject();
        if (obj == null) return;

        obj.transform.position = spawnPos;
        obj.SetActive(true);

        var rose = obj.GetComponent<BrimstoneRose>();
        if (rose != null)
        {
            rose.SetUp(DamageType.Magic,
                       GetDamageList("BrimstoneAttack4Rose"),
                       enemyStat,
                       enemyStat.CurCritRate,
                       enemyStat.CurCritDamage);
            spawnedRoses.Add(rose);
        }
    }

        // ── Brimrose / Phase2 ─────────────────────────────────────────
    public void EndBrimrose()
    {
        isBrimrose = false;
        isPhase2   = true;
        enemyStat.CanDamge = true;
        animator?.SetBool("isBrimrose", false);
        animator?.SetBool("isPhase2",   true);
        if (brimroseSkill != null) brimroseSkill.enabled = false;
        rotIndex = -1;
        phase2Circle?.Activate();
        EnterIdle();
    }

    // ── Shared Helpers ────────────────────────────────────────────
    private void SpawnHellblastRandom()
    {
        float half  = spreadAngle * 0.5f;
        float angle = Mathf.Atan2(dirToPlayer.y, dirToPlayer.x) * Mathf.Rad2Deg
                    + Random.Range(-half, half);
        SpawnBulletAt(hellblastPool, transform.position, angle, "BrimstoneAttack1");
    }

    private void SpawnBulletAt(EasyPoolingList pool, Vector2 pos, float angle, string skillId)
    {
        GameObject obj = pool.GetGameObject();
        if (obj == null) return;
        obj.transform.position = pos;
        obj.SetActive(true);

        var sr = obj.GetComponent<SpriteRenderer>();
        if (sr != null) sr.flipY = Mathf.Cos(angle * Mathf.Deg2Rad) < 0f;

        var bullet = obj.GetComponent<BulletObject>();
        if (bullet != null)
        {
            bullet.SetUp(DamageType.Magic, GetDamageList(skillId), enemyStat,
                         enemyStat.CurCritRate, enemyStat.CurCritDamage);
            bullet.EasyModeChange(BulletMoveMode.Angle, angle);
        }
    }

    private int CalcHellblastCount()
    {
        float hpLost = 1f - (enemyStat.CurHealth / enemyStat.MaxHealth);
        return baseHellblastCount + Mathf.FloorToInt(hpLost * 10f);
    }

    private List<float> GetDamageList(string id)
    {
        if (skillData.TryGetValue(id, out var data) && data.damage?.Count > 0)
            return data.damage;
        return new List<float> { 300f };
    }

    private void FindPlayer()
    {
        if (playerTransform != null) return;
        var go = GameObject.FindWithTag("Player");
        if (go != null) playerTransform = go.transform;
    }

    private void ResetAnimatorBools()
    {
        animator?.SetBool("isStone",   false);
        animator?.SetBool("isBrimrose",false);
        animator?.SetBool("isPhase2",  false);
    }

    private void InitDartLineWarning()
    {
        dartLineWarningObj = new GameObject("DartLineWarning");
        dartLineWarningObj.transform.parent = null;
        dartLineRenderer = dartLineWarningObj.AddComponent<LineRenderer>();
        SetupLineRenderer(dartLineRenderer, Color.red);
        dartLineWarningObj.SetActive(false);

        dartLineWarningObj2 = new GameObject("DartLineWarning2");
        dartLineWarningObj2.transform.parent = null;
        dartLineRenderer2 = dartLineWarningObj2.AddComponent<LineRenderer>();
        SetupLineRenderer(dartLineRenderer2, Color.red);
        dartLineWarningObj2.SetActive(false);
    }

    private void SetupLineRenderer(LineRenderer lr, Color color)
    {
        lr.positionCount = 2;
        lr.startWidth    = 0.03f;
        lr.endWidth      = 0.03f;
        lr.material      = new Material(Shader.Find("Sprites/Default"));
        lr.startColor    = color;
        lr.endColor      = color;
        lr.useWorldSpace = true;
    }

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, petalOffset);

        Vector3 fbCenter = transform.position + new Vector3(fireballOffset.x, fireballOffset.y, 0f);
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.9f);
        Gizmos.DrawWireSphere(fbCenter, 0.3f);

        Gizmos.color = new Color(0f, 0.8f, 1f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, dartSquareRadius);

        Gizmos.color = new Color(1f, 0f, 0.5f, 0.15f);
        Gizmos.DrawSphere(transform.position, atk3AttackRange);
        Gizmos.color = new Color(1f, 0f, 0.5f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, atk3AttackRange);

        // Attack4 rose spawn area
        Gizmos.color = new Color(0.8f, 0f, 0.8f, 0.15f);
        Gizmos.DrawSphere(transform.position, atk4SpawnRadius);
        Gizmos.color = new Color(0.8f, 0f, 0.8f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, atk4SpawnRadius);
    }
}