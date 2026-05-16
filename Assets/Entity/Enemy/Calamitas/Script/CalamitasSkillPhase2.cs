using System;
using System.Collections.Generic;
using UnityEngine;

public class CalamitasSkillPhase2 : MonoBehaviour
{
    public Action OnComplete;

    // ── Idle ──────────────────────────────────────────────────────
    [Header("Idle")]
    [SerializeField] private float idleDuration      = 2.5f;
    [SerializeField] private float idleFireInterval  = 1f;
    [SerializeField] private float orbitDistanceX    = 4f;
    [SerializeField] private float orbitHeightY      = 2f;
    [SerializeField] private float orbitMoveSpeed    = 3f;

    // ── Skill 1 — Chasing Inferno ─────────────────────────────────
    [Header("Skill 1 — Sun")]
    [SerializeField] private float sunLiveTime = 15f;

    [Header("Skill 1 — Orbit")]
    [SerializeField] private float orbitTopY         = 5f;
    [SerializeField] private float orbitSideX        = 5f;
    [SerializeField] private float orbitSideY        = 2f;
    [SerializeField] private float orbitSideDuration = 2.5f;

    [Header("Skill 1 — Side Hellfire")]
    [SerializeField] private int   s1HellfireExtraPairs = 1;
    [SerializeField] private float s1HellfireAngleStep  = 20f;
    [SerializeField] private float s1HellfireInterval   = 1.5f;

    [Header("Skill 1 — Top DarkSoul")]
    [SerializeField] private float s1DarkSoulInterval   = 1f;
    [SerializeField] private int   s1DarkSoulExtraPairs = 1;
    [SerializeField] private float s1DarkSoulAngleStep  = 20f;
    [SerializeField] private int   s1DarkSoulCount      = 3;

    [Header("Skill 1 — SuicideBomber")]
    [SerializeField] private int   s1BomberCount      = 6;
    [SerializeField] private float s1BomberRingRadius = 3f;
    [SerializeField] private float s1BomberDelay      = 0.3f;

    [Header("Skill 1 — Bomb")]
    [SerializeField] private int   s1BombCount        = 3;
    [SerializeField] private float s1BombInterval     = 4f;
    [SerializeField] private float s1BombSpread       = 2f;

    // ── Skill 2 — Revolving Lasers ────────────────────────────────
    [Header("Skill 2 — Laser")]
    [SerializeField] private Transform laserPivot;
    [SerializeField] private float     laserRotateSpeed  = 60f;
    [SerializeField] private float     s2LaserStartDelay = 1.5f;

    [Header("Skill 2 — Ring")]
    [SerializeField] private int   s2RingCount       = 12;
    [SerializeField] private int   s2RingRepeat      = 3;
    [SerializeField] private float s2RingInterval    = 1f;

    [Header("Skill 2 — Dagger Lines")]
    [SerializeField] private int   s2DaggerGroupCount  = 2;
    [SerializeField] private int   s2DaggerLinesPerGroup = 4;
    [SerializeField] private float s2DaggerLineSpacing = 1.2f;
    [SerializeField] private float s2DaggerWarnDuration = 1.5f;
    [SerializeField] private int   s2DaggerRepeat      = 3;
    [SerializeField] private float s2DaggerLineWidth   = 0.03f;
    [SerializeField] private Color s2DaggerLineColor   = Color.red;

    [Header("Skill 2 — Hellblast")]
    [SerializeField] private float s2HellblastInterval   = 0.5f;
    [SerializeField] private float s2HellblastDuration   = 5f;
    [SerializeField] private int   s2HellblastExtraPairs = 1;
    [SerializeField] private float s2HellblastAngleMin   = 15f;
    [SerializeField] private float s2HellblastAngleMax   = 45f;

    // ── Skill 3 — Phantom Execution ───────────────────────────────
    [Header("Skill 3 — Teleport")]
    [SerializeField] private float teleportRectX = 5f;
    [SerializeField] private float teleportRectY = 3f;

    [Header("Skill 3 — Aim")]
    [SerializeField] private int   s3LineCount     = 5;
    [SerializeField] private float s3AimInterval   = 0.7f;
    [SerializeField] private float s3LineHighY     = 8f;
    [SerializeField] private float s3LineLowY      = 8f;
    [SerializeField] private float s3LineOffsetX   = 1.5f;
    [SerializeField] private float s3LineWidth     = 0.03f;
    [SerializeField] private Color s3LineColor     = new Color(1f, 0f, 0.5f, 0.9f);
    [SerializeField] private int   s3RepeatCount   = 4;

