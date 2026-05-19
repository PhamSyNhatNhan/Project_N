using TMPro;
using UnityEngine;

/// <summary>
/// Gắn trong Hall scene — quản lý toàn bộ UI nội bộ:
///   - Shard display
///   - CharacterSelect → MapSelect flow
///   - Inventory, CharacterInfo, Shop, Gacha panels (chưa triển khai)
/// </summary>
public class HallUIManager : MonoBehaviour
{
    [Header("Shard Display")]
    [SerializeField] private TextMeshProUGUI shardText;

    [Header("Select UI")]
    [SerializeField] private CharacterSelectUI characterSelectUI;
    [SerializeField] private MapSelectUI       mapSelectUI;

    // TODO: triển khai sau
    // [Header("Panels")]
    // [SerializeField] private GameObject inventoryPanel;
    // [SerializeField] private GameObject characterInfoPanel;
    // [SerializeField] private GameObject shopPanel;
    // [SerializeField] private GameObject gachaPanel;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        EventManager.Gm.OnUserDataChanged.Get().AddListener(HandleUserDataChanged);
        EventManager.Gm.OnCharacterConfirmed.Get().AddListener(HandleCharacterConfirmed);
        EventManager.Gm.OnOpenCharacterSelect.Get().AddListener(HandleOpenCharacterSelect);
    }

    private void OnDestroy()
    {
        EventManager.Gm.OnUserDataChanged.Get().RemoveListener(HandleUserDataChanged);
        EventManager.Gm.OnCharacterConfirmed.Get().RemoveListener(HandleCharacterConfirmed);
        EventManager.Gm.OnOpenCharacterSelect.Get().RemoveListener(HandleOpenCharacterSelect);
    }

    private void Start()
    {
        CloseAll();
        RefreshShardDisplay();
    }

    // ── Shard ─────────────────────────────────────────────────────
    private void HandleUserDataChanged(Component sender, object data)
    {
        RefreshShardDisplay();
    }

    private void RefreshShardDisplay()
    {
        if (shardText == null) return;
        if (UserDataManager.Instance == null) return;
        shardText.text = $"{UserDataManager.Instance.Shards:N0}";
    }

    // ── Character → Map flow ──────────────────────────────────────
    private void HandleOpenCharacterSelect(Component sender, object data)
    {
        Debug.Log("[HallUIManager] HandleOpenCharacterSelect called");

        CloseAll();
        characterSelectUI?.Show();
    }

    private void HandleCharacterConfirmed(Component sender, object data)
    {
        characterSelectUI?.Hide();
        mapSelectUI?.Show();
    }

    // ── TODO: Panel Toggle — uncomment khi triển khai ─────────────
    // public void ToggleInventory()     => Toggle(inventoryPanel);
    // public void ToggleCharacterInfo() => Toggle(characterInfoPanel);
    // public void ToggleShop()          => Toggle(shopPanel);
    // public void ToggleGacha()         => Toggle(gachaPanel);

    public void CloseAll()
    {
        characterSelectUI?.Hide();
        mapSelectUI?.Hide();

        // TODO: uncomment khi triển khai
        // SetPanel(inventoryPanel,     false);
        // SetPanel(characterInfoPanel, false);
        // SetPanel(shopPanel,          false);
        // SetPanel(gachaPanel,         false);
    }

    // ── Helpers ───────────────────────────────────────────────────
    // private void Toggle(GameObject panel)
    // {
    //     if (panel == null) return;
    //     bool next = !panel.activeSelf;
    //     CloseAll();
    //     SetPanel(panel, next);
    // }

    // private void SetPanel(GameObject panel, bool active)
    // {
    //     if (panel != null) panel.SetActive(active);
    // }
}