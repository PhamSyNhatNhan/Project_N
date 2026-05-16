using System;
using System.Collections.Generic;
using UnityEngine;

public class CalamitasSkillPhase1 : MonoBehaviour
{
    // ── Callback ──────────────────────────────────────────────────
    public Action OnComplete;

    // ── Idle ──────────────────────────────────────────────────────
    [Header("Idle")]
    [SerializeField] private float idleDuration     = 3f;
    [SerializeField] private float idleFireInterval = 1f;
    [SerializeField] private float orbitDistanceX   = 4f;
    [SerializeField] private float orbitHeightY     = 2f;
    [SerializeField] private float orbitMoveSpeed   = 3f;

    // ── Skill 1 — Condemn Ring ────────────────────────────────────
    [Header("Skill 1 — Condemn Ring")]
    [SerializeField] private float warningDuration    = 1f;
    [SerializeField] private int   ringBulletCount    = 12;
    [SerializeField] private int   hellfireExtraPairs1 = 1;
    [SerializeField] private float hellfireAngleStep1  = 20f;
    [SerializeField] private float skill1SwitchDelay   = 1f;
    [SerializeField] private int   skill1RepeatCount   = 3;
    [SerializeField] private float warningLineLength   = 4f;
    [SerializeField] private float warningLineWidth    = 0.03f;
    [SerializeField] private Color warningLineColor    = new Color(1f, 0.2f, 0.2f, 0.8f);

    // ── Skill 2 — Dash ────────────────────────────────────────────
    [Header("Skill 2 — Dash")]
    [SerializeField] private GameObject dashFxObject;
    [SerializeField] private float     dashVelocity        = 800f;
    [SerializeField] private float     dashTime            = 0.2f;
    [SerializeField] private float     dashMaxDist         = 6f;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private int       hellfireExtraPairs2 = 1;
    [SerializeField] private float     hellfireAngleStep2  = 20f;
    [SerializeField] private float     skill2Delay         = 1.5f;
    [SerializeField] private int       skill2RepeatCount   = 5;

    // ── Skill 3 — Teleport ────────────────────────────────────────
    [Header("Skill 3 — Teleport")]
    [SerializeField] private float teleportRectX       = 4f;
    [SerializeField] private float teleportRectY       = 3f;
    [SerializeField] private float teleportDelay       = 0.5f;
    [SerializeField] private int   teleportCount       = 4;
    [SerializeField] private float teleportSpreadAngle = 45f;
    [SerializeField] private int   hellfireExtraPairs3 = 1;
    [SerializeField] private float hellfireAngleStep3  = 20f;

    [Header("Skill 3 — Hellblast Column")]
    [SerializeField] private float columnOffsetX = 0.4f;
    [SerializeField] private float columnOffsetY = 0.6f;

    // ── Components ────────────────────────────────────────────────
    private Stat             bossStat;
    private Move             bossMove;
    private TimeScale        timeScale;
    private Transform        playerTransform;
    private CalamitasShadow  shadow;
    private SpriteRenderer   bossRenderer;
    private Hitbox           hitbox;

    // ── Pools & Damage ────────────────────────────────────────────
    private EasyPoolingList hellfirePool;
    private EasyPoolingList condemnPool;
    private EasyPoolingList hellblastPool;
    private List<float>     dmgHellfire  = new List<float> { 250f };
    private List<float>     dmgCondemn   = new List<float> { 300f };
    private List<float>     dmgHellblast = new List<float> { 200f };
    private List<float>     dmgDash      = new List<float> { 350f };

    // ── FSM ───────────────────────────────────────────────────────
    private enum S
    {
        Idle,
        S1Warning, S1Switch,
        S2Dash, S2Delay,
        S3Teleport, S3Delay,
    }

    private static readonly S[] Rotation =
    {
        S.Idle, S.S1Warning,
        S.Idle, S.S2Dash,
        S.Idle, S.S3Teleport,
    };

    private S     cur;
    private int   rotIdx     = -1;
    private float timer;

    // Idle
    private float idleFireTimer;
    private int   orbitSide = 1;

    // Skill 1
    private int skill1Left;

    // Skill 2
    private int                    skill2Left;
    private Vector2                dashStartPos;
    private readonly HashSet<Stat> dashHits = new HashSet<Stat>();

    // Skill 3
    private int     tpLeft;
    private Vector2 tpDest;
    private bool    tpWaiting;
    private float   tpPreTimer;

