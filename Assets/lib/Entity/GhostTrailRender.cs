using System.Collections.Generic;
using UnityEngine;

/// <summary>

/// </summary>
public class GhostTrailRender : MonoBehaviour
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Auto Control")]
    [SerializeField] private bool  playOnEnable   = false;
    [SerializeField] private bool  isActive       = false;

    [Header("Spawn")]
    [SerializeField] private float ghostInterval  = 0.05f;
    [SerializeField] private GameObject ghostPrefab;      

    [Header("Ghost Appearance")]
    [SerializeField] private float ghostDuration  = 0.3f;
    [SerializeField] private float startAlpha     = 0.6f;
    [SerializeField] private bool  useCustomColor = false;
    [SerializeField] private Color ghostColor     = new Color(1f, 1f, 1f, 0.6f);

    [Header("Sort")]
    [SerializeField] private int   sortingOrderOffset = -1;

    // ── Runtime ───────────────────────────────────────────────────
    private SpriteRenderer srcRenderer;
    private float          spawnTimer;

    // ── Pool ──────────────────────────────────────────────────────
    private EasyPoolingList pool = new EasyPoolingList();

    private struct GhostFadeData
    {
        public GameObject     obj;
        public SpriteRenderer sr;
        public Color          startColor;
        public float          elapsed;
        public float          duration;
    }

    private List<GhostFadeData> activeGhosts = new List<GhostFadeData>();

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        srcRenderer = GetComponent<SpriteRenderer>();

        if (ghostPrefab == null)
        {
            ghostPrefab = new GameObject("GhostBase");
            ghostPrefab.AddComponent<SpriteRenderer>();
            ghostPrefab.SetActive(false);
        }

        pool.SetPrefab(ghostPrefab);
    }

    private void OnEnable()
    {
        if (playOnEnable) isActive = true;
        spawnTimer = 0f;
    }

    private void OnDisable()
    {
        isActive   = false;
        spawnTimer = 0f;
        KillAllGhosts();
    }

    private void Update()
    {
        TickSpawn();
        TickFade();
    }

    // ── Spawn ─────────────────────────────────────────────────────
    private void TickSpawn()
    {
        if (!isActive || srcRenderer == null) return;

        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f) return;

        spawnTimer = ghostInterval;
        SpawnGhost();
    }

    private void SpawnGhost()
    {
        GameObject obj = pool.GetGameObject();
        if (obj == null) return;

        obj.transform.position   = transform.position;
        obj.transform.rotation   = transform.rotation;
        obj.transform.localScale = transform.lossyScale;

        var sr          = obj.GetComponent<SpriteRenderer>();
        sr.sprite       = srcRenderer.sprite;
        sr.flipX        = srcRenderer.flipX;
        sr.flipY        = srcRenderer.flipY;
        sr.sortingLayerID = srcRenderer.sortingLayerID;
        sr.sortingOrder   = srcRenderer.sortingOrder + sortingOrderOffset;

        Color baseColor = useCustomColor ? ghostColor : srcRenderer.color;
        Color c         = new Color(baseColor.r, baseColor.g, baseColor.b, startAlpha);
        sr.color        = c;

        obj.SetActive(true);

        activeGhosts.Add(new GhostFadeData
        {
            obj        = obj,
            sr         = sr,
            startColor = c,
            elapsed    = 0f,
            duration   = ghostDuration
        });
    }

    // ── Fade ──────────────────────────────────────────────────────
    private void TickFade()
    {
        for (int i = activeGhosts.Count - 1; i >= 0; i--)
        {
            GhostFadeData g = activeGhosts[i];
            g.elapsed += Time.deltaTime;

            float t    = Mathf.Clamp01(g.elapsed / g.duration);
            g.sr.color = new Color(g.startColor.r, g.startColor.g, g.startColor.b,
                                   Mathf.Lerp(g.startColor.a, 0f, t));

            if (g.elapsed >= g.duration)
            {
                pool.ReturnToPool(g.obj);
                activeGhosts.RemoveAt(i);
            }
            else
            {
                activeGhosts[i] = g;
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void KillAllGhosts()
    {
        for (int i = 0; i < activeGhosts.Count; i++)
            pool.ReturnToPool(activeGhosts[i].obj);
        activeGhosts.Clear();
    }

    private void OnDestroy()
    {
        pool.ClearPool();
    }

    // ── Public API ────────────────────────────────────────────────
    public void StartTrail() => isActive = true;
    public void StopTrail()  => isActive = false;

    public bool  IsActive          { get => isActive;       set => isActive = value; }
    public float GhostInterval     { get => ghostInterval;  set => ghostInterval = value; }
    public float GhostDuration     { get => ghostDuration;  set => ghostDuration = value; }
    public float StartAlpha        { get => startAlpha;     set => startAlpha = value; }
    public bool  UseCustomColor    { get => useCustomColor; set => useCustomColor = value; }
    public Color GhostColor        { get => ghostColor;     set => ghostColor = value; }
}