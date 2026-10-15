using System;

// EN: Signed defense reduction is bounded by atan; K=500 gives 50 percent reduction at effective DEF=500.
// VI: Hệ số giảm DEF có dấu được giới hạn bằng atan; K=500 giảm 50 phần trăm tại DEF hiệu dụng=500.
public class PNDamageCalculation
{
    public const double DefenseConstant = 500d;

    public virtual double CalculateDefenseCoefficient(double defense, double defenseFactor = 1d)
    {
        if (double.IsNaN(defense) || double.IsInfinity(defense))
            throw new ArgumentOutOfRangeException(nameof(defense));
        if (double.IsNaN(defenseFactor) || double.IsInfinity(defenseFactor) || defenseFactor < 0d)
            throw new ArgumentOutOfRangeException(nameof(defenseFactor));
        double effectiveDefense = (double)defense * defenseFactor;
        return (2d / Math.PI) * Math.Atan(effectiveDefense / DefenseConstant);
    }

    public virtual double Calculate(double damage, double defense, PNDamageTypeDefinition damageType)
    {
        if (damageType == null) throw new ArgumentNullException(nameof(damageType));
        if (double.IsNaN(damage) || double.IsInfinity(damage) || damage < 0d)
            throw new ArgumentOutOfRangeException(nameof(damage));
        double coefficient = CalculateDefenseCoefficient(defense, damageType.DefenseFactor);
        double result = damage - damage * coefficient;
        if (double.IsNaN(result) || double.IsInfinity(result) || result > double.MaxValue)
            throw new OverflowException("Damage exceeded the double range.");
        return (double)result;
    }
}
