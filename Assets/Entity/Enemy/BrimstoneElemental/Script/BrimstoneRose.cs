using UnityEngine;

public class BrimstoneRose : EasyProjectileResetHitObject
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Rose")]
    [SerializeField] private GameObject brimstonePetalBulletPrefab;
    [SerializeField] private Vector2    petalOffset = new Vector2(0.2f, 0.2f);

    // ── Runtime ───────────────────────────────────────────────────
    private readonly EasyPoolingList petalPool = new EasyPoolingList();

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        if (brimstonePetalBulletPrefab != null)
            petalPool.SetPrefab(brimstonePetalBulletPrefab);
    }

    // ── Public API ────────────────────────────────────────────────
    public void Activate()
    {
        SpawnPetal(petalOffset);
        SpawnPetal(petalOffset);
        gameObject.SetActive(false);
    }

    // ── Spawn ─────────────────────────────────────────────────────
    private void SpawnPetal(Vector2 offset)
    {
        GameObject obj = petalPool.GetGameObject();
        if (obj == null) return;

        Vector2 randomOffset = new Vector2(
            Random.Range(-offset.x, offset.x),
            Random.Range(-offset.y, offset.y)
        );

        obj.transform.position = (Vector2)transform.position + randomOffset;
        obj.SetActive(true);

        var bullet = obj.GetComponent<BulletObject>();
        if (bullet != null)
            bullet.SetUp(type, damage, stat, critRate, critDamage);
    }
}