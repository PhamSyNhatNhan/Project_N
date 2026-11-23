using EveEngine;

// EN: Eve's looks for EVERY machine (Docs/ObservedViewPlan.md PV3). Her slashes are Actions whose views the engine drives; what is not an Action (the Burst form of the sword, the energy trail of a motion)
//     is a registered look (`EveLooks.cs`) that this code only turns on and off with `SetLook`. The engine draws it where Eve is simulated and publishes it for everyone else; nothing here reads a
//     picture or knows a role.
// VI: Hình dạng của Eve cho MỌI máy: nhát chém là view do engine đẩy; Burst và vệt năng lượng là look đăng ký, code Eve chỉ bật/tắt bằng SetLook.
public partial class EveCharacterSkill
{
    public static readonly EveId BurstLook = EveId.Parse("project_n:eve_burst"), MotionTrailLook = EveId.Parse("project_n:eve_motion_trail");

    // The energy trail of a motion.
    private void MotionLook(bool on) => SetLook(MotionTrailLook, on);
#if UNITY_5_3_OR_NEWER
    // What the looks need from Eve's visuals.
    internal EveEnergyVisual LookEnergy() => EnsureEnergy();
    internal EveEnergyVisual LookEnergyIfAny => energy;
#endif
}
