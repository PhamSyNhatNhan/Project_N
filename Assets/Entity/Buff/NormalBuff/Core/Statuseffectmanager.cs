using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gắn lên entity — quản lý tất cả status effect đang active.
/// Mọi thao tác mutate (Apply / Remove / RemoveAll) đều an toàn
/// kể cả khi được gọi giữa lúc Update() đang iterate.
/// </summary>
public class StatusEffectManager : MonoBehaviour
{
    // ── Active Effects ────────────────────────────────────────────
    private Dictionary<EffectType, StatusEffect> activeEffects = new();

    // ── Deferred Command Queue ────────────────────────────────────
    private enum CmdKind { Apply, AddStack, AddMilestoneStack, Remove, RemoveAll }

    private struct DeferredCmd
    {
        public CmdKind Kind;

        // shared
        public StatusEffect Effect;
        public Stat Source;
        public float Duration;
        public float TickInterval;

        // stacking
        public int MaxStacks;
        public bool RefreshOnStack;
        public bool StackResetOnExpire;

        // milestone
        public int Threshold;
        public float TriggerDuration;
        public float StackDuration;
        public bool CanStackDuringTrigger;
        public bool KeepLeftoverStacks;
        public int StackAmount;

        // remove
        public EffectType RemoveType;
    }

    private readonly List<DeferredCmd> deferredCmds = new();
    private bool isIterating = false;

    // ── Immune ────────────────────────────────────────────────────
    private HashSet<EffectType> immuneSet = new HashSet<EffectType>();

