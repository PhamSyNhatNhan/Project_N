using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gắn lên Panel (Grid Layout Group) — quản lý toàn bộ buff/debuff icon.
/// Subscribe OnPlayerSpawned để tự động set entityKey khi player spawn.
/// </summary>
public class BuffPanel : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private GameObject buffIconPrefab;

    private string _entityKey = "";
    private Dictionary<EffectType, BuffIconUI> activeIcons = new();

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        EventManager.Gm.OnPlayerSpawned.Get().AddListener(OnPlayerSpawned);
    }

    private void OnDestroy()
    {
        EventManager.Gm.OnPlayerSpawned.Get().RemoveListener(OnPlayerSpawned);
        Unsubscribe();
    }

    private void OnEnable()  => Subscribe();
    private void OnDisable() => Unsubscribe();

    // ── Handler ───────────────────────────────────────────────────
    private void OnPlayerSpawned(Component sender, object data)
    {
        if (data is not Stat stat) return;
        SetEntityKey($"{stat.NameCharacter}_{stat.GetInstanceID()}");
    }

    private void OnEffectChanged(Component sender, object data)
    {
        if (data is not EffectDisplayData effectData) return;

        if (effectData.IsRemoved)
        {
            RemoveIcon(effectData.Type);
            return;
        }

        if (activeIcons.TryGetValue(effectData.Type, out var existing))
            existing.Refresh(effectData);
        else
            AddIcon(effectData);
    }

    // ── Internal ──────────────────────────────────────────────────
    private void AddIcon(EffectDisplayData data)
    {
        if (buffIconPrefab == null)
        {
            Debug.LogWarning("[BuffPanel] buffIconPrefab chưa được gán!");
            return;
        }

        var go   = Instantiate(buffIconPrefab, transform);
        var icon = go.GetComponent<BuffIconUI>();
        if (icon == null)
        {
            Debug.LogWarning("[BuffPanel] buffIconPrefab thiếu component BuffIconUI!");
            Destroy(go);
            return;
        }

        icon.SetData(data);
        activeIcons[data.Type] = icon;
    }

    private void RemoveIcon(EffectType type)
    {
        if (!activeIcons.TryGetValue(type, out var icon)) return;
        Destroy(icon.gameObject);
        activeIcons.Remove(type);
    }

    private void Subscribe()
    {
        if (string.IsNullOrEmpty(_entityKey)) return;
        EventManager.Entity.OnEntityEffectChanged
            .Get(_entityKey)
            .AddListener(OnEffectChanged);
    }

    private void Unsubscribe()
    {
        if (string.IsNullOrEmpty(_entityKey)) return;
        EventManager.Entity.OnEntityEffectChanged
            .Get(_entityKey)
            .RemoveListener(OnEffectChanged);
    }

    public void ClearAll()
    {
        foreach (var icon in activeIcons.Values)
            if (icon != null) Destroy(icon.gameObject);
        activeIcons.Clear();
    }

    public void SetEntityKey(string key)
    {
        Unsubscribe();
        ClearAll();
        _entityKey = key;
        Subscribe();
    }

    public string EntityKey => _entityKey;
}