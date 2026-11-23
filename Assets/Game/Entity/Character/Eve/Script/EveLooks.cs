#if UNITY_5_3_OR_NEWER
using EveEngine;

// EN: Eve's looks (Docs/ObservedViewPlan.md PV3), registered once and drawn on every machine by the engine (`PNSkill.SetLook`): the Burst form of the sword and the energy trail of a motion.
//     Eve's code only turns them on and off; there is no network branch here and no per-key handler.
// VI: Look của Eve: dạng Burst của kiếm và vệt năng lượng khi chuyển động. Code Eve chỉ bật/tắt; không còn nhánh mạng hay xử lý theo khóa.
public sealed class EveBurstLook : PNLook
{
    public static readonly EveId Id = EveId.Parse("project_n:eve_burst");
    private readonly EveCharacterSkill eve;
    private EveBurstLook(EveCharacterSkill eve) { this.eve = eve; var energy = eve.LookEnergy(); eve.WeaponRig.SetBurst(true); energy.SetBurst(true); energy.Flash(); }
    public static PNLook Create(PNLookBuild build)
        => build.Skill is EveCharacterSkill eve && eve.WeaponRig != null ? new EveBurstLook(eve) : null;
    public override void End(PNActionViewEnd kind) { eve.WeaponRig?.SetBurst(false); eve.LookEnergyIfAny?.SetBurst(false); }
}

public sealed class EveMotionTrailLook : PNLook
{
    public static readonly EveId Id = EveId.Parse("project_n:eve_motion_trail");
    private readonly EveCharacterSkill eve;
    private EveMotionTrailLook(EveCharacterSkill eve) { this.eve = eve; eve.LookEnergy().SetMotion(true); }
    public static PNLook Create(PNLookBuild build) => build.Skill is EveCharacterSkill eve ? new EveMotionTrailLook(eve) : null;
    public override void End(PNActionViewEnd kind) => eve.LookEnergyIfAny?.SetMotion(false);
}
#endif
