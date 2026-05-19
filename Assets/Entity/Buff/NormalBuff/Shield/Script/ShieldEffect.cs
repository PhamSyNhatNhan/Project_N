using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shield — SimpleEffect
/// Hấp thụ damage trước khi trừ vào HP.
/// UseTimeScale = false — không bị ảnh hưởng bởi time stop.
/// Implement IEffectConfigurable để nhận ShieldData từ AddEffect.
/// </summary>
public class ShieldEffect : SimpleEffect, IEffectConfigurable
{
    public override EffectType     Type        => EffectType.Shield;
    public override EffectCategory Category    => EffectCategory.Buff;
    public override string         DisplayName => "Shield";
    public override List<string>   IconPaths   => new List<string> { "Effects/Shield/Shield" };

    // ── Default Config ────────────────────────────────────────────
    public override float DefaultDuration     => 10f;
    public override bool  UseTimeScale        => false;
    public virtual  float DefaultShieldAmount => 1000f;

    private float _shieldAmount;
    private float _curShield;
    private float _pendingDuration = -1f;

    // ── IEffectConfigurable ───────────────────────────────────────
    public void Configure(object data)
    {
        if (data is not ShieldData shieldData) return;

        if (target != null)
        {
            // Đã active — cộng dồn shield, lấy duration mới
            _shieldAmount += shieldData.Amount;
            _curShield    += shieldData.Amount;
            TimeLeft       = shieldData.Duration;
            FireEvent();
        }
        else
        {
            // Chưa Init — lưu tạm để Init dùng
            _shieldAmount    = shieldData.Amount;
            _curShield       = shieldData.Amount;
            _pendingDuration = shieldData.Duration;
        }
    }

    // ── Init ──────────────────────────────────────────────────────
    public override void Init(Stat target, Stat source, float duration, float tickInterval = 0f)
    {
        // Nếu Configure đã set duration thì dùng, không thì dùng param
        float finalDuration = _pendingDuration > 0f ? _pendingDuration : duration;
        base.Init(target, source, finalDuration, tickInterval);

        if (_shieldAmount <= 0f)
        {
            _shieldAmount = DefaultShieldAmount;
            _curShield    = _shieldAmount;
        }

        _pendingDuration = -1f;
    }

    // ── Reapply — cộng dồn shield, lấy duration mới ──────────────
    public void Reapply(float shieldAmount, float duration)
    {
        _shieldAmount += shieldAmount;
        _curShield    += shieldAmount;
        TimeLeft       = duration;
        FireEvent();
    }

    // ── BuildDisplayData ──────────────────────────────────────────
    protected override EffectDisplayData BuildDisplayData(bool isRemoved) => new EffectDisplayData
    {
        Type        = Type,
        Category    = Category,
        DisplayName = DisplayName,
        Icon        = icons.Count > 0 ? icons[0] : null,
        Duration    = Duration,
        CurStacks   = 0,
        MaxStacks   = 0,
        Value       = _curShield,
        IsRemoved   = isRemoved
    };

    public override void OnApply()
    {
        target.OnBeforeTakeDamage += AbsorbDamage;
        base.OnApply();
    }

    public override void OnRemove()
    {
        target.OnBeforeTakeDamage -= AbsorbDamage;
        base.OnRemove();
    }

    private float AbsorbDamage(float damage, DamageType type)
    {
        if (_curShield <= 0f) return damage;

        float absorbed  = Mathf.Min(_curShield, damage);
        _curShield     -= absorbed;
        float remaining = damage - absorbed;

        if (_curShield <= 0f)
            target.GetComponent<StatusEffectManager>()?.Remove(Type);

        return remaining;
    }

    public override void Reset()
    {
        base.Reset();
        _shieldAmount    = 0f;
        _curShield       = 0f;
        _pendingDuration = -1f;
    }
}