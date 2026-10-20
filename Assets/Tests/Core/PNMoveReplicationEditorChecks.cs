#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using EveEngine.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Linq;

public class PNMoveNativeProbe : PNPlayerMove
{
    public void Tick() => FixedUpdate();
    public void InputTick() => Update();
    public void DisableMove() { enabled=false;OnDisable(); }
    public void EnableMove() { enabled=true;OnEnable(); }
}
public sealed class PNMoveFaultMotion : PNMotionAction
{
    public static Action CancelHook;
    public override void Cancel(PNSkillActionContext context)
    { var hook=CancelHook;CancelHook=null;base.Cancel(context);hook?.Invoke(); }
}
public sealed class PNMoveFaultModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    {
        var id=EveId.Parse("test:move_fault_motion");
        hub.Actions().Register(new PNSkillActionDefinition(id,typeof(PNMoveFaultMotion),(d,h)=>PNMotionSettings.Parse(d.Perform),PNSkillContextRequirements.Direction));
        hub.Actions().RequestOverride(EveId.Parse("base:motion_action"),id,"test","1.0.0",new[]{"project_n"});
    }
}
// Test-only owner/observer binding for one client session over the typed Move seam (no byte transport).
public sealed class PNMoveNativeClient : IDisposable
{
    private readonly EveSessionContext session;
    private readonly Guid host;
    private readonly Dictionary<MoveIdentity,PNMove> actors=new Dictionary<MoveIdentity,PNMove>();
    private readonly Dictionary<MoveIdentity,Action<MovePoseReport>> subscriptions=new Dictionary<MoveIdentity,Action<MovePoseReport>>();
    private readonly Dictionary<MoveIdentity,object> reports=new Dictionary<MoveIdentity,object>();
    private readonly Dictionary<MoveIdentity,MoveBaselineRequest> baselineRequests=new Dictionary<MoveIdentity,MoveBaselineRequest>();
    private uint requestCounter;
    public PNMoveNativeClient(EveSessionContext session,Guid admittedHost)
    {
        if(session==null || !session.IsActive || session.Role!=EveSessionRole.Client || !session.LocalOwnerId.HasValue || session.Admission.AdmittedHostSession!=admittedHost)
            throw new InvalidOperationException("Trusted owner and host admission required.");
        this.session=session;host=admittedHost;
    }
    public void Bind(PNMove move)
    {
        var id=move.NetworkIdentity(host);if(actors.ContainsKey(id))throw new InvalidOperationException("Already bound.");
        if(move.GetComponent<EveEntityIdentity>().OwnerId==session.LocalOwnerId.Value)
        {
            move.BindOwnerMovement(host);Action<MovePoseReport> listener=report=>reports[id]=report;
            move.MovementReportPublished+=listener;subscriptions.Add(id,listener);
            requestCounter=MoveOrder.Next(requestCounter);move.OwnerMovement.ExpectBaseline(requestCounter);
            var request=new MoveBaselineRequest(id,requestCounter);baselineRequests[id]=request;reports[id]=request;
        }
        else move.BindObserverMovement(host);
        actors.Add(id,move);
    }
    public bool Receive(object message)
    {
        var id=message is MoveMirror m?m.Identity:((MoveMessage)message).Identity;
        if(id.Session!=host || !actors.TryGetValue(id,out var move) || !move.NetworkUsable)return false;
        if(move.NetworkRole==PNMoveNetworkRole.Owner)return move.ReceiveOwnerMessage(message);
        return message is MovePoseState state && move.ObserverMovement!=null && move.ObserverMovement.Receive(state,true);
    }
    public void AdvanceObservers(double estimatedHostTime)
    {
        foreach(var pair in actors.ToArray())
        {
            if(!pair.Value.NetworkUsable) { Unbind(pair.Key);continue; }
            if(pair.Value.NetworkRole==PNMoveNetworkRole.Observer)pair.Value.ApplyObserverPose(Math.Max(0,estimatedHostTime-.1));
        }
    }
    public bool SendReports(Func<object[],bool> accept)
    {
        foreach(var pair in baselineRequests.ToArray())
            if(actors.TryGetValue(pair.Key,out var move) && move.OwnerMovement?.NeedsBaseline==true)reports[pair.Key]=pair.Value;
            else baselineRequests.Remove(pair.Key);
        if(reports.Count==0)return false;
        var batch=reports.Values.ToArray();if(!accept(batch))return false;reports.Clear();return true;
    }
    public void Unbind(MoveIdentity id)
    {
        if(!actors.TryGetValue(id,out var move))return;
        if(subscriptions.TryGetValue(id,out var subscription)) { move.MovementReportPublished-=subscription;subscriptions.Remove(id); }
        reports.Remove(id);baselineRequests.Remove(id);actors.Remove(id);move.DetachNetwork();
    }
    public void Dispose() { foreach(var id in actors.Keys.ToArray())Unbind(id);reports.Clear();baselineRequests.Clear(); }
}
public static class PNMoveReplicationEditorChecks
{
    // A clone-only batch fixture: manually steps DEFAULT PhysX. Never call against an authored scene.
    public static string Run()
    {
        if(Application.isPlaying || EveRegistrySession.State!=EveSessionState.Stopped || SceneManager.GetActiveScene().rootCount!=0)
            throw new InvalidOperationException("Stopped empty disposable clone scene required.");
        var original=SceneManager.GetActiveScene();var roots=new List<GameObject>();int checks=0;var lines=new List<string>();
        var previousSimulation=Physics.simulationMode;
        void Check(bool ok,string label) { if(!ok)throw new Exception(label);checks++;lines.Add("PASS "+label); }
        var docs=new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),Path.Combine(Application.persistentDataPath,"Mods"),Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule(),new PNSkillEffectsNativeModule(),new PNMoveFaultModule()},docs);
        var compatibility=EveContentCompatibility.Create(hub,docs,"test:move_native");
        var projectileManagers=new List<PNProjectileManager>();
        void Begin(EveSessionRole role) { EveRegistrySession.Begin();EveRegistrySession.Start(hub,role,compatibility); var projectileRoot=new GameObject("Move check projectile manager");roots.Add(projectileRoot);var projectileManager=projectileRoot.AddComponent<PNProjectileManager>();projectileManager.Initialize(EveRegistrySession.Context,new PNProjectileProfile{MaxActive=20});projectileManager.ManualAdvance=true;projectileManagers.Add(projectileManager); }
        void End() { foreach(var created in projectileManagers)if(created!=null)created.Shutdown();projectileManagers.Clear();if(EveRegistrySession.State!=EveSessionState.Stopped)EveRegistrySession.End();foreach(var root in roots)if(root!=null)UnityEngine.Object.DestroyImmediate(root);roots.Clear(); }
        PNMoveNativeProbe Actor(long id,long owner,string skillKind=null)
        {
            var root=new GameObject("Disposable Move actor");roots.Add(root);root.SetActive(false);root.transform.position=new Vector3((id%100)*4,1.02f,0);
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id),EveId.Parse("project_n:eve"),owner);
            var body=root.AddComponent<Rigidbody>();body.constraints=RigidbodyConstraints.FreezeRotation;body.useGravity=true;
            root.AddComponent<CapsuleCollider>();root.AddComponent<EveCharacterStat>();root.AddComponent<EveTimeScale>();root.AddComponent<PNBuffManager>().RenderPresentation=false;
            if(skillKind!=null)
            {
                root.AddComponent<PNAbilityManager>();
                if(skillKind=="shared")root.AddComponent<PNSharedDashEditorProbe>().InitializeInput();
                else if(skillKind=="primary")root.AddComponent<PNEveAttackSpeedProbe>().EnsureInput();
                else root.AddComponent<PNSkillEffectsNativeProbe>();
            }
            var move=root.AddComponent<PNMoveNativeProbe>();var loader=root.AddComponent<EveEntityLoader>();loader.Load(hub);root.SetActive(true);return move;
        }
        GameObject Floor()
        { var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);roots.Add(floor);floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(100,1,100);Physics.SyncTransforms();return floor; }
        Guid hostId=Guid.Empty;var ownerWire=new List<object>();var observerWire=new List<object>();
        try
        {
            Physics.simulationMode=SimulationMode.Script;
            Begin(EveSessionRole.Host);var session=EveRegistrySession.Context;hostId=session.SessionId;session.Admission.AdmitPeer(7,compatibility);session.Admission.AdmitPeer(8,compatibility);
            Floor();var local=Actor(5001,0);var remote=Actor(5002,7);var body=local.Body;
            Physics.SyncTransforms();for(int i=0;i<40;i++){local.Tick();Physics.Simulate(.02f);}local.Tick();Check(local.IsGrounded,"native ground sensing on real collider pos="+local.Body.position+" vel="+local.Body.linearVelocity+" bounds="+local.GetComponent<CapsuleCollider>().bounds+" ready="+local.IsReady+" scale="+local.MovementTimeScale+" hits="+Physics.SphereCastAll(local.Body.position, .2f, Vector3.down, 4).Length);
            local.SetMoveDirection(Vector2.right);var start=body.position;for(int i=0;i<5;i++){local.Tick();Physics.Simulate(.02f);}
            Check(body.position.x>start.x+.1f,"native locomotion integrates real Rigidbody");
            Check(local.TryJump(),"native jump begins on ground");local.Tick();Physics.Simulate(.02f);Check(body.position.y>start.y && body.linearVelocity.y>0,"native physical jump rises");
            Check(PNLocalPlayerInput.Allows(local.GetComponent<EveEntityLoader>()) && !PNLocalPlayerInput.Allows(remote.GetComponent<EveEntityLoader>()),"host input only host-owned player");
            using(var world=new PNMoveHostWorld(session))
            {
                world.Track(local,false);world.Track(remote,true);Check(remote.Body.isKinematic && !remote.IsReady,"host remote body has no locomotion writer");
                var remoteId=remote.NetworkIdentity(session.SessionId);
                Check(world.Receive(new MoveBaselineRequest(remoteId,1),7,.9)==MoveReportResult.Accepted,"actual adapter answers owner bootstrap request");
                local.TeleportToPoint(new Vector2(local.Body.position.x+1,local.Body.position.z),0);
                void Collect(long peer,double time,List<object> sink)
                {
                    foreach(var move in new[]{local,remote})
                    {
                        var model=world.TypedModel(move.NetworkIdentity(session.SessionId));
                        if(model==null || !model.Ready)continue;
                        if(model.Owner>0 && model.Owner==peer){sink.Add(model.Mirror);sink.Add(model.LastCorrection);}
                        else sink.Add(model.Sample(time));
                    }
                }
                {
                    world.Advance(1);Collect(7,1,ownerWire);Collect(8,1,observerWire);
                    Check(ownerWire.OfType<MovePoseState>().Single(x=>x.Identity.Entity==5001).Epoch==2,"native command teleport publishes one observer epoch cut");
                    var baseline=ownerWire.OfType<MoveCorrection>().Single(x=>x.Identity.Entity==5002);
                    var mirror=ownerWire.OfType<MoveMirror>().Single(x=>x.Identity.Entity==5002);
                    var report=new MovePoseReport(baseline.Identity,1,baseline.Epoch,mirror.Revision,baseline.Id,new MovementState(new MoveVector(3,2,0),new MoveVector(0,2,0)));
                    Check(world.Receive(report,8,1.2)==MoveReportResult.WrongOwner,"actual Move adapter rejects wrong authenticated peer");
                    Check(world.Receive(report,7,1.2)==MoveReportResult.Accepted && Math.Abs(remote.Body.position.x-3)<.001,"actual Move adapter accepts owned pose");
                    world.Relocate(baseline.Identity,new MovementState(new MoveVector(5,3,0),default),new MoveRecovery(new MoveVector(0,4,0),default));
                    world.Advance(1.3);Collect(7,1.3,ownerWire);
                    // Bootstrap of both actors gets repeated. Client ignores correction duplicates by ID.
                    var retainedModel=remote.HostMovement;uint beforeDisable=retainedModel.LastCorrection.Id;
                    remote.DisableMove();world.Advance(1.4);Check(!retainedModel.Ready,"host disabled Move fences owner reports");
                    remote.EnableMove();world.Advance(1.5);
                    Check(retainedModel.Ready && remote.Body.isKinematic && retainedModel.LastCorrection.Id==MoveOrder.Next(beforeDisable),"host re-enable emits one fresh baseline without waiting forever for owner rebind");
                }
            }
            Check(!remote.Body.isKinematic,"host movement teardown restores owned kinematic setting");End();

            // Actual owners, not a generic callback standing in for three distinct lifecycle paths.
            Begin(EveSessionRole.Host);session=EveRegistrySession.Context;
            using(var world=new PNMoveHostWorld(session))
            {
                long executionActor=5100;
                foreach(string kind in new[]{"shared","primary","auxiliary"})
                {
                    var move=Actor(++executionActor,0,kind);move.Body.useGravity=false;world.Track(move,false);
                    var clock=move.GetComponent<EveTimeScale>();clock.MinScale=0;
                    Action<float> tick;Func<bool> active;Action startMotion;Action stopMotion;
                    IDisposable contribution=null;
                    if(kind=="shared")
                    {
                        var skill=move.GetComponent<PNSharedDashEditorProbe>();
                        startMotion=()=>Check(skill.TryDash(),"shared owner starts registered motion");tick=skill.TickDash;active=()=>skill.IsDashing;stopMotion=()=>skill.TryInterrupt(PNControlIds.Dash);
                    }
                    else if(kind=="primary")
                    {
                        var skill=move.GetComponent<PNEveAttackSpeedProbe>();
                        startMotion=()=>Check(skill.TrySkill(),"primary owner starts registered motion");tick=skill.TickAttack;active=()=>skill.IsBusy;stopMotion=()=>skill.TryInterrupt(PNControlIds.Skill);
                    }
                    else
                    {
                        var skill=move.GetComponent<PNSkillEffectsNativeProbe>();
                        contribution=skill.Effects.Contribute("Move native auxiliary",new PNSkillContribution(JObject.Parse("{'readMode':'Live','effects':[{'trigger':'OnActivated','kind':'Action','id':'base:motion_action','data':{'perform':{'distance':1,'duration':1,'ignoreEntities':true,'permissionId':'base:motion_voluntary'}},'direction':'Invocation','ownership':'Independent','affectedByAttackSpeed':false}]}"),hub));
                        startMotion=()=>{Check(skill.Begin(PNSkillEffectsNativeProbe.SkillEntry),"auxiliary owner begins invocation");skill.Commit();};tick=skill.Tick;active=()=>!skill.Controls.IsAllowed(PNControlIds.Move);stopMotion=()=>skill.End(true);
                    }
                    startMotion();var startPose=move.Body.position;tick(0);
                    Check(Vector3.Distance(startPose,move.Body.position)<.0001f && active(),kind+" zero entity delta preserves active motion");
                    var freeze=clock.AddTimedModifier(EveId.Parse("test:move_freeze"),EveTimeModifierKind.Scale,-1,10);move.Tick();startPose=move.Body.position;tick(0);Physics.Simulate(.02f);
                    Check(Vector3.Distance(startPose,move.Body.position)<.001f && active(),kind+" freeze pauses without cancellation");
                    clock.RemoveModifier(freeze);move.Tick();tick(.01f);Check(active(),kind+" thaw retains execution");
                    var id=move.NetworkIdentity(session.SessionId);
                    world.Relocate(id,new MovementState(new MoveVector(20,5,0),default),default);tick(.1f);
                    Check(!active() && move.CanMove && Math.Abs(move.Body.position.x-20)<.001f,kind+" relocation notifies exact owner and blocks stale motion");
                    if(kind=="primary")move.GetComponent<PNEveAttackSpeedProbe>().SkillRecovery.ResetToFull();
                    startMotion();bool reentered=false;
                    PNMoveFaultMotion.CancelHook=()=>
                    {
                        // Try the SAME owner path while the old callback is unwinding. Composition rejects replacement.
                        try { startMotion();reentered=active(); } catch { }
                        throw new InvalidOperationException("EXPECTED Move native owner callback fault: "+kind);
                    };
                    world.Relocate(id,new MovementState(new MoveVector(25,5,0),default),default);tick(.1f);
                    Check(!active() && move.CanMove && !reentered && Math.Abs(move.Body.position.x-25)<.001f,kind+" callback throw/re-entry leaves no stale write or stranded wrapper");
                    if(kind=="primary")move.GetComponent<PNEveAttackSpeedProbe>().SkillRecovery.ResetToFull();
                    startMotion();Check(active(),kind+" callback failure does not permanently block future execution");stopMotion();
                    stopMotion();contribution?.Dispose();
                }
            }
            End();

            Begin(EveSessionRole.Client);session=EveRegistrySession.Context;session.Admission.AdmitHost(hostId,compatibility,7);Floor();
            var owner=Actor(5002,7);var other=Actor(5001,0);body=owner.Body;
            using(var client=new PNMoveNativeClient(session,hostId))
            {
                client.Bind(owner);client.Bind(other);Check(!owner.IsReady && !owner.CanWriteMotion,"owner blocked until coherent Move baseline");
                foreach(var bytes in ownerWire)client.Receive(bytes);
                owner.Tick();Check(owner.IsReady && owner.OwnerMovement.AppliedCorrection==3 && Math.Abs(body.position.x-5)<.001,"actual owner adapter bootstrap/correction applies at fixed boundary");
                Check(!owner.GetComponent<EveEntityLoader>().LoadContext.CanMutate,"prediction never grants gameplay mutation");
                Check(PNLocalPlayerInput.Allows(owner.GetComponent<EveEntityLoader>()) && !PNLocalPlayerInput.Allows(other.GetComponent<EveEntityLoader>()),"client device input restricted to admitted owner");
                owner.SetMoveDirection(Vector2.right);start=body.position;for(int i=0;i<5;i++){owner.Tick();Physics.Simulate(.02f);}
                Check(body.position.x>start.x+.1f,"native owner prediction moves client Rigidbody");
                object[] reports=null;Check(client.SendReports(batch=>{reports=batch;return true;}) && reports.Length>0 && reports.All(b=>b is MovePoseReport),"native per-fixed reports on compact channel");
                var identity=owner.NetworkIdentity(hostId);var old=owner.OwnerMovement.Mirror;
                var frozen=new MoveMirror(identity,MoveOrder.Next(old.Revision),old.LifeRevision,false,old.Rights,old.MoveSpeed,old.JumpSpeed,0,0,old.MotionPermissions);
                client.Receive((frozen));
                var correction=new MoveCorrection(identity,4,4,frozen.Revision,new MovementState(new MoveVector(6,4,0),default),new MoveRecovery(new MoveVector(0,6,0),default,true),MoveCorrectionReason.Constraint);
                client.Receive((correction));owner.Tick();start=body.position;
                for(int i=0;i<5;i++){owner.Tick();Physics.Simulate(.02f);}
                Check(Vector3.Distance(start,body.position)<.001 && body.linearVelocity==Vector3.zero && body.constraints==RigidbodyConstraints.FreezeAll,"native frozen correction holds coherent physical pose");
                var thaw=new MoveMirror(identity,MoveOrder.Next(frozen.Revision),old.LifeRevision,false,old.Rights,old.MoveSpeed,old.JumpSpeed,1,1,old.MotionPermissions);
                client.Receive((thaw));owner.Tick();
                Check(body.linearVelocity.y>5.9f && body.constraints==RigidbodyConstraints.FreezeRotation && body.useGravity,"native thaw uses corrected resume velocity and restores owned physics");
                foreach(var bytes in observerWire)client.Receive(bytes);client.AdvanceObservers(1.15);
                Check(other.Body.isKinematic && other.ObserverMovement.Count>0,"native observer applies compact samples without locomotion");
                other.GetComponent<EveEntityLoader>().Unload();client.AdvanceObservers(2);
                Check(other.ObserverMovement==null,"client adapter retires unloaded observer");
                owner.DisableMove();Check(!owner.CanWriteMotion && owner.OwnerMovement==null,"disable invalidates prediction and pending state");
                client.Unbind(identity);owner.EnableMove();client.Bind(owner);
                foreach(var bytes in ownerWire)client.Receive(bytes);owner.Tick();
                Check(!owner.IsReady && owner.Body.isKinematic,"same-generation rebind holds physics and rejects queued old baseline");
                client.Receive((thaw));client.Receive((new MoveCorrection(identity,5,5,thaw.Revision,new MovementState(new MoveVector(7,4,0),default),default,MoveCorrectionReason.Baseline,100,2)));owner.Tick();
                object[] rebound=null;client.SendReports(batch=>{rebound=batch;return true;});
                Check(owner.IsReady && rebound.OfType<MovePoseReport>().Single().Sequence==101,"actual adapter rebind uses matching baseline and monotonic sequence floor");
                var dead=new MoveMirror(identity,MoveOrder.Next(thaw.Revision),2,true,thaw.Rights,thaw.MoveSpeed,thaw.JumpSpeed,1,1,thaw.MotionPermissions);
                client.Receive((dead));owner.Tick();start=body.position;
                for(int i=0;i<5;i++)Physics.Simulate(.02f);
                Check(body.isKinematic && !owner.IsReady && Vector3.Distance(start,body.position)<.001f,"dead airborne owner holds physics even before dependent correction");
                client.Receive((new MoveCorrection(identity,6,6,dead.Revision,new MovementState(new MoveVector(7,4,0),default),default,MoveCorrectionReason.Death,101,2)));owner.Tick();start=body.position;
                for(int i=0;i<5;i++)Physics.Simulate(.02f);
                Check(body.isKinematic && Vector3.Distance(start,body.position)<.001f,"dead airborne correction stays held after apply-once acknowledgement");
                var alive=new MoveMirror(identity,MoveOrder.Next(dead.Revision),3,false,dead.Rights,dead.MoveSpeed,dead.JumpSpeed,1,1,dead.MotionPermissions);
                client.Receive((alive));owner.Tick();Check(!owner.IsReady && body.isKinematic,"revive mirror alone cannot release a held body before new epoch correction");
                client.Receive((new MoveCorrection(identity,7,7,alive.Revision,new MovementState(new MoveVector(8,4,0),default),default,MoveCorrectionReason.Death,101,2)));owner.Tick();
                Check(owner.IsReady && !body.isKinematic,"revive compatible correction releases owned hold");
                owner.SetMoveDirection(Vector2.zero);for(int i=0;i<60;i++){owner.Tick();Physics.Simulate(.02f);}owner.Tick();
                var noJump=new MoveMirror(identity,MoveOrder.Next(alive.Revision),3,false,alive.Rights&~MoveRights.Jump,alive.MoveSpeed,alive.JumpSpeed,1,1,alive.MotionPermissions);
                client.Receive((noJump));Check(owner.IsGrounded && !owner.TryJump(),"client native jump obeys mirrored independent jump denial");
                var jump=new MoveMirror(identity,MoveOrder.Next(noJump.Revision),3,false,alive.Rights,alive.MoveSpeed,alive.JumpSpeed,1,1,alive.MotionPermissions);
                client.Receive((jump));Check(owner.TryJump() && body.linearVelocity.y>0 && !owner.GetComponent<EveEntityLoader>().LoadContext.CanMutate,"client native mirrored jump writes physics without granting gameplay mutation");
            }
            End();Check(SceneManager.GetActiveScene()==original && original.rootCount==0,"native fixture preserves empty clone scene state");
            return "PASS "+checks+" Move native checks\n"+string.Join("\n",lines);
        }
        finally { try { End(); } finally { Physics.simulationMode=previousSimulation; } }
    }
}
#endif
