using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Poison — StackingEffect
/// Damage = baseDamage * (1 + tierBonus%) * curStacks mỗi tick.
/// Hết duration → chia đôi stack (làm tròn xuống), không mất hết.
/// </summary>
public class PoisonEffect : StackingEffect
{
    public override EffectType     Type        => EffectType.Poison;
    public override EffectCategory Category    => EffectCategory.Debuff;
    public override string         DisplayName => "Poison";
    public override List<string>   IconPaths   => new List<string> { "Effects/Poison/Poison" };

    // ── Default Config ────────────────────────────────────────────
    public override float DefaultDuration     => 5f;
    public override float DefaultTickInterval => 0.5f;
    public override int   DefaultMaxStacks    => 70;

    // ── Default Damages ───────────────────────────────────────────
    public override List<DamageEntry> DefaultDamages => new List<DamageEntry>
    {
        new DamageEntry(DamageType.True, 25f)
    };

    // ── Tier bonus % theo stack cao nhất ─────────────────────────
    // Mỗi 10 stack tăng tier: 10/20/35/45/65/75/150
    private static readonly float[] TierBonus = { 10f, 20f, 35f, 45f, 65f, 75f, 150f };

    private float GetTierBonus()
    {
        int tier = Mathf.Min((curStacks - 1) / 10, TierBonus.Length - 1);
        return TierBonus[tier] / 100f;
    }

    // ── IsExpired ─────────────────────────────────────────────────
    public override bool IsExpired => _forceExpire;
    private bool _forceExpire = false;

    // ── OnTick — override để xử lý half decay khi hết duration ───
    private float _tickTimer = 0f;

    public override void OnTick(float deltaTime)
    {
        TimeLeft -= deltaTime;

        if (tickInterval > 0f)
        {
            _tickTimer += deltaTime;
            if (_tickTimer >= tickInterval)
            {
                _tickTimer -= tickInterval;
                OnInterval();
            }
        }

        if (TimeLeft <= 0f)
            HalfDecayStacks();
    }

    private void HalfDecayStacks()
    {
        curStacks = Mathf.FloorToInt(curStacks / 2f);

        if (curStacks <= 0)
        {
            curStacks    = 0;
            _forceExpire = true;
            FireEvent(isRemoved: true);
            return;
        }

        TimeLeft = Duration;
        FireEvent();
    }

    // ── OnInterval ────────────────────────────────────────────────
    protected override void OnInterval()
    {
        if (target == null || curStacks <= 0) return;

        float bonus   = GetTierBonus();
        var   damages = GetDamages();

        foreach (var entry in damages)
        {
            float total = entry.Amount * (1f + bonus) * curStacks;
            target.TakeDamage(entry.Type, total, 0f, 0f);
        }
    }

    protected override void OnApplyValue()  { }
    protected override void OnRemoveValue() { }

    public override void OnApply()  => base.OnApply();

    public override void OnRemove()
    {
        FireEvent(isRemoved: true);
    }

    public override void Reset()
    {
        base.Reset();
        _forceExpire = false;
        _tickTimer   = 0f;
    }
}