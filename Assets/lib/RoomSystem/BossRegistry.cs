using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Component trên GameManager.
/// Map BossId → Boss prefab để RoomSpawner spawn đúng boss.
/// </summary>
public class BossRegistry : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        public BossId     bossId;
        public GameObject prefab;
    }

    [Header("Boss Prefabs")]
    [SerializeField] private Entry[] entries;

    private Dictionary<BossId, GameObject> _map;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        _map = new Dictionary<BossId, GameObject>();
        if (entries == null) return;
        foreach (var e in entries)
            if (e.prefab != null)
                _map[e.bossId] = e.prefab;
    }

    // ── API ───────────────────────────────────────────────────────
    public GameObject GetPrefab(BossId bossId)
    {
        if (_map.TryGetValue(bossId, out GameObject prefab)) return prefab;
        Debug.LogError($"[BossRegistry] Không tìm thấy prefab cho BossId '{bossId}'.");
        return null;
    }
}