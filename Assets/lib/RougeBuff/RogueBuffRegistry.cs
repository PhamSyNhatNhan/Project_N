using System.Collections.Generic;
using UnityEngine;

// ════════════════════════════════════════════════════════════════
// Chứa các Prefab của các RogueBuff 
// ════════════════════════════════════════════════════════════════
public class RogueBuffRegistry : MonoBehaviour
{
    public static RogueBuffRegistry Instance { get; private set; }

    [System.Serializable]
    public struct Entry
    {
        public BuffGroupId    groupId;
        public RogueBuffGroup prefab;
    }

    [Header("Buff Group Prefabs")]
    [SerializeField] private Entry[] entries;

    private Dictionary<BuffGroupId, RogueBuffGroup> _map;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        _map = new Dictionary<BuffGroupId, RogueBuffGroup>();
        if (entries == null) return;
        foreach (var e in entries)
            if (e.prefab != null)
                _map[e.groupId] = e.prefab;
    }

    // ── API ───────────────────────────────────────────────────────
    public bool TryGetPrefab(BuffGroupId groupId, out RogueBuffGroup prefab)
    {
        if (_map == null) { prefab = null; return false; }
        return _map.TryGetValue(groupId, out prefab);
    }

    /// <summary>Trả về tất cả entry đã đăng ký — dùng cho RogueBuffSelectUI.</summary>
    public IEnumerable<KeyValuePair<BuffGroupId, RogueBuffGroup>> GetAllEntries()
    {
        if (_map == null) yield break;
        foreach (var pair in _map)
            yield return pair;
    }
}