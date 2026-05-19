using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;

/// <summary>
/// DontDestroyOnLoad — tồn tại xuyên suốt game.
/// Quản lý UserData: Shard và Talent đã unlock.
/// Tự động inject Sakuya khi khởi tạo lần đầu.
/// </summary>
public class UserDataManager : MonoBehaviour
{
    public static UserDataManager Instance { get; private set; }

    // ── Default ───────────────────────────────────────────────────
    private static readonly Talent DefaultTalent = Talent.Sakuya;

    // ── Runtime ───────────────────────────────────────────────────
    private UserData _data;

    // ── Properties ────────────────────────────────────────────────
    public int Shards => _data.shards;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Load();
    }

    // ── Shard API ─────────────────────────────────────────────────

    /// <summary>Thêm shard. Chỉ chấp nhận giá trị dương.</summary>
    public void AddShards(int amount)
    {
        if (amount <= 0) return;
        _data.shards += amount;
        Save();
        EventManager.Gm.OnUserDataChanged.Get().Invoke(this, null);
    }

    /// <summary>
    /// Tiêu shard. Trả về true nếu đủ tiền và trừ thành công.
    /// </summary>
    public bool SpendShards(int amount)
    {
        if (amount <= 0) return false;
        if (_data.shards < amount)
        {
            Debug.Log($"[UserDataManager] Không đủ Shard. Cần {amount}, có {_data.shards}.");
            return false;
        }
        _data.shards -= amount;
        Save();
        EventManager.Gm.OnUserDataChanged.Get().Invoke(this, null);
        return true;
    }

    /// <summary>Kiểm tra đủ shard không.</summary>
    public bool HasEnoughShards(int amount) => _data.shards >= amount;

    // ── Talent API ────────────────────────────────────────────────

    /// <summary>Kiểm tra talent đã unlock chưa.</summary>
    public bool IsUnlocked(Talent talent)
        => _data.unlockedTalents.Contains(talent.ToString());

    /// <summary>
    /// Unlock talent bằng shard. Trả về true nếu thành công.
    /// Thất bại nếu: đã unlock, hoặc không đủ shard.
    /// </summary>
    public bool UnlockTalent(Talent talent, int cost)
    {
        if (IsUnlocked(talent))
        {
            Debug.Log($"[UserDataManager] {talent} đã được unlock.");
            return false;
        }

        if (!SpendShards(cost))
            return false;

        _data.unlockedTalents.Add(talent.ToString());
        Save();
        EventManager.Gm.OnUserDataChanged.Get().Invoke(this, null);
        return true;
    }

    /// <summary>Lấy danh sách tất cả talent đã unlock.</summary>
    public IReadOnlyList<string> GetUnlockedTalents() => _data.unlockedTalents;

    // ── Save / Load ───────────────────────────────────────────────
    private static string SavePath
        => Path.Combine(Application.persistentDataPath, "UserData.json");

    private void Load()
    {
        if (File.Exists(SavePath))
        {
            try
            {
                _data = JsonConvert.DeserializeObject<UserData>(File.ReadAllText(SavePath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UserDataManager] Lỗi load UserData: {ex.Message} — tạo mới.");
                _data = null;
            }
        }

        if (_data == null)
            _data = new UserData();

        Validate();
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(SavePath, JsonConvert.SerializeObject(_data, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UserDataManager] Lỗi save UserData: {ex.Message}");
        }
    }

    // ── Validate ──────────────────────────────────────────────────
    private void Validate()
    {
        bool dirty = false;

        // Shards không âm
        if (_data.shards < 0)
        {
            _data.shards = 0;
            dirty = true;
        }

        // unlockedTalents không null
        if (_data.unlockedTalents == null)
        {
            _data.unlockedTalents = new List<string>();
            dirty = true;
        }

        // Sakuya luôn unlock
        string defaultKey = DefaultTalent.ToString();
        if (!_data.unlockedTalents.Contains(defaultKey))
        {
            _data.unlockedTalents.Add(defaultKey);
            dirty = true;
        }

        if (dirty) Save();
    }

    // ── Debug ─────────────────────────────────────────────────────
    public void DeleteUserData()
    {
        if (File.Exists(SavePath)) File.Delete(SavePath);
        _data = new UserData();
        Validate();
    }
}