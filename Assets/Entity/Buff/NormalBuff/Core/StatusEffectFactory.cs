using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Factory + Object Pool cho StatusEffect.
/// - Register: đăng ký 1 lần khi khởi động (StatusEffectInitializer).
/// - Create:   lấy instance từ pool hoặc tạo mới.
/// - Return:   trả instance về pool sau khi Reset().
/// </summary>
public static class StatusEffectFactory
{
    // ── Registry: EffectType → constructor ───────────────────────
    private static readonly Dictionary<EffectType, Func<StatusEffect>> registry
        = new Dictionary<EffectType, Func<StatusEffect>>();

    // ── Pool: EffectType → stack of idle instances ────────────────
    private static readonly Dictionary<EffectType, Stack<StatusEffect>> pool
        = new Dictionary<EffectType, Stack<StatusEffect>>();

    // ── Register ──────────────────────────────────────────────────

    /// <summary>
    /// Đăng ký factory function cho một EffectType.
    /// Gọi từ StatusEffectInitializer khi game khởi động.
    /// </summary>
    public static void Register(EffectType type, Func<StatusEffect> constructor)
    {
        if (constructor == null)
        {
            Debug.LogError($"[StatusEffectFactory] Constructor null cho {type}.");
            return;
        }

        if (registry.ContainsKey(type))
            Debug.LogWarning($"[StatusEffectFactory] Ghi đè đăng ký cho {type}.");

        registry[type] = constructor;
    }

    // ── Create ────────────────────────────────────────────────────

    /// <summary>
    /// Lấy instance từ pool nếu có, ngược lại tạo mới.
    /// Trả về null nếu type chưa được Register.
    /// </summary>
    public static StatusEffect Create(EffectType type)
    {
        // Thử lấy từ pool trước
        if (pool.TryGetValue(type, out var stack) && stack.Count > 0)
            return stack.Pop();

        // Tạo mới
        if (registry.TryGetValue(type, out var constructor))
            return constructor();

        Debug.LogError($"[StatusEffectFactory] EffectType '{type}' chưa được Register.");
        return null;
    }

    /// <summary>
    /// Lấy instance đúng kiểu T từ pool hoặc tạo mới.
    /// Tiện dụng khi caller cần ép kiểu ngay (StackingEffect, StackableEffect).
    /// </summary>
    public static T Create<T>(EffectType type) where T : StatusEffect
    {
        var effect = Create(type);
        if (effect is T typed) return typed;

        if (effect != null)
            Debug.LogError($"[StatusEffectFactory] '{type}' không phải kiểu {typeof(T).Name}.");

        return null;
    }

    // ── Return (Pool) ─────────────────────────────────────────────

    /// <summary>
    /// Trả instance về pool sau khi đã gọi Reset().
    /// StatusEffectManager gọi hàm này thay vì để GC thu.
    /// </summary>
    public static void Return(StatusEffect effect)
    {
        if (effect == null) return;

        EffectType type = effect.Type;

        if (!registry.ContainsKey(type))
        {
            Debug.LogWarning($"[StatusEffectFactory] Return type '{type}' chưa Register — bỏ qua.");
            return;
        }

        if (!pool.ContainsKey(type))
            pool[type] = new Stack<StatusEffect>();

        effect.Reset();
        pool[type].Push(effect);
    }

    // ── Debug ─────────────────────────────────────────────────────

    /// <summary>Số instance đang idle trong pool của một type.</summary>
    public static int PoolCount(EffectType type)
        => pool.TryGetValue(type, out var stack) ? stack.Count : 0;

    /// <summary>Xóa toàn bộ pool (dùng khi load scene mới).</summary>
    public static void ClearPool()
    {
        pool.Clear();
    }

    /// <summary>Kiểm tra type đã được Register chưa.</summary>
    public static bool IsRegistered(EffectType type) => registry.ContainsKey(type);
}