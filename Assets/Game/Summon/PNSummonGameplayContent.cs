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
    public static readonly EveId Eve2PetAttack = EveId.Parse("project_n:eve2_pet_attack");
    public static void Register(EveRegistryHub hub)
    {
        hub.Operations().Register(new PNSkillOperationDefinition(CreateSummon, typeof(PNCreateSummonOperation), PNCreateSummonOperation.Validate, PNSkillContextRequirements.None,
            PNCreateSummonOperation.ValidateReferences));
        hub.Actions().Register(new PNSkillActionDefinition(SummonAction, typeof(PNSummonAction), (action, registry) => PNSummonAction.Validate(action.Perform, registry), PNSkillContextRequirements.None,
            (data, context) => { var obj = PNBuffContentBatch.Object(data); context.Require<PNSkillOperationDefinition>(obj["operationId"], "operationId"); context.Registry.Operations().Get(context.Read(obj["operationId"], "operationId")).ValidateReferences(obj["data"], context); }).NoView());
        // The pet's shot: a registered skill of the pet (the character `project_n:eve2_pet`), a plain projectile_action over create_projectile. When and where it shoots is the pet block
        // (interval, aim stop, search radius); the damage is a flat 10, scaled by the modifiers the pet inherited from its owner (magic, projectile_damage, summon_damage). Unity builds only.
#if UNITY_5_3_OR_NEWER
        hub.GetOrCreate<EveSkillDefinition>().Register(new EveSkillDefinition(Eve2PetAttack, JObject.Parse(@"{
          'categoryIds':['base:attack'],'affectedByAttackSpeed':false,'skill':{},
          'action':{'id':'project_n:projectile_action','start':{},'perform':{'operationId':'base:create_projectile','data':{
            'projectileId':'project_n:eve2_pet_shot','input':{},'count':1,'spread':0,
            'damage':{'value':{'amount':10},'canCrit':true,'tags':['base:magic','project_n:summon_damage']}}},'exit':{},'cancel':{}}}")));
#endif
        hub.Get<PNSkillBehaviorDefinition>().Register(new PNSkillBehaviorDefinition(CastBehavior, CastBehavior, c => new PNSummonCastBehavior(c), PNSummonCastBehavior.Validate, PNSummonCastBehavior.ValidateReferences));
        hub.Get<PNSkillBehaviorDefinition>().Register(new PNSkillBehaviorDefinition(TargetStrikeBehavior, TargetStrikeBehavior, c => new PNTargetStrikeBehavior(c), PNTargetStrikeBehavior.Validate, PNTargetStrikeBehavior.ValidateReferences));
    }
    // G7 trial character: Eve 2's Attack slot is a slash on the nearest enemy in reach (one decision point, no network code).
    public static readonly EveId TargetStrikeBehavior = EveId.Parse("project_n:target_strike");
    public static readonly EveId Eve2SlashSkill = EveId.Parse("project_n:eve2_slash_skill");
    public static readonly EveId Eve2SlashProfile = EveId.Parse("project_n:binding_eve2_slash");
    public static readonly EveId Eve2SlashReady = EveId.Parse("project_n:eve2_slash_ready");

    // Skill-slot binding of Eve 2: the Skill slot calls the pet (8 s cooldown, shown in the Ability UI) and Dash stays the shared Dash. Runs when the content is
    // finalized, after the characters (and so the skill data in their JSON) exist. Attack, Ultimate and Burst stay unmanaged (the empty legacy hooks).
    public static void RegisterBindings(EveRegistryHub hub)
    {
        if (!hub.Characters.TryGet(Eve2, out _)) return;
        var cooldown = new PNAbilitySettings { Id = Eve2PetReady.ToString(), Kind = PNAbilityKind.Cooldown, Clock = PNAbilityClock.Game, ShowInUI = true, Maximum = 1, InitialAmount = 1, AutoRecover = true, Cooldown = 8d, Recovery = PNAbilityRecovery.PerStack }.Create();
        var data = new JObject { ["cooldownAbilityId"] = Eve2PetReady.ToString(), ["actionDataId"] = Eve2PetSkill.ToString() };
        hub.Get<PNSkillBindingProfile>().Register(new PNSkillBindingProfile(Eve2PetProfile, Eve2PetSkill, CastBehavior, data, new PlayerSkillInputConfig { PressImmediately = true },
            actionDataIds: new[] { Eve2PetSkill }, abilities: new[] { cooldown }));
        var slashCooldown = new PNAbilitySettings { Id = Eve2SlashReady.ToString(), Kind = PNAbilityKind.Cooldown, Clock = PNAbilityClock.Game, ShowInUI = true, Maximum = 1, InitialAmount = 1, AutoRecover = true, Cooldown = .6d, Recovery = PNAbilityRecovery.PerStack }.Create();
        var slashData = new JObject { ["cooldownAbilityId"] = Eve2SlashReady.ToString(), ["actionDataId"] = Eve2SlashSkill.ToString(), ["reach"] = 3 };
        hub.Get<PNSkillBindingProfile>().Register(new PNSkillBindingProfile(Eve2SlashProfile, Eve2SlashSkill, TargetStrikeBehavior, slashData, new PlayerSkillInputConfig { PressImmediately = true },
            actionDataIds: new[] { Eve2SlashSkill }, abilities: new[] { slashCooldown }));
        if (!hub.Get<PNSkillBaseBindings>().TryGet(Eve2, out _))
            hub.Get<PNSkillBaseBindings>().Register(new PNSkillBaseBindings(Eve2, new[]
            {
                new System.Collections.Generic.KeyValuePair<EveId, EveId>(PNSkillSlots.Attack, Eve2SlashProfile),
                new System.Collections.Generic.KeyValuePair<EveId, EveId>(PNSkillSlots.Skill, Eve2PetProfile),
                new System.Collections.Generic.KeyValuePair<EveId, EveId>(PNSkillSlots.Dash, PNGameSkillBindings.SharedDash)
            }));
    }
}
