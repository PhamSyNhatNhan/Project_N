using System;
using EveEngine;

// EN: Route for modifiers that travel with a damage packet. The receiver applies them as its own for that hit (defense or a tag's Mitigation);
//     the sender never evaluates this route.
// VI: Đường tính cho modifier đi kèm gói damage. Bên nhận áp chúng như modifier của mình trong đòn đó (DEF hoặc Mitigation theo tag);
//     bên gây không bao giờ tính đường này.
public class PNPenetrationCalculation : PNStatCalculation
{
    public override void ValidateModifier(PNStatSet stats, EveStatModifier modifier)
    {
        var id = stats.ResolveIdentity(modifier.Id);
        if (id != PNContentIds.Defense && stats.IsBaseStatId(id))
            throw new ArgumentException("Penetration modifiers target defense or a damage tag/element/type, not stat " + id, nameof(modifier));
        ValidateGroupedContribution(stats, modifier);
    }

    public override double Calculate(PNStatCalculationContext context)
        => throw new InvalidOperationException("Penetration modifiers are applied by the damage receiver, not evaluated by the sender.");
}
