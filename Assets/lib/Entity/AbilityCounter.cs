using UnityEngine;

/// <summary>
/// Cơ chế stack/counter — không có CD timer.
/// Luôn hiển thị nếu showOnUI = true.
/// curCounter clamp [0, maxCounter], không âm, không vượt max.
/// </summary>
public class AbilityCounter
{
    // ── Config ────────────────────────────────────────────────────
    private readonly string skillId;
    private readonly string entityKey;
    private readonly Sprite icon;
    private readonly bool   showOnUI;
    private readonly int    maxCounter;

    // ── Runtime ───────────────────────────────────────────────────
    private int curCounter;

    // ── Properties ────────────────────────────────────────────────
    public int    CurCounter => curCounter;
    public int    MaxCounter => maxCounter;
    public Sprite Icon       => icon;
    public bool   ShowOnUI   => showOnUI;

    /// <summary>IsReady — có ít nhất 1 stack.</summary>
    public bool IsReady => curCounter > 0;

    /// <summary>IsReady với số stack yêu cầu cụ thể.</summary>
    public bool IsReadyFor(int required) => curCounter >= required;

    // ── Constructor ───────────────────────────────────────────────
    public AbilityCounter(string skillId, string entityKey,
                          int    maxCounter,
                          bool   showOnUI = false,
                          Sprite icon     = null)
    {
        this.skillId    = skillId;
        this.entityKey  = entityKey;
        this.maxCounter = maxCounter;
        this.showOnUI   = showOnUI;
        this.icon       = icon;

        curCounter = 0;
    }

    // ── Init ──────────────────────────────────────────────────────
    /// <summary>
    /// Fire lần đầu khi ApplyData() — chỉ fire nếu showOnUI = true.
    /// Hiển thị 0/maxCounter ngay từ đầu.
    /// </summary>
    public void FireInitialEvent()
    {
        if (!showOnUI) return;
        FireEvent();
    }

    // ── API ───────────────────────────────────────────────────────
    /// <summary>Cộng stack, clamp về maxCounter. Không fire nếu không thay đổi.</summary>
    public void AddCounter(int amount)
    {
        if (amount <= 0) return;
        int next = Mathf.Min(curCounter + amount, maxCounter);
        if (next == curCounter) return;
        curCounter = next;
        FireEvent();
    }

    /// <summary>Set stack trực tiếp, clamp [0, maxCounter]. Không fire nếu không thay đổi.</summary>
    public void SetCounter(int value)
    {
        int next = Mathf.Clamp(value, 0, maxCounter);
        if (next == curCounter) return;
        curCounter = next;
        FireEvent();
    }

    /// <summary>Trừ stack. Không âm. Không fire nếu amount <= 0.</summary>
    public bool Use(int amount = 1)
    {
        if (amount <= 0) return false;
        if (curCounter < amount) return false;
        curCounter -= amount;
        FireEvent();
        return true;
    }

    // ── Internal ──────────────────────────────────────────────────
    private void FireEvent()
    {
        if (!showOnUI) return;

        EventManager.Entity.OnEntitySkillCdReady
            .Get(entityKey)
            .Invoke(null, new SkillCdReadyData
            {
                SkillId      = skillId,
                Icon         = icon,
                CurCharge    = 0,
                MaxCharge    = 0,
                CdLeft       = 0f,
                BaseCd       = 0f,
                Counter      = curCounter,
                MaxCounter   = maxCounter,
                IsInfinite   = false,
                ShowOnUI     = showOnUI,
                DisplayOrder = 1,
                IsRemoved    = false
            });
    }
}