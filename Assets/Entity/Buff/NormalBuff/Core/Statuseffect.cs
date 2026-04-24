using System.Collections.Generic;
using UnityEngine;

// ── StatusEffect base ─────────────────────────────────────────────────
public abstract class StatusEffect
{
    // ── Identity ──────────────────────────────────────────────────
    public abstract EffectType     Type        { get; }
    public abstract EffectCategory Category    { get; }
    public abstract string         DisplayName { get; }

    // Path trong Resources — con khai báo, base tự load
    public abstract List<string> IconPaths { get; }

    // ── Runtime ───────────────────────────────────────────────────
    protected Stat      target;
    protected Stat      source;
    protected TimeScale timeScale;
    protected string    entityKey;

    protected List<Sprite> icons = new List<Sprite>();

    protected float tickInterval;
    private   float tickTimer;

    public float        Duration  { get; protected set; }
    public float        TimeLeft  { get; protected set; }
    public List<Sprite> Icons     => icons;
    public virtual bool IsExpired => TimeLeft <= 0f;

    // ── Init ──────────────────────────────────────────────────────
    public virtual void Init(Stat target, Stat source, float duration, float tickInterval = 0f)
    {
        this.target       = target;
        this.source       = source;
        this.timeScale    = target.GetComponent<TimeScale>();
        this.entityKey    = $"{target.NameCharacter}_{target.GetInstanceID()}";
        this.tickInterval = tickInterval;
        Duration          = duration;
        TimeLeft          = duration;
        tickTimer         = 0f;

        LoadIcons();
    }

    // ── Lifecycle ─────────────────────────────────────────────────
    public virtual void OnApply()  { }
    public virtual void OnRemove() { }

    public virtual void OnTick(float deltaTime)
    {
        TimeLeft -= deltaTime;

        if (tickInterval <= 0f) return;
        tickTimer += deltaTime;
        if (tickTimer < tickInterval) return;
        tickTimer -= tickInterval;
        OnInterval();
    }

    protected virtual void OnInterval() { }

    // ── Event ─────────────────────────────────────────────────────
    protected void FireEvent(bool isRemoved = false)
    {
        EventManager.Entity.OnEntityEffectChanged
            .Get(entityKey)
            .Invoke(null, BuildDisplayData(isRemoved));
    }

    protected virtual EffectDisplayData BuildDisplayData(bool isRemoved) => new EffectDisplayData
    {
        Type        = Type,
        Category    = Category,
        DisplayName = DisplayName,
        Icon        = icons.Count > 0 ? icons[0] : null,
        Duration    = Duration,
        CurStacks   = 0,
        MaxStacks   = 0,
        IsRemoved   = isRemoved
    };

    // ── Reset ─────────────────────────────────────────────────────
    public virtual void Reset()
    {
        target       = null;
        source       = null;
        timeScale    = null;
        entityKey    = null;
        Duration     = 0f;
        TimeLeft     = 0f;
        tickTimer    = 0f;
        tickInterval = 0f;
        icons.Clear();
    }

    // ── Internal ──────────────────────────────────────────────────
    private void LoadIcons()
    {
        icons.Clear();
        if (IconPaths == null) return;
        foreach (var path in IconPaths)
        {
            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
                Debug.LogWarning($"[StatusEffect] Không tìm thấy icon: '{path}'");
            else
                icons.Add(sprite);
        }
    }

    protected float DeltaTime => Time.deltaTime;
}