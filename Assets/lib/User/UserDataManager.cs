using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;

/// <summary>
/// DontDestroyOnLoad — tồn tại xuyên suốt game.
/// Quản lý UserData: Shard, CharacterShard và Talent đã unlock.
/// Tự động inject Dael khi khởi tạo lần đầu.
/// </summary>
public class UserDataManager : MonoBehaviour
{
    public static UserDataManager Instance { get; private set; }

    // ── Default ───────────────────────────────────────────────────
    private static readonly Talent DefaultTalent = Talent.Dael;

    // ── Runtime ───────────────────────────────────────────────────
    private UserData _data;

    // ── Properties ────────────────────────────────────────────────
    public int Shards          => _data.shards;
    public int CharacterShards => _data.characterShards;

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
    public void AddShards(int amount)
    {
        if (amount <= 0) return;
        _data.shards += amount;
        Save();
        EventManager.Gm.OnUserDataChanged.Get().Invoke(this, null);
    }

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

    public bool HasEnoughShards(int amount) => _data.shards >= amount;

    // ── CharacterShard API ────────────────────────────────────────
    public void AddCharacterShards(int amount)
    {
        if (amount <= 0) return;
        _data.characterShards += amount;
        Save();
        EventManager.Gm.OnUserDataChanged.Get().Invoke(this, null);
    }

    public bool SpendCharacterShards(int amount)
    {
        if (amount <= 0) return false;
        if (_data.characterShards < amount)
        {
            Debug.Log($"[UserDataManager] Không đủ CharacterShard. Cần {amount}, có {_data.characterShards}.");
            return false;
        }
        _data.characterShards -= amount;
        Save();
        EventManager.Gm.OnUserDataChanged.Get().Invoke(this, null);
        return true;
    }

    public bool HasEnoughCharacterShards(int amount) => _data.characterShards >= amount;

    // ── Talent API ────────────────────────────────────────────────
    public bool IsUnlocked(Talent talent)
        => _data.unlockedTalents.Contains(talent.ToString());

    public bool UnlockTalent(Talent talent, int cost)
    {
        if (IsUnlocked(talent))
        {
            Debug.Log($"[UserDataManager] {talent} đã được unlock.");
            return false;
        }
        if (!SpendShards(cost)) return false;
        _data.unlockedTalents.Add(talent.ToString());
        Save();
        EventManager.Gm.OnUserDataChanged.Get().Invoke(this, null);
        return true;
    }

    /// <summary>Unlock talent không trừ shard — dùng khi đã thanh toán riêng (gacha).</summary>
    public bool UnlockTalentFree(Talent talent)
    {
        if (IsUnlocked(talent)) return false;
        _data.unlockedTalents.Add(talent.ToString());
        Save();
        EventManager.Gm.OnUserDataChanged.Get().Invoke(this, null);
        return true;
    }

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
        if (_data == null) _data = new UserData();
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

        if (_data.shards < 0)          { _data.shards = 0; dirty = true; }
        if (_data.characterShards < 0) { _data.characterShards = 0; dirty = true; }

        if (_data.unlockedTalents == null)
        {
            _data.unlockedTalents = new System.Collections.Generic.List<string>();
            dirty = true;
        }

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