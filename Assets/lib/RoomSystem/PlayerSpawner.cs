using UnityEngine;

/// <summary>
/// Component trong scene — spawn player và inject persistence data.
/// Đặt trong mỗi scene combat/boss/shop.
///
/// Flow:
///   Awake: Instantiate prefab → EntityLoader.Awake() load raw data
///   EntityLoader.Start: ApplyData() → fire OnEntityLoaded
///   PlayerSpawner: nhận OnEntityLoaded → inject bonusFlat/Multiplier + curHealth → ApplyData lại
///   Start: restore RogueBuff → fire OnPlayerSpawned
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private PlayerRegistry playerRegistry;

    private Stat   _playerStat;
    private string _entityKey;

    public Stat PlayerStat => _playerStat;

    // ── Awake — Spawn ─────────────────────────────────────────────
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
            Debug.LogError("[PlayerSpawner] CurrentRun null.");
            return;
        }

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
            Debug.LogError("[PlayerSpawner] Player prefab thiếu Stat.");
            return;
        }

        // Lắng nghe OnEntityLoaded để inject sau khi ApplyData chạy xong
        _entityKey = $"{_playerStat.NameCharacter}_{_playerStat.GetInstanceID()}";
        EventManager.Gm.OnEntityLoaded.Get(_entityKey).AddListener(OnEntityLoaded);
    }

    // ── OnEntityLoaded — inject sau ApplyData ─────────────────────
    private void OnEntityLoaded(Component sender, object data)
    {
        EventManager.Gm.OnEntityLoaded.Get(_entityKey).RemoveListener(OnEntityLoaded);

        if (_playerStat == null) return;

        RunSaveData save = DungeonFlowManager.Instance.CurrentRun;
        if (save == null) return;

        InjectStatData(save);

        // ApplyData lại để apply đúng bonus đã inject
        _playerStat.ApplyData();
        
    }

    // ── Start — Restore RogueBuff + Fire OnPlayerSpawned ─────────
    private void Start()
    {
        if (_playerStat == null) return;

        RunSaveData save = DungeonFlowManager.Instance.CurrentRun;

        if (save?.rogueBuffData != null)
        {
            var buffManager = _playerStat.GetComponent<RogueBuffManager>();
            if (buffManager == null)
                Debug.LogWarning("[PlayerSpawner] Player thiếu RogueBuffManager.");
            else
                buffManager.Initialize(save.rogueBuffData);
        }

        EventManager.Gm.OnPlayerSpawned.Get().Invoke(this, _playerStat);
    }

    // ── Inject ────────────────────────────────────────────────────
    private void InjectStatData(RunSaveData save)
    {
        _playerStat.LoadPersistenceHealth(save.curHealth);

        _playerStat.BonusFlatHealth            = save.bonusFlatHealth;
        _playerStat.BonusFlatDefense           = save.bonusFlatDefense;
        _playerStat.BonusFlatResistantPhysical = save.bonusFlatResistantPhysical;
        _playerStat.BonusFlatResistantMagic    = save.bonusFlatResistantMagic;
        _playerStat.BonusFlatAttack            = save.bonusFlatAttack;

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

        Debug.LogWarning("[PlayerSpawner] Không tìm thấy PlayerSpawnPoint — spawn tại (0,0,0).");
        return Vector3.zero;
    }
}