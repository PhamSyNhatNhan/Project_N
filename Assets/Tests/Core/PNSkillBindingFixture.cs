using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Shared by standalone and native fixtures; not automatically registered in game content.
public sealed class PNSkillBindingFixtureModule:EveIContentModule,EveIContentFinalizeModule
{
    public static readonly EveId Behavior=EveId.Parse("fixture:charge_logic"),Profile=EveId.Parse("fixture:charge"),
        Hybrid=EveId.Parse("fixture:hybrid"), Recovery=EveId.Parse("fixture:recovery"),Entry=EveId.Parse("fixture:entry"), Buff=EveId.Parse("fixture:binding_buff");
    public void Register(EveRegistryHub hub)
    {
        var entry=(JObject)hub.Skills.Get(PNGameplayContentModule.PlayerDash).Data;
        entry["categoryIds"]=new JArray("base:attack","base:skill");entry["affectedByAttackSpeed"]=false;
        hub.Skills.Register(new EveSkillDefinition(Entry,entry));
        hub.Get<PNSkillBehaviorDefinition>().Register(new PNSkillBehaviorDefinition(Behavior,Behavior,c=>new PNSkillChargeFixture(c)));
        foreach(var id in new[]{Profile,Hybrid,EveId.Parse("fixture:death_reset")})hub.Get<PNSkillBindingProfile>().Register(new PNSkillBindingProfile(id,Entry,Behavior,
            input:new PlayerSkillInputConfig{CanTap=false,HoldMode=PlayerSkillHoldMode.Hold,HoldDuration=.1f},
            actionDataIds:new[]{PNGameplayContentModule.PlayerDash},abilities:new[]{new PNAbilitySettings{Id=Recovery.ToString(),Kind=PNAbilityKind.Cooldown,Maximum=2,InitialAmount=2,Cooldown=3,Clock=PNAbilityClock.Game,ShowInUI=true}.Create()},resetAbilitiesOnDeath:id.Path=="death_reset"));
        var template=hub.Buffs().Get(EveId.Parse("project_n:buff_skill_binding_template"));
        var data=(JObject)template.Payload;data["duration"]=1;data["profileId"]=Profile.ToString();
        hub.Buffs().Register(template.Derive(Buff,data,Array.Empty<EveId>(),false,1,PNBuffInstanceScope.Target,true));
    }
    public void FinalizeContent(EveRegistryHub hub)
    {
        if(hub.Characters.TryGet(EveId.Parse("project_n:eve"),out var eve))
            hub.Characters.Register(new EveCharacterDefinition(EveId.Parse("fixture:eve_extension"),eve.Stats,eve.SkillIds,eve.PortraitPath,eve.Data));
    }
    public void ValidateContent(EveRegistryHub hub) { }
}
public sealed class PNSkillChargeFixture:PNSkillBehavior
{
    private readonly PNSkillBehaviorContext context;
    private bool charging;
    private PNMotionAction action;
    private PNSkillActionContext actionContext;
    public static int Starts,Ends,Cancels,SelectionLeases,InputStarts;
    public PNSkillChargeFixture(PNSkillBehaviorContext context){this.context=context;}
    public override void SelectionChanged(bool selected)
    {if(selected){SelectionLeases++;context.SelectionResources.Own(new PNBuffRelease(()=>SelectionLeases--));}}
    public override void Input(PNSkillInputEvent input)
    {
        if(input.Phase==PNSkillInputPhase.Started){InputStarts++;charging=true;return;}
        if(input.Phase==PNSkillInputPhase.Canceled){charging=false;return;}
        if(!charging||action!=null)return;
        var recovery=context.Abilities.GetCooldown(PNSkillBindingFixtureModule.Recovery);
        if(!recovery.CanConsume()||!context.TryBegin())return;
        charging=false;
        try
        {
            action=(PNMotionAction)context.CreateAction(PNGameplayContentModule.PlayerDash,out actionContext,direction:Vector3.right);
            action.BindMovementOwner(Cancel);action.Start(actionContext);action.Perform(actionContext);
            if(!recovery.TryConsume())throw new InvalidOperationException("Charge recovery changed.");
            Starts++;context.Commit(direction:Vector3.right);
        }
        catch{Finish(true);throw;}
    }
    public override void Advance(float delta)
    {if(action==null)return;var active=action;active.Advance(delta);if(ReferenceEquals(active,action)&&active.IsComplete)Finish(false);}
    private void Finish(bool cancelled)
    {
        var active=action;var data=actionContext;action=null;actionContext=null;
        try{if(active!=null){if(cancelled){active.Cancel(data);Cancels++;}else{active.Exit(data);Ends++;}}}
        finally{try{active?.Dispose();}finally{context.Complete(cancelled);}}
    }
    public override void Cancel(){charging=false;Finish(true);}
    public override void Dispose()=>Cancel();
}
public static class PNSkillBindingDllChecks
{
    public static string Verify(EveRegistryHub hub,string owner)
    {
        var profile=new EveId(owner,"skill_profile");var counter=new EveId(owner,"skill_counter");
        if(!hub.Get<PNSkillBindingProfile>().TryGet(profile,out _))throw new Exception("DLL binding profile missing.");
        var roots=new System.Collections.Generic.List<GameObject>();
        var loaders=new System.Collections.Generic.List<EveEntityLoader>();
        PNPlayerSkill Player(long id)
        {
#if UNITY_5_3_OR_NEWER
            var root=new GameObject("DLL binding player");roots.Add(root);root.SetActive(false);
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id),EveId.Parse("project_n:dummy"),0);
            root.AddComponent<DummyStat>();root.AddComponent<PNAbilityManager>();root.AddComponent<Rigidbody>().useGravity=false;
            var skill=root.AddComponent<PNPlayerSkill>();var loader=root.AddComponent<EveEntityLoader>();loaders.Add(loader);loader.Load(hub);root.SetActive(true);return skill;
#else
            var skill=new PNSharedDashProbe();var loader=new EveTestLoader();loaders.Add(loader);var identity=new EveEntityIdentity();identity.Assign(new EveEntityId(id),EveId.Parse("project_n:dummy"),0);
            MonoBehaviour[] parts={skill,loader,identity,new DummyStat(),new PNAbilityManager(),new Rigidbody()};foreach(var part in parts){part.Components=parts;part.gameObject.SetActive(true);}skill.InitializeInput();loader.Load(hub);return skill;
#endif
        }
        try
        {
            var first=Player(991801);var second=Player(991802);
            using(var lease=first.Bindings.AcquireOverride("dll",new PNSkillOverrideRequest(new[]{new System.Collections.Generic.KeyValuePair<EveId,EveId>(PNSkillSlots.Dash,profile)})).Lease)
            {
                first.RequestSlot(PNSkillSlots.Dash,new PNSkillInputEvent(PNSkillInputPhase.Started,PNSkillInteraction.Tap));first.RequestSlot(PNSkillSlots.Dash,new PNSkillInputEvent(PNSkillInputPhase.Performed,PNSkillInteraction.Tap));
                if(first.Abilities.GetCounter(counter).Current!=1||first.CurrentInvocation!=null)throw new Exception("DLL behavior did not execute and complete once.");
            }
            using(var lease=first.Bindings.AcquireOverride("dll-again",new PNSkillOverrideRequest(new[]{new System.Collections.Generic.KeyValuePair<EveId,EveId>(PNSkillSlots.Dash,profile)})).Lease)
            using(var other=second.Bindings.AcquireOverride("dll-other",new PNSkillOverrideRequest(new[]{new System.Collections.Generic.KeyValuePair<EveId,EveId>(PNSkillSlots.Dash,profile)})).Lease)
            {
                first.RequestSlot(PNSkillSlots.Dash,new PNSkillInputEvent(PNSkillInputPhase.Started,PNSkillInteraction.Tap));first.RequestSlot(PNSkillSlots.Dash,new PNSkillInputEvent(PNSkillInputPhase.Performed,PNSkillInteraction.Tap));
                second.RequestSlot(PNSkillSlots.Dash,new PNSkillInputEvent(PNSkillInputPhase.Started,PNSkillInteraction.Tap));second.RequestSlot(PNSkillSlots.Dash,new PNSkillInputEvent(PNSkillInputPhase.Performed,PNSkillInteraction.Tap));
                if(first.Abilities.GetCounter(counter).Current!=2||second.Abilities.GetCounter(counter).Current!=1||first.Abilities.GetCounter(counter).Handle==second.Abilities.GetCounter(counter).Handle)throw new Exception("DLL binding state lost or shared between entities.");
            }
            return "PASS DLL behavior registration/execution without Eve, re-equip state retention and two-entity isolation";
        }
        finally
        {
            foreach(var loader in loaders)loader.Unload();
#if UNITY_5_3_OR_NEWER
            foreach(var root in roots)UnityEngine.Object.DestroyImmediate(root);
#endif
        }
    }
}
