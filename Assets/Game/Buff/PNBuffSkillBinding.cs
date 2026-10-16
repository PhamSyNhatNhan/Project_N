using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

public static class PNBuffSkillBindingContent
{
    public static void Register(EveRegistryHub hub)
    {
        var id=EveId.Parse("project_n:buff_skill_binding_template");
        hub.Buffs().Register(new PNBuffDefinition(id,id,data=>new PNBuffSkillBinding(data),
            new JObject{["duration"]=5,["slot"]=PNSkillSlots.Attack.ToString(),["profileId"]=PNGameSkillBindings.SharedDash.ToString(),["priority"]=0,["applyTransition"]="FinishCurrent",["releaseTransition"]="FinishCurrent"},
            validate:Validate,validateReferences:(data,context)=>context.Require<PNSkillBindingProfile>(EveId.Parse(PNBuffContentBatch.String(PNBuffContentBatch.Object(data),"profileId")),"profileId")));
    }
    public static void Validate(JToken data,EveRegistryHub hub)
    {
        var obj=PNBuffContentBatch.Object(data);PNBuffContentBatch.Fields(obj,"duration","slot","profileId","priority","applyTransition","releaseTransition");
        double duration=PNBuffContentBatch.Number(obj,"duration");PNBuffCollection.Seconds(duration,false);
        if(!EveId.Parse(PNBuffContentBatch.String(obj,"slot")).IsValid)throw new FormatException("Slot required.");
        EveId.Parse(PNBuffContentBatch.String(obj,"profileId"));
        PNBuffContentBatch.Integer(obj,"priority");Transition(obj,"applyTransition");Transition(obj,"releaseTransition");
    }
    internal static PNSkillTransition Transition(JObject obj,string field)
    {var value=PNBuffContentBatch.String(obj,field);if(value=="FinishCurrent")return PNSkillTransition.FinishCurrent;if(value=="CancelCurrent")return PNSkillTransition.CancelCurrent;throw new FormatException("Unknown binding transition.");}
}
public sealed class PNBuffSkillBinding:PNBuffBehavior
{
    private readonly JObject data;
    public PNBuffSkillBinding(JToken data){this.data=(JObject)data.DeepClone();}
    public override bool EventDrivenReplication=>true;
    public override void OnApply(PNBuffContext context,JToken input){context.SetDuration(data["duration"].Value<double>());Grant(context);}
    public override void OnBehaviorStarted(PNBuffContext context,bool migrated){if(!migrated){context.SetDuration(data["duration"].Value<double>());Grant(context);}}
    public override void OnResume(PNBuffContext context)=>Grant(context);
    private void Grant(PNBuffContext context)
    {
        var skill=context.Services.Target?.GetComponent<PNSkill>();if(skill?.Bindings==null)throw new InvalidOperationException("Target has no Skill bindings.");
        var receipt=skill.Bindings.AcquireOverride("buff:"+context.Handle.InstanceId,new PNSkillOverrideRequest(new[]{new System.Collections.Generic.KeyValuePair<EveId,EveId>(EveId.Parse(data["slot"].Value<string>()),EveId.Parse(data["profileId"].Value<string>()))},data["priority"].Value<int>(),PNBuffSkillBindingContent.Transition(data,"applyTransition"),PNBuffSkillBindingContent.Transition(data,"releaseTransition")));
        context.Resources.Own(receipt.Lease);if(receipt.Status==PNSkillBindingReceiptStatus.Failed)throw receipt.Error;
        if(receipt.Status==PNSkillBindingReceiptStatus.Pending)
        {
            Action<PNSkillBindingReceipt> completed=value=>{if(value.Status==PNSkillBindingReceiptStatus.Failed&&context.Handle.IsAlive)context.End(PNBuffEndReason.RuntimeError);};
            receipt.Completed+=completed;context.Resources.Own(new PNBuffRelease(()=>receipt.Completed-=completed));
        }
    }
}
