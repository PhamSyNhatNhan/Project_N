using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Vòng tròn giới hạn phase 2 — spawn nhiều sprite xếp liên tiếp thành vòng.
/// Mỗi sprite xoay tiếp tuyến với vòng tròn.
/// Player ra ngoài → spawn LightDagger tấn công.
/// </summary>
public class BrimstonePhase2Circle : MonoBehaviour
{
    // ── Config Circle ─────────────────────────────────────────────
    [Header("Circle Sprites")]
    [SerializeField] private GameObject vineSpritePrefab; // prefab chứa SpriteRenderer với CharredVine
    [SerializeField] private float      radius        = 8f;
    [SerializeField] private int        vineCount     = 32;  // số sprite trên vòng
    [SerializeField] private float      vineScale     = 1f;  // scale từng sprite

    [Header("LightDagger")]
    [SerializeField] private GameObject lightDaggerPrefab;
    [SerializeField] private float      daggerInterval = 0.1f;
    [SerializeField] private float      daggerDamage   = 150f;

    // ── Runtime ───────────────────────────────────────────────────
    private List<GameObject> vines       = new List<GameObject>();
    private Transform        playerTransform;
    private Vector2          circleCenter;
    private float            daggerTimer;
    private bool             isActive    = false;

    private TimeScale timeScale;
    private Stat      stat;
    private float DeltaTime => timeScale.DeltaTime;

    private readonly EasyPoolingList daggerPool = new EasyPoolingList();

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        timeScale = GetComponent<TimeScale>();
        stat      = GetComponent<Stat>();
        if (lightDaggerPrefab != null)
            daggerPool.SetPrefab(lightDaggerPrefab);
    }

    private void OnDisable()
    {
        isActive = false;
        SetVinesVisible(false);
    }

    private void OnDestroy()
    {
        foreach (var v in vines)
            if (v != null) Destroy(v);
        daggerPool.ClearPool();
    }

    private void Update()
    {
        if (!isActive) return;
        FindPlayer();
        if (playerTransform == null) return;

        float dist = Vector2.Distance(playerTransform.position, circleCenter);
        if (dist > radius)
        {
            daggerTimer -= DeltaTime;
            if (daggerTimer <= 0f)
            {
                daggerTimer = daggerInterval;
                SpawnDagger();
            }
        }
        else
        {
            daggerTimer = 0f;
        }
    }

    // ── Public API ────────────────────────────────────────────────
    public void Activate()
    {
        circleCenter = transform.position;
        isActive     = true;
        daggerTimer  = 0f;
        BuildCircle();
        SetVinesVisible(true);
    }

    public void Deactivate()
    {
        isActive = false;
        SetVinesVisible(false);
    }

    // ── Circle Building ───────────────────────────────────────────
    private void BuildCircle()
    {
        // Tái dùng vines cũ nếu số lượng khớp, không thì tạo lại
        if (vines.Count != vineCount)
        {
            foreach (var v in vines)
                if (v != null) Destroy(v);
            vines.Clear();

            for (int i = 0; i < vineCount; i++)
            {
                var obj = Instantiate(vineSpritePrefab);
                obj.transform.parent = null;
                obj.SetActive(false);
                vines.Add(obj);
            }
        }

        for (int i = 0; i < vineCount; i++)
        {
            float   angle  = 360f / vineCount * i;
            float   rad    = angle * Mathf.Deg2Rad;
            Vector3 pos    = new Vector3(
                circleCenter.x + Mathf.Cos(rad) * radius,
                circleCenter.y + Mathf.Sin(rad) * radius,
                0f);

            var v = vines[i];
            v.transform.position   = pos;
            v.transform.rotation   = Quaternion.Euler(0f, 0f, angle + 90f); // tiếp tuyến
            v.transform.localScale = Vector3.one * vineScale;
        }
    }

    private void SetVinesVisible(bool visible)
    {
        foreach (var v in vines)
            if (v != null) v.SetActive(visible);
    }

    // ── Spawn Dagger ──────────────────────────────────────────────
    private void SpawnDagger()
    {
        if (playerTransform == null) return;

        float   angle    = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        Vector2 spawnPos = circleCenter
                         + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

        GameObject obj = daggerPool.GetGameObject();
        if (obj == null) return;

        obj.transform.position = spawnPos;
        obj.SetActive(true);

        var dagger = obj.GetComponent<LightDagger>();
        if (dagger != null)
        {
            Vector2 toPlayer = ((Vector2)playerTransform.position - spawnPos).normalized;
            float   aimAngle = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg;
            dagger.SetUp(DamageType.Magic,
                         new List<float> { daggerDamage },
                         stat,
                         stat != null ? stat.CurCritRate   : 5f,
                         stat != null ? stat.CurCritDamage : 50f);
            dagger.SetDirection(aimAngle);
            dagger.Activate();
        }
    }

    private void FindPlayer()
    {
        if (playerTransform != null) return;
        var go = GameObject.FindWithTag("Player");
        if (go != null) playerTransform = go.transform;
    }

    // ── Gizmos ────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.15f);
        Gizmos.DrawSphere(transform.position, radius);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}