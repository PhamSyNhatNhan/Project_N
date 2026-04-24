using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sub Phase 3 — Chaos layer chạy song song CalamitasSkillPhase3.
/// 3 skill · mỗi skill 4 biến thể (30/30/30/10%) · pick random, không lặp skill · 20% skip.
/// Timer = variantCooldown tính từ khi skill kết thúc.
/// </summary>
public class CalamitasSkillSubPhase3 : MonoBehaviour
{
    // ── Variant Cooldowns ─────────────────────────────────────────
    [Header("Variant Cooldowns")]
    [SerializeField] private float s1V1Cd = 2f;
    [SerializeField] private float s1V2Cd = 3.5f;
    [SerializeField] private float s1V3Cd = 2.5f;
    [SerializeField] private float s1V4Cd = 4.5f;
    [SerializeField] private float s2V1Cd = 2f;
    [SerializeField] private float s2V2Cd = 2.5f;
    [SerializeField] private float s2V3Cd = 3f;
    [SerializeField] private float s2V4Cd = 4.5f;
    [SerializeField] private float s3V1Cd = 2.5f;
    [SerializeField] private float s3V2Cd = 3f;
    [SerializeField] private float s3V3Cd = 3.5f;
    [SerializeField] private float s3V4Cd = 4.5f;

    // ── Pick ──────────────────────────────────────────────────────
    [Header("Pick")]
    [SerializeField] private float            initialDelay = 3f;
    [SerializeField] private float            skipDuration = 2f;
    [SerializeField] [Range(0f, 1f)] private float skipChance  = 0.2f;
    [SerializeField] [Range(0f, 1f)] private float v4Chance    = 0.1f;

    // ── Skill 1 — Shared ──────────────────────────────────────────
    [Header("Skill 1 — Shared Warn")]
    [SerializeField] private float s1WarnDuration = 1f;
    [SerializeField] private float s1LineLength   = 24f;
    [SerializeField] private float s1WarnWidth    = 0.03f;
    [SerializeField] private Color s1WarnColor    = new Color(1f, 0.3f, 0f, 0.9f);

    [Header("Skill 1 V1/V3 — Parallel Lines")]
    [SerializeField] private int   s1LineCount   = 4;
    [SerializeField] private float s1LineSpacing = 1.2f;

    [Header("Skill 1 V1 — Repeat")]
    [SerializeField] private float s1V1RepeatDelay = 0.5f;

    [Header("Skill 1 V2 — Sniper Accum")]
    [SerializeField] private int   s1V2MaxLines     = 10;
    [SerializeField] private float s1V2Interval     = 0.1f;
    [SerializeField] private float s1V2WaitDuration = 1.5f;
    [SerializeField] private float s1V2LineLength   = 24f;

    [Header("Skill 1 V3 — Side Sweep")]
    [SerializeField] private int   s1V3LineCount  = 5;
    [SerializeField] private float s1V3SpacingY   = 1.2f;
    [SerializeField] private float s1V3OffsetX    = 24f;
    [SerializeField] private float s1V3LineLength = 48f; // centered at player, cần phủ từ head qua player

    [Header("Skill 1 V4 — Polygon")]
    [SerializeField] private int   s1V4Points    = 12;
    [SerializeField] private float s1V4RadMin    = 3f;
    [SerializeField] private float s1V4RadMax    = 5f;
    [SerializeField] private float s1V4WarnLen   = 8f;

    // ── Skill 2 — Shared ──────────────────────────────────────────
    [Header("Skill 2 — Shared Warn")]
    [SerializeField] private int   s2DartCount    = 6;
    [SerializeField] private float s2DartSpacing  = 0.8f;
    [SerializeField] private float s2WarnDuration = 1f;
    [SerializeField] private float s2WarnWidth    = 0.03f;
    [SerializeField] private Color s2WarnColor    = new Color(0.2f, 0.6f, 1f, 0.9f);

    [Header("Skill 2 V3/V4 — Square")]
    [SerializeField] private float s2SquareOffset = 3f;
    [SerializeField] private float s2V4DelayB     = 1f;

    // ── Skill 3 ───────────────────────────────────────────────────
    [Header("Skill 3 — Strip")]
    [SerializeField] private int   s3StripCount   = 7;
    [SerializeField] private float s3StripSpacing = 1.5f;

    [Header("Skill 3 V3 — Scatter")]
    [SerializeField] private int   s3ScatterCount  = 6;
    [SerializeField] private float s3ScatterWait   = 1f;
    [SerializeField] private float s3ScatterRadMin = 1f;
    [SerializeField] private float s3ScatterRadMax = 5f;

