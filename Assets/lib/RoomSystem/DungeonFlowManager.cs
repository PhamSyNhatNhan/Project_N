using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;

/// <summary>
/// Bộ não điều phối dungeon — DontDestroyOnLoad duy nhất.
/// Chịu trách nhiệm:
///   - Giữ RunSaveData và DungeonConfig xuyên suốt run
///   - Lắng nghe OnRoomCleared → save player state → load scene tiếp
///   - Dùng config.sceneName để load scene, không cần SceneRegistry
/// Không spawn entity, không biết gì về Player/Enemy cụ thể.
/// </summary>
public class DungeonFlowManager : MonoBehaviour
{
    public static DungeonFlowManager Instance { get; private set; }

    // ── Runtime ───────────────────────────────────────────────────
    private RunSaveData   _currentRun;
    private DungeonConfig _dungeonConfig;

    // ── Pending — lưu tạm khi chọn nhân vật, dùng khi confirm map ─
    public Talent? PendingTalent { get; private set; }

    public void SetPendingTalent(Talent talent)
    {
        PendingTalent = talent;
    }

    // ── Properties ────────────────────────────────────────────────
    public RunSaveData   CurrentRun    => _currentRun;
    public DungeonConfig DungeonConfig => _dungeonConfig;
    public FloorConfig   CurrentFloor  => GetFloorConfig(_currentRun?.currentFloor ?? 1);

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        EventManager.Gm.OnRoomCleared.Get().AddListener(HandleRoomCleared);
    }

    private void OnDisable()
    {
        EventManager.Gm.OnRoomCleared.Get().RemoveListener(HandleRoomCleared);
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Bắt đầu run mới. Gọi từ CharacterSelectUI sau khi chọn nhân vật + map.
    /// </summary>
    public void StartNewRun(Talent talent, string dungeonGroupId)
    {
        _currentRun = new RunSaveData
        {
            talent         = talent,
            currentFloor   = 1,
            dungeonGroupId = dungeonGroupId,
        };

        LoadDungeonConfig(dungeonGroupId);
        SaveRunData();
        LoadCurrentFloorScene();
    }

    /// <summary>
    /// Tiếp tục run đã lưu. Gọi từ MainMenu khi player chọn Continue.
    /// </summary>
    public void ContinueRun()
    {
        _currentRun = LoadRunData();
        if (_currentRun == null)
        {
            Debug.LogError("[DungeonFlowManager] Không tìm thấy save data để continue.");
            return;
        }

        if (string.IsNullOrEmpty(_currentRun.dungeonGroupId))
        {
            Debug.LogError("[DungeonFlowManager] RunSaveData thiếu dungeonGroupId.");
            return;
        }

        LoadDungeonConfig(_currentRun.dungeonGroupId);
        LoadCurrentFloorScene();
    }

    /// <summary>
    /// Lấy FloorConfig theo floorId. Trả về null nếu không tìm thấy.
    /// </summary>
    public FloorConfig GetFloorConfig(int floorId)
    {
        if (_dungeonConfig?.floors == null) return null;
        foreach (var floor in _dungeonConfig.floors)
            if (floor.floorId == floorId) return floor;

        Debug.LogWarning($"[DungeonFlowManager] Không tìm thấy FloorConfig cho floor {floorId}");
        return null;
    }

    // ── Event Handler ─────────────────────────────────────────────
    private void HandleRoomCleared(Component sender, object data)
    {
        _currentRun.isRoomCleared  = true;
        _currentRun.isBuffSelected = false;
        SaveRunData();
    }

    /// <summary>
    /// Gọi sau khi player chọn xong Rogue Buff — tăng floor, save state rồi load scene tiếp.
    /// </summary>
    public void HandleRogueBuffSelected()
    {
        _currentRun.currentFloor++;
        _currentRun.isRoomCleared  = false;
        _currentRun.isBuffSelected = false;
        SavePlayerState();
        SaveRunData();
        LoadCurrentFloorScene();
    }

    /// <summary>
    /// Gọi khi clear tầng cuối — save state, xóa save rồi về Start.
    /// </summary>
    public void HandleFinalFloorCleared()
    {
        SavePlayerState();
        DeleteRunData();
        EventManager.Ui.TriggerLoadingScene.Get().Invoke(this, "Start");
    }

    // ── Scene Loading ─────────────────────────────────────────────
    private void LoadCurrentFloorScene()
    {
        FloorConfig config = GetFloorConfig(_currentRun.currentFloor);
        if (config == null)
        {
            Debug.LogError($"[DungeonFlowManager] Không có config cho floor {_currentRun.currentFloor}");
            return;
        }

        if (string.IsNullOrEmpty(config.sceneName))
        {
            Debug.LogError($"[DungeonFlowManager] FloorConfig floor {config.floorId} thiếu sceneName.");
            return;
        }

        EventManager.Ui.TriggerLoadingScene.Get().Invoke(this, config.sceneName);
    }

    // ── Save / Load Player State ──────────────────────────────────
    private void SavePlayerState()
    {
        Stat stat = null;

        var spawner = GetComponent<PlayerSpawner>();
        if (spawner != null)
            stat = spawner.PlayerStat;

        if (stat == null)
            stat = FindObjectOfType<PlayerController>()?.GetComponent<Stat>();

        if (stat == null)
        {
            Debug.LogError("[DungeonFlowManager] Không tìm thấy Stat của Player để save.");
            return;
        }

        // ── Health ────────────────────────────────────────────────
        _currentRun.curHealth = stat.CurHealth;

        // ── Bonus Flat ────────────────────────────────────────────
        _currentRun.bonusFlatHealth            = stat.BonusFlatHealth;
        _currentRun.bonusFlatDefense           = stat.BonusFlatDefense;
        _currentRun.bonusFlatResistantPhysical = stat.BonusFlatResistantPhysical;
        _currentRun.bonusFlatResistantMagic    = stat.BonusFlatResistantMagic;
        _currentRun.bonusFlatAttack            = stat.BonusFlatAttack;

        // ── Bonus Multiplier ──────────────────────────────────────
        _currentRun.bonusMultiplierHealth                = stat.BonusMultiplierHealth;
        _currentRun.bonusMultiplierDefense               = stat.BonusMultiplierDefense;
        _currentRun.bonusMultiplierResistantPhysical     = stat.BonusMultiplierResistantPhysical;
        _currentRun.bonusMultiplierResistantMagic        = stat.BonusMultiplierResistantMagic;
        _currentRun.bonusMultiplierAttack                = stat.BonusMultiplierAttack;
        _currentRun.bonusMultiplierAttackSpeed           = stat.BonusMultiplierAttackSpeed;
        _currentRun.bonusMultiplierBonusDamage           = stat.BonusMultiplierBonusDamage;
        _currentRun.bonusMultiplierBonusPhysical         = stat.BonusMultiplierBonusPhysical;
        _currentRun.bonusMultiplierBonusMagic            = stat.BonusMultiplierBonusMagic;
        _currentRun.bonusMultiplierCritRate              = stat.BonusMultiplierCritRate;
        _currentRun.bonusMultiplierCritDamage            = stat.BonusMultiplierCritDamage;
        _currentRun.bonusMultiplierMultiplierDamageBonus = stat.BonusMultiplierMultiplierDamageBonus;
        _currentRun.bonusMultiplierMultiplierDamageTaken = stat.BonusMultiplierMultiplierDamageTaken;

        // ── RogueBuff ─────────────────────────────────────────────
        var buffManager = FindObjectOfType<RogueBuffManager>();
        if (buffManager != null)
            _currentRun.rogueBuffData = buffManager.CreateSaveData();
    }

    // ── JSON IO ───────────────────────────────────────────────────
    private void LoadDungeonConfig(string groupId)
    {
        // Ưu tiên lấy từ MapRegistry nếu đã load sẵn
        var mapRegistry = FindObjectOfType<MapRegistry>();
        if (mapRegistry != null)
        {
            _dungeonConfig = mapRegistry.GetConfig(groupId);
            if (_dungeonConfig != null) return;
        }

        // Fallback: tự load từ file
        string path = Path.Combine(Application.dataPath, "Data", "Dungeon", $"{groupId}.json");
        if (!File.Exists(path))
        {
            Debug.LogError($"[DungeonFlowManager] Không tìm thấy DungeonConfig: {path}");
            return;
        }
        _dungeonConfig = JsonConvert.DeserializeObject<DungeonConfig>(File.ReadAllText(path));
    }

    private void SaveRunData()
    {
        string path = Path.Combine(Application.persistentDataPath, "RunSave.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(_currentRun, Formatting.Indented));
    }

    private RunSaveData LoadRunData()
    {
        string path = Path.Combine(Application.persistentDataPath, "RunSave.json");
        if (!File.Exists(path)) return null;
        return JsonConvert.DeserializeObject<RunSaveData>(File.ReadAllText(path));
    }

    // ── Debug ─────────────────────────────────────────────────────
    public void DeleteRunData()
    {
        string path = Path.Combine(Application.persistentDataPath, "RunSave.json");
        if (File.Exists(path)) File.Delete(path);
        _currentRun = null;
    }

    public bool HasSaveData()
    {
        return File.Exists(Path.Combine(Application.persistentDataPath, "RunSave.json"));
    }
}