using System;
using System.Collections.Generic;
using UnityEngine;

public class CalamitasSkillPhase3 : MonoBehaviour
{
    public Action OnComplete;

    // ── Idle ──────────────────────────────────────────────────────
    [Header("Idle")]
    [SerializeField] private float idleDuration     = 2f;
    [SerializeField] private float idleFireInterval = 1f;
    [SerializeField] private float orbitDistanceX   = 4f;
    [SerializeField] private float orbitHeightY     = 2f;
    [SerializeField] private float orbitMoveSpeed   = 4f;

    // ── Skill 1 — Dash Ring ───────────────────────────────────────
    [Header("Skill 1 — Dash Ring")]
    [SerializeField] private GameObject dashFxObject;
    [SerializeField] private LayerMask  playerLayer;
    [SerializeField] private float      dashVelocity    = 900f;
    [SerializeField] private float      dashTime        = 0.2f;
    [SerializeField] private float      dashMaxDist     = 8f;
    [SerializeField] private float      dashDelay       = 1.5f;
    [SerializeField] private int        dashRepeatCount = 3;
    [SerializeField] private int        ringBulletCount = 12;

    // ── Skill 2 — Inferno Convergence ────────────────────────────
    [Header("Skill 2 — Seeker")]
    [SerializeField] private GameObject seekerPrefab;
    [SerializeField] private int        seekerCount         = 5;
    [SerializeField] private float      seekerSpawnRadius   = 2f;
    [SerializeField] private float      seekerSpawnInterval = 0.1f;   // NEW: stagger spawn

    [Header("Skill 2 — Orbit")]
    [SerializeField] private float s2OrbitDistanceX = 5f;
    [SerializeField] private float s2OrbitHeightY   = 2f;
    [SerializeField] private float s2OrbitDuration  = 2f;

    [Header("Skill 2 — Attack Chain")]
    [SerializeField] private float s2ChainInterval      = 1.5f;
    [SerializeField] private int   s2RepeatCount        = 3;
    [SerializeField] private float s2HellBlastOffset    = 1.5f;
    [SerializeField] private float s2DartWarnDuration   = 1f;
    [SerializeField] private int   s2DartCount          = 8;
    [SerializeField] private float s2DartSpacing        = 0.8f;
    [SerializeField] private float s2DartLineLength     = 6f;   // NEW: warning line length
    [SerializeField] private int   s2HellfireExtraPairs = 1;
    [SerializeField] private float s2HellfireAngleStep  = 20f;
    [SerializeField] private int   s2DarkSoulExtraPairs = 1;
    [SerializeField] private float s2DarkSoulAngleStep  = 20f;
    [SerializeField] private float s2DartLineWidth      = 0.03f;
    [SerializeField] private Color s2DartLineColor      = Color.red;

    // ── Skill 3 — Damnation Cross ─────────────────────────────────
    [Header("Skill 3 — Teleport")]
    [SerializeField] private Vector2 s3ArenaCenter = Vector2.zero;

    [Header("Skill 3 — Bomb Line")]
    [SerializeField] private int   s3BombCount   = 7;
    [SerializeField] private float s3BombSpacing = 1.5f;

    [Header("Skill 3 — Ring")]
    [SerializeField] private int   s3RingCount      = 12;
    [SerializeField] private float s3WarnDuration   = 1f;
    [SerializeField] private float s3WarnLineLength = 3f;
    [SerializeField] private float s3WarnLineWidth  = 0.03f;
    [SerializeField] private Color s3WarnLineColor  = new Color(1f, 0.2f, 0.2f, 0.8f);

    [Header("Skill 3 — Hellfire")]
    [SerializeField] private float s3HellfireDelay      = 1.5f;
    [SerializeField] private int   s3HellfireExtraPairs = 1;
    [SerializeField] private float s3HellfireAngleStep  = 20f;
    [SerializeField] private float s3CycleDelay         = 1.5f;
    [SerializeField] private int   s3RepeatCount        = 3;

    // ── Components ────────────────────────────────────────────────
    private Stat            bossStat;
    private Move            bossMove;
    private TimeScale       timeScale;
    private SpriteRenderer  bossRenderer;
    private CalamitasShadow shadow;
    private Hitbox          hitbox;
    private Transform       playerTransform;

