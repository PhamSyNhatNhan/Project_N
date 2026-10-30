using EveEngine;
using Newtonsoft.Json.Linq;

// The Starkeeper's registered combat data. PLACEHOLDER numbers (the real moveset is not designed yet): one melee slash, 120% attack power physical, 1.5 s cooldown.
public static class PNStarkeeperCombatContent
{
    public static readonly EveId Slash = EveId.Parse("project_n:starkeeper_slash");
    public static void Register(EveRegistryHub hub)
    {
        // Same ability id as the shared enemy melee behavior reads (PNEnemyGameplayContent.Recovery), so the shared state machine gates the slash on it.
        hub.GetOrCreate<EveSkillDefinition>().Register(new EveSkillDefinition(Slash, JObject.Parse(@"{
          'categoryIds':['base:attack'],'affectedByAttackSpeed':true,
          'skill':{'abilities':[{'id':'project_n:enemy_attack_ready','kind':'Cooldown','showInUI':false,'iconPath':'','displayOrder':0,
            'maximum':1,'initialAmount':1,'cooldown':1.5,'recovery':'PerStack','autoRecover':true,'clock':'Entity'}]},
          'action':{'id':'base:damage_action','start':{},'perform':{'value':{'amount':120,'statId':'base:attack_power'},'tags':['base:physical','base:attack'],'canCrit':true},'exit':{},'cancel':{}}
        }")));
    }
}
