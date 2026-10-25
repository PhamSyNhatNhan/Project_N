using EveEngine;
using Newtonsoft.Json.Linq;

// Registers what every summon skill shares (Docs/SummonPlan.md): the create_summon Operation, the summon action and the summon_cast skill behavior.
// What a particular summon is (character, skill, behavior) is registered by its own content.
public static class PNSummonGameplayContent
{
    public static readonly EveId CreateSummon = EveId.Parse("project_n:create_summon");
    public static readonly EveId SummonAction = EveId.Parse("project_n:summon_action");
    public static readonly EveId CastBehavior = EveId.Parse("project_n:summon_cast");
    // Eve 2's pet (placeholder content): its character, the skill data in Eve2Character.json, the Skill-slot profile and its cooldown Ability.
    public static readonly EveId Eve2 = EveId.Parse("project_n:eve_2");
    public static readonly EveId Eve2PetSkill = EveId.Parse("project_n:eve2_pet_skill");
    public static readonly EveId Eve2PetProfile = EveId.Parse("project_n:binding_eve2_pet");
    public static readonly EveId Eve2PetReady = EveId.Parse("project_n:eve2_pet_ready");
    public static readonly EveId Eve2PetShot = EveId.Parse("project_n:eve2_pet_shot");
    public static void Register(EveRegistryHub hub)
    {
        hub.Operations().Register(new PNSkillOperationDefinition(CreateSummon, typeof(PNCreateSummonOperation), PNCreateSummonOperation.Validate, PNSkillContextRequirements.None,
            PNCreateSummonOperation.ValidateReferences));
        hub.Actions().Register(new PNSkillActionDefinition(SummonAction, typeof(PNSummonAction), (action, registry) => PNSummonAction.Validate(action.Perform, registry), PNSkillContextRequirements.None,
            (data, context) => { var obj = PNBuffContentBatch.Object(data); context.Require<PNSkillOperationDefinition>(obj["operationId"], "operationId"); context.Registry.Operations().Get(context.Read(obj["operationId"], "operationId")).ValidateReferences(obj["data"], context); }));
        hub.Get<PNSkillBehaviorDefinition>().Register(new PNSkillBehaviorDefinition(CastBehavior, CastBehavior, c => new PNSummonCastBehavior(c), PNSummonCastBehavior.Validate, PNSummonCastBehavior.ValidateReferences));
    }

    // Skill-slot binding of Eve 2: the Skill slot calls the pet (8 s cooldown, shown in the Ability UI) and Dash stays the shared Dash. Runs when the content is
    // finalized, after the characters (and so the skill data in their JSON) exist. Attack, Ultimate and Burst stay unmanaged (the empty legacy hooks).
    public static void RegisterBindings(EveRegistryHub hub)
    {
        if (!hub.Characters.TryGet(Eve2, out _)) return;
        var cooldown = new PNAbilitySettings { Id = Eve2PetReady.ToString(), Kind = PNAbilityKind.Cooldown, Clock = PNAbilityClock.Game, ShowInUI = true, Maximum = 1, InitialAmount = 1, AutoRecover = true, Cooldown = 8d, Recovery = PNAbilityRecovery.PerStack }.Create();
        var data = new JObject { ["cooldownAbilityId"] = Eve2PetReady.ToString(), ["actionDataId"] = Eve2PetSkill.ToString() };
        hub.Get<PNSkillBindingProfile>().Register(new PNSkillBindingProfile(Eve2PetProfile, Eve2PetSkill, CastBehavior, data, new PlayerSkillInputConfig { PressImmediately = true },
            actionDataIds: new[] { Eve2PetSkill }, abilities: new[] { cooldown }));
        if (!hub.Get<PNSkillBaseBindings>().TryGet(Eve2, out _))
            hub.Get<PNSkillBaseBindings>().Register(new PNSkillBaseBindings(Eve2, new[]
            {
                new System.Collections.Generic.KeyValuePair<EveId, EveId>(PNSkillSlots.Skill, Eve2PetProfile),
                new System.Collections.Generic.KeyValuePair<EveId, EveId>(PNSkillSlots.Dash, PNGameSkillBindings.SharedDash)
            }));
    }
}
