using System.Collections.Generic;

/// <summary>
/// Lưu trữ các buff/debuff tạm thời cho một chỉ số.
/// Flat và Multiplier được tách riêng, mỗi buff dùng string id làm key
/// để có thể thêm/xoá độc lập mà không ảnh hưởng đến chỉ số gốc.
///
/// Công thức áp dụng lên chỉ số gốc (khớp với setStartStat):
///   finalValue = (baseValue + bonusFlat) * (1 + (bonusMultiplier + GetTotalMultiplier()) / 100) + GetTotalFlat()
/// </summary>
public class StatBonus
{
    private readonly Dictionary<string, float> _flat       = new Dictionary<string, float>();
    private readonly Dictionary<string, float> _multiplier = new Dictionary<string, float>();

    // ── Flat ──────────────────────────────────────────────────────────
    public void SetFlat(string id, float value) => _flat[id] = value;
    public void RemoveFlat(string id) => _flat.Remove(id);

    /// <summary>Tổng tất cả buff flat đang active.</summary>
    public float GetTotalFlat()
    {
        float total = 0f;
        foreach (float v in _flat.Values) total += v;
        return total;
    }

    // ── Multiplier ────────────────────────────────────────────────────
    public void SetMultiplier(string id, float value) => _multiplier[id] = value;
    public void RemoveMultiplier(string id) => _multiplier.Remove(id);

    /// <summary>Tổng tất cả buff multiplier đang active.</summary>
    public float GetTotalMultiplier()
    {
        float total = 0f;
        foreach (float v in _multiplier.Values) total += v;
        return total;
    }

    // ── Computed ──────────────────────────────────────────────────────

    /// <summary>
    /// Tính giá trị cuối cùng có tính cả buff tạm thời.
    /// Truyền vào maxStat đã tính sẵn từ Stat (base + bonusFlat/Multiplier cố định).
    ///   result = maxStat * (1 + GetTotalMultiplier() / 100) + GetTotalFlat()
    /// </summary>
    public float GetFinalValue(float maxStat)
        => maxStat * (1f + GetTotalMultiplier() / 100f) + GetTotalFlat();
}