using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lắng nghe OnBurstActive — fade in/out Canvas theo name.
/// Khi chuyển scene → fade out tất cả canvas đang active.
/// Yêu cầu CanvasGroup trên mỗi Canvas object.
/// </summary>
public class BurstCanvasManager : MonoBehaviour
{
    // ── Entry ─────────────────────────────────────────────────────
    [System.Serializable]
    public class CanvasEntry
    {
        public string      name;
        public GameObject  canvas;
    }

    [SerializeField] private List<CanvasEntry> entries  = new List<CanvasEntry>();
    [SerializeField] private float             fadeTime = 0.3f;

    private Dictionary<string, GameObject>  canvasMap     = new Dictionary<string, GameObject>();
    private Dictionary<string, Coroutine>   fadeRoutines  = new Dictionary<string, Coroutine>();

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        foreach (var entry in entries)
            if (!string.IsNullOrEmpty(entry.name) && entry.canvas != null)
                canvasMap[entry.name] = entry.canvas;
    }

    private void OnEnable()
    {
        EventManager.Ui.OnBurstActive.Get().AddListener(OnBurstActive);
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void OnDisable()
    {
        EventManager.Ui.OnBurstActive.Get().RemoveListener(OnBurstActive);
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    // ── Handlers ──────────────────────────────────────────────────
    private void OnBurstActive(Component sender, object data)
    {
        if (data is not BurstActiveData burstData) return;
        if (!canvasMap.TryGetValue(burstData.Name, out var canvas)) return;
        if (canvas == null) return;

        SetFade(burstData.Name, canvas, burstData.IsActive);
    }

    private void OnSceneUnloaded(Scene scene)
    {
        foreach (var kvp in canvasMap)
            if (kvp.Value != null)
                SetFade(kvp.Key, kvp.Value, false);
    }

    // ── Fade ──────────────────────────────────────────────────────
    private void SetFade(string name, GameObject canvas, bool show)
    {
        var cg = canvas.GetComponent<CanvasGroup>();
        if (cg == null) cg = canvas.AddComponent<CanvasGroup>();

        if (fadeRoutines.TryGetValue(name, out var existing) && existing != null)
            StopCoroutine(existing);

        fadeRoutines[name] = StartCoroutine(FadeRoutine(cg, canvas, show));
    }

    private IEnumerator FadeRoutine(CanvasGroup cg, GameObject canvas, bool show)
    {
        if (show)
        {
            cg.alpha = 0f;
            canvas.SetActive(true);
        }

        float start  = cg.alpha;
        float target = show ? 1f : 0f;
        float elapsed = 0f;

        while (elapsed < fadeTime)
        {
            elapsed += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(start, target, elapsed / fadeTime);
            yield return null;
        }

        cg.alpha = target;
        if (!show) canvas.SetActive(false);
    }

    // ── Public API ────────────────────────────────────────────────
    public void Register(string name, GameObject canvas)
    {
        if (string.IsNullOrEmpty(name) || canvas == null) return;
        canvasMap[name] = canvas;
    }

    public void Unregister(string name) => canvasMap.Remove(name);
}