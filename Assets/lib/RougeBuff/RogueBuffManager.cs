using System.Collections.Generic;
using UnityEngine;

// ════════════════════════════════════════════════════════════════
//  RogueBuffManager — Component trên player GameObject
//
//  Setup: kéo RogueBuffRegistry asset vào field registry — 1 lần
//         dùng chung cho tất cả nhân vật
//
//  Direct API:
//    buffManager.AddGroup(BuffGroupId.Warrior)
//    buffManager.ActivateMinor(BuffGroupId.Warrior, 1)
//
//  Qua event (không cần reference):
//    EventManager.RogueBuff.OnAddGroup.Get().Invoke(this, BuffGroupId.Warrior)
//    EventManager.RogueBuff.OnActivateMinor.Get().Invoke(this, (BuffGroupId.Warrior, 1))
// ════════════════════════════════════════════════════════════════
public class RogueBuffManager : MonoBehaviour
{
    private Dictionary<BuffGroupId, RogueBuffGroup> activeGroups = new();

    // ── Events (C#) ───────────────────────────────────────────────
    public System.Action<RogueBuffGroup> OnGroupAdded;
    public System.Action<MinorBuff, int> OnMinorActivated;
    public System.Action<MajorBuff>      OnMajorUnlocked;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        // Subscribe EventManager → forward xuống direct API
        EventManager.RogueBuff.OnAddGroup.Get()
            .AddListener((sender, data) =>
            {
                if (data is BuffGroupId id) AddGroup(id);
            });

        EventManager.RogueBuff.OnActivateMinor.Get()
            .AddListener((sender, data) =>
            {
                if (data is (BuffGroupId id, int idx)) ActivateMinor(id, idx);
            });
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

        if (RogueBuffRegistry.Instance == null)
        {
            Debug.LogError("[RogueBuffManager] RogueBuffRegistry chưa tồn tại — thêm component vào GameManager");
            return null;
        }

        if (!RogueBuffRegistry.Instance.TryGetPrefab(groupId, out var prefab))
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