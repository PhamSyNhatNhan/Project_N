using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// The Starchanter's registered combat data (Docs/StarchanterSkillsPlan.md): two support skills, each with its own cooldown Ability (<skill id>_ready) and one registered Action.
//   starchanter_heal  (16 s) : teleports to the hurt ally with the lowest health, stands still, takes a block_hits guard and heals a zone for `duration` seconds (`tickPercent` of each
//                              recipient's maximum health every `interval`, `finalPercent` at the end). Losing the guard cancels the cast and skips the final heal.
//   starchanter_strike (5 s)  : a close melee hit (1000% attack power, physical), used only when the Starchanter has no ally left in sight.
//   starchanter_boost (10 s) : the behavior walks to the nearest ally first; the Action then gives speed_boost to the allies (and itself) within `radius`.
// Numbers are the plan's starting values, tuned here and nowhere else; the user rebalances later.
public static class PNStarchanterCombatContent
{
    public static readonly EveId Heal = EveId.Parse("project_n:starchanter_heal"), Boost = EveId.Parse("project_n:starchanter_boost"), Strike = EveId.Parse("project_n:starchanter_strike"),
        HealAction = EveId.Parse("project_n:starchanter_heal_action"), BoostAction = EveId.Parse("project_n:starchanter_boost_action");
    public static void Register(EveRegistryHub hub)
    {
        hub.Actions().Register(new PNSkillActionDefinition(HealAction, typeof(PNStarchanterHealAction), (a, h) => ValidateHeal(a, h), PNSkillContextRequirements.Direction | PNSkillContextRequirements.Position,
            (data, references) =>
            {
                references.Require<PNSkillOperationDefinition>(new JValue("base:lock_movement"), "movementLock");
                references.Require<PNBuffDefinition>(data["guard"]["buffId"], "guardBuff");
            }));
        hub.Actions().Register(new PNSkillActionDefinition(BoostAction, typeof(PNStarchanterBoostAction), (a, h) => ValidateBoost(a, h), PNSkillContextRequirements.Direction,
            (data, references) =>
            {
                references.Require<PNSkillOperationDefinition>(new JValue("base:lock_movement"), "movementLock");
                references.Require<PNBuffDefinition>(data["buffId"], "boostBuff");
            }));
        var skills = hub.GetOrCreate<EveSkillDefinition>();
        skills.Register(Skill(Heal, 16, HealAction, JObject.Parse(@"{
          'teleportOut':0.6,'teleportIn':0.7,'duration':5,'interval':1.5,'tickPercent':0.05,'finalPercent':0.10,'radius':2,'landingDistance':0.8,'recovery':0.4,
          'guard':{'buffId':'project_n:block_hits','duration':5.5,'hits':2}}")));
        // The strike (only when it has no ally left): a close melee hit through the shared damage Action, 1000% attack power, physical, classified as an attack (so attack speed applies).
        skills.Register(StrikeSkill(5));
        skills.Register(Skill(Boost, 10, BoostAction, JObject.Parse(@"{
          'windup':0.3,'recovery':0.3,'radius':2,'timeout':4,'seek':8,'buffId':'project_n:speed_boost'}")));
    }
    private static EveSkillDefinition StrikeSkill(double cooldown)
    {
        var ability = JObject.Parse(@"{'kind':'Cooldown','showInUI':false,'iconPath':'','displayOrder':0,'maximum':1,'initialAmount':1,'recovery':'PerStack','autoRecover':true,'clock':'Entity'}");
        ability["id"] = Strike + "_ready"; ability["cooldown"] = cooldown;
        return new EveSkillDefinition(Strike, new JObject
        {
            ["categoryIds"] = new JArray("base:attack"), ["affectedByAttackSpeed"] = true, ["skill"] = new JObject { ["abilities"] = new JArray(ability) },
            ["action"] = new JObject { ["id"] = "base:damage_action", ["start"] = new JObject(), ["perform"] = JObject.Parse("{'value':{'amount':1000,'statId':'base:attack_power'},'tags':['base:physical'],'canCrit':false}"), ["exit"] = new JObject(), ["cancel"] = new JObject() }
        });
    }
    private static EveSkillDefinition Skill(EveId id, double cooldown, EveId action, JObject perform)
    {
        var ability = JObject.Parse(@"{'kind':'Cooldown','showInUI':false,'iconPath':'','displayOrder':0,'maximum':1,'initialAmount':1,'recovery':'PerStack','autoRecover':true,'clock':'Entity'}");
        ability["id"] = id + "_ready"; ability["cooldown"] = cooldown;
        return new EveSkillDefinition(id, new JObject
        {
            ["categoryIds"] = new JArray("base:skill"), ["affectedByAttackSpeed"] = false, ["skill"] = new JObject { ["abilities"] = new JArray(ability) },
            ["action"] = new JObject { ["id"] = action.ToString(), ["start"] = new JObject(), ["perform"] = perform, ["exit"] = new JObject(), ["cancel"] = new JObject() }
        });
    }
    private static void Positive(JObject p, string key, double maximum)
    { double n = PNBuffContentBatch.Number(p, key); if (double.IsNaN(n) || double.IsInfinity(n) || n <= 0 || n > maximum) throw new FormatException("Invalid Starchanter number: " + key); }
    private static void BuffId(JObject p, string key, EveRegistryHub hub)
    {
        if (p[key]?.Type != JTokenType.String) throw new FormatException("Starchanter " + key + " must be a Buff id.");
        var id = EveId.Parse(p[key].Value<string>()); if (!id.IsValid) throw new FormatException("Starchanter " + key + " is not a valid id.");
    }
    public static void ValidateHeal(PNSkillActionData data, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(data.Perform);
        PNBuffContentBatch.Fields(p, "teleportOut", "teleportIn", "duration", "interval", "tickPercent", "finalPercent", "radius", "landingDistance", "recovery", "guard");
        Positive(p, "teleportOut", 10); Positive(p, "teleportIn", 10); Positive(p, "duration", 60); Positive(p, "interval", 60); Positive(p, "tickPercent", 1); Positive(p, "finalPercent", 1);
        Positive(p, "radius", 20); Positive(p, "landingDistance", 10); Positive(p, "recovery", 10);
        if (PNBuffContentBatch.Number(p, "interval") >= PNBuffContentBatch.Number(p, "duration")) throw new FormatException("The heal interval must be shorter than the duration.");
        var guard = PNBuffContentBatch.Object(p["guard"]); PNBuffContentBatch.Fields(guard, "buffId", "duration", "hits");
        BuffId(guard, "buffId", hub); Positive(guard, "duration", 600);
        int hits = PNBuffContentBatch.Integer(guard, "hits"); if (hits < 1 || hits > 1000) throw new FormatException("Invalid Starchanter guard hits.");
        if (PNBuffContentBatch.Number(guard, "duration") < PNBuffContentBatch.Number(p, "duration")) throw new FormatException("The guard must last at least as long as the heal zone.");
    }
    public static void ValidateBoost(PNSkillActionData data, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(data.Perform);
        PNBuffContentBatch.Fields(p, "windup", "recovery", "radius", "timeout", "seek", "buffId");
        Positive(p, "windup", 10); Positive(p, "recovery", 10); Positive(p, "radius", 20); Positive(p, "timeout", 60); Positive(p, "seek", 100);
        BuffId(p, "buffId", hub);
    }
}
