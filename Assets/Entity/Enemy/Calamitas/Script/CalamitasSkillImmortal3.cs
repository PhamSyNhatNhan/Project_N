using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phase cuối của Calamitas — FSM Update-based, không dùng Coroutine.
///
/// Luồng 1 (State 2): Dart Wall  → dùng dartPool
/// Luồng 2 (State 2): Sniper     → dùng daggerPool  ← KHÔNG dùng Dart
///
/// Conventions:
///   • DeltaTime = timeScale.DeltaTime
///   • entityKey trong Awake
///   • OnDisable: CanDamge=true, ResetAll
///   • OnEnable:  ResetAll, ResetState
/// </summary>
public class CalamitasSkillImmortal3 : MonoBehaviour
{
    // ── Arena ──────────────────────────────────────────────────────
    [Header("Arena")]
    [SerializeField] private ArenaWallManager arenaWallManager;
    [SerializeField] private float            lerpWallSpeed = 3f;

    // ── State 0 ─────────────────────────────────────────────────────
    [Header("State 0 — Arena Open")]
    [SerializeField] private float arenaOpenDelay = 1.2f;

    // ── State 1 — Collapsing Squares ──────────────────────────────
    [Header("State 1 — Squares")]
    [SerializeField] private float sqWarnDuration  = 0.75f;
    [SerializeField] private float sqBulletSpacing = 1.5f;
    [SerializeField] private float sqGapAfter      = 0.4f;

    // ── State 1 — Sweep Matrix ─────────────────────────────────────
    [Header("State 1 — Sweep Matrix")]
    [SerializeField] private int   sweepCount     = 5;
    [SerializeField] private float sweepInterval  = 0.2f;   // mỗi line cách nhau bao lâu
    [SerializeField] private float sweepFireDelay = 0.75f;  // mỗi line bắn sau bao lâu kể từ khi xuất hiện
    [SerializeField] private float sweepSpacing   = 1.5f;

    // ── State 1 — Cross & X ────────────────────────────────────────
    [Header("State 1 — Cross & X")]
    [SerializeField] private float crossWarnDuration = 0.75f;
    [SerializeField] private float crossSpacing      = 1.2f;
    [SerializeField] private int   crossCycles       = 2;
    [SerializeField] private float crossGap          = 0.4f;

    // ── State 2 ─────────────────────────────────────────────────────
    [Header("State 2 — Corridor")]
    [SerializeField] private Vector2 corridorSize      = new Vector2(24f, 6f);
    [SerializeField] private float   corridorTransWait = 3f;

    [Header("State 2 — Luồng 1: Dart Wall")]
    [SerializeField] private int   dartStrips        = 20;
    [SerializeField] private float dartStripInterval = 0.5f;
    [SerializeField] private float dartSpacing       = 0.8f;
    [SerializeField] private int   dartGapMin        = 1;
    [SerializeField] private int   dartGapMax        = 2;

    [Header("State 2 — Luồng 2: Sniper Dagger")]
    [SerializeField] private float sniperInterval  = 2f;   // kích hoạt cứ mỗi 2s
    [SerializeField] private float sniperFireDelay = 0.75f;

    // ── State 3.1 — Trailing Crosshairs ───────────────────────────
    [Header("State 3.1 — Trailing Crosshairs")]
    [SerializeField] private Vector2 s31ArenaSize   = new Vector2(8f, 16f);
    [SerializeField] private float   s31Duration    = 8f;
    [SerializeField] private float   trailInterval  = 0.75f;
    [SerializeField] private float   trailFireDelay = 1.5f;
    [SerializeField] private float   trailLen       = 5f;

    // ── State 3.2 — Shifting Barcode ──────────────────────────────
    [Header("State 3.2 — Shifting Barcode")]
    [SerializeField] private Vector2 s32ArenaSize      = new Vector2(16f, 8f);
    [SerializeField] private int     barcodeCount      = 5;
    [SerializeField] private float   barcodeAmplitude  = 2.5f;
    [SerializeField] private float   barcodeFrequency  = 1.2f;
    [SerializeField] private float   barcodeShowTime   = 1.5f;
    [SerializeField] private float   barcodeFireTime   = 2f;
    [SerializeField] private float   barcodeFireRate   = 0.15f;
    [SerializeField] private float   barcodeSpacing    = 1.0f;

    // ── State 3.3 — Line Art ───────────────────────────────────────
    [Header("State 3.3 — Line Art")]
    [SerializeField] private Vector2 s33ArenaSize    = new Vector2(12f, 12f);
    [SerializeField] private float   lineArtWarnTime = 1.5f;
    [SerializeField] private float   lineArtGap      = 0.5f;
    [SerializeField] private float   lineArtSpacing  = 1.2f;

    // ── State 4.1 — Spiral Center ─────────────────────────────────
    [Header("State 4.1 — Spiral")]
    [SerializeField] private Vector2 finalArenaSize  = new Vector2(6f, 6f);
    [SerializeField] private int     spiralSpokes    = 3;
    [SerializeField] private float   spiralRotSpeed  = 50f;
    [SerializeField] private float   spiralDuration  = 3.5f;
    [SerializeField] private float   spiralFireRate  = 0.25f;
    [SerializeField] private float   spiralSpokeLen  = 10f;
    [SerializeField] private float   spiralSpacing   = 1.4f;

