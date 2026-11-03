using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// The Starforger's registered combat data (Docs/StarforgerSkillsPlan.md): two skills, each with its own cooldown Ability (<skill id>_ready) and Action, the slam's damage Operation, the star bolt
// projectile and the star area. Every number lives in these payloads and nowhere else.
//   starforger_slam (10 s) : crouches `crouch` seconds (a warning ring shows the landing), jumps to the target's spot (a Hop trajectory of `hop.duration`, invulnerable and immune to control while
//                            airborne), slams on landing (`starforger_slam_hit`: radius 0.7 m, 300%, physical) and rests `recovery`. Every star shoots one extra bolt toward the landing at take-off.
//   starforger_star (5 s)  : gathers `cast` seconds and makes one star (at most `maxStars`; the skill is not used with all of them alive). A star is a dependent area: no health, lives `lifetime`,
//                            shoots a bolt (50%) at the nearest hostile within `range` every `interval`, its first one `firstDelay` + slot * `stagger` after it appeared.
public static class PNStarforgerCombatContent
{
    public static readonly EveId Slam = EveId.Parse("project_n:starforger_slam"), Star = EveId.Parse("project_n:starforger_star"),
        SlamAction = EveId.Parse("project_n:starforger_slam_action"), StarAction = EveId.Parse("project_n:starforger_star_action"),
        SlamHit = EveId.Parse("project_n:starforger_slam_hit"), Bolt = EveId.Parse("project_n:starforger_bolt"), StarArea = EveId.Parse("project_n:starforger_star_area");

