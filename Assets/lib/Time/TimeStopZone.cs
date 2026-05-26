using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Poolable object quản lý timestop zone.
/// Dùng Physics2D.OverlapCircleAll mỗi frame — đảm bảo catch bullet mới spawn.
/// Lưu list object đang bị slow, giải phóng khi disable.
/// Skill gọi Activate(duration) để bật, tự disable sau duration.
/// </summary>
public class TimeStopZone : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private float     radius      = 15f;
    [SerializeField] private LayerMask affectLayer;
    [SerializeField] private float     slowScale   = 0f;

    private float                    durationTimer  = 0f;
    private HashSet<TimeScale> affectedObjects = new HashSet<TimeScale>();

    // ── API ───────────────────────────────────────────────────────
    public void Activate(float duration)
    {
        durationTimer = duration;
        gameObject.SetActive(true);
    }

    // ── Lifecycle ─────────────────────────────────────────────────
    private void OnDisable()
    {
        // Giải phóng tất cả object đang bị slow
        foreach (var ts in affectedObjects)
            if (ts != null)
                ts.RemoveModifier("timestop");

        affectedObjects.Clear();
        durationTimer = 0f;
    }

    private void Update()
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, radius, affectLayer);
        foreach (var hit in hits)
        {
            var ts = hit.GetComponent<TimeScale>();
            if (ts == null) continue;

            if (!ts.HasModifier("timestop"))
            {
                ts.AddModifier("timestop", slowScale);
                affectedObjects.Add(ts);
            }
        }

        durationTimer -= Time.unscaledDeltaTime;
        if (durationTimer <= 0f)
            gameObject.SetActive(false);
    }

    // ── Gizmo ─────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.5f, 0f, 1f, 0.15f);
        Gizmos.DrawSphere(transform.position, radius);
        Gizmos.color = new Color(0.5f, 0f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}