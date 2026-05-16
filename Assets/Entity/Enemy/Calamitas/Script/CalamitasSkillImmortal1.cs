using System;
using System.Collections.Generic;
using UnityEngine;

public class CalamitasSkillImmortal1 : MonoBehaviour
{
    // ── Callback ──────────────────────────────────────────────────
    public Action OnComplete;

    // ── Dash ──────────────────────────────────────────────────────
    [Header("Dash")]
    [SerializeField] private GameObject dashFxObject;
    [SerializeField] private LayerMask  playerLayer;
    [SerializeField] private float      warningDuration    = 1.5f;
    [SerializeField] private float      dashVelocity       = 900f;
    [SerializeField] private float      dashTime           = 0.25f;
    [SerializeField] private float      dashMaxDist        = 8f;
    [SerializeField] private float      dashDelay          = 1f;
    [SerializeField] private int        dashPerCycle       = 3;
    [SerializeField] private int        hellfireExtraPairs = 1;
    [SerializeField] private float      hellfireAngleStep  = 20f;
    [SerializeField] private float      dashWarnLineWidth  = 0.05f;
    [SerializeField] private Color      warnLineColor      = new Color(1f, 0.3f, 0f, 0.9f);

    // ── Cycle ─────────────────────────────────────────────────────
    [Header("Cycle")]
    [SerializeField] private int   totalCycles         = 3;
    [SerializeField] private float initialIdleDuration = 2.5f;
    [SerializeField] private float idleDuration        = 0.8f;

    // ── Pattern — Warning ─────────────────────────────────────────
    [Header("Pattern — Warning")]
    [SerializeField] private float patternWarnDuration      = 1.5f;
    [SerializeField] private float patternPreviewLength     = 5f;
    [SerializeField] private float patternWarnLineWidth     = 0.02f;  // mảnh hơn dash
    [SerializeField] private float patternDecagonWarnLength = 8f;     // dài hơn cho pattern 2 & 3

    // ── Pattern — Checkerboard ────────────────────────────────────
    [Header("Pattern — Checkerboard")]
    [SerializeField] private float cbRectW      = 10f;
    [SerializeField] private float cbRectH      = 7f;
    [SerializeField] private float cbSpacingMin = 0.8f;
    [SerializeField] private float cbSpacingMax = 1.4f;

    // ── Pattern — Double Ring ─────────────────────────────────────
    [Header("Pattern — Double Ring")]
    [SerializeField] private int   ringCount1      = 12;
    [SerializeField] private int   ringCount2      = 12;
    [SerializeField] private float ringRadiusMin   = 2f;
    [SerializeField] private float ringRadiusMax   = 4f;
    [SerializeField] private float ringOffsetX     = 3f;   // offset trái/phải player

    // ── Pattern — Decagon ─────────────────────────────────────────
    [Header("Pattern — Decagon")]
    [SerializeField] private int   decagonPoints = 12;
    [SerializeField] private float decagonRadMin = 3f;
    [SerializeField] private float decagonRadMax = 5f;

    // ── Components ────────────────────────────────────────────────
    private Stat              bossStat;
    private Move              bossMove;
    private TimeScale         timeScale;
    private CapsuleCollider2D bossCollider;
    private SpriteRenderer    bossRenderer;
    private Hitbox            hitbox;
    private CalamitasShadow   shadow;
    private Transform         playerTransform;
    private GameObject        shield;

    // ── Pools & Damage ────────────────────────────────────────────
    private EasyPoolingList hellfirePool;
    private EasyPoolingList daggerPool;
    private List<float>     dmgHellfire = new List<float> { 280f };
    private List<float>     dmgDagger   = new List<float> { 220f };
    private List<float>     dmgDash     = new List<float> { 350f };

    // ── Pattern data ──────────────────────────────────────────────
    private struct PatternShot
    {
        public Vector2 pos;
        public float   angle;
        public PatternShot(Vector2 p, float a) { pos = p; angle = a; }
    }
    private readonly List<PatternShot>  patternShots     = new List<PatternShot>();
    private readonly List<LineRenderer> patternWarnLines = new List<LineRenderer>();

    // ── FSM ───────────────────────────────────────────────────────
    private enum S
    {
        InitialIdle, InitialDepart,
        LineWarn, Dash, DashDelay,
        Idle, PatternWarn, PatternFire
    }

    private S     cur;
    private float timer;
    private float shadowTimer;

    // Cycle
    private int cyclesDone;
    private int dashesThisCycle;
    private int patternIndex;
    private int patternRepeat;

