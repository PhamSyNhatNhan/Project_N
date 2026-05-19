using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gắn lên entity — quản lý tất cả status effect đang active.
/// Mọi thao tác mutate (Apply / Remove / RemoveAll) đều an toàn
/// kể cả khi được gọi giữa lúc Update() đang iterate.
///
/// Caller chỉ cần truyền EffectType — Factory lo tạo/pool instance.
/// </summary>
public class StatusEffectManager : MonoBehaviour
{
    // ── Active Effects ────────────────────────────────────────────
    private Dictionary<EffectType, StatusEffect> activeEffects = new();

    // ── Deferred Command Queue ────────────────────────────────────
    private enum CmdKind { Apply, AddStack, AddMilestoneStack, Remove, RemoveAll }

    private struct DeferredCmd
    {
        public CmdKind    Kind;
        public EffectType EffectType;
        public Stat       Source;

        // Simple / Stacking shared
        public float Duration;
        public float TickInterval;

        // Stacking
        public int  MaxStacks;
        public bool RefreshOnStack;
        public bool StackResetOnExpire;

        // Milestone
        public int   Threshold;
        public float TriggerDuration;
        public float StackDuration;
        public bool  CanStackDuringTrigger;
        public bool  KeepLeftoverStacks;
        public int   StackAmount;

        // Damage override
        public List<DamageEntry> Damages;

        // Remove
        public EffectType RemoveType;
    }

    private readonly List<DeferredCmd> deferredCmds = new();
    private bool isIterating = false;

    // ── Immune ────────────────────────────────────────────────────
    private HashSet<EffectType> immuneSet = new HashSet<EffectType>();

