using TMPro;
using UnityEngine;

/// <summary>
/// Gắn trong Hall scene — quản lý toàn bộ UI nội bộ:
///   - Shard display
///   - CharacterSelect → MapSelect flow
///   - CharacterInfo panel
///   - Shop, Gacha panels (chưa triển khai)
/// </summary>
public class HallUIManager : MonoBehaviour
{
    [Header("Shard Display")]
    [SerializeField] private TextMeshProUGUI shardText;
    [SerializeField] private TextMeshProUGUI charShardText;

    [Header("Select UI")]
    [SerializeField] private CharacterSelectUI characterSelectUI;
    [SerializeField] private MapSelectUI       mapSelectUI;

    [Header("Panels")]
    [SerializeField] private CharacterInfoUI characterInfoUI;
    [SerializeField] private GachaUI          gachaUI;
    // [SerializeField] private GameObject shopPanel;
    // [SerializeField] private GameObject gachaPanel;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        EventManager.Gm.OnUserDataChanged.Get().AddListener(HandleUserDataChanged);
        EventManager.Gm.OnCharacterConfirmed.Get().AddListener(HandleCharacterConfirmed);
        EventManager.Gm.OnOpenCharacterSelect.Get().AddListener(HandleOpenCharacterSelect);
        EventManager.Gm.OnOpenCharacterInfo.Get().AddListener(HandleOpenCharacterInfo);
        EventManager.Gm.OnOpenGacha.Get().AddListener(HandleOpenGacha);
    }

    private void OnDestroy()
    {
        EventManager.Gm.OnUserDataChanged.Get().RemoveListener(HandleUserDataChanged);
        EventManager.Gm.OnCharacterConfirmed.Get().RemoveListener(HandleCharacterConfirmed);
        EventManager.Gm.OnOpenCharacterSelect.Get().RemoveListener(HandleOpenCharacterSelect);
        EventManager.Gm.OnOpenCharacterInfo.Get().RemoveListener(HandleOpenCharacterInfo);
        EventManager.Gm.OnOpenGacha.Get().RemoveListener(HandleOpenGacha);
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
        if (UserDataManager.Instance == null) return;
        if (shardText     != null) shardText.text     = $"{UserDataManager.Instance.Shards:N0}";
        if (charShardText != null) charShardText.text = $"{UserDataManager.Instance.CharacterShards:N0}";
    }

    // ── Character → Map flow ──────────────────────────────────────
    private void HandleOpenCharacterSelect(Component sender, object data)
    {
        CloseAll();
        characterSelectUI?.Show();
    }

    private void HandleCharacterConfirmed(Component sender, object data)
    {
        characterSelectUI?.Hide();
        mapSelectUI?.Show();
    }

    // ── Character Info ────────────────────────────────────────────
    private void HandleOpenCharacterInfo(Component sender, object data)
    {
        CloseAll();
        characterInfoUI?.Show();
    }

    // ── Gacha ─────────────────────────────────────────────────────
    private void HandleOpenGacha(Component sender, object data)
    {
        CloseAll();
        gachaUI?.Show();
    }

    // ── Close All ─────────────────────────────────────────────────
    public void CloseAll()
    {
        characterSelectUI?.Hide();
        mapSelectUI?.Hide();
        characterInfoUI?.Hide();
        gachaUI?.Hide();
    }
}