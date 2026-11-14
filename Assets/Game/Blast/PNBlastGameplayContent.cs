using EveEngine;
using Newtonsoft.Json.Linq;

// The generic blasts and their create Operation. Concrete attacks (the Starweaver triangle, the Starreaper rain) register their own definitions in their content modules.
public static class PNBlastGameplayContent
{
    public static readonly EveId Cylinder = EveId.Parse("project_n:blast_cylinder"), Polygon = EveId.Parse("project_n:blast_polygon"), CreateBlast = EveId.Parse("project_n:create_blast");
    public static void Register(EveRegistryHub hub)
    {
        var instances = hub.Instances();
        instances.Register(new PNBlastDefinition(Cylinder, typeof(PNCylinderBlast), new JObject(), (token, registry) => PNBuffContentBatch.Fields(PNBuffContentBatch.Object(token)), input => PNCylinderBlast.Validate(input)));
        instances.Register(new PNBlastDefinition(Polygon, typeof(PNPolygonBlast), new JObject(), (token, registry) => PNBuffContentBatch.Fields(PNBuffContentBatch.Object(token)), input => PNPolygonBlast.Validate(input)));
        hub.Operations().Register(new PNSkillOperationDefinition(CreateBlast, typeof(PNCreateBlastOperation), PNCreateBlastOperation.Validate, PNSkillContextRequirements.Position));
    }
}