    // ── Warning lines — pre-allocated pool ───────────────────────
    // FIX: không Instantiate/Destroy mỗi cycle nữa.
    // Toàn bộ LineRenderer được tạo 1 lần trong Awake(),
    // ShowWarnLines() chỉ SetActive(true) + cập nhật position,
    // ClearWarnLines() chỉ SetActive(false).
    private readonly List<LineRenderer> warnLines = new List<LineRenderer>();
    private          GameObject         warnLineRoot;

    // Cache hướng mỗi line (hằng số, tính 1 lần khi build pool)
    private Vector3[] warnLineDirs;

    // Shared material — tránh tạo Material instance mới mỗi LineRenderer
    private static Material s_warnLineMat;

    private float DeltaTime => timeScale.DeltaTime;

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

        warnLineRoot = new GameObject("Skill1WarnLines");
        warnLineRoot.transform.SetParent(null);

        BuildWarnLinePool();
    }

    private void OnEnable()
    {
        bossStat.CanDamge = true;
        rotIdx            = -1;
        orbitSide         = 1;
        tpWaiting         = false;
        if (bossRenderer != null) bossRenderer.enabled = true;
        FindPlayer();
        NextRot();
    }

    private void OnDisable()
    {
        bossMove.ResetAll();
        ClearWarnLines();
        tpWaiting = false;
        if (dashFxObject  != null) dashFxObject.SetActive(false);
        if (bossRenderer  != null) bossRenderer.enabled = true;
    }

    // ── Setup ─────────────────────────────────────────────────────
    public void LoadData(EasyPoolingList hellfire, EasyPoolingList condemn,
                         EasyPoolingList hellblast,
                         List<float> hfDmg, List<float> cdDmg,
                         List<float> hbDmg, List<float> dashDmg)
    {
        hellfirePool  = hellfire;
        condemnPool   = condemn;
        hellblastPool = hellblast;
        if (hfDmg   != null) dmgHellfire  = hfDmg;
        if (cdDmg   != null) dmgCondemn   = cdDmg;
        if (hbDmg   != null) dmgHellblast = hbDmg;
        if (dashDmg != null) dmgDash      = dashDmg;
    }

    // ── Update ────────────────────────────────────────────────────
    private void Update()
    {
        switch (cur)
        {
            case S.Idle:      UpdateIdle();      break;
            case S.S1Warning: UpdateS1Warning(); break;
            case S.S1Switch:  UpdateS1Switch();  break;
            case S.S2Dash:    UpdateS2Dash();    break;
            case S.S2Delay:   UpdateS2Delay();   break;
            case S.S3Teleport:UpdateS3Teleport();break;
            case S.S3Delay:   UpdateS3Delay();   break;
        }
    }

    private void FixedUpdate()
    {
        if (cur == S.Idle || cur == S.S1Warning || cur == S.S1Switch || cur == S.S2Delay)
            UpdateOrbit();
    }

    // ── Rotation ──────────────────────────────────────────────────
    private void NextRot()
    {
        rotIdx = (rotIdx + 1) % Rotation.Length;
        S next  = Rotation[rotIdx];

        if (next == S.S1Warning)  skill1Left = skill1RepeatCount;
        if (next == S.S2Dash)     skill2Left = skill2RepeatCount;
        if (next == S.S3Teleport) tpLeft     = teleportCount;

        Enter(next);
    }

    private void Enter(S state)
    {
        cur = state;
        switch (state)
        {
            case S.Idle:
                timer         = idleDuration;
                idleFireTimer = idleFireInterval;
                break;

            case S.S1Warning:
                timer = warningDuration;
                ShowWarnLines();
                break;

            case S.S1Switch:
                orbitSide *= -1;
                timer      = skill1SwitchDelay;
                break;

            case S.S2Dash:
                dashHits.Clear();
                dashStartPos = transform.position;
                if (dashFxObject != null) dashFxObject.SetActive(true);
                if (playerTransform != null)
                {
                    Vector2 dir       = ((Vector2)playerTransform.position
                                        - (Vector2)transform.position).normalized;
                    bossMove.MoveSnap = dir;
                    bossMove.MoveTo(dashVelocity, dashTime);
                }
                FireHellfire(AngleToPlayer(), hellfireExtraPairs2, hellfireAngleStep2);
                timer = dashTime;
                break;

            case S.S2Delay:
                timer = skill2Delay;
                break;

            case S.S3Teleport:
                StartTeleport();
                break;

            case S.S3Delay:
                timer = teleportDelay;
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // IDLE
    // ══════════════════════════════════════════════════════════════
    private void UpdateIdle()
    {
        idleFireTimer -= DeltaTime;
        if (idleFireTimer <= 0f)
        {
            idleFireTimer = idleFireInterval;
            FireHellfire(AngleToPlayer(), 0, 0f);
        }

        timer -= DeltaTime;
        if (timer <= 0f) NextRot();
    }

    private void UpdateOrbit()
    {
        if (playerTransform == null) return;

        Vector2 target = (Vector2)playerTransform.position
                       + new Vector2(orbitDistanceX * orbitSide, orbitHeightY);
        Vector2 delta  = target - (Vector2)transform.position;
        float   dist   = delta.magnitude;

        if (dist < 0.05f) { bossMove.Rb.linearVelocity = Vector2.zero; return; }

        float speed = Mathf.Min(dist * 5f, orbitMoveSpeed);
        bossMove.Rb.linearVelocity = delta.normalized * speed;
    }

    // ══════════════════════════════════════════════════════════════
    // SKILL 1
    // ══════════════════════════════════════════════════════════════
    private void UpdateS1Warning()
    {
        UpdateWarnLines();
        timer -= DeltaTime;
        if (timer > 0f) return;

        ClearWarnLines();
        FireS1Ring();
    }

    private void FireS1Ring()
    {
        for (int i = 0; i < ringBulletCount; i++)
            FireCondemn(360f / ringBulletCount * i);

        FireHellfire(AngleToPlayer(), hellfireExtraPairs1, hellfireAngleStep1);

        skill1Left--;
        if (skill1Left <= 0)
            NextRot();
        else
            Enter(S.S1Switch);
    }

    private void UpdateS1Switch()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S1Warning);
    }

    // ══════════════════════════════════════════════════════════════
    // SKILL 2
    // ══════════════════════════════════════════════════════════════
    private void UpdateS2Dash()
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
        bool tooFar  = Vector2.Distance(dashStartPos, transform.position) >= dashMaxDist;
        bool timeUp  = timer <= 0f;

        if (!tooFar && !timeUp) return;

        bossMove.StopMovement();
        if (dashFxObject != null) dashFxObject.SetActive(false);

        if (playerTransform != null)
            orbitSide = transform.position.x >= playerTransform.position.x ? 1 : -1;

        skill2Left--;
        if (skill2Left <= 0)
            NextRot();
        else
            Enter(S.S2Delay);
    }

    private void UpdateS2Delay()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S2Dash);
    }

    // ══════════════════════════════════════════════════════════════
    // SKILL 3
    // ══════════════════════════════════════════════════════════════
    private void StartTeleport()
    {
        if (playerTransform == null) return;

        float rx = UnityEngine.Random.Range(-teleportRectX, teleportRectX);
        float ry = UnityEngine.Random.Range(-teleportRectY, teleportRectY);
        tpDest   = (Vector2)playerTransform.position + new Vector2(rx, ry);

        if (bossRenderer != null) bossRenderer.enabled = false;
        shadow?.PlayDepartShadow(transform.position);
        shadow?.PlayArriveShadow(tpDest);

        tpWaiting  = true;
        tpPreTimer = shadow != null ? Mathf.Max(0f, shadow.FadeDuration - 0.1f) : 0f;
    }

    private void UpdateS3Teleport()
    {
        if (tpWaiting)
        {
            tpPreTimer -= DeltaTime;
            if (tpPreTimer > 0f) return;

            tpWaiting               = false;
            bossMove.TargetPosition = tpDest;
            bossMove.TranfromToXY(0f);
            if (bossRenderer != null) bossRenderer.enabled = true;
            timer = teleportDelay;
            return;
        }

        timer -= DeltaTime;
        if (timer > 0f) return;

        float half = teleportSpreadAngle * 0.5f;
        float ang  = AngleToPlayer() + UnityEngine.Random.Range(-half, half);
        FireHellfire(ang, hellfireExtraPairs3, hellfireAngleStep3);

        Vector2 p = transform.position;
        FireHellblastAt(p + new Vector2(-columnOffsetX,  columnOffsetY));
        FireHellblastAt(p + new Vector2( columnOffsetX,  0f           ));
        FireHellblastAt(p + new Vector2(-columnOffsetX, -columnOffsetY));

        tpLeft--;
        if (tpLeft <= 0)
            NextRot();
        else
            Enter(S.S3Delay);
    }

    private void UpdateS3Delay()
    {
        timer -= DeltaTime;
        if (timer <= 0f) Enter(S.S3Teleport);
    }

    // ── Fire Helpers ──────────────────────────────────────────────
    private void FireHellfire(float baseAngle, int pairs, float step)
    {
        Spawn(hellfirePool, dmgHellfire, baseAngle);
        for (int i = 1; i <= pairs; i++)
        {
            Spawn(hellfirePool, dmgHellfire, baseAngle + step * i);
            Spawn(hellfirePool, dmgHellfire, baseAngle - step * i);
        }
    }

    private void FireCondemn(float angle) => Spawn(condemnPool, dmgCondemn, angle);

    private void FireHellblastAt(Vector2 pos)
    {
        if (hellblastPool == null) return;
        var obj = hellblastPool.GetGameObject();
        if (obj == null) return;
        obj.transform.position = pos;
        obj.SetActive(true);
        var b = obj.GetComponent<BulletObject>();
        if (b == null) return;
        b.SetUp(DamageType.Magic, dmgHellblast,
                bossStat, bossStat.CurCritRate, bossStat.CurCritDamage);
        b.EasyModeChange(BulletMoveMode.Target);
    }

    private void Spawn(EasyPoolingList pool, List<float> dmg, float angle)
    {
        if (pool == null) return;
        var obj = pool.GetGameObject();
        if (obj == null) return;
        obj.transform.position = transform.position;
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

    private void FindPlayer()
    {
        var go = GameObject.FindWithTag("Player");
        if (go != null) playerTransform = go.transform;
    }

    // ── Warning Lines ─────────────────────────────────────────────
    private void BuildWarnLinePool()
    {
        if (s_warnLineMat == null)
        {
            var shader = Shader.Find("Sprites/Default")
                      ?? Shader.Find("UI/Default")
                      ?? Shader.Find("Unlit/Color");
            if (shader != null)
                s_warnLineMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        warnLineDirs = new Vector3[ringBulletCount];
        float step   = 360f / ringBulletCount;

        for (int i = 0; i < ringBulletCount; i++)
        {
            float   a   = step * i * Mathf.Deg2Rad;
            warnLineDirs[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);

            var go = new GameObject($"WL_{i}");
            go.transform.SetParent(warnLineRoot.transform);

            var lr              = go.AddComponent<LineRenderer>();
            lr.positionCount    = 2;
            lr.startWidth       = warningLineWidth;
            lr.endWidth         = warningLineWidth;
            lr.useWorldSpace    = true;
            lr.sortingLayerName = "Default";
            lr.sortingOrder     = 10;
            lr.sharedMaterial   = s_warnLineMat;
            lr.startColor       = warningLineColor;
            lr.endColor         = new Color(warningLineColor.r, warningLineColor.g,
                                            warningLineColor.b, 0f);
            go.SetActive(false);
            warnLines.Add(lr);
        }
    }
    
    private void ShowWarnLines()
    {
        Vector3 origin = transform.position;
        for (int i = 0; i < warnLines.Count; i++)
        {
            var lr = warnLines[i];
            if (lr == null) continue;
            lr.SetPosition(0, origin);
            lr.SetPosition(1, origin + warnLineDirs[i] * warningLineLength);
            lr.gameObject.SetActive(true);
        }
    }

    // UpdateWarnLines chỉ cập nhật origin (boss có thể đang move).
    // Không cần tính lại dir vì warnLineDirs đã cache.
    private void UpdateWarnLines()
    {
        Vector3 origin = transform.position;
        for (int i = 0; i < warnLines.Count; i++)
        {
            var lr = warnLines[i];
            if (lr == null) continue;
            lr.SetPosition(0, origin);
            lr.SetPosition(1, origin + warnLineDirs[i] * warningLineLength);
        }
    }

    private void ClearWarnLines()
    {
        foreach (var lr in warnLines)
            if (lr != null) lr.gameObject.SetActive(false);
    }

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        if (playerTransform == null) return;

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireSphere(
            playerTransform.position + new Vector3( orbitDistanceX, orbitHeightY, 0f), 0.3f);
        Gizmos.DrawWireSphere(
            playerTransform.position + new Vector3(-orbitDistanceX, orbitHeightY, 0f), 0.3f);

        Gizmos.color = new Color(0.8f, 0.3f, 1f, 0.8f);
        Vector3 c  = playerTransform.position;
        Vector3 tl = c + new Vector3(-teleportRectX,  teleportRectY, 0f);
        Vector3 tr = c + new Vector3( teleportRectX,  teleportRectY, 0f);
        Vector3 bl = c + new Vector3(-teleportRectX, -teleportRectY, 0f);
        Vector3 br = c + new Vector3( teleportRectX, -teleportRectY, 0f);
        Gizmos.DrawLine(tl, tr); Gizmos.DrawLine(tr, br);
        Gizmos.DrawLine(br, bl); Gizmos.DrawLine(bl, tl);
    }
}