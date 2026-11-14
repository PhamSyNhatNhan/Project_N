using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// The Starreaper's registered combat data (Docs/StarreaperSkillsPlan.md): three skills, each with its own cooldown Ability (<skill id>_ready) and Action, the arrow projectile and the two areas
// (the Tàn Ảnh shadow and the Mưa Tinh rain). Every number lives in these payloads and nowhere else; the Actions and areas read them.
//   starreaper_pierce      (8 s)  : Xuyên Tinh  - aims `windup` seconds, then shoots one arrow at the target (re-aimed at the shot); category attack, 250%.
//   starreaper_afterimage  (20 s) : Tàn Ảnh     - retreats `motion.distance` with a shadow left at the old spot, shoots the pierce arrow, the shadow shoots two arrows `delay` seconds later (120% each).
//   starreaper_rain        (12 s) : Mưa Tinh     - gathers `windup` seconds, shoots one arrow into the sky; the rain area warns the landing spots at once and lands 5-7 arrows (150% each, category skill).
public static class PNStarreaperCombatContent
{
    public static readonly EveId Pierce = EveId.Parse("project_n:starreaper_pierce"), Afterimage = EveId.Parse("project_n:starreaper_afterimage"), Rain = EveId.Parse("project_n:starreaper_rain"),
        PierceAction = EveId.Parse("project_n:starreaper_pierce_action"), AfterimageAction = EveId.Parse("project_n:starreaper_afterimage_action"), RainAction = EveId.Parse("project_n:starreaper_rain_action"),
        Arrow = EveId.Parse("project_n:starreaper_arrow"), EchoArrow = EveId.Parse("project_n:starreaper_echo_arrow"), Shadow = EveId.Parse("project_n:starreaper_shadow"), RainBlast = EveId.Parse("project_n:starreaper_rain_blast"), RainOperation = EveId.Parse("project_n:starreaper_rain_blasts");

