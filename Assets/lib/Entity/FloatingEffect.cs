using UnityEngine;

/// <summary>
/// Làm entity lơ lửng lên xuống theo sin wave.
/// Gắn lên GameObject cần hiệu ứng, không phụ thuộc vào logic khác.
/// </summary>
public class FloatingEffect : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private float amplitude = 0.3f;   // biên độ lên xuống
    [SerializeField] private float frequency = 1.5f;   // tốc độ dao động
    [SerializeField] private float phaseOffset = 0f;   // lệch pha ban đầu (tránh nhiều entity đồng bộ)

    private Vector3 originLocalPos;
    private bool    initialized = false;

    private void OnEnable()
    {
        originLocalPos = transform.localPosition;
        initialized    = true;
    }

    private void OnDisable()
    {
        if (initialized)
            transform.localPosition = originLocalPos;
    }

    private void Update()
    {
        if (!initialized) return;

        float offsetY = Mathf.Sin(Time.time * frequency * Mathf.PI * 2f + phaseOffset) * amplitude;
        transform.localPosition = originLocalPos + new Vector3(0f, offsetY, 0f);
    }
}