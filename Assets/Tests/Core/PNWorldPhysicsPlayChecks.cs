#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

public sealed class PNWorldFixtureRuntime : EveGameRuntime
{
    protected override void Awake() { }
    protected override IEnumerable<EveIContentModule> CreateContentModules()
    {foreach(var module in base.CreateContentModules())yield return module;yield return new PNWorldFixtureModule();}
}
public sealed class PNWorldMoveProbe : PNMoveNativeProbe
{
    public bool WorldInputEnabled=>pc!=null&&pc.Controller.Move.enabled;
    public Vector2 WorldRawInput=>pc==null?Vector2.zero:pc.Controller.Move.ReadValue<Vector2>();
}
public sealed class PNWorldPhysicsPlayChecks : MonoBehaviour
{
    private static string Output => (Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith("--pn-world-check-output="))?.Substring("--pn-world-check-output=".Length)??"F:/unity/Project_N/.utmp/WorldS2S4/S3/").TrimEnd('/')+"/";
    private readonly List<PNMoveNativeProbe> actors=new List<PNMoveNativeProbe>();
    private readonly List<GameObject> passiveActors=new List<GameObject>();
    private readonly List<string> evidence=new List<string>();
    private PNWorldFixtureRuntime runtime;
    private Keyboard keyboard;
    private InputSettings.BackgroundBehavior background;
    private InputSettings.EditorInputBehaviorInPlayMode editorInput;
    private void Check(bool value,string label){if(!value)throw new Exception(label);evidence.Add("PASS "+label);Debug.Log("WorldS3 PASS "+label);}
    private IEnumerator Start()
    {
        var iterator=Run();
        while(true)
        {
            bool next;object value=null;
            try{next=iterator.MoveNext();if(next)value=iterator.Current;}
            catch(Exception error){Directory.CreateDirectory(Output);File.WriteAllText(Output+"Failure.txt",error+"\n"+string.Join("\n",evidence));Debug.LogException(error);UnityEditor.EditorApplication.isPlaying=false;yield break;}
            if(!next)break;yield return value;
        }
        Directory.CreateDirectory(Output);File.WriteAllText(Output+"NativePlay.txt","PASS "+evidence.Count+" world physics/clock cases\n"+string.Join("\n",evidence));
        UnityEditor.EditorApplication.isPlaying=false;
    }
    private PNWorldMoveProbe Actor(EveWorldContext world,long owner,Vector3 position)
    {
        var go=new GameObject("World S3 actor");go.SetActive(false);SceneManager.MoveGameObjectToScene(go,world.PhysicsOwner);go.transform.position=position;
        var id=go.AddComponent<EveEntityIdentity>();id.Assign(world.Session.Entities.AllocateHostId(),EveId.Parse("project_n:eve"),owner);id.BindWorld(world,"chunk-1");
        var body=go.AddComponent<Rigidbody>();body.constraints=RigidbodyConstraints.FreezeRotation;
        go.AddComponent<CapsuleCollider>();go.AddComponent<EveCharacterStat>();go.AddComponent<EveTimeScale>();go.AddComponent<PNAbilityManager>();
        var skill=go.AddComponent<PNSharedDashEditorProbe>();skill.InitializeInput();
        var move=go.AddComponent<PNWorldMoveProbe>();move.EnsureInputInitialized();
        go.AddComponent<PNFactoryEntity>();actors.Add(move);go.AddComponent<EveEntityLoader>().Load(world.Session.Registry);go.SetActive(true);return move;
    }
    private IEnumerator Run()
    {
        background=InputSystem.settings.backgroundBehavior;editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        PNGameBootstrap.End();PNGameBootstrap.Runtime.UnloadContent();
        runtime=new GameObject("Disposable S3 runtime").AddComponent<PNWorldFixtureRuntime>();runtime.BeginSession(EveSessionRole.Host);
        var session=EveRegistrySession.Context;var service=runtime.GetComponent<PNGameSession>();
        var a=service.Worlds.Create(EveId.Parse("fixture:world"));var b=service.Worlds.Create(EveId.Parse("fixture:world"));
        double deadline=Time.realtimeSinceStartupAsDouble+15;
        while(a.State!=EveWorldState.Ready||b.State!=EveWorldState.Ready)
        {if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("world prepare");yield return null;}
        Check(a.PhysicsOwner.GetPhysicsScene()!=b.PhysicsOwner.GetPhysicsScene()&&a.PhysicsOwner.GetPhysicsScene()!=Physics.defaultPhysicsScene,"two actual local physics scenes");
        var player=Actor(a,0,new Vector3(0,1.02f,0));var target=Actor(a,1,new Vector3(3,1.02f,0));var other=Actor(b,1,new Vector3(0,1.02f,0));
        yield return new WaitForSecondsRealtime(.7f);
        Check(player.IsGrounded&&other.IsGrounded,"ground probes resolve each local world floor");
        Check(Math.Abs(player.Body.position.x)<.05f&&Math.Abs(other.Body.position.x)<.05f,"bodies at identical coordinates in different worlds do not collide");
        using(var cast=new PNHitboxCast(EveHitboxShape.Sphere(6)))
        {
            var targets=new List<PNStat>();cast.Query(new EveHitboxPose(player.Body.position,Quaternion.identity),0,targets,player.GetComponent<PNStat>());
            Check(targets.Count==1&&targets[0]==target.GetComponent<PNStat>(),"hit query sees other chunk in same world and excludes other world");
            Check(!PNWorldGameplay.CanTarget(player,other)&&PNWorldGameplay.CanTarget(player,target),"direct live targeting defaults to same world");
            var id=player.GetComponent<EveEntityIdentity>();
            var beforeClock=player.GetComponent<EveTimeScale>();beforeClock.AddTimedModifier(EveId.Parse("fixture:relocate"),EveTimeModifierKind.Scale,.1f,10);
            var retained=player.GetComponent<PNBuffManager>().Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_stat_template"),PNBuffSource.Capture(player.GetComponent<PNStat>())));
            using(id.HoldGameplay())
            {
                Check(player.GetComponent<EveEntityLoader>().IsReady&&!PNLocalPlayerInput.Allows(player.GetComponent<EveEntityLoader>()),"actor hold retains publication and blocks local input");
                id.RelocateWorldFixture(b,"chunk-1");player.Body.position=new Vector3(5,1.02f,0);player.transform.position=player.Body.position;
            }
            Check(beforeClock.Modifiers.Count==1&&retained.Handle.IsAlive,"world relocation callback scope retains existing time modifier and Buff");
            yield return new WaitForSecondsRealtime(.2f);
            cast.Query(new EveHitboxPose(player.Body.position,Quaternion.identity),1,targets,player.GetComponent<PNStat>());
            Check(targets.Count==1&&targets[0]==other.GetComponent<PNStat>(),"cached hit query rebinds physics owner after held fixture relocation");
            double groundUntil=Time.realtimeSinceStartupAsDouble+3;
            while(!player.IsGrounded&&Time.realtimeSinceStartupAsDouble<groundUntil)yield return new WaitForFixedUpdate();
            Check(player.IsGrounded&&player.TryJump(),"ground and jump remain valid after fixture relocation pos="+player.Body.position+" velocity="+player.Body.linearVelocity+" ready="+player.IsReady+" scale="+player.MovementTimeScale+" jump="+player.CanJump+" constraints="+player.Body.constraints);
        }
        yield return new WaitForSecondsRealtime(.8f);
        keyboard=InputSystem.AddDevice<Keyboard>();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D));InputSystem.Update();
        Check(keyboard.dKey.isPressed&&player.WorldInputEnabled&&player.WorldRawInput.x>0,"real injected keyboard reaches enabled action map enabled="+player.WorldInputEnabled+" raw="+player.WorldRawInput+" pressed="+keyboard.dKey.isPressed);
        player.InputTick();yield return null;
        float x=player.Body.position.x;for(int i=0;i<15;i++)yield return new WaitForFixedUpdate();
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();yield return null;
        Check(player.Body.position.x>x+.1f,"actual keyboard input moves the body in the relocated world before="+x+" after="+player.Body.position.x+" input="+player.CanInput+" move="+player.CanMove+" direction="+player.MoveDirection+" ready="+player.IsReady);
        var skill=player.GetComponent<PNSharedDashEditorProbe>();Check(skill.TryDash(),"registered shared Dash starts in relocated world");
        var clock=player.GetComponent<EveTimeScale>();clock.AddTimedModifier(EveId.Parse("fixture:timer"),EveTimeModifierKind.Scale,.1f,2);
        var buffs=player.GetComponent<PNBuffManager>();var buff=buffs.Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_stat_template"),PNBuffSource.Capture(player.GetComponent<PNStat>())));
        Check(buff.Handle.IsAlive,"registered Buff applied before pause");
        var areas=runtime.GetComponent<PNAreaManager>();
        var area=areas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"),JObject.Parse("{'radius':2,'reduction':0.2,'minimumReduction':0.1,'duration':10,'dependent':true,'layerMask':-1,'layerMode':'Include'}"),PNBuffSource.Capture(player.GetComponent<PNStat>()),b.PhysicsOwner,player.Body.position,player.GetComponent<PNStat>()));
        Check(area.Status==PNAreaStatus.Created,"world-owned Area created");
        double cooldown=skill.DashRecovery.CooldownRemaining;double time=b.GameplayTime;var position=player.Body.position;ulong steps=b.PhysicsSteps;int modifiers=clock.Modifiers.Count;
        b.SetPaused(true);yield return new WaitForSecondsRealtime(.35f);
        Check(b.GameplayTime==time&&b.PhysicsSteps==steps&&Vector3.Distance(player.Body.position,position)<.001f,"world pause stops gameplay clock and physics");
        Check(a.PhysicsSteps>steps/2&&a.GameplayTime>time,"other world continues during pause");
        Check(skill.DashRecovery.CooldownRemaining==cooldown&&clock.Modifiers.Count==modifiers&&buff.Handle.IsAlive&&area.Handle.IsAlive&&skill.IsDashing,"pause preserves cooldown, time modifier, Buff, Area and running motion");
        Check(player.gameObject.activeInHierarchy&&player.GetComponent<EveEntityLoader>().IsReady,"pause uses no disable/unpublication");
        b.SetPaused(false);yield return new WaitForSecondsRealtime(.35f);
        Check(!skill.IsDashing&&skill.DashRecovery.CooldownRemaining<cooldown&&b.PhysicsSteps>steps,"resume continues motion and timers without compensating paused time");
        var passive=new GameObject("Held body without Move");passive.SetActive(false);SceneManager.MoveGameObjectToScene(passive,b.PhysicsOwner);passive.transform.position=new Vector3(20,4,0);passiveActors.Add(passive);
        var passiveId=passive.AddComponent<EveEntityIdentity>();passiveId.Assign(session.Entities.AllocateHostId(),EveId.Parse("project_n:dummy"),0);passiveId.BindWorld(b,"chunk-1");
        var passiveBody=passive.AddComponent<Rigidbody>();passive.AddComponent<CapsuleCollider>();passive.AddComponent<PNEnemyStat>();passive.AddComponent<EveEntityLoader>().Load(session.Registry);passive.SetActive(true);passiveBody.linearVelocity=Vector3.down;
        var passivePosition=passiveBody.position;
        using(passiveId.HoldGameplay())
        {for(int i=0;i<8;i++)yield return new WaitForFixedUpdate();Check(Vector3.Distance(passiveBody.position,passivePosition)<.001f&&passiveBody.linearVelocity==Vector3.down,"actor hold freezes a raw body without Move and preserves its velocity");}
        yield return new WaitForFixedUpdate();Check(passiveBody.position.y<passivePosition.y,"raw body resumes physics after its actor hold");
        bool duplicate=false;try{b.SimulatePhysics(1,.02f);}catch(InvalidOperationException){duplicate=true;}
        Check(duplicate,"duplicate/old physics boundary is rejected");
        Check(areas.Get(area.Handle)!=null,"area survives world pause/resume");
        b.SetPaused(true);areas.ReleaseWorld(b);Check(!area.Handle.IsAlive&&clock.Modifiers.Count<modifiers,"paused world area dependencies release their exact Buff and time modifier");b.SetPaused(false);
        foreach(var actor in actors){actor.GetComponent<EveEntityLoader>().Unload();actor.GetComponent<EveEntityIdentity>().ResetIdentity();actor.DisposeEntityResources();Destroy(actor.gameObject);}actors.Clear();
        foreach(var actor in passiveActors){actor.GetComponent<EveEntityLoader>().Unload();actor.GetComponent<EveEntityIdentity>().ResetIdentity();Destroy(actor);}passiveActors.Clear();
        try{runtime.EndSession();}catch(InvalidOperationException){}catch(AggregateException){}
        deadline=Time.realtimeSinceStartupAsDouble+10;
        while(EveRegistrySession.State==EveSessionState.Stopping)
        {if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("world cleanup");try{EveRegistrySession.RetryCleanup();}catch{}yield return null;}
        Check(EveRegistrySession.State==EveSessionState.Stopped&&session.Worlds.Snapshot().Count==0,"play teardown drains world leases and scenes");
        if(keyboard!=null){InputSystem.RemoveDevice(keyboard);keyboard=null;}
        InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
    }
}
#endif
