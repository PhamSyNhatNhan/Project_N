using EveEngine;
using Newtonsoft.Json.Linq;

// Registers the projectile registry ("projectiles"), the built-in implementations and the basic volley Operation/Action.
// Same shape as PNAreaGameplayContent.
public static class PNProjectileGameplayContent
{
    public static readonly EveId Directional = EveId.Parse("project_n:directional");
    public static readonly EveId Homing = EveId.Parse("project_n:homing");
    public static readonly EveId EveSkillHoming = EveId.Parse("project_n:eve_skill_homing");
    public static readonly EveId CreateProjectile = EveId.Parse("project_n:create_projectile");
    public static readonly EveId ProjectileAction = EveId.Parse("project_n:projectile_action");
    public static void Register(EveRegistryHub hub)
    {
        var projectiles = hub.GetOrCreate<PNProjectileDefinition>(); hub.NameRegistry<PNProjectileDefinition>("projectiles");
        var data = JObject.Parse("{'speed':12,'acceleration':0,'speedMin':0,'speedMax':40,'life':{'mode':'Time','value':3},'volume':{'size':[0.3,0.3,0.3]},'environmentMask':['Wall'],'hitLimit':1}");
        projectiles.Register(new PNProjectileDefinition(Directional, typeof(PNDirectionalProjectile), data, PNDirectionalSettings.ValidateDefinition, PNDirectionalSettings.ValidateInput));
        projectiles.RegisterBase(EveId.Parse("base:directional"), Directional, "project_n",
            compatible: (original, replacement) => original.ImplementationType.IsAssignableFrom(replacement.ImplementationType));
        var homing = (JObject)data.DeepClone(); homing["turnRate"] = 180; homing["homeAfter"] = 0.2; homing["lostTarget"] = "Reacquire"; homing["searchRadius"] = 10; homing["searchInterval"] = 0.2;
        projectiles.Register(new PNProjectileDefinition(Homing, typeof(PNHomingProjectile), homing, PNHomingSettings.ValidateDefinition, PNDirectionalSettings.ValidateInput));
        projectiles.RegisterBase(EveId.Parse("base:homing"), Homing, "project_n",
            compatible: (original, replacement) => original.ImplementationType.IsAssignableFrom(replacement.ImplementationType));
        // Eve's Skill volley: a homing projectile with a small glowing trail. Flies on in its last direction when no target remains (3.5 s).
        var eve = (JObject)data.DeepClone(); eve["speed"] = 7; eve["life"] = JObject.Parse("{'mode':'Time','value':3.5}"); eve["volume"] = JObject.Parse("{'size':[0.175,0.175,0.175]}");
        eve["turnRate"] = 540; eve["homeAfter"] = 0.25; eve["lostTarget"] = "Reacquire"; eve["searchRadius"] = 12; eve["searchInterval"] = 0.2;
        projectiles.Register(new PNProjectileDefinition(EveSkillHoming, typeof(PNTrailHomingProjectile), eve, PNHomingSettings.ValidateDefinition, PNDirectionalSettings.ValidateInput));
        projectiles.RegisterBase(EveId.Parse("base:eve_skill_homing"), EveSkillHoming, "project_n",
            compatible: (original, replacement) => original.ImplementationType.IsAssignableFrom(replacement.ImplementationType));
        hub.Operations().Register(new PNSkillOperationDefinition(CreateProjectile, typeof(PNCreateProjectileOperation), PNCreateProjectileOperation.Validate, PNSkillContextRequirements.Direction,
            (payload, context) => context.Require<PNProjectileDefinition>(payload["projectileId"], "projectileId")));
#if UNITY_5_3_OR_NEWER
        hub.Actions().Register(new PNSkillActionDefinition(ProjectileAction, typeof(PNProjectileAction),
            (action, registry) => PNProjectileAction.Validate(action.Perform, registry), PNSkillContextRequirements.Direction));
#endif
    }
}
