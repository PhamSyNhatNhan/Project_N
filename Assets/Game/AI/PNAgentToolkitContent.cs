#if UNITY_5_3_OR_NEWER
using EveEngine;
using Newtonsoft.Json.Linq;

public static class PNAgentToolkitContent
{
    public static readonly EveId Support = EveId.Parse("project_n:agent_support");
    public static readonly EveId SupportReady = EveId.Parse("project_n:agent_support_ready");
    public static readonly EveId Curve = EveId.Parse("project_n:agent_curve");
    public static readonly EveId Hop = EveId.Parse("project_n:agent_hop");
    public static readonly EveId Orbit = EveId.Parse("project_n:agent_orbit");
    public static readonly EveId ContactMotion = EveId.Parse("project_n:agent_contact_motion");
    public static void Register(EveRegistryHub hub)
    {
        hub.GetOrCreate<PNSkillActionDefinition>().Register(new PNSkillActionDefinition(EveId.Parse("project_n:trajectory_action"), typeof(PNAgentTrajectoryAction),
            (data, registry) => PNAgentTrajectoryAction.Validate(data, registry, false), PNSkillContextRequirements.Direction,
            (data, references) => references.Require<PNSkillOperationDefinition>(new JValue("base:lock_movement"), "movementLock")));
        hub.GetOrCreate<PNSkillActionDefinition>().Register(new PNSkillActionDefinition(EveId.Parse("project_n:contact_motion_action"), typeof(PNAgentContactMotionAction),
            (data, registry) => PNAgentTrajectoryAction.Validate(data, registry, true), PNSkillContextRequirements.Direction,
            (data, references) => references.Require<PNSkillOperationDefinition>(new JValue("base:lock_movement"), "movementLock")));
        void MotionSkill(EveId id, string perform, string action = "base:trajectory_action")
        {
            hub.GetOrCreate<EveSkillDefinition>().Register(new EveSkillDefinition(id, new JObject {
                ["categoryIds"] = new JArray("base:skill"), ["affectedByAttackSpeed"] = false,
                ["action"] = new JObject { ["id"] = action, ["start"] = new JObject(), ["perform"] = JObject.Parse(perform), ["exit"] = new JObject(), ["cancel"] = new JObject() }
            }));
        }
        const string shared = @"'duration':0.6,'permissionId':'base:motion_voluntary','actorCollision':'IgnoreEnemyBodies','maxSegmentLength':0.12,'maxChordError':0.015,'maxSubsteps':64";
        MotionSkill(Curve, "{'kind':'CurveXZ','distance':1.5,'lateralOffset':0.5," + shared + "}");
        MotionSkill(Hop, "{'kind':'Hop','distance':1.5,'height':0.6," + shared + "}");
        MotionSkill(Orbit, "{'kind':'OrbitSegment','radius':1.5,'sweepDegrees':90," + shared + "}");
        MotionSkill(ContactMotion, "{'kind':'CurveXZ','distance':2,'lateralOffset':0,'hitRadius':0.4,'activeStart':0,'activeEnd':1,'repeatInterval':0,'canCrit':false,'value':{'amount':1000},'tags':['base:physical','base:skill']," + shared + "}", "base:contact_motion_action");
        MotionSkill(EveId.Parse("project_n:agent_dodge"), "{'distance':1.5,'duration':0.2,'ignoreEntities':false,'permissionId':'base:motion_voluntary'}", "base:motion_action");
        MotionSkill(EveId.Parse("project_n:agent_backstep"), "{'distance':1,'duration':0.2,'ignoreEntities':false,'permissionId':'base:motion_voluntary'}", "base:motion_action");
        hub.GetOrCreate<PNSkillActionDefinition>().Register(new PNSkillActionDefinition(EveId.Parse("project_n:agent_heal_action"), typeof(PNAgentHealAction),
            PNAgentHealAction.Validate, PNSkillContextRequirements.Targets,
            (data, references) => references.Require<PNSkillOperationDefinition>(new JValue("base:heal"), "healOperation")));
        hub.GetOrCreate<EveSkillDefinition>().Register(new EveSkillDefinition(Support, JObject.Parse(@"{
          'categoryIds':['base:skill'],'affectedByAttackSpeed':false,
          'skill':{'abilities':[{'id':'project_n:agent_support_ready','kind':'Cooldown','showInUI':false,'iconPath':'','displayOrder':0,
            'maximum':1,'initialAmount':1,'cooldown':2,'recovery':'PerStack','autoRecover':true,'clock':'Entity'}]},
          'action':{'id':'base:agent_heal_action','start':{},'perform':{'value':{'amount':1000}},'exit':{},'cancel':{}}
        }")));
    }
}
#endif
