using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

/// <summary>
/// Registry component — map talentId (string) → PlayerDisplayData
/// (prefab + display info build từ JSON Entity/Player/{name}/Data/{name}.json).
///
/// Kéo vào danh sách của GameRegistry. Truy cập:
///   GameRegistry.Get&lt;PlayerRegistry&gt;("Player").GetPrefab(talent);
/// Mod thêm nhân vật: .Register("MyCharacter", displayData);
/// </summary>
public class PlayerRegistry : MonoBehaviour, IGameRegistryComponent
{
    [Serializable]
    public struct Entry
    {
        public Talent     talent;
        public GameObject prefab;
    }

    [Header("Player Prefabs")]
    [SerializeField] private Entry[] entries;

    private readonly Dictionary<string, PlayerDisplayData> _map
        = new Dictionary<string, PlayerDisplayData>();

    // ── IGameRegistryComponent ────────────────────────────────────
    public void Initialize()
    {
        _map.Clear();
        if (entries == null) return;

        foreach (var e in entries)
        {
            if (e.prefab == null) continue;
            _map[e.talent.ToString()] = LoadDisplayData(e.talent, e.prefab);
        }
    }

    // ── API ───────────────────────────────────────────────────────
    public GameObject GetPrefab(Talent talent) => GetDisplayData(talent.ToString())?.prefab;
    public GameObject GetPrefab(string talentId) => GetDisplayData(talentId)?.prefab;

    public PlayerDisplayData GetDisplayData(Talent talent) => GetDisplayData(talent.ToString());

    public PlayerDisplayData GetDisplayData(string talentId)
    {
        if (!string.IsNullOrEmpty(talentId) && _map.TryGetValue(talentId, out var data))
            return data;
        Debug.LogError($"[PlayerRegistry] Không tìm thấy display data cho '{talentId}'.");
        return null;
    }

    public IEnumerable<PlayerDisplayData> GetAllDisplayData() => _map.Values;

    /// <summary>Đăng ký runtime (mod). Ghi đè nếu id đã tồn tại.</summary>
    public void Register(string talentId, PlayerDisplayData data)
    {
        if (string.IsNullOrEmpty(talentId) || data == null) return;
        _map[talentId] = data;
    }

    public void Unregister(string talentId) => _map.Remove(talentId ?? "");

    // ── Load display data từ JSON ─────────────────────────────────
    private PlayerDisplayData LoadDisplayData(Talent talent, GameObject prefab)
    {
        string name         = talent.ToString();
        string resourcePath = $"Entity/Player/{name}/Data/{name}";

        var data = new PlayerDisplayData
        {
            talent      = talent,
            prefab      = prefab,
            displayName = name,
            description = "",
            avatar      = null,
            portrait    = null,
        };

        TextAsset textAsset = Resources.Load<TextAsset>(resourcePath);
        if (textAsset == null)
        {
            Debug.LogWarning($"[PlayerRegistry] Không tìm thấy file: Resources/{resourcePath}.json");
            return data;
        }

        try
        {
            EntityData entityData = JsonConvert.DeserializeObject<EntityData>(textAsset.text);

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
            Debug.LogError($"[PlayerRegistry] Lỗi parse '{resourcePath}': {ex.Message}");
        }

        return data;
    }
}