    [Header("Skill 3 V4 — Triple Strip")]
    [SerializeField] private float s3V4StripDelay = 1f;

    [Header("Gizmo — Scatter Zone")]
    [SerializeField] private Vector2 s3ScatterCenter = Vector2.zero;

    // ── Components ────────────────────────────────────────────────
    private Stat                 bossStat;
    private TimeScale            timeScale;
    private CalamitasSkillPhase3 phase3;
    private Transform            playerTransform;

    // ── Pools & Damage ────────────────────────────────────────────
    private EasyPoolingList daggerPool;
    private EasyPoolingList dartPool;
    private EasyPoolingList bombPool;

    private List<float> dmgDagger = new List<float> { 220f };
    private List<float> dmgDart   = new List<float> { 260f };
    private List<float> dmgBomb   = new List<float> { 400f };

    // ── Shared material cho tất cả LineRenderer (tránh leak) ──────
    private static Material s_lineMaterial;
    private static Material GetSharedLineMaterial()
    {
        if (s_lineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader != null)
            {
                s_lineMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }
        return s_lineMaterial;
    }

    // ── FSM ───────────────────────────────────────────────────────
    private enum S
    {
        Idle,
        S1V1Warn, S1V1Delay,          // V1: parallel ×2
        S1V2Accum, S1V2Wait,           // V2: sniper accumulate
        S1V3Warn,                      // V3: right→left sweep (s1V3SideLeft tracks side)
        S1V4Warn,                      // V4: polygon
        S2Warn,                        // V1/V2/V3: warn → fire
        S2V4WarnA, S2V4WaitB, S2V4WarnB, // V4: two groups delayed
        S3Wait,                        // V3/V4: wait between batches
    }

    private enum Skill { None, S1, S2, S3 }

    private S     cur;
    private Skill curSkill;
    private Skill lastSkill;
    private int   curVariant;    // 1–4
    private float timer;

    // ── Pattern data ──────────────────────────────────────────────
    private struct PatternShot
    {
        public Vector2 pos;      // CENTER of warning line (for rendering centered)
        public Vector2 firePos;  // HEAD — where dagger actually spawns
        public float   angle;    // hướng bay của dagger
        // center ≠ firePos: V1 parallel (center = player+perpOffset, firePos = head end)
        //                   V3 side sweep (center = player, firePos = head)
        public PatternShot(Vector2 center, Vector2 fire, float a)
            { pos = center; firePos = fire; angle = a; }
        // center == firePos: V4 polygon (head IS the stored position)
        public PatternShot(Vector2 p, float a)
            { pos = p; firePos = p; angle = a; }
    }

    // Skill 2: line described by (center, direction-angle); darts fire perpendicular
    private struct S2Line
    {
        public Vector2 center;
        public float   angle;
    }

    // S1 shot list + line renderers
    private readonly List<PatternShot>  s1Shots  = new List<PatternShot>();
    private readonly List<LineRenderer> s1LRs    = new List<LineRenderer>();

    // S2 line data + warn renderers (group A and B for V4)
    private readonly List<S2Line>       s2LinesA = new List<S2Line>();
    private readonly List<S2Line>       s2LinesB = new List<S2Line>();
    private readonly List<LineRenderer> s2WarnA  = new List<LineRenderer>();
    private readonly List<LineRenderer> s2WarnB  = new List<LineRenderer>();

    // ── Skill-specific state ──────────────────────────────────────
    private int   s1V1RepeatLeft;
    private float s1V1Angle;

    private float s1V2AccumTimer;
    private int   s1V2AccumDone;

    private int   s1V3SideLeft;    // 2 = right pending, 1 = left pending

    private int   s3BatchLeft;

