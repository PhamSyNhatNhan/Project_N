using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Component trên GameManager.
/// Map enemyId (string) → Enemy prefab để RoomSpawner spawn đúng loại quái.
/// enemyId khớp với tên trong WaveConfig.enemyPool của DungeonConfig JSON.
/// </summary>
public class EnemyRegistry : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        public string     enemyId;
        public GameObject prefab;
    }

    [Header("Enemy Prefabs")]
    [SerializeField] private Entry[] entries;

    private Dictionary<string, GameObject> _map;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        _map = new Dictionary<string, GameObject>();
        if (entries == null) return;
        foreach (var e in entries)
            if (e.prefab != null && !string.IsNullOrEmpty(e.enemyId))
                _map[e.enemyId] = e.prefab;
    }

    // ── API ───────────────────────────────────────────────────────
    public GameObject GetPrefab(string enemyId)
    {
        if (_map.TryGetValue(enemyId, out GameObject prefab)) return prefab;
        Debug.LogError($"[EnemyRegistry] Không tìm thấy prefab cho enemyId '{enemyId}'.");
        return null;
    }
}