    // Dash
    private Vector2              lineHead;
    private Vector2              dashDir;
    private Vector2              dashStartPos;
    private readonly HashSet<Stat> dashHits = new HashSet<Stat>();

    // Line warning
    private LineRenderer warnLine;
    private GameObject   warnLineGo;

    private float DeltaTime => timeScale.DeltaTime;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        bossStat     = GetComponent<Stat>();
        bossMove     = GetComponent<Move>();
        timeScale    = GetComponent<TimeScale>();
        bossCollider = GetComponent<CapsuleCollider2D>();
        hitbox       = GetComponent<Hitbox>();
        shadow       = GetComponent<CalamitasShadow>();
        bossRenderer = GetComponent<SpriteRenderer>()
                    ?? GetComponentInChildren<SpriteRenderer>();

        EnsureLineMat();

        warnLineGo = new GameObject("Immortal1WarnLine");
        warnLineGo.transform.SetParent(null);
        warnLine                  = warnLineGo.AddComponent<LineRenderer>();
        warnLine.positionCount    = 2;
        warnLine.startWidth       = dashWarnLineWidth;
        warnLine.endWidth         = dashWarnLineWidth;
        warnLine.useWorldSpace    = true;
        warnLine.sortingLayerName = "Default";
        warnLine.sortingOrder     = 10;
        warnLine.sharedMaterial   = s_lineMat;
        warnLine.startColor       = warnLineColor;
        warnLine.endColor         = warnLineColor;
        warnLineGo.SetActive(false);
    }

    private static Material s_lineMat;
    private static void EnsureLineMat()
    {
        if (s_lineMat != null) return;
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (shader != null)
            s_lineMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    private void OnEnable()
    {
        cyclesDone      = 0;
        dashesThisCycle = 0;
        patternIndex    = 0;
        patternRepeat   = 0;

        FindPlayer();
        SetBossVisible(true);
        SetShield(true);
        if (dashFxObject != null) dashFxObject.SetActive(false);

        Enter(S.InitialIdle);
    }

    private void OnDisable()
    {
        SetBossVisible(true);
        SetShield(false);
        bossMove.ResetAll();
        warnLineGo?.SetActive(false);
        if (dashFxObject != null) dashFxObject.SetActive(false);
        HidePatternPreview();
    }

    // ── Setup ─────────────────────────────────────────────────────
    public void SetShieldObject(GameObject shieldObj) => shield = shieldObj;

    public void LoadData(EasyPoolingList hellfire, EasyPoolingList dagger,
                         List<float> hfDmg, List<float> dagDmg, List<float> dashDmg)
    {
        hellfirePool = hellfire;
        daggerPool   = dagger;
        if (hfDmg   != null) dmgHellfire = hfDmg;
        if (dagDmg  != null) dmgDagger   = dagDmg;
        if (dashDmg != null) dmgDash     = dashDmg;
    }

    // ── Update ────────────────────────────────────────────────────
    private void Update()
    {
        FacePlayer();
        switch (cur)
        {
            case S.InitialIdle:   UpdateInitialIdle();   break;
            case S.InitialDepart: UpdateInitialDepart(); break;
            case S.LineWarn:      UpdateLineWarn();      break;
            case S.Dash:          UpdateDash();          break;
            case S.DashDelay:     UpdateDashDelay();     break;
            case S.Idle:          UpdateIdle();          break;
            case S.PatternWarn:   UpdatePatternWarn();   break;
            case S.PatternFire:   UpdatePatternFire();   break;
        }
    }

    private void FacePlayer()
    {
        if (playerTransform == null) return;
        if (cur == S.Dash) return;
        float toPlayerX  = playerTransform.position.x - transform.position.x;
        int   wantedFlip = toPlayerX >= 0f ? 1 : -1;
        if (wantedFlip != bossMove.FlipDirect)
        {
            bossMove.FlipDirect = wantedFlip;
            transform.rotation = Quaternion.Euler(0f, wantedFlip == 1 ? 0f : 180f, 0f);
        }
    }

    // ── Enter ─────────────────────────────────────────────────────
    private void Enter(S state)
    {
        cur = state;
        switch (state)
        {
            case S.InitialIdle:
                timer = initialIdleDuration;
                break;

            case S.InitialDepart:
                shadow?.PlayDepartShadow(transform.position);
                SetBossVisible(false);
                shadowTimer = shadow != null ? Mathf.Max(0f, shadow.FadeDuration - 0.1f) : 0f;
                CalcLineHead();
                break;

            case S.LineWarn:
                // Reset velocity trước khi set position — tránh drift
                bossMove.ResetAll();
                transform.position = lineHead;
                SetBossVisible(false);
                warnLineGo.SetActive(true);
                UpdateLinePositions();
                timer = warningDuration;
                break;

            case S.Dash:
                warnLineGo.SetActive(false);
                SetBossVisible(true);
                dashStartPos = lineHead;
                transform.position = lineHead;
                if (dashFxObject != null)
                {
                    float fxAngle = Mathf.Atan2(dashDir.y, dashDir.x) * Mathf.Rad2Deg;
                    dashFxObject.transform.localRotation = Quaternion.Euler(0f, 0f, fxAngle);
                    dashFxObject.SetActive(true);
                }
                dashHits.Clear();
                bossMove.MoveSnap = dashDir;
                bossMove.MoveTo(dashVelocity, dashTime);
                FireHellfire(Mathf.Atan2(dashDir.y, dashDir.x) * Mathf.Rad2Deg);
                timer = dashTime;
                break;

            case S.DashDelay:
                bossMove.StopMovement();
                SetBossVisible(false);
                if (dashFxObject != null) dashFxObject.SetActive(false);
                timer = dashDelay;
                break;

            case S.Idle:
                warnLineGo?.SetActive(false);
                timer = idleDuration;
                break;

            case S.PatternWarn:
                bossMove.StopMovement();
                if (dashFxObject != null) dashFxObject.SetActive(false);
                warnLineGo?.SetActive(false);
                PreparePattern();
                ShowPatternPreview();
                timer = patternWarnDuration;
                break;

            case S.PatternFire:
                HidePatternPreview();
                FirePattern();
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // UPDATE METHODS
    // ══════════════════════════════════════════════════════════════
    private void UpdateInitialIdle()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.InitialDepart);
    }

    private void UpdateInitialDepart()
    {
        shadowTimer -= DeltaTime;
        if (shadowTimer <= 0f) Enter(S.LineWarn);
    }

    private void UpdateLineWarn()
    {
        // Đuôi line xoay theo player, đầu line cố định
        if (playerTransform != null)
            dashDir = ((Vector2)playerTransform.position - lineHead).normalized;
        UpdateLinePositions();

        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.Dash);
    }

    private void UpdateDash()
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
                                 dmgDash.Count > 0 ? dmgDash[0] : 350f,
                                 bossStat.CurCritRate, bossStat.CurCritDamage);
                }
        }

        timer -= DeltaTime;
        float traveled = Vector2.Distance(transform.position, dashStartPos);
        if (timer > 0f && traveled < dashMaxDist) return;

        bossMove.StopMovement();
        dashesThisCycle++;

        if (dashesThisCycle < dashPerCycle)
            Enter(S.DashDelay);
        else
        {
            dashesThisCycle = 0;
            Enter(S.PatternWarn);
        }
    }

    private void UpdateDashDelay()
    {
        timer -= DeltaTime;
        if (timer <= 0f)
        {
            CalcLineHead();
            Enter(S.LineWarn);
        }
    }

    private void UpdateIdle()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.PatternWarn);
    }

    private void UpdatePatternWarn()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.PatternFire);
    }

    private void UpdatePatternFire()
    {
        patternRepeat++;
        if (patternRepeat >= 3)
        {
            patternRepeat = 0;
            patternIndex  = (patternIndex + 1) % 3;
            cyclesDone++;

            if (cyclesDone >= totalCycles)
            {
                enabled = false;
                OnComplete?.Invoke();
                return;
            }

            dashesThisCycle = 0;
            CalcLineHead();
            Enter(S.LineWarn);
        }
        else
        {
            Enter(S.Idle);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // CALC & LINE
    // ══════════════════════════════════════════════════════════════
    private void CalcLineHead()
    {
        if (playerTransform == null) return;
        float   angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
        Vector2 dir   = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        lineHead = (Vector2)playerTransform.position - dir * (dashMaxDist * 0.5f);
        dashDir  = dir;
    }

    private void UpdateLinePositions()
    {
        Vector2 tail = lineHead + dashDir * dashMaxDist;
        warnLine.SetPosition(0, lineHead);
        warnLine.SetPosition(1, tail);
    }

    // ══════════════════════════════════════════════════════════════
    // PATTERN PREPARE
    // ══════════════════════════════════════════════════════════════
    private void PreparePattern()
    {
        patternShots.Clear();
        if (playerTransform == null) return;
        Vector2 center = playerTransform.position;
        switch (patternIndex)
        {
            case 0: PrepareCheckerboard(center); break;
            case 1: PrepareDoubleRing(center);   break;
            case 2: PrepareDecagon(center);      break;
        }
    }

    private void FirePattern()
    {
        foreach (var shot in patternShots)
            SpawnDagger(shot.pos, shot.angle);
        patternShots.Clear();
    }

    // ── Checkerboard: 1 bullet mỗi đầu line ──────────────────────
    private void PrepareCheckerboard(Vector2 center)
    {
        float rotation = patternRepeat == 1 ? 45f : 0f;
        float spacing  = UnityEngine.Random.Range(cbSpacingMin, cbSpacingMax);
        center += new Vector2(
            UnityEngine.Random.Range(-0.5f, 0.5f),
            UnityEngine.Random.Range(-0.5f, 0.5f));

        float cos = Mathf.Cos(rotation * Mathf.Deg2Rad);
        float sin = Mathf.Sin(rotation * Mathf.Deg2Rad);

        // Lines ngang — bullet spawn 2 đầu bắn vào
        int hCount = Mathf.RoundToInt(cbRectH / spacing);
        for (int i = -hCount; i <= hCount; i++)
        {
            Vector2 offset = new Vector2(-sin, cos) * (i * spacing);
            Vector2 dir    = new Vector2(cos, sin);
            // 1 bullet từ trái vào, 1 bullet từ phải vào
            patternShots.Add(new PatternShot(center + offset - dir * cbRectW,
                Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg));
            patternShots.Add(new PatternShot(center + offset + dir * cbRectW,
                Mathf.Atan2(-dir.y, -dir.x) * Mathf.Rad2Deg));
        }

        // Lines dọc — bullet spawn 2 đầu bắn vào
        int vCount = Mathf.RoundToInt(cbRectW / spacing);
        for (int i = -vCount; i <= vCount; i++)
        {
            Vector2 offset = new Vector2(cos, sin) * (i * spacing);
            Vector2 dir    = new Vector2(-sin, cos);
            patternShots.Add(new PatternShot(center + offset - dir * cbRectH,
                Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg));
            patternShots.Add(new PatternShot(center + offset + dir * cbRectH,
                Mathf.Atan2(-dir.y, -dir.x) * Mathf.Rad2Deg));
        }
    }

    // ── Double Ring: offset trái/phải player ──────────────────────
    private void PrepareDoubleRing(Vector2 center)
    {
        float rndY = UnityEngine.Random.Range(-0.5f, 0.5f);

        // Ring 1 — bên trái player
        Vector2 center1 = center + new Vector2(-ringOffsetX, rndY);
        // Ring 2 — bên phải player
        Vector2 center2 = center + new Vector2( ringOffsetX, rndY);

        float baseAngle1 = UnityEngine.Random.Range(0f, 360f);
        float baseAngle2 = baseAngle1 + UnityEngine.Random.Range(10f, 50f);

        PrepareRing(center1, ringCount1, baseAngle1);
        PrepareRing(center2, ringCount2, baseAngle2);
    }

    private void PrepareRing(Vector2 center, int count, float baseAngle)
    {
        float step = 360f / count;
        for (int i = 0; i < count; i++)
            patternShots.Add(new PatternShot(center, baseAngle + step * i));
    }

    // ── Decagon ───────────────────────────────────────────────────
    private void PrepareDecagon(Vector2 center)
    {
        float radius    = UnityEngine.Random.Range(decagonRadMin, decagonRadMax);
        float baseAngle = UnityEngine.Random.Range(0f, 360f);
        center += new Vector2(
            UnityEngine.Random.Range(-0.5f, 0.5f),
            UnityEngine.Random.Range(-0.5f, 0.5f));

        float     step   = 360f / decagonPoints;
        Vector2[] points = new Vector2[decagonPoints];
        for (int i = 0; i < decagonPoints; i++)
        {
            float a = (baseAngle + step * i) * Mathf.Deg2Rad;
            points[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        for (int i = 0; i < decagonPoints; i++)
            for (int j = i + 1; j < decagonPoints; j++)
            {
                Vector2 dir = (points[j] - points[i]).normalized;
                float   a   = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                patternShots.Add(new PatternShot(points[i], a));
                patternShots.Add(new PatternShot(points[j], a + 180f));
            }
    }

    // ══════════════════════════════════════════════════════════════
    // PATTERN PREVIEW
    // ══════════════════════════════════════════════════════════════
    private LineRenderer GetOrCreatePatternWarnLine(int index)
    {
        while (patternWarnLines.Count <= index)
        {
            EnsureLineMat();
            var go = new GameObject("Immortal1PatWarn_" + patternWarnLines.Count);
            go.transform.SetParent(null);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount    = 2;
            lr.useWorldSpace    = true;
            lr.sortingLayerName = "Default";
            lr.sortingOrder     = 10;
            lr.sharedMaterial   = s_lineMat;
            lr.startColor       = warnLineColor;
            lr.endColor         = warnLineColor;
            go.SetActive(false);
            patternWarnLines.Add(lr);
        }
        return patternWarnLines[index];
    }

    private void ShowPatternPreview()
    {
        // Pattern 0 (checkerboard): mảnh hơn, ngắn hơn
        // Pattern 1, 2 (ring, decagon): mảnh và dài hơn
        float lineWidth = patternWarnLineWidth;
        float lineLen   = patternIndex == 0 ? patternPreviewLength : patternDecagonWarnLength;

        for (int i = 0; i < patternShots.Count; i++)
        {
            var     lr   = GetOrCreatePatternWarnLine(i);
            var     shot = patternShots[i];
            Vector2 dir  = new Vector2(
                Mathf.Cos(shot.angle * Mathf.Deg2Rad),
                Mathf.Sin(shot.angle * Mathf.Deg2Rad));

            lr.startWidth = lineWidth;
            lr.endWidth   = lineWidth;
            lr.SetPosition(0, shot.pos);
            lr.SetPosition(1, (Vector3)shot.pos + new Vector3(dir.x, dir.y, 0f) * lineLen);
            lr.gameObject.SetActive(true);
        }
        // Tắt các line dư
        for (int i = patternShots.Count; i < patternWarnLines.Count; i++)
            patternWarnLines[i].gameObject.SetActive(false);
    }

    private void HidePatternPreview()
    {
        foreach (var lr in patternWarnLines)
            if (lr != null) lr.gameObject.SetActive(false);
    }

    // ══════════════════════════════════════════════════════════════
    // FIRE HELPERS
    // ══════════════════════════════════════════════════════════════
    private void FireHellfire(float baseAngle)
    {
        SpawnBullet(hellfirePool, dmgHellfire, transform.position, baseAngle);
        for (int i = 1; i <= hellfireExtraPairs; i++)
        {
            SpawnBullet(hellfirePool, dmgHellfire, transform.position, baseAngle + hellfireAngleStep * i);
            SpawnBullet(hellfirePool, dmgHellfire, transform.position, baseAngle - hellfireAngleStep * i);
        }
    }

    private void SpawnDagger(Vector2 pos, float angle)
        => SpawnBullet(daggerPool, dmgDagger, pos, angle);

    private void SpawnBullet(EasyPoolingList pool, List<float> dmg, Vector2 pos, float angle)
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

    // ── Helpers ───────────────────────────────────────────────────
    private void SetBossVisible(bool visible)
    {
        if (bossRenderer != null) bossRenderer.enabled = visible;
        if (bossCollider != null) bossCollider.enabled = visible;
    }

    private void SetShield(bool active)
    {
        if (shield != null) shield.SetActive(active);
    }

    private void FindPlayer()
    {
        var go = GameObject.FindWithTag("Player");
        if (go != null) playerTransform = go.transform;
    }

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        if (playerTransform == null) return;
        Vector3 c = playerTransform.position;

        // Checkerboard rect
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.6f);
        Gizmos.DrawLine(c + new Vector3(-cbRectW,  cbRectH, 0f), c + new Vector3( cbRectW,  cbRectH, 0f));
        Gizmos.DrawLine(c + new Vector3( cbRectW,  cbRectH, 0f), c + new Vector3( cbRectW, -cbRectH, 0f));
        Gizmos.DrawLine(c + new Vector3( cbRectW, -cbRectH, 0f), c + new Vector3(-cbRectW, -cbRectH, 0f));
        Gizmos.DrawLine(c + new Vector3(-cbRectW, -cbRectH, 0f), c + new Vector3(-cbRectW,  cbRectH, 0f));

        // Ring centers
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.8f);
        Gizmos.DrawWireSphere(c + new Vector3(-ringOffsetX, 0f, 0f), 0.2f);
        Gizmos.DrawWireSphere(c + new Vector3( ringOffsetX, 0f, 0f), 0.2f);
        Gizmos.DrawWireSphere(c + new Vector3(-ringOffsetX, 0f, 0f), ringRadiusMin);
        Gizmos.DrawWireSphere(c + new Vector3( ringOffsetX, 0f, 0f), ringRadiusMax);

        // Decagon radius
        Gizmos.color = new Color(0.8f, 0.2f, 0.8f, 0.3f);
        Gizmos.DrawWireSphere(c, decagonRadMin);
        Gizmos.color = new Color(0.8f, 0.2f, 0.8f, 0.6f);
        Gizmos.DrawWireSphere(c, decagonRadMax);
    }
}