using EveEngine;
using Newtonsoft.Json.Linq;

public static class PNAreaTraits
{
    // Only one area carrying it per world instance; a later one ends the earlier with `Replaced`.
    public static readonly EveId Solizeitastronhringur = EveId.Parse("project_n:solizeitastronhringur");
}

public static class PNAreaGameplayContent
{
    public static void Register(EveRegistryHub hub)
    {
        // Area traits (IL1b): registered before the areas, since an area names its traits.
        hub.GetOrCreate<PNAreaTraitDefinition>(); hub.NameRegistry<PNAreaTraitDefinition>("area_traits");
        hub.AreaTraits().Register(new PNAreaTraitDefinition(PNAreaTraits.Solizeitastronhringur, typeof(PNSolizeitastronhringurTrait), PNAreaTraitDefinition.NoPayload));
        var areas = hub.Instances();
        var data = JObject.Parse("{'buffId':'base:timescale_reduction_area','pollPeriod':0.05,'fadeIn':0.1,'fadeOut':0.3}");
        data["visual"] = PNTimeScaleAreaVisualSettings.DefaultPayload();
        areas.Register(new PNAreaDefinition(EveId.Parse("project_n:timescale_area"), typeof(PNTimeScaleArea),
            data,
            PNTimeScaleAreaSettings.ValidateDefinition, input => { new PNTimeScaleAreaSettings(input); },
            (payload, context) => context.Require<PNBuffDefinition>(payload["buffId"], "buffId")));
        areas.RegisterBase<PNAreaDefinition>(EveId.Parse("base:timescale_area"), EveId.Parse("project_n:timescale_area"), "project_n",
            compatible: (original, replacement) => original.ImplementationType.IsAssignableFrom(replacement.ImplementationType));
        hub.Operations().Register(new PNSkillOperationDefinition(EveId.Parse("project_n:create_area"), typeof(PNCreateAreaOperation),
            PNCreateAreaOperation.Validate, PNSkillContextRequirements.Position));
#if UNITY_5_3_OR_NEWER
        hub.Actions().Register(new PNSkillActionDefinition(EveId.Parse("project_n:area_action"), typeof(PNAreaAction),
            (data, registry) => PNAreaAction.Validate(data.Perform, registry), PNSkillContextRequirements.Position).NoView());
#endif
    }
}
