using System;
using EveEngine;

// EN: Applies incoming grouped modifiers without altering base stats or resource maxima.
// VI: Áp modifier theo nhóm cho giá trị nhận mà không đổi stat nền hoặc giới hạn resource.
public class PNMitigationCalculation : PNStatCalculation
{
    public override void ValidateModifier(PNStatSet stats, EveStatModifier modifier)
    {
        ValidateGroupedContribution(stats, modifier);
    }

    public override double Calculate(PNStatCalculationContext context) => context.ApplyGroups(true);
}
