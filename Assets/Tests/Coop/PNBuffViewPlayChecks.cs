using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using Mirror;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ProjectN.Coop.Tests
{
    // Opt-in paired passive fixture. Requests are file coordination, not gameplay input.
    // State, Ability images and Buff records still travel through the actual transports.
    public sealed class PNBuffViewFrameProbe : MonoBehaviour
    {
        public Action Tick;
        private void LateUpdate()=>Tick?.Invoke();
    }
    public static class PNBuffViewPlayChecks
    {
        public static bool Enabled => Environment.GetCommandLineArgs().Contains("--pn-buff-view");
        private static readonly EveId Buff = EveId.Parse("project_n:timescale_reduction");
        private static readonly EveId Ability = EveId.Parse("test:t4b_pulse");
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static IEnumerator Wait(Func<bool> predicate,string label)
        {
            double until=Time.realtimeSinceStartupAsDouble+40;
            while(!predicate()){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException(label);yield return null;}
        }
        public static IEnumerator Run(string folder,string peer,PNPlayerMove subject)
        {
            Exception error=null;var stack=new Stack<IEnumerator>();stack.Push(Measure(folder,peer,subject));
            try
            {
                while(stack.Count>0)
                {
                    var e=stack.Peek();bool next=false;try{next=e.MoveNext();}catch(Exception x){error=x;Debug.LogError("T4b original failure: "+x+" network="+(NetworkManager.singleton as PNNetworkManager)?.LastFailure);}
                    if(error!=null)break;
                    if(!next){(stack.Pop() as IDisposable)?.Dispose();continue;}
                    if(e.Current is IEnumerator child){stack.Push(child);continue;}yield return e.Current;
                }
            }
            finally{while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();}
            File.WriteAllText(Path.Combine(folder,peer+".txt"),error==null?"PASS T4b paired Buff/Ability30 events":"FAIL T4b "+error);
            if(error!=null)Debug.LogException(error);Application.Quit(error==null?0:1);
        }
        private static IEnumerator Measure(string folder,string peer,PNPlayerMove subject)
        {
            bool host=NetworkServer.active,owner=peer=="client1";
            var network=(PNNetworkManager)NetworkManager.singleton;var buffs=subject.GetComponent<PNBuffManager>();
            var session=EveRegistrySession.Context;var stablePresentation=buffs.Presentation;
            var abilities=subject.GetComponent<PNAbilityManager>().Abilities;var view=subject.GetComponent<PNSkillOwnerView>();
            var definition=new PNAbilitySettings{Id=Ability.ToString(),Kind=PNAbilityKind.State,Clock=PNAbilityClock.Game,HasDuration=true,Duration=20}.Create();
            if(host)abilities.Define(definition);else typeof(PNAbilityCollection).GetMethod("DefineBindingAbility",Flags).Invoke(abilities,new object[]{definition});
            string P(string name)=>Path.Combine(folder,name);
            void Mark(string name)=>File.WriteAllText(P(name),"ok");
            int pulse=-1;PNBuffHandle handle=default;int added=0,removed=0;
            var ui=owner?UnityEngine.Object.FindAnyObjectByType<PNUiRuntime>():null;
            EveEntityId previousUiPlayer=ui!=null?ui.LocalPlayerId:default;
            if(ui!=null){ui.LocalPlayerId=subject.GetComponent<EveEntityIdentity>().Id;ui.RefreshUI();}
            var handlers=!host?(IDictionary)typeof(NetworkClient).GetField("handlers",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null):null;
            object oldHandler=null;ushort messageId=NetworkMessageId<PNBuffPacket>.Id;
            using(var output=new StreamWriter(P(peer+"-view.jsonl")){AutoFlush=true})
            {
                void Log(string kind,object data=null)
                {
                    if(!session.IsActive)return;var s=PNTimeService.For(session).Sample();
                    output.WriteLine(JsonConvert.SerializeObject(new{kind,pulse,local=s.Local,host=s.Host,presentation=s.Presentation,uncertainty=s.Uncertainty,ready=s.Ready,frame=Time.frameCount,role=subject.NetworkRole.ToString(),data}));
                }
                Action<PNBuffPresentationChange> changed=c=>
                {
                    if(c.State.Id!=Buff)return;
                    if(c.Kind==PNBuffPresentationChangeKind.Added)added++;
                    if(c.Kind==PNBuffPresentationChangeKind.Removed)removed++;
                    Log("buff-"+c.Kind,new{instance=c.State.InstanceId,sequence=c.Sequence});
                };
                Action applied=()=>Log("ability-image",new{revision=view.AppliedRevision,active=abilities.Find(Ability).OfType<PNAbilityState>().SingleOrDefault()?.IsActive ?? false,remaining=abilities.Find(Ability).OfType<PNAbilityState>().SingleOrDefault()?.DurationRemaining});
                stablePresentation.PresentationChanged+=changed;view.Applied+=applied;
                if(!host)
                {
                    oldHandler=handlers[messageId];
                    NetworkClient.ReplaceHandler<PNBuffPacket>(p=>
                    {
                        if(p.Stream.Entity==subject.GetComponent<EveEntityIdentity>().Id.Value)
                            Log("buff-arrival",new{p.Kind,p.Revision,p.Transfer,p.HostTime,p.Time,p.Index,p.Count,p.ClockBaseline});
                        typeof(PNNetworkManager).GetMethod("ReceiveBuffPacket",Flags).Invoke(network,new object[]{p});
                    });
                }
                var probeObject=new GameObject("T4b frame probe");
                var probe=probeObject.AddComponent<PNBuffViewFrameProbe>();
                probe.Tick=()=>
                {
                    if(!session.IsActive||buffs==null||!buffs.IsReady)return;
                    buffs.TryReadPresentationTiming(Buff,out var timing);
                    var card=ui?.HudRoot?.Q("player-buffs")?.Q("buff-"+timing.InstanceId);
                    Log("tick",new{timing.Present,timing.Remaining,instance=timing.InstanceId,hudCard=card!=null&&card.panel!=null&&card.resolvedStyle.display!=DisplayStyle.None,opacity=card?.resolvedStyle.opacity});
                };
                try
                {
                    Mark(peer+"-view-ready");
                    yield return Wait(()=>File.Exists(P("host-view-ready"))&&File.Exists(P("client1-view-ready"))&&File.Exists(P("client2-view-ready")),"paired declarations");
                    for(pulse=0;pulse<30;pulse++)
                    {
                        if(owner){Log("fixture-request",new{predicted=false});Mark("view-request-"+pulse);}
                        if(host)
                        {
                            yield return Wait(()=>File.Exists(P("view-request-"+pulse)),"fixture request");
                            abilities.Find(Ability).Cast<PNAbilityState>().Single().TryActivate(20);
                            var receipt=buffs.Buffs.Apply(new PNBuffApplyRequest(Buff,PNCombatSource.Capture(subject.GetComponent<PNStat>()),new JObject{["duration"]=20,["reduction"]=0,["minimumReduction"]=0}));
                            if(receipt.Status!=PNBuffStatus.Applied)throw new Exception("pulse apply "+receipt.Status+" "+receipt.Message);
                            handle=receipt.Handle;Log("authority-start",new{instance=handle.InstanceId});Mark("view-start-"+pulse);
                        }
                        yield return Wait(()=>File.Exists(P("view-start-"+pulse)),"authority start");
                        yield return Wait(()=>buffs.TryReadPresentationTiming(Buff,out var t)&&t.Present,"Buff first-on");
                        Log("visible-first",new{ability=abilities.Find(Ability).OfType<PNAbilityState>().SingleOrDefault()?.IsActive ?? false});
                        if(!host)
                        {
                            if(owner&&pulse==2){yield return new WaitForSecondsRealtime(.2f);ScreenCapture.CaptureScreenshot(P(peer+"-view-on.png"));yield return null;}
                            Mark(peer+"-view-on-"+pulse);
                        }
                        double until=Time.realtimeSinceStartupAsDouble+.3;
                        while(Time.realtimeSinceStartupAsDouble<until)
                        {
                            buffs.TryReadPresentationTiming(Buff,out var t);Log("sample",new{t.Present,t.Remaining,buffNow=buffs.AuthoritativePresentationTime?.Invoke(),ability=abilities.Find(Ability).OfType<PNAbilityState>().SingleOrDefault()?.DurationRemaining});yield return null;
                        }
                        if(host)
                        {
                            yield return Wait(()=>File.Exists(P("client1-view-on-"+pulse))&&File.Exists(P("client2-view-on-"+pulse)),"all viewers on");
                            buffs.Buffs.Remove(handle);handle=default;abilities.Find(Ability).Cast<PNAbilityState>().Single().End();Log("authority-end");Mark("view-end-"+pulse);
                        }
                        yield return Wait(()=>File.Exists(P("view-end-"+pulse)),"authority end");
                        yield return Wait(()=>!buffs.TryReadPresentationTiming(Buff,out var t)||!t.Present,"Buff first-off");
                        Log("visible-off");Mark(peer+"-view-off-"+pulse);
                        if(host)yield return Wait(()=>File.Exists(P("client1-view-off-"+pulse))&&File.Exists(P("client2-view-off-"+pulse)),"all viewers off");
                    }
                    if(added!=30||removed!=30)throw new Exception("paired lifecycle counts "+added+"/"+removed);
                    if(Environment.GetCommandLineArgs().Contains("--pn-buff-view-cuts"))
                        yield return Cuts(folder,peer,subject,Log);
                    Log("complete",new{added,removed});Mark(peer+"-view-complete");
                    yield return Wait(()=>File.Exists(P("host-view-complete"))&&File.Exists(P("client1-view-complete"))&&File.Exists(P("client2-view-complete")),"all complete");
                }
                finally
                {
                    probe.Tick=null;UnityEngine.Object.Destroy(probeObject);
                    stablePresentation.PresentationChanged-=changed;view.Applied-=applied;
                    if(handlers!=null)handlers[messageId]=oldHandler;
                    if(ui!=null){ui.LocalPlayerId=previousUiPlayer;ui.RefreshUI();}
                    if(host&&buffs!=null&&buffs.IsReady&&handle.IsAlive)buffs.Buffs.Remove(handle);
                }
            }
        }
        private static object Read(object value,string name)
            =>value.GetType().GetField(name,Flags)?.GetValue(value)??value.GetType().GetProperty(name,Flags)?.GetValue(value);
        private static IEnumerator Cuts(string folder,string peer,PNPlayerMove subject,Action<string,object> log)
        {
            bool host=NetworkServer.active;var network=(PNNetworkManager)NetworkManager.singleton;
            string P(string n)=>Path.Combine(folder,n);
            void Mark(string n)=>File.WriteAllText(P(n),"ok");
            var buffs=subject.GetComponent<PNBuffManager>();PNBuffHandle handle=default,burnHandle=default;
            var replica=buffs.Replica;var cueIds=new HashSet<long>();bool duplicateCue=false;
            Action<PNBuffCue> cue=c=>{if(!cueIds.Add(c.Id))duplicateCue=true;log("cut-cue",new{c.Id,c.Name,c.InstanceId,c.At});};
            replica.CuePresented+=cue;
            try
            {
            if(peer=="client1")Mark("view-cut-request");
            if(host)
            {
                yield return Wait(()=>File.Exists(P("view-cut-request")),"cut request");
                var receipt=buffs.Buffs.Apply(new PNBuffApplyRequest(Buff,PNCombatSource.Capture(subject.GetComponent<PNStat>()),new JObject{["duration"]=30,["reduction"]=0,["minimumReduction"]=0}));
                if(receipt.Status!=PNBuffStatus.Applied)throw new Exception("cut Buff not applied");handle=receipt.Handle;
                var burn=buffs.Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("project_n:burn"),PNCombatSource.Capture(subject.GetComponent<PNStat>())));
                if(burn.Status!=PNBuffStatus.Applied)throw new Exception("cut Burn not applied");burnHandle=burn.Handle;Mark("view-cut-active");
            }
            yield return Wait(()=>File.Exists(P("view-cut-active")),"cut active");
            if(peer=="client1")
            {
                yield return Wait(()=>buffs.TryReadPresentationTiming(Buff,out var t)&&t.Present,"cut Buff visible");
                var bindings=(IDictionary)typeof(PNNetworkManager).GetField("bindings",Flags).GetValue(network);
                long entity=subject.GetComponent<EveEntityIdentity>().Id.Value;
                var b=bindings.Values.Cast<object>().Single(x=>((PNActorMetadata)Read(x,"Metadata")).Entity==entity);
                var move=Read(b,"Movement");var state=Read(b,"Buffs");var view=Read(state,"View");
                var handlers=(IDictionary)typeof(NetworkClient).GetField("handlers",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                ushort id=NetworkMessageId<PNMovePacket>.Id;var saved=handlers[id];bool restored=false;
                uint before=(uint)Read(state,"ClockRequest");
                yield return Wait(()=>(int)Read(Read(move,"Controls"),"Count")==0&&Read(move,"Applying")==null,"drained movement before isolated pose-role probe");
                try
                {
                    // This is a pose-role adapter probe, not an ownership-transfer
                    // feature: isolate its old owner Move controls, then request a
                    // genuine fresh owner baseline when restoring the real role.
                    NetworkClient.ReplaceHandler<PNMovePacket>(p=>{if(p.Stream.Entity!=entity)typeof(PNNetworkManager).GetMethod("ReceiveMovePacket",Flags).Invoke(network,new object[]{p});});
                    move.GetType().GetField("LatestReport",Flags).SetValue(move,null);
                    subject.BindObserverMovement(EveRegistrySession.Context.Admission.AdmittedHostSession);
                    log("cut-to-observer",new{before});
                    yield return Wait(()=>(bool)Read(view,"Prepared")&&Read(view,"Active").ToString()=="Presentation"&&(uint)Read(state,"ClockRequest")>before,"same-binding Host to Presentation baseline cut");
                    if(!buffs.TryReadPresentationTiming(Buff,out var timing)||!timing.Present)throw new Exception("role cut lost Buff presence");
                    Mark("client1-view-host-observer-pass");log("cut-observer-prepared",new{request=Read(state,"ClockRequest")});
                    double until=Time.realtimeSinceStartupAsDouble+.4;while(Time.realtimeSinceStartupAsDouble<until)yield return null;
                    ScreenCapture.CaptureScreenshot(P("client1-view-cut-observer.png"));yield return null;
                    before=(uint)Read(state,"ClockRequest");
                    subject.BindOwnerMovement(EveRegistrySession.Context.Admission.AdmittedHostSession);
                    handlers[id]=saved;restored=true;
                    typeof(PNNetworkManager).GetMethod("RequestMove",Flags).Invoke(network,new object[]{b,true});
                    log("cut-to-owner",new{before});
                    yield return Wait(()=>(bool)Read(view,"Prepared")&&Read(view,"Active").ToString()=="Host"&&(uint)Read(state,"ClockRequest")>before&&subject.MovementBaselineApplied&&!subject.MovementHeld,"same-binding Presentation to Host cut and actual owner recovery");
                    Mark("client1-view-observer-host-pass");log("cut-owner-prepared",new{request=Read(state,"ClockRequest")});
                    var skill=subject.GetComponent<PNPlayerSkill>();var remote=subject.GetComponent<PNSkillRemoteInput>();
                    yield return Wait(()=>skill.CanInput&&skill.PC.Controller.Dash.enabled&&skill.DashRecovery.Current>0,"actual InputActions after role cut");
                    var background=InputSystem.settings.backgroundBehavior;
                    InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                    var keyboard=InputSystem.AddDevice<Keyboard>();var start=subject.transform.position;
                    int charges=skill.DashRecovery.Current,guesses=remote.PredictedCount;
                    try
                    {
                        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.K));yield return null;
                        if(!skill.IsPredictingDash||remote.PredictedCount!=guesses+1||skill.DashRecovery.Current!=charges-1)throw new Exception("role cut lost immediate Keyboard prediction");
                        InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                        yield return new WaitForSecondsRealtime(.3f);
                        var distance=subject.transform.position-start;distance.y=0;
                        if(distance.sqrMagnitude<.0025f)throw new Exception("role cut did not restore actual owner body movement");
                        Mark("client1-view-cut-keyboard-body-pass");log("cut-keyboard-dash",new{predicted=true,distance=distance.magnitude});
                    }
                    finally{if(keyboard.added)InputSystem.RemoveDevice(keyboard);InputSystem.settings.backgroundBehavior=background;}
                    until=Time.realtimeSinceStartupAsDouble+1;while(Time.realtimeSinceStartupAsDouble<until)yield return null;
                    ScreenCapture.CaptureScreenshot(P("client1-view-cut-owner.png"));yield return null;
                    if(Environment.GetCommandLineArgs().Contains("--pn-buff-view-expire"))
                    {
                        var session=EveRegistrySession.Context;
                        ushort buffId=NetworkMessageId<PNBuffPacket>.Id;var oldBuff=handlers[buffId];bool suppressed=false;
                        NetworkClient.ReplaceHandler<PNBuffPacket>(p=>
                        {
                            if(p.Stream.Entity==entity&&p.ClockBaseline){suppressed=true;return;}
                            typeof(PNNetworkManager).GetMethod("ReceiveBuffPacket",Flags).Invoke(network,new object[]{p});
                        });
                        try
                        {
                            NetworkClient.ReplaceHandler<PNMovePacket>(p=>{if(p.Stream.Entity!=entity)typeof(PNNetworkManager).GetMethod("ReceiveMovePacket",Flags).Invoke(network,new object[]{p});});
                            move.GetType().GetField("LatestReport",Flags).SetValue(move,null);
                            subject.BindObserverMovement(session.Admission.AdmittedHostSession);restored=false;
                            yield return Wait(()=>(bool)Read(view,"Transition"),"role expiry transition started");
                            double original=(double)Read(view,"Deadline");
                            if(original>PNTimeService.For(session).Sample().Local+PNTimeService.For(session).RemainingRecoverySeconds+.05)throw new Exception("role expiry exceeded actual policy budget");
                            view.GetType().GetProperty("Deadline",Flags).SetValue(view,PNTimeService.For(session).Sample().Local+.5);
                            yield return Wait(()=>!session.IsActive||!string.IsNullOrEmpty(session.ConnectionFailureCode),"actual empty role-cut baseline timeout");
                            if(!suppressed||network.LastFailure==null||!network.LastFailure.Contains("Buff view role/cut deadline"))throw new Exception("role timeout did not use real failure path "+network.LastFailure);
                            Mark("client1-view-empty-cut-timeout-pass");
                        }
                        finally{handlers[buffId]=oldBuff;}
                    }
                }
                finally
                {
                    handlers[id]=saved;
                    if(!restored&&EveRegistrySession.IsReady&&subject!=null&&subject.NetworkUsable){subject.BindOwnerMovement(EveRegistrySession.Context.Admission.AdmittedHostSession);typeof(PNNetworkManager).GetMethod("RequestMove",Flags).Invoke(network,new object[]{b,true});}
                }
                Mark("view-cut-done");
            }
            if(host)
            {
                yield return Wait(()=>File.Exists(P("view-cut-done")),"cut completion");
                if(buffs!=null&&buffs.IsReady){if(handle.IsAlive)buffs.Buffs.Remove(handle);if(burnHandle.IsAlive)buffs.Buffs.Remove(burnHandle);}Mark("view-cut-finished");
            }
            yield return Wait(()=>File.Exists(P("view-cut-finished")),"cut finished");
            yield return Wait(()=>!buffs.TryReadPresentationTiming(Buff,out var t)||!t.Present,"cut Buff removed");
            if(duplicateCue||cueIds.Count==0)throw new Exception("role baseline replayed a cue or cue path never ran");
            Mark(peer+"-view-cues-once-pass");
            log("cut-cues-complete",new{count=cueIds.Count});
            }
            finally
            {
                replica.CuePresented-=cue;
                if(host&&buffs!=null&&buffs.IsReady)
                {if(handle.IsAlive)buffs.Buffs.Remove(handle);if(burnHandle.IsAlive)buffs.Buffs.Remove(burnHandle);}
            }
        }
    }
}



