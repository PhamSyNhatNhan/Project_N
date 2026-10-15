using System;
using EveEngine;

public enum PNAbilityChangeReason { Created, Added, Used, Recovered, Expired, Activated, Ended, Refreshed, Reset, Reconfigured, Removed }

// EN: Scoped to one runtime collection; stale/foreign handles cannot address new instances.
// VI: Gắn với một tập runtime; handle cũ/khác entity không trỏ vào instance mới.
public readonly struct PNAbilityHandle : IEquatable<PNAbilityHandle>
{
    public Guid Owner { get; }
    public long Instance { get; }
    internal PNAbilityHandle(Guid owner, long instance) { Owner = owner; Instance = instance; }
    public bool Equals(PNAbilityHandle other) => Owner == other.Owner && Instance == other.Instance;
    public override bool Equals(object obj) => obj is PNAbilityHandle other && Equals(other);
    public override int GetHashCode() => Owner.GetHashCode() ^ Instance.GetHashCode();
    public static bool operator ==(PNAbilityHandle a, PNAbilityHandle b) => a.Equals(b);
    public static bool operator !=(PNAbilityHandle a, PNAbilityHandle b) => !a.Equals(b);
}

public sealed class PNAbilitySnapshot
{
    public EveId Id { get; }
    public PNAbilityHandle Handle { get; }
    public PNAbilityKind Kind { get; }
    public int? Amount { get; }
    public int? Maximum { get; }
    public bool IsActive { get; }
    public double CooldownRemaining { get; }
    public double? DurationRemaining { get; }
    internal PNAbilitySnapshot(PNAbility ability)
    {
        Id = ability.Id; Handle = ability.Handle; Kind = ability.Definition.Kind;
        Amount = ability.Amount; Maximum = ability.Maximum; IsActive = ability.IsActive;
        CooldownRemaining = ability.CooldownRemaining; DurationRemaining = ability.DurationRemaining;
    }
}

public sealed class PNAbilityChange
{
    public PNAbilityChangeReason Reason { get; }
    public PNAbilitySnapshot Before { get; }
    public PNAbilitySnapshot After { get; }
    public int Quantity { get; }
    internal PNAbilityChange(PNAbilityChangeReason reason, PNAbilitySnapshot before, PNAbilitySnapshot after, int? quantity = null)
    {
        Reason = reason; Before = before; After = after;
        Quantity = quantity ?? Math.Abs((after?.Amount ?? 0) - (before?.Amount ?? 0));
    }
}

// EN: Passive counters/flags only. Skills consume this state and implement all effects.
// VI: Chỉ bộ đếm/flag; skill đọc trạng thái và thực hiện toàn bộ hiệu ứng.
public abstract class PNAbility
{
    private PNAbilityCollection owner;
    public PNAbilityDefinition Definition { get; }
    public EveId Id => Definition.Id;
    public PNAbilityHandle Handle { get; }
    public bool IsAttached => owner != null;
    public virtual int? Amount => null;
    public virtual int? Maximum => null;
    public virtual bool IsActive => false;
    public virtual double CooldownRemaining => 0;
    public virtual double? DurationRemaining => null;
    protected PNAbility(PNAbilityCollection owner, PNAbilityDefinition definition, PNAbilityHandle handle)
    { this.owner = owner; Definition = definition; Handle = handle; }
    public PNAbilitySnapshot Snapshot() => new PNAbilitySnapshot(this);
    protected void RequireAttached()
    { if (owner == null) throw new InvalidOperationException("Ability instance has been removed or unloaded."); owner.RequireMutable(); }
    protected void Changed(PNAbilityChangeReason reason, PNAbilitySnapshot before, int? quantity = null)
        => owner.Notify(new PNAbilityChange(reason, before, Snapshot(), quantity));
    internal void Detach() => owner = null;
    internal abstract void Advance(double delta);
    public abstract void Reset();
    public abstract bool CanConsume(int amount = 1);
    public abstract bool TryConsume(int amount = 1);
}
