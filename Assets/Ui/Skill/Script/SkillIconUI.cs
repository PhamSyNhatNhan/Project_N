using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Hiển thị 1 skill icon:
/// - Charge  — dưới trái, chỉ hiện khi MaxCharge > 1
/// - CD      — dưới phải, chỉ hiện khi đang CD
/// - Counter — trên phải, chỉ hiện khi MaxCounter > 1
/// </summary>
public class SkillIconUI : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private Image           iconImage;
    [SerializeField] private TextMeshProUGUI chargeText;   // dưới trái
    [SerializeField] private TextMeshProUGUI cdText;       // dưới phải
    [SerializeField] private TextMeshProUGUI counterText;  // trên phải

    [Header("Fallback")]
    [SerializeField] private Sprite placeholderIcon;

    // ── Runtime ───────────────────────────────────────────────────
    private SkillCdReadyData curData;
    private float            cdTimeLeft;
    private bool             hasCd;

    // ── Setup ─────────────────────────────────────────────────────
    public void SetData(SkillCdReadyData data)
    {
        curData    = data;
        cdTimeLeft = data.CdLeft;
        hasCd      = !data.IsInfinite && data.BaseCd > 0f;

        if (iconImage != null)
            iconImage.sprite = data.Icon != null ? data.Icon : placeholderIcon;

        UpdateCharge(data);
        UpdateCounter(data);
        UpdateCd(cdTimeLeft);
    }

    public void Refresh(SkillCdReadyData data)
    {
        curData    = data;
        cdTimeLeft = data.CdLeft;
        hasCd      = !data.IsInfinite && data.BaseCd > 0f;

        UpdateCharge(data);
        UpdateCounter(data);
        UpdateCd(cdTimeLeft);
    }

    // ── Update — tự đếm CD ────────────────────────────────────────
    private void Update()
    {
        if (!hasCd || cdTimeLeft <= 0f) return;

        cdTimeLeft -= Time.deltaTime;
        cdTimeLeft  = Mathf.Max(0f, cdTimeLeft);
        UpdateCd(cdTimeLeft);
    }

    // ── Internal ──────────────────────────────────────────────────
    private void UpdateCharge(SkillCdReadyData data)
    {
        if (chargeText == null) return;
        chargeText.text = data.MaxCharge > 1 ? data.CurCharge.ToString() : "";
    }

    private void UpdateCounter(SkillCdReadyData data)
    {
        if (counterText == null) return;
        counterText.text = data.MaxCounter > 0 ? data.Counter.ToString() : "";
    }

    private void UpdateCd(float time)
    {
        if (cdText == null) return;
        if (!hasCd || time <= 0f) { cdText.text = ""; return; }

        cdText.text = time > 1f ? Mathf.CeilToInt(time).ToString()
                    : time > 0f ? "1"
                    : "";
    }

    // ── Properties ────────────────────────────────────────────────
    public string SkillId      => curData?.SkillId;
    public int    DisplayOrder => curData?.DisplayOrder ?? 0;
}