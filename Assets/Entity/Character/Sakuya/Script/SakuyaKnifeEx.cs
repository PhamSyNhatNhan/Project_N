using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// KnifeEx — dao của Burst spiral.
/// Khi đang spiral: không gây damage, không hồi PWS.
/// Khi release (homing): gây damage bình thường, không hồi PWS.
/// Clock2 active → thêm 250 damage vào mỗi đòn.
/// </summary>
public class SakuyaKnifeEx : SakuyaKnife
{
    private bool  isSpiral      = true;
    private float clock2Bonus   = 0f;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void OnEnable()
    {
        base.OnEnable();
        isSpiral = true;
    }

    // ── Trigger — không gây damage khi đang spiral ────────────────
    protected override void OnTriggerEnter2D(Collider2D other)
    {
        if (isSpiral) return;
        base.OnTriggerEnter2D(other);
    }

    // ── Override SendDamage — không hồi PWS, thêm Clock2 bonus ───
    // PWS = 0 được set qua SetUp từ SakuyaSkill

    // ── API ───────────────────────────────────────────────────────
    public void SetSpiral(bool spiral) => isSpiral = spiral;

    public void SetClock2Bonus(float bonus) => clock2Bonus = bonus;

    public void ReleaseSpiral()
    {
        isSpiral = false;
        // Thêm clock2 bonus vào damage
        if (clock2Bonus > 0f && damage != null && damage.Count > 0)
            damage[0] += clock2Bonus;

        EasyModeChange(BulletMoveMode.Homing);
    }

    public bool IsSpiral => isSpiral;
}