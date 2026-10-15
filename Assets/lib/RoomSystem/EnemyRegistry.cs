using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry component — map enemyId (string) → Enemy prefab.
/// enemyId khớp với tên trong WaveConfig.enemyPool của DungeonConfig JSON.
///
/// Kéo vào danh sách của GameRegistry. Truy cập:
///   GameRegistry.Get&lt;EnemyRegistry&gt;("Enemy").GetPrefab("Slime");
/// Mod thêm enemy: .Register("MyModEnemy", prefab);
/// </summary>
public class EnemyRegistry : MonoBehaviour, IGameRegistryComponent
{
    [Serializable]
    public struct Entry
    {
        public string     enemyId;
        public GameObject prefab;
    }

    [Header("Enemy Prefabs")]
    [SerializeField] private Entry[] entries;

    private readonly Dictionary<string, GameObject> _map
        = new Dictionary<string, GameObject>();

    // ── IGameRegistryComponent ────────────────────────────────────
    public void Initialize()
    {
        _map.Clear();
        if (entries == null) return;
        foreach (var e in entries)
            if (e.prefab != null && !string.IsNullOrEmpty(e.enemyId))
                _map[e.enemyId] = e.prefab;
    }

    // ── API ───────────────────────────────────────────────────────
    public GameObject GetPrefab(string enemyId)
    {
        if (!string.IsNullOrEmpty(enemyId) && _map.TryGetValue(enemyId, out var prefab))
            return prefab;
        Debug.LogError($"[EnemyRegistry] Không tìm thấy prefab cho enemyId '{enemyId}'.");
        return null;
    }

    public bool Contains(string enemyId)
        => !string.IsNullOrEmpty(enemyId) && _map.ContainsKey(enemyId);

    public IEnumerable<KeyValuePair<string, GameObject>> GetAll() => _map;

    /// <summary>Đăng ký runtime (mod). Ghi đè nếu id đã tồn tại.</summary>
    public void Register(string enemyId, GameObject prefab)
    {
        if (string.IsNullOrEmpty(enemyId) || prefab == null) return;
        _map[enemyId] = prefab;
    }

    public void Unregister(string enemyId) => _map.Remove(enemyId ?? "");
}