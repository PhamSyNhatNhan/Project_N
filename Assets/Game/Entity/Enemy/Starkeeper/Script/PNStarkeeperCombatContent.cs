using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// The Starkeeper's registered combat data: two skills, each with its own cooldown Ability (<skill id>_ready), each running one registered Action.
// Numbers are the plan's starting values (Docs/StarkeeperImplementationPlan.md section 4), tuned here and nowhere else.
public static class PNStarkeeperCombatContent
{
    public static readonly EveId StarSweep = EveId.Parse("project_n:starkeeper_star_sweep"), StarPoint = EveId.Parse("project_n:starkeeper_star_point"),
        SweepAction = EveId.Parse("project_n:starkeeper_sweep_action"), StarPointAction = EveId.Parse("project_n:starkeeper_star_point_action");
    public static void Register(EveRegistryHub hub)
    {
        hub.Actions().Register(new PNSkillActionDefinition(SweepAction, typeof(PNStarkeeperSweepAction), (a, h) => ValidateSweep(a, h), PNSkillContextRequirements.Direction));
        hub.Actions().Register(new PNSkillActionDefinition(StarPointAction, typeof(PNStarkeeperStarPointAction), (a, h) => ValidateStarPoint(a, h),
            PNSkillContextRequirements.Direction | PNSkillContextRequirements.Position,
            (data, references) => references.Require<PNSkillOperationDefinition>(new JValue("base:lock_movement"), "movementLock")));
        var skills = hub.GetOrCreate<EveSkillDefinition>();
        skills.Register(Skill(StarSweep, "base:attack", true, 3, SweepAction, JObject.Parse(@"{
          'windup':0.4,'gap':0.35,'reaimAt':0.2,'recovery':0.6,'maxReaimDegrees':70,'radius':1.4,'angle':100,'thickness':0.6,
          'step':{'distance':0.5,'duration':0.12,'stopDistance':0.55,'permissionId':'base:motion_voluntary','actorCollision':'IgnoreEnemyBodies'},
          'hits':[{'value':{'amount':80,'statId':'base:attack_power'},'iframeDuration':0.2},{'value':{'amount':110,'statId':'base:attack_power'}}],
          'canCrit':true,'tags':['base:physical']}")));
        skills.Register(Skill(StarPoint, "base:skill", false, 7, StarPointAction, JObject.Parse(@"{
          'windup':2.0,'recovery':1.2,'maxRange':3.0,'markRadius':0.35,'tags':['base:physical'],
          'motion':{'kind':'CurveXZ','distance':3.0,'lateralOffset':0,'duration':0.25,'permissionId':'base:motion_voluntary','actorCollision':'IgnoreEnemyBodies',
            'maxSegmentLength':0.12,'maxChordError':0.015,'maxSubsteps':64,'hitRadius':0.35,'activeStart':0,'activeEnd':1,'repeatInterval':0,'canCrit':true,
            'value':{'amount':220,'statId':'base:attack_power'},'tags':['base:physical']}}")));
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
    private static void Positive(JObject p, string key, double maximum)
    { double n = PNBuffContentBatch.Number(p, key); if (double.IsNaN(n) || double.IsInfinity(n) || n <= 0 || n > maximum) throw new FormatException("Invalid Starkeeper number: " + key); }
    public static void ValidateSweep(PNSkillActionData data, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(data.Perform);
        PNBuffContentBatch.Fields(p, "windup", "gap", "reaimAt", "recovery", "maxReaimDegrees", "radius", "angle", "thickness", "step", "hits", "canCrit", "tags");
        Positive(p, "windup", 10); Positive(p, "gap", 10); Positive(p, "reaimAt", 10); Positive(p, "recovery", 10); Positive(p, "maxReaimDegrees", 180);
        Positive(p, "radius", 5); Positive(p, "angle", 360); Positive(p, "thickness", 5);
        var step = PNBuffContentBatch.Object(p["step"]);
        PNBuffContentBatch.Fields(step, "distance", "duration", "stopDistance", "permissionId", "actorCollision");
        Positive(step, "distance", 5); Positive(step, "duration", 2); Positive(step, "stopDistance", 5);
        if (step["permissionId"]?.Type != JTokenType.String || step["actorCollision"]?.Type != JTokenType.String) throw new FormatException("Step permission and actor collision required.");
#if UNITY_5_3_OR_NEWER
        hub.ControlPermissions().Get(EveId.Parse(step["permissionId"].Value<string>()));
        if (!Enum.TryParse(step["actorCollision"].Value<string>(), false, out PNMotionActorCollision collision) || !Enum.IsDefined(typeof(PNMotionActorCollision), collision)) throw new FormatException("Unknown step actor collision.");
#endif
        // the second sweep re-aims inside the gap, and its step (the last `duration` seconds before the hit) must start after the re-aim
        if (PNBuffContentBatch.Number(p, "reaimAt") + PNBuffContentBatch.Number(step, "duration") > PNBuffContentBatch.Number(p, "gap")) throw new FormatException("reaimAt plus the step duration must fit inside the gap.");
        if (PNBuffContentBatch.Number(step, "duration") > PNBuffContentBatch.Number(p, "windup")) throw new FormatException("The step must fit inside the windup.");
        if (p["canCrit"]?.Type != JTokenType.Boolean) throw new FormatException("canCrit required.");
        if (!(p["hits"] is JArray hits) || hits.Count != 2) throw new FormatException("Exactly two sweeps required.");
        foreach (var token in hits)
        {
            var hit = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(hit, "value", "iframeDuration");
#if UNITY_5_3_OR_NEWER
            PNDamage.ParseIFrameDuration(hit);                       // optional, finite; the first sweep keeps the target vulnerable long enough for the second one to land in full
#endif
            var values = PNSkillValues.Parse(hit["value"]); values.ValidateStats(hub); values.ValidateNonnegativeAmounts();
        }
        new PNStatSet(hub).ResolveDamageType(data.PerformTags);
    }
    public static void ValidateStarPoint(PNSkillActionData data, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(data.Perform);
        PNBuffContentBatch.Fields(p, "windup", "recovery", "maxRange", "markRadius", "motion", "tags");
        Positive(p, "windup", 10); Positive(p, "recovery", 10); Positive(p, "maxRange", 20); Positive(p, "markRadius", 5);
        var motion = PNBuffContentBatch.Object(p["motion"]);
#if UNITY_5_3_OR_NEWER
        var settings = PNAgentTrajectorySettings.Parse(motion, hub, true);
        if (settings.Kind != PNTrajectoryKind.CurveXZ || settings.LateralOffset != 0) throw new FormatException("The star point lunge is a straight CurveXZ.");
        if (Math.Abs(settings.Distance - PNBuffContentBatch.Number(p, "maxRange")) > 1e-4) throw new FormatException("maxRange must equal the lunge distance.");
#else
        if (motion["kind"]?.Value<string>() != "CurveXZ") throw new FormatException("The star point lunge is a straight CurveXZ.");
#endif
        new PNStatSet(hub).ResolveDamageType(data.PerformTags);
    }
}
