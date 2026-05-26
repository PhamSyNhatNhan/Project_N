using UnityEngine;

/// <summary>
/// Tự động pulse alpha + scale (breathing) liên tục.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class DaelGlowPulse : MonoBehaviour
{
    [Header("Pulse Speed")]
    [Range(0.1f, 5f)] public float speed = 1.5f;

    [Header("Alpha Breath")]
    [Range(0f, 1f)] public float alphaMin = 0.2f;
    [Range(0f, 1f)] public float alphaMax = 0.9f;

    [Header("Scale Breath")]
    public bool  scaleBreath    = true;
    [Range(0f, 0.3f)] public float scaleAmount = 0.08f; 
    Vector3 _baseScale;

    SpriteRenderer _sr;

    void Awake()
    {
        _sr        = GetComponent<SpriteRenderer>();
        _baseScale = transform.localScale;
    }

    void Update()
    {
        // Cùng 1 sóng sine cho cả alpha lẫn scale → đồng bộ
        float t = 0.5f + 0.5f * Mathf.Sin(Time.time * speed);

        // Alpha
        Color c = _sr.color;
        c.a     = Mathf.Lerp(alphaMin, alphaMax, t);
        _sr.color = c;

        // Scale breath
        if (scaleBreath)
        {
            float s = 1f + scaleAmount * t;
            transform.localScale = _baseScale * s;
        }
    }
}