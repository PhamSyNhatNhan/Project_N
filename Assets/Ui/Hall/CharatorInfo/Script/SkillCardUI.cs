using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// Prefab 1 skill card trong grid Skills.
/// Hiện icon + skillId + CD label.
/// Tap → callback OnSelected(skillData).
/// </summary>
public class SkillCardUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image           iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI cdText;
    [SerializeField] private GameObject      selectedHighlight;
    [SerializeField] private Button          button;

    // ── Runtime ───────────────────────────────────────────────────
    private SkillData             _data;
    private Action<SkillCardUI, SkillData> _onSelected;

    // ── Setup ─────────────────────────────────────────────────────
    public void Setup(SkillData data, Action<SkillCardUI, SkillData> onSelected)
    {
        _data       = data;
        // Wrap để truyền cả card reference lên parent
        _onSelected = onSelected;

        // Icon
        if (iconImage != null)
        {
            if (!string.IsNullOrEmpty(data.iconPath))
            {
                var sprite = Resources.Load<Sprite>(data.iconPath);
                iconImage.sprite  = sprite;
                iconImage.enabled = sprite != null;
            }
            else
            {
                iconImage.enabled = false;
            }
        }

        // Name
        if (nameText != null)
            nameText.text = data.skillId;

        // CD label
        if (cdText != null)
            cdText.text = BuildCdLabel(data);

        // Type badge — skillType là string

        SetSelected(false);

        button?.onClick.RemoveAllListeners();
        button?.onClick.AddListener(OnTap);
    }

    // ── CD label ──────────────────────────────────────────────────
    private string BuildCdLabel(SkillData data)
    {
        string cdType = data.Get<string>("cdType", "cooldown");

        if (cdType == "counter")
        {
            int max = data.Get<int>("maxCounter", 0);
            return max > 0 ? $"×{max}" : "Counter";
        }

        if (cdType == "infinite" || data.cooldown <= 0f)
            return "Passive";

        int maxCharge = data.Get<int>("maxCharge", 1);
        return maxCharge > 1
            ? $"{data.cooldown}s ×{maxCharge}"
            : $"{data.cooldown}s";
    }


    // ── Select state ──────────────────────────────────────────────
    public void SetSelected(bool selected)
    {
        if (selectedHighlight != null)
            selectedHighlight.SetActive(selected);
    }

    // ── Tap ───────────────────────────────────────────────────────
    private void OnTap()
    {
        _onSelected?.Invoke(this, _data);
    }
}