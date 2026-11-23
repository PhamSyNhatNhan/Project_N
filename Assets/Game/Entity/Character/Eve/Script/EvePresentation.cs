using System;
using EveEngine;
using UnityEngine;

// EN: Eve's look for the OTHER players (Docs/SkillNetworkFrameworkPlan.md G8). Her slashes are Actions the framework replays on every observer (every Action is replayed by default since G8c, nothing here). What is not an Action goes
//     through `Presentation`: Burst (the weapon's form) and the energy trail of a motion. The authority sets them where Eve draws them herself; an observer draws them from `OnPresentationChanged`. The owner's client
//     and the authority draw their own look in the cast code and ignore the picture.
// VI: Hình dạng của Eve cho NGƯỜI KHÁC. Nhát chém là Action do bộ khung phát lại; thứ không phải Action (Burst, vệt năng lượng của motion) đi qua Presentation; người xem vẽ từ OnPresentationChanged.
public partial class EveCharacterSkill
{
    public const string BurstPresentation = "burst", MotionPresentation = "motion";

    // The energy trail of a motion: drawn here and published for the observers (a client's `Set` is ignored, so the owner's guess only draws).
    private void MotionLook(bool on)
    {
        Presentation.Set(MotionPresentation, on ? "1" : "");
#if UNITY_5_3_OR_NEWER
        if (on) EnsureEnergy().SetMotion(true); else energy?.SetMotion(false);
#endif
    }

    private void OnPresentationChanged(string key, string value)
    {
        if (HasGameplayAuthority || IsOwnerClient || ownedAbilities == null || movesetRegistry == null) return;
        bool on = value == "1";
#if UNITY_5_3_OR_NEWER
        switch (key)
        {
            case BurstPresentation: WeaponRig?.SetBurst(on); if (on) EnsureEnergy().Flash(); else energy?.SetBurst(false); break;
            case MotionPresentation: if (on) EnsureEnergy().SetMotion(true); else if (energy != null && !IsBusy) energy.SetMotion(false); break;
        }
#endif
    }
    // An observer whose entity finished loading after the picture arrived draws what the picture already says.
    private void ReplayPresentation()
    {
        foreach (var pair in Presentation.Values) OnPresentationChanged(pair.Key, pair.Value);
    }
}
