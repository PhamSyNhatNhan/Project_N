using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Component trên GameManager.
/// Map RoomType/BossId → scene name để DungeonFlowManager load đúng scene.
/// </summary>
public class SceneRegistry : MonoBehaviour
{
    [Serializable]
    public struct RoomEntry
    {
        public RoomType roomType;
        public string   sceneName;
    }

    [Serializable]
    public struct BossEntry
    {
        public BossId bossId;
        public string sceneName;
    }

    [Header("Room Scenes")]
    [SerializeField] private RoomEntry[] roomEntries;

    [Header("Boss Scenes")]
    [SerializeField] private BossEntry[] bossEntries;

    private Dictionary<RoomType, string> _roomMap;
    private Dictionary<BossId, string>   _bossMap;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        _roomMap = new Dictionary<RoomType, string>();
        if (roomEntries != null)
            foreach (var e in roomEntries)
                _roomMap[e.roomType] = e.sceneName;

        _bossMap = new Dictionary<BossId, string>();
        if (bossEntries != null)
            foreach (var e in bossEntries)
                _bossMap[e.bossId] = e.sceneName;
    }

    // ── API ───────────────────────────────────────────────────────
    public string GetRoomScene(RoomType roomType)
    {
        if (_roomMap.TryGetValue(roomType, out string sceneName)) return sceneName;
        Debug.LogError($"[SceneRegistry] Không tìm thấy scene cho RoomType '{roomType}'.");
        return null;
    }

    public string GetBossScene(BossId bossId)
    {
        if (_bossMap.TryGetValue(bossId, out string sceneName)) return sceneName;
        Debug.LogError($"[SceneRegistry] Không tìm thấy scene cho BossId '{bossId}'.");
        return null;
    }
}