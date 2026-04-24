using UnityEngine;

/// <summary>
/// 3 bóng xoáy theo đường spiral hội tụ về blinkTarget, rõ dần theo thời gian
/// </summary>
public class BrimstoneBlinkShadow : MonoBehaviour
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Config")]
    [SerializeField] private int   shadowCount   = 3;
    [SerializeField] private float spawnRadius   = 2.5f;  // bán kính xuất phát
    [SerializeField] private float duration      = 0.5f;  // thời gian hội tụ
    [SerializeField] private float spiralTurns   = 1.5f;  // số vòng xoáy
    [SerializeField] private float endAlpha      = 0.85f;

    // ── Runtime ───────────────────────────────────────────────────
    private struct ShadowData
    {
        public GameObject     obj;
        public SpriteRenderer sr;
        public float          startAngle; // góc xuất phát (radian)
        public Vector2        targetPos;
    }

    private ShadowData[] shadows;
    private float        timer;
    private bool         isRunning;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        shadows = new ShadowData[shadowCount];
        for (int i = 0; i < shadowCount; i++)
        {
            var obj              = new GameObject($"BlinkShadow_{i}");
            obj.transform.parent = null;
            var sr               = obj.AddComponent<SpriteRenderer>();
            obj.SetActive(false);
            shadows[i] = new ShadowData { obj = obj, sr = sr };
        }
    }

    private void Update()
    {
        if (!isRunning) return;

        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / duration);

        for (int i = 0; i < shadows.Length; i++)
        {
            ref ShadowData s = ref shadows[i];
            if (!s.obj.activeSelf) continue;

            // Spiral: góc tăng dần, bán kính thu nhỏ
            float angle  = s.startAngle + spiralTurns * 2f * Mathf.PI * t;
            float radius = Mathf.Lerp(spawnRadius, 0f, t);

            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            s.obj.transform.position = s.targetPos + offset;

            // Rõ dần
            float alpha = Mathf.Lerp(0f, endAlpha, t);
            s.sr.color  = new Color(s.sr.color.r, s.sr.color.g, s.sr.color.b, alpha);
        }

        if (t >= 1f) Stop();
    }

    private void OnDestroy()
    {
        foreach (var s in shadows)
            if (s.obj != null) Destroy(s.obj);
    }

    // ── Public API ────────────────────────────────────────────────
    public void Play(Vector2 targetPos, SpriteRenderer srcRenderer)
    {
        Stop();
        timer     = 0f;
        isRunning = true;

        for (int i = 0; i < shadows.Length; i++)
        {
            ref ShadowData s = ref shadows[i];

            // Chia đều góc xuất phát + random nhỏ
            s.startAngle = (2f * Mathf.PI / shadowCount * i)
                         + Random.Range(-0.2f, 0.2f);
            s.targetPos  = targetPos;

            // Vị trí ban đầu trên vòng tròn
            Vector2 startPos = targetPos + new Vector2(
                Mathf.Cos(s.startAngle), Mathf.Sin(s.startAngle)) * spawnRadius;

            s.obj.transform.position   = startPos;
            s.obj.transform.rotation   = srcRenderer.transform.rotation;
            s.obj.transform.localScale = srcRenderer.transform.lossyScale;

            s.sr.sprite         = srcRenderer.sprite;
            s.sr.flipX          = srcRenderer.flipX;
            s.sr.flipY          = srcRenderer.flipY;
            s.sr.sortingLayerID = srcRenderer.sortingLayerID;
            s.sr.sortingOrder   = srcRenderer.sortingOrder - 1;
            s.sr.color          = new Color(srcRenderer.color.r,
                                            srcRenderer.color.g,
                                            srcRenderer.color.b, 0f);
            s.obj.SetActive(true);
        }
    }

    private void Stop()
    {
        isRunning = false;
        for (int i = 0; i < shadows.Length; i++)
            if (shadows[i].obj != null) shadows[i].obj.SetActive(false);
    }
}