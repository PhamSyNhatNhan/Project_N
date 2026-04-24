using System.Collections;
using UnityEngine;

public class EnemyStat : Stat
{
    [SerializeField] protected EnemyType type = EnemyType.Normal;

    [Header("FX")]
    [SerializeField] private GameObject fxSpawn;
    [SerializeField] private GameObject fxDead;

    [Header("Spawn")]
    [Tooltip("Quái có sẵn trên sân — bỏ qua spawn effect lần đầu")]
    [SerializeField] private bool    isPreplaced    = false;
    [SerializeField] private Vector2 spawnFxOffset  = Vector2.zero;

    // ── Cache ─────────────────────────────────────────────────────
    private Renderer[]   renderers;
    private Collider2D[] colliders;

    protected GameObject fxDeadInstance;
    protected GameObject fxSpawnInstance;

    protected bool isDead;
    protected bool isInitialized;
    protected bool isSpawning;

    // ── Components ────────────────────────────────────────────────
    private StatusEffectManager effectManager;
    private AutoDisableNotify   spawnNotify;

    protected float spawnFxBaseSize    = 1f;
    protected float fxDeadBaseSize     = 1f;
    protected float cachedEnemySizeX;
    protected float cachedBottomOffset;

    // ── Init ──────────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        renderers     = GetComponentsInChildren<Renderer>();
        colliders     = GetComponentsInChildren<Collider2D>();
        effectManager = GetComponent<StatusEffectManager>();

        Bounds initBounds  = GetCombinedBounds();
        cachedEnemySizeX   = initBounds.size.x;
        cachedBottomOffset = initBounds.min.y - transform.position.y;

        if (fxSpawn != null)
        {
            fxSpawnInstance = Instantiate(fxSpawn);

            var spawnCol = fxSpawnInstance.GetComponent<Collider2D>();
            if (spawnCol != null)
                spawnFxBaseSize = Mathf.Max(spawnCol.bounds.size.x, 0.01f);

            fxSpawnInstance.SetActive(false);
            spawnNotify = fxSpawnInstance.GetComponent<AutoDisableNotify>();
            if (spawnNotify != null)
                spawnNotify.OnFinished += OnSpawnFinished;
        }

        if (fxDead != null)
        {
            fxDeadInstance = Instantiate(fxDead);

            var deadCol = fxDeadInstance.GetComponent<Collider2D>();
            if (deadCol != null)
                fxDeadBaseSize = Mathf.Max(deadCol.bounds.size.x, 0.01f);

            fxDeadInstance.SetActive(false);
        }
    }

    // ── Enable / Disable ──────────────────────────────────────────
    private void OnEnable()
    {
        if (!isInitialized) return;

        if (isSpawning)
        {
            isSpawning = false;
            SetVisible(true);
            return;
        }

        setStartStat();
    }

    private void OnDisable()
    {
        if (effectManager != null)
            effectManager.RemoveAll();

        if (!isSpawning && fxSpawnInstance != null)
            fxSpawnInstance.SetActive(false);

        StopAllCoroutines();
    }

    private void OnDestroy()
    {
        if (spawnNotify != null)
            spawnNotify.OnFinished -= OnSpawnFinished;
    }

    // ── Stat ──────────────────────────────────────────────────────
    protected override void setStartStat()
    {
        isDead = false;
        base.setStartStat();
        isInitialized = true;

        EventManager.Entity.OnEntityHealthChanged
            .Get(entityKey)
            .Invoke(this, CurHealth / MaxHealth);

        if (isPreplaced)
        {
            SetVisible(true);
            isPreplaced = false;
            return;
        }

        PlaySpawnFx();
    }

    // ── FX Virtual Methods ────────────────────────────────────────
    protected virtual void PlaySpawnFx()
    {
        if (fxSpawnInstance == null) return;

        float scale = (cachedEnemySizeX / spawnFxBaseSize) * 2f;

        SetVisible(false);
        isSpawning = true;

        Vector2 spawnPos = new Vector2(
            transform.position.x + spawnFxOffset.x,
            transform.position.y + cachedBottomOffset + spawnFxOffset.y
        );
        fxSpawnInstance.transform.position   = spawnPos;
        fxSpawnInstance.transform.localScale = Vector3.one * scale;
        fxSpawnInstance.SetActive(true);

        gameObject.SetActive(false);
    }

    protected virtual void PlayDeadFx()
    {
        if (fxDeadInstance == null) return;

        float scale = (cachedEnemySizeX / fxDeadBaseSize) * 2f;

        fxDeadInstance.transform.position   = transform.position;
        fxDeadInstance.transform.rotation   = Quaternion.identity;
        fxDeadInstance.transform.localScale = Vector3.one * scale;
        fxDeadInstance.SetActive(true);
    }

    // ── Spawn callback ────────────────────────────────────────────
    private void OnSpawnFinished()
    {
        gameObject.SetActive(true);
    }

    // ── Dead ──────────────────────────────────────────────────────
    protected override void OnDead()
    {
        if (isDead) return;
        isDead = true;

        foreach (var col in colliders) col.enabled = false;

        StartCoroutine(PlayDeadThenDisable());
        EventManager.Entity.OnEntityDead.Get(entityKey).Invoke(this, null);
    }

    private IEnumerator PlayDeadThenDisable()
    {
        PlayDeadFx();
        yield return null;
        gameObject.SetActive(false);
    }

    // ── Helper ────────────────────────────────────────────────────
    protected void SetVisible(bool visible)
    {
        foreach (var r in renderers) r.enabled = visible;
        foreach (var c in colliders) c.enabled = visible;
    }

    private Bounds GetCombinedBounds()
    {
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        foreach (var col in colliders)
        {
            if (col != null)
                bounds.Encapsulate(col.bounds);
        }
        return bounds;
    }
}