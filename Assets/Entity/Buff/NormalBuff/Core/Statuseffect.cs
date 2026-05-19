using System.Collections.Generic;
using UnityEngine;

// ── StatusEffect base ─────────────────────────────────────────────────
public abstract class StatusEffect
{
    // ── Identity ──────────────────────────────────────────────────
    public abstract EffectType     Type        { get; }
    public abstract EffectCategory Category    { get; }
    public abstract string         DisplayName { get; }
    public abstract List<string>   IconPaths   { get; }

    // ── TimeScale ─────────────────────────────────────────────────
    /// <summary>
    /// true  = duration bị ảnh hưởng bởi TimeScale của entity (default).
    /// false = duration tính theo real time — Immortal, Shield.
    /// </summary>
    public virtual bool UseTimeScale => true;

    // ── Default Config — con override để thay đổi balance ────────
    public virtual float DefaultDuration              => 3f;
    public virtual float DefaultTickInterval          => 1f;
    public virtual int   DefaultMaxStacks             => 5;
    public virtual bool  DefaultRefreshOnStack        => true;
    public virtual bool  DefaultStackResetOnExpire    => true;
    public virtual int   DefaultThreshold             => 4;
    public virtual float DefaultTriggerDuration       => 2f;
    public virtual float DefaultStackDuration         => 5f;
    public virtual bool  DefaultCanStackDuringTrigger => false;
    public virtual bool  DefaultKeepLeftoverStacks    => false;

    // ── Damage ────────────────────────────────────────────────────
    /// <summary>
    /// Damage mặc định — con override để khai báo giá trị riêng.
    /// Không thể override từ ngoài — dùng SetDamages() để thay thế.
    /// </summary>
    public virtual List<DamageEntry> DefaultDamages => new List<DamageEntry>();

    private List<DamageEntry> _customDamages;
    private bool              _useCustomDamage = false;

    /// <summary>
    /// Set damage từ ngoài — override DefaultDamages.
    /// Truyền null để reset về DefaultDamages.
    /// </summary>
    public void SetDamages(List<DamageEntry> damages)
    {
        if (damages == null)
        {
            _customDamages   = null;
            _useCustomDamage = false;
        }
        else
        {
            _customDamages   = damages;
            _useCustomDamage = true;
        }
    }

    /// <summary>Lấy damage list — custom nếu đã set, ngược lại dùng default.</summary>
    protected List<DamageEntry> GetDamages()
        => _useCustomDamage ? _customDamages : DefaultDamages;

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
        target           = null;
        source           = null;
        timeScale        = null;
        entityKey        = null;
        Duration         = 0f;
        TimeLeft         = 0f;
        tickTimer        = 0f;
        tickInterval     = 0f;
        _customDamages   = null;
        _useCustomDamage = false;
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