    // ── Components ────────────────────────────────────────────────
    private Stat stat;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        stat = GetComponent<Stat>();
    }

    private void Update()
    {
        // 1. Flush bất kỳ deferred command nào được queue từ frame trước
        FlushDeferred();

        if (activeEffects.Count == 0) return;

        // 2. Tick — đánh dấu đang iterate
        var toRemove = new List<EffectType>();

        isIterating = true;
        foreach (var pair in activeEffects)
        {
            pair.Value.OnTick(Time.deltaTime);
            if (pair.Value.IsExpired)
                toRemove.Add(pair.Key);
        }
        isIterating = false;

        // 3. Remove expired
        foreach (var type in toRemove)
            RemoveImmediate(type);

        // 4. Flush lại — phòng trường hợp OnTick / OnRemove queue thêm command
        FlushDeferred();
    }

    private void OnDisable()
    {
        RemoveAll();
    }

    // ── Apply ─────────────────────────────────────────────────────

    /// <summary>Apply SimpleEffect — replace nếu đã có</summary>
    public void Apply(SimpleEffect effect, Stat source, float duration, float tickInterval = 0f)
    {
        if (!CanApply(effect.Type)) return;

        if (isIterating)
        {
            deferredCmds.Add(new DeferredCmd
            {
                Kind         = CmdKind.Apply,
                Effect       = effect,
                Source       = source,
                Duration     = duration,
                TickInterval = tickInterval
            });
            return;
        }

        ApplyImmediate(effect, source, duration, tickInterval);
    }

    /// <summary>Apply StackingEffect — AddStack nếu đã có, tạo mới nếu chưa</summary>
    public void AddStack(StackingEffect effect, Stat source, float duration,
                         float tickInterval, int maxStacks,
                         bool refreshOnStack = true, bool stackResetOnExpire = true)
    {
        if (!CanApply(effect.Type)) return;

        if (isIterating)
        {
            deferredCmds.Add(new DeferredCmd
            {
                Kind              = CmdKind.AddStack,
                Effect            = effect,
                Source            = source,
                Duration          = duration,
                TickInterval      = tickInterval,
                MaxStacks         = maxStacks,
                RefreshOnStack    = refreshOnStack,
                StackResetOnExpire = stackResetOnExpire
            });
            return;
        }

        AddStackImmediate(effect, source, duration, tickInterval,
                          maxStacks, refreshOnStack, stackResetOnExpire);
    }

    /// <summary>Apply StackableEffect — AddStack nếu đã có, tạo mới nếu chưa</summary>
    public void AddMilestoneStack(StackableEffect effect, Stat source,
                                  int threshold, float triggerDuration,
                                  float stackDuration        = 5f,
                                  bool canStackDuringTrigger = false,
                                  bool keepLeftoverStacks    = false,
                                  int  stackAmount           = 1)
    {
        if (!CanApply(effect.Type)) return;

        if (isIterating)
        {
            deferredCmds.Add(new DeferredCmd
            {
                Kind                  = CmdKind.AddMilestoneStack,
                Effect                = effect,
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

        AddMilestoneStackImmediate(effect, source, threshold, triggerDuration,
                                   stackDuration, canStackDuringTrigger,
                                   keepLeftoverStacks, stackAmount);
    }

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

    // ── Internal — CanApply ───────────────────────────────────────
    protected virtual bool CanApply(EffectType type)
    {
        if (!immuneSet.Contains(type)) return true;
        Debug.Log($"[StatusEffectManager] {gameObject.name} immune với {type}");
        return false;
    }

    // ── Immediate Implementations (chỉ gọi khi KHÔNG iterate) ────

    private void ApplyImmediate(SimpleEffect effect, Stat source,
                                float duration, float tickInterval)
    {
        RemoveImmediate(effect.Type);
        effect.Init(stat, source, duration, tickInterval);
        effect.OnApply();
        activeEffects[effect.Type] = effect;
        FireChangedEvent();
    }

    private void AddStackImmediate(StackingEffect effect, Stat source,
                                   float duration, float tickInterval,
                                   int maxStacks, bool refreshOnStack,
                                   bool stackResetOnExpire)
    {
        if (activeEffects.TryGetValue(effect.Type, out var existing)
            && existing is StackingEffect stacking)
        {
            stacking.AddStack();
        }
        else
        {
            RemoveImmediate(effect.Type);
            effect.Init(stat, source, duration, tickInterval,
                        maxStacks, refreshOnStack, stackResetOnExpire);
            effect.OnApply();
            activeEffects[effect.Type] = effect;
        }

        FireChangedEvent();
    }

    private void AddMilestoneStackImmediate(StackableEffect effect, Stat source,
                                            int threshold, float triggerDuration,
                                            float stackDuration,
                                            bool canStackDuringTrigger,
                                            bool keepLeftoverStacks,
                                            int stackAmount)
    {
        if (activeEffects.TryGetValue(effect.Type, out var existing)
            && existing is StackableEffect stackable)
        {
            stackable.AddStack(stackAmount);
        }
        else
        {
            RemoveImmediate(effect.Type);
            effect.Init(stat, source, threshold, triggerDuration,
                        stackDuration, canStackDuringTrigger, keepLeftoverStacks);
            effect.OnApply();
            effect.AddStack(stackAmount);
            activeEffects[effect.Type] = effect;
        }

        FireChangedEvent();
    }

    private void RemoveImmediate(EffectType type)
    {
        if (!activeEffects.TryGetValue(type, out var effect)) return;
        effect.OnRemove();
        effect.Reset();
        activeEffects.Remove(type);
        FireChangedEvent();
    }

    private void RemoveAllImmediate()
    {
        foreach (var effect in activeEffects.Values)
        {
            effect.OnRemove();
            effect.Reset();
        }
        activeEffects.Clear();
        FireChangedEvent();
    }

    // ── Flush Deferred ────────────────────────────────────────────

    private void FlushDeferred()
    {
        if (deferredCmds.Count == 0) return;

        // Copy ra để tránh re-entrant flush
        var snapshot = new List<DeferredCmd>(deferredCmds);
        deferredCmds.Clear();

        foreach (var cmd in snapshot)
        {
            switch (cmd.Kind)
            {
                case CmdKind.Apply:
                    if (cmd.Effect is SimpleEffect simple)
                        ApplyImmediate(simple, cmd.Source, cmd.Duration, cmd.TickInterval);
                    break;

                case CmdKind.AddStack:
                    if (cmd.Effect is StackingEffect stacking)
                        AddStackImmediate(stacking, cmd.Source, cmd.Duration,
                                          cmd.TickInterval, cmd.MaxStacks,
                                          cmd.RefreshOnStack, cmd.StackResetOnExpire);
                    break;

                case CmdKind.AddMilestoneStack:
                    if (cmd.Effect is StackableEffect stackable)
                        AddMilestoneStackImmediate(stackable, cmd.Source,
                                                   cmd.Threshold, cmd.TriggerDuration,
                                                   cmd.StackDuration,
                                                   cmd.CanStackDuringTrigger,
                                                   cmd.KeepLeftoverStacks,
                                                   cmd.StackAmount);
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

    // ── Event ─────────────────────────────────────────────────────

    private void FireChangedEvent()
    {
        // TODO: fire khi UI system sẵn sàng
        // EventManager.Entity.OnEntityEffectChanged.Get(entityKey).Invoke(this, activeEffects);
    }
}