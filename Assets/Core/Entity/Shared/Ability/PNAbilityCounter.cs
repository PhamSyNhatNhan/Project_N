using System;
using System.Collections.Generic;
using System.Linq;

public sealed class PNAbilityCounterGrant
{
    public long Id { get; }
    public int Amount { get; }
    public double? Remaining { get; }
    internal PNAbilityCounterGrant(long id, int amount, double? remaining)
    { Id = id; Amount = amount; Remaining = remaining; }
}

public sealed partial class PNAbilityCounter : PNAbility
{
    private sealed class Grant { public long Id; public int Count; public double? Remaining; }
    private readonly List<Grant> grants = new List<Grant>();
    private int current, maximum;
    private long nextGrant;
    public int Current => current;
    public int Capacity => maximum;
    public override int? Amount => current;
    public override int? Maximum => maximum;
    public override double? DurationRemaining => grants.Where(g => g.Remaining.HasValue)
        .Select(g => g.Remaining).OrderBy(t => t).FirstOrDefault();
    public IReadOnlyList<PNAbilityCounterGrant> Grants => grants.Select(g => new PNAbilityCounterGrant(g.Id, g.Count, g.Remaining)).ToList().AsReadOnly();
    internal PNAbilityCounter(PNAbilityCollection owner, PNAbilityDefinition definition, PNAbilityHandle handle)
        : base(owner, definition, handle) { Initialize(); }
    private void Initialize()
    {
        grants.Clear(); current = 0; maximum = Definition.Maximum;
        if (Definition.InitialAmount > 0) AddInternal(Definition.InitialAmount, Definition.Duration);
    }
    private void AddInternal(int amount, double? duration)
    {
        if (Definition.Lifetime == PNAbilityLifetime.Shared && grants.Count > 0) grants[0].Count += amount;
        else grants.Add(new Grant { Id = checked(++nextGrant), Count = amount, Remaining = duration });
        current += amount;
    }
    public int Add(int amount, double? duration = null)
    {
        RequireAttached(); if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (duration.HasValue) PNAbilityDefinition.RequireSeconds(duration.Value, false);
        int added = (int)Math.Min((long)amount, maximum - current);
        if (added == 0) return 0;
        var before = Snapshot(); AddInternal(added, duration ?? Definition.Duration);
        Changed(PNAbilityChangeReason.Added, before); return added;
    }
    public override bool CanConsume(int amount = 1) => IsAttached && amount > 0 && current >= amount;
    public override bool TryConsume(int amount = 1)
    {
        RequireAttached(); if (!CanConsume(amount)) return false;
        var before = Snapshot(); Drain(amount); Changed(PNAbilityChangeReason.Used, before); return true;
    }
    private void Drain(int amount)
    {
        // EN: Earliest expiry first, permanent grants last; ties preserve grant order.
        // VI: Tiêu phần hết hạn sớm trước, phần vô hạn sau; cùng hạn giữ thứ tự nhận.
        foreach (var grant in grants.OrderBy(g => g.Remaining ?? double.PositiveInfinity).ThenBy(g => g.Id).ToArray())
        {
            int consumed = Math.Min(amount, grant.Count); grant.Count -= consumed; current -= consumed; amount -= consumed;
            if (grant.Count == 0) grants.Remove(grant);
            if (amount == 0) break;
        }
    }
    public void Clear()
    {
        RequireAttached(); if (current == 0) return;
        var before = Snapshot(); grants.Clear(); current = 0; Changed(PNAbilityChangeReason.Reset, before);
    }
    public void SetAmount(int value)
    {
        RequireAttached(); if (value < 0 || value > maximum) throw new ArgumentOutOfRangeException(nameof(value));
        if (value == current) return;
        var before = Snapshot();
        if (value > current) AddInternal(value - current, Definition.Duration); else Drain(current - value);
        Changed(PNAbilityChangeReason.Reconfigured, before);
    }
    public void SetMaximum(int value)
    {
        RequireAttached(); if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (value == maximum) return;
        var before = Snapshot(); maximum = value; if (current > maximum) Drain(current - maximum);
        Changed(PNAbilityChangeReason.Reconfigured, before);
    }
    // EN: Addition never refreshes existing timers, including when full; refresh is explicit.
    // VI: Cộng lượng không làm mới timer đang có, kể cả khi đầy; gia hạn phải chủ động.
    public void RefreshDuration(double seconds, long? grantId = null)
    {
        RequireAttached(); PNAbilityDefinition.RequireSeconds(seconds, false);
        var targets = grants.Where(g => !grantId.HasValue || g.Id == grantId.Value).ToArray();
        if (grantId.HasValue && targets.Length == 0) throw new KeyNotFoundException("Counter grant does not exist.");
        if (targets.Length == 0) return;
        var before = Snapshot(); foreach (var grant in targets) grant.Remaining = seconds;
        Changed(PNAbilityChangeReason.Refreshed, before);
    }
    public override void Reset()
    {
        RequireAttached(); var before = Snapshot(); Initialize(); Changed(PNAbilityChangeReason.Reset, before);
    }
    internal override void Advance(double delta)
    {
        if (delta == 0) return;
        var expiring = grants.Where(g => g.Remaining.HasValue && g.Remaining.Value <= delta)
            .OrderBy(g => g.Remaining).ThenBy(g => g.Id).ToArray();
        foreach (var grant in grants)
            if (grant.Remaining.HasValue) grant.Remaining = Math.Max(0, grant.Remaining.Value - delta);
        foreach (var grant in expiring)
        {
            var before = Snapshot(); current -= grant.Count; grants.Remove(grant);
            Changed(PNAbilityChangeReason.Expired, before);
        }
    }
}
