#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

public partial class PNStat
{
    // Pure capture: unlike EndFloorAndCaptureSnapshot this never expires a source.
    public JObject CaptureWorldState()
    {
        if(Set.GetType()!=typeof(PNStatSet)||buffSourceIds.Count!=0||Set.BaseSnapshot().Values.Any(x=>x.GetType()!=typeof(EveBaseStat)&&x.GetType()!=typeof(EveResourceStat))||Set.SourceSnapshot().Values.SelectMany(x=>x).Any(x=>x.GetType()!=typeof(EveStatModifier)))throw new InvalidOperationException("Custom/Buff-owned stat state requires its selected state provider.");
        return new JObject{["combat"]=combatAbilities.CaptureCheckpoint(),["character"]=CharacterId.ToString(),["bases"]=new JArray(Set.BaseSnapshot().Select(p=>new JObject{["id"]=p.Key.ToString(),["value"]=p.Value.Value,["resource"]=p.Value is EveResourceStat,["current"]=p.Value is EveResourceStat?GetCurrent(p.Key):0,["policy"]=(int)(p.Value is EveResourceStat r?r.MaxChangePolicy:default)})),["sources"]=new JArray(Set.SourceSnapshot().Select(p=>new JObject{["id"]=p.Key.ToString(),["persistent"]=persistentSourceIds.Contains(p.Key),["modifiers"]=new JArray(p.Value.Select(m=>{var value=new JObject{["statId"]=m.Id.ToString(),["value"]=m.Value,["group"]=m.Group};if(m.CalculationId.IsValid)value["calculationId"]=m.CalculationId.ToString();if(m.ContributionId.IsValid)value["contributionId"]=m.ContributionId.ToString();return value;}))}))};
    }
    public void ValidateWorldState(JObject state,EveRegistryHub registry)
    {
        if(!(state["combat"] is JArray combat))throw new FormatException("Combat passive state is required.");
        using(var check=new PNAbilityCollection(new[]{iframeDefinition}))check.RestoreCheckpoint(combat);
        if(state.Value<string>("character")!=CharacterId.ToString())throw new FormatException("Actor character state differs; use an explicit conversion provider.");
        var copy=new PNStatSet(registry);var bases=(JArray)state["bases"];var sources=(JArray)state["sources"];
        if(bases.Count>4096||sources.Count>4096||bases.Select(x=>(string)x["id"]).Distinct().Count()!=bases.Count||sources.Select(x=>(string)x["id"]).Distinct().Count()!=sources.Count)throw new FormatException("Bounded unique stat keys required.");
        foreach(var item in bases.Cast<JObject>()){var id=EveId.Parse(item.Value<string>("id"));var policy=(EveMaxChangePolicy)item.Value<int>("policy");if(!Enum.IsDefined(typeof(EveMaxChangePolicy),policy))throw new FormatException("Invalid resource policy.");copy.AddBase(item.Value<bool>("resource")?(EveBaseStat)new EveResourceStat(id,item.Value<double>("value"),item.Value<double>("current"),policy):new EveBaseStat(id,item.Value<double>("value")));}
        foreach(var source in sources.Cast<JObject>()){var mods=(JArray)source["modifiers"];if(mods.Count>4096)throw new FormatException("Modifier bound exceeded.");foreach(var item in mods.Cast<JObject>())PNStatModifierData.Validate(item,registry);copy.SetSource(EveId.Parse(source.Value<string>("id")),mods.Cast<JObject>().Select(x=>PNStatModifierData.Parse(x)));}
        foreach(var item in bases.Where(x=>(bool)x["resource"])) {var id=EveId.Parse((string)item["id"]);double current=(double)item["current"];if(double.IsNaN(current)||double.IsInfinity(current)||current<0||current>copy.GetMax(id))throw new FormatException("Invalid resource current.");}
    }
    public void RestoreWorldState(JObject state,EveRegistryHub registry)
    {
        RequireStatMutation();ValidateWorldState(state,registry);
        var restored=new PNStatSet(registry);
        foreach(var item in ((JArray)state["bases"]).Cast<JObject>()){var id=EveId.Parse(item.Value<string>("id"));restored.AddBase(item.Value<bool>("resource")?(EveBaseStat)new EveResourceStat(id,item.Value<double>("value"),null,(EveMaxChangePolicy)item.Value<int>("policy")):new EveBaseStat(id,item.Value<double>("value")));}
        persistentSourceIds.Clear();buffSourceIds.Clear();
        foreach(var source in ((JArray)state["sources"]).Cast<JObject>()){var id=EveId.Parse(source.Value<string>("id"));restored.SetSource(id,((JArray)source["modifiers"]).Cast<JObject>().Select(x=>PNStatModifierData.Parse(x)));if(source.Value<bool>("persistent"))persistentSourceIds.Add(id);}
        foreach(var item in state["bases"].Where(x=>(bool)x["resource"]))restored.SetCurrent(EveId.Parse((string)item["id"]),(double)item["current"]);
        RestoreInitializedSet(restored);
        combatAbilities.RestoreCheckpoint((JArray)state["combat"]);
    }
}
#endif