    // ── State 4.2 — Fake-out ──────────────────────────────────────
    [Header("State 4.2 — Fake-out")]
    [SerializeField] private float fakeSpawnRate     = 0.1f;  
    [SerializeField] private float fakeCrossArmLen   = 3f;
    [SerializeField] private float fakeWarnDuration  = 2.5f;
    [SerializeField] private float fakePause         = 0.6f;

    // ── Line Visuals ──────────────────────────────────────────────
    [Header("Line Visuals")]
    [SerializeField] private float warnLineWidth = 0.04f;
    [SerializeField] private Color warnLineColor = new Color(1f, 0.25f, 0.1f, 0.9f);
    [SerializeField] private Color fakeLineColor = new Color(0.45f, 0.45f, 1f, 0.75f);

    // ── Callback ───────────────────────────────────────────────────
    public System.Action OnComplete;

    // ── Components ─────────────────────────────────────────────────
    private string    entityKey;
    private Stat      bossStat;
    private TimeScale timeScale;
    private Transform playerTransform;

    // ── Pools & Damage ─────────────────────────────────────────────
    private EasyPoolingList daggerPool;
    private EasyPoolingList dartPool;
    private List<float>     dmgDagger = new List<float> { 240f };
    private List<float>     dmgDart   = new List<float> { 200f };

    // ══════════════════════════════════════════════════════════════
    // FSM
    // ══════════════════════════════════════════════════════════════
    private enum S
    {
        S0_Open,
        S1_SqWarn, S1_SqGap, S1_SqInterWait,
        S1_SwStagger, S1_SwBetween,
        S1_CrWarn, S1_CrGap, S1_InterWait12,
        S2_Trans, S2_Active, S2_End,
        S31_Trail,
        S32_Barcode,
        S33_Warn, S33_Gap,
        S41_Spiral,
        S42_FakeWarn, S42_FakePause,
        Done
    }

    private S     cur;
    private float timer; // đếm lên từ 0 mỗi khi Enter state mới

    // ── S1 Squares ────────────────────────────────────────────────
    private static readonly float[] SqSizes = { 12f, 8f, 4f };
    private int sqIndex;

    // ── S1 Sweep ──────────────────────────────────────────────────
    private bool                        isSweepVertical;
    private float                       sweepElapsed;
    private int                         sweepLinesShown;
    private readonly List<float>        sweepShowTimes  = new List<float>();
    private readonly List<bool>         sweepFiredFlags = new List<bool>();
    private readonly List<LineRenderer> sweepLRs        = new List<LineRenderer>();

    // ── S1 Cross ──────────────────────────────────────────────────
    private int  crossCycleIdx;
    private bool crossIsPlus;  // true=Plus, false=X

    // ── S2 Dart Wall (Luồng 1) ────────────────────────────────────
    private int   dartsFired;
    private float dartTimer;

    // ── S2 Sniper Dagger (Luồng 2) ────────────────────────────────
    private float                       sniperTimer;
    private bool                        sniperWarning;
    private float                       sniperWarnTimer;
    private readonly float[]            sniperXPos   = new float[3];
    private readonly List<LineRenderer> sniperWarnLRs = new List<LineRenderer>();

    // ── S31 Trailing Crosshairs ────────────────────────────────────
    private struct CrosshairEvent
    {
        public LineRenderer lrH, lrV;
        public Vector2      center;
        public float        fireAt; // s31Elapsed khi cần bắn
    }
    private float                         s31Elapsed;
    private float                         trailSpawnTimer;
    private readonly List<CrosshairEvent> pendingCH = new List<CrosshairEvent>();

    // ── S32 Barcode ────────────────────────────────────────────────
    private readonly List<LineRenderer> barcodeLRs   = new List<LineRenderer>();
    private readonly List<float>        barcodeBaseX  = new List<float>();
    private float                       s32Elapsed;
    private float                       barcodeFireTimer;

    // ── S33 Line Art ───────────────────────────────────────────────
    private readonly int[]                            lineArtOrder = new int[3];
    private int                                       lineArtIdx;
    private readonly List<(Vector2 from, Vector2 to)> lineArtSegs  = new List<(Vector2, Vector2)>();

    // ── S41 Spiral ────────────────────────────────────────────────
    private readonly List<LineRenderer> spiralLRs   = new List<LineRenderer>();
    private float                       s41Elapsed;
    private float                       s41Angle;
    private float                       s41FireTimer;

    // ── S42 Fake ──────────────────────────────────────────────────
    private readonly List<LineRenderer> fakeLRs = new List<LineRenderer>();
    private float fakeSpawnTimer; 

    // ── Line Renderer Pool ─────────────────────────────────────────
    private readonly List<LineRenderer> activeLines   = new List<LineRenderer>();
    private readonly List<LineRenderer> inactiveLines = new List<LineRenderer>();
    private static   Material           s_lineMat;

