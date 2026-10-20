using System;

// EN: Platform limits of the projectile service (not gameplay content, so mods cannot change them and they stay out of the content
//     hash): the global number of projectile objects and the largest number of sub-steps a tick is split into.
// VI: Giới hạn nền tảng của dịch vụ đạn (không phải nội dung gameplay nên mod không đổi được): tổng số object đạn và số bước con tối đa.
public sealed class PNProjectileProfile
{
    // Mobile 500, otherwise 1000 (the design budget; mobile is not measured on a device yet).
    public static int DefaultMaxActive
    {
        get
        {
#if UNITY_5_3_OR_NEWER
            return UnityEngine.Application.platform == UnityEngine.RuntimePlatform.Android || UnityEngine.Application.platform == UnityEngine.RuntimePlatform.IPhonePlayer ? 500 : 1000;
#else
            return 1000;
#endif
        }
    }
    // Limit on projectile objects across every pool (active + idle). At the limit the oldest idle object is destroyed to make room;
    // when every object is active the new projectile is rejected.
    public int MaxActive { get; set; } = DefaultMaxActive;
    public int MaxSubSteps { get; set; } = 4;
    // How often the per-world candidate list is rebuilt (seconds of game time; 0 = every tick). Boxes are re-read every tick regardless.
    public float TargetRefreshSeconds { get; set; } = .1f;
    public void Validate()
    {
        if (MaxActive < 1 || MaxActive > 100000) throw new ArgumentOutOfRangeException(nameof(MaxActive));
        if (MaxSubSteps < 1 || MaxSubSteps > 16) throw new ArgumentOutOfRangeException(nameof(MaxSubSteps));
        if (float.IsNaN(TargetRefreshSeconds) || TargetRefreshSeconds < 0f || TargetRefreshSeconds > 5f) throw new ArgumentOutOfRangeException(nameof(TargetRefreshSeconds));
    }
}
