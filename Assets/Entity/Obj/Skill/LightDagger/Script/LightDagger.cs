using System.Collections.Generic;
using UnityEngine;

public class LightDagger : BulletObject
{
    // ── State ─────────────────────────────────────────────────────
    private bool   isActivated = false;
    private Hitbox hitbox;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        hitbox   = GetComponent<Hitbox>();
        moveMode = BulletMoveMode.Custom;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        isActivated = false;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        isActivated = false;
    }

    // ── Movement — đứng yên cho đến khi Activate ─────────────────
    protected override void CustomMovement()
    {
        if (!isActivated)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        rb.linearVelocity = MoveDir * speed * FixedDeltaTime;
    }

    // ── Damage — chỉ gây damage sau khi Activate ─────────────────
    public override void SendDamage()
    {
        if (!isActivated || hitbox == null) return;

        var enemies = hitbox.detectObject(EnableDamage);
        if (enemies == null || enemies.Count == 0) return;

        foreach (var enemy in enemies)
        {
            var s = enemy.GetComponent<Stat>();
            if (s == null) continue;
            s.TakeDamage(type, damage[0], critRate, critDamage);
        }
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        SendDamage();
    }

    // ── Public API ────────────────────────────────────────────────
    /// <summary>Kích hoạt di chuyển và gây damage</summary>
    public void Activate()
    {
        isActivated = true;
    }

    /// <summary>Setup hướng bay trước khi Activate</summary>
    public void SetDirection(float angleDeg)
    {
        EasyModeChange(BulletMoveMode.Angle, angleDeg);
        moveMode = BulletMoveMode.Custom; // giữ custom sau khi set direction
    }
}