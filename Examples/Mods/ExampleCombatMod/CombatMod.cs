using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: This optional mod replaces scale through the supported registry contract.
// VI: Mod tùy chọn thay scale qua hợp đồng registry được hỗ trợ.
namespace ExampleCombatMod
{
    public sealed class CombatMod : EveCodeMod
    {
        public override void Register(EveModRegistrationContext context)
        {
            var implementation = new EveId(context.Namespace, "scale");
            context.Register(new PNStatCalculationDefinition(implementation, typeof(CombatScale)));
            context.Override<PNStatCalculationDefinition>(EveId.Parse("base:scale"), implementation);
        }
    }

    public sealed class CombatScale : PNStatCalculation
    {
        public override double Calculate(PNStatCalculationContext context)
            => context.ApplyGroups(true, context.Ids.Count == 1 && context.Ids[0] == PNContentIds.Defense);
    }

    // Optional caller examples; registration does not apply these to gameplay automatically.
    public static class AreaExamples
    {
        public static PNAreaReceipt CreateSlowField(PNAreaManager manager, PNStat caster, Vector3 position,
            float radius, double reduction, double minimumReduction, double duration, bool dependent, int layerMask)
            => manager.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"),
                new JObject { ["radius"] = radius, ["reduction"] = reduction, ["minimumReduction"] = minimumReduction,
                    ["duration"] = duration, ["dependent"] = dependent, ["layerMask"] = layerMask, ["layerMode"] = "Include" },
                PNBuffSource.Capture(caster), caster.gameObject.scene, position, caster));

        public static PNBuffReceipt ApplyTimedSlow(PNStat caster, PNStat target, double reduction, double minimumReduction, double duration)
            => target.GetComponent<PNBuffManager>().Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:timescale_reduction"),
                PNBuffSource.Capture(caster), new JObject { ["reduction"] = reduction, ["minimumReduction"] = minimumReduction, ["duration"] = duration }));
    }
}
