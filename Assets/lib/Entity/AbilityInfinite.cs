using UnityEngine;

/// <summary>
/// Cơ chế hiển thị trạng thái — không có CD timer.
/// Dùng để hiện icon khi entity đang ở trạng thái đặc biệt.
/// Activate() / Deactivate() để bật/tắt, có guard check tránh fire thừa.
/// </summary>
public class AbilityInfinite
{
    // ── Config ────────────────────────────────────────────────────
    private readonly string skillId;
    private readonly string entityKey;
    private readonly Sprite icon;
    private readonly bool   showOnUI;

    // ── Runtime ───────────────────────────────────────────────────
    private bool isReady;

    // ── Properties ────────────────────────────────────────────────
    public bool   IsReady  => isReady;
    public Sprite Icon     => icon;
    public bool   ShowOnUI => showOnUI;

    // ── Constructor ───────────────────────────────────────────────
    public AbilityInfinite(string skillId, string entityKey,
                           bool   isReady  = false,
                           bool   showOnUI = false,
                           Sprite icon     = null)
    {
        this.skillId   = skillId;
        this.entityKey = entityKey;
        this.isReady   = isReady;
        this.showOnUI  = showOnUI;
        this.icon      = icon;
    }

    // ── Init ──────────────────────────────────────────────────────
    /// <summary>
    /// Fire lần đầu khi ApplyData() — chỉ fire nếu showOnUI = true VÀ isReady = true.
    /// </summary>
    public void FireInitialEvent()
    {
        if (!showOnUI || !isReady) return;
        FireEvent();
    }

    // ── API ───────────────────────────────────────────────────────
    /// <summary>Bật trạng thái — không fire nếu đã true.</summary>
    public void Activate()
    {
        if (isReady) return;
        isReady = true;
        FireEvent();
    }

    /// <summary>Tắt trạng thái — không fire nếu đã false.</summary>
    public void Deactivate()
    {
        if (!isReady) return;
        isReady = false;
        FireEvent();
    }

    /// <summary>Use() — đổi isReady thành false, tương đương Deactivate().</summary>
    public void Use()
    {
        Deactivate();
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
                Counter      = 0,
                MaxCounter   = 0,
                IsInfinite   = true,
                ShowOnUI     = showOnUI,
                DisplayOrder = 2,
                IsRemoved    = !isReady
            });
    }
}