using EveEngine;
using UnityEngine;

// EN: What Eve chooses when a cast starts (Docs/EveMovesetRefactorPlan.md E2): the kind, the combo step and branch, the direction and the target. Plain data: the Decide* methods
//     only read input, Abilities and positions and write nothing, so the machine that owns the input can decide and the machine that runs the cast can execute the same decision.
//     The rush distance is not stored: it follows from the target, the positions and the motion data when the motion starts.
// VI: Điều Eve chọn khi bắt đầu một cast: loại, bước combo, nhánh, hướng, mục tiêu. Dữ liệu thuần; hàm Decide* chỉ đọc, không ghi.
public readonly struct EveCastDecision
{
    public EveCast Cast { get; }
    public Vector3 Direction { get; }
    public PNStat Target { get; }
    public EveEntityId TargetId => Target == null ? default : Target.EntityId;
    // Attack: the combo step (0..2) the cast uses and whether the idle time ran out so the combo restarts first. Ultimate: unused.
    public int Step { get; }
    public bool ResetCombo { get; }
    // Attack: the next-attack empowerment is active (rush, then the larger slash). Ultimate: the cast runs inside Burst, so it is the three-stroke finisher.
    public bool Empowered { get; }
    public bool Finisher { get; }
    // Attack: the slash profile the cast ends with ("attack_right", "empowered_left", ...).
    public string Profile { get; }
    public EveCastDecision(EveCast cast, Vector3 direction, PNStat target, int step = 0, bool resetCombo = false, bool empowered = false, bool finisher = false, string profile = null)
    { Cast = cast; Direction = direction; Target = target; Step = step; ResetCombo = resetCombo; Empowered = empowered; Finisher = finisher; Profile = profile; }
}
