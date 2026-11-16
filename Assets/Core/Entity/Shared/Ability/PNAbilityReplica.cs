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
    public bool HasConfiguration { get; }
    public double BaseCooldown { get; }
    public double ReductionFlat { get; }
    public double ReductionPercent { get; }
    public bool AutoRecover { get; }
    public bool Transient { get; }
    private readonly IReadOnlyList<PNAbilityReplicaGrant> grants;
    public IReadOnlyList<PNAbilityReplicaGrant> Grants => grants ?? Array.Empty<PNAbilityReplicaGrant>();
    public double ActualCooldown => PNAbilityDefinition.CalculateCooldown(BaseCooldown, ReductionFlat, ReductionPercent);
    public PNAbilityReplicaItem(EveId id, int index, int amount, int maximum, bool active, double cooldownRemaining, bool hasDuration, double durationRemaining)
    {
        if (!id.IsValid) throw new ArgumentException("An Ability id is required.", nameof(id));
        if (index < 0 || amount < 0 || maximum < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (double.IsNaN(cooldownRemaining) || double.IsInfinity(cooldownRemaining) || cooldownRemaining < 0 || double.IsNaN(durationRemaining) || double.IsInfinity(durationRemaining) || durationRemaining < 0)
            throw new ArgumentOutOfRangeException(nameof(cooldownRemaining), "Times must be finite and not negative.");
        Id = id; Index = index; Amount = amount; Maximum = maximum; Active = active; CooldownRemaining = cooldownRemaining; HasDuration = hasDuration; DurationRemaining = durationRemaining;
        HasConfiguration = false; BaseCooldown = ReductionFlat = ReductionPercent = 0; AutoRecover = false; Transient = false; grants = null;
    }
    public PNAbilityReplicaItem(EveId id, int index, int amount, int maximum, bool active, double cooldownRemaining,
        bool hasDuration, double durationRemaining, double baseCooldown, double reductionFlat, double reductionPercent,
        bool autoRecover, IEnumerable<PNAbilityReplicaGrant> grants, bool transient = false)
        : this(id, index, amount, maximum, active, cooldownRemaining, hasDuration, durationRemaining)
    {
        PNAbilityDefinition.RequireSeconds(baseCooldown, true);
        PNAbilityDefinition.CalculateCooldown(baseCooldown, reductionFlat, reductionPercent);
        var copy = (grants ?? Array.Empty<PNAbilityReplicaGrant>()).ToArray();
        if (copy.Length > 256 || copy.Select(g => g.Id).Distinct().Count() != copy.Length || copy.Any(g => g.Id == 0 || g.Amount < 1))
            throw new ArgumentException("Bounded unique counter grants required.");
        HasConfiguration = true; BaseCooldown = baseCooldown; ReductionFlat = reductionFlat; ReductionPercent = reductionPercent;
        AutoRecover = autoRecover; Transient = transient; this.grants = Array.AsReadOnly(copy);
    }
    internal PNAbilityReplicaItem WithConfiguration(PNAbilityDefinition definition)
        => HasConfiguration ? this : new PNAbilityReplicaItem(Id, Index, Amount, Maximum, Active, CooldownRemaining,
            HasDuration, DurationRemaining, definition.Cooldown, 0, 0, definition.AutoRecover,
            definition.Kind == PNAbilityKind.Counter && Amount > 0 ? new[] { new PNAbilityReplicaGrant(1, Amount, HasDuration ? DurationRemaining : (double?)null) } : null);
    internal PNAbilityReplicaItem Copy(int amount, bool active, double remaining, bool timed, double duration, IEnumerable<PNAbilityReplicaGrant> values)
        => new PNAbilityReplicaItem(Id, Index, amount, Maximum, active, remaining, timed, duration,
            BaseCooldown, ReductionFlat, ReductionPercent, AutoRecover, values, Transient);
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
        if (!IsReplica) throw new InvalidOperationException("An Ability replica applies only on a machine without gameplay authority.");
        var copy = items.ToArray();
        if (predictionLayers.Count > 0) return ReplacePredictionTruth(copy);
        return ApplyReplicaCore(copy);
    }
    private int ApplyReplicaCore(IEnumerable<PNAbilityReplicaItem> items)
    {
        var byId = Instances.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.ToArray()); int applied = 0;
        Batch(() =>
        {
            foreach (var item in items)
                if (byId.TryGetValue(item.Id, out var list) && item.Index < list.Length) { list[item.Index].ApplyReplica(item); applied++; }
        });
        return applied;
    }
    private void SynchronizeReplicaShape(PNAbilityReplicaItem[] items)
    {
        var rows = items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.OrderBy(i => i.Index).ToArray());
        foreach (var pair in rows)
        {
            if (!definitions.TryGetValue(pair.Key, out var d)) continue;
            for (int i = 0; i < pair.Value.Length; i++)
                if (pair.Value[i].Index != i || i > 0 && d.Kind != PNAbilityKind.State) throw new FormatException("Ability instance order.");
        }
        foreach (var group in Instances.GroupBy(a => a.Id))
        {
            rows.TryGetValue(group.Key, out var wanted); int count = wanted?.Length ?? 0;
            foreach (var extra in group.Skip(count).ToArray())
            { var before = extra.Snapshot(); instances.Remove(extra.Handle); extra.Detach(); Notify(new PNAbilityChange(PNAbilityChangeReason.Removed, before, null)); }
        }
        foreach (var pair in rows)
        {
            if (!definitions.TryGetValue(pair.Key, out var d)) continue;
            int count = Find(pair.Key).Count;
            while (count < pair.Value.Length) { Create(d, pair.Value[count].Transient); count++; }
        }
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
    internal override PNAbilityReplicaItem CaptureReplica(int index) => new PNAbilityReplicaItem(Id, index, current, maximum, recovering, remaining, false, 0,
        baseCooldown, reduceFlat, reducePercent, autoRecover, null);
    internal override void ApplyReplica(PNAbilityReplicaItem item)
    {
        var before = Snapshot(); maximum = Math.Max(1, item.Maximum); current = Math.Min(item.Amount, maximum);
        recovering = item.Active && current < maximum; remaining = recovering ? item.CooldownRemaining : 0;
        if (item.HasConfiguration) { baseCooldown = item.BaseCooldown; reduceFlat = item.ReductionFlat; reducePercent = item.ReductionPercent; autoRecover = item.AutoRecover; }
        Changed(PNAbilityChangeReason.Refreshed, before);
    }
}

