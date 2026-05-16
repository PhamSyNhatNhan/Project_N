using UnityEngine;

/// <summary>
/// EasyRotate - Xoay GameObject 2D với nhiều tùy chọn linh hoạt.
/// Gắn script này vào bất kỳ GameObject 2D nào để xoay tự động.
/// </summary>
public class EasyRotate : MonoBehaviour
{
    [Header("Tốc độ xoay (độ/giây)")]
    [Tooltip("Dương = ngược chiều kim đồng hồ, Âm = chiều kim đồng hồ")]
    public float speed = 90f;

    [Header("Hướng xoay")]
    public RotateDirection direction = RotateDirection.Clockwise;

    [Header("Chế độ xoay")]
    public RotateMode mode = RotateMode.Continuous;

    [Header("Xoay đến góc cụ thể (chỉ dùng khi mode = RotateTo)")]
    public float targetAngle = 0f;
    [Tooltip("Dừng lại khi đến góc mục tiêu?")]
    public bool stopAtTarget = true;

    [Header("Xoay qua lại (chỉ dùng khi mode = PingPong)")]
    public float pingPongMin = -45f;
    public float pingPongMax = 45f;

    [Header("Điều khiển")]
    public bool playOnStart = true;
    public bool useLocalRotation = true;

    // ── Trạng thái nội bộ ──────────────────────────────────────────────────
    private bool _isPlaying;
    private float _currentAngle;
    private int   _pingPongDir = 1;

    // ── Enum ──────────────────────────────────────────────────────────────
    public enum RotateDirection { Clockwise, CounterClockwise }

    public enum RotateMode
    {
        Continuous, 
        RotateTo,   
        PingPong    
    }

    // ── Unity Lifecycle ───────────────────────────────────────────────────
    void Start()
    {
        _currentAngle = useLocalRotation
            ? transform.localEulerAngles.z
            : transform.eulerAngles.z;

        if (playOnStart) Play();
    }

    void Update()
    {
        if (!_isPlaying) return;

        switch (mode)
        {
            case RotateMode.Continuous: UpdateContinuous(); break;
            case RotateMode.RotateTo:   UpdateRotateTo();   break;
            case RotateMode.PingPong:   UpdatePingPong();   break;
        }
    }

    // ── Chế độ xoay ──────────────────────────────────────────────────────
    void UpdateContinuous()
    {
        float delta = GetSignedSpeed() * Time.deltaTime;
        ApplyRotation(delta);
    }

    void UpdateRotateTo()
    {
        float remaining = Mathf.DeltaAngle(_currentAngle, targetAngle);

        if (Mathf.Abs(remaining) <= 0.1f)
        {
            ApplyExactAngle(targetAngle);
            if (stopAtTarget) Stop();
            return;
        }

        float delta = GetSignedSpeed() * Time.deltaTime;

        if (Mathf.Abs(delta) > Mathf.Abs(remaining))
            delta = remaining;

        ApplyRotation(delta);
    }

    void UpdatePingPong()
    {
        float delta = speed * _pingPongDir * Time.deltaTime;
        _currentAngle += delta;

        if (_currentAngle >= pingPongMax)
        {
            _currentAngle = pingPongMax;
            _pingPongDir  = -1;
        }
        else if (_currentAngle <= pingPongMin)
        {
            _currentAngle = pingPongMin;
            _pingPongDir  = 1;
        }

        SetAngle(_currentAngle);
    }

    // ── Hàm tiện ích ─────────────────────────────────────────────────────
    float GetSignedSpeed()
    {
        return direction == RotateDirection.Clockwise ? -speed : speed;
    }

    void ApplyRotation(float delta)
    {
        _currentAngle += delta;
        SetAngle(_currentAngle);
    }

    void ApplyExactAngle(float angle)
    {
        _currentAngle = angle;
        SetAngle(angle);
    }

    void SetAngle(float angle)
    {
        if (useLocalRotation)
            transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        else
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    // ── API công khai ─────────────────────────────────────────────────────

    /// <summary>Bắt đầu xoay.</summary>
    public void Play() => _isPlaying = true;

    /// <summary>Dừng xoay.</summary>
    public void Stop() => _isPlaying = false;

    /// <summary>Bật/tắt xoay.</summary>
    public void Toggle() => _isPlaying = !_isPlaying;

    /// <summary>Đặt lại về góc 0 và dừng.</summary>
    public void Reset()
    {
        Stop();
        ApplyExactAngle(0f);
    }

    /// <summary>Thay đổi tốc độ lúc runtime.</summary>
    public void SetSpeed(float newSpeed) => speed = Mathf.Abs(newSpeed);

    /// <summary>Xoay ngay đến góc chỉ định (chuyển sang mode RotateTo).</summary>
    public void RotateTo(float angle, bool stopWhenDone = true)
    {
        mode        = RotateMode.RotateTo;
        targetAngle = angle;
        stopAtTarget = stopWhenDone;
        Play();
    }

    /// <summary>Đảo chiều xoay.</summary>
    public void FlipDirection()
    {
        direction = direction == RotateDirection.Clockwise
            ? RotateDirection.CounterClockwise
            : RotateDirection.Clockwise;
    }

    /// <summary>Góc hiện tại (Z-axis).</summary>
    public float CurrentAngle => _currentAngle;

    /// <summary>Đang xoay?</summary>
    public bool IsPlaying => _isPlaying;
}