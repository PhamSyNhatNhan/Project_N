using System;

public sealed partial class PNAbilityState : PNAbility
{
    private bool active;
    private double? duration;
    private double cooldown, cooldownRemaining;
    private double reduceFlat, reducePercent;
    public double ActualCooldown => PNAbilityDefinition.CalculateCooldown(cooldown, reduceFlat, reducePercent);
    internal bool Transient { get; }
    public override bool IsActive => active;
    public bool CanActivate => IsAttached && !active && cooldownRemaining == 0;
    public override double? DurationRemaining => duration;
    public override double CooldownRemaining => cooldownRemaining;
    internal PNAbilityState(PNAbilityCollection owner, PNAbilityDefinition definition, PNAbilityHandle handle, bool transient)
        : base(owner, definition, handle) { Transient = transient; Initialize(!transient && definition.InitiallyActive); }
    private void Initialize(bool initiallyActive)
    {
        cooldown = Definition.Cooldown; reduceFlat = reducePercent = 0; cooldownRemaining = 0; active = initiallyActive;
        duration = active ? Definition.Duration : null;
        if (active && Definition.CooldownStart == PNAbilityCooldownStart.OnActivate) cooldownRemaining = ActualCooldown;
    }
    public bool TryActivate(double? seconds = null)
    {
        RequireAttached(); if (seconds.HasValue) PNAbilityDefinition.RequireSeconds(seconds.Value, false);
        if (!CanActivate) return false;
        var before = Snapshot(); active = true; duration = seconds ?? Definition.Duration;
        if (Definition.CooldownStart == PNAbilityCooldownStart.OnActivate) cooldownRemaining = ActualCooldown;
        Changed(PNAbilityChangeReason.Activated, before); return true;
    }
    public void RefreshDuration(double seconds)
    {
        RequireAttached(); PNAbilityDefinition.RequireSeconds(seconds, false);
        if (!active) throw new InvalidOperationException("Only active states can be refreshed.");
        var before = Snapshot(); duration = seconds; Changed(PNAbilityChangeReason.Refreshed, before);
    }
    public void ExtendDuration(double seconds)
    {
        RequireAttached(); PNAbilityDefinition.RequireSeconds(seconds, false);
        if (!active || !duration.HasValue) throw new InvalidOperationException("Only active timed states can be extended.");
        double next = duration.Value + seconds; PNAbilityDefinition.RequireSeconds(next, false);
        var before = Snapshot(); duration = next; Changed(PNAbilityChangeReason.Refreshed, before);
    }
    public void SetCooldown(double seconds)
    {
        RequireAttached(); PNAbilityDefinition.RequireSeconds(seconds, true);
        PNAbilityDefinition.CalculateCooldown(seconds, reduceFlat, reducePercent);
        var before = Snapshot(); cooldown = seconds; Changed(PNAbilityChangeReason.Reconfigured, before);
    }
    public void SetReduction(double flat, double percent)
    {
        RequireAttached(); PNAbilityDefinition.CalculateCooldown(cooldown, flat, percent);
        var before = Snapshot(); reduceFlat = flat; reducePercent = percent;
        Changed(PNAbilityChangeReason.Reconfigured, before);
    }
    public void RestartCooldown()
    {
        RequireAttached(); var before = Snapshot(); cooldownRemaining = ActualCooldown;
        Changed(PNAbilityChangeReason.Reconfigured, before);
    }
    public bool End()
    {
        RequireAttached(); if (!active) return false;
        EndInternal(PNAbilityChangeReason.Ended); return true;
    }
    private void EndInternal(PNAbilityChangeReason reason)
    {
        var before = Snapshot(); active = false; duration = null;
        if (Definition.CooldownStart == PNAbilityCooldownStart.OnEnd) cooldownRemaining = ActualCooldown;
        Changed(reason, before, reason == PNAbilityChangeReason.Used ? 1 : 0);
    }
    public override bool CanConsume(int amount = 1) => IsAttached && active && amount == 1;
    public override bool TryConsume(int amount = 1)
    {
        RequireAttached(); if (!CanConsume(amount)) return false;
        EndInternal(PNAbilityChangeReason.Used); return true;
    }
    public override void Reset()
    {
        RequireAttached(); var before = Snapshot(); Initialize(!Transient && Definition.InitiallyActive);
        Changed(PNAbilityChangeReason.Reset, before);
    }
    private void AdvanceCooldown(double delta)
    {
        if (cooldownRemaining == 0) return;
        var before = Snapshot(); cooldownRemaining = Math.Max(0, cooldownRemaining - delta);
        if (cooldownRemaining == 0) Changed(PNAbilityChangeReason.Recovered, before);
    }
    internal override void Advance(double delta)
    {
        if (active && duration.HasValue && duration.Value <= delta)
        {
            double untilEnd = duration.Value;
            AdvanceCooldown(untilEnd); duration = 0;
            EndInternal(PNAbilityChangeReason.Expired);
            AdvanceCooldown(delta - untilEnd);
        }
        else
        {
            AdvanceCooldown(delta);
            if (active && duration.HasValue) duration -= delta;
        }
    }
}
