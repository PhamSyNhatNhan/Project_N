#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// Explicit passive-state codec. No action/cost/reward is replayed on restoration.
public sealed partial class PNAbilityCollection
{
    public JArray CaptureCheckpoint()
    {
        if(disposed)throw new ObjectDisposedException(nameof(PNAbilityCollection));
        var records=Instances.Select(a=>new JObject{["definition"]=Describe(a.Definition),["transient"]=a is PNAbilityState state&&state.Transient,["state"]=a is PNAbilityCooldown cd?cd.ExportCheckpoint():a is PNAbilityCounter count?count.ExportCheckpoint():((PNAbilityState)a).ExportCheckpoint()});
        var declarations=Definitions.Where(d=>!Instances.Any(a=>a.Id==d.Id)).Select(d=>new JObject{["definition"]=Describe(d),["declarationOnly"]=true});
        return new JArray(records.Concat(declarations));
    }
    private static JObject Describe(PNAbilityDefinition d)=>JObject.FromObject(new PNAbilitySettings{Id=d.Id.ToString(),Kind=d.Kind,Clock=d.Clock,ShowInUI=d.ShowInUI,IconPath=d.IconPath,DisplayOrder=d.DisplayOrder,Maximum=d.Maximum,InitialAmount=d.InitialAmount,AutoRecover=d.AutoRecover,Cooldown=d.Cooldown,Recovery=d.Recovery,HasDuration=d.Duration.HasValue,Duration=d.Duration??10,Lifetime=d.Lifetime,InitiallyActive=d.InitiallyActive,Reapply=d.Reapply,CooldownStart=d.CooldownStart});

