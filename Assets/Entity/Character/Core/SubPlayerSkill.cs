using UnityEngine;

/// <summary>
/// Mở rộng PlayerSkill với cơ chế inputDelay và EasyFlipLock.
/// Sau khi Use() một skill, inputDelayTimer đếm ngược.
/// IsReady() check thêm IsInputReady.
/// Khi hết delay, OnInputDelayEnd() tự reset tất cả flag về false.
/// EasyFlipLock(duration, target) — lock flip, tự nhả sau duration, gọi lại reset timer.
/// </summary>
public class SubPlayerSkill : PlayerSkill
{
    // ── Input Delay ───────────────────────────────────────────────
    private float inputDelayTimer = 0f;

    // ── Flip Lock ─────────────────────────────────────────────────
    private float     flipLockTimer  = 0f;
    private Transform flipLockTarget = null;

    public bool IsInputReady => inputDelayTimer <= 0f;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Update()
    {
        base.Update();
        TickInputDelay();
        TickFlipLock();
    }

    // ── Input Delay ───────────────────────────────────────────────
    private void TickInputDelay()
    {
        if (inputDelayTimer <= 0f) return;

        inputDelayTimer -= Time.deltaTime;
        if (inputDelayTimer > 0f) return;

        inputDelayTimer = 0f;
        OnInputDelayEnd();
    }

    /// <summary>
    /// Gọi khi inputDelay kết thúc.
    /// Reset tất cả flag về false — subclass override để thêm logic riêng.
    /// </summary>
    protected virtual void OnInputDelayEnd()
    {
        isAttack = false;
        isSkill  = false;
        isUlti   = false;
        isDash   = false;
        isBurst  = false;
        canInput = true;

        var controller = GetComponent<PlayerController>();
        if (controller != null)
        {
            controller.CanInput = true;
            controller.CanFlip  = true;
        }
    }

    /// <summary>Set inputDelay — lấy giá trị lớn hơn để không bị override bởi delay nhỏ hơn.</summary>
    protected void SetInputDelay(float delay)
    {
        inputDelayTimer = Mathf.Max(inputDelayTimer, delay);
    }

    // ── Flip Lock ─────────────────────────────────────────────────
    private void TickFlipLock()
    {
        if (flipLockTimer <= 0f) return;

        flipLockTimer -= Time.deltaTime;
        if (flipLockTimer > 0f) return;

        flipLockTimer  = 0f;
        flipLockTarget = null;
        GetComponent<PlayerController>().CanFlip = true;
    }

    /// <summary>
    /// Lock flip trong duration giây rồi tự nhả.
    /// Flip về hướng target trước khi lock.
    /// Gọi lại trước khi hết sẽ reset timer bắt đầu lại.
    /// </summary>
    protected void EasyFlipLock(float duration, Transform target)
    {
        var controller = GetComponent<PlayerController>();
        if (target != null)
            controller.Flipping(target);

        controller.CanFlip = false;
        flipLockTimer      = duration;
        flipLockTarget     = target;
    }

    // ── Override IsReady & Use ────────────────────────────────────
    /// <summary>Override IsReady — check thêm IsInputReady.</summary>
    protected new bool IsReady(string skillId, int requiredCounter = 1)
    {
        if (!IsInputReady) return false;
        return base.IsReady(skillId, requiredCounter);
    }

    /// <summary>
    /// Override Use — tự đọc inputDelay từ skillData sau khi dùng.
    /// </summary>
    protected new bool Use(string skillId, int counterAmount = 1)
    {
        bool result = base.Use(skillId, counterAmount);
        if (result && skillData.TryGetValue(skillId, out var data))
            SetInputDelay(data.Get<float>("inputDelay", 0f));
        return result;
    }

    /// <summary>
    /// Use mà không set inputDelay — dùng khi delay sẽ được set thủ công sau.
    /// </summary>
    protected bool UseOnly(string skillId, int counterAmount = 1)
    {
        return base.Use(skillId, counterAmount);
    }

    /// <summary>
    /// Use với flag applyInputDelay.
    /// true → set inputDelay từ skillData (hành vi mặc định).
    /// false → chỉ tick cooldown, không set delay.
    /// </summary>
    protected bool Use(string skillId, bool applyInputDelay, int counterAmount = 1)
    {
        if (applyInputDelay)
            return Use(skillId, counterAmount);
        return UseOnly(skillId, counterAmount);
    }

    /// <summary>
    /// Use với attackSpeed scale — delay / (atkSpeed / 100).
    /// Dùng cho Attack và SilverAmbush.
    /// </summary>
    protected bool UseWithAtkSpeed(string skillId, float atkSpeed, int counterAmount = 1)
    {
        bool result = base.Use(skillId, counterAmount);
        if (result && skillData.TryGetValue(skillId, out var data))
        {
            float delay  = data.Get<float>("inputDelay", 0f);
            float scaled = atkSpeed > 0f ? delay / (atkSpeed / 100f) : delay;
            SetInputDelay(scaled);
        }
        return result;
    }
}