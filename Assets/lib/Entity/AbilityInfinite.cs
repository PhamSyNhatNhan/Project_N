using UnityEngine;

/// <summary>
/// Cơ chế hiển thị trạng thái — không có CD timer.
/// Dùng để hiện icon khi entity đang ở trạng thái đặc biệt.
/// Activate() / Deactivate() để bật/tắt, có guard check tránh fire thừa.
/// Optional duration: > 0 thì tự deactivate sau duration giây.
/// Activate lại khi đang active → reset timer.
/// Tick(deltaTime) phải được gọi từ Skill.Update() mỗi frame.
/// </summary>
public class AbilityInfinite
{
    // ── Config ────────────────────────────────────────────────────
    private readonly string skillId;
    private readonly string entityKey;
    private readonly Sprite icon;
    private readonly bool   showOnUI;

    // ── Runtime ───────────────────────────────────────────────────
    private bool  isReady;
    private float duration;
    private float timer;

    // ── Properties ────────────────────────────────────────────────
    public bool   IsReady   => isReady;
    public Sprite Icon      => icon;
    public bool   ShowOnUI  => showOnUI;
    public float  TimeLeft  => timer;

    // ── Constructor ───────────────────────────────────────────────
    public AbilityInfinite(string skillId, string entityKey,
                           bool   isReady  = false,
                           bool   showOnUI = false,
                           Sprite icon     = null,
                           float  duration = 0f)
    {
        this.skillId   = skillId;
        this.entityKey = entityKey;
        this.isReady   = isReady;
        this.showOnUI  = showOnUI;
        this.icon      = icon;
        this.duration  = duration;
        this.timer     = 0f;
    }

    // ── Tick ──────────────────────────────────────────────────────
    /// <summary>Gọi từ Skill.Update() mỗi frame khi có duration.</summary>
    public void Tick(float deltaTime)
    {
        if (!isReady || duration <= 0f) return;

        timer -= deltaTime;
        if (timer <= 0f)
        {
            timer = 0f;
            Deactivate();
        }
    }

    // ── Init ──────────────────────────────────────────────────────
    public void FireInitialEvent()
    {
        if (!showOnUI || !isReady) return;
        FireEvent();
    }

    // ── API ───────────────────────────────────────────────────────
    /// <summary>
    /// Bật trạng thái.
    /// Nếu đang active và có duration → reset timer.
    /// </summary>
    public void Activate(float overrideDuration = -1f)
    {
        float d = overrideDuration >= 0f ? overrideDuration : duration;

        if (isReady)
        {
            // Reset timer nếu đang active
            if (d > 0f) timer = d;
            return;
        }

        isReady = true;
        timer   = d > 0f ? d : 0f;
        FireEvent();
    }

    /// <summary>Tắt trạng thái — không fire nếu đã false.</summary>
    public void Deactivate()
    {
        if (!isReady) return;
        isReady = false;
        timer   = 0f;
        FireEvent();
    }

    /// <summary>Use() — tương đương Deactivate().</summary>
    public void Use() => Deactivate();

    /// <summary>Set duration mới (không ảnh hưởng trạng thái hiện tại).</summary>
    public void SetDuration(float d) => duration = d;

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