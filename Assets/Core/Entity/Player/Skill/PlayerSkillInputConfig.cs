using System;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;

// EN: Selects the held-button interaction independently of Tap.
// VI: Chọn tương tác giữ nút độc lập với Tap.
public enum PlayerSkillHoldMode { None, Hold, SlowTap, Repeat }

// EN: Per-button input settings; zero durations select Input System defaults.
// VI: Cấu hình input từng nút; thời gian bằng 0 dùng mặc định Input System.
[Serializable]
public class PlayerSkillInputConfig
{
    public bool CanTap = true;
    // EN: Execute a single press at key-down instead of waiting for Tap release.
    // VI: Thực hiện một lần lúc nhấn xuống thay vì chờ nhả nút Tap.
    public bool PressImmediately;
    public PlayerSkillHoldMode HoldMode;
    public float TapDuration;
    public float HoldDuration;
    public float SlowTapDuration;
    public float RepeatDelay;
    public float RepeatInterval;

    public virtual bool HasInteraction => CanTap || HoldMode != PlayerSkillHoldMode.None;

    public virtual void Validate()
    {
        if (!Enum.IsDefined(typeof(PlayerSkillHoldMode), HoldMode)) throw new ArgumentOutOfRangeException(nameof(HoldMode));
        foreach (float value in new[] { TapDuration, HoldDuration, SlowTapDuration, RepeatDelay, RepeatInterval })
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(nameof(value), "Input durations must be finite and nonnegative.");
    }

    public virtual string BuildInteractions()
    {
        Validate();
        var interactions = new List<string>();
        if (CanTap) interactions.Add(PressImmediately ? "press(behavior=0)" : Duration("tap", TapDuration));
        switch (HoldMode)
        {
            case PlayerSkillHoldMode.Hold: interactions.Add(Duration("hold", HoldDuration)); break;
            case PlayerSkillHoldMode.SlowTap: interactions.Add(Duration("slowTap", SlowTapDuration)); break;
            case PlayerSkillHoldMode.Repeat:
                interactions.Add("PNRepeat(delay=" + Number(RepeatDelay) + ",interval=" + Number(RepeatInterval) + ")");
                break;
        }
        return string.Join(";", interactions);
    }

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Duration(string name, float value)
        => value > 0f ? name + "(duration=" + Number(value) + ")" : name + "()";
}
