using System;
using EveEngine;

// Immutable sender-side snapshot. Receiving applies only the target's modifiers.
public sealed class PNHeal
{
    public Guid SessionId { get; }
    public EveEntityId SourceId { get; }
    public double BaseAmount { get; }
    public double Amount { get; }

    public PNHeal(Guid sessionId, EveEntityId sourceId, double amount, double? baseAmount = null)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Heal session is required.", nameof(sessionId));
        if (!sourceId.IsValid) throw new ArgumentException("Heal source is required.", nameof(sourceId));
        RequireAmount(amount);
        RequireAmount(baseAmount ?? amount);
        SessionId = sessionId; SourceId = sourceId; Amount = amount; BaseAmount = baseAmount ?? amount;
    }

    internal static void RequireAmount(double amount)
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0d)
            throw new ArgumentOutOfRangeException(nameof(amount), "Heal amount must be finite and nonnegative.");
    }
}

public enum PNHealStatus { Applied, NoMissingHealth, DeadTarget, ZeroAmount }

public sealed class PNHealResult
{
    public PNHeal Heal { get; }
    public EveEntityId TargetId { get; }
    public double CalculatedHeal { get; }
    public double HealthBefore { get; }
    public double HealthAfter { get; }
    public double AppliedHeal => Math.Max(0d, HealthAfter - HealthBefore);
    public double Overheal => Status == PNHealStatus.DeadTarget ? 0d : Math.Max(0d, CalculatedHeal - AppliedHeal);
    public PNHealStatus Status { get; }

    public PNHealResult(PNHeal heal, EveEntityId targetId, double calculatedHeal,
        double healthBefore, double healthAfter, PNHealStatus status)
    {
        Heal = heal ?? throw new ArgumentNullException(nameof(heal));
        if (!targetId.IsValid) throw new ArgumentException("Heal target is required.", nameof(targetId));
        PNHeal.RequireAmount(calculatedHeal); PNHeal.RequireAmount(healthBefore); PNHeal.RequireAmount(healthAfter);
        if (healthAfter < healthBefore) throw new ArgumentException("Heal cannot decrease health.");
        TargetId = targetId; CalculatedHeal = calculatedHeal;
        HealthBefore = healthBefore; HealthAfter = healthAfter; Status = status;
    }
}
