using EveEngine;
using Newtonsoft.Json.Linq;

public static class PNAreaGameplayContent
{
    public static void Register(EveRegistryHub hub)
    {
        var areas = hub.GetOrCreate<PNAreaDefinition>(); hub.NameRegistry<PNAreaDefinition>("areas");
        var data = JObject.Parse("{'buffId':'base:timescale_reduction_area','pollPeriod':0.05,'fadeIn':0.1,'fadeOut':0.3}");
        data["visual"] = PNTimeScaleAreaVisualSettings.DefaultPayload();
        areas.Register(new PNAreaDefinition(EveId.Parse("project_n:timescale_area"), typeof(PNTimeScaleArea),
            data,
            PNTimeScaleAreaSettings.ValidateDefinition, input => { new PNTimeScaleAreaSettings(input); },
            (payload, context) => context.Require<PNBuffDefinition>(payload["buffId"], "buffId")));
        areas.RegisterBase(EveId.Parse("base:timescale_area"), EveId.Parse("project_n:timescale_area"), "project_n",
            compatible: (original, replacement) => original.ImplementationType.IsAssignableFrom(replacement.ImplementationType));
        hub.Operations().Register(new PNSkillOperationDefinition(EveId.Parse("project_n:create_area"), typeof(PNCreateAreaOperation),
            PNCreateAreaOperation.Validate, PNSkillContextRequirements.Position));
#if UNITY_5_3_OR_NEWER
        hub.Actions().Register(new PNSkillActionDefinition(EveId.Parse("project_n:area_action"), typeof(PNAreaAction),
            (data, registry) => PNAreaAction.Validate(data.Perform, registry), PNSkillContextRequirements.Position));
#endif
    }
}
