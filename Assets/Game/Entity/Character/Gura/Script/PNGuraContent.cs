using System;
using System.Collections.Generic;
using System.Linq;
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
    public static readonly EveId CombatStrikeAction = EveId.Parse("project_n:gura_combat_strike");
    public static readonly EveId DashAction = EveId.Parse("project_n:gura_dash_action");
    public static readonly EveId ThrownTrident = EveId.Parse("project_n:gura_thrown_trident"), TridentSplash = EveId.Parse("project_n:gura_trident_splash");
    public static readonly EveId ComboStrikeAction = EveId.Parse("project_n:combo_strike_action");
    public static readonly EveId ComboStep = EveId.Parse("project_n:gura_combo_step"), ComboWindow = EveId.Parse("project_n:gura_combo_window"),
        NextFour = EveId.Parse("project_n:gura_next_four"), WaterCoat = EveId.Parse("project_n:gura_water_coat");
    public static readonly EveId SkillProfile = EveId.Parse("project_n:binding_gura_skill"), SkillBehavior = EveId.Parse("project_n:gura_cast"),
        TridentSkill = EveId.Parse("project_n:gura_trident"), TridentAction = EveId.Parse("project_n:gura_trident_action"), TridentProjectile = EveId.Parse("project_n:gura_trident"),
        SkillReady = EveId.Parse("project_n:gura_skill_ready"), SkillBurstSkill = EveId.Parse("project_n:gura_skill_burst"),
        UltimateProfile = EveId.Parse("project_n:binding_gura_ultimate"), UltimateBehavior = EveId.Parse("project_n:gura_ultimate"), BurstProfile = EveId.Parse("project_n:binding_gura_burst"), BurstBehavior = EveId.Parse("project_n:gura_burst"),
        SharkSkill = EveId.Parse("project_n:gura_shark"), DiveSkill = EveId.Parse("project_n:gura_dive"), LeapSkill = EveId.Parse("project_n:gura_leap"), FangsSkill = EveId.Parse("project_n:gura_fangs"), BurstSlamSkill = EveId.Parse("project_n:gura_burst_slam"),
        FangAction = EveId.Parse("project_n:gura_fang_action"), TideArea = EveId.Parse("project_n:gura_burst_area");
    public static readonly EveId BurstStack = EveId.Parse("project_n:gura_burst_stack"), WaveStack = EveId.Parse("project_n:gura_wave_stack"), FangStack = EveId.Parse("project_n:gura_fang_stack"),
        BurstState = EveId.Parse("project_n:gura_burst_state"), UltimateReady = EveId.Parse("project_n:gura_ultimate_ready"), BurstSkillReady = EveId.Parse("project_n:gura_burst_skill_ready"),
        BurstUltimateReady = EveId.Parse("project_n:gura_burst_ultimate_ready"), ChainCount = EveId.Parse("project_n:gura_chain_count"), ChainState = EveId.Parse("project_n:gura_chain_state"),
        Chain6 = EveId.Parse("project_n:gura_chain_6"), Chain12 = EveId.Parse("project_n:gura_chain_12");
    public static readonly EveId[] AttackSkills = {
        EveId.Parse("project_n:gura_attack_1"), EveId.Parse("project_n:gura_attack_2"), EveId.Parse("project_n:gura_attack_3"), EveId.Parse("project_n:gura_attack_4") };

    // The Actions and behaviors (content session startup; shared definitions only, no entity state).
    public static void Register(EveRegistryHub hub)
    {
        var action = new PNSkillActionDefinition(ComboStrikeAction, typeof(PNComboStrikeAction), (a, h) => PNComboStrikeAction.Validate(a, h), PNSkillContextRequirements.Direction).NoView();
        hub.Actions().Register(action);
        var combat = new PNSkillActionDefinition(CombatStrikeAction, typeof(PNComboStrikeAction), ValidateCombatStrike, PNSkillContextRequirements.Direction);
        var dash = new PNSkillActionDefinition(DashAction, typeof(PNMotionAction), PNGameplayContentModule.ValidateEffectAction, PNSkillContextRequirements.Direction).WithDriverProgress();
        var throwing = new PNSkillActionDefinition(TridentAction, typeof(PNGuraTridentAction), PNGuraTridentAction.Validate, PNSkillContextRequirements.Direction);
#if UNITY_5_3_OR_NEWER
        combat.WithView(PNGuraCombatView.CreateStrike, PNContentIds.MeleeScale);
        dash.WithView(PNGuraCombatView.CreateDash);
        throwing.WithView(PNGuraCombatView.CreateThrow);
#else
        combat.NoView(); dash.NoView(); throwing.NoView();
#endif
        hub.Actions().Register(combat);
        hub.Actions().Register(dash);
        hub.Actions().Register(throwing);
        // The trident: Eve-like homing, one hit ends it; the Action blasts where it ends (the projectile itself deals no damage).
        var trident = JObject.Parse("{'speed':14,'acceleration':0,'speedMin':0,'speedMax':40,'life':{'mode':'Time','value':1.6},'volume':{'size':[0.3,0.3,0.3]},'environmentMask':['Wall'],'hitLimit':1,'turnRate':720,'homeAfter':0,'lostTarget':'Reacquire','searchRadius':10,'searchInterval':0.2}");
        hub.Instances().Register(new PNProjectileDefinition(TridentProjectile, typeof(PNHomingProjectile), trident, PNHomingSettings.ValidateDefinition, PNDirectionalSettings.ValidateInput));
        hub.Instances().Register(new PNProjectileDefinition(ThrownTrident, typeof(PNGuraThrownTrident), trident, PNHomingSettings.ValidateDefinition, PNDirectionalSettings.ValidateInput));
        hub.Instances().Register(new PNBlastDefinition(TridentSplash, typeof(PNGuraTridentSplash), new JObject(),
            (token, _) => PNBuffContentBatch.Fields(PNBuffContentBatch.Object(token)), input => PNCylinderBlast.Validate(input)));
        hub.Actions().Register(new PNSkillActionDefinition(FangAction, typeof(PNGuraFangAction), (a, h) => PNGuraFangAction.Validate(a, h), PNSkillContextRequirements.Direction).NoView());
        hub.Get<PNSkillBehaviorDefinition>().Register(new PNSkillBehaviorDefinition(UltimateBehavior, UltimateBehavior, c => new GuraUltimateBehavior(c), GuraUltimateBehavior.Validate, GuraUltimateBehavior.ValidateReferences));
        hub.Get<PNSkillBehaviorDefinition>().Register(new PNSkillBehaviorDefinition(BurstBehavior, BurstBehavior, c => new GuraBurstBehavior(c), GuraBurstBehavior.Validate, GuraBurstBehavior.ValidateReferences));
        // The tide of the Burst: its own area class, carrying the one-per-world trait (D2).
        hub.Instances().Register(new PNAreaDefinition(TideArea, typeof(PNGuraTideArea), JObject.Parse("{'pollPeriod':0.2,'slowBuffId':'base:timescale_reduction_area','blessingId':'project_n:gura_blessing','burstStateId':'project_n:gura_burst_state'}"),
            PNGuraTideArea.ValidateDefinition, input => { new PNGuraTideSettings(input); },
            (payload, context) => { context.Require<PNBuffDefinition>(payload["slowBuffId"], "slowBuffId"); context.Require<PNBuffDefinition>(payload["blessingId"], "blessingId"); },
            new[] { new PNAreaTraitUse(PNAreaTraits.Solizeitastronhringur) }));
        hub.Get<PNSkillBehaviorDefinition>().Register(new PNSkillBehaviorDefinition(SkillBehavior, SkillBehavior, c => new GuraCastBehavior(c), GuraCastBehavior.Validate, GuraCastBehavior.ValidateReferences));
        hub.Get<PNSkillBehaviorDefinition>().Register(new PNSkillBehaviorDefinition(AttackBehavior, AttackBehavior, c => new GuraAttackBehavior(c), GuraAttackBehavior.Validate, GuraAttackBehavior.ValidateReferences));
    }

    private static void ValidateCombatStrike(PNSkillActionData data, EveRegistryHub hub)
    {
        PNComboStrikeAction.Validate(data, hub);
        var payload = (JObject)data.Perform;
        switch (payload["visualState"]?.Value<string>())
        {
            case "attack_1": case "attack_2": case "attack_3": case "attack_4": case "shark": case "dive":
                break;
            default:
                throw new FormatException("Gura combat strike requires an attack, shark or dive visualState.");
        }
    }

    // The slot bindings, once the characters (and so the skill data in their JSON) exist. Attack and Dash are Gura's; the other slots join with the later stages.
    public static void RegisterBindings(EveRegistryHub hub)
    {
        if (!hub.Characters.TryGet(Character, out _)) return;
        var steps = new JArray(); foreach (var id in AttackSkills) steps.Add(id.ToString());
        var data = new JObject { ["comboStepId"] = ComboStep.ToString(), ["comboWindowId"] = ComboWindow.ToString(), ["nextFourId"] = NextFour.ToString(), ["reach"] = 2.5, ["steps"] = steps,
            ["gains"] = JArray.Parse("[{'burst':3,'wave':2,'cut':1},{'burst':3,'wave':2,'cut':1},{'burst':6,'wave':3,'cut':1.5},{'burst':5,'wave':3,'cut':3}]"),
            ["chain"] = JObject.Parse("{'milestones':[6,12,18],'fang':2}"), ["freeSkillId"] = SkillBurstSkill.ToString(), ["free"] = JObject.Parse("{'burst':10,'wave':4}") };
        // Tap presses at once; holding repeats the combo (the same gesture Eve uses for her held chain).
        var attackInput = new PlayerSkillInputConfig { PressImmediately = true, HoldMode = PlayerSkillHoldMode.Repeat, RepeatDelay = .2f, RepeatInterval = .05f };
        hub.Get<PNSkillBindingProfile>().Register(new PNSkillBindingProfile(AttackProfile, AttackSkills[0], AttackBehavior, data, attackInput, actionDataIds: AttackSkills.Concat(new[] { SkillBurstSkill }).ToArray()));
        // Dash: the shared player Dash logic. A tap dashes on release; a press still held after the tap window dashes once and keeps the water coat until released (PNGuraSkill.RepeatDash).
        var dashInput = new PlayerSkillInputConfig { TapDuration = .15f, HoldMode = PlayerSkillHoldMode.Repeat, RepeatDelay = .16f, RepeatInterval = .1f };
        hub.Get<PNSkillBindingProfile>().Register(new PNSkillBindingProfile(DashProfile, DashSkill, PNGameSkillBindings.LegacyBehavior,
            new JObject { ["slot"] = PNSkillSlots.Dash.ToString(), ["eve"] = false }, dashInput));
        var skillData = new JObject { ["cooldownAbilityId"] = SkillReady.ToString(), ["actionDataId"] = TridentSkill.ToString(), ["reach"] = 8.0, ["afterStateId"] = NextFour.ToString(),
            ["gain"] = new JObject { ["burst"] = 10, ["wave"] = 4, ["cut"] = 2.5 }, ["inBurst"] = new JObject { ["cooldownAbilityId"] = BurstSkillReady.ToString(), ["actionDataId"] = SkillBurstSkill.ToString() } };
        hub.Get<PNSkillBindingProfile>().Register(new PNSkillBindingProfile(SkillProfile, TridentSkill, SkillBehavior, skillData, new PlayerSkillInputConfig { PressImmediately = true }, actionDataIds: new[] { TridentSkill, SkillBurstSkill }));
        var ultimateData = new JObject { ["cooldownAbilityId"] = UltimateReady.ToString(), ["reach"] = 3.0, ["shark"] = SharkSkill.ToString(), ["dive"] = DiveSkill.ToString(),
            ["gain"] = new JObject { ["burst"] = 15, ["wave"] = 0, ["cut"] = 0 },
            ["inBurst"] = new JObject { ["leap"] = LeapSkill.ToString(), ["fangs"] = FangsSkill.ToString(), ["fangId"] = FangStack.ToString(), ["burstStateId"] = BurstState.ToString() } };
        // Tap on release; a hold fires once held long enough and its release (Canceled) lets go of the dive.
        var ultimateInput = new PlayerSkillInputConfig { TapDuration = .2f, HoldMode = PlayerSkillHoldMode.Hold, HoldDuration = .2f };
        hub.Get<PNSkillBindingProfile>().Register(new PNSkillBindingProfile(UltimateProfile, SharkSkill, UltimateBehavior, ultimateData, ultimateInput, actionDataIds: new[] { SharkSkill, DiveSkill, LeapSkill, FangsSkill }));
        var burstData = new JObject { ["actionDataId"] = BurstSlamSkill.ToString(), ["reach"] = 6.0, ["cost"] = 125, ["startWave"] = 10, ["burstStateId"] = BurstState.ToString(), ["stackId"] = BurstStack.ToString(), ["waveId"] = WaveStack.ToString() };
        hub.Get<PNSkillBindingProfile>().Register(new PNSkillBindingProfile(BurstProfile, BurstSlamSkill, BurstBehavior, burstData, new PlayerSkillInputConfig { PressImmediately = true }, actionDataIds: new[] { BurstSlamSkill }));
        if (!hub.Get<PNSkillBaseBindings>().TryGet(Character, out _))
            hub.Get<PNSkillBaseBindings>().Register(new PNSkillBaseBindings(Character, new[]
            {
                new KeyValuePair<EveId, EveId>(PNSkillSlots.Attack, AttackProfile),
                new KeyValuePair<EveId, EveId>(PNSkillSlots.Skill, SkillProfile),
                new KeyValuePair<EveId, EveId>(PNSkillSlots.Ultimate, UltimateProfile),
                new KeyValuePair<EveId, EveId>(PNSkillSlots.Burst, BurstProfile),
                new KeyValuePair<EveId, EveId>(PNSkillSlots.Dash, DashProfile)
            }));
    }
}
