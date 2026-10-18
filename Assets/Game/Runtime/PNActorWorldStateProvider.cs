#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Conservative baseline serializer. Additional owned behavior must use a selected
// provider instead of pretending a replicated display payload is authoritative.
public sealed class PNActorWorldStateProvider : IPNWorldStateProvider
{
    public JToken Capture(PNWorldStateContext context)
    {
        var actor=context.Actor??throw new InvalidOperationException("Actor provider requires an actor.");
        var buffs=actor.GetComponent<PNBuffManager>();var control=actor.GetComponent<PNControlManager>();var time=actor.GetComponent<EveTimeScale>();var skill=actor.GetComponent<PNSkill>();
        if(buffs!=null&&(!buffs.CanCaptureState||buffs.Buffs.Snapshot().Count!=0)||control!=null&&control.State.Snapshot().Count!=0||time!=null&&time.Modifiers.Count!=0||skill?.Bindings?.ActiveLeaseCount>0||skill?.CurrentInvocation!=null||skill?.Effects?.ActiveContributionCount>0||skill?.Effects?.ActiveExecutionCount>0)
            throw new InvalidOperationException("Live Buff/Control/Time/Skill leases require an explicit state serializer; source remains resident.");
        return new JObject{["stat"]=actor.GetComponent<PNStat>().CaptureWorldState(),["abilities"]=actor.GetComponent<PNAbilityManager>()?.Abilities.CaptureCheckpoint(),["move"]=actor.GetComponent<PNMove>()?.CaptureWorldState(),["minimumTimeScale"]=time?.MinScale??0,
            ["control"]=new JArray(context.Session.Registry.Get<PNControlPermissionDefinition>().Snapshot().Keys.Select(id=>new JObject{["id"]=id.ToString(),["allowed"]=control==null||control.State.GetBaseAllowed(id)}))};
    }
    public void Validate(PNWorldStateContext context,JToken value)
    {
        var state=(JObject)value;context.Actor.GetComponent<PNStat>().ValidateWorldState((JObject)state["stat"],context.Session.Registry);
        if(state["abilities"] is JArray abilities)using(var check=new PNAbilityCollection())check.RestoreCheckpoint(abilities);
        if(state["move"] is JObject move){if(context.Actor.GetComponent<PNMove>()==null)throw new FormatException("Movement owner is missing.");PNMove.ValidateWorldState(move);}
        float minimum=state.Value<float>("minimumTimeScale");if(float.IsNaN(minimum)||minimum<0||minimum>1)throw new FormatException("Invalid minimum time scale.");
        var controls=(JArray)state["control"];if(controls.Count>256||controls.Select(x=>(string)x["id"]).Distinct().Count()!=controls.Count)throw new FormatException("Unique bounded controls required.");
        foreach(var entry in controls){context.Session.Registry.Get<PNControlPermissionDefinition>().Get(EveId.Parse((string)entry["id"]));if(entry["allowed"].Type!=JTokenType.Boolean)throw new FormatException("Control boolean required.");}
    }
    public void Restore(PNWorldStateContext context,JToken value)
    {
        Validate(context,value);var state=(JObject)value;var actor=context.Actor;
        actor.GetComponent<PNStat>().RestoreWorldState((JObject)state["stat"],context.Session.Registry);
        if(state["abilities"] is JArray abilities)(actor.GetComponent<PNAbilityManager>()??throw new InvalidOperationException("Ability owner missing.")).Abilities.RestoreCheckpoint(abilities);
        if(actor.GetComponent<EveTimeScale>() is EveTimeScale time)time.MinScale=state.Value<float>("minimumTimeScale");
        if(state["move"] is JObject move)actor.GetComponent<PNMove>().RestoreWorldState(move);
        var control=actor.GetComponent<PNControlManager>();if(control!=null)foreach(var entry in state["control"])control.State.SetBaseAllowed(EveId.Parse((string)entry["id"]),(bool)entry["allowed"]);
    }
    public void Dispose() { }
}
#endif
