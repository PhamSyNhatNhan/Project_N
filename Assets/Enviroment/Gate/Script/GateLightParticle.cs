using System.Collections.Generic;
using UnityEngine;

public class GateLightParticle : MonoBehaviour
{
    [Header("Sprites")]
    [SerializeField] private Sprite[] particleSprites;

    [Header("Spawn Zone (local)")]
    [SerializeField] private Vector2 spawnZoneSize = new Vector2(2f, 3f);

    [Header("Spawn Settings")]
    [SerializeField] private float spawnInterval = 0.1f;
    [SerializeField] private int   maxParticles  = 30;
    [SerializeField] private int   spawnPerWave  = 2;

    [Header("Particle Settings")]
    [SerializeField] private float speedMin    = 1f;
    [SerializeField] private float speedMax    = 3f;
    [SerializeField] private float lifetimeMin = 0.6f;
    [SerializeField] private float lifetimeMax = 1.4f;
    [SerializeField] private float sizeMin     = 0.1f;
    [SerializeField] private float sizeMax     = 0.35f;

    // ── Pooling ──────────────────────────────────────────────
    private List<EasyPoolingList> pools  = new List<EasyPoolingList>();
    private List<ParticleData>    active = new List<ParticleData>();

    // ── Runtime ──────────────────────────────────────────────
    private float spawnTimer;
    private int   spawnIndex;

    // ─────────────────────────────────────────────────────────
    struct ParticleData
    {
        public GameObject go;
        public Vector2    velocity;
        public float      lifetime;
        public float      elapsed;
        public float      startAlpha;
        public int        poolIndex;
    }

    // ─────────────────────────────────────────────────────────
    void Awake()
    {
        BuildPools();
    }

    void BuildPools()
    {
        pools.Clear();
        if (particleSprites == null || particleSprites.Length == 0) return;

        foreach (Sprite sprite in particleSprites)
        {
            GameObject prefab = new GameObject("LightParticle_" + sprite.name);
            prefab.SetActive(false);
            prefab.transform.SetParent(this.transform);

            SpriteRenderer sr = prefab.AddComponent<SpriteRenderer>();
            sr.sprite       = sprite;
            sr.material     = new Material(Shader.Find("Sprites/Default"));
            sr.sortingOrder = 10;

            EasyPoolingList pool = new EasyPoolingList();
            pool.SetPrefab(prefab);
            pools.Add(pool);
        }
    }

    void Update()
    {
        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval)
        {
            spawnTimer = 0f;
            TrySpawn();
        }

        TickParticles();
    }

    // ── Spawn ─────────────────────────────────────────────────
    void TrySpawn()
    {
        if (active.Count >= maxParticles || pools.Count == 0) return;

        int count = Mathf.Min(spawnPerWave, maxParticles - active.Count);
        for (int i = 0; i < count; i++) SpawnOne();
    }

    void SpawnOne()
    {
        int poolIndex = Random.Range(0, pools.Count);
        GameObject go = pools[poolIndex].GetGameObject();
        if (go == null) return;

        Vector2 localOffset = new Vector2(
            Random.Range(-spawnZoneSize.x * 0.5f, spawnZoneSize.x * 0.5f),
            Random.Range(-spawnZoneSize.y * 0.5f, spawnZoneSize.y * 0.5f)
        );
        go.transform.position = transform.position + (Vector3)localOffset;

        float size = Random.Range(sizeMin, sizeMax);
        go.transform.localScale = Vector3.one * size;

        Vector2 baseDir = localOffset == Vector2.zero 
            ? Random.insideUnitCircle.normalized 
            : localOffset.normalized;

        float jitterAngle = Random.Range(-30f, 30f); 
        float rad         = jitterAngle * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(
            baseDir.x * Mathf.Cos(rad) - baseDir.y * Mathf.Sin(rad),
            baseDir.x * Mathf.Sin(rad) + baseDir.y * Mathf.Cos(rad)
        ).normalized;

        float speed = Random.Range(speedMin, speedMax);

        SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
        Color c = sr.color;
        c.a      = 1f;
        sr.color = c;

        go.SetActive(true);

        active.Add(new ParticleData
        {
            go         = go,
            velocity   = dir * speed,
            lifetime   = Random.Range(lifetimeMin, lifetimeMax),
            elapsed    = 0f,
            startAlpha = 1f,
            poolIndex  = poolIndex
        });
    }

    // ── Tick ─────────────────────────────────────────────────
    void TickParticles()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            ParticleData p = active[i];
            p.elapsed += Time.deltaTime;

            if (p.elapsed >= p.lifetime)
            {
                pools[p.poolIndex].ReturnToPool(p.go);
                active.RemoveAt(i);
                continue;
            }

            p.go.transform.position += (Vector3)(p.velocity * Time.deltaTime);

            float t = p.elapsed / p.lifetime;
            SpriteRenderer sr = p.go.GetComponent<SpriteRenderer>();
            Color c = sr.color;
            c.a     = p.startAlpha * (1f - Mathf.Pow(t, 2f));
            sr.color = c;

            active[i] = p;
        }
    }

    // ── Gizmo ─────────────────────────────────────────────────
#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.4f);
        Gizmos.DrawWireCube(transform.position, new Vector3(spawnZoneSize.x, spawnZoneSize.y, 0f));
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.08f);
        Gizmos.DrawCube(transform.position, new Vector3(spawnZoneSize.x, spawnZoneSize.y, 0f));
    }
#endif
}