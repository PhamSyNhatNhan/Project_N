using UnityEngine;

/// <summary>
/// Quản lý cooldown và charge cho skill.
/// Hỗ trợ 2 chế độ: PerStack (hồi từng charge) và AllAtOnce (hồi cả cụm).
/// Reduce CD qua flat (giây) và percent (%).
/// Fire OnEntitySkillCdReady mỗi khi trạng thái thay đổi để UI cập nhật.
/// </summary>
public class AbilityCooldown
{
    // ── Config ────────────────────────────────────────────────────
    private readonly string     skillId;
    private readonly string     entityKey;
    private readonly Sprite     icon;
    private readonly bool       showOnUI;
    private          float      baseCd;
    private          int        maxCharge;
    private          ChargeMode chargeMode;

    // ── Reduce ────────────────────────────────────────────────────
    private float reduceFlat    = 0f;
    private float reducePercent = 0f;

    // ── Runtime ───────────────────────────────────────────────────
    private int   curCharge;
    private float timer;
    private bool  isOnCd;

    // ── Properties ────────────────────────────────────────────────
    public int    CurCharge => curCharge;
    public int    MaxCharge => maxCharge;
    public float  BaseCd    => baseCd;
    public bool   IsReady   => curCharge > 0;
    public bool   IsOnCd    => isOnCd;
    public float  CdLeft    => isOnCd ? timer : 0f;
    public Sprite Icon      => icon;
    public bool   ShowOnUI  => showOnUI;

    /// <summary>CD thực tế sau khi apply reduce</summary>
    public float ActualCd
    {
        get
        {
            float cd = baseCd * (1f - Mathf.Clamp(reducePercent, 0f, 100f) / 100f);
            cd -= reduceFlat;
            return Mathf.Max(0f, cd);
        }
    }

    // ── Constructor ───────────────────────────────────────────────
    public AbilityCooldown(string skillId, string entityKey, float baseCd,
                           int        maxCharge  = 1,
                           ChargeMode chargeMode = ChargeMode.PerStack,
                           Sprite     icon       = null,
                           bool       showOnUI   = false)
    {
        this.skillId    = skillId;
        this.entityKey  = entityKey;
        this.baseCd     = baseCd;
        this.maxCharge  = maxCharge;
        this.chargeMode = chargeMode;
        this.icon       = icon;
        this.showOnUI   = showOnUI;

        curCharge = maxCharge;
        timer     = 0f;
        isOnCd    = false;
    }

    // ── Tick — gọi từ Skill.Update() ─────────────────────────────
    public void Tick(float deltaTime)
    {
        if (!isOnCd) return;

        timer -= deltaTime;
        if (timer > 0f) return;

        if (chargeMode == ChargeMode.PerStack)
        {
            curCharge = Mathf.Min(curCharge + 1, maxCharge);

            if (curCharge < maxCharge)
            {
                timer = ActualCd;
                FireEvent();
            }
            else
            {
                isOnCd = false;
                timer  = 0f;
                FireEvent();
            }
        }
        else // AllAtOnce
        {
            curCharge = maxCharge;
            isOnCd    = false;
            timer     = 0f;
            FireEvent();
        }
    }

    // ── Use ───────────────────────────────────────────────────────
    /// <summary>Fire event lần đầu khi khởi tạo — dùng cho skill có showOnUI = true</summary>
    public void FireInitialEvent()
    {
        if (!showOnUI) return;
        FireEvent();
    }

    public bool Use()
    {
        if (curCharge <= 0) return false;
        curCharge--;
        StartCdIfNeeded();
        FireEvent();
        return true;
    }

    // ── Reduce ────────────────────────────────────────────────────
    public void SetReduceFlat(float flat)            => reduceFlat    = Mathf.Max(0f, flat);
    public void SetReducePercent(float percent)      => reducePercent = Mathf.Clamp(percent, 0f, 100f);
    public void SetReduce(float flat, float percent) { SetReduceFlat(flat); SetReducePercent(percent); }

    // ── Reset ─────────────────────────────────────────────────────
    public void ResetToFull()
    {
        curCharge = maxCharge;
        isOnCd    = false;
        timer     = 0f;
    }

    // ── Internal ──────────────────────────────────────────────────
    private void StartCdIfNeeded()
    {
        if (isOnCd) return;

        bool shouldStart = chargeMode == ChargeMode.PerStack
            ? curCharge < maxCharge
            : curCharge <= 0;

        if (!shouldStart) return;

        isOnCd = true;
        timer  = ActualCd;
    }

    private void FireEvent()
    {
        bool isRemoved = maxCharge <= 1 && !isOnCd && !showOnUI;

        EventManager.Entity.OnEntitySkillCdReady
            .Get(entityKey)
            .Invoke(null, new SkillCdReadyData
            {
                SkillId      = skillId,
                Icon         = icon,
                CurCharge    = curCharge,
                MaxCharge    = maxCharge,
                CdLeft       = CdLeft,
                BaseCd       = ActualCd,
                Counter      = 0,
                MaxCounter   = 0,
                IsInfinite   = false,
                ShowOnUI     = showOnUI,
                DisplayOrder = 0,
                IsRemoved    = isRemoved
            });
    }
}

/// <summary>Data kèm theo event khi CD/counter/state thay đổi</summary>
public class SkillCdReadyData
{
    public string SkillId    { get; set; }
    public Sprite Icon       { get; set; }

    // ── Charge ────────────────────────────────────────────────────
    public int   CurCharge  { get; set; }
    public int   MaxCharge  { get; set; }

    // ── CD ────────────────────────────────────────────────────────
    public float CdLeft     { get; set; }
    public float BaseCd     { get; set; }

    // ── Counter ───────────────────────────────────────────────────
    public int   Counter    { get; set; }
    public int   MaxCounter { get; set; }

    // ── Infinite ──────────────────────────────────────────────────
    public bool  IsInfinite { get; set; }

    // ── Display ───────────────────────────────────────────────────
    public bool  ShowOnUI     { get; set; }
    public int   DisplayOrder { get; set; }

    // ── State ─────────────────────────────────────────────────────
    public bool  IsRemoved  { get; set; }
}