    public static void Register(EveRegistryHub hub)
    {
        hub.Instances().Register(new PNProjectileDefinition(Bolt, typeof(PNStarforgerBoltProjectile),
            JObject.Parse(@"{'speed':4,'acceleration':0,'speedMin':0,'speedMax':4,'life':{'mode':'Time','value':3},'volume':{'size':[0.2,0.3,0.3]},'environmentMask':['Wall'],'hitLimit':1}"),
            PNDirectionalSettings.ValidateDefinition, PNDirectionalSettings.ValidateInput));
        hub.Instances().Register(new PNAreaDefinition(StarArea, typeof(PNStarforgerStarArea), new JObject(), (token, registry) => PNBuffContentBatch.Fields(PNBuffContentBatch.Object(token)), ValidateStarInput));
        hub.Operations().Register(new PNSkillOperationDefinition(SlamHit, typeof(PNStarforgerSlamHitOperation), ValidateSlamHit, PNSkillContextRequirements.Position));
        hub.Actions().Register(new PNSkillActionDefinition(SlamAction, typeof(PNStarforgerSlamAction), (a, h) => ValidateSlam(a, h), PNSkillContextRequirements.Direction | PNSkillContextRequirements.Position,
            (data, references) => references.Require<PNSkillOperationDefinition>(new JValue("base:lock_movement"), "movementLock")));
        hub.Actions().Register(new PNSkillActionDefinition(StarAction, typeof(PNStarforgerStarAction), (a, h) => ValidateStar(a, h), PNSkillContextRequirements.Direction));
        var skills = hub.GetOrCreate<EveSkillDefinition>();
        skills.Register(Skill(Slam, "base:skill", false, 10, SlamAction, JObject.Parse(@"{
          'range':3.5,'crouch':0.5,'recovery':0.8,
          'hop':{'kind':'Hop','distance':3.5,'height':2.0,'duration':0.8,'permissionId':'base:motion_voluntary','actorCollision':'IgnoreEnemyBodies','maxSegmentLength':0.12,'maxChordError':0.015,'maxSubsteps':64},
          'hit':{'radius':0.7,'height':0.6,'value':{'amount':300,'statId':'base:attack_power'},'canCrit':true,'tags':['base:physical']},
          'tags':['base:physical']}")));
        skills.Register(Skill(Star, "base:attack", false, 5, StarAction, JObject.Parse(@"{
          'range':4,'cast':0.8,'recovery':0.2,'maxStars':3,'orbit':1.2,'slotDegrees':120,
          'star':{'areaId':'project_n:starforger_star_area','followCaster':false,'input':{'lifetime':16,'firstDelay':1,'stagger':0.667,'interval':2,'range':3,'slot':0,
            'value':{'amount':50,'statId':'base:attack_power'},'canCrit':true,'tags':['base:magic']}},
          'tags':['base:magic']}")));
    }
    private static EveSkillDefinition Skill(EveId id, string category, bool attackSpeed, double cooldown, EveId action, JObject perform)
    {
        var ability = JObject.Parse(@"{'kind':'Cooldown','showInUI':false,'iconPath':'','displayOrder':0,'maximum':1,'initialAmount':1,'recovery':'PerStack','autoRecover':true,'clock':'Entity'}");
        ability["id"] = id + "_ready"; ability["cooldown"] = cooldown;
        return new EveSkillDefinition(id, new JObject
        {
            ["categoryIds"] = new JArray(category), ["affectedByAttackSpeed"] = attackSpeed, ["skill"] = new JObject { ["abilities"] = new JArray(ability) },
            ["action"] = new JObject { ["id"] = action.ToString(), ["start"] = new JObject(), ["perform"] = perform, ["exit"] = new JObject(), ["cancel"] = new JObject() }
        });
    }

    // ---- validation ----
    private static void Positive(JObject p, string key, double maximum)
    { double n = PNBuffContentBatch.Number(p, key); if (double.IsNaN(n) || double.IsInfinity(n) || n <= 0 || n > maximum) throw new FormatException("Invalid Starforger number: " + key); }
    private static void ValidateDamage(JObject p, EveRegistryHub hub)
    {
        if (p["canCrit"]?.Type != JTokenType.Boolean) throw new FormatException("canCrit required.");
        var values = PNSkillValues.Parse(p["value"]); values.ValidateStats(hub); values.ValidateNonnegativeAmounts();
        var tags = PNSkillEffect.Ids(p["tags"]).ToArray(); if (tags.Length == 0) throw new FormatException("Starforger damage needs tags.");
        new PNStatSet(hub).ResolveDamageType(tags);
    }
    public static void ValidateSlamHit(JToken token, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(p, "radius", "height", "value", "canCrit", "tags");
        Positive(p, "radius", 20); Positive(p, "height", 20); ValidateDamage(p, hub);
    }
    public static void ValidateSlam(PNSkillActionData data, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(data.Perform); PNBuffContentBatch.Fields(p, "range", "crouch", "recovery", "hop", "hit", "tags");
        Positive(p, "range", 30); Positive(p, "crouch", 10); Positive(p, "recovery", 10);
        var hop = PNBuffContentBatch.Object(p["hop"]);
#if UNITY_5_3_OR_NEWER
        var settings = PNAgentTrajectorySettings.Parse(hop, hub, false);
        if (settings.Kind != PNTrajectoryKind.Hop) throw new FormatException("The slam jump is a Hop.");
        if (Math.Abs(settings.Distance - PNBuffContentBatch.Number(p, "range")) > 1e-4) throw new FormatException("range must equal the hop distance.");
#else
        if (hop["kind"]?.Value<string>() != "Hop") throw new FormatException("The slam jump is a Hop.");
#endif
        ValidateSlamHit(p["hit"], hub);
        new PNStatSet(hub).ResolveDamageType(data.PerformTags);
    }
    public static void ValidateStar(PNSkillActionData data, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(data.Perform); PNBuffContentBatch.Fields(p, "range", "cast", "recovery", "maxStars", "orbit", "slotDegrees", "star", "tags");
        Positive(p, "range", 30); Positive(p, "cast", 10); Positive(p, "recovery", 10); Positive(p, "orbit", 20); Positive(p, "slotDegrees", 360);
        int max = PNBuffContentBatch.Integer(p, "maxStars"); if (max < 1 || max > 8) throw new FormatException("maxStars must be 1..8.");
        var star = PNBuffContentBatch.Object(p["star"]); PNBuffContentBatch.Fields(star, "areaId", "followCaster", "input");
        if (PNBuffContentBatch.String(star, "areaId") != StarArea.ToString() || PNBuffContentBatch.Bool(star, "followCaster")) throw new FormatException("The star skill creates the star area, not following the caster.");
        ValidateStarInput(star["input"]); ValidateDamage((JObject)star["input"], hub);
    }
    public static void ValidateStarInput(JToken token)
    {
        var o = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(o, "lifetime", "firstDelay", "stagger", "interval", "range", "slot", "value", "canCrit", "tags");
        Positive(o, "lifetime", 600); Positive(o, "firstDelay", 60); Positive(o, "interval", 60); Positive(o, "range", 100);
        if (PNBuffContentBatch.Number(o, "stagger") < 0) throw new FormatException("stagger must not be negative.");
        int slot = PNBuffContentBatch.Integer(o, "slot"); if (slot < 0 || slot > 7) throw new FormatException("slot must be 0..7.");
        PNSkillValues.Parse(o["value"]).ValidateNonnegativeAmounts(); PNSkillEffect.Ids(o["tags"]).ToArray();
    }
}