    private float DeltaTime => timeScale.DeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        bossStat  = GetComponent<Stat>();
        timeScale = GetComponent<TimeScale>();
        phase3    = GetComponent<CalamitasSkillPhase3>();
    }

    private void OnEnable()
    {
        GetPlayer();
        lastSkill = Skill.None;
        timer     = initialDelay;
        cur       = S.Idle;
    }

    private void OnDisable()
    {
        ClearLines(s1LRs);
        ClearLines(s2WarnA);
        ClearLines(s2WarnB);
        s1Shots.Clear();
        s2LinesA.Clear();
        s2LinesB.Clear();
    }

    // ── Setup ─────────────────────────────────────────────────────
    public void LoadData(
        EasyPoolingList dagger, EasyPoolingList dart, EasyPoolingList bomb,
        List<float> dag, List<float> dt, List<float> bm)
    {
        daggerPool = dagger;
        dartPool   = dart;
        bombPool   = bomb;
        if (dag != null) dmgDagger = dag;
        if (dt  != null) dmgDart   = dt;
        if (bm  != null) dmgBomb   = bm;
    }

    // ── Update ────────────────────────────────────────────────────
    private void Update()
    {
        switch (cur)
        {
            case S.Idle:        UpdateIdle();       break;
            case S.S1V1Warn:    UpdateS1V1Warn();   break;
            case S.S1V1Delay:   UpdateS1V1Delay();  break;
            case S.S1V2Accum:   UpdateS1V2Accum();  break;
            case S.S1V2Wait:    UpdateS1V2Wait();   break;
            case S.S1V3Warn:    UpdateS1V3Warn();   break;
            case S.S1V4Warn:    UpdateS1V4Warn();   break;
            case S.S2Warn:      UpdateS2Warn();     break;
            case S.S2V4WarnA:   UpdateS2V4WarnA();  break;
            case S.S2V4WaitB:   UpdateS2V4WaitB();  break;
            case S.S2V4WarnB:   UpdateS2V4WarnB();  break;
            case S.S3Wait:      UpdateS3Wait();     break;
        }
    }

    // ── Enter ─────────────────────────────────────────────────────
    private void Enter(S state)
    {
        cur = state;
        switch (state)
        {
            case S.Idle:
                // timer set by FinishSkill / OnEnable
                break;

            // ════════════════════════════════════════════════════
            // SKILL 1 V1 — Parallel lines, 2 repeats
            // ════════════════════════════════════════════════════
            case S.S1V1Warn:
                PrepareS1Parallel(s1V1Angle, s1LineCount, s1LineSpacing);
                ShowS1Lines(s1LineLength);
                timer = s1WarnDuration;
                break;

            case S.S1V1Delay:
                ClearLines(s1LRs);
                s1Shots.Clear();
                s1V1Angle = Random.Range(0f, 360f);
                timer     = s1V1RepeatDelay;
                break;

            // ════════════════════════════════════════════════════
            // SKILL 1 V2 — Sniper accumulate
            // ════════════════════════════════════════════════════
            case S.S1V2Accum:
                s1Shots.Clear();
                ClearLines(s1LRs);
                s1V2AccumTimer = 0f;    // spawn first line immediately
                s1V2AccumDone  = 0;
                break;

            case S.S1V2Wait:
                timer = s1V2WaitDuration;
                break;

            // ════════════════════════════════════════════════════
            // SKILL 1 V3 — Side sweep
            // ════════════════════════════════════════════════════
            case S.S1V3Warn:
                PrepareS1V3(rightSide: s1V3SideLeft == 2);
                ShowS1Lines(s1V3LineLength);
                timer = s1WarnDuration;
                break;

            // ════════════════════════════════════════════════════
            // SKILL 1 V4 — Polygon
            // ════════════════════════════════════════════════════
            case S.S1V4Warn:
                PrepareS1V4Polygon();
                ShowS1Lines(s1V4WarnLen);
                timer = s1WarnDuration;
                break;

            // ════════════════════════════════════════════════════
            // SKILL 2 V1/V2/V3 — Single warn → fire
            // ════════════════════════════════════════════════════
            case S.S2Warn:
                PrepareS2(curVariant);
                ShowS2WarnLines(s2LinesA, s2WarnA);
                timer = s2WarnDuration;
                break;

            // ════════════════════════════════════════════════════
            // SKILL 2 V4 — Square then Diamond (delayed)
            // ════════════════════════════════════════════════════
            case S.S2V4WarnA:
                PrepareS2Square();          // fills both s2LinesA (square) and s2LinesB (diamond)
                ShowS2WarnLines(s2LinesA, s2WarnA);
                timer = s2WarnDuration;
                break;

            case S.S2V4WaitB:
                timer = s2V4DelayB;
                break;

            case S.S2V4WarnB:
                ShowS2WarnLines(s2LinesB, s2WarnB);  // s2LinesB already prepared in WarnA
                timer = s2WarnDuration;
                break;

            // ════════════════════════════════════════════════════
            // SKILL 3 — Wait between batches
            // ════════════════════════════════════════════════════
            case S.S3Wait:
                timer = curVariant == 3 ? s3ScatterWait : s3V4StripDelay;
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // IDLE / PICK
    // ══════════════════════════════════════════════════════════════
    private void UpdateIdle()
    {
        timer -= DeltaTime;
        if (timer > 0f) return;

        // 20% chance: skip this window
        if (Random.value < skipChance)
        {
            timer = skipDuration;
            return;
        }

        curSkill   = PickSkill();
        lastSkill  = curSkill;
        curVariant = PickVariant();
        LaunchSkill();
    }

    private Skill PickSkill()
    {
        var pool = new List<Skill> { Skill.S1, Skill.S2, Skill.S3 };
        if (lastSkill != Skill.None) pool.Remove(lastSkill);
        return pool[Random.Range(0, pool.Count)];
    }

    private int PickVariant()
    {
        // 10% V4, 30% each V1/V2/V3
        float r    = Random.value;
        float each = (1f - v4Chance) / 3f;
        if (r < v4Chance)              return 4;
        if (r < v4Chance + each)       return 3;
        if (r < v4Chance + each * 2f)  return 2;
        return 1;
    }

    private void LaunchSkill()
    {
        switch (curSkill)
        {
            case Skill.S1:
                switch (curVariant)
                {
                    case 1:
                        s1V1RepeatLeft = 2;
                        s1V1Angle      = Random.Range(0f, 360f);
                        Enter(S.S1V1Warn);
                        break;
                    case 2:
                        Enter(S.S1V2Accum);
                        break;
                    case 3:
                        s1V3SideLeft = 2;           // right first
                        Enter(S.S1V3Warn);
                        break;
                    case 4:
                        Enter(S.S1V4Warn);
                        break;
                }
                break;

            case Skill.S2:
                Enter(curVariant == 4 ? S.S2V4WarnA : S.S2Warn);
                break;

            case Skill.S3:
                FireS3Batch();
                switch (curVariant)
                {
                    case 1:
                    case 2:
                        FinishSkill();
                        break;
                    case 3:
                        s3BatchLeft = 1;    // 1 more batch of scatter
                        Enter(S.S3Wait);
                        break;
                    case 4:
                        s3BatchLeft = 2;    // 2 more strips
                        Enter(S.S3Wait);
                        break;
                }
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // SKILL 1 UPDATES
    // ══════════════════════════════════════════════════════════════
    private void UpdateS1V1Warn()
    {
        timer -= DeltaTime;
        if (timer > 0f) return;
        FireS1Daggers();
        s1V1RepeatLeft--;
        if (s1V1RepeatLeft <= 0) FinishSkill();
        else Enter(S.S1V1Delay);
    }

    private void UpdateS1V1Delay()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S1V1Warn);
    }

    private void UpdateS1V2Accum()
    {
        s1V2AccumTimer -= DeltaTime;
        if (s1V2AccumTimer > 0f) return;
        if (playerTransform == null) { GetPlayer(); return; }

        Vector2 bossPos    = transform.position;
        Vector2 playerPos  = playerTransform.position;
        float   angle      = Mathf.Atan2(playerPos.y - bossPos.y,
                                         playerPos.x - bossPos.x) * Mathf.Rad2Deg;

        s1Shots.Add(new PatternShot(bossPos, angle));

        // Show this line incrementally — V2 dùng line 1 chiều (từ boss đến hướng player)
        EnsureLines(s1LRs, s1V2AccumDone + 1, s1WarnColor, s1WarnWidth);
        SetLineFrom(s1LRs[s1V2AccumDone], bossPos, angle, s1V2LineLength);
        s1LRs[s1V2AccumDone].gameObject.SetActive(true);

        s1V2AccumDone++;
        s1V2AccumTimer = s1V2Interval;

        if (s1V2AccumDone >= s1V2MaxLines) Enter(S.S1V2Wait);
    }

    private void UpdateS1V2Wait()
    {
        timer -= DeltaTime;
        if (timer > 0f) return;
        FireS1Daggers();
        FinishSkill();
    }

    private void UpdateS1V3Warn()
    {
        timer -= DeltaTime;
        if (timer > 0f) return;
        FireS1Daggers();
        s1V3SideLeft--;
        if (s1V3SideLeft <= 0) FinishSkill();
        else Enter(S.S1V3Warn);
    }

    private void UpdateS1V4Warn()
    {
        timer -= DeltaTime;
        if (timer > 0f) return;
        FireS1Daggers();
        FinishSkill();
    }

    // ══════════════════════════════════════════════════════════════
    // SKILL 2 UPDATES
    // ══════════════════════════════════════════════════════════════
    private void UpdateS2Warn()
    {
        timer -= DeltaTime;
        if (timer > 0f) return;
        ClearLines(s2WarnA);
        FireS2Darts(s2LinesA);
        FinishSkill();
    }

    private void UpdateS2V4WarnA()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S2V4WaitB);
    }

    private void UpdateS2V4WaitB()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S2V4WarnB);
    }

    private void UpdateS2V4WarnB()
    {
        timer -= DeltaTime;
        if (timer > 0f) return;
        ClearLines(s2WarnA);
        ClearLines(s2WarnB);
        FireS2Darts(s2LinesA);
        FireS2Darts(s2LinesB);
        FinishSkill();
    }

    // ══════════════════════════════════════════════════════════════
    // SKILL 3 UPDATE
    // ══════════════════════════════════════════════════════════════
    private void UpdateS3Wait()
    {
        timer -= DeltaTime;
        if (timer > 0f) return;
        FireS3Batch();
        s3BatchLeft--;
        if (s3BatchLeft <= 0) FinishSkill();
        else timer = curVariant == 3 ? s3ScatterWait : s3V4StripDelay;
        // stay in S3Wait
    }

    // ── Finish ────────────────────────────────────────────────────
    private void FinishSkill()
    {
        ClearLines(s1LRs);
        ClearLines(s2WarnA);
        ClearLines(s2WarnB);
        s1Shots.Clear();
        s2LinesA.Clear();
        s2LinesB.Clear();
        timer = GetVariantCooldown();
        cur   = S.Idle;
    }

    private float GetVariantCooldown() => (curSkill, curVariant) switch
    {
        (Skill.S1, 1) => s1V1Cd,
        (Skill.S1, 2) => s1V2Cd,
        (Skill.S1, 3) => s1V3Cd,
        (Skill.S1, 4) => s1V4Cd,
        (Skill.S2, 1) => s2V1Cd,
        (Skill.S2, 2) => s2V2Cd,
        (Skill.S2, 3) => s2V3Cd,
        (Skill.S2, 4) => s2V4Cd,
        (Skill.S3, 1) => s3V1Cd,
        (Skill.S3, 2) => s3V2Cd,
        (Skill.S3, 3) => s3V3Cd,
        (Skill.S3, 4) => s3V4Cd,
        _             => 2f,
    };

    // ══════════════════════════════════════════════════════════════
    // PREPARE — SKILL 1
    // ══════════════════════════════════════════════════════════════

    /// V1: N lines song song, line giữa ngắm player, hướng = fireAngle
    /// pos = center của line (= player + perpOffset), firePos = head (đầu xa)
    /// Dagger spawn tại head, bay theo (fireAngle + 180°) để chạy DỌC warning line
    /// về phía đuôi (xuyên qua vị trí player ở giữa line).
    private void PrepareS1Parallel(float fireAngle, int count, float spacing)
    {
        s1Shots.Clear();
        if (playerTransform == null) return;

        Vector2 fireDir = new Vector2(Mathf.Cos(fireAngle * Mathf.Deg2Rad),
                                      Mathf.Sin(fireAngle * Mathf.Deg2Rad));
        Vector2 perpDir = new Vector2(-fireDir.y, fireDir.x);
        Vector2 player  = playerTransform.position;
        float   perpHalf = (count - 1) * spacing * 0.5f;
        float   lineHalf = s1LineLength * 0.5f;
        float   flyAngle = fireAngle + 180f;   // FIX: bay từ head về đuôi

        for (int i = 0; i < count; i++)
        {
            float   off        = -perpHalf + i * spacing;
            Vector2 lineCenter = player + perpDir * off;          // center of this line
            Vector2 head       = lineCenter + fireDir * lineHalf; // dagger fires from head end
            s1Shots.Add(new PatternShot(lineCenter, head, flyAngle));
        }
    }

    /// V3: heads xếp trên trục X offset từ player, mỗi line nhắm thẳng vào player lúc spawn.
    /// center của warning line = player (để warning hiển thị hội tụ từ player),
    /// firePos = head (dagger spawn tại đây, bay về phía player theo angle đã tính).
    private void PrepareS1V3(bool rightSide)
    {
        s1Shots.Clear();
        ClearLines(s1LRs);
        if (playerTransform == null) return;

        Vector2 playerPos = playerTransform.position;
        float   headX     = playerPos.x + (rightSide ? s1V3OffsetX : -s1V3OffsetX);
        float   half      = (s1V3LineCount - 1) * s1V3SpacingY * 0.5f;

        for (int i = 0; i < s1V3LineCount; i++)
        {
            // Xếp từ trên xuống: i=0 → đỉnh
            float   headY = playerPos.y + (half - i * s1V3SpacingY);
            Vector2 head  = new Vector2(headX, headY);
            Vector2 toP   = ((Vector2)playerPos - head).normalized;
            float   angle = Mathf.Atan2(toP.y, toP.x) * Mathf.Rad2Deg;
            // center = playerPos (warning hội tụ tại player), firePos = head
            s1Shots.Add(new PatternShot(playerPos, head, angle));
        }
    }

    /// V4: đa giác N cạnh (tham chiếu Immortal1 PrepareDecagon)
    /// Mỗi cặp đỉnh bắn 2 dagger đối nhau — dagger bắn từ đỉnh về phía đỉnh kia
    private void PrepareS1V4Polygon()
    {
        s1Shots.Clear();
        if (playerTransform == null) return;

        Vector2   center    = playerTransform.position;
        float     radius    = Random.Range(s1V4RadMin, s1V4RadMax);
        float     baseAngle = Random.Range(0f, 360f);
        float     step      = 360f / s1V4Points;
        Vector2[] pts       = new Vector2[s1V4Points];

        for (int i = 0; i < s1V4Points; i++)
        {
            float a = (baseAngle + step * i) * Mathf.Deg2Rad;
            pts[i]  = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        // Mỗi cặp đỉnh (i,j) → 2 shot bắn ngược chiều dọc trục i→j
        // pos = firePos = vertex (head chính là đỉnh đa giác)
        for (int i = 0; i < s1V4Points; i++)
        {
            for (int j = i + 1; j < s1V4Points; j++)
            {
                Vector2 dir = (pts[j] - pts[i]).normalized;
                float   a   = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                s1Shots.Add(new PatternShot(pts[i], a));
                s1Shots.Add(new PatternShot(pts[j], a + 180f));
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // PREPARE — SKILL 2
    // ══════════════════════════════════════════════════════════════

    private void PrepareS2(int variant)
    {
        s2LinesA.Clear();
        if (playerTransform == null) return;
        Vector2 pp = playerTransform.position;

        switch (variant)
        {
            case 1: // 1 line, tâm = player, random angle
                s2LinesA.Add(new S2Line { center = pp, angle = Random.Range(0f, 360f) });
                break;

            case 2: // 2 line giao nhau tại player, góc cách nhau 30–150°
            {
                float a1 = Random.Range(0f, 180f);
                float a2 = a1 + Random.Range(30f, 150f);
                s2LinesA.Add(new S2Line { center = pp, angle = a1 });
                s2LinesA.Add(new S2Line { center = pp, angle = a2 });
                break;
            }

            case 3: // 4 line hình vuông — dùng chung logic với V4 nhưng chỉ lấy group A
                PrepareS2Square();
                break;
        }
    }

    /// Tạo 4 tâm hình vuông (s2LinesA) và 4 tâm hình kim cương (s2LinesB) quanh player.
    /// Mỗi line vuông góc với trục hướng tâm của nó (tangent với hình) → dart bắn vào trong/ngoài.
    private void PrepareS2Square()
    {
        s2LinesA.Clear();
        s2LinesB.Clear();
        if (playerTransform == null) return;

        Vector2 pp        = playerTransform.position;
        float   squareRot = Random.Range(0f, 360f);

        // Group A: hình vuông — 4 hướng cách đều 90°
        // lineAngle = radialAngle + 90° → tangent với hình vuông
        for (int i = 0; i < 4; i++)
        {
            float   radial = squareRot + 90f * i;
            float   a      = radial * Mathf.Deg2Rad;
            Vector2 center = pp + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s2SquareOffset;
            s2LinesA.Add(new S2Line { center = center, angle = radial + 90f });
        }

        // Group B: hình kim cương — xoay 45° so với group A
        for (int i = 0; i < 4; i++)
        {
            float   radial = squareRot + 45f + 90f * i;
            float   a      = radial * Mathf.Deg2Rad;
            Vector2 center = pp + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s2SquareOffset;
            s2LinesB.Add(new S2Line { center = center, angle = radial + 90f });
        }
    }

    // ══════════════════════════════════════════════════════════════
    // FIRE — SKILL 1
    // ══════════════════════════════════════════════════════════════
    private void FireS1Daggers()
    {
        foreach (var shot in s1Shots)
            SpawnBullet(daggerPool, dmgDagger, shot.firePos, shot.angle);
        ClearLines(s1LRs);
        s1Shots.Clear();
    }

    // ══════════════════════════════════════════════════════════════
    // FIRE — SKILL 2
    // ══════════════════════════════════════════════════════════════
    private void FireS2Darts(List<S2Line> lines)
    {
        float half = (s2DartCount - 1) * s2DartSpacing * 0.5f;
        foreach (var line in lines)
        {
            Vector2 lineDir = new Vector2(Mathf.Cos(line.angle * Mathf.Deg2Rad),
                                          Mathf.Sin(line.angle * Mathf.Deg2Rad));
            float fireA = line.angle + 90f;   // vuông góc chiều 1
            float fireB = line.angle - 90f;   // vuông góc chiều 2

            for (int i = 0; i < s2DartCount; i++)
            {
                float   off = -half + i * s2DartSpacing;
                Vector2 pos = line.center + lineDir * off;
                SpawnBullet(dartPool, dmgDart, pos, fireA);
                SpawnBullet(dartPool, dmgDart, pos, fireB);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // FIRE — SKILL 3
    // ══════════════════════════════════════════════════════════════
    private void FireS3Batch()
    {
        switch (curVariant)
        {
            case 1:
                FireS3Strip(Random.Range(0f, 360f));
                break;

            case 2:
            {
                float a = Random.Range(0f, 180f);
                FireS3Strip(a);
                FireS3Strip(a + 90f);   // chéo vuông góc
                break;
            }

            case 3:
                FireS3Scatter();
                break;

            case 4:
                FireS3Strip(Random.Range(0f, 360f));   // tâm = player lúc batch này
                break;
        }
    }

    /// Dải bomb thẳng: tâm = player hiện tại, hướng random truyền vào
    private void FireS3Strip(float angle)
    {
        if (playerTransform == null || bombPool == null) return;
        Vector2 center = playerTransform.position;
        Vector2 dir    = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad),
                                     Mathf.Sin(angle * Mathf.Deg2Rad));
        float half = (s3StripCount - 1) * s3StripSpacing * 0.5f;
        for (int i = 0; i < s3StripCount; i++)
        {
            float   t   = -half + i * s3StripSpacing;
            Vector2 pos = center + dir * t;
            SpawnBomb(pos);
        }
    }

    /// V3: N bomb spawn ngẫu nhiên trong vùng cố định trên arena
    private void FireS3Scatter()
    {
        for (int i = 0; i < s3ScatterCount; i++)
        {
            float   a = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float   r = Random.Range(s3ScatterRadMin, s3ScatterRadMax);
            Vector2 pos = s3ScatterCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            SpawnBomb(pos);
        }
    }

    private void SpawnBomb(Vector2 pos)
    {
        if (bombPool == null) return;
        var obj = bombPool.GetGameObject();
        if (obj == null) return;
        obj.transform.position = pos;
        obj.SetActive(true);
        var b = obj.GetComponent<BulletObject>();
        if (b != null)
            b.SetUp(DamageType.Magic, dmgBomb, bossStat,
                    bossStat.CurCritRate, bossStat.CurCritDamage);
    }

    // ══════════════════════════════════════════════════════════════
    // BULLET SPAWN
    // ══════════════════════════════════════════════════════════════
    private void SpawnBullet(EasyPoolingList pool, List<float> dmg, Vector2 pos, float angle)
    {
        if (pool == null) return;
        var obj = pool.GetGameObject();
        if (obj == null) return;
        obj.transform.position = pos;
        obj.SetActive(true);
        var b = obj.GetComponent<BulletObject>();
        if (b == null) return;
        b.SetUp(DamageType.Magic, dmg, bossStat, bossStat.CurCritRate, bossStat.CurCritDamage);
        b.EasyModeChange(BulletMoveMode.Angle, angle);
    }

    // ══════════════════════════════════════════════════════════════
    // LINE RENDERER HELPERS
    // ══════════════════════════════════════════════════════════════

    /// Hiển thị S1 warning lines: centered tại shot.pos, kéo dài len về 2 phía theo shot.angle
    private void ShowS1Lines(float len)
    {
        EnsureLines(s1LRs, s1Shots.Count, s1WarnColor, s1WarnWidth);
        for (int i = 0; i < s1Shots.Count; i++)
        {
            SetLine(s1LRs[i], s1Shots[i].pos, s1Shots[i].angle, len);
            s1LRs[i].gameObject.SetActive(true);
        }
        for (int i = s1Shots.Count; i < s1LRs.Count; i++)
            s1LRs[i].gameObject.SetActive(false);
    }

    /// Hiển thị S2 warning lines: vẽ đường thẳng qua center theo lineAngle
    private void ShowS2WarnLines(List<S2Line> lineData, List<LineRenderer> lrs)
    {
        float halfLen = (s2DartCount - 1) * s2DartSpacing * 0.5f;
        EnsureLines(lrs, lineData.Count, s2WarnColor, s2WarnWidth);
        for (int i = 0; i < lineData.Count; i++)
        {
            var     ld  = lineData[i];
            Vector2 dir = new Vector2(Mathf.Cos(ld.angle * Mathf.Deg2Rad),
                                      Mathf.Sin(ld.angle * Mathf.Deg2Rad));
            lrs[i].SetPosition(0, (Vector3)ld.center - new Vector3(dir.x, dir.y) * halfLen);
            lrs[i].SetPosition(1, (Vector3)ld.center + new Vector3(dir.x, dir.y) * halfLen);
            lrs[i].gameObject.SetActive(true);
        }
        for (int i = lineData.Count; i < lrs.Count; i++)
            lrs[i].gameObject.SetActive(false);
    }

    /// Vẽ line CENTERED tại pos — kéo dài halfLen về 2 phía theo angle
    private void SetLine(LineRenderer lr, Vector2 pos, float angle, float len)
    {
        Vector2 dir     = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        float   halfLen = len * 0.5f;
        lr.SetPosition(0, (Vector3)pos - new Vector3(dir.x, dir.y) * halfLen);
        lr.SetPosition(1, (Vector3)pos + new Vector3(dir.x, dir.y) * halfLen);
    }

    /// Vẽ line 1 chiều: từ from kéo dài len theo angle (dùng cho V2 sniper)
    private void SetLineFrom(LineRenderer lr, Vector2 from, float angle, float len)
    {
        Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        lr.SetPosition(0, from);
        lr.SetPosition(1, (Vector3)from + new Vector3(dir.x, dir.y) * len);
    }

    private void EnsureLines(List<LineRenderer> list, int count, Color color, float width)
    {
        Material sharedMat = GetSharedLineMaterial();

        while (list.Count < count)
        {
            var go = new GameObject($"SubP3Line_{list.Count}");
            go.transform.SetParent(null);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount    = 2;
            lr.startWidth       = width;
            lr.endWidth         = width;
            lr.useWorldSpace    = true;
            lr.sortingLayerName = "Default";
            lr.sortingOrder     = 10;
            // Dùng sharedMaterial để tránh leak Material instance mỗi line
            lr.sharedMaterial   = sharedMat;
            lr.startColor       = color;
            lr.endColor         = color;
            go.SetActive(false);
            list.Add(lr);
        }

        // Cập nhật color/width cho các line đã tồn tại (phòng trường hợp color của caller khác)
        for (int i = 0; i < list.Count && i < count; i++)
        {
            if (list[i] == null) continue;
            list[i].startColor = color;
            list[i].endColor   = color;
            list[i].startWidth = width;
            list[i].endWidth   = width;
        }

        for (int i = count; i < list.Count; i++)
            list[i].gameObject.SetActive(false);
    }

    private void ClearLines(List<LineRenderer> list)
    {
        foreach (var lr in list)
            if (lr != null) lr.gameObject.SetActive(false);
    }

    private void GetPlayer()
    {
        var go = GameObject.FindWithTag("Player");
        if (go != null) playerTransform = go.transform;
    }

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        // S1V4 polygon radius
        if (playerTransform != null)
        {
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.3f);
            Gizmos.DrawWireSphere(playerTransform.position, s1V4RadMin);
            Gizmos.DrawWireSphere(playerTransform.position, s1V4RadMax);

            // S2 square offset
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.3f);
            Gizmos.DrawWireSphere(playerTransform.position, s2SquareOffset);
        }

        // S3 scatter zone (arena-absolute)
        Vector3 sc = new Vector3(s3ScatterCenter.x, s3ScatterCenter.y, 0f);
        Gizmos.color = new Color(1f, 0.8f, 0f, 0.3f);
        Gizmos.DrawWireSphere(sc, s3ScatterRadMin);
        Gizmos.color = new Color(1f, 0.8f, 0f, 0.6f);
        Gizmos.DrawWireSphere(sc, s3ScatterRadMax);
    }
}