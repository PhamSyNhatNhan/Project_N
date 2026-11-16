using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using Mirror;
using Newtonsoft.Json;
using UnityEngine;

namespace ProjectN.Coop.Tests
{
    // PreT5 measurement only. No scheduler/wire changes; optional clone instrumentation
    // records production boundaries separately (see the retained clone patch script).
    public static class PNActionTimePlayChecks
    {
        public static bool Enabled => Environment.GetCommandLineArgs().Contains("--pn-action-time");
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static IEnumerator Wait(Func<bool> ready,string label)
        { double until=Time.realtimeSinceStartupAsDouble+45;while(!ready()){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException(label);yield return null;} }
        public static IEnumerator Run(string folder,string peer,PNPlayerMove subject)
        {
            Exception failure=null;var stack=new Stack<IEnumerator>();stack.Push(Measure(folder,peer,subject));
            try { while(stack.Count>0){var e=stack.Peek();bool next=false;try{next=e.MoveNext();}catch(Exception x){failure=x;}
                if(failure!=null)break;if(!next){(stack.Pop() as IDisposable)?.Dispose();continue;}
                if(e.Current is IEnumerator child){stack.Push(child);continue;}yield return e.Current;} }
            finally {while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();}
            File.WriteAllText(Path.Combine(folder,peer+".txt"),failure==null?"PASS PreT5 measurement fixture":"FAIL PreT5 "+failure);
            if(failure!=null)Debug.LogException(failure);Application.Quit(failure==null?0:1);
        }
        private static IEnumerator Measure(string folder,string peer,PNPlayerMove subject)
        {
            bool host=NetworkServer.active,owner=peer=="client1";
            string P(string s)=>Path.Combine(folder,s);
            void Mark(string s)=>File.WriteAllText(P(s),"ok");
            var skill=subject.GetComponent<EveCharacterSkill>();var cast=subject.GetComponent<PNSkillCastView>();
            var remote=subject.GetComponent<PNSkillRemoteInput>();var session=EveRegistrySession.Context;
            if(skill==null||cast==null)throw new Exception("PreT5 needs Eve sweep character");
            var clock=PNTimeService.For(session);IPNCameraHandle camera=null;
            var stat=subject.GetComponent<PNStat>();var speedSource=EveId.Parse("test:pret5_speed");int captures=0;int lastPlayed=cast.PlayedCount;int currentPulse=-1;
            Exception producerFailure = null;
            float originalGlobalRate=Time.timeScale;
            var entityClock=subject.GetComponent<EveTimeScale>();long entityModifier=0,minReduction=0;
            var probeObject=new GameObject("PreT5 disposable frame probe");var probe=probeObject.AddComponent<PNBuffViewFrameProbe>();
            using(var output=new StreamWriter(P(peer+"-frames.jsonl")){AutoFlush=true})
            using(var packets=new StreamWriter(P(peer+"-network.jsonl")){AutoFlush=true})
            {
                void Packet(string direction,NetworkDiagnostics.MessageInfo info)
                {
                    var state=info.message is PNSyncStateMessage message?(object)new{message.State,message.Entity,message.Revision}:
                        info.message is PNMovePacket move?(object)new{State="move",move.Stream.Entity,move.Kind}:null;
                    packets.WriteLine(JsonConvert.SerializeObject(new{direction,pulse=currentPulse,local=Time.unscaledTimeAsDouble,frame=Time.frameCount,type=info.message.GetType().Name,info.bytes,info.count,state}));
                }
                Action<NetworkDiagnostics.MessageInfo> incoming=info=>Packet("in",info),outgoing=info=>Packet("out",info);
                NetworkDiagnostics.InMessageEvent+=incoming;NetworkDiagnostics.OutMessageEvent+=outgoing;
                probe.Tick=()=>{if(!session.IsActive)return;var s=clock.Sample();var at=subject.transform.position;
                    if (Environment.GetCommandLineArgs().Contains("--pn-action-producer"))
                    {
                        var records = host ? PNSkillCastView.ParseRuns(cast.Capture(), out _) : cast.ReceivedRuns;
                        var running = typeof(EveCharacterSkill).GetField("attack", Flags)?.GetValue(skill);
                        var elapsed = running == null ? -1 : (float)running.GetType().GetField("elapsed", Flags).GetValue(running);
                        File.AppendAllText(P(peer+"-producer.jsonl"), JsonConvert.SerializeObject(new { frame=Time.frameCount, stamp=s.Local, pulse=currentPulse, elapsed, key=skill.CurrentInvocation?.CastKey??0, records, rejected=skill.ActionRuns?.Rejected??0 })+"\n");
                        if (host && records.Any(r => r.End == PNActionRunEnd.Active && r.Key.Cast == skill.CurrentInvocation?.CastKey && elapsed >= 0 && Math.Abs(r.Progress - elapsed) > .00001))
                            producerFailure = new Exception("T5.1 producer progress differs from actual sweep elapsed");
                    }
                    output.WriteLine(JsonConvert.SerializeObject(new{pulse=currentPulse,frame=Time.frameCount,local=s.Local,host=s.Host,presentation=s.Presentation,ready=s.Ready,uncertainty=s.Uncertainty,
                        x=at.x,z=at.z,role=subject.NetworkRole.ToString(),observed=skill.ObservedActionCount,played=cast.PlayedCount,serial=cast.LastSerial,busy=skill.IsBusy,
                        pending=remote.PendingPredictions,ack=remote.AckedKey,castKey=skill.CurrentInvocation?.CastKey??0,
                        attackSpeed=host?stat.Evaluate(PNContentIds.AttackSpeed):0,globalRate=Time.timeScale,entityRate=entityClock!=null?entityClock.Scale:1}));
                    if(peer=="client2"&&cast.PlayedCount!=lastPlayed&&skill.ObservedActionCount>0&&captures<6)
                    {lastPlayed=cast.PlayedCount;typeof(PNTimeBaselineChecks).GetMethod("CaptureClockFrame",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{P("observed-"+(captures++)+".png")});}
                };
                try
                {
                    if(peer=="client2")camera=PNCameraServices.Resolve(session).RequestCinematic(subject,"pret5-action",PNCinematicShot.Follow(subject.transform,new Vector3(0,2.8f,-2.8f),new Vector3(45,0,0),fieldOfView:38,blendIn:0,blendOut:0,priority:100,aimTarget:subject.transform,aimOffset:new Vector3(0,.5f,0)));
                    Mark(peer+"-action-ready");
                    yield return Wait(()=>File.Exists(P("host-action-ready"))&&File.Exists(P("client1-action-ready"))&&File.Exists(P("client2-action-ready")),"action peers");
                    yield return new WaitForSecondsRealtime(2);
                    bool faults=Environment.GetCommandLineArgs().Contains("--pn-action-probes");
                    int count=faults?9:34;
                    for(int pulse=0;pulse<count;pulse++)
                    {
                        currentPulse=pulse;bool repeatPulse=pulse==count-2||(faults&&pulse>=2&&pulse<=5);
                        if(owner)
                        {
                            yield return Wait(()=>!skill.IsBusy&&!skill.IsDashing&&remote.PendingPredictions==0,"idle pulse "+pulse);
                            File.WriteAllText(P("action-phase"),pulse.ToString());Mark("action-request-"+pulse);
                        }
                        yield return Wait(()=>File.Exists(P("action-request-"+pulse)),"pulse request");
                        if(host)
                        {
                            if(repeatPulse)skill.BurstGauge.Add(100000);
                            if(faults&&repeatPulse)
                            {
                                if(!skill.IsInBurst&&!skill.TryBurst())throw new Exception("fault fixture Burst setup failed");
                                skill.SlashStacks.Add(100000);
                                foreach(var state in subject.GetComponent<PNAbilityManager>().Abilities.Find(EveCharacterSkill.RepeatExhaustedId).OfType<PNAbilityState>())if(state.IsActive)state.End();
                                // Let this explicit authority-side fixture setup reach the owner
                                // before blocking Sync; otherwise no input runs during the stall.
                                yield return new WaitForSecondsRealtime(.6f);
                            }
                            if(faults&&pulse==6)
                            {
                                var rows=(Newtonsoft.Json.Linq.JArray)cast.Capture()["e"];
                                string data=(string)rows.Last[2];
                                for(int i=0;i<16;i++)typeof(PNSkillCastView).GetMethod("OnCastEvent",Flags).Invoke(cast,new object[]{new PNSkillCastEvent("action",data,1,0,.3f)});
                                for(int i=0;i<16;i++)typeof(PNSkillCastView).GetMethod("OnCastEvent",Flags).Invoke(cast,new object[]{new PNSkillCastEvent("end",data,1,0,0)});
                                Mark("synthetic-ring-pressure-16-start-end");
                            }
                            if(faults&&pulse>=2&&pulse<=4)File.WriteAllText(P("sync-stall-until"),(Time.realtimeSinceStartupAsDouble+new[]{.2,.5,2}[pulse-2]).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                            Mark("action-authorized-"+pulse);
                        }
                        yield return Wait(()=>File.Exists(P("action-authorized-"+pulse)),"pulse authorized");
                        if(owner)
                        {
                            bool repeat=repeatPulse;bool ultimate=pulse==count-1;
                            if(repeat&&!skill.IsInBurst)
                            {
                                yield return Wait(()=>skill.BurstGauge.Current>=skill.Moveset.BurstCost,"Burst gauge");
                                foreach(var phase in new[]{PNSkillInputPhase.Started,PNSkillInputPhase.Performed,PNSkillInputPhase.Canceled})skill.RequestSlot(PNSkillSlots.Burst,new PNSkillInputEvent(phase,PNSkillInteraction.Tap));
                                long burstKey=remote.LastSentKey;
                                yield return Wait(()=>remote.AckedKey>=burstKey&&skill.IsInBurst&&!skill.IsBusy,"Burst active for Repeat");
                            }
                            var slot=ultimate?PNSkillSlots.Ultimate:PNSkillSlots.Attack;
                            if(ultimate)yield return Wait(()=>skill.UltimateRecovery.Current>=1,"ultimate charge");
                            var interaction=repeat?PNSkillInteraction.Repeat:PNSkillInteraction.Tap;
                            if(!skill.RequestSlot(slot,new PNSkillInputEvent(PNSkillInputPhase.Started,interaction))||!skill.RequestSlot(slot,new PNSkillInputEvent(PNSkillInputPhase.Performed,interaction)))throw new Exception("input declined "+pulse);
                            if(repeat)yield return new WaitForSecondsRealtime(3);
                            skill.RequestSlot(slot,new PNSkillInputEvent(PNSkillInputPhase.Canceled,interaction));
                            long sent=remote.LastSentKey;
                            yield return Wait(()=>remote.AckedKey>=sent&&!skill.IsBusy&&remote.PendingPredictions==0,"cast finished "+pulse);
                            Mark("action-done-"+pulse);
                        }
                        if(host&&faults&&pulse==5)
                        {
                            yield return Wait(()=>skill.CurrentCast==EveCast.Repeat,"live speed cast started");
                            double baseSpeed=stat.Evaluate(PNContentIds.AttackSpeed);
                            foreach(double targetSpeed in new[]{200d,0d,100d})
                            {
                                stat.SetSource(speedSource,new[]{new EveStatModifier(PNContentIds.AttackSpeed,targetSpeed-baseSpeed,0)});
                                if(Math.Abs(stat.Evaluate(PNContentIds.AttackSpeed)-targetSpeed)>.001)throw new Exception("speed probe did not reach target");
                                yield return new WaitForSecondsRealtime(.12f);
                            }
                            stat.RemoveSource(speedSource);
                            foreach(float globalRate in new[]{0f,.5f,1f,2f,4f,8f})
                            {skill.SlashStacks.Add(100000);Time.timeScale=globalRate;yield return new WaitForSecondsRealtime(.12f);}
                            Time.timeScale=originalGlobalRate;
                            minReduction=entityClock.AddOwnedModifier(speedSource,EveTimeModifierKind.MinScaleReduction,entityClock.MinScale,subject);
                            foreach(float scaleDelta in new[]{-.75f,-1f})
                            {
                                skill.SlashStacks.Add(100000);entityModifier=entityClock.AddOwnedModifier(speedSource,EveTimeModifierKind.Scale,scaleDelta,subject);
                                if(Math.Abs(entityClock.Scale-(1+scaleDelta))>.001)throw new Exception("entity probe did not reach target");
                                yield return new WaitForSecondsRealtime(.15f);entityClock.RemoveModifier(entityModifier);entityModifier=0;
                            }
                            entityClock.RemoveModifier(minReduction);minReduction=0;
                        }
                        yield return Wait(()=>File.Exists(P("action-done-"+pulse)),"pulse done");
                        yield return new WaitForSecondsRealtime(.35f);
                        Mark(peer+"-action-pulse-"+pulse);
                    }
                    yield return new WaitForSecondsRealtime(2);
                    if(!session.IsActive)throw new Exception("session ended during probe");
                    if (producerFailure != null) throw producerFailure;
                    Mark(peer+"-action-complete");
                    yield return Wait(()=>File.Exists(P("host-action-complete"))&&File.Exists(P("client1-action-complete"))&&File.Exists(P("client2-action-complete")),"complete barrier");
                    if(faults)
                    {
                        if(peer=="client2")
                        {
                            NetworkClient.Disconnect();
                            yield return Wait(()=>skill==null||skill.ObservedActionCount==0,"disconnect observed cleanup");
                            Mark("client2-disconnect-cleanup-pass");
                        }
                        else yield return Wait(()=>File.Exists(P("client2-disconnect-cleanup-pass")),"observer disconnect checked");
                    }
                }
                finally {NetworkDiagnostics.InMessageEvent-=incoming;NetworkDiagnostics.OutMessageEvent-=outgoing;probe.Tick=null;UnityEngine.Object.Destroy(probeObject);camera?.Dispose();Time.timeScale=originalGlobalRate;if(entityModifier!=0&&entityClock!=null)entityClock.RemoveModifier(entityModifier);if(minReduction!=0&&entityClock!=null)entityClock.RemoveModifier(minReduction);if(host&&session.IsActive)stat.RemoveSource(speedSource);}
            }
        }
    }
}
