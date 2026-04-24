using UnityEngine;

/// <summary>
/// Hiệu ứng idle — scale pulse + slow rotate
/// Gọi SetActive(true/false) để bật/tắt
/// </summary>
public class IdleEffect : MonoBehaviour
{
    [Header("Scale Pulse")]
    [SerializeField] private float pulseSpeed  = 2f;
    [SerializeField] private float pulseAmount = 0.08f;

    [Header("Slow Rotate")]
    [SerializeField] private float rotateSpeed = 45f;

    // ── Runtime ───────────────────────────────────────────────────
    private Vector3 baseScale;
    private bool    isActive;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    private void Update()
    {
        if (!isActive) return;

        // Scale pulse
        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
        transform.localScale = baseScale * pulse;

        // Slow rotate
        transform.Rotate(0f, 0f, rotateSpeed * Time.deltaTime);
    }

    // ── Public ────────────────────────────────────────────────────
    public void SetActive(bool active)
    {
        isActive = active;

        if (!active)
        {
            // Reset về scale gốc khi tắt
            transform.localScale = baseScale;
        }
    }

    // Cập nhật baseScale nếu scale thay đổi từ bên ngoài
    public void RefreshBaseScale() => baseScale = transform.localScale;
}