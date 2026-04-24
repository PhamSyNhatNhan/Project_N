using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;

/// <summary>
/// Component trên GameManager.
/// Map Talent → PlayerDisplayData (prefab + display info từ JSON).
/// </summary>
public class PlayerRegistry : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        public Talent     talent;
        public GameObject prefab;
    }

    [Header("Player Prefabs")]
    [SerializeField] private Entry[] entries;

    private Dictionary<Talent, PlayerDisplayData> _map;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        _map = new Dictionary<Talent, PlayerDisplayData>();
        if (entries == null) return;

        foreach (var e in entries)
        {
            if (e.prefab == null) continue;

            PlayerDisplayData displayData = LoadDisplayData(e.talent, e.prefab);
            _map[e.talent] = displayData;
        }
    }

    // ── Load display data từ JSON ─────────────────────────────────
    private PlayerDisplayData LoadDisplayData(Talent talent, GameObject prefab)
    {
        string name = talent.ToString();
        string path = Path.Combine(Application.dataPath, "Entity", "Character", name, "Data", $"{name}.json");

        var data = new PlayerDisplayData
        {
            talent      = talent,
            prefab      = prefab,
            displayName = name,
            description = "",
            avatar      = null,
            portrait    = null,
        };

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[PlayerRegistry] Không tìm thấy JSON: {path}");
            return data;
        }

        try
        {
            string     json       = File.ReadAllText(path);
            EntityData entityData = JsonConvert.DeserializeObject<EntityData>(json);

            if (entityData?.stat != null)
            {
                data.displayName = entityData.stat.Get<string>("displayName", name);
                data.description = entityData.stat.Get<string>("description", "");

                string iconPath = entityData.stat.Get<string>("entityIconPath", "");
                if (!string.IsNullOrEmpty(iconPath))
                {
                    data.avatar = Resources.Load<Sprite>(iconPath);
                    if (data.avatar == null)
                        Debug.LogWarning($"[PlayerRegistry] Không tìm thấy avatar: '{iconPath}'");
                }

                string portraitPath = entityData.stat.Get<string>("portraitPath", "");
                if (!string.IsNullOrEmpty(portraitPath))
                {
                    data.portrait = Resources.Load<Sprite>(portraitPath);
                    if (data.portrait == null)
                        Debug.LogWarning($"[PlayerRegistry] Không tìm thấy portrait: '{portraitPath}'");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlayerRegistry] Lỗi parse JSON '{path}': {ex.Message}");
        }

        return data;
    }

    // ── API ───────────────────────────────────────────────────────
    public GameObject GetPrefab(Talent talent)
    {
        if (_map.TryGetValue(talent, out var data)) return data.prefab;
        Debug.LogError($"[PlayerRegistry] Không tìm thấy prefab cho Talent '{talent}'.");
        return null;
    }

    public PlayerDisplayData GetDisplayData(Talent talent)
    {
        if (_map.TryGetValue(talent, out var data)) return data;
        Debug.LogError($"[PlayerRegistry] Không tìm thấy display data cho Talent '{talent}'.");
        return null;
    }

    public IEnumerable<PlayerDisplayData> GetAllDisplayData()
        => _map?.Values ?? System.Linq.Enumerable.Empty<PlayerDisplayData>();
}