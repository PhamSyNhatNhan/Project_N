using System;
using EveEngine;
using Newtonsoft.Json.Linq;

public static class PNStarweaverCombatContent
{
    public static readonly EveId Weave=EveId.Parse("project_n:starweaver_weave"), Triangle=EveId.Parse("project_n:starweaver_constellation"), Action=EveId.Parse("project_n:starweaver_cast"), Bolt=EveId.Parse("project_n:starweaver_bolt"), TriangleBlast=EveId.Parse("project_n:starweaver_triangle_blast");
    public static void Register(EveRegistryHub hub)
    {
        hub.Actions().Register(new PNSkillActionDefinition(Action,typeof(PNStarweaverCastAction),(a,h)=>ValidatePayload(a.Perform,h),PNSkillContextRequirements.Direction));
        hub.Instances().Register(new PNBlastDefinition(TriangleBlast,typeof(PNStarweaverTriangleBlast),new JObject(),(token,registry)=>PNBuffContentBatch.Fields(PNBuffContentBatch.Object(token)),ValidateTriangleInput));
        hub.Instances().Register(new PNProjectileDefinition(Bolt,typeof(PNStarweaverProjectile),JObject.Parse(@"{'speed':5,'acceleration':0,'speedMin':0,'speedMax':5,'life':{'mode':'Time','value':1.2},'volume':{'size':[0.10,0.14,0.16]},'environmentMask':['Wall'],'hitLimit':1}"),PNDirectionalSettings.ValidateDefinition,PNDirectionalSettings.ValidateInput));
        foreach(bool triangle in new[]{false,true})
        {
            var cooldown=triangle?8:3;var id=triangle?Triangle:Weave;
            var perform=JObject.Parse(@"{'windup':0.5,'recovery':0.35,'operationId':'project_n:create_projectile','data':{'projectileId':'project_n:starweaver_bolt','input':{},'count':3,'spread':18,'damage':{'value':{'amount':150,'statId':'base:attack_power'},'canCrit':true,'tags':['base:magic']}}}");
            if(triangle){perform["windup"]=2.0;perform["recovery"]=1.2;perform["operationId"]=PNBlastGameplayContent.CreateBlast.ToString();perform["data"]=JObject.Parse(@"{'blastId':'project_n:starweaver_triangle_blast','dependency':'Action','input':{'delay':2.0,'tail':1.2,'radius':1.2,'height':0.6,'sides':3,'rotation':0,'value':{'amount':250,'statId':'base:attack_power'},'canCrit':true,'tags':['base:magic']}}");}
            perform["tags"]=new JArray("base:magic");
            var ability=JObject.Parse(@"{'kind':'Cooldown','showInUI':false,'iconPath':'','displayOrder':0,'maximum':1,'initialAmount':1,'recovery':'PerStack','autoRecover':true,'clock':'Entity'}");ability["id"]=id+"_ready";ability["cooldown"]=cooldown;
            hub.GetOrCreate<EveSkillDefinition>().Register(new EveSkillDefinition(id,new JObject{["categoryIds"]=new JArray(triangle?"base:skill":"base:attack"),["affectedByAttackSpeed"]=!triangle,["skill"]=new JObject{["abilities"]=new JArray(ability)},["action"]=new JObject{["id"]=Action.ToString(),["start"]=new JObject(),["perform"]=perform,["exit"]=new JObject(),["cancel"]=new JObject()}}));
        }
    }
    public static void ValidatePayload(JToken token,EveRegistryHub hub)
    {
        var p=PNBuffContentBatch.Object(token);PNBuffContentBatch.Fields(p,"windup","recovery","operationId","data","tags");
        foreach(var key in new[]{"windup","recovery"}){double n=PNBuffContentBatch.Number(p,key);if(n<=0||n>30)throw new FormatException("Invalid Starweaver timing");}
        var id=EveId.Parse(PNBuffContentBatch.String(p,"operationId"));hub.Operations().GetEffective(id).ValidatePayload(p["data"],hub);
        if(id==PNBlastGameplayContent.CreateBlast)
        {
            var blast=PNBuffContentBatch.Object(p["data"]);var input=PNBuffContentBatch.Object(blast["input"]);
            if(blast["blastId"]?.Value<string>()!=TriangleBlast.ToString()||blast["dependency"]?.Value<string>()!="Action")throw new FormatException("The constellation creates its triangle blast, dependent on the cast.");
            if(Math.Abs(input["delay"].Value<double>()-p["windup"].Value<double>())>1e-6)throw new FormatException("The triangle blast's delay must equal the cast's windup.");
        }
    }
    // The triangle blast is a 3-sided Polygon Blast whose delay is the cast's windup (one authoritative number, checked in ValidatePayload).
    public static void ValidateTriangleInput(JToken token)
    {
        PNBlastSchema.ValidatePolygon(token);
        if(PNBuffContentBatch.Integer(PNBuffContentBatch.Object(token),"sides")!=3)throw new FormatException("The constellation is a triangle: sides must be 3.");
    }
}
