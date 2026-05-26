using UnityEngine;

/// <summary>
/// Cơ chế stack/counter — không có CD timer.
/// curCounter clamp [0, maxCounter], không âm, không vượt max.
/// Hook Before/On/After cho cả Add và Use.
/// Optional duration: > 0 thì tự reset counter về 0 sau duration giây.
/// Timer reset mỗi khi AddCounter được gọi.
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
    private int   curCounter;
    private float duration;
    private float timer;

    // ── Properties ────────────────────────────────────────────────
    public int    CurCounter => curCounter;
    public int    MaxCounter => maxCounter;
    public Sprite Icon       => icon;
    public bool   ShowOnUI   => showOnUI;
    public float  TimeLeft   => timer;

    public bool IsReady              => curCounter > 0;
    public bool IsReadyFor(int req)  => curCounter >= req;

    // ── Hooks ─────────────────────────────────────────────────────
    public event System.Action<int> OnBeforeCounterAdded;
    public event System.Action<int> OnCounterAdded;
    public event System.Action<int> OnAfterCounterAdded;

    public event System.Action<int> OnBeforeCounterUsed;
    public event System.Action<int> OnCounterUsed;
    public event System.Action<int> OnAfterCounterUsed;

    // ── Constructor ───────────────────────────────────────────────
    public AbilityCounter(string skillId, string entityKey,
                          int    maxCounter,
                          bool   showOnUI = false,
                          Sprite icon     = null,
                          float  duration = 0f)
    {
        this.skillId    = skillId;
        this.entityKey  = entityKey;
        this.maxCounter = maxCounter;
        this.showOnUI   = showOnUI;
        this.icon       = icon;
        this.duration   = duration;
        curCounter      = 0;
        timer           = 0f;
    }

    // ── Tick ──────────────────────────────────────────────────────
    public void Tick(float deltaTime)
    {
        if (duration <= 0f || curCounter <= 0) return;
        timer -= deltaTime;
        if (timer <= 0f)
        {
            timer = 0f;
            SetCounter(0);
        }
    }

    // ── Init ──────────────────────────────────────────────────────
    public void FireInitialEvent()
    {
        if (!showOnUI) return;
        FireEvent();
    }

    // ── API ───────────────────────────────────────────────────────
    public void AddCounter(int amount)
    {
        if (amount <= 0) return;
        int next = Mathf.Min(curCounter + amount, maxCounter);
        if (next == curCounter) return;

        OnBeforeCounterAdded?.Invoke(amount);
        curCounter = next;
        if (duration > 0f) timer = duration;
        FireEvent();
        OnCounterAdded?.Invoke(amount);
        OnAfterCounterAdded?.Invoke(curCounter);
    }

    public void SetCounter(int value)
    {
        int next = Mathf.Clamp(value, 0, maxCounter);
        if (next == curCounter) return;
        int delta = next - curCounter;

        if (delta > 0)
        {
            OnBeforeCounterAdded?.Invoke(delta);
            curCounter = next;
            if (duration > 0f) timer = duration;
            FireEvent();
            OnCounterAdded?.Invoke(delta);
            OnAfterCounterAdded?.Invoke(curCounter);
        }
        else
        {
            int amount = -delta;
            OnBeforeCounterUsed?.Invoke(amount);
            curCounter = next;
            FireEvent();
            OnCounterUsed?.Invoke(amount);
            OnAfterCounterUsed?.Invoke(curCounter);
        }
    }

    public bool Use(int amount = 1)
    {
        if (amount <= 0 || curCounter < amount) return false;
        OnBeforeCounterUsed?.Invoke(amount);
        curCounter -= amount;
        FireEvent();
        OnCounterUsed?.Invoke(amount);
        OnAfterCounterUsed?.Invoke(curCounter);
        return true;
    }

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
                Counter      = curCounter,
                MaxCounter   = maxCounter,
                IsInfinite   = false,
                ShowOnUI     = showOnUI,
                DisplayOrder = 1,
                IsRemoved    = false
            });
    }
}