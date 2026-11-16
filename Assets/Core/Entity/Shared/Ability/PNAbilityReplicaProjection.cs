using System;
using System.Collections.Generic;
using System.Linq;

// Passive state arithmetic only. Neither projection nor undo executes gameplay.
public readonly struct PNAbilityReplicaGrant
{
    public readonly long Id;
    public readonly int Amount;
    public readonly double? Remaining;
    public PNAbilityReplicaGrant(long id, int amount, double? remaining)
    {
        if (id == 0 || amount < 1) throw new ArgumentOutOfRangeException(nameof(amount));
        if (remaining.HasValue) PNAbilityDefinition.RequireSeconds(remaining.Value, true);
        Id = id; Amount = amount; Remaining = remaining;
    }
}

public static class PNAbilityReplicaProjection
{
    public static void Validate(PNAbilityReplicaItem item, PNAbilityDefinition definition)
    {
        if (item.Maximum < 1 || item.Amount > item.Maximum) throw new FormatException("Ability amount.");
        if (definition.Kind != PNAbilityKind.State && (item.Index != 0 || item.Transient)) throw new FormatException("Singleton Ability instance.");
        if (definition.Kind == PNAbilityKind.Counter && item.HasConfiguration && (item.Grants.Sum(g => (long)g.Amount) != item.Amount || item.Active || item.CooldownRemaining != 0)) throw new FormatException("Counter grant state.");
        if (definition.Kind == PNAbilityKind.Cooldown && (item.HasDuration || item.Grants.Count > 0 || item.Transient || item.Active && (item.Amount == item.Maximum || item.CooldownRemaining <= 0))) throw new FormatException("Cooldown state.");
        if (definition.Kind == PNAbilityKind.State && (item.Grants.Count > 0 || item.Maximum != 1 || item.HasDuration && !item.Active)) throw new FormatException("State image.");
        if (definition.Kind == PNAbilityKind.State && item.Amount != (item.Active ? 1 : 0)) throw new FormatException("State flag/amount.");
    }
    public static PNAbilityReplicaItem Advance(PNAbilityReplicaItem item, PNAbilityDefinition definition, double elapsed)
    {
        PNAbilityDefinition.RequireSeconds(elapsed, true);
        var value = item.WithConfiguration(definition);
        Validate(value, definition);
        int amount = value.Amount; bool active = value.Active, timed = value.HasDuration;
        double remaining = value.CooldownRemaining, duration = value.DurationRemaining;
        var grants = value.Grants;
        if (definition.Kind == PNAbilityKind.Cooldown && active && elapsed >= remaining)
        {
            double surplus = elapsed - remaining, period = value.ActualCooldown;
            if (definition.Recovery == PNAbilityRecovery.AllAtOnce || period == 0) amount = value.Maximum;
            else amount += (int)Math.Min(value.Maximum - amount, 1 + Math.Floor(surplus / period));
            active = amount < value.Maximum;
            remaining = active ? period - surplus % period : 0;
        }
        else if (definition.Kind == PNAbilityKind.Cooldown && active) remaining -= elapsed;
        else if (definition.Kind == PNAbilityKind.State)
        {
            if (active && timed && duration <= elapsed)
            {
                double after = elapsed - duration;
                remaining = definition.CooldownStart == PNAbilityCooldownStart.OnEnd
                    ? Math.Max(0, value.ActualCooldown - after) : Math.Max(0, remaining - elapsed);
                active = timed = false; duration = 0; amount = 0;
            }
            else { remaining = Math.Max(0, remaining - elapsed); if (active && timed) duration -= elapsed; }
        }
        else if (definition.Kind == PNAbilityKind.Counter)
        {
            grants = grants.Where(g => !g.Remaining.HasValue || g.Remaining.Value > elapsed)
                .Select(g => new PNAbilityReplicaGrant(g.Id, g.Amount, g.Remaining.HasValue ? g.Remaining - elapsed : null)).ToArray();
            amount = grants.Sum(g => g.Amount);
            double? next = grants.Where(g => g.Remaining.HasValue).Select(g => g.Remaining).OrderBy(t => t).FirstOrDefault();
            timed = next.HasValue; duration = next ?? 0;
        }
        return value.Copy(amount, active, remaining, timed, duration, grants);
    }

