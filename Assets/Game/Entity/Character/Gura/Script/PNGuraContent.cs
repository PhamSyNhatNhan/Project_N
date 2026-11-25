using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Registered content of Gura (Docs/GuraCharacter.md). The character and its skill data live in BaseContent/characters/GuraCharacter.json; this registers what that data names: the combo strike Action,
//     the attack behavior and the slot bindings. Numbers are in the JSON (D7: tentative values). Nothing here knows the network: the behaviors run the same code on every machine.
// VI: Noi dung dang ky cua Gura: Action chem combo, behavior danh thuong va binding cac o. So lieu o JSON.
public static class PNGuraContent
{
    public static readonly EveId Character = EveId.Parse("project_n:gura");
    public static readonly EveId Moveset = EveId.Parse("project_n:gura_moveset");
    public static readonly EveId DashSkill = EveId.Parse("project_n:gura_dash");
    public static readonly EveId DashProfile = EveId.Parse("project_n:binding_gura_dash");
    public static readonly EveId AttackProfile = EveId.Parse("project_n:binding_gura_attack");
    public static readonly EveId AttackBehavior = EveId.Parse("project_n:gura_attack");
    public static readonly EveId ComboStrikeAction = EveId.Parse("project_n:combo_strike_action");
    public static readonly EveId ComboStep = EveId.Parse("project_n:gura_combo_step"), ComboWindow = EveId.Parse("project_n:gura_combo_window"),
        NextFour = EveId.Parse("project_n:gura_next_four"), WaterCoat = EveId.Parse("project_n:gura_water_coat");
    public static readonly EveId SkillProfile = EveId.Parse("project_n:binding_gura_skill"), SkillBehavior = EveId.Parse("project_n:gura_cast"),
        TridentSkill = EveId.Parse("project_n:gura_trident"), TridentAction = EveId.Parse("project_n:gura_trident_action"), TridentProjectile = EveId.Parse("project_n:gura_trident"),
        SkillReady = EveId.Parse("project_n:gura_skill_ready");
    public static readonly EveId[] AttackSkills = {
        EveId.Parse("project_n:gura_attack_1"), EveId.Parse("project_n:gura_attack_2"), EveId.Parse("project_n:gura_attack_3"), EveId.Parse("project_n:gura_attack_4") };

    // The Actions and behaviors (content session startup; shared definitions only, no entity state).
    public static void Register(EveRegistryHub hub)
    {
        var action = new PNSkillActionDefinition(ComboStrikeAction, typeof(PNComboStrikeAction), (a, h) => PNComboStrikeAction.Validate(a, h), PNSkillContextRequirements.Direction).NoView();
        hub.Actions().Register(action);
        // The trident: Eve-like homing, one hit ends it; the Action blasts where it ends (the projectile itself deals no damage).
        var trident = JObject.Parse("{'speed':14,'acceleration':0,'speedMin':0,'speedMax':40,'life':{'mode':'Time','value':1.6},'volume':{'size':[0.3,0.3,0.3]},'environmentMask':['Wall'],'hitLimit':1,'turnRate':720,'homeAfter':0,'lostTarget':'Reacquire','searchRadius':10,'searchInterval':0.2}");
        hub.Instances().Register(new PNProjectileDefinition(TridentProjectile, typeof(PNHomingProjectile), trident, PNHomingSettings.ValidateDefinition, PNDirectionalSettings.ValidateInput));
        hub.Actions().Register(new PNSkillActionDefinition(TridentAction, typeof(PNGuraTridentAction), (a, h) => PNGuraTridentAction.Validate(a, h), PNSkillContextRequirements.Direction).NoView());
        hub.Get<PNSkillBehaviorDefinition>().Register(new PNSkillBehaviorDefinition(SkillBehavior, SkillBehavior, c => new GuraCastBehavior(c), GuraCastBehavior.Validate, GuraCastBehavior.ValidateReferences));
        hub.Get<PNSkillBehaviorDefinition>().Register(new PNSkillBehaviorDefinition(AttackBehavior, AttackBehavior, c => new GuraAttackBehavior(c), GuraAttackBehavior.Validate, GuraAttackBehavior.ValidateReferences));
    }

    // The slot bindings, once the characters (and so the skill data in their JSON) exist. Attack and Dash are Gura's; the other slots join with the later stages.
    public static void RegisterBindings(EveRegistryHub hub)
    {
        if (!hub.Characters.TryGet(Character, out _)) return;
        var steps = new JArray(); foreach (var id in AttackSkills) steps.Add(id.ToString());
        var data = new JObject { ["comboStepId"] = ComboStep.ToString(), ["comboWindowId"] = ComboWindow.ToString(), ["nextFourId"] = NextFour.ToString(), ["reach"] = 2.5, ["steps"] = steps };
        // Tap presses at once; holding repeats the combo (the same gesture Eve uses for her held chain).
        var attackInput = new PlayerSkillInputConfig { PressImmediately = true, HoldMode = PlayerSkillHoldMode.Repeat, RepeatDelay = .2f, RepeatInterval = .05f };
        hub.Get<PNSkillBindingProfile>().Register(new PNSkillBindingProfile(AttackProfile, AttackSkills[0], AttackBehavior, data, attackInput, actionDataIds: AttackSkills));
        // Dash: the shared player Dash logic. A tap dashes on release, a hold dashes and gives the water coat (PNGuraSkill.HoldDash).
        var dashInput = new PlayerSkillInputConfig { TapDuration = .15f, HoldMode = PlayerSkillHoldMode.Hold, HoldDuration = .15f };
        hub.Get<PNSkillBindingProfile>().Register(new PNSkillBindingProfile(DashProfile, DashSkill, PNGameSkillBindings.LegacyBehavior,
            new JObject { ["slot"] = PNSkillSlots.Dash.ToString(), ["eve"] = false }, dashInput));
        var skillData = new JObject { ["cooldownAbilityId"] = SkillReady.ToString(), ["actionDataId"] = TridentSkill.ToString(), ["reach"] = 8.0, ["afterStateId"] = NextFour.ToString() };
        hub.Get<PNSkillBindingProfile>().Register(new PNSkillBindingProfile(SkillProfile, TridentSkill, SkillBehavior, skillData, new PlayerSkillInputConfig { PressImmediately = true }, actionDataIds: new[] { TridentSkill }));
        if (!hub.Get<PNSkillBaseBindings>().TryGet(Character, out _))
            hub.Get<PNSkillBaseBindings>().Register(new PNSkillBaseBindings(Character, new[]
            {
                new KeyValuePair<EveId, EveId>(PNSkillSlots.Attack, AttackProfile),
                new KeyValuePair<EveId, EveId>(PNSkillSlots.Skill, SkillProfile),
                new KeyValuePair<EveId, EveId>(PNSkillSlots.Dash, DashProfile)
            }));
    }
}
