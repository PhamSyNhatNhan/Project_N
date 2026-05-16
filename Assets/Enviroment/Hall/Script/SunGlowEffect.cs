using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class SunGlowEffect : MonoBehaviour
{
    [Header("Alpha")]
    [Range(0f, 1f)] public float baseAlpha = 0.5f;
    [Range(0f, 1f)] public float breathStrength = 0.12f;
    public float breathSpeed = 0.8f;

    [Header("Scale")]
    [Range(0f, 0.1f)] public float scalePulse = 0.03f;

    SpriteRenderer sr;
    Vector3 originalScale;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        originalScale = transform.localScale;
    }

    void Update()
    {
        float breath = Mathf.Sin(Time.time * breathSpeed) * 0.7f
                       + Mathf.Sin(Time.time * breathSpeed * 1.7f) * 0.3f;

        Color c = sr.color;
        c.a = Mathf.Clamp01(baseAlpha + breath * breathStrength);
        sr.color = c;

        float s = 1f + breath * scalePulse;
        transform.localScale = originalScale * s;
    }
}