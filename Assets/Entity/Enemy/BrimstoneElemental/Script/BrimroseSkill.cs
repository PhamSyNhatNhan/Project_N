using System.Collections.Generic;
using UnityEngine;

public class BrimroseSkill : MonoBehaviour
{
    // ── Config General ────────────────────────────────────────────
    [Header("General")]
    [SerializeField] private float startDelay = 2.5f;
    [SerializeField] private float stepDelay  = 1.0f;

    // ── Option 1 — Nova Pulse ─────────────────────────────────────
    [Header("Option1 - Nova Pulse")]
    [SerializeField] private GameObject novaFireballPrefab;
    [SerializeField] private int        novaCount        = 12;
    [SerializeField] private float      novaRotateOffset = 15f;
    [SerializeField] private int        novaRepeat       = 5;
    [SerializeField] private float      novaInterval     = 0.4f;

    // ── Option 2 — Cross Sweep ────────────────────────────────────
    [Header("Option2 - Cross Sweep")]
    [SerializeField] private Transform  laserPivot;
    [SerializeField] private float      laserWait        = 0.8f;
    [SerializeField] private float      laserRotateSpeed = 60f;
    [SerializeField] private int        laserTurns       = 3;
    [SerializeField] private GameObject ringDartPrefab;
    [SerializeField] private int        ringCount        = 12;
    [SerializeField] private int        ringLayers       = 3;
    [SerializeField] private float      ringLayerOffset  = 10f;
    [SerializeField] private float      ringFireAngle    = 90f;   // bắn mỗi khi xoay được N độ

    // ── Option 3 — Cross Dart + Corner Blast ─────────────────────
    [Header("Option3 - Cross Dart")]
    [SerializeField] private GameObject dartPrefab;
    [SerializeField] private int        dartLineCount   = 10;
    [SerializeField] private float      dartLineSpacing = 1.2f;
    [SerializeField] private float      dartLineWarning = 0.8f;
    [SerializeField] private float      dartLineWidth   = 0.03f;

    [Header("Option3 - Corner Blast")]
    [SerializeField] private GameObject hellblastPrefab;
    [SerializeField] private Vector2    cornerOffset    = new Vector2(2f, 2f);
    [SerializeField] private int        cornerCount     = 4;
    [SerializeField] private int        cornerExtra     = 1;
    [SerializeField] private float      cornerInterval  = 1.2f;
    [SerializeField] private int        fanCount        = 5;
    [SerializeField] private float      fanAngle        = 15f;

    // 4 hướng offset xoay vòng
    private static readonly Vector2[] CornerDirs =
    {
        new Vector2( 1f,  1f),
        new Vector2(-1f,  1f),
        new Vector2(-1f, -1f),
        new Vector2( 1f, -1f)
    };

    // ── FSM ───────────────────────────────────────────────────────
    private enum Phase
    {
        Idle, StartDelay,
        Option1, Delay12,
        Option2Wait, Option2, Delay23,
        Option3, Done
    }

    private Phase  curPhase = Phase.Idle;
    private float  phaseTimer;
    private float  subTimer;
    private int    repeatLeft;

    // Option1
    private float novaCurrentAngle = 0f;

    // Option2
    private float laserTotalAngle  = 0f;
    private float lastFireAngle    = 0f;
    private float ringCurrentAngle = 0f;

    // Option3
    private bool    opt3IsWarning    = false;
    private int     opt3CornerIndex  = 0;
    private int     opt3TotalCorners = 0;
    private LineRenderer crossH;
    private LineRenderer crossV;

    // ── Runtime ───────────────────────────────────────────────────
    private System.Action onEndCallback;
    private TimeScale     timeScale;
    private Stat          stat;
    private float DeltaTime => timeScale.DeltaTime;

    private List<float> dmgNova      = new List<float>();
    private List<float> dmgLaser     = new List<float>();
    private List<float> dmgRingDart  = new List<float>();
    private List<float> dmgDart      = new List<float>();
    private List<float> dmgHellblast = new List<float>();

