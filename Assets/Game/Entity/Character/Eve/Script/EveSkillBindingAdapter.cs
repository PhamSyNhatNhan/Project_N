using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;

public static class PNGameSkillBindings
{
    public static readonly EveId LegacyBehavior=EveId.Parse("project_n:legacy_skill_hooks"), SharedDash=EveId.Parse("project_n:binding_player_dash");
    public static EveId EveProfile(EveId slot)=>EveId.Parse("project_n:binding_eve_"+slot.Path.Substring("slot_".Length));
    public static void RegisterBehaviors(EveRegistryHub hub)
    {
        var behaviors=hub.Get<PNSkillBehaviorDefinition>();
        behaviors.Register(new PNSkillBehaviorDefinition(LegacyBehavior,LegacyBehavior,c=>new PNLegacySkillBehavior(c),
            (data,content)=>{var obj=PNBuffContentBatch.Object(data);PNBuffContentBatch.Fields(obj,"slot","eve");var slot=EveId.Parse(PNBuffContentBatch.String(obj,"slot"));if(!PNSkillSlots.Player.ContainsSlot(slot))throw new FormatException("Legacy player slot required.");PNBuffContentBatch.Bool(obj,"eve");}));
        var profiles=hub.Get<PNSkillBindingProfile>();
        profiles.Register(new PNSkillBindingProfile(SharedDash,EveId.Parse("base:player_dash"),LegacyBehavior,
            new JObject{["slot"]=PNSkillSlots.Dash.ToString(),["eve"]=false},new PlayerSkillInputConfig{PressImmediately=true}));
        PNBuffSkillBindingContent.Register(hub);
    }
    public static void Register(EveRegistryHub hub)
    {
        var profiles=hub.Get<PNSkillBindingProfile>();
        var eve=EveId.Parse("project_n:eve");
        if(!hub.Characters.TryGet(eve,out _))return;
        var tuning=EveMovesetSettings.Parse((PNSkillData.Parse(hub.Skills.Get(EveId.Parse("project_n:eve_moveset")).Data).Skill as JObject)?["tuning"]);
        var entry=new[]{PNGameplayContentModule.EveAttackEntry,PNGameplayContentModule.EveSkillEntry,PNGameplayContentModule.EveUltimateEntry,EveId.Parse("base:eve_dash"),PNGameplayContentModule.EveBurstEntry};
        var pairs=new List<KeyValuePair<EveId,EveId>>();
        for(int i=0;i<PNSkillSlots.Player.Count;i++)
        {
            var slot=PNSkillSlots.Player[i];var profile=EveProfile(slot);var input=new PlayerSkillInputConfig{PressImmediately=slot==PNSkillSlots.Dash};
            if(slot==PNSkillSlots.Attack){input.HoldMode=PlayerSkillHoldMode.Repeat;input.RepeatDelay=tuning.RepeatDelay;input.RepeatInterval=tuning.RepeatInterval;}
            var display=slot==PNSkillSlots.Skill?new[]{EveCharacterSkill.SkillCooldownId}:slot==PNSkillSlots.Ultimate?new[]{EveCharacterSkill.UltimateCooldownId}:slot==PNSkillSlots.Dash?new[]{EveCharacterSkill.DashCooldownId}:Array.Empty<EveId>();
            profiles.Register(new PNSkillBindingProfile(profile,entry[i],LegacyBehavior,new JObject{["slot"]=slot.ToString(),["eve"]=true},input,displayAbilityIds:display));
            pairs.Add(new KeyValuePair<EveId,EveId>(slot,profile));
        }
        if(!hub.Get<PNSkillBaseBindings>().TryGet(eve,out _))hub.Get<PNSkillBaseBindings>().Register(new PNSkillBaseBindings(eve,pairs));
    }
    private static bool ContainsSlot(this IReadOnlyList<EveId> slots,EveId id){foreach(var slot in slots)if(slot==id)return true;return false;}
}
public sealed class PNLegacySkillBehavior:PNSkillBehavior
{
    private readonly PNPlayerSkill player;
    private readonly EveId slot;
    public PNLegacySkillBehavior(PNSkillBehaviorContext context)
    {
        player=context.Skill as PNPlayerSkill??throw new InvalidOperationException("Legacy player behavior requires PNPlayerSkill.");
        var data=PNBuffContentBatch.Object(context.Profile.Data);slot=EveId.Parse(PNBuffContentBatch.String(data,"slot"));
        if(PNBuffContentBatch.Bool(data,"eve")&&!(player is EveCharacterSkill))throw new InvalidOperationException("Eve profile requires Eve moveset.");
        if(!PNBuffContentBatch.Bool(data,"eve")&&player is EveCharacterSkill)throw new InvalidOperationException("Shared-player Dash profile requires the shared Dash implementation; use the Eve Dash profile for Eve.");
    }
    public override void Input(PNSkillInputEvent input)=>player.DispatchLegacyBindingInput(slot,input);
    public override void Cancel()=>player.TryInterrupt(EveId.Parse("project_n:binding_changed"));
}
public partial class EveCharacterSkill
{
    protected override EveId DefaultDashBindingProfile=>PNGameSkillBindings.EveProfile(PNSkillSlots.Dash);
    protected override bool UsesCharacterBindings=>true;
    protected override bool CanBeginBindingExecution=>!IsBusy&&base.CanBeginBindingExecution;
    protected override bool LegacyBindingBusy=>IsBusy||IsDashing;
    private bool CanContinueEveRepeat=>Bindings==null||!Bindings.TryGet(PNSkillSlots.Attack,out var profile)||profile==PNGameSkillBindings.EveProfile(PNSkillSlots.Attack);
}
