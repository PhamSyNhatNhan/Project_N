using UnityEngine;

/// <summary>
/// Đăng ký toàn bộ StatusEffect vào Factory — gắn lên GameManager.
/// Chạy trong Awake trước mọi component khác (Script Execution Order).
/// Thêm effect mới: Register thêm 1 dòng ở đây.
/// </summary>
public class StatusEffectInitializer : MonoBehaviour
{
    private void Awake()
    {
        RegisterAll();
    }

    private static void RegisterAll()
    {
        // ── SimpleEffect ───────────────────────────────────────────
        // EffectType.Stun — chưa triển khai
        StatusEffectFactory.Register(EffectType.Slow,     () => new SlowEffect());
        StatusEffectFactory.Register(EffectType.Shield,   () => new ShieldEffect());
        StatusEffectFactory.Register(EffectType.Immortal, () => new ImmortalEffect());

        // ── StackingEffect ─────────────────────────────────────────
        StatusEffectFactory.Register(EffectType.Burn,     () => new BurnEffect());
        StatusEffectFactory.Register(EffectType.Poison,   () => new PoisonEffect());
        StatusEffectFactory.Register(EffectType.Bleed,    () => new BleedEffect());

        // ── StackableEffect (Milestone) ────────────────────────────
        StatusEffectFactory.Register(EffectType.Freeze,   () => new FreezeEffect());
        StatusEffectFactory.Register(EffectType.Shock,    () => new ShockEffect());

        // Thêm effect mới tại đây:
        // StatusEffectFactory.Register(EffectType.XXX, () => new XXXEffect());
    }
}