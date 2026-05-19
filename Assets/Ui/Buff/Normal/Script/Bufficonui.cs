using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gắn lên prefab BuffIcon.
/// Stack text — dưới trái
/// CD text    — dưới phải
/// Value text — trên phải
/// </summary>
public class BuffIconUI : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private Image            iconImage;
    [SerializeField] private TextMeshProUGUI  stackText;  // dưới trái
    [SerializeField] private TextMeshProUGUI  cdText;     // dưới phải
    [SerializeField] private TextMeshProUGUI  valueText;  // trên phải

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        if (iconImage == null) iconImage = GetComponentInChildren<Image>();
        if (stackText == null || cdText == null || valueText == null)
        {
            var texts = GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length >= 1 && stackText == null) stackText = texts[0];
            if (texts.Length >= 2 && cdText    == null) cdText    = texts[1];
            if (texts.Length >= 3 && valueText == null) valueText = texts[2];
        }
    }

    // ── Runtime ───────────────────────────────────────────────────
    private EffectDisplayData curData;
    private float             timeLeft;
    private bool              hasDuration;

    // ── Setup ─────────────────────────────────────────────────────
    public void SetData(EffectDisplayData data)
    {
        curData     = data;
        timeLeft    = data.Duration;
        hasDuration = data.Duration > 0f;

        if (iconImage != null)
            iconImage.sprite = data.Icon;

        UpdateStackText(data.CurStacks, data.MaxStacks);
        UpdateValueText(data.Value);
        UpdateCdText(timeLeft);
    }

    public void Refresh(EffectDisplayData data)
    {
        curData     = data;
        timeLeft    = data.Duration;
        hasDuration = data.Duration > 0f;

        if (iconImage != null)
            iconImage.sprite = data.Icon;

        UpdateStackText(data.CurStacks, data.MaxStacks);
        UpdateValueText(data.Value);
        UpdateCdText(timeLeft);
    }

    private void Update()
    {
        if (curData == null || !hasDuration) return;

        timeLeft -= Time.deltaTime;
        timeLeft  = Mathf.Max(0f, timeLeft);
        UpdateCdText(timeLeft);
    }

    // ── Internal ──────────────────────────────────────────────────
    private void UpdateStackText(int cur, int max)
    {
        if (stackText == null) return;
        stackText.text = (max > 0 && cur > 1) ? cur.ToString() : "";
    }

    /// <summary>
    /// Hiện Value nếu >= 0. Ẩn nếu -1 (không dùng).
    /// Backward compatible — effect không set Value thì text tự ẩn.
    /// </summary>
    private void UpdateValueText(float value)
    {
        if (valueText == null) return;
        if (value < 0f)
        {
            valueText.text = "";
            return;
        }
        valueText.text = Mathf.RoundToInt(value).ToString();
    }

    private void UpdateCdText(float time)
    {
        if (cdText == null) return;
        if (!hasDuration) { cdText.text = ""; return; }

        cdText.text = time > 1f ? Mathf.CeilToInt(time).ToString()
                    : time > 0f ? "1"
                    : "";
    }
}