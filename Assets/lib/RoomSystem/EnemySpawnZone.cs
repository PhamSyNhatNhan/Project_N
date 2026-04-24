using UnityEngine;

/// <summary>
/// Component đánh dấu vùng/điểm spawn enemy trong scene.
/// Gắn lên RoomManager hoặc GameObject riêng.
///
/// Mode Points : nhiều Transform con, random không trùng điểm liên tiếp.
/// Mode Zone   : 1 vùng hình chữ nhật hoặc tròn, random trong vùng.
/// </summary>
public class EnemySpawnZone : MonoBehaviour
{
    public enum SpawnMode { Points, Zone }
    public enum ZoneShape { Rectangle, Circle }

    [Header("Mode")]
    [SerializeField] private SpawnMode spawnMode = SpawnMode.Points;

    // ── Points ────────────────────────────────────────────────────
    [Header("Points Mode")]
    [Tooltip("Danh sách điểm spawn — kéo Transform vào đây")]
    [SerializeField] private Transform[] spawnPoints;

    // ── Zone ──────────────────────────────────────────────────────
    [Header("Zone Mode")]
    [SerializeField] private ZoneShape zoneShape    = ZoneShape.Rectangle;
    [SerializeField] private Vector2   zoneSize     = new Vector2(10f, 6f); // width x height (Rectangle)
    [SerializeField] private float     zoneRadius   = 5f;                   // (Circle)

    // ── Runtime ───────────────────────────────────────────────────
    private int _lastPointIndex = -1;

    // ── Public API ────────────────────────────────────────────────
    /// <summary>
    /// Trả về vị trí spawn tiếp theo.
    /// Points: random, không trùng điểm vừa dùng.
    /// Zone  : random trong vùng.
    /// </summary>
    public Vector3 GetSpawnPosition()
    {
        return spawnMode == SpawnMode.Points
            ? GetPointPosition()
            : GetZonePosition();
    }

    // ── Points ────────────────────────────────────────────────────
    private Vector3 GetPointPosition()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("[EnemySpawnZone] Không có SpawnPoint nào — dùng vị trí gốc.");
            return transform.position;
        }

        if (spawnPoints.Length == 1)
            return spawnPoints[0].position;

        int index;
        do { index = Random.Range(0, spawnPoints.Length); }
        while (index == _lastPointIndex);

        _lastPointIndex = index;
        return spawnPoints[index].position;
    }

    // ── Zone ──────────────────────────────────────────────────────
    private Vector3 GetZonePosition()
    {
        Vector3 center = transform.position;

        if (zoneShape == ZoneShape.Rectangle)
        {
            float x = Random.Range(-zoneSize.x / 2f, zoneSize.x / 2f);
            float y = Random.Range(-zoneSize.y / 2f, zoneSize.y / 2f);
            return center + new Vector3(x, y, 0f);
        }
        else // Circle
        {
            Vector2 offset = Random.insideUnitCircle * zoneRadius;
            return center + new Vector3(offset.x, offset.y, 0f);
        }
    }

    // ── Gizmos ────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (spawnMode == SpawnMode.Points)
        {
            Gizmos.color = Color.cyan;
            if (spawnPoints == null) return;
            foreach (var point in spawnPoints)
            {
                if (point == null) continue;
                Gizmos.DrawWireSphere(point.position, 0.3f);
                Gizmos.DrawLine(transform.position, point.position);
            }
        }
        else
        {
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.3f);
            if (zoneShape == ZoneShape.Rectangle)
            {
                // Fill
                Gizmos.DrawCube(transform.position, new Vector3(zoneSize.x, zoneSize.y, 0f));
                // Outline
                Gizmos.color = new Color(0f, 1f, 0.5f, 1f);
                Gizmos.DrawWireCube(transform.position, new Vector3(zoneSize.x, zoneSize.y, 0f));
            }
            else
            {
                // Fill
                Gizmos.DrawSphere(transform.position, zoneRadius);
                // Outline
                Gizmos.color = new Color(0f, 1f, 0.5f, 1f);
                Gizmos.DrawWireSphere(transform.position, zoneRadius);
            }
        }
    }
#endif
}