    private float DeltaTime => timeScale.DeltaTime;

    // ══════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ══════════════════════════════════════════════════════════════
    private void Awake()
    {
        entityKey = GetInstanceID().ToString();
        bossStat  = GetComponent<Stat>();
        timeScale = GetComponent<TimeScale>();
    }

    private void OnEnable()
    {
        FindPlayer();
        ResetAll();
        Enter(S.S0_Open);
        // Subscribe(entityKey, ...);
    }

    private void OnDisable()
    {
        bossStat.CanDamge = true;
        ResetAll();
        // Unsubscribe(entityKey);
    }

    // ── Load Data ──────────────────────────────────────────────────
    public void LoadData(
        EasyPoolingList daggerPool, EasyPoolingList dartPool,
        List<float> dmgDagger,     List<float> dmgDart)
    {
        this.daggerPool = daggerPool;
        this.dartPool   = dartPool;
        if (dmgDagger?.Count > 0) this.dmgDagger = dmgDagger;
        if (dmgDart?.Count   > 0) this.dmgDart   = dmgDart;
    }

    // ══════════════════════════════════════════════════════════════
    // FSM CORE
    // ══════════════════════════════════════════════════════════════
    private void Enter(S next)
    {
        cur   = next;
        timer = 0f;
        OnEnter();
    }

    private void Update()
    {
        timer += DeltaTime;
        Tick(DeltaTime);
    }

    // ── OnEnter: khởi tạo khi vào state ──────────────────────────
    private void OnEnter()
    {
        switch (cur)
        {
            case S.S0_Open:
                if (playerTransform != null)
                {
                    transform.position = playerTransform.position;
                }

                if (arenaWallManager != null)
                    arenaWallManager.SetupArena(new Vector2(16f, 16f));
                break;

            case S.S1_SqWarn:
                DrawSquareBorder(SqSizes[sqIndex]);
                break;

            case S.S1_SwStagger:
                sweepElapsed    = 0f;
                sweepLinesShown = 0;
                sweepShowTimes.Clear();
                sweepFiredFlags.Clear();
                sweepLRs.Clear();
                break;

            case S.S1_CrWarn:
                if (crossIsPlus) DrawPlus();
                else              DrawDiagonalX();
                break;

            case S.S2_Trans:
                if (arenaWallManager != null)
                    arenaWallManager.SetTargetSize(corridorSize, lerpWallSpeed);
                break;

            case S.S2_Active:
                dartsFired    = 0;  dartTimer    = 0f;
                sniperTimer   = 0f; sniperWarning = false;
                sniperWarnTimer = 0f;
                break;

            case S.S31_Trail:
                if (arenaWallManager != null)
                    arenaWallManager.SetTargetSize(s31ArenaSize, lerpWallSpeed);
                s31Elapsed = 0f; trailSpawnTimer = 0f;
                pendingCH.Clear();
                break;

            case S.S32_Barcode:
                if (arenaWallManager != null)
                    arenaWallManager.SetTargetSize(s32ArenaSize, lerpWallSpeed);
                InitBarcode();
                s32Elapsed = 0f; barcodeFireTimer = 0f;
                break;

            case S.S33_Warn:
                if (lineArtIdx == 0)
                {
                    if (arenaWallManager != null)
                        arenaWallManager.SetTargetSize(s33ArenaSize, lerpWallSpeed);
                    ShuffleLineArtOrder();
                }
                DrawLineArtShape(lineArtOrder[lineArtIdx]);
                break;

            case S.S41_Spiral:
                if (arenaWallManager != null)
                    arenaWallManager.SetTargetSize(finalArenaSize, lerpWallSpeed);
                spiralLRs.Clear();
                for (int i = 0; i < spiralSpokes; i++)
                    spiralLRs.Add(AcquireLine(warnLineColor));
                s41Elapsed = 0f; s41Angle = 0f; s41FireTimer = 0f;
                break;

            case S.S42_FakeWarn:
                fakeSpawnTimer = 0f;
                fakeLRs.Clear();
                break; 

            case S.Done:
                if (arenaWallManager != null) arenaWallManager.HideArena(); 
                
                OnComplete?.Invoke();
                enabled = false;
                break;
        }
    }