    // ── Components ────────────────────────────────────────────────
    private Stat            bossStat;
    private Move            bossMove;
    private TimeScale       timeScale;
    private SpriteRenderer  bossRenderer;
    private CalamitasShadow shadow;
    private Transform       playerTransform;

    // ── Pools & Damage ────────────────────────────────────────────
    private EasyPoolingList hellfirePool;
    private EasyPoolingList hellblastPool;
    private EasyPoolingList daggerPool;
    private EasyPoolingList darkSoulPool;
    private EasyPoolingList suicideBomberPool;
    private EasyPoolingList bombPool;
    private EasyPoolingList sunPool;

    private List<float> dmgHellfire      = new List<float> { 280f };
    private List<float> dmgHellblast     = new List<float> { 300f };
    private List<float> dmgDagger        = new List<float> { 250f };
    private List<float> dmgDarkSoul      = new List<float> { 220f };
    private List<float> dmgSuicideBomber = new List<float> { 400f };
    private List<float> dmgBomb          = new List<float> { 350f };
    private List<float> dmgSun           = new List<float> { 200f };

    // ── FSM ───────────────────────────────────────────────────────
    private enum S
    {
        Intro,
        Idle,
        S1Start, S1Top, S1Right, S1Left, S1BomberSpawn,
        S2Teleport, S2LaserWait, S2Ring, S2DaggerWarn, S2DaggerFire, S2Hellblast,
        S3Teleport, S3Aiming, S3LockOn, S3Fire,
    }

    private static readonly S[] Rotation =
    {
        S.Idle, S.S1Start, S.Idle, S.S2Teleport, S.Idle, S.S3Teleport
    };

    private S     cur;
    private int   rotIdx = -1;
    private float timer;
    private int   orbitSide = 1;

    // Skill 1
    private GameObject sunInstance;
    private float      s1FireTimer;
    private float      s1BombTimer;
    private float      s1BomberTimer;
    private int        s1DarkSoulFired;
    private int        s1BombersFired;

    // Skill 2
    private int   s2RingFired;
    private int   s2DaggerRepeatDone;
    private float s2HellblastTimer;
    private float s2HellblastLeft;
    private float s2LaserAngle;
    private readonly List<LineRenderer> s2WarnLines = new List<LineRenderer>();

    // Skill 3
    private int     s3Cycles;
    private int     s3LinesShown;
    private float   s3AimTimer;
    private readonly Vector2[] s3LinePos    = new Vector2[10];
    private readonly float[]   s3LineAngles = new float[10];
    private readonly List<LineRenderer> s3WarnLines = new List<LineRenderer>();

    private float DeltaTime => timeScale.DeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        bossStat     = GetComponent<Stat>();
        bossMove     = GetComponent<Move>();
        timeScale    = GetComponent<TimeScale>();
        shadow       = GetComponent<CalamitasShadow>();
        bossRenderer = GetComponent<SpriteRenderer>()
                    ?? GetComponentInChildren<SpriteRenderer>();

        // Pre-create S2 dagger warning lines
        int s2LineTotal = s2DaggerGroupCount * s2DaggerLinesPerGroup;
        for (int i = 0; i < s2LineTotal + 4; i++)
            s2WarnLines.Add(CreateLineRenderer($"S2Warn_{i}", s2DaggerLineColor, s2DaggerLineWidth));

