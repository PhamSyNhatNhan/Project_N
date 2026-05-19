using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI chọn nhân vật trong Hall Scene.
/// Chỉ hiện nhân vật đã unlock trong UserDataManager.
/// Điều phối bởi HallUIManager — không tự lắng nghe event.
/// </summary>
public class CharacterSelectUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image           portraitImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Button          prevButton;
    [SerializeField] private Button          nextButton;
    [SerializeField] private Button          confirmButton;

    // ── Runtime ───────────────────────────────────────────────────
    private List<PlayerDisplayData> _characters   = new List<PlayerDisplayData>();
    private int                     _currentIndex = 0;

    // ── Lifecycle ─────────────────────────────────────────────────
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

    // ── Load — chỉ nhân vật đã unlock ────────────────────────────
    private void LoadCharacters()
    {
        _characters.Clear();

        var registry = FindObjectOfType<PlayerRegistry>();
        if (registry == null)
        {
            Debug.LogError("[CharacterSelectUI] Không tìm thấy PlayerRegistry.");
            return;
        }

        if (UserDataManager.Instance == null)
        {
            Debug.LogError("[CharacterSelectUI] Không tìm thấy UserDataManager.");
            return;
        }

        foreach (var data in registry.GetAllDisplayData())
        {
            if (UserDataManager.Instance.IsUnlocked(data.talent))
                _characters.Add(data);
        }

        if (_characters.Count == 0)
        {
            Debug.LogError("[CharacterSelectUI] Không có nhân vật nào đã unlock.");
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

        EventManager.Gm.OnCharacterConfirmed.Get().Invoke(this, null);
    }

    // ── Refresh UI ────────────────────────────────────────────────
    private void RefreshUI()
    {
        if (_characters.Count == 0) return;

        PlayerDisplayData data = _characters[_currentIndex];

        if (portraitImage != null)
        {
            portraitImage.sprite  = data.portrait;
            portraitImage.enabled = data.portrait != null;
        }

        if (nameText != null)
            nameText.text = data.displayName;

        if (prevButton != null) prevButton.gameObject.SetActive(_characters.Count > 1);
        if (nextButton != null) nextButton.gameObject.SetActive(_characters.Count > 1);
    }

    // ── Public API ────────────────────────────────────────────────
    // CharacterSelectUI
    public void Show()
    {
        Debug.Log("[CharacterSelectUI] Show called");
        gameObject.SetActive(true);
        Debug.Log($"[CharacterSelectUI] activeSelf={gameObject.activeSelf}, activeInHierarchy={gameObject.activeInHierarchy}");
    }
    public void Hide()
    {
        Debug.Log("[CharacterSelectUI] Hide called", gameObject);
        gameObject.SetActive(false);
    }}