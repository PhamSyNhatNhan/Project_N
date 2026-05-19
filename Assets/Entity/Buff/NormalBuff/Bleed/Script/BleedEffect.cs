using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bleed — StackingEffect
/// Damage true mỗi tick theo stack.
/// Hết duration → trừ 10 stack, refresh nếu còn stack.
/// Dưới 10 stack → về 0, IsExpired = true → Manager remove.
/// </summary>
public class BleedEffect : StackingEffect
{
    public override EffectType     Type        => EffectType.Bleed;
    public override EffectCategory Category    => EffectCategory.Debuff;
    public override string         DisplayName => "Bleed";
    public override List<string>   IconPaths   => new List<string> { "Effects/Bleed/Bleed" };

    // ── Default Config ────────────────────────────────────────────
    public override float DefaultDuration     => 5f;
    public override float DefaultTickInterval => 0.7f;
    public override int   DefaultMaxStacks    => 100;

    // ── Default Damages ───────────────────────────────────────────
    public override List<DamageEntry> DefaultDamages => new List<DamageEntry>
    {
        new DamageEntry(DamageType.True, 10f)
    };

    private const int StackDecayAmount = 10;
    private bool _forceExpire = false;

    // ── IsExpired — chỉ true khi stack về 0 ──────────────────────
    public override bool IsExpired => _forceExpire;

    // ── OnTick — override để xử lý decay khi hết duration ────────
    public override void OnTick(float deltaTime)
    {
        TimeLeft -= deltaTime;

        // Tick damage
        if (tickInterval > 0f)
        {
            tickTimerBleed += deltaTime;
            if (tickTimerBleed >= tickInterval)
            {
                tickTimerBleed -= tickInterval;
                OnInterval();
            }
        }

        // Hết duration → decay stack
        if (TimeLeft <= 0f)
            DecayStacks();
    }

    private float tickTimerBleed = 0f;

    private void DecayStacks()
    {
        curStacks -= StackDecayAmount;

        if (curStacks <= 0)
        {
            curStacks    = 0;
            _forceExpire = true;
            FireEvent(isRemoved: true);
            return;
        }

        // Còn stack — refresh duration
        TimeLeft = Duration;
        FireEvent();
    }

    // ── OnInterval ────────────────────────────────────────────────
    protected override void OnInterval()
    {
        if (target == null) return;

        var damages = GetDamages();
        foreach (var entry in damages)
            target.TakeDamage(entry.Type, entry.Amount * curStacks, 0f, 0f);
    }

    protected override void OnApplyValue()  { }
    protected override void OnRemoveValue() { }

    public override void OnApply() => base.OnApply();

    public override void OnRemove()
    {
        FireEvent(isRemoved: true);
    }

    public override void Reset()
    {
        base.Reset();
        _forceExpire   = false;
        tickTimerBleed = 0f;
    }
}