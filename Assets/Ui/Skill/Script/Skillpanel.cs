using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Quản lý skill icon — subscribe OnEntitySkillCdReady
/// Mặc định tự tìm Player qua tag
/// Thứ tự hiển thị: AbilityCooldown (0) → AbilityCounter (1) → AbilityInfinite (2)
/// </summary>
public class SkillPanel : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private GameObject skillIconPrefab;
    [SerializeField] private string     entityKey = "";

    private bool keySetFromOutside = false;

    // ── Runtime — key = skillId ───────────────────────────────────
    private Dictionary<string, SkillIconUI> activeIcons = new();

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Start()
    {
        if (keySetFromOutside) return;

        if (string.IsNullOrEmpty(entityKey))
        {
            var stat = GameObject.FindWithTag("Player")?.GetComponent<Stat>();
            if (stat != null)
                SetEntityKey($"{stat.NameCharacter}_{stat.GetInstanceID()}");
            else
                Debug.LogWarning("[SkillPanel] Không tìm thấy Player tag!");
        }
        else
        {
            Subscribe();
        }
    }

    private void OnDisable() => Unsubscribe();

    // ── Handler ───────────────────────────────────────────────────
    private void OnSkillCdChanged(Component sender, object data)
    {
        if (data is not SkillCdReadyData skillData) return;
        if (!skillData.ShowOnUI) return;

        if (skillData.IsRemoved)
        {
            RemoveIcon(skillData.SkillId);
            return;
        }

        if (activeIcons.TryGetValue(skillData.SkillId, out var existing))
        {
            existing.Refresh(skillData);
        }
        else
        {
            AddIcon(skillData);
            SortIcons();
        }
    }

    private void RemoveIcon(string skillId)
    {
        if (!activeIcons.TryGetValue(skillId, out var icon)) return;
        Destroy(icon.gameObject);
        activeIcons.Remove(skillId);
        SortIcons();
    }

    // ── Internal ──────────────────────────────────────────────────
    private void AddIcon(SkillCdReadyData data)
    {
        if (skillIconPrefab == null)
        {
            Debug.LogWarning("[SkillPanel] skillIconPrefab chưa được gán!");
            return;
        }

        var go   = Instantiate(skillIconPrefab, transform);
        var icon = go.GetComponent<SkillIconUI>();
        if (icon == null)
        {
            Debug.LogWarning("[SkillPanel] skillIconPrefab thiếu SkillIconUI!");
            Destroy(go);
            return;
        }

        icon.SetData(data);
        activeIcons[data.SkillId] = icon;
    }

    /// <summary>
    /// Sắp xếp lại toàn bộ icon theo DisplayOrder.
    /// Cùng DisplayOrder → giữ thứ tự hiện tại (ổn định).
    /// </summary>
    private void SortIcons()
    {
        var sorted = activeIcons.Values
            .Where(icon => icon != null)
            .OrderBy(icon => icon.DisplayOrder)
            .ToList();

        for (int i = 0; i < sorted.Count; i++)
            sorted[i].transform.SetSiblingIndex(i);
    }

    private void Subscribe()
    {
        if (string.IsNullOrEmpty(entityKey)) return;
        EventManager.Entity.OnEntitySkillCdReady
            .Get(entityKey)
            .AddListener(OnSkillCdChanged);
    }

    private void Unsubscribe()
    {
        if (string.IsNullOrEmpty(entityKey)) return;
        EventManager.Entity.OnEntitySkillCdReady
            .Get(entityKey)
            .RemoveListener(OnSkillCdChanged);
    }

    public void SetEntityKey(string key)
    {
        keySetFromOutside = true;
        Unsubscribe();
        entityKey = key;
        Subscribe();
    }

    public string EntityKey => entityKey;
}