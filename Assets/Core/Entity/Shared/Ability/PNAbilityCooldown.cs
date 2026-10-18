using System;

public sealed partial class PNAbilityCooldown : PNAbility
{
    private int current, maximum;
    private double baseCooldown, reduceFlat, reducePercent, remaining;
    private bool autoRecover, recovering;
    public int Current => current;
    public int Capacity => maximum;
    public override int? Amount => current;
    public override int? Maximum => maximum;
    public override double CooldownRemaining => remaining;
    public bool IsRecovering => recovering;
    public bool AutoRecover => autoRecover;
    public double ActualCooldown => PNAbilityDefinition.CalculateCooldown(baseCooldown, reduceFlat, reducePercent);
    internal PNAbilityCooldown(PNAbilityCollection owner, PNAbilityDefinition definition, PNAbilityHandle handle)
        : base(owner, definition, handle) { Initialize(); }
    private void Initialize()
    {
        current = Definition.InitialAmount; maximum = Definition.Maximum;
        baseCooldown = Definition.Cooldown; reduceFlat = reducePercent = remaining = 0;
        autoRecover = Definition.AutoRecover; recovering = false;
        StartIfNeeded();
    }
    public override bool CanConsume(int amount = 1) => IsAttached && amount == 1 && current > 0;
    public override bool TryConsume(int amount = 1)
    {
        RequireAttached(); if (!CanConsume(amount)) return false;
        var before = Snapshot(); current--; StartIfNeeded();
        Changed(PNAbilityChangeReason.Used, before, 1); return true;
    }
    public int AddCharges(int amount)
    {
        RequireAttached(); if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        int added = (int)Math.Min((long)amount, maximum - current);
        if (added == 0) return 0;
        var before = Snapshot(); current += added;
        if (current == maximum) Stop();
        else StartIfNeeded();
        Changed(PNAbilityChangeReason.Added, before); return added;
    }
    public void SetMaximum(int value)
    {
        RequireAttached(); if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (value == maximum) return;
        var before = Snapshot(); maximum = value; current = Math.Min(current, maximum);
        if (current == maximum) Stop(); else StartIfNeeded();
        Changed(PNAbilityChangeReason.Reconfigured, before);
    }
    // EN: Parameter changes affect the NEXT recovery cycle; explicit RestartRecovery adjusts this one.
    // VI: Đổi tham số áp dụng lượt hồi TIẾP; RestartRecovery chủ động chỉnh lượt hiện tại.
    public void SetCooldown(double seconds)
    {
        RequireAttached(); PNAbilityDefinition.RequireSeconds(seconds, true);
        PNAbilityDefinition.CalculateCooldown(seconds, reduceFlat, reducePercent);
        var before = Snapshot(); baseCooldown = seconds; Changed(PNAbilityChangeReason.Reconfigured, before);
    }
    public void SetReduction(double flat, double percent)
    {
        RequireAttached(); PNAbilityDefinition.CalculateCooldown(baseCooldown, flat, percent);
        var before = Snapshot(); reduceFlat = flat; reducePercent = percent;
        Changed(PNAbilityChangeReason.Reconfigured, before);
    }
    public void SetAutoRecovery(bool enabled)
    {
        RequireAttached(); if (enabled == autoRecover) return;
        var before = Snapshot(); autoRecover = enabled;
        if (!enabled) Stop(); else StartIfNeeded();
        Changed(PNAbilityChangeReason.Reconfigured, before);
    }
    public void RestartRecovery()
    {
        RequireAttached(); var before = Snapshot(); Stop(); StartIfNeeded();
        Changed(PNAbilityChangeReason.Reconfigured, before);
    }
    // EN: Gameplay shortens the CURRENT recovery; surplus can cross further charge periods.
    // VI: Gameplay rút ngắn lượt hồi HIỆN TẠI; thời gian dư có thể hồi thêm charge.
    public void ReduceRemaining(double seconds)
    {
        RequireAttached(); PNAbilityDefinition.RequireSeconds(seconds, true);
        if (!recovering || seconds == 0) return;
        if (seconds >= remaining) { Advance(seconds); return; }
        var before = Snapshot(); remaining -= seconds;
        Changed(PNAbilityChangeReason.Reconfigured, before);
    }
    public void ResetToFull()
    {
        RequireAttached(); var before = Snapshot(); current = maximum; Stop();
        Changed(PNAbilityChangeReason.Reset, before);
    }
    public override void Reset()
    {
        RequireAttached(); var before = Snapshot(); Initialize(); Changed(PNAbilityChangeReason.Reset, before);
    }
    private void Stop() { recovering = false; remaining = 0; }
    private void StartIfNeeded()
    {
        if (!autoRecover || recovering || current == maximum ||
            (Definition.Recovery == PNAbilityRecovery.AllAtOnce && current != 0)) return;
        double period = ActualCooldown;
        if (period == 0) { current = maximum; Stop(); }
        else { recovering = true; remaining = period; }
    }
    internal override void Advance(double delta)
    {
        if (!recovering) return;
        if (delta < remaining) { remaining -= delta; return; }
        var before = Snapshot(); double surplus = delta - remaining;
        if (Definition.Recovery == PNAbilityRecovery.AllAtOnce) { current = maximum; Stop(); }
        else
        {
            current++;
            double period = ActualCooldown;
            if (current == maximum || period == 0) { current = maximum; Stop(); }
            else
            {
                // EN: Aggregate crossed cycles without looping once per stack or losing overshoot.
                // VI: Gộp các lượt đã đi qua, không lặp theo từng stack và không mất thời gian dư.
                double crossed = Math.Floor(surplus / period);
                int missing = maximum - current;
                if (crossed >= missing) { current = maximum; Stop(); }
                else { current += (int)crossed; remaining = period - surplus % period; }
            }
        }
        Changed(PNAbilityChangeReason.Recovered, before);
    }
}
