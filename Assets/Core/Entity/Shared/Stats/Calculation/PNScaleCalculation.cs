// EN: Base-backed flats precede grouped multipliers; non-stat damage flats follow them.
// VI: Flat base/resource cộng trước hệ số nhóm; flat damage ngoài stat cộng sau.
public class PNScaleCalculation : PNStatCalculation
{
    public override void ValidateModifier(PNStatSet stats, EveEngine.EveStatModifier modifier)
        => ValidateGroupedContribution(stats, modifier);
    public override double Calculate(PNStatCalculationContext context)
        => context.ApplyScaleGroups(context.Ids.Count == 1 &&
            (context.Ids[0] == PNContentIds.Defense || context.Ids[0] == PNContentIds.CritRate ||
             context.Ids[0] == PNContentIds.CooldownReduction));
}
