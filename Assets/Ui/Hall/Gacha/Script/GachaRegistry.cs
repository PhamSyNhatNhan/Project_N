using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Component trên GameManager.
/// Load tất cả GachaBannerData JSON từ Resources/Data/Gacha/
/// theo danh sách bannerIds trong Inspector.
/// </summary>
public class GachaRegistry : MonoBehaviour
{
    [Header("Banner IDs")]
    [Tooltip("Danh sách bannerId khớp với tên file JSON trong Resources/Data/Gacha/")]
    [SerializeField] private string[] bannerIds;

    private readonly List<GachaBannerData> _banners = new List<GachaBannerData>();

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        if (bannerIds == null) return;
        foreach (string id in bannerIds)
            LoadBanner(id);
    }

    // ── Load ──────────────────────────────────────────────────────
    private void LoadBanner(string bannerId)
    {
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

            _banners.Add(data);
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
        foreach (var b in _banners)
            if (b.bannerId == bannerId) return b;
        Debug.LogError($"[GachaRegistry] Không tìm thấy banner '{bannerId}'");
        return null;
    }
}