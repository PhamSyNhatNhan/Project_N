using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

/// <summary>
/// Component trên GameManager.
/// Load tất cả DungeonConfig JSON theo danh sách groupId,
/// cache MapDisplayData để MapSelectUI hiển thị.
/// </summary>
public class MapRegistry : MonoBehaviour
{
    [Header("Dungeon Group IDs")]
    [Tooltip("Danh sách groupId khớp với tên file JSON trong Assets/Data/Dungeon/")]
    [SerializeField] private string[] groupIds;

    private Dictionary<string, MapDisplayData>  _displayMap  = new();
    private Dictionary<string, DungeonConfig>   _configMap   = new();

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        if (groupIds == null) return;

        foreach (string groupId in groupIds)
            LoadGroup(groupId);
    }

    // ── Load ──────────────────────────────────────────────────────
    private void LoadGroup(string groupId)
    {
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
        catch (Exception ex)
        {
            Debug.LogError($"[MapRegistry] Lỗi parse '{resourcePath}': {ex.Message}");
        }
    }

    // ── API ───────────────────────────────────────────────────────
    public DungeonConfig GetConfig(string groupId)
    {
        if (_configMap.TryGetValue(groupId, out var config)) return config;
        Debug.LogError($"[MapRegistry] Không tìm thấy config cho '{groupId}'.");
        return null;
    }

    public MapDisplayData GetDisplayData(string groupId)
    {
        if (_displayMap.TryGetValue(groupId, out var data)) return data;
        Debug.LogError($"[MapRegistry] Không tìm thấy display data cho '{groupId}'.");
        return null;
    }

    public IEnumerable<MapDisplayData> GetAllDisplayData() => _displayMap.Values;
}