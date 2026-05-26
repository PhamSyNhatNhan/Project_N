using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// DontDestroyOnLoad — quản lý logic gacha:
///   - Roll 1 hoặc 10 lần
///   - Tính pity (soft pity tuyến tính từ 0, hard pity ở 90)
///   - Nếu ra Talent đã có → trả về CharacterShard thay thế
///   - Lưu pity vào file GachaSave.json
/// </summary>
public class GachaManager : MonoBehaviour
{
    public static GachaManager Instance { get; private set; }

    private const int ShardOnDuplicate = 7;

    private GachaSaveData _saveData;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>Roll 1 lần. Trả về kết quả và trừ shard.</summary>
    public List<GachaPullResult> Pull(GachaBannerData banner, int count)
    {
        int cost = banner.costPerPull * count;
        if (!UserDataManager.Instance.SpendShards(cost))
        {
            Debug.Log("[GachaManager] Không đủ Shard.");
            return null;
        }

        var results = new List<GachaPullResult>();
        int pity = _saveData.GetPity(banner.bannerId);

        for (int i = 0; i < count; i++)
        {
            pity++;
            float rate = CalcRate(banner, pity);
            bool isTalent = Random.value * 100f < rate;

            GachaPullResult result;
            if (isTalent)
            {
                pity = 0; // reset pity
                bool alreadyUnlocked = UserDataManager.Instance.IsUnlocked(banner.talentEnum);

                if (alreadyUnlocked)
                {
                    // Đã có nhân vật → CharacterShard
                    result = new GachaPullResult
                    {
                        type   = GachaResultType.CharacterShard,
                        talent = banner.talentEnum,
                        shards = ShardOnDuplicate
                    };
                    UserDataManager.Instance.AddShards(ShardOnDuplicate);
                }
                else
                {
                    // Unlock talent mới
                    UserDataManager.Instance.UnlockTalentFree(banner.talentEnum);
                    result = new GachaPullResult
                    {
                        type   = GachaResultType.Talent,
                        talent = banner.talentEnum,
                    };
                }
            }
            else
            {
                result = new GachaPullResult
                {
                    type   = GachaResultType.CharacterShard,
                    talent = banner.talentEnum,
                    shards = 1
                };
                UserDataManager.Instance.AddCharacterShards(1);
            }

            results.Add(result);
        }

        _saveData.SetPity(banner.bannerId, pity);
        Save();
        return results;
    }

    public int GetPity(string bannerId) => _saveData.GetPity(bannerId);

    /// <summary>Rate thực tế theo pity — tuyến tính từ đầu, mạnh sau softPityStart.</summary>
    public float CalcRate(GachaBannerData banner, int pity)
    {
        float baseRate = banner.talentRate;
        if (pity < banner.softPityStart)
            return baseRate + pity * 0.03f;   // tăng nhẹ từ đầu

        // Sau soft pity: tăng mạnh đến 100% ở hardLimit
        float softRange = banner.pityHardLimit - banner.softPityStart;
        float prog      = (pity - banner.softPityStart) / softRange;
        return Mathf.Lerp(baseRate, 100f, prog);
    }

    // ── Save / Load ───────────────────────────────────────────────
    private static string SavePath
        => Path.Combine(Application.persistentDataPath, "GachaSave.json");

    private void Load()
    {
        if (File.Exists(SavePath))
        {
            try
            {
                _saveData = JsonConvert.DeserializeObject<GachaSaveData>(File.ReadAllText(SavePath));
            }
            catch { _saveData = null; }
        }
        if (_saveData == null) _saveData = new GachaSaveData();
    }

    private void Save()
    {
        File.WriteAllText(SavePath, JsonConvert.SerializeObject(_saveData, Formatting.Indented));
    }

    public void DeleteSaveData()
    {
        if (File.Exists(SavePath)) File.Delete(SavePath);
        _saveData = new GachaSaveData();
    }
}