        // Pre-create S3 aim lines
        for (int i = 0; i < 10; i++)
            s3WarnLines.Add(CreateLineRenderer($"S3Aim_{i}", s3LineColor, s3LineWidth));
    }

    private void Start()
    {
        if (laserPivot != null) laserPivot.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        rotIdx    = -1;
        orbitSide = 1;
        bossStat.CanDamge = true;
        FindPlayer();
        Enter(S.Intro);
    }

    private void OnDisable()
    {
        bossMove.ResetAll();
        bossMove.CanMove = true;
        if (laserPivot != null) laserPivot.gameObject.SetActive(false);
        HideAllS2Lines();
        HideAllS3Lines();
        if (sunInstance != null) sunInstance.SetActive(false);
    }

    // ── Setup ─────────────────────────────────────────────────────
    public void LoadData(
        EasyPoolingList hellfire, EasyPoolingList hellblast,
        EasyPoolingList dagger,   EasyPoolingList darkSoul,
        EasyPoolingList suicide,  EasyPoolingList bomb,
        EasyPoolingList sun,
        List<float> dmgHF, List<float> dmgHB, List<float> dmgDag,
        List<float> dmgDS, List<float> dmgSB, List<float> dmgBmb,
        List<float> dmgSun)
    {
        hellfirePool      = hellfire;
        hellblastPool     = hellblast;
        daggerPool        = dagger;
        darkSoulPool      = darkSoul;
        suicideBomberPool = suicide;
        bombPool          = bomb;
        sunPool           = sun;

        if (dmgHF  != null) dmgHellfire      = dmgHF;
        if (dmgHB  != null) dmgHellblast     = dmgHB;
        if (dmgDag != null) dmgDagger        = dmgDag;
        if (dmgDS  != null) dmgDarkSoul      = dmgDS;
        if (dmgSB  != null) dmgSuicideBomber = dmgSB;
        if (dmgBmb != null) dmgBomb          = dmgBmb;
        if (dmgSun != null) this.dmgSun      = dmgSun;
    }

    // ── Update ────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        switch (cur)
        {
            case S.Idle:
            case S.S1Start:
            case S.S1Right:
            case S.S1Left:
            case S.S1Top:
            case S.S1BomberSpawn:
                UpdateOrbit();
                break;
        }
    }

    // ── Enter ─────────────────────────────────────────────────────
    private void Enter(S state)
    {
        cur = state;
        switch (state)
        {
            case S.Intro:
                // Teleport về tâm trước khi bắt đầu phase
                bossMove.ResetAll();
                bossMove.CanMove = false;
                s3Cycles         = 0;
                shadow?.PlayDepartShadow(transform.position);
                shadow?.PlayArriveShadow(Vector2.zero);
                if (bossRenderer != null) bossRenderer.enabled = false;
                timer = shadow != null ? Mathf.Max(0f, shadow.FadeDuration - 0.1f) : 0f;
                break;

            case S.Idle:
                timer         = idleDuration;
                s1FireTimer   = idleFireInterval;
                break;

            case S.S1Start:
                SpawnSun();
                s1BombTimer = s1BombInterval;
                Enter(S.S1Top);
                return;

            case S.S1Top:
                s1FireTimer     = s1DarkSoulInterval;
                s1DarkSoulFired = 0;
                break;

            case S.S1Right:
                orbitSide     = 1;
                timer         = orbitSideDuration;
                s1FireTimer   = s1HellfireInterval;
                break;

            case S.S1Left:
                orbitSide     = -1;
                timer         = orbitSideDuration;
                s1FireTimer   = s1HellfireInterval;
                break;

            case S.S1BomberSpawn:
                s1BombersFired = 0;
                s1BomberTimer  = 0f;
                break;

            case S.S2Teleport:
                shadow?.PlayDepartShadow(transform.position);
                shadow?.PlayArriveShadow(Vector2.zero);
                if (bossRenderer != null) bossRenderer.enabled = false;
                timer = shadow != null ? Mathf.Max(0f, shadow.FadeDuration - 0.1f) : 0f;
                break;

            case S.S2LaserWait:
                bossMove.ResetAll();
                bossMove.CanMove = false;

                if (bossMove.FlipDirect != 1)
                {
                    bossMove.FlipDirect    = 1;
                    transform.rotation     = Quaternion.Euler(0f, 0f, 0f);
                }
                timer = s2LaserStartDelay;
                break;

            case S.S2Ring:
                s2RingFired = 0;
                timer       = 0f;
                if (laserPivot != null)
                {
                    foreach (var laser in laserPivot.GetComponentsInChildren<BrimstoneLaser>(true))
                        laser.SetUp(DamageType.Magic, dmgHellfire, bossStat,
                                    bossStat.CurCritRate, bossStat.CurCritDamage);
                    laserPivot.gameObject.SetActive(true);
                }
                break;

            case S.S2DaggerWarn:
                HideAllS2Lines();
                ShowS2DaggerWarning();
                timer = s2DaggerWarnDuration;
                break;

            case S.S2DaggerFire:
                FireS2Daggers();
                s2DaggerRepeatDone++;
                if (s2DaggerRepeatDone < s2DaggerRepeat)
                    Enter(S.S2DaggerWarn);
                else
                    Enter(S.S2Hellblast);
                break;

            case S.S2Hellblast:
                s2HellblastTimer = s2HellblastInterval;
                s2HellblastLeft  = s2HellblastDuration;
                break;

            case S.S3Teleport:
                bossMove.ResetAll();
                bossMove.CanMove = false;
                s3TeleportDone   = false;
                DoS3Teleport();
                break;

            case S.S3Aiming:
                HideAllS3Lines();
                s3LinesShown = 0;
                s3AimTimer   = s3AimInterval;
                break;

            case S.S3LockOn:
                timer = s3AimInterval;
                break;

            case S.S3Fire:
                FireS3Daggers();
                s3Cycles++;
                if (s3Cycles >= s3RepeatCount)
                {
                    bossMove.CanMove = true;
                    NextRot();
                }
                else
                    Enter(S.S3Teleport);
                break;
        }
    }

    private void NextRot()
    {
        rotIdx = (rotIdx + 1) % Rotation.Length;
        Enter(Rotation[rotIdx]);
    }

    // ══════════════════════════════════════════════════════════════
    // IDLE
    // ══════════════════════════════════════════════════════════════
    private void UpdateIdle()
    {
        s1FireTimer -= DeltaTime;
        if (s1FireTimer <= 0f)
        {
            s1FireTimer = idleFireInterval;
            FireHellfire(AngleToPlayer(), 0, 0f);
        }
        timer -= DeltaTime;
        if (timer <= 0f) NextRot();
    }

    private void UpdateOrbit()
    {
        if (playerTransform == null) return;
        Vector2 target = GetOrbitTarget();
        Vector2 delta  = target - (Vector2)transform.position;
        float   dist   = delta.magnitude;
        if (dist < 0.05f) { bossMove.Rb.linearVelocity = Vector2.zero; return; }
        float speed = Mathf.Min(dist * 5f, orbitMoveSpeed);
        bossMove.Rb.linearVelocity = delta.normalized * speed;
    }

    private Vector2 GetOrbitTarget()
    {
        Vector2 p = playerTransform.position;
        return cur switch
        {
            S.Idle     => p + new Vector2(orbitDistanceX * orbitSide, orbitHeightY),
            S.S1Top    => p + new Vector2(0f,              orbitTopY),
            S.S1Right  => p + new Vector2(orbitSideX,     orbitSideY),
            S.S1Left   => p + new Vector2(-orbitSideX,    orbitSideY),
            S.S1BomberSpawn => p + new Vector2(0f,         orbitTopY),
            _ => p + new Vector2(orbitDistanceX * orbitSide, orbitHeightY),
        };
    }

    // ══════════════════════════════════════════════════════════════
    // SKILL 1
    // ══════════════════════════════════════════════════════════════
    private void SpawnSun()
    {
        if (sunPool == null) return;
        var obj = sunPool.GetGameObject();
        if (obj == null) return;
        obj.transform.position = transform.position;
        obj.SetActive(true);
        var b = obj.GetComponent<BulletObject>();
        if (b != null)
        {
            b.SetUp(DamageType.Magic, dmgSun, bossStat,
                    bossStat.CurCritRate, bossStat.CurCritDamage);
            b.EasyModeChange(BulletMoveMode.Homing);
        }
        sunInstance = obj;
    }

    private bool IsSunAlive() => sunInstance != null && sunInstance.activeSelf;

    private void CheckSunDied()
    {
        if (!IsSunAlive()) NextRot();
    }

    private void UpdateS1Bombs()
    {
        s1BombTimer -= DeltaTime;
        if (s1BombTimer <= 0f)
        {
            s1BombTimer = s1BombInterval;
            SpawnBombs();
        }
    }

    private void SpawnBombs()
    {
        if (playerTransform == null || bombPool == null) return;
        Vector2 playerPos = playerTransform.position;

        for (int i = 0; i < s1BombCount; i++)
        {
            var obj = bombPool.GetGameObject();
            if (obj == null) continue;

            Vector2 pos = i == 0
                ? playerPos
                : playerPos + UnityEngine.Random.insideUnitCircle * s1BombSpread;

            obj.transform.position = pos;
            obj.SetActive(true);
            var b = obj.GetComponent<BulletObject>();
            if (b != null)
                b.SetUp(DamageType.Magic, dmgBomb, bossStat,
                        bossStat.CurCritRate, bossStat.CurCritDamage);
        }
    }

    private void UpdateS1Top()
    {
        UpdateS1Bombs();
        CheckSunDied();
        if (cur != S.S1Top) return;

        s1FireTimer -= DeltaTime;
        if (s1FireTimer <= 0f)
        {
            s1FireTimer = s1DarkSoulInterval;
            FireDarkSouls();
            s1DarkSoulFired++;
            if (s1DarkSoulFired >= s1DarkSoulCount)
                Enter(S.S1BomberSpawn);
        }
    }

    private void FireDarkSouls()
    {
        if (darkSoulPool == null) return;
        float base1 = AngleToPlayer();
        SpawnBulletAngle(darkSoulPool, dmgDarkSoul, transform.position, base1);
        for (int i = 1; i <= s1DarkSoulExtraPairs; i++)
        {
            SpawnBulletAngle(darkSoulPool, dmgDarkSoul, transform.position, base1 + s1DarkSoulAngleStep * i);
            SpawnBulletAngle(darkSoulPool, dmgDarkSoul, transform.position, base1 - s1DarkSoulAngleStep * i);
        }
    }

    private void UpdateS1BomberSpawn()
    {
        UpdateS1Bombs();
        CheckSunDied();
        if (cur != S.S1BomberSpawn) return;

        s1BomberTimer -= DeltaTime;
        if (s1BomberTimer > 0f) return;

        if (s1BombersFired < s1BomberCount)
        {
            SpawnSuicideBomber(s1BombersFired);
            s1BombersFired++;
            s1BomberTimer = s1BomberDelay;
        }
        else
        {
            Enter(S.S1Right);
        }
    }

    private void SpawnSuicideBomber(int index)
    {
        if (suicideBomberPool == null) return;
        float   angle = 360f / s1BomberCount * index;
        float   rad   = angle * Mathf.Deg2Rad;
        Vector2 pos   = (Vector2)transform.position
                      + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * s1BomberRingRadius;

        var obj = suicideBomberPool.GetGameObject();
        if (obj == null) return;
        obj.transform.position = pos;
        obj.SetActive(true);
        var b = obj.GetComponent<BulletObject>();
        if (b != null)
        {
            b.SetUp(DamageType.Magic, dmgSuicideBomber, bossStat,
                    bossStat.CurCritRate, bossStat.CurCritDamage);
            b.EasyModeChange(BulletMoveMode.Homing);
        }
    }

    private void UpdateS1Right()
    {
        UpdateS1Bombs();
        CheckSunDied();
        if (cur != S.S1Right) return;

        s1FireTimer -= DeltaTime;
        if (s1FireTimer <= 0f)
        {
            s1FireTimer = s1HellfireInterval;
            FireHellfire(AngleToPlayer(), s1HellfireExtraPairs, s1HellfireAngleStep);
        }
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S1Left);
    }

    private void UpdateS1Left()
    {
        UpdateS1Bombs();
        CheckSunDied();
        if (cur != S.S1Left) return;

        s1FireTimer -= DeltaTime;
        if (s1FireTimer <= 0f)
        {
            s1FireTimer = s1HellfireInterval;
            FireHellfire(AngleToPlayer(), s1HellfireExtraPairs, s1HellfireAngleStep);
        }
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S1Top);
    }

    // ══════════════════════════════════════════════════════════════
    // SKILL 2
    // ══════════════════════════════════════════════════════════════
    private void UpdateS2Teleport()
    {
        timer -= DeltaTime;
        if (timer > 0f) return;
        transform.position = Vector3.zero;
        if (bossRenderer != null) bossRenderer.enabled = true;
        s2DaggerRepeatDone = 0;
        s2LaserAngle       = 0f;
        Enter(S.S2LaserWait);
    }

    private void UpdateS2LaserWait()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S2Ring);
    }

    private void UpdateS2Ring()
    {
        // Rotate laser
        if (laserPivot != null)
        {
            float rot = laserRotateSpeed * DeltaTime;
            laserPivot.Rotate(0f, 0f, rot);
            s2LaserAngle += rot;
        }

        timer -= DeltaTime;
        if (timer > 0f) return;

        FireRing();
        s2RingFired++;

        if (s2RingFired >= s2RingRepeat)
        {
            s2DaggerRepeatDone = 0;
            Enter(S.S2DaggerWarn);
        }
        else
        {
            timer = s2RingInterval;
        }
    }

    private void FireRing()
    {
        float step = 360f / s2RingCount;
        for (int i = 0; i < s2RingCount; i++)
            SpawnBulletAngle(hellfirePool, dmgHellfire, transform.position, step * i);
    }

    private void UpdateS2DaggerWarn()
    {
        // Rotate laser
        if (laserPivot != null)
            laserPivot.Rotate(0f, 0f, laserRotateSpeed * DeltaTime);

        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S2DaggerFire);
    }

    private float s2DaggerAngle;
    private void ShowS2DaggerWarning()
    {
        s2DaggerAngle = UnityEngine.Random.Range(0f, 360f);
        int lineIdx = 0;
        for (int g = 0; g < s2DaggerGroupCount; g++)
        {
            float groupAngle = s2DaggerAngle + g * 90f;
            float cos = Mathf.Cos(groupAngle * Mathf.Deg2Rad);
            float sin = Mathf.Sin(groupAngle * Mathf.Deg2Rad);
            Vector2 lineDir = new Vector2(cos, sin);
            Vector2 perpDir = new Vector2(-sin, cos);

            float halfLen = 8f;
            float halfSpan = (s2DaggerLinesPerGroup - 1) * s2DaggerLineSpacing * 0.5f;

            for (int l = 0; l < s2DaggerLinesPerGroup; l++)
            {
                float   offset = -halfSpan + l * s2DaggerLineSpacing;
                Vector2 center = (Vector2)transform.position + perpDir * offset;

                if (lineIdx >= s2WarnLines.Count) break;
                var lr = s2WarnLines[lineIdx++];
                lr.SetPosition(0, center - lineDir * halfLen);
                lr.SetPosition(1, center + lineDir * halfLen);
                lr.gameObject.SetActive(true);
            }
        }
    }

    private void FireS2Daggers()
    {
        HideAllS2Lines();
        int lineIdx = 0;
        for (int g = 0; g < s2DaggerGroupCount; g++)
        {
            float groupAngle = s2DaggerAngle + g * 90f;
            float cos = Mathf.Cos(groupAngle * Mathf.Deg2Rad);
            float sin = Mathf.Sin(groupAngle * Mathf.Deg2Rad);
            Vector2 lineDir = new Vector2(cos, sin);
            Vector2 perpDir = new Vector2(-sin, cos);

            float halfSpan = (s2DaggerLinesPerGroup - 1) * s2DaggerLineSpacing * 0.5f;

            for (int l = 0; l < s2DaggerLinesPerGroup; l++)
            {
                float   offset   = -halfSpan + l * s2DaggerLineSpacing;
                Vector2 spawnPos = (Vector2)transform.position + perpDir * offset - lineDir * 8f;
                SpawnBulletAngle(daggerPool, dmgDagger, spawnPos, groupAngle);
                spawnPos = (Vector2)transform.position + perpDir * offset + lineDir * 8f;
                SpawnBulletAngle(daggerPool, dmgDagger, spawnPos, groupAngle + 180f);
                lineIdx++;
            }
        }
    }

    private void UpdateS2Hellblast()
    {
        // Rotate laser
        if (laserPivot != null)
            laserPivot.Rotate(0f, 0f, laserRotateSpeed * DeltaTime);

        s2HellblastLeft  -= DeltaTime;
        s2HellblastTimer -= DeltaTime;

        if (s2HellblastTimer <= 0f)
        {
            s2HellblastTimer = s2HellblastInterval;
            float spread = UnityEngine.Random.Range(s2HellblastAngleMin, s2HellblastAngleMax);
            FireHellblast(AngleToPlayer(), s2HellblastExtraPairs, spread);
        }

        if (s2HellblastLeft <= 0f)
        {
            if (laserPivot != null) laserPivot.gameObject.SetActive(false);
            bossMove.CanMove = true;
            NextRot();
        }
    }

    private void FireHellblast(float baseAngle, int pairs, float angleStep)
    {
        SpawnBulletAngle(hellblastPool, dmgHellblast, transform.position, baseAngle);
        for (int i = 1; i <= pairs; i++)
        {
            SpawnBulletAngle(hellblastPool, dmgHellblast, transform.position, baseAngle + angleStep * i);
            SpawnBulletAngle(hellblastPool, dmgHellblast, transform.position, baseAngle - angleStep * i);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // SKILL 3
    // ══════════════════════════════════════════════════════════════
    private void DoS3Teleport()
    {
        if (playerTransform == null) return;
        float rx = UnityEngine.Random.Range(-teleportRectX, teleportRectX);
        float ry = UnityEngine.Random.Range(-teleportRectY, teleportRectY);
        Vector2 dest = (Vector2)playerTransform.position + new Vector2(rx, ry);

        shadow?.PlayDepartShadow(transform.position);
        shadow?.PlayArriveShadow(dest);
        if (bossRenderer != null) bossRenderer.enabled = false;

        timer          = shadow != null ? Mathf.Max(0f, shadow.FadeDuration - 0.1f) : 0f;
        s3TeleportDest = dest;
        s3TeleportDone = false;
    }

    private Vector2 s3TeleportDest;
    private bool    s3TeleportDone;

    private void UpdateS3Teleport_Tick()
    {
        timer -= DeltaTime;
        if (timer > 0f) return;
        if (!s3TeleportDone)
        {
            s3TeleportDone     = true;
            transform.position = s3TeleportDest;
            if (bossRenderer != null) bossRenderer.enabled = true;
        }
        Enter(S.S3Aiming);
    }

    private void UpdateS3Aiming()
    {
        s3AimTimer -= DeltaTime;
        if (s3AimTimer > 0f) return;
        s3AimTimer = s3AimInterval;

        if (s3LinesShown < s3LineCount)
        {
            ShowS3AimLine(s3LinesShown);
            s3LinesShown++;
        }

        if (s3LinesShown >= s3LineCount)
            Enter(S.S3LockOn);
    }

    private void ShowS3AimLine(int index)
    {
        if (index >= s3WarnLines.Count || playerTransform == null) return;

        float offset = (index - (s3LineCount - 1) * 0.5f)
                     * (s3LineOffsetX / Mathf.Max(1, s3LineCount - 1));
        Vector2 start = new Vector2(
            playerTransform.position.x + offset,
            playerTransform.position.y + s3LineHighY
        );

        Vector2 dir      = ((Vector2)playerTransform.position - start).normalized;
        float   lineLen  = s3LineHighY + s3LineLowY;
        Vector2 end      = start + dir * lineLen;
        float   fireAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        s3LinePos[index]    = new Vector2(start.x, start.y);
        s3LineAngles[index] = fireAngle;

        var lr = s3WarnLines[index];
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.gameObject.SetActive(true);
    }

    private void UpdateS3LockOn()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S3Fire);
    }

    private void FireS3Daggers()
    {
        HideAllS3Lines();
        for (int i = 0; i < s3LinesShown; i++)
            SpawnBulletAngle(daggerPool, dmgDagger, s3LinePos[i], s3LineAngles[i]);
    }

    // ── Update ────────────────────────────────────────────────────
    private void Update()
    {

        switch (cur)
        {
            case S.Intro:         UpdateIntro();            break;
            case S.Idle:          UpdateIdle();             break;
            case S.S1Start:       /* instant → S1Top */     break;
            case S.S1Top:         UpdateS1Top();            break;
            case S.S1Right:       UpdateS1Right();          break;
            case S.S1Left:        UpdateS1Left();           break;
            case S.S1BomberSpawn: UpdateS1BomberSpawn();    break;
            case S.S2Teleport:    UpdateS2Teleport();       break;
            case S.S2LaserWait:   UpdateS2LaserWait();      break;
            case S.S2Ring:        UpdateS2Ring();           break;
            case S.S2DaggerWarn:  UpdateS2DaggerWarn();     break;
            case S.S2DaggerFire:  /* instant */             break;
            case S.S2Hellblast:   UpdateS2Hellblast();      break;
            case S.S3Teleport:    UpdateS3Teleport_Tick();  break;
            case S.S3Aiming:      UpdateS3Aiming();         break;
            case S.S3LockOn:      UpdateS3LockOn();         break;
            case S.S3Fire:        /* instant */             break;
        }
    }

    private void UpdateIntro()
    {
        timer -= DeltaTime;
        if (timer > 0f) return;

        transform.position = Vector3.zero;
        if (bossRenderer != null) bossRenderer.enabled = true;
        bossMove.CanMove = true;
        NextRot();
    }

    // ── Fire Helpers ──────────────────────────────────────────────
    private void FireHellfire(float baseAngle, int pairs, float step)
    {
        SpawnBulletAngle(hellfirePool, dmgHellfire, transform.position, baseAngle);
        for (int i = 1; i <= pairs; i++)
        {
            SpawnBulletAngle(hellfirePool, dmgHellfire, transform.position, baseAngle + step * i);
            SpawnBulletAngle(hellfirePool, dmgHellfire, transform.position, baseAngle - step * i);
        }
    }

    private void SpawnBulletAngle(EasyPoolingList pool, List<float> dmg, Vector2 pos, float angle)
    {
        if (pool == null) return;
        var obj = pool.GetGameObject();
        if (obj == null) return;
        obj.transform.position = pos;
        obj.SetActive(true);
        var b = obj.GetComponent<BulletObject>();
        if (b == null) return;
        b.SetUp(DamageType.Magic, dmg, bossStat,
                bossStat.CurCritRate, bossStat.CurCritDamage);
        b.EasyModeChange(BulletMoveMode.Angle, angle);
    }

    private float AngleToPlayer()
    {
        if (playerTransform == null) return 0f;
        Vector2 d = ((Vector2)playerTransform.position - (Vector2)transform.position).normalized;
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }
    
    public bool LockFacing =>
        cur == S.Intro        ||
        cur == S.S2LaserWait  || cur == S.S2Ring       ||
        cur == S.S2DaggerWarn || cur == S.S2DaggerFire || cur == S.S2Hellblast;

    private void FindPlayer()
    {
        var go = GameObject.FindWithTag("Player");
        if (go != null) playerTransform = go.transform;
    }

    // ── Line Helpers ──────────────────────────────────────────────
    private static Material s_lineMat;

    private LineRenderer CreateLineRenderer(string name, Color color, float width)
    {
        if (s_lineMat == null)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader != null)
                s_lineMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        var go = new GameObject(name);
        go.transform.SetParent(null);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount    = 2;
        lr.startWidth       = width;
        lr.endWidth         = width;
        lr.useWorldSpace    = true;
        lr.sortingLayerName = "Default";
        lr.sortingOrder     = 10;
        lr.sharedMaterial   = s_lineMat;
        lr.startColor       = color;
        lr.endColor         = color;
        go.SetActive(false);
        return lr;
    }

    private void HideAllS2Lines()
    {
        foreach (var lr in s2WarnLines)
            if (lr != null) lr.gameObject.SetActive(false);
    }

    private void HideAllS3Lines()
    {
        foreach (var lr in s3WarnLines)
            if (lr != null) lr.gameObject.SetActive(false);
    }

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        if (playerTransform == null) return;
        Vector3 p = playerTransform.position;

        // Idle orbit
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireSphere(p + new Vector3( orbitDistanceX, orbitHeightY, 0f), 0.3f);
        Gizmos.DrawWireSphere(p + new Vector3(-orbitDistanceX, orbitHeightY, 0f), 0.3f);

        // S1 orbit positions
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
        Gizmos.DrawWireSphere(p + new Vector3(0f,         orbitTopY,  0f), 0.3f);
        Gizmos.DrawWireSphere(p + new Vector3( orbitSideX, orbitSideY, 0f), 0.3f);
        Gizmos.DrawWireSphere(p + new Vector3(-orbitSideX, orbitSideY, 0f), 0.3f);

        // S1 bomber ring
        Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, s1BomberRingRadius);

        // S3 teleport rect (quanh player)
        Gizmos.color = new Color(0.8f, 0.3f, 1f, 0.8f);
        Vector3 tl = p + new Vector3(-teleportRectX,  teleportRectY, 0f);
        Vector3 tr = p + new Vector3( teleportRectX,  teleportRectY, 0f);
        Vector3 bl = p + new Vector3(-teleportRectX, -teleportRectY, 0f);
        Vector3 br = p + new Vector3( teleportRectX, -teleportRectY, 0f);
        Gizmos.DrawLine(tl, tr); Gizmos.DrawLine(tr, br);
        Gizmos.DrawLine(br, bl); Gizmos.DrawLine(bl, tl);
        Gizmos.color = new Color(0.8f, 0.3f, 1f, 0.08f);
        Gizmos.DrawCube(p, new Vector3(teleportRectX * 2f, teleportRectY * 2f, 0f));
    }
}