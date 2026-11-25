using System;
using System.Collections.Generic;
using EveEngine;

// EN: One crowd-control strength travelling with a hit (Docs/BodyMotionPlan.md 3.4, B5-B7). The sender's Scale route already turned the registered input into `Value` and the sender's penetration modifiers
//     ride along; the receiver applies its own Mitigation (plus that penetration) in `PNStat.ReceiveCrowdControl`. Every control effect (push, pull, later stun or slow) takes its strength from here, none computes it.
// VI: Mot gia tri khong che di kem don (B5-B7): ben gay da Scale `Value` tu gia tri dang ky, penetration cua ben gay di cung; ben nhan ap Mitigation cua minh o `PNStat.ReceiveCrowdControl`.
public sealed class PNCrowdControl
{
    public Guid SessionId { get; }
    public EveEntityId SourceId { get; }
    public double Input { get; }
    public double Value { get; }
    public IReadOnlyList<EveStatModifier> Penetration { get; }

    public PNCrowdControl(Guid sessionId, EveEntityId sourceId, double input, double value, IReadOnlyList<EveStatModifier> penetration)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Crowd control session is required.", nameof(sessionId));
        Require(input, nameof(input)); Require(value, nameof(value));
        SessionId = sessionId; SourceId = sourceId; Input = input; Value = value; Penetration = penetration ?? PNDamage.NoPenetration;
    }
    internal static void Require(double amount, string name)
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0d)
            throw new ArgumentOutOfRangeException(name, "Crowd control must be finite and nonnegative.");
    }
}

public partial class PNStat
{
    // The sender, live: the registered `input` (e.g. 1.1) through this entity's Scale route of the crowd_control tag, with its penetration modifiers captured. A Buff or a system source uses the captured snapshot instead.
    public virtual PNCrowdControl CreateCrowdControl(double input)
    {
        var stats = RequireCombatReady();
        PNCrowdControl.Require(input, nameof(input));
        return new PNCrowdControl(SessionId, EntityId, input, Calculate(PNContentIds.Scale, input, PNContentIds.CrowdControl), stats.PenetrationModifiers);
    }
    // The receiver: the factor this entity takes from the hit, after its own Mitigation route of the crowd_control tag with the hit's penetration joined for this call only (nothing is stored).
    // No modifier -> the value unchanged. Never negative; zero means the control effect does not happen (B7).
    public virtual double ReceiveCrowdControl(PNCrowdControl control)
    {
        var stats = RequireCombatReady();
        if (control == null) throw new ArgumentNullException(nameof(control));
        if (control.SessionId != SessionId) throw new ArgumentException("Crowd control belongs to another session.", nameof(control));
        return stats.CalculateCrowdControl(control.Value, control.Penetration);
    }
}
