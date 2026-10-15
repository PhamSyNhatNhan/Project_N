using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using static EveSessionSmokeTests;

public class PNFlatCustomCalculator : PNStatCalculation
{
    public override double Calculate(PNStatCalculationContext c) => c.BaseValue + c.Modifiers.Sum(m => m.Value * 2);
}
public static class PNFlatContributionTests
{
    private class ForeignFlatModule : EveIContentModule
    {
        public void Register(EveRegistryHub h)
        {
            h.Contributions().Register(new PNStatContributionDefinition(EveId.Parse("foreign:flat")));
            h.Calculations().Register(new PNStatCalculationDefinition(EveId.Parse("test:custom_flat_calc"),typeof(PNFlatCustomCalculator)));
        }
    }
    public static void Run()
    {
        var hub=new EveRegistryHub();new EveBuiltInContentModule().Register(hub);new PNContentModule().Register(hub);new PNGameplayContentModule().Register(hub);
        var custom=EveId.Parse("test:custom_flat");var calc=EveId.Parse("test:custom_flat_calc");
        hub.Contributions().Register(new PNStatContributionDefinition(custom));
        hub.Calculations().Register(new PNStatCalculationDefinition(calc,typeof(PNFlatCustomCalculator)));hub.Seal();
        Check(hub.Contributions().Get(EveId.Parse("base:before")).Id==PNContentIds.Before && hub.Contributions().Get(EveId.Parse("base:after")).Id==PNContentIds.After,"Contribution aliases resolve metadata definitions");
        Reject(()=>new PNStatContributionDefinition(default),"Invalid contribution definition rejected");
        Reject(()=>hub.Contributions().Register(new PNStatContributionDefinition(EveId.Parse("test:late"))),"Contribution registry sealed");
        var set=new PNStatSet(hub);set.AddBase(new EveBaseStat(PNContentIds.AttackPower,100));set.AddBase(new EveResourceStat(PNContentIds.Health,100,40,EveMaxChangePolicy.PreserveRatio));set.AddBase(new EveBaseStat(PNContentIds.Defense,0));
        var source=EveId.Parse("test:flats");
        set.SetSource(source,new[]{new EveStatModifier(PNContentIds.AttackPower,10,0),new EveStatModifier(PNContentIds.AttackPower,.2,1)});
        Check(Math.Abs(set.Evaluate(PNContentIds.AttackPower)-130)<1e-9,"Default stat flat is after percentages");
        set.SetSource(source,new[]{new EveStatModifier(PNContentIds.AttackPower,10,0,PNContentIds.Scale,EveId.Parse("base:before")),new EveStatModifier(PNContentIds.AttackPower,.2,1)});
        Check(Math.Abs(set.Evaluate(PNContentIds.AttackPower)-132)<1e-9,"Explicit before stat flat");
        foreach(var route in new[]{PNContentIds.Scale,PNContentIds.Mitigation})foreach(var tag in new[]{PNContentIds.Fire,PNContentIds.Heal})
        {
            set.SetSource(source,new[]{new EveStatModifier(tag,10,0,route,PNContentIds.Before),new EveStatModifier(tag,20,0,route),new EveStatModifier(tag,.5,1,route)});
            Check(Math.Abs(set.Calculate(route,100,tag,tag)-185)<1e-9,"Before/after flats and tag dedup "+route+" "+tag);
            Check(set.Evaluate(tag)==0,"Flat combat selector does not create a base stat "+route+" "+tag);
            set.SetSource(source,new[]{new EveStatModifier(tag,10,0,route),new EveStatModifier(tag,-1,1,route)});
            Check(set.Calculate(route,100,tag)==10,"After flat survives zero percentage factor "+route+" "+tag);
            set.SetSource(source,new[]{new EveStatModifier(tag,-200,0,route)});
            Check(set.Calculate(route,100,tag)==0,"Negative flat result clamps "+route+" "+tag);
        }
        set.SetSource(source,new[]{new EveStatModifier(PNContentIds.Defense,-10,0)});Check(set.Evaluate(PNContentIds.Defense)==-10,"Signed DEF policy preserved");
        set.SetSource(source,new[]{new EveStatModifier(PNContentIds.Health,10,0),new EveStatModifier(PNContentIds.Health,.2,1)});
        Check(set.GetMax(PNContentIds.Health)==130 && set.GetCurrent(PNContentIds.Health)==52,"Default after resource flat preserves ratio");
        Reject(()=>set.SetSource(source,new[]{new EveStatModifier(PNContentIds.Health,10,0,PNContentIds.Scale,custom)}),"Unsupported registered contribution rejected by default calculator");
        Check(set.GetMax(PNContentIds.Health)==130 && set.GetCurrent(PNContentIds.Health)==52,"Rejected contribution preserves resource/source state");
        Reject(()=>new EveStatModifier(PNContentIds.Fire,1,1,PNContentIds.Scale,PNContentIds.Before),"Contribution forbidden on percentage group");
        Reject(()=>set.SetSource(source,new[]{new EveStatModifier(PNContentIds.Fire,10,0,PNContentIds.Scale,EveId.Parse("test:missing"))}),"Unknown contribution rejected");
        var parsed=PNStatModifierData.Parse(JObject.Parse("{'statId':'base:fire','value':10,'group':0,'calculationId':'base:mitigation','contributionId':'base:before'}"));
        Check(parsed.Clone().ContributionId==parsed.ContributionId && parsed.Clone().CalculationId==parsed.CalculationId,"Parser and Clone preserve both routing IDs");
        var omitted=PNStatModifierData.Parse(JObject.Parse("{'statId':'base:fire','value':10,'group':0}"));
        Check(!omitted.CalculationId.IsValid && !omitted.ContributionId.IsValid && set.ResolveContributionId(omitted)==PNContentIds.After,"Omitted routing IDs use calculator default and after");
        Reject(()=>PNStatModifierData.Parse(JObject.Parse("{'statId':'base:fire','value':10,'group':1,'contributionId':null}")),"Presence of contribution field on group1 rejected even null");
        Reject(()=>PNStatModifierData.Parse(JObject.Parse("{'statId':'base:fire','value':10,'group':0,'contributionId':''}")),"Empty contribution rejected");
        set.SetSource(source,new[]{new EveStatModifier(PNContentIds.Fire,10,0,calc,custom)});Check(set.Calculate(calc,100,PNContentIds.Fire)==120,"Custom calculator interprets mod metadata without external math");
        var fingerprint=EveContentCompatibility.Create(hub,Array.Empty<EveContentDocument>(),"test:flat");
        Check(fingerprint.ContentHashes.ContainsKey("registry:"+typeof(PNStatContributionDefinition).FullName),"Contribution definitions participate in content hash");
        string PayloadHash(string contribution)
        {
            var h=new EveRegistryHub();new PNContentModule().Register(h);new PNGameplayContentModule().Register(h);
            var payload=JObject.Parse("{'duration':5,'clock':'Game','statId':'base:fire','value':10,'group':0,'maximumStacks':3}");payload["contributionId"]=contribution;
            var parent=h.Buffs().Get(EveId.Parse("base:buff_stat_template"));
            h.Buffs().Register(parent.Derive(EveId.Parse("test:hash_flat"),payload,parent.Tags,false,1,PNBuffInstanceScope.Target,true));h.Seal();
            return EveContentCompatibility.Create(h,Array.Empty<EveContentDocument>(),"test:flat_hash").ContentHashes["registry:"+typeof(PNBuffDefinition).FullName];
        }
        Check(PayloadHash("base:before")!=PayloadHash("base:after"),"Changing only contributionId changes Buff content fingerprint");
        var modules=new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule(),new ForeignFlatModule()};
        const string json="{'schemaVersion':1,'extensions':{'project_n':{'buffs':[{'id':'test:json_flat','factoryId':'base:buff_stat_template','data':{'duration':5,'clock':'Game','statId':'base:fire','value':10,'group':0,'maximumStacks':3,'calculationId':'test:custom_flat_calc','contributionId':'foreign:flat'},'tags':['base:buff'],'multipleInstances':false,'maximumInstances':1,'scope':'Target','canDispel':true,'showInUI':false}]}}}";
        Reject(()=>new EveContentBootstrap().Build(modules,new[]{new EveContentDocument("flat",json,"test",dependencies:new[]{"project_n"})}),"Foreign contribution requires declared pack dependency");
        var valid=new EveContentBootstrap().Build(modules,new[]{new EveContentDocument("flat",json,"test",dependencies:new[]{"project_n","foreign"})});
        valid.Buffs().Get(EveId.Parse("test:json_flat")).Validate(valid);
        Check(valid.Buffs().Get(EveId.Parse("test:json_flat")).Payload["contributionId"].Value<string>()=="foreign:flat","JSON Buff custom contribution reference validated and retained");
    }
}
