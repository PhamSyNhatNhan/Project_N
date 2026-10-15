using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

// EN: Immutable inputs for one calculation; modifiers are already filtered by calculation and selected IDs.
// VI: Đầu vào bất biến của một phép tính; modifier đã lọc theo đường tính và ID được chọn.
public class PNStatCalculationContext
{
    public PNStatSet Stats { get; }
    public EveId CalculationId { get; }
    public double BaseValue { get; }
    public IReadOnlyList<EveId> Ids { get; }
    public IReadOnlyList<EveStatModifier> Modifiers { get; }

    public PNStatCalculationContext(PNStatSet stats, EveId calculationId, double baseValue,
        IEnumerable<EveId> ids, IEnumerable<EveStatModifier> modifiers)
    {
        Stats = stats ?? throw new ArgumentNullException(nameof(stats));
        if (!calculationId.IsValid) throw new ArgumentException("Calculation ID is required.", nameof(calculationId));
        if (double.IsNaN(baseValue) || double.IsInfinity(baseValue)) throw new ArgumentOutOfRangeException(nameof(baseValue));
        CalculationId = calculationId;
        BaseValue = baseValue;
        Ids = Array.AsReadOnly((ids ?? throw new ArgumentNullException(nameof(ids))).ToArray());
        Modifiers = Array.AsReadOnly((modifiers ?? throw new ArgumentNullException(nameof(modifiers))).ToArray());
    }
    internal PNStatCalculationContext(PNStatSet stats, EveId calculationId, double baseValue,
        IReadOnlyList<EveId> ids, IReadOnlyList<EveStatModifier> modifiers, bool owned)
    {
        Stats = stats; CalculationId = calculationId; BaseValue = baseValue;
        Ids = ids; Modifiers = modifiers;
    }

    // Calculators own the formula. Registry definitions select group-0 placement only.
    public virtual double ApplyGroups(bool includeFlat, bool allowNegative = false)
        => ApplyGroupedValues(includeFlat, allowNegative);

    // Kept as a public helper; default group0 is after for stats and tags alike.
    public virtual double ApplyScaleGroups(bool allowNegative = false)
        => ApplyGroupedValues(true, allowNegative);

    private double ApplyGroupedValues(bool includeFlat, bool allowNegative)
    {
        double value = BaseValue;
        double lateFlat = 0d;
        var groups = new SortedDictionary<int, double>();
        foreach (var modifier in Modifiers)
        {
            if (modifier.Group == 0)
            {
                if (!includeFlat) throw new InvalidOperationException("This calculation does not accept group 0.");
                var contribution = Stats.ResolveContributionId(modifier);
                if (contribution == PNContentIds.Before) value += modifier.Value;
                else if (contribution == PNContentIds.After) lateFlat += modifier.Value;
                else throw new ArgumentException("Grouped calculator does not support contribution " + contribution);
            }
            else
            {
                groups.TryGetValue(modifier.Group, out double total);
                groups[modifier.Group] = total + modifier.Value;
            }
        }
        foreach (var group in groups) value *= Stats.ResolveCalculationMultiplier(group.Key, group.Value);
        value += lateFlat;
        if (double.IsNaN(value) || double.IsInfinity(value) || value > double.MaxValue || value < -double.MaxValue)
            throw new OverflowException("Calculation exceeded the double range.");
        return allowNegative ? (double)value : Math.Max(0d, (double)value);
    }
}

// EN: Code modules register new calculation implementations; they need not use the default group formula.
// VI: Module code đăng ký triển khai tính mới; không bắt buộc dùng công thức nhóm mặc định.
public abstract class PNStatCalculation
{
    public virtual void ValidateModifier(PNStatSet stats, EveStatModifier modifier) { }
    protected void ValidateGroupedContribution(PNStatSet stats, EveStatModifier modifier)
    {
        if (modifier.Group != 0) return;
        var id = stats.ResolveContributionId(modifier);
        if (id != PNContentIds.Before && id != PNContentIds.After)
            throw new ArgumentException("Calculator does not support contribution " + id, nameof(modifier));
    }
    public abstract double Calculate(PNStatCalculationContext context);
}
