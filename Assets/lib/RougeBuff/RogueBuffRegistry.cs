using System.Collections.Generic;
using UnityEngine;

// ════════════════════════════════════════════════════════════════
// Registry component — chứa các Prefab của các RogueBuff group.
//
// Kéo vào danh sách của GameRegistry (entry name gợi ý: "RogueBuff").
// Truy cập:
//   GameRegistry.Get<RogueBuffRegistry>("RogueBuff").TryGetPrefab(...)
//
// Lưu ý: key vẫn là BuffGroupId enum vì RogueBuffSaveData và
// RogueBuffManager đang lưu enum — chuyển sang string sẽ đụng save data,
// để dành cho đợt refactor mod riêng.
// ════════════════════════════════════════════════════════════════
public class RogueBuffRegistry : MonoBehaviour, IGameRegistryComponent
{
    [System.Serializable]
    public struct Entry
    {
        public BuffGroupId    groupId;
        public RogueBuffGroup prefab;
    }

    [Header("Buff Group Prefabs")]
    [SerializeField] private Entry[] entries;

    private readonly Dictionary<BuffGroupId, RogueBuffGroup> _map
        = new Dictionary<BuffGroupId, RogueBuffGroup>();

    // ── IGameRegistryComponent ────────────────────────────────────
    public void Initialize()
    {
        _map.Clear();
        if (entries == null) return;
        foreach (var e in entries)
            if (e.prefab != null)
                _map[e.groupId] = e.prefab;
    }

    // ── API ───────────────────────────────────────────────────────
    public bool TryGetPrefab(BuffGroupId groupId, out RogueBuffGroup prefab)
        => _map.TryGetValue(groupId, out prefab);

    /// <summary>Trả về tất cả entry đã đăng ký — dùng cho RogueBuffSelectUI.</summary>
    public IEnumerable<KeyValuePair<BuffGroupId, RogueBuffGroup>> GetAllEntries() => _map;

    /// <summary>Đăng ký runtime. Ghi đè nếu id đã tồn tại.</summary>
    public void Register(BuffGroupId groupId, RogueBuffGroup prefab)
    {
        if (prefab == null) return;
        _map[groupId] = prefab;
    }

    public void Unregister(BuffGroupId groupId) => _map.Remove(groupId);
}