    // ── Pools & Damage ────────────────────────────────────────────
    private EasyPoolingList hellfirePool;
    private EasyPoolingList hellblastPool;
    private EasyPoolingList condemnPool;
    private EasyPoolingList dartPool;    // ← pool mới cho CalamitasDart (đã tách khỏi Dagger)
    private EasyPoolingList darkSoulPool;
    private EasyPoolingList bombPool;

    private List<float> dmgHellfire  = new List<float> { 300f };
    private List<float> dmgHellblast = new List<float> { 320f };
    private List<float> dmgCondemn   = new List<float> { 350f };
    private List<float> dmgDart      = new List<float> { 280f };
    private List<float> dmgDarkSoul  = new List<float> { 260f };
    private List<float> dmgBomb      = new List<float> { 400f };
    private List<float> dmgDash      = new List<float> { 380f };

    // ── FSM ───────────────────────────────────────────────────────
    private enum S
    {
        Idle,
        S1Dash, S1Delay,
        S2Spawn, S2Spawning,                 
        S2Side, S2Chain1Wait, S2Chain2, S2ChainDelay,
        S3Teleport, S3Bomb, S3Warn, S3Hellfire, S3CycleDelay,
    }

    private static readonly S[] Rotation =
    {
        S.Idle, S.S1Dash, S.Idle, S.S2Spawn, S.Idle, S.S3Teleport
    };

    private S     cur;
    private int   rotIdx = -1;
    private float timer;
    private float idleFireTimer;
    private int   orbitSide = 1;

    // S1
    private int             s1RepeatLeft;
    private Vector2         s1DashStart;
    private readonly HashSet<Stat> dashHits = new HashSet<Stat>();

    // S2
    private int     s2RepeatLeft;
    private float   s2DartAngle;              // Góc toPlayer (angle đạn darts sẽ bắn dọc theo trục này)
    private int     s2SeekerSpawnIdx;         // NEW: index seeker tiếp theo sẽ spawn
    private float   s2SeekerSpawnTimer;       // NEW: đếm ngược giữa các lần spawn
    private readonly List<GameObject> seekerList = new List<GameObject>();

    // S3
    private int     s3CycleLeft;
    private float   s3HellfireTimer;
    private bool    s3TeleportDone;
    private Vector2 s3TeleportDest;

    // Warning lines
    private readonly List<LineRenderer> s2Lines = new List<LineRenderer>();
    private readonly List<LineRenderer> s3Lines = new List<LineRenderer>();

    private float DeltaTime      => timeScale.DeltaTime;
    private float FixedDeltaTime => timeScale.FixDeltaTime;