    // Compose one successful passive write over newer authority state. Age both sides
    // with the same ability clock first; expired guesses cannot invent charges/state.
    internal static PNAbilityReplicaItem Overlay(PNAbilityReplicaItem truth, PNAbilityReplicaItem before,
        PNAbilityReplicaItem after, PNAbilityDefinition definition, double age, bool firstWrite)
    {
        truth = truth.WithConfiguration(definition);
        // If an earlier speculative spend was removed, this spend may now be the
        // first one in a full queue. Age its own period instead of inheriting debt
        // from the refused cast that originally preceded it.
        if (firstWrite && definition.Kind == PNAbilityKind.Cooldown && !truth.Active && truth.Amount == truth.Maximum
            && before.Amount - after.Amount == 1 && before.Maximum == after.Maximum && after.AutoRecover
            && (definition.Recovery == PNAbilityRecovery.PerStack || truth.Maximum == 1))
        {
            var spent = truth.Copy(truth.Amount - 1, true, after.ActualCooldown, false, 0, null);
            return Advance(spent, definition, age);
        }
        before = Advance(before, definition, age); after = Advance(after, definition, age);
        int maximum = before.Maximum != after.Maximum ? after.Maximum : truth.Maximum;
        double baseCd = before.BaseCooldown != after.BaseCooldown ? after.BaseCooldown : truth.BaseCooldown;
        double flat = before.ReductionFlat != after.ReductionFlat ? after.ReductionFlat : truth.ReductionFlat;
        double percent = before.ReductionPercent != after.ReductionPercent ? after.ReductionPercent : truth.ReductionPercent;
        bool auto = before.AutoRecover != after.AutoRecover ? after.AutoRecover : truth.AutoRecover;
        int amount = (int)Math.Max(0, Math.Min(maximum, (long)truth.Amount + after.Amount - before.Amount));
        bool active = truth.Active, timed = truth.HasDuration;
        double remaining = truth.CooldownRemaining, duration = truth.DurationRemaining;
        var grants = truth.Grants;
        if (definition.Kind == PNAbilityKind.Counter)
        {
            var merged = truth.Grants.ToDictionary(g => g.Id);
            var old = before.Grants.ToDictionary(g => g.Id);
            var next = after.Grants.ToDictionary(g => g.Id);
            foreach (long id in old.Keys.Union(next.Keys))
            {
                old.TryGetValue(id, out var b); next.TryGetValue(id, out var a); merged.TryGetValue(id, out var t);
                int count = Math.Max(0, t.Amount + a.Amount - b.Amount);
                if (count == 0) merged.Remove(id);
                else merged[id] = new PNAbilityReplicaGrant(id, count, b.Remaining != a.Remaining ? a.Remaining : t.Amount > 0 ? t.Remaining : a.Remaining);
            }
            // A changed authority image may already have retired a consumed group.
            // Clamp aggregate quantities; never manufacture missing expired groups.
            var list = merged.Values.OrderBy(g => g.Id).ToList();
            int total = list.Sum(g => g.Amount), remove = Math.Max(0, total - amount);
            foreach (var g in list.OrderBy(g => g.Remaining ?? double.PositiveInfinity).ThenBy(g => g.Id).ToArray())
            {
                int n = Math.Min(remove, g.Amount); remove -= n;
                int index = list.FindIndex(x => x.Id == g.Id);
                if (n == g.Amount) list.RemoveAt(index);
                else if (n > 0) list[index] = new PNAbilityReplicaGrant(g.Id, g.Amount - n, g.Remaining);
            }
            grants = list.ToArray(); amount = grants.Sum(g => g.Amount);
            double? nextEnd = grants.Where(g => g.Remaining.HasValue).Select(g => g.Remaining).OrderBy(x => x).FirstOrDefault();
            timed = nextEnd.HasValue; duration = nextEnd ?? 0;
        }
        else if (definition.Kind == PNAbilityKind.Cooldown)
        {
            if (amount == maximum || !auto) { active = false; remaining = 0; }
            else if (!active || before.CooldownRemaining != after.CooldownRemaining || before.Active != after.Active)
            { active = after.Active; remaining = active ? after.CooldownRemaining : 0; }
        }
        else
        {
            if (before.Active != after.Active || before.HasDuration != after.HasDuration || before.DurationRemaining != after.DurationRemaining)
            { active = after.Active; timed = after.HasDuration; duration = after.DurationRemaining; }
            if (before.CooldownRemaining != after.CooldownRemaining) remaining = after.CooldownRemaining;
            amount = active ? 1 : 0;
        }
        return new PNAbilityReplicaItem(truth.Id, truth.Index, amount, maximum, active, remaining, timed, duration,
            baseCd, flat, percent, auto, grants, truth.Transient);
    }
}
