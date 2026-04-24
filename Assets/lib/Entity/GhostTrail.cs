using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ghost trail dùng SpriteRenderer snapshot — chỉ active khi được gọi
/// </summary>
public class GhostTrail : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private float    ghostInterval  = 0.05f;  // giây giữa mỗi ghost
    [SerializeField] private float    ghostDuration  = 0.3f;   // ghost tồn tại bao lâu
    [SerializeField] private Color    ghostColor     = new Color(1f, 1f, 1f, 0.5f);
    [SerializeField] private bool     useCustomColor = false;

    // ── Runtime ───────────────────────────────────────────────────
    private SpriteRenderer  srcRenderer;
    private List<GameObject> pool = new List<GameObject>();
    private Coroutine        trailCoroutine;
    private bool             isActive;

    private void Awake()
    {
        srcRenderer = GetComponent<SpriteRenderer>();
    }
    
    private void OnDisable()
    {
        StopTrail();
        foreach (var obj in pool)
            if (obj != null) obj.SetActive(false);
    }

    // ── Public API ────────────────────────────────────────────────
    public void StartTrail()
    {
        if (isActive) return;
        isActive       = true;
        trailCoroutine = StartCoroutine(TrailCoroutine());
    }

    public void StopTrail()
    {
        isActive = false;
        if (trailCoroutine != null)
        {
            StopCoroutine(trailCoroutine);
            trailCoroutine = null;
        }
    }

    // ── Coroutine ─────────────────────────────────────────────────
    private IEnumerator TrailCoroutine()
    {
        while (isActive)
        {
            SpawnGhost();
            yield return new WaitForSeconds(ghostInterval);
        }
    }

    private void SpawnGhost()
    {
        if (srcRenderer == null) return;

        GameObject ghost = GetFromPool();
        ghost.transform.position   = transform.position;
        ghost.transform.rotation   = transform.rotation;
        ghost.transform.localScale = transform.lossyScale;
        ghost.SetActive(true);

        var sr = ghost.GetComponent<SpriteRenderer>();
        sr.sprite = srcRenderer.sprite;
        sr.color  = useCustomColor ? ghostColor : new Color(
            srcRenderer.color.r,
            srcRenderer.color.g,
            srcRenderer.color.b,
            ghostColor.a
        );

        StartCoroutine(FadeGhost(ghost, sr));
    }

    private IEnumerator FadeGhost(GameObject ghost, SpriteRenderer sr)
    {
        float elapsed  = 0f;
        Color startColor = sr.color;

        while (elapsed < ghostDuration)
        {
            elapsed    += Time.deltaTime;
            float alpha = Mathf.Lerp(startColor.a, 0f, elapsed / ghostDuration);
            sr.color    = new Color(startColor.r, startColor.g, startColor.b, alpha);
            yield return null;
        }

        ghost.SetActive(false);
    }

    // ── Pool ──────────────────────────────────────────────────────
    private GameObject GetFromPool()
    {
        foreach (var obj in pool)
        {
            if (obj != null && !obj.activeSelf)
                return obj;
        }

        var newObj = new GameObject("Ghost");
        newObj.AddComponent<SpriteRenderer>();
        newObj.SetActive(false);
        pool.Add(newObj);
        return newObj;
    }
}