using UnityEngine;

// ── SimpleEffect ──────────────────────────────────────────────────────
// Effect 1 lần, không stack — Stun, Root, Slow đơn giản
public abstract class SimpleEffect : StatusEffect
{
    public override void OnApply()
    {
        base.OnApply();
        FireEvent();
    }

    public override void OnRemove()
    {
        base.OnRemove();
        FireEvent(isRemoved: true);
    }
}

// ── StackingEffect ────────────────────────────────────────────────────
// Cộng dồn giá trị — Burn, Poison, Slow nhiều lớp
public abstract class StackingEffect : StatusEffect
{
    protected int   maxStacks          = 5;
    protected bool  refreshOnStack     = true;
    protected bool  stackResetOnExpire = true;

    protected int curStacks = 1;
    public    int CurStacks => curStacks;
    public    int MaxStacks => maxStacks;

    public virtual void Init(Stat target, Stat source, float duration,
                             float tickInterval, int maxStacks,
                             bool refreshOnStack = true, bool stackResetOnExpire = true)
    {
        base.Init(target, source, duration, tickInterval);
        this.maxStacks          = maxStacks;
        this.refreshOnStack     = refreshOnStack;
        this.stackResetOnExpire = stackResetOnExpire;
        curStacks               = 1;
    }

    public override void OnApply()
    {
        base.OnApply();
        OnApplyValue();
        FireEvent();
    }

    public virtual void AddStack(int newMaxStacks = -1,
                                 float newDuration = -1f,
                                 float newTickInterval = -1f,
                                 bool? newRefreshOnStack = null,
                                 bool? newStackResetOnExpire = null)
    {
        // Cập nhật maxStacks nếu source mới có max cao hơn
        if (newMaxStacks > maxStacks)
            maxStacks = newMaxStacks;

        // Cập nhật duration, tickInterval, flags từ source mới
        if (newDuration      >= 0f) Duration      = newDuration;
        if (newTickInterval  >= 0f) tickInterval   = newTickInterval;
        if (newRefreshOnStack.HasValue)     refreshOnStack     = newRefreshOnStack.Value;
        if (newStackResetOnExpire.HasValue) stackResetOnExpire = newStackResetOnExpire.Value;

        if (curStacks >= maxStacks)
        {
            // Đã max stack — chỉ refresh duration nếu được cấu hình
            if (refreshOnStack) RefreshDuration();
            return;
        }
        OnRemoveValue();
        curStacks++;
        if (refreshOnStack) RefreshDuration();
        OnApplyValue();
        OnStackAdded(curStacks);
        FireEvent(); // stack thay đổi → fire
    }

    protected virtual float CalcValue()             => curStacks;
    protected virtual void  OnApplyValue()          { }
    protected virtual void  OnRemoveValue()         { }
    protected virtual void  OnReapply()             { }
    protected virtual void  OnStackAdded(int current) { }

    public override void OnRemove()
    {
        if (stackResetOnExpire)
        {
            OnRemoveValue();
            curStacks = 0;
            FireEvent(isRemoved: true);
            base.OnRemove();
        }
        else
        {
            OnRemoveValue();
            curStacks--;
            if (curStacks > 0)
            {
                RefreshDuration();
                OnApplyValue();
                FireEvent(); // duration thay đổi đột ngột → fire
                return;
            }
            FireEvent(isRemoved: true);
            base.OnRemove();
        }
    }

    protected override EffectDisplayData BuildDisplayData(bool isRemoved) => new EffectDisplayData
    {
        Type        = Type,
        Category    = Category,
        DisplayName = DisplayName,
        Icon        = icons.Count > 0 ? icons[0] : null,
        Duration    = Duration,
        CurStacks   = curStacks,
        MaxStacks   = maxStacks,
        IsRemoved   = isRemoved
    };

    private void RefreshDuration()
    {
        TimeLeft = Duration;
        FireEvent(); // duration thay đổi đột ngột → fire
    }

    public override void Reset()
    {
        base.Reset();
        curStacks = 0;
    }
}

