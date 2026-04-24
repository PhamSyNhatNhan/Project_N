using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Script cho 1 BuffCard trong RogueBuffSelectUI.
/// Tap 1: highlight/select. Tap 2: confirm.
/// </summary>
public class BuffCardUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image           iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descText;
    [SerializeField] private Button          cardButton;
    [SerializeField] private GameObject      highlightObject; // highlight khi selected

    // ── Runtime ───────────────────────────────────────────────────
    private BuffGroupId  _groupId;
    private int          _minorIndex;
    private bool         _selected;
    private System.Action<BuffGroupId, int>            _onConfirmMinor;
    private System.Action<BuffGroupId, RogueBuffGroup> _onConfirmGroup;
    private RogueBuffGroup                             _groupPrefab;
    private RogueBuffSelectUI                          _parent;

    // ── Setup Minor (bước 2) ──────────────────────────────────────
    public void Setup(MinorBuff buff, BuffGroupId groupId, int minorIndex, System.Action<BuffGroupId, int> onConfirm, RogueBuffSelectUI parent = null)
    {
        _groupId        = groupId;
        _minorIndex     = minorIndex;
        _onConfirmMinor = onConfirm;
        _onConfirmGroup = null;
        _selected       = false;
        _parent         = parent;

        if (iconImage != null)
        {
            iconImage.sprite  = buff.Icon;
            iconImage.enabled = buff.Icon != null;
        }

        if (nameText != null) nameText.text = buff.DisplayName;
        if (descText != null) descText.text = buff.Description;

        SetHighlight(false);
        cardButton?.onClick.RemoveAllListeners();
        cardButton?.onClick.AddListener(OnTap);
    }

    // ── Setup Group (bước 1) ──────────────────────────────────────
    public void SetupGroup(RogueBuffGroup prefab, BuffGroupId groupId, System.Action<BuffGroupId, RogueBuffGroup> onConfirm, RogueBuffSelectUI parent = null)
    {
        _groupId        = groupId;
        _groupPrefab    = prefab;
        _onConfirmGroup = onConfirm;
        _onConfirmMinor = null;
        _selected       = false;
        _parent         = parent;

        if (iconImage != null)
        {
            iconImage.sprite  = prefab.GroupIcon;
            iconImage.enabled = prefab.GroupIcon != null;
        }

        if (nameText != null) nameText.text = prefab.GroupDisplayName;
        if (descText != null) descText.text = prefab.GroupDescription;

        SetHighlight(false);
        cardButton?.onClick.RemoveAllListeners();
        cardButton?.onClick.AddListener(OnTap);
    }

    // ── Tap Logic ─────────────────────────────────────────────────
    private void OnTap()
    {
        if (!_selected)
        {
            // Tap 1 — deselect các card khác rồi highlight card này
            _parent?.DeselectOthers(this);
            _selected = true;
            SetHighlight(true);
        }
        else
        {
            // Tap 2 — confirm
            if (_onConfirmMinor != null)
                _onConfirmMinor.Invoke(_groupId, _minorIndex);
            else
                _onConfirmGroup?.Invoke(_groupId, _groupPrefab);
        }
    }

    // ── Public — deselect từ bên ngoài (khi card khác được chọn) ──
    public void Deselect()
    {
        _selected = false;
        SetHighlight(false);
    }

    // ── Highlight ─────────────────────────────────────────────────
    private void SetHighlight(bool active)
    {
        if (highlightObject != null)
            highlightObject.SetActive(active);
    }
}