    private const string ArrowShot = @"{'projectileId':'project_n:starreaper_arrow','input':{},'count':1,'spread':0,'aimRadius':12,
          'damage':{'value':{'amount':250,'statId':'base:attack_power'},'canCrit':true,'tags':['base:magic']}}";

    // Mob-private presentation input; physical directional settings are unchanged.
    public static void ValidateArrowInput(JToken input)
    {
        if(input==null||input.Type==JTokenType.Null)return;
        var obj=PNBuffContentBatch.Object(input);PNBuffContentBatch.Fields(obj,"echoVisual");
        if(obj["echoVisual"]!=null)PNBuffContentBatch.Bool(obj,"echoVisual");
    }

    public static void Register(EveRegistryHub hub)
    {
        var instances = hub.Instances();
        instances.Register(new PNProjectileDefinition(Arrow, typeof(PNStarreaperProjectile),
            JObject.Parse(@"{'speed':4,'acceleration':0,'speedMin':0,'speedMax':4,'life':{'mode':'Time','value':5},'volume':{'size':[0.5,0.3,0.5]},'environmentMask':[],'hitLimit':100000}"),
            PNDirectionalSettings.ValidateDefinition, ValidateArrowInput));
        var echoArrowData=(JObject)instances.GetKind<PNProjectileDefinition>(Arrow).Payload.DeepClone();
        echoArrowData["speed"]=2;echoArrowData["speedMax"]=2;
        instances.Register(new PNProjectileDefinition(EchoArrow,typeof(PNStarreaperProjectile),echoArrowData,PNDirectionalSettings.ValidateDefinition,ValidateArrowInput));
        instances.Register(new PNBlastDefinition(RainBlast, typeof(PNStarreaperRainBlast), new JObject(), (token, registry) => PNBuffContentBatch.Fields(PNBuffContentBatch.Object(token)), ValidateRainBlastInput));
        hub.Operations().Register(new PNSkillOperationDefinition(RainOperation, typeof(PNStarreaperRainOperation), (token, registry) => { ValidateRainInput(token); ValidateDamage(PNBuffContentBatch.Object(token), registry); }, PNSkillContextRequirements.Position));
        hub.Actions().Register(new PNSkillActionDefinition(PierceAction, typeof(PNStarreaperPierceAction), (a, h) => ValidatePierce(a, h), PNSkillContextRequirements.Direction | PNSkillContextRequirements.Position));
        hub.Actions().Register(new PNSkillActionDefinition(AfterimageAction, typeof(PNStarreaperAfterimageAction), (a, h) => ValidateAfterimage(a, h),
            PNSkillContextRequirements.Direction | PNSkillContextRequirements.Position,
            (data, references) => references.Require<PNSkillOperationDefinition>(new JValue("base:lock_movement"), "movementLock")));
        hub.Actions().Register(new PNSkillActionDefinition(RainAction, typeof(PNStarreaperRainAction), (a, h) => ValidateRain(a, h), PNSkillContextRequirements.Direction | PNSkillContextRequirements.Position));
        var skills = hub.GetOrCreate<EveSkillDefinition>();
        skills.Register(Skill(Pierce, "base:attack", true, 8, PierceAction, JObject.Parse(@"{
          'windup':1.5,'recovery':0.4,'range':3.0,'shot':" + ArrowShot + @",'tags':['base:magic']}")));
        skills.Register(Skill(Afterimage, "base:attack", false, 20, AfterimageAction, JObject.Parse(@"{
          'range':1.5,'recovery':0.4,
          'motion':{'kind':'CurveXZ','distance':1.5,'lateralOffset':0,'duration':0.16,'permissionId':'base:motion_voluntary','actorCollision':'IgnoreEnemyBodies','maxSegmentLength':0.12,'maxChordError':0.015,'maxSubsteps':64},
          'shot':" + ArrowShot + @",
          'summon':{'character':'project_n:starreaper_shadow','group':'starreaper_shadow','limit':1,'whenFull':'replaceOldest'},
          'tags':['base:magic']}")));
        skills.Register(Skill(Rain, "base:skill", false, 12, RainAction, JObject.Parse(@"{
          'windup':2.0,'recovery':0.5,'range':2.5,
          'rain':{'seek':6,'flight':1.0,'interval':0.15,'tail':0.45,'minCount':5,'maxCount':7,'scatterRadius':2.0,'minSpacing':0.8,'radius':0.5,'height':0.5,
            'value':{'amount':150,'statId':'base:attack_power'},'canCrit':true,'tags':['base:magic']},
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
    { double n = PNBuffContentBatch.Number(p, key); if (double.IsNaN(n) || double.IsInfinity(n) || n <= 0 || n > maximum) throw new FormatException("Invalid Starreaper number: " + key); }
    private static void ValidateDamage(JObject p, EveRegistryHub hub)
    {
        if (p["canCrit"]?.Type != JTokenType.Boolean) throw new FormatException("canCrit required.");
        var values = PNSkillValues.Parse(p["value"]); values.ValidateStats(hub); values.ValidateNonnegativeAmounts();
        var tags = PNSkillEffect.Ids(p["tags"]).ToArray(); if (tags.Length == 0) throw new FormatException("Starreaper damage needs tags.");
        new PNStatSet(hub).ResolveDamageType(tags);
    }
    private static void ValidateShot(JToken shot, EveRegistryHub hub) => PNCreateProjectileOperation.Validate(shot, hub);
    public static void ValidatePierce(PNSkillActionData data, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(data.Perform); PNBuffContentBatch.Fields(p, "windup", "recovery", "range", "shot", "tags");
        Positive(p, "windup", 10); Positive(p, "recovery", 10); Positive(p, "range", 30); ValidateShot(p["shot"], hub);
    }
    public static void ValidateAfterimage(PNSkillActionData data, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(data.Perform); PNBuffContentBatch.Fields(p, "range", "recovery", "motion", "shot", "summon", "tags");
        Positive(p, "range", 30); Positive(p, "recovery", 10); ValidateShot(p["shot"], hub);
        var motion = PNBuffContentBatch.Object(p["motion"]);
#if UNITY_5_3_OR_NEWER
        var settings = PNAgentTrajectorySettings.Parse(motion, hub, false);
        if (settings.Kind != PNTrajectoryKind.CurveXZ || settings.LateralOffset != 0) throw new FormatException("The retreat is a straight CurveXZ.");
#else
        if (motion["kind"]?.Value<string>() != "CurveXZ") throw new FormatException("The retreat is a straight CurveXZ.");
#endif
        // The shadow is a summon (Docs/StrikeBlastPlan.md U1): the skill only says which character and how many may live; what the shadow does is the character's `shadow` block (ValidateCharacters).
        var summon = PNCreateSummonSettings.Parse(p["summon"]);
        if (summon.Character != Shadow || summon.Limit != 1 || summon.Lifetime.HasValue) throw new FormatException("The afterimage summons its shadow: character " + Shadow + ", limit 1, no lifetime (the shadow ends itself).");
    }
    public static void ValidateRain(PNSkillActionData data, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(data.Perform); PNBuffContentBatch.Fields(p, "windup", "recovery", "range", "rain", "tags");
        Positive(p, "windup", 10); Positive(p, "recovery", 10); Positive(p, "range", 30);
        ValidateRainInput(p["rain"]); ValidateDamage((JObject)p["rain"], hub);
    }
    public static void ValidateRainInput(JToken token)
    {
        var o = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(o, "seek", "flight", "interval", "tail", "minCount", "maxCount", "scatterRadius", "minSpacing", "radius", "height", "value", "canCrit", "tags");
        Positive(o, "seek", 100); Positive(o, "flight", 30); Positive(o, "interval", 10); Positive(o, "scatterRadius", 50); Positive(o, "radius", 20); Positive(o, "height", 20);
        if (PNBuffContentBatch.Number(o, "tail") < 0) throw new FormatException("tail must not be negative.");
        if (PNBuffContentBatch.Number(o, "minSpacing") < 0) throw new FormatException("minSpacing must not be negative.");
        int min = PNBuffContentBatch.Integer(o, "minCount"), max = PNBuffContentBatch.Integer(o, "maxCount");
        if (min < 1 || max < min || max > 16) throw new FormatException("The rain count must be 1 <= minCount <= maxCount <= 16.");
        PNSkillValues.Parse(o["value"]).ValidateNonnegativeAmounts(); PNSkillEffect.Ids(o["tags"]).ToArray();
    }
    // One landing of the rain: the common blast fields plus the landing's number and whether it draws the arrow shot into the sky.
    public static void ValidateRainBlastInput(JToken token)
    {
        var o = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(o, PNBlastSchema.Common.Concat(new[] { "index", "sky" }).ToArray());
        PNBlastSchema.ValidateCommon(o); if (PNBuffContentBatch.Integer(o, "index") < 0) throw new FormatException("index must not be negative."); PNBuffContentBatch.Bool(o, "sky");
    }
    // Content check: the `shadow` block of every character that has one (the shadow summon's numbers: delay, gap, sideAngle, aimRadius, appearance, damage).
    public static void ValidateCharacters(EveRegistryHub hub)
    {
        foreach (var character in hub.Characters.Snapshot().Values)
        {
            var block = (character.Data as JObject)?["shadow"]; if (block == null) continue;
            try { ValidateShadowInput(block); ValidateDamage((JObject)block, hub); }
            catch (FormatException error) { throw new FormatException("Character " + character.Id + " shadow block: " + error.Message); }
        }
    }
    public static void ValidateShadowInput(JToken token)
    {
        var o = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(o, "delay", "gap", "sideAngle", "aimRadius", "appearance", "value", "canCrit", "tags");
        Positive(o, "delay", 30); Positive(o, "gap", 10); Positive(o, "aimRadius", 100);
        double angle = PNBuffContentBatch.Number(o, "sideAngle"); if (double.IsNaN(angle) || angle < 0 || angle > 90) throw new FormatException("sideAngle must be 0..90 degrees.");
        EveId.Parse(PNBuffContentBatch.String(o, "appearance"));
        PNSkillValues.Parse(o["value"]).ValidateNonnegativeAmounts(); PNSkillEffect.Ids(o["tags"]).ToArray();
    }
}