    // ── Tick: logic mỗi frame ─────────────────────────────────────
    private void Tick(float dt)
    {
        switch (cur)
        {
            // ────── State 0 ────────────────────────────────────────
            case S.S0_Open:
                if (timer >= arenaOpenDelay) Enter(S.S1_SqWarn);
                break;

            // ────── State 1: Squares ───────────────────────────────
            case S.S1_SqWarn:
                if (timer >= sqWarnDuration)
                {
                    FireSquareBorder(SqSizes[sqIndex]);
                    ReturnAllLines();
                    Enter(S.S1_SqGap);
                }
                break;

            case S.S1_SqGap:
                if (timer >= sqGapAfter)
                {
                    if (sqIndex < SqSizes.Length - 1) { sqIndex++; Enter(S.S1_SqWarn); }
                    else Enter(S.S1_SqInterWait);
                }
                break;

            case S.S1_SqInterWait:
                if (timer >= 0.6f)
                {
                    isSweepVertical = true;
                    sqIndex         = 0;
                    Enter(S.S1_SwStagger);
                }
                break;

            // ────── State 1: Sweep ─────────────────────────────────
            case S.S1_SwStagger:
                Tick_SwStagger(dt);
                break;

            case S.S1_SwBetween:
                if (timer >= 0.3f)
                {
                    isSweepVertical = false;
                    Enter(S.S1_SwStagger);
                }
                break;

            // ────── State 1: Cross & X ─────────────────────────────
            case S.S1_CrWarn:
                if (timer >= crossWarnDuration)
                {
                    if (crossIsPlus) FirePlus();
                    else              FireDiagonalX();
                    ReturnAllLines();
                    Enter(S.S1_CrGap);
                }
                break;

            case S.S1_CrGap:
                if (timer >= crossGap)
                {
                    if (crossIsPlus) { crossIsPlus = false; Enter(S.S1_CrWarn); }
                    else
                    {
                        crossCycleIdx++;
                        if (crossCycleIdx < crossCycles) { crossIsPlus = true; Enter(S.S1_CrWarn); }
                        else Enter(S.S1_InterWait12);
                    }
                }
                break;

            case S.S1_InterWait12:
                if (timer >= 0.6f) Enter(S.S2_Trans);
                break;

            // ────── State 2 ─────────────────────────────────────────
            case S.S2_Trans:
                if (timer >= corridorTransWait) Enter(S.S2_Active);
                break;

            case S.S2_Active:
                Tick_S2_Active(dt);
                break;

            case S.S2_End:
                if (timer >= 1.5f) Enter(S.S31_Trail);
                break;

            // ────── State 3.1 ──────────────────────────────────────
            case S.S31_Trail:
                Tick_S31_Trail(dt);
                break;

            // ────── State 3.2 ──────────────────────────────────────
            case S.S32_Barcode:
                Tick_S32_Barcode(dt);
                break;

            // ────── State 3.3 ──────────────────────────────────────
            case S.S33_Warn:
                if (timer >= lineArtWarnTime)
                {
                    foreach (var (f, t) in lineArtSegs)
                        FirePerpAlongLine(f, t, lineArtSpacing);
                    ReturnAllLines();
                    lineArtSegs.Clear();
                    Enter(S.S33_Gap);
                }
                break;

            case S.S33_Gap:
                if (timer >= lineArtGap)
                {
                    lineArtIdx++;
                    if (lineArtIdx < 3) Enter(S.S33_Warn);
                    else                Enter(S.S41_Spiral);
                }
                break;

            // ────── State 4.1 ──────────────────────────────────────
            case S.S41_Spiral:
                Tick_S41_Spiral(dt);
                break;

            // ────── State 4.2 ──────────────────────────────────────
            case S.S42_FakeWarn:
                fakeSpawnTimer += dt;
                if (fakeSpawnTimer >= fakeSpawnRate && timer < fakeWarnDuration)
                {
                    fakeSpawnTimer = 0f;
                    SpawnSingleFakeCross();
                }

                if (timer >= fakeWarnDuration)
                {
                    foreach (var lr in fakeLRs) ReturnLine(lr);
                    fakeLRs.Clear();
                    Enter(S.S42_FakePause);
                }
                break;

            case S.S42_FakePause:
                if (timer >= fakePause) Enter(S.Done);
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // TICK HELPERS
    // ══════════════════════════════════════════════════════════════

    // ── Sweep: staggered lines, each fires sweepFireDelay after appearing ──
    private void Tick_SwStagger(float dt)
    {
        sweepElapsed += dt;
        const float half = 8f; // 16×16 arena

        // Hiện thêm line theo stagger
        int toShow = Mathf.Min(sweepCount,
                               Mathf.FloorToInt(sweepElapsed / sweepInterval) + 1);
        while (sweepLinesShown < toShow)
        {
            float p = Mathf.Lerp(-half, half,
                                  (float)sweepLinesShown / (sweepCount - 1));
            LineRenderer lr = isSweepVertical
                ? AcquireDrawLine(new Vector2(p, -half), new Vector2(p,  half), warnLineColor)
                : AcquireDrawLine(new Vector2(-half, p), new Vector2( half, p), warnLineColor);
            sweepLRs.Add(lr);
            sweepShowTimes.Add(sweepElapsed);
            sweepFiredFlags.Add(false);
            sweepLinesShown++;
        }

        // Bắn từng line khi đủ delay
        for (int i = 0; i < sweepFiredFlags.Count; i++)
        {
            if (sweepFiredFlags[i]) continue;
            if (sweepElapsed - sweepShowTimes[i] < sweepFireDelay) continue;

            float p = Mathf.Lerp(-half, half, (float)i / (sweepCount - 1));
            if (isSweepVertical)
            {
                FireDaggersAlongLine(new Vector2(p,-half), new Vector2(p, half),   0f, sweepSpacing);
                FireDaggersAlongLine(new Vector2(p,-half), new Vector2(p, half), 180f, sweepSpacing);
            }
            else
            {
                FireDaggersAlongLine(new Vector2(-half,p), new Vector2(half,p),  90f, sweepSpacing);
                FireDaggersAlongLine(new Vector2(-half,p), new Vector2(half,p), 270f, sweepSpacing);
            }
            sweepFiredFlags[i] = true;
        }

        // Chuyển state khi tất cả line đã xuất hiện và đã bắn
        if (sweepLinesShown < sweepCount) return;
        foreach (var f in sweepFiredFlags) if (!f) return;

        ReturnAllLines();
        if (isSweepVertical) Enter(S.S1_SwBetween);
        else
        {
            crossCycleIdx = 0; crossIsPlus = true;
            Enter(S.S1_CrWarn);
        }
    }

    // ── State 2: Dart + Sniper chạy song song ─────────────────────
    private void Tick_S2_Active(float dt)
    {
        // Luồng 1: Dart Wall
        if (dartsFired < dartStrips)
        {
            dartTimer += dt;
            if (dartTimer >= dartStripInterval)
            {
                dartTimer = 0f;
                SpawnDartStrip();
                dartsFired++;
            }
        }

        // Luồng 2: Sniper Dagger
        if (!sniperWarning)
        {
            sniperTimer += dt;
            if (sniperTimer >= sniperInterval)
            {
                sniperTimer = 0f;
                ShowSniperWarningLines();
                sniperWarning   = true;
                sniperWarnTimer = 0f;
            }
        }
        else
        {
            sniperWarnTimer += dt;
            if (sniperWarnTimer >= sniperFireDelay)
            {
                FireSniperDaggers();  // dùng daggerPool
                ClearSniperLines();
                sniperWarning = false;
            }
        }

        // Kết thúc khi hết dải Dart VÀ sniper không đang warn
        if (dartsFired >= dartStrips && !sniperWarning)
        {
            ReturnAllLines();
            Enter(S.S2_End);
        }
    }

    // ── State 3.1: Trailing Crosshairs ────────────────────────────
    private void Tick_S31_Trail(float dt)
    {
        s31Elapsed      += dt;
        trailSpawnTimer += dt;

        if (s31Elapsed < s31Duration && trailSpawnTimer >= trailInterval)
        {
            trailSpawnTimer = 0f;
            if (playerTransform != null)
                SpawnTrailCrosshair(playerTransform.position);
        }

        // Bắn pending crosshairs
        for (int i = pendingCH.Count - 1; i >= 0; i--)
        {
            var ev = pendingCH[i];
            if (s31Elapsed < ev.fireAt) continue;
            for (int j = 0; j < 4; j++)
                SpawnBullet(daggerPool, dmgDagger, ev.center, j * 90f);
            ReturnLine(ev.lrH);
            ReturnLine(ev.lrV);
            pendingCH.RemoveAt(i);
        }

        if (s31Elapsed >= s31Duration + trailFireDelay + 0.3f && pendingCH.Count == 0)
        {
            ReturnAllLines();
            Enter(S.S32_Barcode);
        }
    }

    // ── State 3.2: Shifting Barcode ───────────────────────────────
    private void Tick_S32_Barcode(float dt)
    {
        s32Elapsed += dt;

        float halfH = s32ArenaSize.y * 0.5f;
        float halfW = s32ArenaSize.x * 0.5f;

        // Animate lines
        for (int i = 0; i < barcodeLRs.Count; i++)
        {
            float phase = (float)i / barcodeCount * Mathf.PI * 2f;
            float cx    = barcodeBaseX[i] + barcodeAmplitude *
                          Mathf.Sin(s32Elapsed * barcodeFrequency + phase);
            cx = Mathf.Clamp(cx, -halfW + 0.2f, halfW - 0.2f);
            SetLineEndpoints(barcodeLRs[i],
                new Vector2(cx, -halfH), new Vector2(cx, halfH));
        }

        // Fire phase
        if (s32Elapsed >= barcodeShowTime)
        {
            barcodeFireTimer += dt;
            if (barcodeFireTimer >= barcodeFireRate)
            {
                barcodeFireTimer = 0f;
                for (int i = 0; i < barcodeLRs.Count; i++)
                {
                    float cx = barcodeLRs[i].GetPosition(0).x;
                    
                    SpawnBullet(daggerPool, dmgDagger, new Vector2(cx, halfH), 270f); 
                }
            }
        }

        if (s32Elapsed >= barcodeShowTime + barcodeFireTime)
        {
            foreach (var lr in barcodeLRs) ReturnLine(lr);
            barcodeLRs.Clear();
            barcodeBaseX.Clear();
            lineArtIdx = 0;
            Enter(S.S33_Warn);
        }
    }

    // ── State 4.1: Spinning Spiral ────────────────────────────────
    private void Tick_S41_Spiral(float dt)
    {
        s41Elapsed   += dt;
        s41Angle     += spiralRotSpeed * dt;
        s41FireTimer += dt;

        float half = spiralSpokeLen * 0.5f;

        for (int i = 0; i < spiralLRs.Count; i++)
        {
            float   a = (s41Angle + 360f * i / spiralSpokes) * Mathf.Deg2Rad;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * half;
            SetLineEndpoints(spiralLRs[i], -d, d);
        }

        if (s41FireTimer >= spiralFireRate)
        {
            s41FireTimer = 0f;
            for (int i = 0; i < spiralSpokes; i++)
            {
                float   a        = (s41Angle + 360f * i / spiralSpokes) * Mathf.Deg2Rad;
                Vector2 spokeDir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float   fireAngle = a * Mathf.Rad2Deg + 90f; // vuông góc với nan hoa
                for (float r = spiralSpacing; r <= half; r += spiralSpacing)
                {
                    SpawnBullet(daggerPool, dmgDagger,  spokeDir * r, fireAngle);
                    SpawnBullet(daggerPool, dmgDagger, -spokeDir * r, fireAngle);
                }
            }
        }

        if (s41Elapsed >= spiralDuration)
        {
            foreach (var lr in spiralLRs) ReturnLine(lr);
            spiralLRs.Clear();
            Enter(S.S42_FakeWarn);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // STATE SETUP HELPERS
    // ══════════════════════════════════════════════════════════════

    private void ShowSniperWarningLines()
    {
        if (playerTransform == null) FindPlayer();
        float halfH = corridorSize.y * 0.5f;
        float halfW = corridorSize.x * 0.5f - 1f;

        sniperXPos[0] = playerTransform != null
            ? Mathf.Clamp(playerTransform.position.x, -halfW, halfW) : 0f;
        sniperXPos[1] = Random.Range(-halfW, halfW);
        sniperXPos[2] = Random.Range(-halfW, halfW);

        sniperWarnLRs.Clear();
        foreach (float x in sniperXPos)
            sniperWarnLRs.Add(AcquireDrawLine(
                new Vector2(x, halfH), new Vector2(x, -halfH), warnLineColor));
    }

    private void FireSniperDaggers() // Luồng 2 dùng DAGGER, không phải Dart
    {
        float halfH = corridorSize.y * 0.5f;
        foreach (float x in sniperXPos)
            SpawnBullet(daggerPool, dmgDagger, new Vector2(x, halfH), 270f);
    }

    private void ClearSniperLines()
    {
        foreach (var lr in sniperWarnLRs) ReturnLine(lr);
        sniperWarnLRs.Clear();
    }

    private void SpawnDartStrip() // Luồng 1 dùng Dart
    {
        float halfH  = corridorSize.y * 0.5f - 0.2f;
        float spawnX = corridorSize.x * 0.5f + 1.5f;
        int   total  = Mathf.Max(3, Mathf.RoundToInt(halfH * 2f / dartSpacing) + 1);
        int   gaps   = Random.Range(dartGapMin, dartGapMax + 1);

        var indices = new List<int>();
        for (int i = 0; i < total; i++) indices.Add(i);
        for (int g = 0; g < gaps && indices.Count > 2; g++)
            indices.RemoveAt(Random.Range(0, indices.Count));

        foreach (int idx in indices)
        {
            float y = Mathf.Lerp(-halfH, halfH, (float)idx / (total - 1));
            SpawnBullet(dartPool, dmgDart, new Vector2(spawnX, y), 180f);
        }
    }

    private void SpawnTrailCrosshair(Vector2 center)
    {
        float hf = trailLen * 0.5f;
        pendingCH.Add(new CrosshairEvent
        {
            lrH    = AcquireDrawLine(center + Vector2.left  * hf, center + Vector2.right * hf, warnLineColor),
            lrV    = AcquireDrawLine(center + Vector2.down  * hf, center + Vector2.up    * hf, warnLineColor),
            center = center,
            fireAt = s31Elapsed + trailFireDelay
        });
    }

    private void InitBarcode()
    {
        float halfH = s32ArenaSize.y * 0.5f, halfW = s32ArenaSize.x * 0.5f;
        barcodeLRs.Clear(); barcodeBaseX.Clear();
        for (int i = 0; i < barcodeCount; i++)
        {
            float bx = Mathf.Lerp(-halfW + 1.5f, halfW - 1.5f, (float)i / (barcodeCount - 1));
            barcodeBaseX.Add(bx);
            barcodeLRs.Add(AcquireDrawLine(
                new Vector2(bx, -halfH), new Vector2(bx, halfH), warnLineColor));
        }
    }

    private void ShuffleLineArtOrder()
    {
        lineArtOrder[0] = 0; lineArtOrder[1] = 1; lineArtOrder[2] = 2;
        for (int i = 2; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (lineArtOrder[i], lineArtOrder[j]) = (lineArtOrder[j], lineArtOrder[i]);
        }
    }

    private void DrawLineArtShape(int shape)
    {
        lineArtSegs.Clear();
        switch (shape)
        {
            case 0: DrawScissors(); break;
            case 1: DrawRock();     break;
            case 2: DrawPaper();    break;
        }
    }

    private void DrawScissors()
    {
        float hw   = s33ArenaSize.x * 0.5f - 0.5f;
        float topY = s33ArenaSize.y * 0.5f - 0.5f;
        float botY = -s33ArenaSize.y * 0.5f + 1f;
        var   tipL = new Vector2(-hw, topY); var tipR = new Vector2(hw, topY);
        var   apex = new Vector2(0f, botY);
        DrawLine(tipL, apex); lineArtSegs.Add((tipL, apex));
        DrawLine(tipR, apex); lineArtSegs.Add((tipR, apex));
    }

    private void DrawRock()
    {
        const float r = 2.5f; const int n = 6;
        for (int i = 0; i < n; i++)
        {
            var p0 = new Vector2(Mathf.Cos(i * 360f/n * Mathf.Deg2Rad),
                                 Mathf.Sin(i * 360f/n * Mathf.Deg2Rad)) * r;
            var p1 = new Vector2(Mathf.Cos((i+1)*360f/n * Mathf.Deg2Rad),
                                 Mathf.Sin((i+1)*360f/n * Mathf.Deg2Rad)) * r;
            DrawLine(p0, p1); lineArtSegs.Add((p0, p1));
        }
    }

    private void DrawPaper()
    {
        float hw = s33ArenaSize.x * 0.5f;
        float ty = s33ArenaSize.y * 0.5f, by = -s33ArenaSize.y * 0.5f;
        for (int i = 0; i < 5; i++)
        {
            float t = (float)i / 4;
            var f   = new Vector2(Mathf.Lerp(-hw*0.3f, hw*0.3f, t), by);
            var to  = new Vector2(Mathf.Lerp(-hw+0.5f, hw-0.5f, t), ty);
            DrawLine(f, to); lineArtSegs.Add((f, to));
        }
    }

    private void SpawnSingleFakeCross()
    {
        if (playerTransform == null) FindPlayer();
        float halfA = Mathf.Min(finalArenaSize.x, finalArenaSize.y) * 0.5f - 0.5f;
        float halfArm = fakeCrossArmLen * 0.5f;

        bool lockOn = (Random.value < 0.3f) && playerTransform != null;
        Vector2 c = lockOn 
            ? (Vector2)playerTransform.position 
            : new Vector2(Random.Range(-halfA, halfA), Random.Range(-halfA, halfA));

        var lrH = AcquireLine(fakeLineColor);
        var lrV = AcquireLine(fakeLineColor);
        SetLineEndpoints(lrH, c + Vector2.left  * halfArm, c + Vector2.right * halfArm);
        SetLineEndpoints(lrV, c + Vector2.down  * halfArm, c + Vector2.up    * halfArm);
        fakeLRs.Add(lrH); 
        fakeLRs.Add(lrV);
    }

    // ══════════════════════════════════════════════════════════════
    // SHAPE DRAW / FIRE HELPERS
    // ══════════════════════════════════════════════════════════════
    private void DrawSquareBorder(float size)
    {
        float h = size * 0.5f;
        DrawLine(new Vector2(-h,  h), new Vector2( h,  h));
        DrawLine(new Vector2(-h, -h), new Vector2( h, -h));
        DrawLine(new Vector2(-h, -h), new Vector2(-h,  h));
        DrawLine(new Vector2( h, -h), new Vector2( h,  h));
    }

    private void FireSquareBorder(float size)
    {
        float h = size * 0.5f;
        FireDaggersAlongLine(new Vector2(-h,  h), new Vector2( h,  h), 270f, sqBulletSpacing);
        FireDaggersAlongLine(new Vector2(-h, -h), new Vector2( h, -h),  90f, sqBulletSpacing);
        FireDaggersAlongLine(new Vector2(-h, -h), new Vector2(-h,  h),   0f, sqBulletSpacing);
        FireDaggersAlongLine(new Vector2( h, -h), new Vector2( h,  h), 180f, sqBulletSpacing);
    }

    private void DrawPlus()
    {
        float h = 8f;
        DrawLine(new Vector2(-h, 0f), new Vector2(h,  0f));
        DrawLine(new Vector2(0f, -h), new Vector2(0f, h));
    }

    private void FirePlus()
    {
        float h = 8f;
        FireDaggersAlongLine(new Vector2(-h, 0f), new Vector2(h,  0f),  90f, crossSpacing);
        FireDaggersAlongLine(new Vector2(-h, 0f), new Vector2(h,  0f), 270f, crossSpacing);
        FireDaggersAlongLine(new Vector2(0f, -h), new Vector2(0f,  h),   0f, crossSpacing);
        FireDaggersAlongLine(new Vector2(0f, -h), new Vector2(0f,  h), 180f, crossSpacing);
    }

    private void DrawDiagonalX()
    {
        float h = 8f;
        DrawLine(new Vector2(-h, -h), new Vector2( h,  h));
        DrawLine(new Vector2(-h,  h), new Vector2( h, -h));
    }

    private void FireDiagonalX()
    {
        float h = 8f;
        FirePerpAlongLine(new Vector2(-h, -h), new Vector2( h,  h), crossSpacing);
        FirePerpAlongLine(new Vector2(-h,  h), new Vector2( h, -h), crossSpacing);
    }

    // ══════════════════════════════════════════════════════════════
    // LINE RENDERER HELPERS
    // ══════════════════════════════════════════════════════════════
    private void DrawLine(Vector2 from, Vector2 to)
    {
        SetLineEndpoints(AcquireLine(warnLineColor), from, to);
    }

    private LineRenderer AcquireLine(Color color)
    {
        LineRenderer lr;
        if (inactiveLines.Count > 0)
        {
            lr = inactiveLines[inactiveLines.Count - 1];
            inactiveLines.RemoveAt(inactiveLines.Count - 1);
        }
        else lr = CreateNewLine();
        lr.startColor = color; lr.endColor   = color;
        lr.startWidth = warnLineWidth; lr.endWidth   = warnLineWidth;
        lr.gameObject.SetActive(true);
        activeLines.Add(lr);
        return lr;
    }

    private LineRenderer AcquireDrawLine(Vector2 from, Vector2 to, Color color)
    {
        var lr = AcquireLine(color);
        SetLineEndpoints(lr, from, to);
        return lr;
    }

    private void SetLineEndpoints(LineRenderer lr, Vector2 from, Vector2 to)
    {
        if (lr == null) return;
        lr.SetPosition(0, (Vector3)from);
        lr.SetPosition(1, (Vector3)to);
    }

    private void ReturnLine(LineRenderer lr)
    {
        if (lr == null) return;
        lr.gameObject.SetActive(false);
        activeLines.Remove(lr);
        inactiveLines.Add(lr);
    }

    private void ReturnAllLines()
    {
        foreach (var lr in activeLines)
        {
            if (lr == null) continue;
            lr.gameObject.SetActive(false);
            inactiveLines.Add(lr);
        }
        activeLines.Clear();
    }

    private LineRenderer CreateNewLine()
    {
        if (s_lineMat == null)
        {
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (sh != null)
                s_lineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        }
        var go = new GameObject("Imm3Line");
        go.transform.SetParent(null);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount    = 2;
        lr.useWorldSpace    = true;
        lr.sortingLayerName = "Default";
        lr.sortingOrder     = 10;
        if (s_lineMat != null) lr.sharedMaterial = s_lineMat;
        go.SetActive(false);
        return lr;
    }

    // ══════════════════════════════════════════════════════════════
    // BULLET HELPERS
    // ══════════════════════════════════════════════════════════════
    private void FireDaggersAlongLine(Vector2 from, Vector2 to, float angle, float spacing)
    {
        float len   = Vector2.Distance(from, to);
        int   count = Mathf.Max(1, Mathf.RoundToInt(len / spacing));
        for (int i = 0; i <= count; i++)
            SpawnBullet(daggerPool, dmgDagger,
                        Vector2.Lerp(from, to, (float)i / count), angle);
    }

    private void FirePerpAlongLine(Vector2 from, Vector2 to, float spacing)
    {
        Vector2 dir   = (to - from).normalized;
        float   angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        FireDaggersAlongLine(from, to, angle + 90f, spacing);
        FireDaggersAlongLine(from, to, angle - 90f, spacing);
    }

    private void SpawnBullet(
        EasyPoolingList pool, List<float> dmg, Vector2 pos, float angle)
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

    // ══════════════════════════════════════════════════════════════
    // MISC
    // ══════════════════════════════════════════════════════════════
    private void ResetAll()
    {
        ReturnAllLines();
        timer         = 0f;
        sqIndex       = 0;
        sweepElapsed  = 0f; sweepLinesShown = 0;
        sweepLRs.Clear(); sweepShowTimes.Clear(); sweepFiredFlags.Clear();
        isSweepVertical = true;
        crossCycleIdx   = 0; crossIsPlus = true;
        dartsFired    = 0; dartTimer    = 0f;
        sniperTimer   = 0f; sniperWarning = false; sniperWarnTimer = 0f;
        sniperWarnLRs.Clear();
        s31Elapsed    = 0f; trailSpawnTimer = 0f; pendingCH.Clear();
        s32Elapsed    = 0f; barcodeFireTimer = 0f;
        barcodeLRs.Clear(); barcodeBaseX.Clear();
        lineArtIdx    = 0; lineArtSegs.Clear();
        spiralLRs.Clear(); s41Elapsed = 0f; s41Angle = 0f; s41FireTimer = 0f;
        fakeLRs.Clear();
    }

    private void FindPlayer()
    {
        var go = GameObject.FindWithTag("Player");
        if (go != null) playerTransform = go.transform;
    }

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        // Hiển thị kích thước arena hiện tại theo state
        Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.2f);
        Vector2 arenaSize = cur switch
        {
            S.S31_Trail => s31ArenaSize,
            S.S32_Barcode => s32ArenaSize,
            S.S33_Warn or S.S33_Gap => s33ArenaSize,
            S.S41_Spiral or S.S42_FakeWarn or S.S42_FakePause => finalArenaSize,
            _ => new Vector2(16f, 16f)
        };
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(arenaSize.x, arenaSize.y, 0f));
    }
}