    public bool LockFacing =>
        cur == S.S1Dash    ||
        cur == S.S3Teleport|| cur == S.S3Bomb ||
        cur == S.S3Warn    || cur == S.S3Hellfire ||
        cur == S.S3CycleDelay;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        bossStat     = GetComponent<Stat>();
        bossMove     = GetComponent<Move>();
        timeScale    = GetComponent<TimeScale>();
        shadow       = GetComponent<CalamitasShadow>();
        hitbox       = GetComponent<Hitbox>();
        bossRenderer = GetComponent<SpriteRenderer>()
                    ?? GetComponentInChildren<SpriteRenderer>();
    }

    private void OnEnable()
    {
        rotIdx    = -1;
        orbitSide = 1;
        bossStat.CanDamge = true;
        bossMove.CanMove  = true;
        GetPlayer();
        Next();
    }

    private void OnDisable()
    {
        bossMove.ResetAll();
        bossMove.CanMove = true;
        if (dashFxObject != null) dashFxObject.SetActive(false);
        if (bossRenderer != null) bossRenderer.enabled = true;
        ClearLines(s2Lines);
        ClearLines(s3Lines);
        ClearSeekers();
    }

    // ── Setup ─────────────────────────────────────────────────────
    public void LoadData(
        EasyPoolingList hellfire, EasyPoolingList hellblast,
        EasyPoolingList condemn,  EasyPoolingList dart,
        EasyPoolingList darkSoul, EasyPoolingList bomb,
        List<float> hf, List<float> hb, List<float> cd,
        List<float> dt, List<float> ds, List<float> bm,
        List<float> dsh)
    {
        hellfirePool  = hellfire;
        hellblastPool = hellblast;
        condemnPool   = condemn;
        dartPool      = dart;
        darkSoulPool  = darkSoul;
        bombPool      = bomb;
        if (hf  != null) dmgHellfire  = hf;
        if (hb  != null) dmgHellblast = hb;
        if (cd  != null) dmgCondemn   = cd;
        if (dt  != null) dmgDart      = dt;
        if (ds  != null) dmgDarkSoul  = ds;
        if (bm  != null) dmgBomb      = bm;
        if (dsh != null) dmgDash      = dsh;
    }

    // ── Update ────────────────────────────────────────────────────
    private void Update()
    {
        switch (cur)
        {
            case S.Idle:          DoUpdateIdle();         break;
            case S.S1Dash:        DoUpdateS1Dash();       break;
            case S.S1Delay:       DoUpdateS1Delay();      break;
            case S.S2Spawn:       /* instant → S2Spawning */ break;
            case S.S2Spawning:    DoUpdateS2Spawning();   break;   // NEW
            case S.S2Side:        DoUpdateS2Side();       break;
            case S.S2Chain1Wait:  DoUpdateS2Chain1Wait(); break;
            case S.S2Chain2:      /* instant */           break;
            case S.S2ChainDelay:  DoUpdateS2ChainDelay(); break;
            case S.S3Teleport:    DoUpdateS3Teleport();   break;
            case S.S3Bomb:        /* instant → S3Warn */  break;
            case S.S3Warn:        DoUpdateS3Warn();       break;
            case S.S3Hellfire:    DoUpdateS3Hellfire();   break;
            case S.S3CycleDelay:  DoUpdateS3CycleDelay(); break;
        }
    }

    private void FixedUpdate()
    {
        if (cur == S.Idle || cur == S.S1Delay || cur == S.S2Side || cur == S.S2ChainDelay)
            DoUpdateOrbit();
    }

    // ── Enter ─────────────────────────────────────────────────────
    private void Enter(S state)
    {
        cur = state;
        switch (state)
        {
            case S.Idle:
                timer         = idleDuration;
                idleFireTimer = idleFireInterval;
                break;

            // ═════════════════════════════════════════════════════
            // SKILL 1
            // ═════════════════════════════════════════════════════
            case S.S1Dash:
                s1DashStart = transform.position;
                dashHits.Clear();

                DoFireRingAt(condemnPool, dmgCondemn, ringBulletCount, s1DashStart);

                if (playerTransform == null) GetPlayer();
                if (playerTransform != null)
                {
                    Vector2 dir       = ((Vector2)playerTransform.position - s1DashStart).normalized;
                    bossMove.MoveSnap = dir;
                    bossMove.MoveTo(dashVelocity, dashTime);
                    if (dashFxObject != null)
                    {
                        float a = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                        dashFxObject.transform.localRotation = Quaternion.Euler(0f, 0f, a);
                        dashFxObject.SetActive(true);
                    }
                }
                timer = dashTime;
                break;

            case S.S1Delay:
                bossMove.ResetAll(); 
                bossMove.CanMove = true; 
                if (dashFxObject != null) dashFxObject.SetActive(false);
                
                if (playerTransform != null)
                    orbitSide = transform.position.x >= playerTransform.position.x ? 1 : -1;
                
                timer = dashDelay;
                break;

            // ═════════════════════════════════════════════════════
            // SKILL 2
            // ═════════════════════════════════════════════════════
            case S.S2Spawn:
                ClearSeekers();
                s2RepeatLeft       = s2RepeatCount;
                s2SeekerSpawnIdx   = 0;
                s2SeekerSpawnTimer = 0f;    // spawn cái đầu ngay
                Enter(S.S2Spawning);
                return;

            case S.S2Spawning:
                // Không init gì — DoUpdateS2Spawning sẽ tự spawn theo interval
                break;

            case S.S2Side:
                timer = s2OrbitDuration;
                break;

            case S.S2Chain1Wait:
                DoFireS2HellBlast();
                DoShowS2DartLines();
                timer = s2DartWarnDuration;
                break;

            case S.S2Chain2:
                ClearLines(s2Lines);
                DoFireS2Darts();
                DoFireFan(hellfirePool, dmgHellfire, s2HellfireExtraPairs, s2HellfireAngleStep);
                Enter(S.S2ChainDelay);
                return;

            case S.S2ChainDelay:
                timer = s2ChainInterval;
                break;

            // ═════════════════════════════════════════════════════
            // SKILL 3
            // ═════════════════════════════════════════════════════
            case S.S3Teleport:
                s3CycleLeft    = s3RepeatCount;
                s3TeleportDone = false;
                s3TeleportDest = s3ArenaCenter;
                shadow?.PlayDepartShadow(transform.position);
                shadow?.PlayArriveShadow(s3TeleportDest);
                if (bossRenderer != null) bossRenderer.enabled = false;
                bossMove.ResetAll();
                bossMove.CanMove = false;
                timer = shadow != null ? Mathf.Max(0f, shadow.FadeDuration - 0.1f) : 0f;
                break;

            case S.S3Bomb:
                DoSpawnBombLine();
                Enter(S.S3Warn);
                return;

            case S.S3Warn:
                DoShowS3Lines();
                timer = s3WarnDuration;
                break;

            case S.S3Hellfire:
                ClearLines(s3Lines);
                DoFireRing(condemnPool, dmgCondemn, s3RingCount);
                s3HellfireTimer = s3HellfireDelay;
                break;

            case S.S3CycleDelay:
                timer = s3CycleDelay;
                break;
        }
    }

    private void Next()
    {
        rotIdx = (rotIdx + 1) % Rotation.Length;
        var next = Rotation[rotIdx];

        if (next == S.S1Dash) s1RepeatLeft = dashRepeatCount;

        Enter(next);
    }

    // ══════════════════════════════════════════════════════════════
    // IDLE
    // ══════════════════════════════════════════════════════════════
    private void DoUpdateIdle()
    {
        idleFireTimer -= DeltaTime;
        if (idleFireTimer <= 0f)
        {
            idleFireTimer = idleFireInterval;
            DoFireFan(hellfirePool, dmgHellfire, 0, 0f);
        }
        timer -= DeltaTime;
        if (timer <= 0f) Next();
    }

    private void DoUpdateOrbit()
    {
        if (playerTransform == null) return;
        float ox = (cur == S.S2Side || cur == S.S2ChainDelay) ? s2OrbitDistanceX : orbitDistanceX;
        float oy = (cur == S.S2Side || cur == S.S2ChainDelay) ? s2OrbitHeightY   : orbitHeightY;
        Vector2 target = (Vector2)playerTransform.position + new Vector2(ox * orbitSide, oy);
        Vector2 delta  = target - (Vector2)transform.position;
        float   dist   = delta.magnitude;
        if (dist < 0.05f) { bossMove.Rb.linearVelocity = Vector2.zero; return; }
        float speed = Mathf.Min(dist * 5f, orbitMoveSpeed);
        bossMove.Rb.linearVelocity = delta.normalized * speed;
    }

    // ══════════════════════════════════════════════════════════════
    // SKILL 1 — Dash Ring
    // ══════════════════════════════════════════════════════════════
    private void DoUpdateS1Dash()
    {
        if (hitbox != null)
        {
            var hits = hitbox.detectObject(playerLayer);
            if (hits != null)
                foreach (var hit in hits)
                {
                    var s = hit.GetComponent<Stat>();
                    if (s == null || s == bossStat || dashHits.Contains(s)) continue;
                    dashHits.Add(s);
                    s.TakeDamage(DamageType.Magic,
                        dmgDash.Count > 0 ? dmgDash[0] : 380f,
                        bossStat.CurCritRate, bossStat.CurCritDamage);
                }
        }

        timer -= DeltaTime;
        float dist  = Vector2.Distance(transform.position, s1DashStart);
        bool  tooFar = dist >= dashMaxDist;
        bool  timeUp = timer <= 0f;

        if (!tooFar && !timeUp) return;

        if (tooFar)
        {
            Vector2 offset = (Vector2)transform.position - s1DashStart;
            if (offset.sqrMagnitude > 0.0001f)
            {
                Vector2 clampedPos = s1DashStart + offset.normalized * dashMaxDist;
                transform.position = clampedPos;
            
                if (bossMove.Rb != null) bossMove.Rb.position = clampedPos;
            }
        }


        bossMove.ResetAll(); 
    
        if (dashFxObject != null) dashFxObject.SetActive(false);

        s1RepeatLeft--;
        if (s1RepeatLeft <= 0) Next();
        else Enter(S.S1Delay);
    }

    private void DoUpdateS1Delay()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S1Dash);
    }

    // ══════════════════════════════════════════════════════════════
    // SKILL 2 — Inferno Convergence
    // ══════════════════════════════════════════════════════════════

    private void DoUpdateS2Spawning()
    {
        s2SeekerSpawnTimer -= DeltaTime;
        if (s2SeekerSpawnTimer > 0f) return;

        if (s2SeekerSpawnIdx >= seekerCount)
        {
            // Spawn xong hết → bắt đầu orbit + chain
            orbitSide = bossMove.FlipDirect >= 0 ? 1 : -1;
            Enter(S.S2Side);
            return;
        }

        DoSpawnOneSeeker(s2SeekerSpawnIdx);
        s2SeekerSpawnIdx++;
        s2SeekerSpawnTimer = seekerSpawnInterval;
    }

    private void DoSpawnOneSeeker(int index)
    {
        if (seekerPrefab == null || playerTransform == null) return;
        if (seekerCount <= 0) return;

        float   step = 360f / seekerCount;
        float   a    = step * index * Mathf.Deg2Rad;
        Vector2 pos  = (Vector2)transform.position
                     + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * seekerSpawnRadius;
        var obj = Instantiate(seekerPrefab, pos, Quaternion.identity);
        var sk  = obj.GetComponent<CalmitasSoulSeekerSupreme>();
        if (sk != null) { sk.SetOrbitCenter(transform); sk.SetPlayer(playerTransform); }
        obj.SetActive(true);
        seekerList.Add(obj);
    }

    private void ClearSeekers()
    {
        foreach (var s in seekerList) if (s != null) s.SetActive(false);
        seekerList.Clear();
    }

    private void DoUpdateS2Side()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S2Chain1Wait);
    }

    private void DoUpdateS2Chain1Wait()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S2Chain2);
    }

    private void DoUpdateS2ChainDelay()
    {
        timer -= DeltaTime;
        if (timer > 0f) return;

        DoFireFan(darkSoulPool, dmgDarkSoul, s2DarkSoulExtraPairs, s2DarkSoulAngleStep);

        s2RepeatLeft--;
        if (s2RepeatLeft <= 0) { ClearSeekers(); Next(); return; }
        orbitSide *= -1;
        Enter(S.S2Side);
    }

    private void DoFireS2HellBlast()
    {
        if (playerTransform == null) return;
        Vector2 toPlayer = ((Vector2)playerTransform.position
                           - (Vector2)transform.position).normalized;
        Vector2 perp     = new Vector2(-toPlayer.y, toPlayer.x);
        float   angle    = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg;
        DoSpawnBullet(hellblastPool, dmgHellblast,
                      (Vector2)transform.position + perp * s2HellBlastOffset, angle);
        DoSpawnBullet(hellblastPool, dmgHellblast,
                      (Vector2)transform.position - perp * s2HellBlastOffset, angle);
    }

    // ════════════════════════════════════════════════════════════════
    //  - Vị trí: rải dọc trục VUÔNG GÓC toPlayer (qua player)
    //  - Fire direction: +toPlayer và −toPlayer (mỗi vị trí 2 viên, đối xứng)
    //  - Warning line cũng vẽ theo 2 hướng fire (cross xuyên qua player)
    // ════════════════════════════════════════════════════════════════
    private void DoShowS2DartLines()
    {
        if (playerTransform == null) return;
        Vector2 toPlayer = ((Vector2)playerTransform.position
                           - (Vector2)transform.position).normalized;

        // Lưu góc FIRE (dọc toPlayer), không còn là góc LINE nữa
        s2DartAngle = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg;

        // Trục rải đạn = vuông góc với toPlayer
        Vector2 lineDir  = new Vector2(-toPlayer.y, toPlayer.x);
        // 2 hướng fire: +toPlayer và −toPlayer
        Vector2 fireDir1 =  toPlayer;
        Vector2 fireDir2 = -toPlayer;

        float half  = (s2DartCount - 1) * s2DartSpacing * 0.5f;
        int   total = s2DartCount * 2;     // 2 warning line / vị trí

        EnsureLines(s2Lines, total, s2DartLineColor, s2DartLineWidth);
        for (int i = 0; i < s2DartCount; i++)
        {
            float   off = -half + i * s2DartSpacing;
            Vector2 pos = (Vector2)playerTransform.position + lineDir * off;

            // Warn +toPlayer
            s2Lines[i].SetPosition(0, pos);
            s2Lines[i].SetPosition(1, (Vector3)pos + (Vector3)(fireDir1 * s2DartLineLength));
            s2Lines[i].gameObject.SetActive(true);

            // Warn −toPlayer
            s2Lines[s2DartCount + i].SetPosition(0, pos);
            s2Lines[s2DartCount + i].SetPosition(1, (Vector3)pos + (Vector3)(fireDir2 * s2DartLineLength));
            s2Lines[s2DartCount + i].gameObject.SetActive(true);
        }
    }

    private void DoFireS2Darts()
    {
        if (playerTransform == null) return;

        Vector2 toPlayer = new Vector2(Mathf.Cos(s2DartAngle * Mathf.Deg2Rad),
                                       Mathf.Sin(s2DartAngle * Mathf.Deg2Rad));
        Vector2 lineDir  = new Vector2(-toPlayer.y, toPlayer.x);

        float a1 = s2DartAngle;           // +toPlayer
        float a2 = s2DartAngle + 180f;    // −toPlayer

        float half = (s2DartCount - 1) * s2DartSpacing * 0.5f;
        for (int i = 0; i < s2DartCount; i++)
        {
            float   off = -half + i * s2DartSpacing;
            Vector2 pos = (Vector2)playerTransform.position + lineDir * off;
            DoSpawnBullet(dartPool, dmgDart, pos, a1);
            DoSpawnBullet(dartPool, dmgDart, pos, a2);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // SKILL 3 — Damnation Cross
    // ══════════════════════════════════════════════════════════════
    private void DoUpdateS3Teleport()
    {
        timer -= DeltaTime;
        if (timer > 0f) return;
        if (!s3TeleportDone)
        {
            s3TeleportDone     = true;
            transform.position = s3TeleportDest;
            if (bossRenderer != null) bossRenderer.enabled = true;
        }
        Enter(S.S3Bomb);
    }

    private void DoSpawnBombLine()
    {
        if (playerTransform == null || bombPool == null) return;
        float   angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
        Vector2 dir   = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        float   half  = (s3BombCount - 1) * s3BombSpacing * 0.5f;
        for (int i = 0; i < s3BombCount; i++)
        {
            float   t   = -half + i * s3BombSpacing;
            Vector2 pos = (Vector2)playerTransform.position + dir * t;
            var     obj = bombPool.GetGameObject();
            if (obj == null) continue;
            obj.transform.position = pos;
            obj.SetActive(true);
            var b = obj.GetComponent<BulletObject>();
            if (b != null)
                b.SetUp(DamageType.Magic, dmgBomb, bossStat,
                        bossStat.CurCritRate, bossStat.CurCritDamage);
        }
    }

    private void DoShowS3Lines()
    {
        EnsureLines(s3Lines, s3RingCount, s3WarnLineColor, s3WarnLineWidth);
        float step = 360f / s3RingCount;
        for (int i = 0; i < s3RingCount; i++)
        {
            float   a   = step * i;
            Vector2 dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
            Vector3 sp  = transform.position;
            s3Lines[i].SetPosition(0, sp);
            s3Lines[i].SetPosition(1, sp + new Vector3(dir.x, dir.y) * s3WarnLineLength);
            s3Lines[i].gameObject.SetActive(true);
        }
    }

    private void DoUpdateS3Warn()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S3Hellfire);
    }

    private void DoUpdateS3Hellfire()
    {
        s3HellfireTimer -= DeltaTime;
        if (s3HellfireTimer > 0f) return;
        DoFireFan(hellfirePool, dmgHellfire, s3HellfireExtraPairs, s3HellfireAngleStep);
        s3CycleLeft--;
        if (s3CycleLeft <= 0) { bossMove.CanMove = true; Next(); }
        else Enter(S.S3CycleDelay);
    }

    private void DoUpdateS3CycleDelay()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S3Bomb);
    }

    // ══════════════════════════════════════════════════════════════
    // FIRE HELPERS
    // ══════════════════════════════════════════════════════════════
    private void DoFireFan(EasyPoolingList pool, List<float> dmg, int pairs, float step)
    {
        float base1 = GetAngleToPlayer();
        DoSpawnBullet(pool, dmg, transform.position, base1);
        for (int i = 1; i <= pairs; i++)
        {
            DoSpawnBullet(pool, dmg, transform.position, base1 + step * i);
            DoSpawnBullet(pool, dmg, transform.position, base1 - step * i);
        }
    }

    private void DoFireRing(EasyPoolingList pool, List<float> dmg, int count)
        => DoFireRingAt(pool, dmg, count, transform.position);

    private void DoFireRingAt(EasyPoolingList pool, List<float> dmg, int count, Vector2 origin)
    {
        if (count <= 0) return;
        float step = 360f / count;
        for (int i = 0; i < count; i++)
            DoSpawnBullet(pool, dmg, origin, step * i);
    }

    private void DoSpawnBullet(EasyPoolingList pool, List<float> dmg, Vector2 pos, float angle)
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

    private float GetAngleToPlayer()
    {
        if (playerTransform == null) return 0f;
        Vector2 d = ((Vector2)playerTransform.position
                    - (Vector2)transform.position).normalized;
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

    private void GetPlayer()
    {
        var go = GameObject.FindWithTag("Player");
        if (go != null) playerTransform = go.transform;
    }

    // ══════════════════════════════════════════════════════════════
    // LINE HELPERS
    // ══════════════════════════════════════════════════════════════
    private void EnsureLines(List<LineRenderer> list, int count, Color color, float width)
    {
        while (list.Count < count)
        {
            var go = new GameObject($"P3Line_{list.Count}");
            go.transform.SetParent(null);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount    = 2;
            lr.startWidth       = width;
            lr.endWidth         = width;
            lr.useWorldSpace    = true;
            lr.sortingLayerName = "Default";
            lr.sortingOrder     = 10;
            lr.material         = new Material(
                Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default"));
            lr.startColor = color;
            lr.endColor   = color;
            go.SetActive(false);
            list.Add(lr);
        }
        for (int i = count; i < list.Count; i++)
            list[i].gameObject.SetActive(false);
    }

    private void ClearLines(List<LineRenderer> list)
    {
        foreach (var lr in list)
            if (lr != null) lr.gameObject.SetActive(false);
    }

    // ══════════════════════════════════════════════════════════════
    // GIZMOS
    // ══════════════════════════════════════════════════════════════
    private void OnDrawGizmos()
    {
        Vector3 bp = transform.position;
        Vector3 pp = playerTransform != null ? playerTransform.position : bp;

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireSphere(pp + new Vector3( orbitDistanceX, orbitHeightY, 0f), 0.3f);
        Gizmos.DrawWireSphere(pp + new Vector3(-orbitDistanceX, orbitHeightY, 0f), 0.3f);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
        Gizmos.DrawWireSphere(pp + new Vector3( s2OrbitDistanceX, s2OrbitHeightY, 0f), 0.3f);
        Gizmos.DrawWireSphere(pp + new Vector3(-s2OrbitDistanceX, s2OrbitHeightY, 0f), 0.3f);

        Gizmos.color = new Color(1f, 0f, 0.5f, 0.4f);
        Gizmos.DrawWireSphere(bp, seekerSpawnRadius);

        Gizmos.color = new Color(0.6f, 0.2f, 1f, 0.8f);
        Gizmos.DrawWireSphere(new Vector3(s3ArenaCenter.x, s3ArenaCenter.y, 0f), 0.5f);

        // Dash max distance (S1)
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(bp, dashMaxDist);
    }
}