    private readonly EasyPoolingList novaPool      = new EasyPoolingList();
    private readonly EasyPoolingList ringDartPool  = new EasyPoolingList();
    private readonly EasyPoolingList dartPool      = new EasyPoolingList();
    private readonly EasyPoolingList hellblastPool = new EasyPoolingList();

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        timeScale = GetComponent<TimeScale>();
        stat      = GetComponent<Stat>();
        crossH    = CreateLine("BrimCrossH", Color.red);
        crossV    = CreateLine("BrimCrossV", Color.red);
        crossH.gameObject.SetActive(false);
        crossV.gameObject.SetActive(false);
    }

    private void Start()
    {
        if (novaFireballPrefab) novaPool.SetPrefab(novaFireballPrefab);
        if (ringDartPrefab)     ringDartPool.SetPrefab(ringDartPrefab);
        if (dartPrefab)         dartPool.SetPrefab(dartPrefab);
        if (hellblastPrefab)    hellblastPool.SetPrefab(hellblastPrefab);
        if (laserPivot != null) laserPivot.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        crossH?.gameObject.SetActive(false);
        crossV?.gameObject.SetActive(false);
        if (laserPivot != null) laserPivot.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (crossH != null) Destroy(crossH.gameObject);
        if (crossV != null) Destroy(crossV.gameObject);
    }

    private void Update()
    {
        switch (curPhase)
        {
            case Phase.StartDelay:  TickDelay(Phase.Option1);    break;
            case Phase.Option1:     UpdateOption1();              break;
            case Phase.Delay12:     TickDelay(Phase.Option2Wait); break;
            case Phase.Option2Wait: TickDelay(Phase.Option2);    break;
            case Phase.Option2:     UpdateOption2();              break;
            case Phase.Delay23:     TickDelay(Phase.Option3);    break;
            case Phase.Option3:     UpdateOption3();              break;
        }
    }

    // ── Public API ────────────────────────────────────────────────
    public void StartBrimrose(System.Action endCallback)
    {
        onEndCallback    = endCallback;
        novaCurrentAngle = 0f;
        EnterPhase(Phase.StartDelay);
    }

    public void LoadData(List<float> nova, List<float> laser, List<float> ringDart,
                         List<float> dart, List<float> hellblast)
    {
        dmgNova      = nova      ?? new List<float> { 250f };
        dmgLaser     = laser     ?? new List<float> { 180f };
        dmgRingDart  = ringDart  ?? new List<float> { 150f };
        dmgDart      = dart      ?? new List<float> { 150f };
        dmgHellblast = hellblast ?? new List<float> { 300f };
        
    }

    // ── Phase Control ─────────────────────────────────────────────
    private void EnterPhase(Phase phase)
    {
        curPhase = phase;
        switch (phase)
        {
            case Phase.StartDelay:
                phaseTimer = startDelay;
                break;

            case Phase.Option1:
                repeatLeft       = novaRepeat;
                subTimer         = 0f;
                break;

            case Phase.Delay12:
            case Phase.Delay23:
                phaseTimer = stepDelay;
                break;

            case Phase.Option2Wait:
                phaseTimer = laserWait;
                break;

            case Phase.Option2:
                laserTotalAngle  = 0f;
                lastFireAngle    = 0f;
                ringCurrentAngle = 0f;
                if (laserPivot != null)
                {
                    // Setup damage cho tất cả BrimstoneLaser con
                    foreach (var laser in laserPivot.GetComponentsInChildren<BrimstoneLaser>(true))
                    {
                        laser.SetUp(DamageType.Magic, dmgLaser, stat,
                                    stat != null ? stat.CurCritRate   : 5f,
                                    stat != null ? stat.CurCritDamage : 50f);
                    }
                    laserPivot.gameObject.SetActive(true);
                }
                break;

            case Phase.Option3:
                opt3IsWarning    = false;
                opt3CornerIndex  = 0;
                opt3TotalCorners = cornerCount + cornerExtra;
                subTimer         = 0f;
                break;

            case Phase.Done:
                if (laserPivot != null) laserPivot.gameObject.SetActive(false);
                onEndCallback?.Invoke();
                break;
        }
    }

    private void TickDelay(Phase next)
    {
        phaseTimer -= DeltaTime;
        if (phaseTimer <= 0f) EnterPhase(next);
    }

    // ══════════════════════════════════════════════════════════════
    // OPTION 1 — Nova Pulse
    // ══════════════════════════════════════════════════════════════
    private void UpdateOption1()
    {
        subTimer -= DeltaTime;
        if (subTimer > 0f) return;
        subTimer = novaInterval;

        SpawnNovaRing(novaCurrentAngle);
        novaCurrentAngle += novaRotateOffset;

        repeatLeft--;
        if (repeatLeft <= 0) EnterPhase(Phase.Delay12);
    }

    private void SpawnNovaRing(float baseAngle)
    {
        for (int i = 0; i < novaCount; i++)
        {
            float      angle = baseAngle + 360f / novaCount * i;
            GameObject obj   = novaPool.GetGameObject();
            if (obj == null) continue;

            obj.transform.position = transform.position;
            obj.SetActive(true);

            var bullet = obj.GetComponent<BulletObject>();
            if (bullet == null) continue;
            bullet.SetUp(DamageType.Magic, dmgNova, stat,
                         stat != null ? stat.CurCritRate : 5f,
                         stat != null ? stat.CurCritDamage : 50f);
            bullet.EasyModeChange(BulletMoveMode.Angle, angle);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // OPTION 2 — Cross Sweep
    // ══════════════════════════════════════════════════════════════
    private void UpdateOption2()
    {
        float rot = laserRotateSpeed * DeltaTime;
        if (laserPivot != null) laserPivot.Rotate(0f, 0f, rot);
        laserTotalAngle += rot;

        // Bắn mỗi khi tích lũy đủ ringFireAngle
        if (laserTotalAngle - lastFireAngle >= ringFireAngle)
        {
            lastFireAngle += ringFireAngle;
            SpawnRingDarts();
        }

        // Kết thúc sau đủ số vòng
        if (laserTotalAngle >= laserTurns * 360f)
        {
            if (laserPivot != null) laserPivot.gameObject.SetActive(false);
            EnterPhase(Phase.Delay23);
        }
    }

    private void SpawnRingDarts()
    {
        for (int layer = 0; layer < ringLayers; layer++)
        {
            float baseAngle = ringCurrentAngle + layer * ringLayerOffset;
            for (int i = 0; i < ringCount; i++)
            {
                float      angle = baseAngle + 360f / ringCount * i;
                GameObject obj   = ringDartPool.GetGameObject();
                if (obj == null) continue;

                obj.transform.position = transform.position;
                obj.SetActive(true);

                var bullet = obj.GetComponent<BulletObject>();
                if (bullet == null) continue;
                bullet.SetUp(DamageType.Physical, dmgRingDart, stat,
                             stat != null ? stat.CurCritRate : 5f,
                             stat != null ? stat.CurCritDamage : 50f);
                bullet.EasyModeChange(BulletMoveMode.Angle, angle);
            }
        }
        ringCurrentAngle += ringLayerOffset;
    }

    // ══════════════════════════════════════════════════════════════
    // OPTION 3 — Cross Dart + Corner Blast
    // ══════════════════════════════════════════════════════════════
    private void UpdateOption3()
    {
        subTimer -= DeltaTime;
        if (subTimer > 0f) return;

        if (opt3IsWarning)
        {
            // Hết warning → teleport đến góc + bắn
            opt3IsWarning = false;
            crossH.gameObject.SetActive(false);
            crossV.gameObject.SetActive(false);

            Vector2 corner = CalcCorner(opt3CornerIndex);
            transform.position = corner;
            SpawnCrossDarts();
            FireFanBlast(corner);

            opt3CornerIndex++;
            if (opt3CornerIndex >= opt3TotalCorners)
            {
                EnterPhase(Phase.Done);
                return;
            }

            subTimer = cornerInterval;
        }
        else
        {
            // Hiện warning cross tại vị trí góc tiếp theo
            opt3IsWarning = true;
            ShowCrossAt(CalcCorner(opt3CornerIndex));
            subTimer = dartLineWarning;
        }
    }

    private Vector2 CalcCorner(int index)
    {
        Vector2 dir      = CornerDirs[index % 4];
        var     playerGo = GameObject.FindWithTag("Player");
        Vector2 pPos     = playerGo != null
                           ? (Vector2)playerGo.transform.position
                           : (Vector2)transform.position;
        return pPos + new Vector2(dir.x * cornerOffset.x, dir.y * cornerOffset.y);
    }

    private void ShowCrossAt(Vector2 center)
    {
        float   half = (dartLineCount - 1) * dartLineSpacing * 0.5f;
        Vector3 pos  = center;
        crossH.SetPosition(0, pos + Vector3.left  * half);
        crossH.SetPosition(1, pos + Vector3.right * half);
        crossH.gameObject.SetActive(true);
        crossV.SetPosition(0, pos + Vector3.down * half);
        crossV.SetPosition(1, pos + Vector3.up   * half);
        crossV.gameObject.SetActive(true);
    }

    private void SpawnCrossDarts()
    {
        float   half     = (dartLineCount - 1) * dartLineSpacing * 0.5f;
        float[] angles   = { 90f, 270f, 180f, 0f };
        Vector2[] perps  = { Vector2.right, Vector2.right, Vector2.up, Vector2.up };

        for (int d = 0; d < 4; d++)
        {
            for (int i = 0; i < dartLineCount; i++)
            {
                float      offset   = -half + i * dartLineSpacing;
                Vector2    spawnPos = (Vector2)transform.position + perps[d] * offset;
                GameObject obj      = dartPool.GetGameObject();
                if (obj == null) continue;

                obj.transform.position = spawnPos;
                obj.SetActive(true);

                var bullet = obj.GetComponent<BulletObject>();
                if (bullet == null) continue;
                bullet.SetUp(DamageType.Physical, dmgDart, stat,
                             stat != null ? stat.CurCritRate : 5f,
                             stat != null ? stat.CurCritDamage : 50f);
                bullet.EasyModeChange(BulletMoveMode.Angle, angles[d]);
            }
        }
    }

    private void FireFanBlast(Vector2 from)
    {
        var     playerGo = GameObject.FindWithTag("Player");
        Vector2 toPlayer = playerGo != null
                           ? ((Vector2)playerGo.transform.position - from).normalized
                           : Vector2.right;

        float baseAngle   = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg;
        float startAngle  = baseAngle - fanAngle * (fanCount - 1) * 0.5f;

        for (int i = 0; i < fanCount; i++)
        {
            float      angle = startAngle + fanAngle * i;
            GameObject obj   = hellblastPool.GetGameObject();
            if (obj == null) continue;

            obj.transform.position = from;
            obj.SetActive(true);

            var bullet = obj.GetComponent<BulletObject>();
            if (bullet == null) continue;
            bullet.SetUp(DamageType.Magic, dmgHellblast, stat,
                         stat != null ? stat.CurCritRate : 5f,
                         stat != null ? stat.CurCritDamage : 50f);
            bullet.EasyModeChange(BulletMoveMode.Angle, angle);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────
    private LineRenderer CreateLine(string objName, Color color)
    {
        var go = new GameObject(objName);
        go.transform.parent = null;
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.startWidth    = dartLineWidth;
        lr.endWidth      = dartLineWidth;
        lr.material      = new Material(Shader.Find("Sprites/Default"));
        lr.startColor    = color;
        lr.endColor      = color;
        lr.useWorldSpace = true;
        return lr;
    }
}