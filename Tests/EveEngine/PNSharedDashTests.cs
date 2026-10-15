using System;
using System.IO;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static EveSessionSmokeTests;

public class PNSharedDashProbe : PNPlayerSkill
{
    public void InitializeInput(){Awake();OnEnable();}
    public void Tick(float delta)=>AdvancePlayerDash(delta);
    public void Disable(){enabled=false;OnDisable();}
    public void Enable(){enabled=true;OnEnable();}
    public void DestroyInput()=>OnDestroy();
    public Vector3 Aim=Vector3.right;
    protected override Vector3 PlayerDashDirection()=>Aim;
}
public class PNSharedMotionProbe : PNMotionAction
{
    public static int Starts;
    public override void Start(PNSkillActionContext context){Starts++;base.Start(context);}
}
public class PNSharedDashOverride : EveIContentModule,EveIContentFinalizeModule
{
    public void Register(EveRegistryHub hub)
    {
        var motion=EveId.Parse("test:shared_motion");
        hub.Actions().Register(new PNSkillActionDefinition(motion,typeof(PNSharedMotionProbe)));
        hub.Actions().RequestOverride(EveId.Parse("base:motion_action"),motion,"test","1",new[]{"project_n"});
        var data=(JObject)hub.Skills.Get(PNGameplayContentModule.PlayerDash).Data;
        data["action"]["perform"]["distance"]=4;data["action"]["perform"]["duration"]=.25;
        data["skill"]["abilities"][0]["cooldown"]=2;
        hub.Skills.Register(new EveSkillDefinition(EveId.Parse("test:shared_dash"),data));
    }
    public void FinalizeContent(EveRegistryHub hub)=>hub.Skills.RequestOverride(EveId.Parse("base:player_dash"),EveId.Parse("test:shared_dash"),"test","1",new[]{"project_n"});
    public void ValidateContent(EveRegistryHub hub) { }
}
public static class PNSharedDashTests
{
    public static void Run()
    {
        Reject(()=>PNMotionSettings.Parse(JObject.Parse("{'distance':1,'duration':0,'ignoreEntities':true}")),"registered motion rejects zero duration");
        Reject(()=>PNMotionSettings.Parse(JObject.Parse("{'distance':-1,'duration':1,'ignoreEntities':true}")),"registered motion rejects negative distance");
        Reject(()=>PNMotionSettings.Parse(JObject.Parse("{'distance':1,'duration':1,'ignoreEntities':1}")),"registered motion rejects invalid collision policy");
        foreach(bool overridden in new[]{false,true})
        {
            // Only Dummy content is loaded: shared Dash does not depend on Eve content or an Eve entity.
            var doc=new EveContentDocument("dummy",File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"BaseContent/characters/DummyCharacter.json")),"project_n");
            EveIContentModule[] modules=overridden?new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule(),new PNSharedDashOverride()}:new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()};
            var hub=new EveContentBootstrap().Build(modules,new[]{doc});
            EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,new[]{doc},"test:shared_dash"));
            (PNSharedDashProbe skill,EveTestLoader loader,PNAbilityManager manager,PNStat stat) Player(long id)
            {
                var identity=new EveEntityIdentity();identity.Assign(new EveEntityId(id),EveId.Parse("project_n:dummy"),0);
                var skill=new PNSharedDashProbe();var loader=new EveTestLoader();var manager=new PNAbilityManager();var stat=new DummyStat();
                MonoBehaviour[] parts={identity,skill,loader,manager,stat};foreach(var part in parts){part.Components=parts;part.gameObject.SetActive(true);}
                skill.InitializeInput();loader.Load(hub);return(skill,loader,manager,stat);
            }
            var first=Player(988001);var second=Player(988002);
            try
            {
                float distance=overridden?4:3,period=overridden?2:5;
                Check(first.skill.Skills.Count==0 && first.skill.DashRecovery.Current==3,"global default Dash loads for character with empty skill list");
                Check(first.skill.DashRecovery.Handle!=second.skill.DashRecovery.Handle,"shared Dash owns independent per-entity recovery handles");
                first.skill.Aim=new Vector3(-1,0,0);int starts=PNSharedMotionProbe.Starts;
                Check(first.skill.TryDash() && first.skill.IsDashing,"shared Dash activates");
                Check(!first.skill.TryDash(),"shared Dash cannot reenter its own action");
                first.skill.Tick(.5f);
                Check(Math.Abs(first.skill.transform.position.x+distance)<.001 && !first.skill.IsDashing,"registered Dash parameters move actual invocation and finish");
                Check(second.skill.DashRecovery.Current==3 && first.skill.DashRecovery.Current==2,"one player spending Dash cannot spend another player's charges");
                if(overridden)Check(PNSharedMotionProbe.Starts==starts+1,"shared Dash action honors registry implementation override");
                first.manager.Advance(period*.4);
                Check(first.skill.TryDash(),"second shared Dash activates during recovery");first.skill.Tick(.5f);
                Check(Math.Abs(first.skill.DashRecovery.CooldownRemaining-period*.6)<.001,"shared Dash preserves existing recovery timer");
                Check(first.skill.TryDash(),"third shared Dash consumes last charge");first.skill.Tick(.5f);
                Check(!first.skill.TryDash(),"empty shared Dash blocks activation");first.manager.Advance(period*.6);
                Check(first.skill.DashRecovery.Current==1,"shared Dash recovers one charge sequentially");
                Check(first.skill.TryDash(),"shared Dash can activate before disable");first.skill.Disable();
                Check(!first.skill.IsDashing,"disable cancels shared Dash");first.skill.Enable();
                first.skill.DashRecovery.ResetToFull();
                first.stat.SetSource(EveId.Parse("test:cdr"),new[]{new EveStatModifier(PNContentIds.CooldownReduction,50,0)});
                Check(first.skill.TryDash(),"shared Dash accepts CDR configuration");
                Check(Math.Abs(first.skill.DashRecovery.CooldownRemaining-period*.5)<.001,"shared Dash applies entity CDR to new recovery");
                first.loader.Unload();first.loader.Load(hub);
                Check(first.skill.DashRecovery.Current==3 && !first.skill.IsDashing,"reload resets shared Dash state without leaking invocations");
                var action=first.skill.CreateMotionAction(EveId.Parse("base:player_dash"),Vector3.right,out var context,distance+1);
                Reject(()=>action.Start(context),"evaluated motion cannot exceed registered maximum");action.Dispose();
            }
            finally{first.skill.DestroyInput();second.skill.DestroyInput();first.loader.Unload();second.loader.Unload();EveRegistrySession.End();}
        }
    }
}