// ── StackableEffect ───────────────────────────────────────────────────
// Tích stack đến ngưỡng rồi kích — Freeze, PoisonMilestone
// Stack có thời gian tồn tại — không thêm mới trong stackDuration → reset về 0
public abstract class StackableEffect : StatusEffect
{
    protected int   threshold;
    protected float triggerDuration;
    protected float stackDuration;         // thời gian stack tồn tại
    protected bool  canStackDuringTrigger = false;
    protected bool  keepLeftoverStacks    = false;

    protected int   curStacks       = 0;
    protected bool  isTriggerActive = false;
    protected float triggerTimer    = 0f;
    protected float stackDecayTimer = 0f;  // đếm ngược đến khi reset stack

    public int   CurStacks       => curStacks;
    public int   Threshold       => threshold;
    public bool  IsTriggerActive => isTriggerActive;
    public float StackTimeLeft   => stackDecayTimer;

    public override bool IsExpired => false;

    public virtual void Init(Stat target, Stat source,
                             int threshold, float triggerDuration,
                             float stackDuration        = 5f,
                             bool canStackDuringTrigger = false,
                             bool keepLeftoverStacks    = false)
    {
        base.Init(target, source, 0f);
        this.threshold             = threshold;
        this.triggerDuration       = triggerDuration;
        this.stackDuration         = stackDuration;
        this.canStackDuringTrigger = canStackDuringTrigger;
        this.keepLeftoverStacks    = keepLeftoverStacks;
        curStacks                  = 0;
        isTriggerActive            = false;
        triggerTimer               = 0f;
        stackDecayTimer            = 0f;
    }

    public override void OnApply()
    {
        base.OnApply();
        FireEvent();
    }

    public virtual void AddStack(int amount = 1,
                                 int   newThreshold       = -1,
                                 float newTriggerDuration = -1f,
                                 float newStackDuration   = -1f)
    {
        // threshold — lấy min (dễ trigger hơn)
        if (newThreshold > 0 && newThreshold < threshold)
            threshold = newThreshold;

        // triggerDuration, stackDuration — lấy max (kéo dài hơn)
        if (newTriggerDuration > triggerDuration) triggerDuration = newTriggerDuration;
        if (newStackDuration   > stackDuration)   stackDuration   = newStackDuration;

        if (isTriggerActive && !canStackDuringTrigger) return;

        curStacks      += amount;
        stackDecayTimer = stackDuration;
        OnStackAdded(amount);
        FireEvent();

        if (curStacks < threshold) return;

        int leftover    = curStacks - threshold;
        curStacks       = keepLeftoverStacks ? leftover : 0;
        stackDecayTimer = keepLeftoverStacks && leftover > 0 ? stackDuration : 0f;
        isTriggerActive = true;
        triggerTimer    = triggerDuration;
        OnThresholdMet();
        FireEvent();
    }

    public override void OnTick(float deltaTime)
    {
        // Đếm trigger timer
        if (isTriggerActive)
        {
            triggerTimer -= deltaTime;
            if (triggerTimer <= 0f)
            {
                isTriggerActive = false;
                OnTriggerEnd();
            }
            return;
        }

        // Đếm stack decay timer
        if (curStacks <= 0 || stackDuration <= 0f) return;
        stackDecayTimer -= deltaTime;
        if (stackDecayTimer > 0f) return;

        // Hết timer → reset toàn bộ stack
        curStacks       = 0;
        stackDecayTimer = 0f;
        FireEvent(isRemoved: true);
        OnStackDecay();
    }

    /// <summary>Gọi khi stack tự reset do hết decay timer</summary>
    protected virtual void OnStackDecay() { }

    public override void OnRemove()
    {
        base.OnRemove();
        FireEvent(isRemoved: true);
    }

    protected override EffectDisplayData BuildDisplayData(bool isRemoved) => new EffectDisplayData
    {
        Type        = Type,
        Category    = Category,
        DisplayName = DisplayName,
        Icon        = icons.Count > 0 ? icons[0] : null,
        Duration    = stackDuration,
        CurStacks   = curStacks,
        MaxStacks   = threshold,
        IsRemoved   = isRemoved
    };

    protected virtual void OnStackAdded(int amount) { }
    protected virtual void OnThresholdMet()          { }
    protected virtual void OnTriggerEnd()            { }

    public override void Reset()
    {
        base.Reset();
        curStacks       = 0;
        isTriggerActive = false;
        triggerTimer    = 0f;
        stackDecayTimer = 0f;
    }
}