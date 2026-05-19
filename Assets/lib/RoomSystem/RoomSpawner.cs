using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Component trên RoomManager GameObject trong scene.
/// Chịu trách nhiệm spawn enemy theo wave, track enemy còn sống,
/// và invoke OnRoomCleared khi hết wave cuối.
/// Bị destroy cùng scene — không DontDestroyOnLoad.
/// </summary>
public class RoomSpawner : MonoBehaviour
{
    // ── Ref ───────────────────────────────────────────────────────
    [SerializeField] private EnemyRegistry  enemyRegistry;
    [SerializeField] private BossRegistry   bossRegistry;
    [SerializeField] private EnemySpawnZone spawnZone;
    [SerializeField] private GameObject     exitGatePrefab;

    // ── Runtime ───────────────────────────────────────────────────
    private FloorConfig          _floorConfig;
    private int                  _currentWaveIndex = 0;
    private List<GameObject>     _aliveEnemies     = new List<GameObject>();
    private bool                 _roomCleared      = false;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Start()
    {
        if (DungeonFlowManager.Instance == null)
        {
            Debug.LogError("[RoomSpawner] DungeonFlowManager chưa tồn tại.");
            return;
        }

        // Tìm Registry từ GameManager nếu chưa assign tay
        if (enemyRegistry == null) enemyRegistry = FindObjectOfType<EnemyRegistry>();
        if (bossRegistry  == null) bossRegistry  = FindObjectOfType<BossRegistry>();

        _floorConfig = DungeonFlowManager.Instance.CurrentFloor;
        if (_floorConfig == null)
        {
            Debug.LogError("[RoomSpawner] Không tìm thấy FloorConfig.");
            return;
        }

        // Shop không có wave — chỉ spawn ExitGate, player tự tương tác
        if (_floorConfig.roomType == RoomType.Shop)
        {
            SpawnExitGate();
            return;
        }

        // Continue sau khi đã chọn buff → load scene tiếp luôn
        var run = DungeonFlowManager.Instance.CurrentRun;
        if (run.isBuffSelected)
        {
            DungeonFlowManager.Instance.HandleRogueBuffSelected();
            return;
        }

        // Continue sau khi clear room nhưng chưa chọn buff → spawn ExitGate
        if (run.isRoomCleared)
        {
            SpawnExitGate();
            return;
        }

        if (_floorConfig.waves == null || _floorConfig.waves.Length == 0)
        {
            Debug.LogWarning("[RoomSpawner] FloorConfig không có wave nào.");
            return;
        }

        // Chờ player tương tác StartGate
        EventManager.Gm.OnStartCombat.Get().AddListener(HandleStartCombat);
    }

    private void OnDestroy()
    {
        EventManager.Gm.OnStartCombat.Get().RemoveListener(HandleStartCombat);
    }

    private void HandleStartCombat(Component sender, object data)
    {
        EventManager.Gm.OnStartCombat.Get().RemoveListener(HandleStartCombat);

        // Fire countdown chỉ cho wave đầu
        if (_floorConfig.waves != null && _floorConfig.waves.Length > 0)
            EventManager.Gm.OnSpawnCountdown.Get().Invoke(this, _floorConfig.waves[0].spawnDelay);

        StartCoroutine(SpawnWave(_currentWaveIndex));
    }

