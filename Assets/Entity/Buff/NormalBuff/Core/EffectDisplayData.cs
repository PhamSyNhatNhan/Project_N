using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data class gửi kèm event OnEntityEffectChanged để UI cập nhật.
/// Fire khi: apply, remove, stack thay đổi, duration thay đổi đột ngột.
/// UI tự đếm duration từ Duration — không fire mỗi tick.
/// </summary>
public class EffectDisplayData
{
    // ── Identity ──────────────────────────────────────────────────
    public EffectType     Type        { get; set; }
    public EffectCategory Category    { get; set; }
    public string         DisplayName { get; set; }

    // Effect tự quyết định truyền icon nào tùy trạng thái
    public Sprite         Icon        { get; set; }

    // ── Duration — UI tự đếm từ đây ──────────────────────────────
    public float Duration { get; set; }

    // ── Stack ─────────────────────────────────────────────────────
    // CurStacks = MaxStacks = 0 nếu không phải StackingEffect
    public int CurStacks { get; set; }
    public int MaxStacks { get; set; }

    // ── State ─────────────────────────────────────────────────────
    // true → UI xóa icon, false → UI thêm/cập nhật icon
    public bool IsRemoved { get; set; }
}