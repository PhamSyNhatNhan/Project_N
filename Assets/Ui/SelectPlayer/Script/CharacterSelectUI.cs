using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI chọn nhân vật trong Hall Scene.
/// Gắn lên Panel root của CharacterSelect UI.
/// 
/// Setup Inspector:
///   - portraitImage   : Image component hiển thị portrait
///   - nameText        : TextMeshProUGUI tên nhân vật
///   - prevButton      : Button ◄
///   - nextButton      : Button ►
///   - confirmButton   : Button Bắt đầu
/// </summary>
public class CharacterSelectUI : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────
    [Header("UI References")]
    [SerializeField] private Image              portraitImage;
    [SerializeField] private TextMeshProUGUI    nameText;
    [SerializeField] private Button             prevButton;
    [SerializeField] private Button             nextButton;
    [SerializeField] private Button             confirmButton;

    // ── Runtime ───────────────────────────────────────────────────
    private List<PlayerDisplayData> _characters = new List<PlayerDisplayData>();
    private int                     _currentIndex = 0;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        EventManager.Gm.OnOpenCharacterSelect.Get().AddListener(HandleOpenCharacterSelect);
    }

    private void OnDestroy()
    {
        EventManager.Gm.OnOpenCharacterSelect.Get().RemoveListener(HandleOpenCharacterSelect);
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
        LoadCharacters();
    }

    private void OnDisable()
    {
        prevButton?.onClick.RemoveListener(OnPrev);
        nextButton?.onClick.RemoveListener(OnNext);
        confirmButton?.onClick.RemoveListener(OnConfirm);
    }

    private void HandleOpenCharacterSelect(Component sender, object data)
    {
        Show();
    }

    // ── Load danh sách nhân vật từ PlayerRegistry ─────────────────
    private void LoadCharacters()
    {
        _characters.Clear();

        var registry = FindObjectOfType<PlayerRegistry>();
        if (registry == null)
        {
            Debug.LogError("[CharacterSelectUI] Không tìm thấy PlayerRegistry.");
            return;
        }

        foreach (var data in registry.GetAllDisplayData())
            _characters.Add(data);

        if (_characters.Count == 0)
        {
            Debug.LogError("[CharacterSelectUI] Không có nhân vật nào trong PlayerRegistry.");
            return;
        }

        _currentIndex = 0;
        RefreshUI();
    }

    // ── Navigation ────────────────────────────────────────────────
    private void OnPrev()
    {
        _currentIndex = (_currentIndex - 1 + _characters.Count) % _characters.Count;
        RefreshUI();
    }

    private void OnNext()
    {
        _currentIndex = (_currentIndex + 1) % _characters.Count;
        RefreshUI();
    }

    // ── Confirm ───────────────────────────────────────────────────
    private void OnConfirm()
    {
        if (_characters.Count == 0) return;

        if (DungeonFlowManager.Instance == null)
        {
            Debug.LogError("[CharacterSelectUI] DungeonFlowManager chưa tồn tại.");
            return;
        }

        DungeonFlowManager.Instance.SetPendingTalent(_characters[_currentIndex].talent);

        Hide();
        EventManager.Gm.OnOpenMapSelect.Get().Invoke(this, null);
    }

    // ── Refresh UI ────────────────────────────────────────────────
    private void RefreshUI()
    {
        if (_characters.Count == 0) return;

        PlayerDisplayData data = _characters[_currentIndex];

        // Portrait
        if (portraitImage != null)
        {
            portraitImage.sprite  = data.portrait;
            portraitImage.enabled = data.portrait != null;
        }

        // Tên
        if (nameText != null)
            nameText.text = data.displayName;

        // Ẩn prev/next nếu chỉ có 1 nhân vật
        if (prevButton != null) prevButton.gameObject.SetActive(_characters.Count > 1);
        if (nextButton != null) nextButton.gameObject.SetActive(_characters.Count > 1);
    }

    // ── Public API — gọi từ InteractiveObject trong Hall ──────────
    public void Show()
    {
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}