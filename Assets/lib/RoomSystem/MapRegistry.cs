using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

/// <summary>
/// Registry component — load DungeonConfig JSON theo danh sách groupId,
/// cache cả config lẫn MapDisplayData.
///
/// Kéo vào danh sách của GameRegistry. Truy cập:
///   GameRegistry.Get&lt;MapRegistry&gt;("Map").GetConfig("Calamity");
/// Mod thêm map: .Register("MyModDungeon", config); — tự build display data.
/// </summary>
public class MapRegistry : MonoBehaviour, IGameRegistryComponent
{
    [Header("Dungeon Group IDs")]
    [Tooltip("Danh sách groupId khớp với tên file JSON trong Resources/Data/Dungeon/")]
    [SerializeField] private string[] groupIds;

    private readonly Dictionary<string, MapDisplayData> _displayMap = new();
    private readonly Dictionary<string, DungeonConfig>  _configMap  = new();

    // ── IGameRegistryComponent ────────────────────────────────────
    public void Initialize()
    {
        _displayMap.Clear();
        _configMap.Clear();

        if (groupIds == null) return;
        foreach (string groupId in groupIds)
            LoadGroup(groupId);
    }

    // ── Load ──────────────────────────────────────────────────────
    private void LoadGroup(string groupId)
    {
        if (string.IsNullOrEmpty(groupId)) return;

        string resourcePath = $"Data/Dungeon/{groupId}";
        TextAsset textAsset = Resources.Load<TextAsset>(resourcePath);

        if (textAsset == null)
        {
            Debug.LogError($"[MapRegistry] Không tìm thấy file: Resources/{resourcePath}.json");
            return;
        }

        try
        {
            DungeonConfig config = JsonConvert.DeserializeObject<DungeonConfig>(textAsset.text);

            if (config == null)
            {
                Debug.LogError($"[MapRegistry] Parse JSON thất bại: {resourcePath}");
                return;
            }

            Register(groupId, config);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MapRegistry] Lỗi parse '{resourcePath}': {ex.Message}");
        }
    }

    // ── API ───────────────────────────────────────────────────────
    public DungeonConfig GetConfig(string groupId)
    {
        if (!string.IsNullOrEmpty(groupId) && _configMap.TryGetValue(groupId, out var config))
            return config;
        Debug.LogError($"[MapRegistry] Không tìm thấy config cho '{groupId}'.");
        return null;
    }

    /// <summary>Như GetConfig nhưng không log lỗi — dùng cho fallback.</summary>
    public DungeonConfig TryGetConfig(string groupId)
    {
        if (!string.IsNullOrEmpty(groupId) && _configMap.TryGetValue(groupId, out var config))
            return config;
        return null;
    }

    public MapDisplayData GetDisplayData(string groupId)
    {
        if (!string.IsNullOrEmpty(groupId) && _displayMap.TryGetValue(groupId, out var data))
            return data;
        Debug.LogError($"[MapRegistry] Không tìm thấy display data cho '{groupId}'.");
        return null;
    }

    public IEnumerable<MapDisplayData> GetAllDisplayData() => _displayMap.Values;

    /// <summary>
    /// Đăng ký map (mod dùng được) — tự build MapDisplayData từ config.
    /// Ghi đè nếu groupId đã tồn tại.
    /// </summary>
    public void Register(string groupId, DungeonConfig config)
    {
        if (string.IsNullOrEmpty(groupId) || config == null) return;

        _configMap[groupId] = config;

        var display = new MapDisplayData
        {
            dungeonGroupId = groupId,
            displayName    = string.IsNullOrEmpty(config.displayName) ? groupId : config.displayName,
            description    = config.description ?? "",
            preview        = null,
        };

        if (!string.IsNullOrEmpty(config.previewPath))
        {
            display.preview = Resources.Load<Sprite>(config.previewPath);
            if (display.preview == null)
                Debug.LogWarning($"[MapRegistry] Không tìm thấy preview: '{config.previewPath}'");
        }

        _displayMap[groupId] = display;
    }

    public void Unregister(string groupId)
    {
        _configMap.Remove(groupId ?? "");
        _displayMap.Remove(groupId ?? "");
    }
}