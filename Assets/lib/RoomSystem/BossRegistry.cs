using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry component — map bossId (string) → Boss prefab.
/// Key trong map = BossId.ToString() để mod thêm boss mới không cần sửa enum.
///
/// Kéo vào danh sách của GameRegistry. Truy cập:
///   GameRegistry.Get&lt;BossRegistry&gt;("Boss").GetPrefab(BossId.Calamitas);
/// Mod thêm boss: .Register("MyModBoss", prefab);
/// </summary>
public class BossRegistry : MonoBehaviour, IGameRegistryComponent
{
    [Serializable]
    public struct Entry
    {
        public BossId     bossId;
        public GameObject prefab;
    }

    [Header("Boss Prefabs")]
    [SerializeField] private Entry[] entries;

    private readonly Dictionary<string, GameObject> _map
        = new Dictionary<string, GameObject>();

    // ── IGameRegistryComponent ────────────────────────────────────
    public void Initialize()
    {
        _map.Clear();
        if (entries == null) return;
        foreach (var e in entries)
            if (e.prefab != null)
                _map[e.bossId.ToString()] = e.prefab;
    }

    // ── API ───────────────────────────────────────────────────────
    public GameObject GetPrefab(BossId bossId) => GetPrefab(bossId.ToString());

    public GameObject GetPrefab(string bossId)
    {
        if (!string.IsNullOrEmpty(bossId) && _map.TryGetValue(bossId, out var prefab))
            return prefab;
        Debug.LogError($"[BossRegistry] Không tìm thấy prefab cho BossId '{bossId}'.");
        return null;
    }

    /// <summary>Đăng ký runtime (mod). Ghi đè nếu id đã tồn tại.</summary>
    public void Register(string bossId, GameObject prefab)
    {
        if (string.IsNullOrEmpty(bossId) || prefab == null) return;
        _map[bossId] = prefab;
    }

    public void Unregister(string bossId) => _map.Remove(bossId ?? "");
}