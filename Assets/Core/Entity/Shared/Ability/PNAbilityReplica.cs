using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

// EN: The state of one Ability instance as a machine without authority holds it (Docs/SkillNetworkingAPlan.md S1). The authority captures it, the owner's client applies it; a client never
//     mutates an Ability through the normal API. `Index` is the position among the instances that share the id, in creation order. Values are plain numbers; time is "remaining at capture".
// VI: Trạng thái của một Ability cho máy không có authority. Authority chụp, client chủ nhân áp; client không ghi Ability bằng API thường.
public readonly struct PNAbilityReplicaItem
{
    public EveId Id { get; }
    public int Index { get; }
    public int Amount { get; }
    public int Maximum { get; }
    public bool Active { get; }
    public double CooldownRemaining { get; }
    public double DurationRemaining { get; }
    public bool HasDuration { get; }
    public PNAbilityReplicaItem(EveId id, int index, int amount, int maximum, bool active, double cooldownRemaining, bool hasDuration, double durationRemaining)
    {
        if (!id.IsValid) throw new ArgumentException("An Ability id is required.", nameof(id));
        if (index < 0 || amount < 0 || maximum < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (double.IsNaN(cooldownRemaining) || double.IsInfinity(cooldownRemaining) || cooldownRemaining < 0 || double.IsNaN(durationRemaining) || double.IsInfinity(durationRemaining) || durationRemaining < 0)
            throw new ArgumentOutOfRangeException(nameof(cooldownRemaining), "Times must be finite and not negative.");
        Id = id; Index = index; Amount = amount; Maximum = maximum; Active = active; CooldownRemaining = cooldownRemaining; HasDuration = hasDuration; DurationRemaining = durationRemaining;
    }
}

public sealed partial class PNAbilityCollection
{
    // Authority side: every attached instance, transient ones included (a client ignores ids it does not have).
    public IReadOnlyList<PNAbilityReplicaItem> CaptureReplica()
    {
        var items = new List<PNAbilityReplicaItem>(); var seen = new Dictionary<EveId, int>();
        foreach (var ability in Instances)
        {
            seen.TryGetValue(ability.Id, out int index); seen[ability.Id] = index + 1; items.Add(ability.CaptureReplica(index));
        }
        return items;
    }

    // Client side: write replicated values into the instances that exist here. Refused on a machine that may mutate Abilities itself (the authority never applies a replica).
    // Returns how many instances were updated; ids or indices this machine does not have are skipped.
    public int ApplyReplica(IEnumerable<PNAbilityReplicaItem> items)
    {
        if (disposed) throw new ObjectDisposedException(nameof(PNAbilityCollection));
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (canMutate == null || canMutate()) throw new InvalidOperationException("An Ability replica applies only on a machine without gameplay authority.");
        var byId = Instances.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.ToArray()); int applied = 0;
        Batch(() =>
        {
            foreach (var item in items)
                if (byId.TryGetValue(item.Id, out var list) && item.Index < list.Length) { list[item.Index].ApplyReplica(item); applied++; }
        });
        return applied;
    }
}

public abstract partial class PNAbility
{
    internal bool IsReplica => owner != null && owner.IsReplica;
    internal abstract PNAbilityReplicaItem CaptureReplica(int index);
    internal abstract void ApplyReplica(PNAbilityReplicaItem item);
}

public sealed partial class PNAbilityCooldown
{
    internal override PNAbilityReplicaItem CaptureReplica(int index) => new PNAbilityReplicaItem(Id, index, current, maximum, recovering, remaining, false, 0);
    internal override void ApplyReplica(PNAbilityReplicaItem item)
    {
        var before = Snapshot(); maximum = Math.Max(1, item.Maximum); current = Math.Min(item.Amount, maximum);
        recovering = item.Active && current < maximum; remaining = recovering ? item.CooldownRemaining : 0;
        Changed(PNAbilityChangeReason.Refreshed, before);
    }
}

public sealed partial class PNAbilityCounter
{
    internal override PNAbilityReplicaItem CaptureReplica(int index)
    {
        var timed = DurationRemaining;
        return new PNAbilityReplicaItem(Id, index, current, maximum, false, 0, timed.HasValue, timed ?? 0);
    }
    internal override void ApplyReplica(PNAbilityReplicaItem item)
    {
        var before = Snapshot(); maximum = Math.Max(1, item.Maximum); grants.Clear(); current = 0;
        int amount = Math.Min(item.Amount, maximum);
        if (amount > 0) { grants.Add(new Grant { Id = checked(++nextGrant), Count = amount, Remaining = item.HasDuration ? item.DurationRemaining : (double?)null }); current = amount; }
        Changed(PNAbilityChangeReason.Refreshed, before);
    }
}

public sealed partial class PNAbilityState
{
    internal override PNAbilityReplicaItem CaptureReplica(int index) => new PNAbilityReplicaItem(Id, index, active ? 1 : 0, 1, active, cooldownRemaining, duration.HasValue, duration ?? 0);
    internal override void ApplyReplica(PNAbilityReplicaItem item)
    {
        var before = Snapshot(); active = item.Active; duration = active && item.HasDuration ? item.DurationRemaining : (double?)null; cooldownRemaining = item.CooldownRemaining;
        Changed(PNAbilityChangeReason.Refreshed, before);
    }
}
// EN: A guess the owner's client made about one Ability (Docs/SkillNetworkingAPlan.md S3, N1/N7): it spent a charge before the authority said so. Rolling it back restores what the replica held, less the time that passed.
// VI: Điều client chủ nhân đoán về một Ability: tiêu charge trước khi authority xác nhận. Hoàn tác trả lại giá trị replica đang giữ, trừ thời gian đã trôi.
public readonly struct PNAbilityPrediction
{
    private readonly PNAbilityCooldown ability; private readonly PNAbilityReplicaItem before; private readonly long at;
    internal PNAbilityPrediction(PNAbilityCooldown ability, PNAbilityReplicaItem before)
    { this.ability = ability; this.before = before; at = System.Diagnostics.Stopwatch.GetTimestamp(); }
    public bool IsValid => ability != null;
    // Puts the charge back (no-op for a default value). The authority's next block overwrites the Ability anyway; this is what the owner sees until then.
    public void Rollback()
    {
        if (ability == null) return;
        double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - at) / (double)System.Diagnostics.Stopwatch.Frequency;
        ability.RestorePrediction(before, elapsed);
    }
}

public sealed partial class PNAbilityCooldown
{
    // Client only (a replica): spends one charge as the authority will. False when this machine may mutate the Ability itself, or there is no charge.
    public bool TryPredictConsume(out PNAbilityPrediction prediction)
    {
        prediction = default;
        if (!IsAttached || !IsReplica || current <= 0) return false;
        var item = CaptureReplica(0); var snapshot = Snapshot(); current--; StartIfNeeded();
        Changed(PNAbilityChangeReason.Used, snapshot, 1); prediction = new PNAbilityPrediction(this, item); return true;
    }
    internal void RestorePrediction(PNAbilityReplicaItem item, double elapsed)
    {
        if (!IsAttached) return;
        var restored = new PNAbilityReplicaItem(item.Id, item.Index, item.Amount, item.Maximum, item.Active, Math.Max(0, item.CooldownRemaining - elapsed), false, 0);
        ApplyReplica(restored);
    }
}
