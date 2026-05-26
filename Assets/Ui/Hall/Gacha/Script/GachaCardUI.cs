using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Prefab card dùng chung cho x1 và x10.
/// Back: mặt úp (pattern).
/// Front: icon + tên + type, border vàng nếu Talent.
/// Flip: rotate Y 180° với animation.
/// Tap → flip nếu chưa lật.
/// </summary>
public class GachaCardUI : MonoBehaviour
{
    [Header("Flip")]
    [SerializeField] private RectTransform cardRoot;       // object cần rotate
    [SerializeField] private Image         backImage;   // background card
    [SerializeField] private Image         frontImage;  // ảnh nhân vật / icon shard

    [Header("Front")]

    [Header("Sprites")]
    [SerializeField] private Sprite        backCard;      // mặt úp — chưa lật
    [SerializeField] private Sprite        backNormal;    // mặt lật — shard
    [SerializeField] private Sprite        backTalent;    // mặt lật — talent

    [Header("Settings")]
    [SerializeField] private float flipDuration = 0.45f;

    [Header("Colors")]
    [SerializeField] private Color talentBorderColor = new Color(0.96f, 0.77f, 0.19f);

    // ── Runtime ───────────────────────────────────────────────────
    private GachaPullResult  _result;
    private bool             _isFlipped  = false;
    private bool             _isFlipping = false;
    private Action           _onFlipped;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        GetComponent<Button>()?.onClick.AddListener(OnTap);
        ShowBack();
    }

    // ── Setup ─────────────────────────────────────────────────────
    public void Setup(GachaPullResult result, Sprite icon, Action onFlipped = null)
    {
        _result    = result;
        _onFlipped = onFlipped;

        if (frontImage != null)
        {
            if (icon != null) frontImage.sprite = icon;
            frontImage.enabled = false; // ẩn cho đến khi lật
        }
        _isFlipped  = false;
        _isFlipping = false;

        // Setup front content

        ShowBack();
    }

    // ── Tap ───────────────────────────────────────────────────────
    public void OnTap()
    {
        if (_isFlipped || _isFlipping) return;
        StartCoroutine(FlipCoroutine());
    }

    // ── Flip API ──────────────────────────────────────────────────
    public void FlipImmediate()
    {
        if (_isFlipped) return;
        StopAllCoroutines();
        _isFlipped  = true;
        _isFlipping = false;
        if (cardRoot != null)
            cardRoot.localEulerAngles = new Vector3(0f, 180f, 0f);
        ShowFront();
        _onFlipped?.Invoke();
    }

    public void Flip() => StartCoroutine(FlipCoroutine());

    public bool IsFlipped => _isFlipped;

    // ── Coroutine ─────────────────────────────────────────────────
    private IEnumerator FlipCoroutine()
    {
        if (_isFlipped || _isFlipping) yield break;
        _isFlipping = true;

        float half = flipDuration / 2f;

        // Phase 1: rotate 0 → 90
        float t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float angle = Mathf.Lerp(0f, 90f, t / half);
            if (cardRoot != null)
                cardRoot.localEulerAngles = new Vector3(0f, angle, 0f);
            yield return null;
        }

        // Swap face at 90°
        ShowFront();

        // Phase 2: rotate 90 → 0 (front visible)
        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float angle = Mathf.Lerp(90f, 0f, t / half);
            if (cardRoot != null)
                cardRoot.localEulerAngles = new Vector3(0f, angle, 0f);
            yield return null;
        }

        if (cardRoot != null)
            cardRoot.localEulerAngles = Vector3.zero;

        _isFlipped  = true;
        _isFlipping = false;
        _onFlipped?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void ShowBack()
    {
        if (backImage  != null) backImage.sprite   = backCard;
        if (frontImage != null) frontImage.enabled = false;
        if (cardRoot   != null) cardRoot.localEulerAngles = Vector3.zero;
    }

    private void ShowFront()
    {
        if (backImage != null && _result != null)
            backImage.sprite = _result.type == GachaResultType.Talent ? backTalent : backNormal;
        if (frontImage != null)
            frontImage.enabled = true;
    }
}