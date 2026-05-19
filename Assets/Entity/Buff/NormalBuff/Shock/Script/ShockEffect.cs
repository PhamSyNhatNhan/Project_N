using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shock — StackableEffect
/// Tích đủ 5 stack → burst damage AOE xung quanh target.
/// Implement IEffectConfigurable để nhận ShockData từ AddEffect.
/// </summary>
public class ShockEffect : StackableEffect, IEffectConfigurable
{
    public override EffectType     Type        => EffectType.Shock;
    public override EffectCategory Category    => EffectCategory.Debuff;
    public override string         DisplayName => "Shock";
    public override List<string>   IconPaths   => new List<string> { "Effects/Shock/Shock" };

    // ── Default Config ────────────────────────────────────────────
    public override int   DefaultThreshold       => 5;
    public override float DefaultTriggerDuration => 1f;
    public override float DefaultStackDuration   => 5f;

    // ── Default Damages ───────────────────────────────────────────
    public override List<DamageEntry> DefaultDamages => new List<DamageEntry>
    {
        new DamageEntry(DamageType.Magic, 300f)
    };

    private float     _burstRadius  = 3f;
    private LayerMask _damageLayer;

    // ── IEffectConfigurable ───────────────────────────────────────
    public void Configure(object data)
    {
        if (data is not ShockData shockData) return;
        _burstRadius = shockData.BurstRadius;
        _damageLayer = shockData.DamageLayer;
        if (shockData.Damages != null)
            SetDamages(shockData.Damages);
    }

    // ── Threshold Met — AOE burst ─────────────────────────────────
    protected override void OnThresholdMet()
    {
        var damages = GetDamages();
        var hits    = Physics2D.OverlapCircleAll(target.transform.position,
                                                 _burstRadius, _damageLayer);
        foreach (var hit in hits)
        {
            var stat = hit.GetComponent<Stat>();
            if (stat == null) continue;

            foreach (var entry in damages)
                stat.TakeDamage(entry.Type, entry.Amount, 0f, 0f);
        }
    }

    protected override void OnTriggerEnd() { }

    public override void OnApply()  => base.OnApply();
    public override void OnRemove() => base.OnRemove();

    public override void Reset()
    {
        base.Reset();
        _burstRadius = 3f;
        _damageLayer = default;
    }
}