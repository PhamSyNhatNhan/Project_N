using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Hoa/cánh bay ngang từ phải sang trái, đường cong nhẹ xuống dưới.
/// Xoay chậm, mờ dần ở cuối — giống cảnh hoa đào trong anime.
/// </summary>
public class BurstPetalUI : MonoBehaviour
{
    [Header("Sprites")]
    [SerializeField] private List<Sprite> sprites = new List<Sprite>();

    [Header("Spawn")]
    [SerializeField] private float spawnInterval = 0.25f;
    [SerializeField] private int   maxParticles  = 20;

    [Header("Horizontal Speed (px/s)")]
    [SerializeField] private float speedMin = 80f;
    [SerializeField] private float speedMax = 180f;

    [Header("Lifetime (s)")]
    [SerializeField] private float lifetimeMin = 2.0f;
    [SerializeField] private float lifetimeMax = 4.0f;

    [Header("Scale")]
    [SerializeField] private float scaleMin = 0.3f;
    [SerializeField] private float scaleMax = 1.0f;

    [Header("Curve (độ cong xuống, px/s²)")]
    [SerializeField] private float curveMin = 10f;
    [SerializeField] private float curveMax = 40f;

    [Header("Vertical Drift (lắc nhẹ lên xuống)")]
    [SerializeField] private float driftAmplitudeMin = 5f;
    [SerializeField] private float driftAmplitudeMax = 20f;
    [SerializeField] private float driftFrequencyMin = 0.5f;
    [SerializeField] private float driftFrequencyMax = 1.5f;

    [Header("Rotation (độ/s)")]
    [SerializeField] private float rotSpeedMin = 10f;
    [SerializeField] private float rotSpeedMax = 50f;

    [Header("Spawn Area")]
    [SerializeField] private float spawnYMax    =  0.5f;  // top edge normalized
    [SerializeField] private float spawnXOffset =  0.5f;  // right edge normalized

    [Header("Fade")]
    [SerializeField] private float fadeStartPercent = 0.75f;

    // ── Runtime ───────────────────────────────────────────────────
    private RectTransform    rectTransform;
    private Coroutine        spawnRoutine;
    private List<GameObject> pool = new List<GameObject>();

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        spawnRoutine = StartCoroutine(SpawnLoop());
    }

    private void OnDisable()
    {
        if (spawnRoutine != null) StopCoroutine(spawnRoutine);
        ReturnAll();
    }

    // ── Spawn Loop ────────────────────────────────────────────────
    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            if (CountActive() < maxParticles && sprites.Count > 0)
                SpawnOne();

            yield return new WaitForSecondsRealtime(spawnInterval);
        }
    }

    private void SpawnOne()
    {
        var go  = GetFromPool();
        var rt  = go.GetComponent<RectTransform>();
        var img = go.GetComponent<Image>();

        img.sprite = sprites[Random.Range(0, sprites.Count)];
        img.color  = Color.white;

        float canvasW = rectTransform.rect.width;
        float canvasH = rectTransform.rect.height;

        // Spawn từ góc trên phải, rải ngẫu nhiên dọc theo cạnh trên và cạnh phải
        float x, y;
        if (Random.value > 0.5f)
        {
            // Cạnh trên
            x = Random.Range(0f, spawnXOffset) * canvasW;
            y = spawnYMax * canvasH;
        }
        else
        {
            // Cạnh phải
            x = spawnXOffset * canvasW;
            y = Random.Range(0f, spawnYMax) * canvasH;
        }
        rt.anchoredPosition = new Vector2(x, y);

        float scale = Random.Range(scaleMin, scaleMax);
        rt.localScale    = Vector3.one * scale;
        // Hướng ban đầu theo chiều bay chéo xuống trái (~225°)
        rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(200f, 250f));

        go.SetActive(true);
        StartCoroutine(PetalRoutine(rt, img));
    }

    private IEnumerator PetalRoutine(RectTransform rt, Image img)
    {
        float speed         = Random.Range(speedMin, speedMax);
        float lifetime      = Random.Range(lifetimeMin, lifetimeMax);
        float curve         = Random.Range(curveMin, curveMax);
        float driftAmp      = Random.Range(driftAmplitudeMin, driftAmplitudeMax);
        float driftFreq     = Random.Range(driftFrequencyMin, driftFrequencyMax);
        float rotSpeed      = Random.Range(rotSpeedMin, rotSpeedMax) * (Random.value > 0.5f ? 1f : -1f);
        float driftOffset   = Random.Range(0f, Mathf.PI * 2f);
        float elapsed       = 0f;
        Vector2 startPos    = rt.anchoredPosition;

        while (elapsed < lifetime)
        {
            if (rt == null) yield break;

            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / lifetime;

            // Bay chéo xuống trái
            float moveX = -speed * elapsed;

            // Rơi xuống tự nhiên
            float moveY = -curve * elapsed * elapsed;

            // Drift lên xuống nhẹ
            float drift = Mathf.Sin(elapsed * driftFreq * Mathf.PI * 2f + driftOffset) * driftAmp;

            rt.anchoredPosition = startPos + new Vector2(moveX, moveY + drift);

            // Xoay chậm
            rt.localRotation = Quaternion.Euler(0f, 0f,
                rt.localEulerAngles.z + rotSpeed * Time.unscaledDeltaTime);

            // Fade cuối
            float alpha = t >= fadeStartPercent
                ? 1f - (t - fadeStartPercent) / (1f - fadeStartPercent)
                : 1f;
            img.color = new Color(1f, 1f, 1f, alpha);

            yield return null;
        }

        if (rt != null)
            rt.gameObject.SetActive(false);
    }

    // ── Pool ──────────────────────────────────────────────────────
    private GameObject GetFromPool()
    {
        foreach (var obj in pool)
            if (obj != null && !obj.activeSelf)
                return obj;

        var go  = new GameObject("BurstPetal");
        go.transform.SetParent(transform, false);
        go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        go.SetActive(false);
        pool.Add(go);
        return go;
    }

    private int CountActive()
    {
        int count = 0;
        foreach (var obj in pool)
            if (obj != null && obj.activeSelf) count++;
        return count;
    }

    private void ReturnAll()
    {
        foreach (var obj in pool)
            if (obj != null) obj.SetActive(false);
    }
}