    public void RestoreCheckpoint(JArray data)
    {
        RequireMutable();
        // Validate the entire graph in an independent passive collection first.
        using(var check=new PNAbilityCollection(Definitions))check.ImportCheckpoint(data);
        ImportCheckpoint(data);
    }
    private void ImportCheckpoint(JArray data)
    {
        if(data==null||data.Count>1024)throw new FormatException("Bounded ability checkpoint required.");
        var used=new System.Collections.Generic.HashSet<PNAbilityHandle>();
        foreach(var entry in data.OfType<JObject>())
        {
            var definition=entry["definition"].ToObject<PNAbilitySettings>().Create();bool declaration=entry["declarationOnly"]!=null&&Bool(entry,"declarationOnly");bool transient=!declaration&&Bool(entry,"transient");
            if(!definitions.ContainsKey(definition.Id))Define(definition);
            else if(!JToken.DeepEquals(Describe(definitions[definition.Id]),Describe(definition)))throw new FormatException("Incompatible selected ability definition.");
            if(declaration)continue;
            PNAbility ability=instances.Values.FirstOrDefault(x=>x.Id==definition.Id&&!used.Contains(x.Handle)&&(x is PNAbilityState s?s.Transient==transient:!transient));
            if(ability==null){if(definition.Kind!=PNAbilityKind.State&&used.Any(h=>instances[h].Id==definition.Id)||transient&&definition.Kind!=PNAbilityKind.State)throw new FormatException("Duplicate singleton/transient kind.");ability=Create(definitions[definition.Id],transient);}
            if(!used.Add(ability.Handle))throw new FormatException("Duplicate singleton ability.");
            var state=(JObject)entry["state"];
            if(ability is PNAbilityCooldown cd)cd.ImportCheckpoint(state);
            else if(ability is PNAbilityCounter counter)counter.ImportCheckpoint(state);
            else ((PNAbilityState)ability).ImportCheckpoint(state);
        }
        if(data.Count!=data.OfType<JObject>().Count())throw new FormatException("Ability record required.");
        foreach(var extra in instances.Values.Where(x=>!used.Contains(x.Handle)).ToArray()){instances.Remove(extra.Handle);extra.Detach();}
    }
    internal static bool Bool(JObject state,string key)
    {if(state[key]?.Type!=JTokenType.Boolean)throw new FormatException("Boolean required: "+key);return state.Value<bool>(key);}
    internal static int Integer(JObject state,string key)
    {if(state[key]?.Type!=JTokenType.Integer)throw new FormatException("Integer required: "+key);return state.Value<int>(key);}
    internal static double Number(JObject state,string key)
    {if(state[key]?.Type!=JTokenType.Float&&state[key]?.Type!=JTokenType.Integer)throw new FormatException("Number required: "+key);double value=state.Value<double?>(key)??throw new FormatException("Missing ability field: "+key);if(double.IsNaN(value)||double.IsInfinity(value)||value<0)throw new FormatException("Invalid ability timer.");return value;}
    internal static double SignedNumber(JObject state,string key)
    {double value=state.Value<double?>(key)??throw new FormatException("Missing ability reduction.");if(double.IsNaN(value)||double.IsInfinity(value))throw new FormatException("Invalid reduction.");return value;}
}
public sealed partial class PNAbilityCooldown
{
    internal JObject ExportCheckpoint()=>new JObject{["current"]=current,["maximum"]=maximum,["base"]=baseCooldown,["flat"]=reduceFlat,["percent"]=reducePercent,["remaining"]=remaining,["auto"]=autoRecover,["recovering"]=recovering};
    internal void ImportCheckpoint(JObject s)
    {
        current=PNAbilityCollection.Integer(s,"current");maximum=PNAbilityCollection.Integer(s,"maximum");if(maximum<1||current<0||current>maximum)throw new FormatException("Invalid charges.");
        baseCooldown=PNAbilityCollection.Number(s,"base");reduceFlat=PNAbilityCollection.Number(s,"flat");reducePercent=PNAbilityCollection.SignedNumber(s,"percent");remaining=PNAbilityCollection.Number(s,"remaining");autoRecover=PNAbilityCollection.Bool(s,"auto");recovering=PNAbilityCollection.Bool(s,"recovering");PNAbilityDefinition.CalculateCooldown(baseCooldown,reduceFlat,reducePercent);
        if(recovering&&(!autoRecover||current==maximum||remaining<=0)||!recovering&&remaining!=0)throw new FormatException("Inconsistent recovery.");
    }
}
public sealed partial class PNAbilityCounter
{
    internal JObject ExportCheckpoint()=>new JObject{["maximum"]=maximum,["grants"]=new JArray(grants.Select(g=>new JObject{["count"]=g.Count,["remaining"]=g.Remaining}))};
    internal void ImportCheckpoint(JObject s)
    {
        maximum=PNAbilityCollection.Integer(s,"maximum");if(maximum<1||!(s["grants"] is JArray values)||values.Count>4096)throw new FormatException("Invalid counter.");
        grants.Clear();current=0;
        foreach(var item in values.Cast<JObject>()){int amount=PNAbilityCollection.Integer(item,"count");double? remaining=item["remaining"].Type==JTokenType.Null?null:(double?)PNAbilityCollection.Number(item,"remaining");if(amount<1||remaining==0)throw new FormatException("Invalid grant.");current=checked(current+amount);grants.Add(new Grant{Id=checked(++nextGrant),Count=amount,Remaining=remaining});}
        if(current>maximum||Definition.Lifetime==PNAbilityLifetime.Shared&&grants.Count>1)throw new FormatException("Counter exceeds capacity.");
    }
}
public sealed partial class PNAbilityState
{
    internal JObject ExportCheckpoint()=>new JObject{["active"]=active,["duration"]=duration,["cooldown"]=cooldown,["remaining"]=cooldownRemaining,["flat"]=reduceFlat,["percent"]=reducePercent};
    internal void ImportCheckpoint(JObject s)
    {
        active=PNAbilityCollection.Bool(s,"active");duration=s["duration"].Type==JTokenType.Null?null:(double?)PNAbilityCollection.Number(s,"duration");cooldown=PNAbilityCollection.Number(s,"cooldown");cooldownRemaining=PNAbilityCollection.Number(s,"remaining");reduceFlat=PNAbilityCollection.Number(s,"flat");reducePercent=PNAbilityCollection.SignedNumber(s,"percent");PNAbilityDefinition.CalculateCooldown(cooldown,reduceFlat,reducePercent);
        if(duration==0||!active&&duration.HasValue)throw new FormatException("Invalid active duration.");
    }
}
#endif
