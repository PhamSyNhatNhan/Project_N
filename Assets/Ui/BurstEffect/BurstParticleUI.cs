using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gắn vào Canvas object của nhân vật.
/// Spawn UI Image ngẫu nhiên từ danh sách sprites, bay lên từ dưới và mờ dần.
/// Tất cả giá trị đều random trong khoảng min/max.
/// </summary>
public class BurstParticleUI : MonoBehaviour
{
    [Header("Sprites")]
    [SerializeField] private List<Sprite> sprites = new List<Sprite>();

    [Header("Spawn")]
    [SerializeField] private float spawnInterval    = 0.15f;
    [SerializeField] private int   maxParticles     = 20;

    [Header("Speed (px/s)")]
    [SerializeField] private float speedMin = 80f;
    [SerializeField] private float speedMax = 160f;

    [Header("Lifetime (s)")]
    [SerializeField] private float lifetimeMin = 0.8f;
    [SerializeField] private float lifetimeMax = 1.6f;

    [Header("Scale")]
    [SerializeField] private float scaleMin = 0.4f;
    [SerializeField] private float scaleMax = 1.2f;

    [Header("Spawn Area (normalized -0.5 to 0.5 of canvas width)")]
    [SerializeField] private float spawnXMin = -0.4f;
    [SerializeField] private float spawnXMax =  0.4f;
    [SerializeField] private float spawnYOffset = -0.5f; // từ dưới canvas

    // ── Runtime ───────────────────────────────────────────────────
    private RectTransform rectTransform;
    private Coroutine     spawnRoutine;
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
            int active = CountActive();
            if (active < maxParticles && sprites.Count > 0)
                SpawnOne();

            yield return new WaitForSecondsRealtime(spawnInterval);
        }
    }

    private void SpawnOne()
    {
        var go = GetFromPool();
        var rt = go.GetComponent<RectTransform>();
        var img = go.GetComponent<Image>();

        // Random sprite
        img.sprite = sprites[Random.Range(0, sprites.Count)];
        img.color  = Color.white;

        // Vị trí spawn — dưới canvas
        float canvasW = rectTransform.rect.width;
        float canvasH = rectTransform.rect.height;
        float x = Random.Range(spawnXMin, spawnXMax) * canvasW;
        float y = spawnYOffset * canvasH;
        rt.anchoredPosition = new Vector2(x, y);

        // Scale ngẫu nhiên
        float scale = Random.Range(scaleMin, scaleMax);
        rt.localScale = Vector3.one * scale;

        go.SetActive(true);
        StartCoroutine(MoveParticle(rt, img));
    }

    private IEnumerator MoveParticle(RectTransform rt, Image img)
    {
        float speed    = Random.Range(speedMin, speedMax);
        float lifetime = Random.Range(lifetimeMin, lifetimeMax);
        float elapsed  = 0f;

        while (elapsed < lifetime)
        {
            if (rt == null) yield break;

            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / lifetime;

            rt.anchoredPosition += Vector2.up * speed * Time.unscaledDeltaTime;
            img.color = new Color(1f, 1f, 1f, 1f - t);

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

        var go  = new GameObject("BurstParticle");
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