public sealed partial class PNAbilityCounter
{
    internal override PNAbilityReplicaItem CaptureReplica(int index)
    {
        var timed = DurationRemaining;
        return new PNAbilityReplicaItem(Id, index, current, maximum, false, 0, timed.HasValue, timed ?? 0,
            0, 0, 0, false, grants.Select(g => new PNAbilityReplicaGrant(g.Id, g.Count, g.Remaining)));
    }
    internal override void ApplyReplica(PNAbilityReplicaItem item)
    {
        var before = Snapshot(); maximum = Math.Max(1, item.Maximum); grants.Clear(); current = 0;
        int amount = Math.Min(item.Amount, maximum);
        if (item.HasConfiguration)
        {
            foreach (var g in item.Grants) { grants.Add(new Grant { Id = g.Id, Count = g.Amount, Remaining = g.Remaining }); current = checked(current + g.Amount); if (g.Id > 0) nextGrant = Math.Max(nextGrant, g.Id); }
        }
        else if (amount > 0) { grants.Add(new Grant { Id = checked(++nextGrant), Count = amount, Remaining = item.HasDuration ? item.DurationRemaining : (double?)null }); current = amount; }
        Changed(PNAbilityChangeReason.Refreshed, before);
    }
}

public sealed partial class PNAbilityState
{
    internal override PNAbilityReplicaItem CaptureReplica(int index) => new PNAbilityReplicaItem(Id, index, active ? 1 : 0, 1, active, cooldownRemaining, duration.HasValue, duration ?? 0,
        cooldown, reduceFlat, reducePercent, false, null, Transient);
    internal override void ApplyReplica(PNAbilityReplicaItem item)
    {
        var before = Snapshot(); active = item.Active; duration = active && item.HasDuration ? item.DurationRemaining : (double?)null; cooldownRemaining = item.CooldownRemaining;
        if (item.HasConfiguration) { cooldown = item.BaseCooldown; reduceFlat = item.ReductionFlat; reducePercent = item.ReductionPercent; }
        Changed(PNAbilityChangeReason.Refreshed, before);
    }
}
// EN: A guess the owner's client made about one Ability (Docs/SkillNetworkingAPlan.md S3, N1/N7): it spent a charge before the authority said so. Rolling it back restores what the replica held, less the time that passed.
// VI: Điều client chủ nhân đoán về một Ability: tiêu charge trước khi authority xác nhận. Hoàn tác trả lại giá trị replica đang giữ, trừ thời gian đã trôi.
public readonly struct PNAbilityPrediction
{
    private readonly PNAbilityPredictionScope scope;
    internal PNAbilityPrediction(PNAbilityPredictionScope scope) { this.scope = scope; }
    public bool IsValid => scope != null;
    // Puts the charge back (no-op for a default value). The authority's next block overwrites the Ability anyway; this is what the owner sees until then.
    public void Rollback()
    {
        scope?.Rollback();
    }
}

public sealed partial class PNAbilityCooldown
{
    // Client only (a replica): spends one charge as the authority will. False when this machine may mutate the Ability itself, or there is no charge.
    public bool TryPredictConsume(out PNAbilityPrediction prediction)
    {
        prediction = default;
        if (!IsAttached || !IsReplica || current <= 0) return false;
        var scope = Owner.BeginPrediction();
        try { if (!TryConsume()) { scope.Rollback(); return false; } prediction = new PNAbilityPrediction(scope); return true; }
        finally { scope.End(); }
    }
}
