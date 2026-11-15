using EveEngine;
using UnityEngine;

// EN: G3a. What Eve keeps for ONE cast lives in the cast's memory (`invocation.Memory`), not in fields of the skill: the direction and target the cast follows, the slash profile it chose, the time left in
//     its phase, the "already rewarded" marks and the finisher mark. The memory belongs to the invocation, so it starts empty with every cast and is dropped with it (after `OnCastEnded`).
//     Two things stay outside because they are not "one cast": the cast state machine itself (`CurrentCast`/`CurrentPhase`, reset in `OnCastEnded`) and the Actions that run (`motion`, `attack`, the lock),
//     which the cast registers for the one cancel path. When no invocation runs (the Dash start hook runs before its activation begins; a guessed Dash has none) the reads and writes go to the memory
//     of the cast that ended last, exactly as the old fields did. G6 removes that case when the Dash guess joins the common path.
// VI: Thứ Eve giữ cho MỘT cast nằm trong bộ nhớ cast (`invocation.Memory`), không nằm trong field của skill. Bộ nhớ thuộc invocation nên mỗi cast bắt đầu rỗng và mất cùng nó (sau `OnCastEnded`).
public sealed class EveCastMemory
{
    public Vector3 Direction;
    public PNStat Target;
    public string PendingAttackProfile;
    public float PhaseRemaining;
    public bool RewardedHit, UltimateRewarded, BurstFinisher;
    public PNSkillActionContext AttackContext;
    // The marks of the cast that ended last are carried into the next one. The old fields kept their value from cast to cast and the golden trace prints them (`rw`, `ur`, `bf`, `face` follows the direction),
    // so a fresh memory would change behaviour that G3a must keep (a Burst cast commits the direction the previous cast left). No rule reads them before the cast writes them; dropping the carry-over is
    // a deliberate trace change for a later step, not part of this one.
    internal void Inherit(EveCastMemory previous)
    {
        if (previous == null || ReferenceEquals(previous, this)) return;
        Direction = previous.Direction; Target = previous.Target; PendingAttackProfile = previous.PendingAttackProfile; PhaseRemaining = previous.PhaseRemaining;
        RewardedHit = previous.RewardedHit; UltimateRewarded = previous.UltimateRewarded; BurstFinisher = previous.BurstFinisher;
    }
}

public partial class EveCharacterSkill
{
    // The memory of the running cast; with no cast, the one that ended last (see the header).
    private EveCastMemory detachedCast = new EveCastMemory();
    private EveCastMemory Cast
    {
        get
        {
            var invocation = CurrentInvocation;
            if (invocation == null || invocation.IsEnded) return detachedCast;
            bool first = !invocation.Memory.HasValue;
            var memory = invocation.Memory.Use<EveCastMemory>();
            if (first) memory.Inherit(detachedCast);
            return memory;
        }
    }
    private Vector3 castDirection { get => Cast.Direction; set => Cast.Direction = value; }
    private PNStat castTarget { get => Cast.Target; set => Cast.Target = value; }
    private string pendingAttackProfile { get => Cast.PendingAttackProfile; set => Cast.PendingAttackProfile = value; }
    private float phaseRemaining { get => Cast.PhaseRemaining; set => Cast.PhaseRemaining = value; }
    private bool rewardedHit { get => Cast.RewardedHit; set => Cast.RewardedHit = value; }
    private bool ultimateRewarded { get => Cast.UltimateRewarded; set => Cast.UltimateRewarded = value; }
    private bool burstFinisher { get => Cast.BurstFinisher; set => Cast.BurstFinisher = value; }
    private PNSkillActionContext attackContext { get => Cast.AttackContext; set => Cast.AttackContext = value; }
}
