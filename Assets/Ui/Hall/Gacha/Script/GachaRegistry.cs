using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Registry component — load tất cả GachaBannerData JSON từ
/// Resources/Data/Gacha/ theo danh sách bannerIds trong Inspector.
///
/// Kéo vào danh sách của GameRegistry (entry name gợi ý: "Gacha"). Truy cập:
///   GameRegistry.Get&lt;GachaRegistry&gt;("Gacha").GetBanner(bannerId);
/// Mod thêm banner: .Register(bannerData);
/// </summary>
public class GachaRegistry : MonoBehaviour, IGameRegistryComponent
{
    [Header("Banner IDs")]
    [Tooltip("Danh sách bannerId khớp với tên file JSON trong Resources/Data/Gacha/")]
    [SerializeField] private string[] bannerIds;

    private readonly List<GachaBannerData>               _banners   = new List<GachaBannerData>();
    private readonly Dictionary<string, GachaBannerData> _bannerMap = new Dictionary<string, GachaBannerData>();

    // ── IGameRegistryComponent ────────────────────────────────────
    public void Initialize()
    {
        _banners.Clear();
        _bannerMap.Clear();

        if (bannerIds == null) return;
        foreach (string id in bannerIds)
            LoadBanner(id);
    }

    // ── Load ──────────────────────────────────────────────────────
    private void LoadBanner(string bannerId)
    {
        if (string.IsNullOrEmpty(bannerId)) return;

        string resourcePath = $"Data/Gacha/{bannerId}";
        TextAsset textAsset = Resources.Load<TextAsset>(resourcePath);

        if (textAsset == null)
        {
            Debug.LogError($"[GachaRegistry] Không tìm thấy: Resources/{resourcePath}.json");
            return;
        }

        try
        {
            GachaBannerData data = JsonConvert.DeserializeObject<GachaBannerData>(textAsset.text);
            if (data == null) return;

            // Parse Talent enum
            if (Enum.TryParse<Talent>(data.talent, out Talent t))
                data.talentEnum = t;
            else
                Debug.LogWarning($"[GachaRegistry] Không parse được Talent '{data.talent}'");

            // Load portrait
            if (!string.IsNullOrEmpty(data.portraitPath))
            {
                data.portrait = Resources.Load<Sprite>(data.portraitPath);
                if (data.portrait == null)
                    Debug.LogWarning($"[GachaRegistry] Không tìm thấy portrait: '{data.portraitPath}'");
            }

            Register(data);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GachaRegistry] Lỗi parse '{bannerId}': {ex.Message}");
        }
    }

    // ── API ───────────────────────────────────────────────────────
    public IReadOnlyList<GachaBannerData> GetAllBanners() => _banners;

    public GachaBannerData GetBanner(string bannerId)
    {
        if (!string.IsNullOrEmpty(bannerId) && _bannerMap.TryGetValue(bannerId, out var data))
            return data;
        Debug.LogError($"[GachaRegistry] Không tìm thấy banner '{bannerId}'");
        return null;
    }

    /// <summary>Đăng ký runtime (mod). Ghi đè nếu bannerId đã tồn tại.</summary>
    public void Register(GachaBannerData data)
    {
        if (data == null || string.IsNullOrEmpty(data.bannerId)) return;

        if (_bannerMap.TryGetValue(data.bannerId, out var existing))
            _banners.Remove(existing);

        _bannerMap[data.bannerId] = data;
        _banners.Add(data);
    }

    public void Unregister(string bannerId)
    {
        if (string.IsNullOrEmpty(bannerId)) return;
        if (_bannerMap.TryGetValue(bannerId, out var existing))
        {
            _banners.Remove(existing);
            _bannerMap.Remove(bannerId);
        }
    }
}