    // ── Components ────────────────────────────────────────────────
    private Stat      stat;
    private TimeScale _timeScale;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        stat       = GetComponent<Stat>();
        _timeScale = GetComponent<TimeScale>();
    }

    private void Update()
    {
        FlushDeferred();

        if (activeEffects.Count == 0) return;

        float scaledDelta = _timeScale != null ? _timeScale.DeltaTime : Time.deltaTime;
        float realDelta   = Time.deltaTime;
        var   toRemove    = new List<EffectType>();

        isIterating = true;
        foreach (var pair in activeEffects)
        {
            float delta = pair.Value.UseTimeScale ? scaledDelta : realDelta;
            pair.Value.OnTick(delta);
            if (pair.Value.IsExpired)
                toRemove.Add(pair.Key);
        }
        isIterating = false;

        foreach (var type in toRemove)
            RemoveImmediate(type);

        FlushDeferred();
    }

    private void OnDisable()
    {
        RemoveAll();
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>Apply SimpleEffect với default config từ effect.</summary>
    public void Apply(EffectType type, Stat source)
    {
        var effect = StatusEffectFactory.Create(type);
        if (effect == null) return;
        float duration     = effect.DefaultDuration;
        float tickInterval = effect.DefaultTickInterval;
        StatusEffectFactory.Return(effect);
        Apply(type, source, duration, tickInterval);
    }

    /// <summary>AddStack với default config từ effect.</summary>
    public void AddStack(EffectType type, Stat source)
    {
        var effect = StatusEffectFactory.Create(type);
        if (effect == null) return;
        float duration           = effect.DefaultDuration;
        float tickInterval       = effect.DefaultTickInterval;
        int   maxStacks          = effect.DefaultMaxStacks;
        bool  refreshOnStack     = effect.DefaultRefreshOnStack;
        bool  stackResetOnExpire = effect.DefaultStackResetOnExpire;
        StatusEffectFactory.Return(effect);
        AddStack(type, source, duration, tickInterval, maxStacks, refreshOnStack, stackResetOnExpire);
    }

    /// <summary>AddMilestoneStack với default config từ effect.</summary>
    public void AddMilestoneStack(EffectType type, Stat source)
    {
        var effect = StatusEffectFactory.Create(type);
        if (effect == null) return;
        int   threshold             = effect.DefaultThreshold;
        float triggerDuration       = effect.DefaultTriggerDuration;
        float stackDuration         = effect.DefaultStackDuration;
        bool  canStackDuringTrigger = effect.DefaultCanStackDuringTrigger;
        bool  keepLeftoverStacks    = effect.DefaultKeepLeftoverStacks;
        StatusEffectFactory.Return(effect);
        AddMilestoneStack(type, source, threshold, triggerDuration,
                          stackDuration, canStackDuringTrigger, keepLeftoverStacks);
    }

    /// <summary>Apply SimpleEffect — Factory tạo instance, replace nếu đã có.</summary>
    public void Apply(EffectType type, Stat source, float duration, float tickInterval = 0f)
    {
        if (!CanApply(type)) return;

        if (isIterating)
        {
            deferredCmds.Add(new DeferredCmd
            {
                Kind         = CmdKind.Apply,
                EffectType   = type,
                Source       = source,
                Duration     = duration,
                TickInterval = tickInterval
            });
            return;
        }

        ApplyImmediate(type, source, duration, tickInterval);
    }

    /// <summary>Apply SimpleEffect với custom damages.</summary>
    public void Apply(EffectType type, Stat source, float duration,
                      List<DamageEntry> damages, float tickInterval = 0f)
    {
        if (!CanApply(type)) return;

        if (isIterating)
        {
            deferredCmds.Add(new DeferredCmd
            {
                Kind         = CmdKind.Apply,
                EffectType   = type,
                Source       = source,
                Duration     = duration,
                TickInterval = tickInterval,
                Damages      = damages
            });
            return;
        }

        ApplyImmediate(type, source, duration, tickInterval, damages);
    }

    /// <summary>AddStack với custom damages.</summary>
    public void AddStack(EffectType type, Stat source,
                         float duration, float tickInterval, int maxStacks,
                         List<DamageEntry> damages,
                         bool refreshOnStack = true, bool stackResetOnExpire = true)
    {
        if (!CanApply(type)) return;

        if (isIterating)
        {
            deferredCmds.Add(new DeferredCmd
            {
                Kind               = CmdKind.AddStack,
                EffectType         = type,
                Source             = source,
                Duration           = duration,
                TickInterval       = tickInterval,
                MaxStacks          = maxStacks,
                RefreshOnStack     = refreshOnStack,
                StackResetOnExpire = stackResetOnExpire,
                Damages            = damages
            });
            return;
        }

        AddStackImmediate(type, source, duration, tickInterval,
                          maxStacks, refreshOnStack, stackResetOnExpire, damages);
    }

    /// <summary>AddMilestoneStack với custom damages.</summary>
    public void AddMilestoneStack(EffectType type, Stat source,
                                  int threshold, float triggerDuration,
                                  List<DamageEntry> damages,
                                  float stackDuration        = 5f,
                                  bool canStackDuringTrigger = false,
                                  bool keepLeftoverStacks    = false,
                                  int  stackAmount           = 1)
    {
        if (!CanApply(type)) return;

        if (isIterating)
        {
            deferredCmds.Add(new DeferredCmd
            {
                Kind                  = CmdKind.AddMilestoneStack,
                EffectType            = type,
                Source                = source,
                Threshold             = threshold,
                TriggerDuration       = triggerDuration,
                StackDuration         = stackDuration,
                CanStackDuringTrigger = canStackDuringTrigger,
                KeepLeftoverStacks    = keepLeftoverStacks,
                StackAmount           = stackAmount,
                Damages               = damages
            });
            return;
        }

        AddMilestoneStackImmediate(type, source, threshold, triggerDuration,
                                   stackDuration, canStackDuringTrigger,
                                   keepLeftoverStacks, stackAmount, damages);
    }

    /// <summary>Apply StackingEffect — AddStack nếu đã có, tạo mới nếu chưa.</summary>
    public void AddStack(EffectType type, Stat source,
                         float duration, float tickInterval,
                         int maxStacks, bool refreshOnStack = true,
                         bool stackResetOnExpire = true)
    {
        if (!CanApply(type)) return;

        if (isIterating)
        {
            deferredCmds.Add(new DeferredCmd
            {
                Kind               = CmdKind.AddStack,
                EffectType         = type,
                Source             = source,
                Duration           = duration,
                TickInterval       = tickInterval,
                MaxStacks          = maxStacks,
                RefreshOnStack     = refreshOnStack,
                StackResetOnExpire = stackResetOnExpire
            });
            return;
        }

        AddStackImmediate(type, source, duration, tickInterval,
                          maxStacks, refreshOnStack, stackResetOnExpire);
    }

    /// <summary>Apply StackableEffect (milestone) — AddStack nếu đã có, tạo mới nếu chưa.</summary>
    public void AddMilestoneStack(EffectType type, Stat source,
                                  int threshold, float triggerDuration,
                                  float stackDuration        = 5f,
                                  bool canStackDuringTrigger = false,
                                  bool keepLeftoverStacks    = false,
                                  int  stackAmount           = 1)
    {
        if (!CanApply(type)) return;

        if (isIterating)
        {
            deferredCmds.Add(new DeferredCmd
            {
                Kind                  = CmdKind.AddMilestoneStack,
                EffectType            = type,
                Source                = source,
                Threshold             = threshold,
                TriggerDuration       = triggerDuration,
                StackDuration         = stackDuration,
                CanStackDuringTrigger = canStackDuringTrigger,
                KeepLeftoverStacks    = keepLeftoverStacks,
                StackAmount           = stackAmount
            });
            return;
        }

        AddMilestoneStackImmediate(type, source, threshold, triggerDuration,
                                   stackDuration, canStackDuringTrigger,
                                   keepLeftoverStacks, stackAmount);
    }

    // ── AddEffect — unified API ───────────────────────────────────

    /// <summary>AddEffect với default config — tự detect SimpleEffect / Stacking / Stackable.</summary>
    public void AddEffect(EffectType type, Stat source = null)
    {
        var effect = StatusEffectFactory.Create(type);
        if (effect == null) return;

        switch (effect)
        {
            case StackableEffect stackable:
            {
                int   threshold             = stackable.DefaultThreshold;
                float triggerDuration       = stackable.DefaultTriggerDuration;
                float stackDuration         = stackable.DefaultStackDuration;
                bool  canStackDuringTrigger = stackable.DefaultCanStackDuringTrigger;
                bool  keepLeftoverStacks    = stackable.DefaultKeepLeftoverStacks;
                StatusEffectFactory.Return(effect);
                AddMilestoneStack(type, source, threshold, triggerDuration,
                                  stackDuration, canStackDuringTrigger, keepLeftoverStacks);
                break;
            }
            case StackingEffect stacking:
            {
                float duration           = stacking.DefaultDuration;
                float tickInterval       = stacking.DefaultTickInterval;
                int   maxStacks          = stacking.DefaultMaxStacks;
                bool  refreshOnStack     = stacking.DefaultRefreshOnStack;
                bool  stackResetOnExpire = stacking.DefaultStackResetOnExpire;
                StatusEffectFactory.Return(effect);
                AddStack(type, source, duration, tickInterval,
                         maxStacks, refreshOnStack, stackResetOnExpire);
                break;
            }
            default:
            {
                float duration     = effect.DefaultDuration;
                float tickInterval = effect.DefaultTickInterval;
                StatusEffectFactory.Return(effect);
                Apply(type, source, duration, tickInterval);
                break;
            }
        }
    }

    /// <summary>AddEffect với custom damages.</summary>
    public void AddEffect(EffectType type, List<DamageEntry> damages, Stat source = null)
    {
        var effect = StatusEffectFactory.Create(type);
        if (effect == null) return;

        switch (effect)
        {
            case StackableEffect stackable:
            {
                int   threshold             = stackable.DefaultThreshold;
                float triggerDuration       = stackable.DefaultTriggerDuration;
                float stackDuration         = stackable.DefaultStackDuration;
                bool  canStackDuringTrigger = stackable.DefaultCanStackDuringTrigger;
                bool  keepLeftoverStacks    = stackable.DefaultKeepLeftoverStacks;
                StatusEffectFactory.Return(effect);
                AddMilestoneStack(type, source, threshold, triggerDuration,
                                  damages, stackDuration, canStackDuringTrigger,
                                  keepLeftoverStacks);
                break;
            }
            case StackingEffect stacking:
            {
                float duration           = stacking.DefaultDuration;
                float tickInterval       = stacking.DefaultTickInterval;
                int   maxStacks          = stacking.DefaultMaxStacks;
                bool  refreshOnStack     = stacking.DefaultRefreshOnStack;
                bool  stackResetOnExpire = stacking.DefaultStackResetOnExpire;
                StatusEffectFactory.Return(effect);
                AddStack(type, source, duration, tickInterval,
                         maxStacks, damages, refreshOnStack, stackResetOnExpire);
                break;
            }
            default:
            {
                float duration     = effect.DefaultDuration;
                float tickInterval = effect.DefaultTickInterval;
                StatusEffectFactory.Return(effect);
                Apply(type, source, duration, damages, tickInterval);
                break;
            }
        }
    }

    /// <summary>AddEffect với custom config data — effect tự xử lý qua IEffectConfigurable.</summary>
    public void AddEffect(EffectType type, object data, Stat source = null)
    {
        // Nếu đã có effect — configure để cộng dồn
        if (activeEffects.TryGetValue(type, out var existing))
        {
            if (existing is IEffectConfigurable configurable)
                configurable.Configure(data);
            return;
        }

        // Chưa có — tạo mới, configure rồi Init trực tiếp (không Return về pool)
        var effect = StatusEffectFactory.Create(type);
        if (effect == null) return;

        if (effect is IEffectConfigurable cfg)
            cfg.Configure(data);

        if (!CanApply(type)) return;

        switch (effect)
        {
            case StackableEffect stackable:
                stackable.Init(stat, source, stackable.DefaultThreshold,
                               stackable.DefaultTriggerDuration,
                               stackable.DefaultStackDuration,
                               stackable.DefaultCanStackDuringTrigger,
                               stackable.DefaultKeepLeftoverStacks);
                stackable.OnApply();
                stackable.AddStack(1);
                activeEffects[type] = stackable;
                break;

            case StackingEffect stacking:
                stacking.Init(stat, source, stacking.DefaultDuration,
                              stacking.DefaultTickInterval,
                              stacking.DefaultMaxStacks,
                              stacking.DefaultRefreshOnStack,
                              stacking.DefaultStackResetOnExpire);
                stacking.OnApply();
                activeEffects[type] = stacking;
                break;

            default:
                effect.Init(stat, source, effect.DefaultDuration,
                            effect.DefaultTickInterval);
                effect.OnApply();
                activeEffects[type] = effect;
                break;
        }
    }

    /// <summary>AddEffect với custom config data — không source.</summary>
    public void AddEffect(EffectType type, object data)
        => AddEffect(type, data, null);

    // ── No-source Overloads ───────────────────────────────────────
    public void Apply(EffectType type)
        => Apply(type, null);

    public void Apply(EffectType type, float duration, float tickInterval = 0f)
        => Apply(type, null, duration, tickInterval);

    public void Apply(EffectType type, float duration,
                      List<DamageEntry> damages, float tickInterval = 0f)
        => Apply(type, null, duration, damages, tickInterval);

    public void AddStack(EffectType type)
        => AddStack(type, null);

    public void AddStack(EffectType type, float duration, float tickInterval,
                         int maxStacks, bool refreshOnStack = true,
                         bool stackResetOnExpire = true)
        => AddStack(type, null, duration, tickInterval, maxStacks,
                    refreshOnStack, stackResetOnExpire);

    public void AddStack(EffectType type, float duration, float tickInterval,
                         int maxStacks, List<DamageEntry> damages,
                         bool refreshOnStack = true, bool stackResetOnExpire = true)
        => AddStack(type, null, duration, tickInterval, maxStacks,
                    damages, refreshOnStack, stackResetOnExpire);

    public void AddMilestoneStack(EffectType type)
        => AddMilestoneStack(type, null);

    public void AddMilestoneStack(EffectType type, int threshold, float triggerDuration,
                                  float stackDuration        = 5f,
                                  bool canStackDuringTrigger = false,
                                  bool keepLeftoverStacks    = false,
                                  int  stackAmount           = 1)
        => AddMilestoneStack(type, null, threshold, triggerDuration,
                             stackDuration, canStackDuringTrigger,
                             keepLeftoverStacks, stackAmount);

    public void AddMilestoneStack(EffectType type, int threshold, float triggerDuration,
                                  List<DamageEntry> damages,
                                  float stackDuration        = 5f,
                                  bool canStackDuringTrigger = false,
                                  bool keepLeftoverStacks    = false,
                                  int  stackAmount           = 1)
        => AddMilestoneStack(type, null, threshold, triggerDuration,
                             damages, stackDuration, canStackDuringTrigger,
                             keepLeftoverStacks, stackAmount);

    // ── Remove ────────────────────────────────────────────────────

    public void Remove(EffectType type)
    {
        if (isIterating)
        {
            deferredCmds.Add(new DeferredCmd { Kind = CmdKind.Remove, RemoveType = type });
            return;
        }
        RemoveImmediate(type);
    }

    public void RemoveAll()
    {
        if (isIterating)
        {
            deferredCmds.Add(new DeferredCmd { Kind = CmdKind.RemoveAll });
            return;
        }
        RemoveAllImmediate();
    }

    // ── Immune ────────────────────────────────────────────────────
    public void AddImmune(params EffectType[] types)
    {
        foreach (var t in types) immuneSet.Add(t);
    }

    public void RemoveImmune(params EffectType[] types)
    {
        foreach (var t in types) immuneSet.Remove(t);
    }

    public bool IsImmune(EffectType type) => immuneSet.Contains(type);

    // ── Query ─────────────────────────────────────────────────────
    public bool HasEffect(EffectType type) => activeEffects.ContainsKey(type);

    public StatusEffect GetEffect(EffectType type)
        => activeEffects.TryGetValue(type, out var e) ? e : null;

    public IReadOnlyDictionary<EffectType, StatusEffect> ActiveEffects => activeEffects;

    // ── Internal ──────────────────────────────────────────────────
    protected virtual bool CanApply(EffectType type)
    {
        if (!immuneSet.Contains(type)) return true;
        Debug.Log($"[StatusEffectManager] {gameObject.name} immune với {type}");
        return false;
    }

    // ── Immediate Implementations ─────────────────────────────────

    private void ApplyImmediate(EffectType type, Stat source,
                                float duration, float tickInterval,
                                List<DamageEntry> damages = null)
    {
        var effect = StatusEffectFactory.Create<SimpleEffect>(type);
        if (effect == null) return;

        RemoveImmediate(type);
        effect.SetDamages(damages);
        effect.Init(stat, source, duration, tickInterval);
        effect.OnApply();
        activeEffects[type] = effect;
    }

    private void AddStackImmediate(EffectType type, Stat source,
                                   float duration, float tickInterval,
                                   int maxStacks, bool refreshOnStack,
                                   bool stackResetOnExpire,
                                   List<DamageEntry> damages = null)
    {
        if (activeEffects.TryGetValue(type, out var existing)
            && existing is StackingEffect stacking)
        {
            stacking.SetDamages(damages);
            stacking.AddStack(maxStacks, duration, tickInterval,
                              refreshOnStack, stackResetOnExpire);
            return;
        }

        var effect = StatusEffectFactory.Create<StackingEffect>(type);
        if (effect == null) return;

        RemoveImmediate(type);
        effect.SetDamages(damages);
        effect.Init(stat, source, duration, tickInterval,
                    maxStacks, refreshOnStack, stackResetOnExpire);
        effect.OnApply();
        activeEffects[type] = effect;
    }

    private void AddMilestoneStackImmediate(EffectType type, Stat source,
                                            int threshold, float triggerDuration,
                                            float stackDuration,
                                            bool canStackDuringTrigger,
                                            bool keepLeftoverStacks,
                                            int stackAmount,
                                            List<DamageEntry> damages = null)
    {
        if (activeEffects.TryGetValue(type, out var existing)
            && existing is StackableEffect stackable)
        {
            stackable.SetDamages(damages);
            stackable.AddStack(stackAmount, threshold, triggerDuration, stackDuration);
            return;
        }

        var effect = StatusEffectFactory.Create<StackableEffect>(type);
        if (effect == null) return;

        RemoveImmediate(type);
        effect.SetDamages(damages);
        effect.Init(stat, source, threshold, triggerDuration,
                    stackDuration, canStackDuringTrigger, keepLeftoverStacks);
        effect.OnApply();
        effect.AddStack(stackAmount);
        activeEffects[type] = effect;
    }

    private void RemoveImmediate(EffectType type)
    {
        if (!activeEffects.TryGetValue(type, out var effect)) return;
        effect.OnRemove();
        activeEffects.Remove(type);
        StatusEffectFactory.Return(effect);
    }

    private void RemoveAllImmediate()
    {
        foreach (var effect in activeEffects.Values)
        {
            effect.OnRemove();
            StatusEffectFactory.Return(effect);
        }
        activeEffects.Clear();
    }

    // ── Flush Deferred ────────────────────────────────────────────
    private void FlushDeferred()
    {
        if (deferredCmds.Count == 0) return;

        var snapshot = new List<DeferredCmd>(deferredCmds);
        deferredCmds.Clear();

        foreach (var cmd in snapshot)
        {
            switch (cmd.Kind)
            {
                case CmdKind.Apply:
                    ApplyImmediate(cmd.EffectType, cmd.Source,
                                   cmd.Duration, cmd.TickInterval, cmd.Damages);
                    break;

                case CmdKind.AddStack:
                    AddStackImmediate(cmd.EffectType, cmd.Source,
                                      cmd.Duration, cmd.TickInterval,
                                      cmd.MaxStacks, cmd.RefreshOnStack,
                                      cmd.StackResetOnExpire, cmd.Damages);
                    break;

                case CmdKind.AddMilestoneStack:
                    AddMilestoneStackImmediate(cmd.EffectType, cmd.Source,
                                              cmd.Threshold, cmd.TriggerDuration,
                                              cmd.StackDuration,
                                              cmd.CanStackDuringTrigger,
                                              cmd.KeepLeftoverStacks,
                                              cmd.StackAmount, cmd.Damages);
                    break;

                case CmdKind.Remove:
                    RemoveImmediate(cmd.RemoveType);
                    break;

                case CmdKind.RemoveAll:
                    RemoveAllImmediate();
                    break;
            }
        }
    }
}