    // ── Wave Spawning ─────────────────────────────────────────────
    private IEnumerator SpawnWave(int waveIndex)
    {
        WaveConfig wave = _floorConfig.waves[waveIndex];

        yield return new WaitForSeconds(wave.spawnDelay);

        if (wave.enemyPool == null || wave.enemyPool.Length == 0)
        {
            Debug.LogWarning($"[RoomSpawner] Wave {waveIndex} không có enemy nào.");
            OnWaveCleared();
            yield break;
        }

        foreach (string entityId in wave.enemyPool)
        {
            // Boss room dùng BossRegistry, còn lại dùng EnemyRegistry
            GameObject prefab = _floorConfig.roomType == RoomType.Boss
                ? bossRegistry?.GetPrefab(_floorConfig.bossId.Value)
                : enemyRegistry?.GetPrefab(entityId);

            if (prefab == null)
            {
                Debug.LogWarning($"[RoomSpawner] Không tìm thấy prefab cho '{entityId}' — bỏ qua.");
                continue;
            }

            Vector3    spawnPos = _floorConfig.roomType == RoomType.Boss
                ? Vector3.zero
                : GetSpawnPosition();
            GameObject enemy    = Instantiate(prefab, spawnPos, Quaternion.identity);

            // Inject ScalingConfig trước ApplyData()
            // EntityLoader.Awake() đã chạy → raw data đã có trong Stat
            InjectScaling(enemy);

            // Track enemy
            _aliveEnemies.Add(enemy);

            // Subscribe OnEntityDead để track khi enemy chết
            var stat = enemy.GetComponent<Stat>();
            if (stat != null)
            {
                string entityKey = $"{stat.NameCharacter}_{stat.GetInstanceID()}";
                EventManager.Entity.OnEntityDead.Get(entityKey)
                    .AddListener((sender, data) => OnEnemyDead(enemy));
            }
        }

        // Nếu tất cả prefab đều null thì coi như wave clear
        if (_aliveEnemies.Count == 0)
            OnWaveCleared();
    }

    // ── Scaling ───────────────────────────────────────────────────
    private void InjectScaling(GameObject enemy)
    {
        var stat = enemy.GetComponent<Stat>();
        if (stat == null) return;

        // Cộng thẳng vào bonusMultiplier trước ApplyData()
        // ScalingConfig chỉ có multiplier, không có flat
        stat.BonusMultiplierHealth                += _floorConfig.scaling.healthMultiplier;
        stat.BonusMultiplierDefense               += _floorConfig.scaling.defenseMultiplier;
        stat.BonusMultiplierResistantPhysical     += _floorConfig.scaling.resistantPhysicalMultiplier;
        stat.BonusMultiplierResistantMagic        += _floorConfig.scaling.resistantMagicMultiplier;
        stat.BonusMultiplierAttack                += _floorConfig.scaling.attackMultiplier;
        stat.BonusMultiplierAttackSpeed           += _floorConfig.scaling.attackSpeedMultiplier;
        stat.BonusMultiplierBonusDamage           += _floorConfig.scaling.bonusDamageMultiplier;
        stat.BonusMultiplierBonusPhysical         += _floorConfig.scaling.bonusPhysicalMultiplier;
        stat.BonusMultiplierBonusMagic            += _floorConfig.scaling.bonusMagicMultiplier;
        stat.BonusMultiplierCritRate              += _floorConfig.scaling.critRateMultiplier;
        stat.BonusMultiplierCritDamage            += _floorConfig.scaling.critDamageMultiplier;
        stat.BonusMultiplierMultiplierDamageBonus += _floorConfig.scaling.multiplierDamageBonusMultiplier;
        stat.BonusMultiplierMultiplierDamageTaken += _floorConfig.scaling.multiplierDamageTakenMultiplier;
    }

    // ── Enemy Dead Tracking ───────────────────────────────────────
    private void OnEnemyDead(GameObject enemy)
    {
        _aliveEnemies.Remove(enemy);

        if (_aliveEnemies.Count == 0)
            OnWaveCleared();
    }

    // ── Wave / Room Cleared ───────────────────────────────────────
    private void OnWaveCleared()
    {
        _currentWaveIndex++;

        // Còn wave tiếp theo
        if (_currentWaveIndex < _floorConfig.waves.Length)
        {
            StartCoroutine(SpawnWave(_currentWaveIndex));
            return;
        }

        // Hết tất cả wave → room cleared
        if (_roomCleared) return;
        _roomCleared = true;

        SpawnExitGate();
        EventManager.Gm.OnRoomCleared.Get().Invoke(this, null);
    }

    // ── Spawn Exit Gate ───────────────────────────────────────────
    private void SpawnExitGate()
    {
        if (exitGatePrefab == null)
        {
            Debug.LogWarning("[RoomSpawner] exitGatePrefab chưa được assign.");
            return;
        }
        Instantiate(exitGatePrefab, Vector3.zero, Quaternion.identity);
    }

    // ── Spawn Position ────────────────────────────────────────────
    private Vector3 GetSpawnPosition()
    {
        if (spawnZone != null) return spawnZone.GetSpawnPosition();

        Debug.LogWarning("[RoomSpawner] Chưa assign EnemySpawnZone — spawn tại (0,0,0).");
        return Vector3.zero;
    }
}