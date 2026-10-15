using System.Collections.Generic;
using UnityEngine;

// ════════════════════════════════════════════════════════════════
//  RogueBuffManager — Component trên player GameObject
//
//  Player luôn duy nhất trong scene → expose static Instance
//  Direct API:
//    RogueBuffManager.Instance.AddGroup(BuffGroupId.Warrior)
//    RogueBuffManager.Instance.ActivateMinor(BuffGroupId.Warrior, 1)
//
//  Qua event (không cần reference):
//    EventManager.RogueBuff.OnAddGroup.Get().Invoke(this, BuffGroupId.Warrior)
//    EventManager.RogueBuff.OnActivateMinor.Get().Invoke(this, (BuffGroupId.Warrior, 1))
// ════════════════════════════════════════════════════════════════
public class RogueBuffManager : MonoBehaviour
{
    /// <summary>
    /// Instance trên player hiện tại — null khi chưa spawn hoặc đã destroy.
    /// Per-scene: player mới spawn sẽ tự thay thế.
    /// </summary>
    public static RogueBuffManager Instance { get; private set; }

    private Dictionary<BuffGroupId, RogueBuffGroup> activeGroups = new();

    // ── Events (C#) ───────────────────────────────────────────────
    public System.Action<RogueBuffGroup> OnGroupAdded;
    public System.Action<MinorBuff, int> OnMinorActivated;
    public System.Action<MajorBuff>      OnMajorUnlocked;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("[RogueBuffManager] Đã tồn tại Instance khác — ghi đè (player mới spawn?).");
        Instance = this;

        // Subscribe EventManager → forward xuống direct API
        EventManager.RogueBuff.OnAddGroup.Get()
            .AddListener(HandleAddGroupEvent);

        EventManager.RogueBuff.OnActivateMinor.Get()
            .AddListener(HandleActivateMinorEvent);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        EventManager.RogueBuff.OnAddGroup.Get()
            .RemoveListener(HandleAddGroupEvent);
        EventManager.RogueBuff.OnActivateMinor.Get()
            .RemoveListener(HandleActivateMinorEvent);
    }

    // ── Event handlers ────────────────────────────────────────────
    private void HandleAddGroupEvent(Component sender, object data)
    {
        if (data is BuffGroupId id) AddGroup(id);
    }

    private void HandleActivateMinorEvent(Component sender, object data)
    {
        if (data is (BuffGroupId id, int idx)) ActivateMinor(id, idx);
    }

    // ── Direct API ────────────────────────────────────────────────

    /// <summary>
    /// Thêm group vào player theo id — tự tìm prefab trong registry.
    /// Nếu group đã tồn tại thì trả về group đó luôn.
    /// </summary>
    public RogueBuffGroup AddGroup(BuffGroupId groupId)
    {
        if (activeGroups.TryGetValue(groupId, out var existing))
            return existing;

        var registry = GameRegistry.Get<RogueBuffRegistry>("RogueBuff");
        if (registry == null) return null; // GameRegistry đã log lỗi

        if (!registry.TryGetPrefab(groupId, out var prefab))
        {
            Debug.LogError($"[RogueBuffManager] Không tìm thấy prefab cho group '{groupId}' trong Registry");
            return null;
        }

        var group = Instantiate(prefab, transform);
        group.name = $"BuffGroup_{groupId}";

        group.OnMinorActivated += (buff, idx) => OnMinorActivated?.Invoke(buff, idx);
        group.OnMajorUnlocked  += buff         => OnMajorUnlocked?.Invoke(buff);

        activeGroups[groupId] = group;
        OnGroupAdded?.Invoke(group);
        return group;
    }

    /// <summary>
    /// Activate minor buff. Nếu group chưa tồn tại → tự AddGroup trước.
    /// </summary>
    public void ActivateMinor(BuffGroupId groupId, int minorIndex)
    {
        if (!activeGroups.ContainsKey(groupId))
            AddGroup(groupId);

        if (!activeGroups.TryGetValue(groupId, out var group)) return;
        group.ActivateMinor(minorIndex);
    }

    // ── Initialize (Restore from save) ───────────────────────────
    public void Initialize(RogueBuffSaveData saveData)
    {
        if (saveData?.groupStates == null) return;

        foreach (var state in saveData.groupStates)
        {
            var group = AddGroup(state.groupId);
            group?.Initialize(state.minorStates);
        }
    }

    // ── Save ──────────────────────────────────────────────────────
    public RogueBuffSaveData CreateSaveData()
    {
        var data = new RogueBuffSaveData
        {
            groupStates = new List<RogueBuffSaveData.GroupState>()
        };

        foreach (var pair in activeGroups)
            data.groupStates.Add(new RogueBuffSaveData.GroupState
            {
                groupId     = pair.Key,
                minorStates = pair.Value.GetMinorStates(),
            });

        return data;
    }

    // ── Reset ─────────────────────────────────────────────────────
    public void ResetAll()
    {
        foreach (var group in activeGroups.Values)
            group.DeactivateAll();
    }

    public void DestroyAll()
    {
        foreach (var group in activeGroups.Values)
            Destroy(group.gameObject);
        activeGroups.Clear();
    }

    // ── Query ─────────────────────────────────────────────────────
    public RogueBuffGroup GetGroup(BuffGroupId groupId)
    {
        activeGroups.TryGetValue(groupId, out var g);
        return g;
    }

    public bool HasGroup(BuffGroupId groupId) => activeGroups.ContainsKey(groupId);

    public IReadOnlyDictionary<BuffGroupId, RogueBuffGroup> ActiveGroups => activeGroups;
}