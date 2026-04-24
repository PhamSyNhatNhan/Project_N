using UnityEngine;

/// <summary>
/// Component trong scene — spawn player và inject persistence data.
/// Đặt trong mỗi scene combat/boss/shop, tự tìm PlayerRegistry từ GameManager.
/// Script Execution Order: sau EntityLoader, trước các component khác.
///
/// Flow:
///   Awake: Instantiate prefab → EntityLoader.Awake() tự chạy (load raw data)
///          → inject bonusFlat/bonusMultiplier + LoadPersistenceHealth
///   Start: restore RogueBuff từ RunSaveData
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    // ── Ref ───────────────────────────────────────────────────────
    [SerializeField] private PlayerRegistry playerRegistry;

    // ── Runtime ───────────────────────────────────────────────────
    private Stat _playerStat;

    /// <summary>Dùng cho DungeonFlowManager.SavePlayerState()</summary>
    public Stat PlayerStat => _playerStat;

    // ── Awake — Spawn + Inject stat ───────────────────────────────
    private void Awake()
    {
        if (playerRegistry == null)
            playerRegistry = FindObjectOfType<PlayerRegistry>();

        if (DungeonFlowManager.Instance == null)
        {
            Debug.LogError("[PlayerSpawner] DungeonFlowManager chưa tồn tại.");
            return;
        }

        RunSaveData save = DungeonFlowManager.Instance.CurrentRun;
        if (save == null)
        {
            Debug.LogError("[PlayerSpawner] CurrentRun null — chưa gọi StartNewRun hoặc ContinueRun.");
            return;
        }

        // ── Spawn ─────────────────────────────────────────────────
        GameObject prefab = playerRegistry?.GetPrefab(save.talent);
        if (prefab == null)
        {
            Debug.LogError($"[PlayerSpawner] Không tìm thấy prefab cho Talent '{save.talent}'.");
            return;
        }

        Vector3    spawnPos = GetSpawnPosition();
        GameObject player   = Instantiate(prefab, spawnPos, Quaternion.identity);

        _playerStat = player.GetComponent<Stat>();
        if (_playerStat == null)
        {
            Debug.LogError("[PlayerSpawner] Player prefab thiếu component Stat.");
            return;
        }

        // ── Inject persistence data vào Stat ──────────────────────
        // EntityLoader.Awake() đã chạy → raw data đã có trong Stat
        // Inject trước EntityLoader.Start() → ApplyData() sẽ dùng đúng giá trị
        InjectStatData(save);
    }

    // ── Start — Restore RogueBuff + Fire OnPlayerSpawned ─────────
    private void Start()
    {
        if (_playerStat == null) return;

        RunSaveData save = DungeonFlowManager.Instance.CurrentRun;

        // Restore RogueBuff
        if (save?.rogueBuffData != null)
        {
            var buffManager = _playerStat.GetComponent<RogueBuffManager>();
            if (buffManager == null)
                Debug.LogWarning("[PlayerSpawner] Player thiếu RogueBuffManager — bỏ qua restore RogueBuff.");
            else
                buffManager.Initialize(save.rogueBuffData);
        }

        // Fire sau khi tất cả đã sẵn sàng — UI subscribe để set entityKey
        EventManager.Gm.OnPlayerSpawned.Get().Invoke(this, _playerStat);
    }

    // ── Inject ────────────────────────────────────────────────────
    private void InjectStatData(RunSaveData save)
    {
        // Health persistence — ApplyData() sẽ restore đúng curHealth
        _playerStat.LoadPersistenceHealth(save.curHealth);

        // ── Bonus Flat ────────────────────────────────────────────
        _playerStat.BonusFlatHealth            = save.bonusFlatHealth;
        _playerStat.BonusFlatDefense           = save.bonusFlatDefense;
        _playerStat.BonusFlatResistantPhysical = save.bonusFlatResistantPhysical;
        _playerStat.BonusFlatResistantMagic    = save.bonusFlatResistantMagic;
        _playerStat.BonusFlatAttack            = save.bonusFlatAttack;

        // ── Bonus Multiplier ──────────────────────────────────────
        _playerStat.BonusMultiplierHealth                = save.bonusMultiplierHealth;
        _playerStat.BonusMultiplierDefense               = save.bonusMultiplierDefense;
        _playerStat.BonusMultiplierResistantPhysical     = save.bonusMultiplierResistantPhysical;
        _playerStat.BonusMultiplierResistantMagic        = save.bonusMultiplierResistantMagic;
        _playerStat.BonusMultiplierAttack                = save.bonusMultiplierAttack;
        _playerStat.BonusMultiplierAttackSpeed           = save.bonusMultiplierAttackSpeed;
        _playerStat.BonusMultiplierBonusDamage           = save.bonusMultiplierBonusDamage;
        _playerStat.BonusMultiplierBonusPhysical         = save.bonusMultiplierBonusPhysical;
        _playerStat.BonusMultiplierBonusMagic            = save.bonusMultiplierBonusMagic;
        _playerStat.BonusMultiplierCritRate              = save.bonusMultiplierCritRate;
        _playerStat.BonusMultiplierCritDamage            = save.bonusMultiplierCritDamage;
        _playerStat.BonusMultiplierMultiplierDamageBonus = save.bonusMultiplierMultiplierDamageBonus;
        _playerStat.BonusMultiplierMultiplierDamageTaken = save.bonusMultiplierMultiplierDamageTaken;
    }

    // ── Spawn Position ────────────────────────────────────────────
    private Vector3 GetSpawnPosition()
    {
        var spawnPoint = GameObject.FindWithTag("PlayerSpawnPoint");
        if (spawnPoint != null) return spawnPoint.transform.position;

        Debug.LogWarning("[PlayerSpawner] Không tìm thấy PlayerSpawnPoint tag — spawn tại (0,0,0).");
        return Vector3.zero;
    }
}