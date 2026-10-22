using EveEngine;
using Newtonsoft.Json.Linq;

// Concrete shared enemy Actions use the existing selected implementations.
public static class PNEnemyGameplayContent
{
    public static readonly EveId Strike = EveId.Parse("project_n:enemy_strike");
    public static readonly EveId Charge = EveId.Parse("project_n:enemy_charge");
    public static readonly EveId Recovery = EveId.Parse("project_n:enemy_attack_ready");
    public static void Register(EveRegistryHub hub)
    {
        var skills = hub.GetOrCreate<EveSkillDefinition>();
        skills.Register(new EveSkillDefinition(Strike, JObject.Parse(@"{
          'categoryIds':['base:attack'],'affectedByAttackSpeed':true,
          'skill':{'abilities':[{'id':'project_n:enemy_attack_ready','kind':'Cooldown','showInUI':false,'iconPath':'','displayOrder':0,
            'maximum':1,'initialAmount':1,'cooldown':1.2,'recovery':'PerStack','autoRecover':true,'clock':'Entity'}]},
          'action':{'id':'base:damage_action','start':{},'perform':{'value':{'amount':1000},'tags':['base:physical','base:attack'],'canCrit':false},'exit':{},'cancel':{}}
        }")));
        skills.Register(new EveSkillDefinition(Charge, JObject.Parse(@"{
          'categoryIds':['base:skill'],'affectedByAttackSpeed':false,
          'action':{'id':'base:motion_action','start':{},'perform':{'distance':2,'duration':0.6,'ignoreEntities':false,'permissionId':'base:motion_voluntary'},'exit':{},'cancel':{}}
        }")));
    }
}
