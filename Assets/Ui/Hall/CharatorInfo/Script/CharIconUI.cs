using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Prefab icon nhân vật trong CharListScrollView.
/// Hiện avatar + selected highlight + locked overlay.
/// Tap → callback OnSelected(index).
/// </summary>
[RequireComponent(typeof(Button))]
public class CharIconUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image      avatarImage;
    [SerializeField] private GameObject selectedHighlight;
    [SerializeField] private GameObject lockedOverlay;

    // ── Runtime ───────────────────────────────────────────────────
    private int             _index;
    private System.Action<int> _onSelected;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(OnTap);
    }

    // ── Setup ─────────────────────────────────────────────────────
    public void Setup(int index, Sprite avatar, bool unlocked, System.Action<int> onSelected)
    {
        _index      = index;
        _onSelected = onSelected;

        if (avatarImage != null)
        {
            avatarImage.sprite  = avatar;
            avatarImage.enabled = avatar != null;
        }

        if (lockedOverlay != null)
            lockedOverlay.SetActive(!unlocked);

        // Disable button nếu chưa unlock
        GetComponent<Button>().interactable = unlocked;

        SetSelected(false);
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
        _onSelected?.Invoke(_index);
    }
}