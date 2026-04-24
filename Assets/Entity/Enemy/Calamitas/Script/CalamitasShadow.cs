using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gắn lên GameObject Calamitas.
/// Gọi PlayDepartShadow(pos) tại điểm xuất phát — bóng tản ra trái/phải rồi fade out.
/// Gọi PlayArriveShadow(pos) tại điểm đến — bóng từ trái/phải gộp lại rồi fade out.
/// </summary>
public class CalamitasShadow : MonoBehaviour
{
    [Header("Shadow")]
    [SerializeField] private int   shadowCount    = 4;     // số bóng mỗi bên
    [SerializeField] private float spreadDistance = 1.5f;  // khoảng cách tản ra tối đa
    [SerializeField] private float fadeDuration   = 0.4f;  // thời gian fade
    [SerializeField] private Color shadowColor    = new Color(0.5f, 0f, 0.8f, 0.6f);

    private SpriteRenderer srcRenderer;

    private class ShadowInstance
    {
        public GameObject   go;
        public SpriteRenderer sr;
        public Vector2      startPos;
        public Vector2      endPos;
        public float        timer;
        public float        duration;
        public float        startAlpha;
        public bool         active;
    }

    private readonly List<ShadowInstance> pool = new List<ShadowInstance>();

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        srcRenderer = GetComponent<SpriteRenderer>();
        if (srcRenderer == null)
            srcRenderer = GetComponentInChildren<SpriteRenderer>();

        PrewarmPool(shadowCount * 2 * 2);
    }

    private void Update()
    {
        foreach (var s in pool)
        {
            if (!s.active) continue;

            s.timer += Time.deltaTime;
            float t = Mathf.Clamp01(s.timer / s.duration);

            s.go.transform.position = Vector2.Lerp(s.startPos, s.endPos, t);

            // Fade out
            Color c = s.sr.color;
            c.a     = Mathf.Lerp(s.startAlpha, 0f, t);
            s.sr.color = c;

            if (t >= 1f)
            {
                s.active = false;
                s.go.SetActive(false);
            }
        }
    }

    public float FadeDuration => fadeDuration;

    // ── Public API ────────────────────────────────────────────────

    /// <summary>Bóng tản ra trái/phải tại điểm xuất phát</summary>
    public void PlayDepartShadow(Vector2 pos)
    {
        if (srcRenderer == null)
        {
            Debug.LogWarning("[CalamitasShadow] srcRenderer null — không tìm thấy SpriteRenderer!");
            return;
        }
        SpawnShadows(pos, spread: true);
    }

    /// <summary>Bóng gộp lại từ trái/phải tại điểm đến</summary>
    public void PlayArriveShadow(Vector2 pos)
    {
        if (srcRenderer == null) return;
        SpawnShadows(pos, spread: false);
    }

    // ── Internal ──────────────────────────────────────────────────
    private void SpawnShadows(Vector2 center, bool spread)
    {
        Sprite sprite = srcRenderer.sprite;
        if (sprite == null) return;

        for (int i = 0; i < shadowCount; i++)
        {
            float t      = (float)(i + 1) / shadowCount;
            float offset = spreadDistance * t;

            // Bên trái
            SpawnOne(center, spread, -offset, t, sprite);
            // Bên phải
            SpawnOne(center, spread,  offset, t, sprite);
        }
    }

    private void SpawnOne(Vector2 center, bool spread, float xOffset, float t,
                          Sprite sprite)
    {
        var s = GetFree();

        s.sr.sprite = sprite;
        s.sr.flipX  = srcRenderer.flipX;

        Color c = shadowColor;
        c.a        = shadowColor.a * (1f - t * 0.5f); // bóng xa mờ hơn
        s.sr.color = c;
        s.startAlpha = c.a;

        Vector2 offsetPos = center + new Vector2(xOffset, 0f);

        if (spread)
        {
            // Xuất phát từ center → tản ra
            s.startPos = center;
            s.endPos   = offsetPos;
        }
        else
        {
            // Xuất phát từ ngoài → gộp vào center
            s.startPos = offsetPos;
            s.endPos   = center;
        }

        s.go.transform.position = s.startPos;
        s.timer    = 0f;
        s.duration = fadeDuration;
        s.active   = true;
        s.go.SetActive(true);
    }

    // ── Pool ──────────────────────────────────────────────────────
    private void PrewarmPool(int count)
    {
        for (int i = 0; i < count; i++)
            pool.Add(CreateInstance(i));
    }

    private ShadowInstance CreateInstance(int index)
    {
        var go = new GameObject($"CalamitasShadow_{index}");
        go.transform.SetParent(null);

        var sr              = go.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = srcRenderer != null ? srcRenderer.sortingLayerName : "Default";
        sr.sortingOrder     = srcRenderer != null ? srcRenderer.sortingOrder - 1 : 0;
        sr.material         = srcRenderer != null
            ? new Material(srcRenderer.material)
            : new Material(Shader.Find("Sprites/Default"));

        go.SetActive(false);

        return new ShadowInstance { go = go, sr = sr, active = false };
    }

    private ShadowInstance GetFree()
    {
        foreach (var s in pool)
            if (!s.active) return s;

        // Expand pool nếu cần
        var newS = CreateInstance(pool.Count);
        pool.Add(newS);
        return newS;
    }

    private void OnDestroy()
    {
        foreach (var s in pool)
            if (s.go != null) Destroy(s.go);
        pool.Clear();
    }
}