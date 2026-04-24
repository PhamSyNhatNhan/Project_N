using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI chọn map trong Hall Scene.
/// Hiện sau khi CharacterSelectUI confirm.
///
/// Setup Inspector:
///   - previewImage  : Image component hiển thị preview map
///   - nameText      : TextMeshProUGUI tên map
///   - descText      : TextMeshProUGUI mô tả map
///   - prevButton    : Button ◄
///   - nextButton    : Button ►
///   - confirmButton : Button Bắt đầu
/// </summary>
public class MapSelectUI : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────
    [Header("UI References")]
    [SerializeField] private Image           previewImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descText;
    [SerializeField] private Button          prevButton;
    [SerializeField] private Button          nextButton;
    [SerializeField] private Button          confirmButton;

    // ── Runtime ───────────────────────────────────────────────────
    private List<MapDisplayData> _maps         = new List<MapDisplayData>();
    private int                  _currentIndex = 0;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        EventManager.Gm.OnOpenMapSelect.Get().AddListener(HandleOpenMapSelect);
    }

    private void OnDestroy()
    {
        EventManager.Gm.OnOpenMapSelect.Get().RemoveListener(HandleOpenMapSelect);
    }

    private void Start()
    {
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        prevButton?.onClick.AddListener(OnPrev);
        nextButton?.onClick.AddListener(OnNext);
        confirmButton?.onClick.AddListener(OnConfirm);
        LoadMaps();
    }

    private void OnDisable()
    {
        prevButton?.onClick.RemoveListener(OnPrev);
        nextButton?.onClick.RemoveListener(OnNext);
        confirmButton?.onClick.RemoveListener(OnConfirm);
    }

    // ── Load danh sách map từ MapRegistry ─────────────────────────
    private void LoadMaps()
    {
        _maps.Clear();

        var registry = FindObjectOfType<MapRegistry>();
        if (registry == null)
        {
            Debug.LogError("[MapSelectUI] Không tìm thấy MapRegistry.");
            return;
        }

        foreach (var data in registry.GetAllDisplayData())
            _maps.Add(data);

        if (_maps.Count == 0)
        {
            Debug.LogError("[MapSelectUI] Không có map nào trong MapRegistry.");
            return;
        }

        _currentIndex = 0;
        RefreshUI();
    }

    // ── Navigation ────────────────────────────────────────────────
    private void OnPrev()
    {
        _currentIndex = (_currentIndex - 1 + _maps.Count) % _maps.Count;
        RefreshUI();
    }

    private void OnNext()
    {
        _currentIndex = (_currentIndex + 1) % _maps.Count;
        RefreshUI();
    }

    // ── Confirm ───────────────────────────────────────────────────
    private void OnConfirm()
    {
        if (_maps.Count == 0) return;

        if (DungeonFlowManager.Instance == null)
        {
            Debug.LogError("[MapSelectUI] DungeonFlowManager chưa tồn tại.");
            return;
        }

        if (DungeonFlowManager.Instance.PendingTalent == null)
        {
            Debug.LogError("[MapSelectUI] PendingTalent null — chưa chọn nhân vật.");
            return;
        }

        string groupId = _maps[_currentIndex].dungeonGroupId;
        Talent talent  = DungeonFlowManager.Instance.PendingTalent.Value;

        Hide();
        DungeonFlowManager.Instance.StartNewRun(talent, groupId);
    }

    // ── Refresh UI ────────────────────────────────────────────────
    private void RefreshUI()
    {
        if (_maps.Count == 0) return;

        MapDisplayData data = _maps[_currentIndex];

        if (previewImage != null)
        {
            previewImage.sprite  = data.preview;
            previewImage.enabled = data.preview != null;
        }

        if (nameText != null) nameText.text = data.displayName;
        if (descText != null) descText.text = data.description;

        if (prevButton != null) prevButton.gameObject.SetActive(_maps.Count > 1);
        if (nextButton != null) nextButton.gameObject.SetActive(_maps.Count > 1);
    }

    // ── Event Handler ─────────────────────────────────────────────
    private void HandleOpenMapSelect(Component sender, object data)
    {
        Show();
    }

    // ── Public API ────────────────────────────────────────────────
    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);
}