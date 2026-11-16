using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ProjectN.Coop.Tests
{
    // Opt-in real peers; declarations are passive entity-local test state, no content overrides.
    public static class PNAbilityTimePlayChecks
    {
        public static bool Enabled => Environment.GetCommandLineArgs().Contains("--pn-ability-time");
        private static readonly EveId[] Ids = { EveId.Parse("test:t4_game"), EveId.Parse("test:t4_entity"), EveId.Parse("test:t4_raw") };
        private static IEnumerator Wait(Func<bool> condition, string name)
        {
            double end = Time.realtimeSinceStartupAsDouble + 40;
            while (!condition()) { if (Time.realtimeSinceStartupAsDouble > end) throw new TimeoutException(name); yield return null; }
        }
        public static IEnumerator Run(string folder, string peer, PNPlayerMove subject)
        {
            var session = EveRegistrySession.Context;
            var manager = (PNNetworkManager)NetworkManager.singleton;
            var stack = new Stack<IEnumerator>(); stack.Push(Measure(folder, peer, subject)); Exception failure = null;
            try
            {
                while (stack.Count > 0)
                {
                    var current = stack.Peek(); bool next = false;
                    try { next = current.MoveNext(); } catch (Exception e) { failure = e; }
                    if (failure != null) break;
                    if (!next) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (current.Current is IEnumerator nested) { stack.Push(nested); continue; }
                    yield return current.Current;
                }
            }
            finally { while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose(); }
            File.WriteAllText(Path.Combine(folder,peer+".txt"), failure == null ? "PASS T4 owner Ability matrix" : "FAIL T4 " + failure + "\nconnection=" + session.ConnectionFailureCode + " manager=" + manager.LastFailure);
            if (failure != null) Debug.LogException(failure);
            Application.Quit(failure == null ? 0 : 1);
        }
        private static IEnumerator Measure(string folder, string peer, PNPlayerMove subject)
        {
            bool host = NetworkServer.active, owner = peer == "client1";
            var network = (PNNetworkManager)NetworkManager.singleton;
            var manager = subject.GetComponent<PNAbilityManager>(); var abilities = manager.Abilities;
            var view = subject.GetComponent<PNSkillOwnerView>(); var clock = subject.GetComponent<EveTimeScale>(); var world = subject.GetComponent<EveEntityIdentity>().World;
            float global = Time.timeScale, minimum = clock.MinScale; long modifier = 0;
            string P(string name) => Path.Combine(folder,name);
            void Mark(string name) => File.WriteAllText(P(name),"ok");
            var define = typeof(PNAbilityCollection).GetMethod("DefineBindingAbility",BindingFlags.Instance|BindingFlags.NonPublic);
            for (int i=0;i<3;i++)
            {
                var d = new PNAbilitySettings { Id=Ids[i].ToString(), Kind=PNAbilityKind.Cooldown, Clock=(PNAbilityClock)i, Maximum=4, InitialAmount=4, Cooldown=600 }.Create();
                if (host) abilities.Define(d); else define.Invoke(abilities,new object[]{d});
            }
            Mark(peer+"-ability-ready");
            yield return Wait(()=>File.Exists(P("host-ability-ready"))&&File.Exists(P("client1-ability-ready"))&&File.Exists(P("client2-ability-ready")),"T4 declarations");
            var phases = new[]{"normal","half","double","quad","eight","pause","resume","entity-quarter","entity-stop","raw-floor","suspend","unsuspend","world-pause","world-resume","interruption","recovered"};
            float[] scales = {1,.5f,2,4,8,0,1,1,1,1,1,1,1,1,1,1};
            bool staleRejected=false, recovered=false, inputGuess=false;
            Keyboard keyboard=null;int inputStep=0, inputCharges=0, inputCount=0;
            Vector3 inputPosition=default;double inputAt=0;bool inputMoved=false;
            double nextRow=0;
            using (var csv = new StreamWriter(P(peer+"-ability.csv")))
            {
                csv.WriteLine("phase,local,host,uncertainty,ready,prepared,age,revision,game,entity,raw,entityScale,rawScale,pending,requests,rateGame,rateEntity,rateRaw");
                try
                {
                    for(int p=0;p<phases.Length;p++)
                    {
                        string phase=phases[p];
                        if(host)
                        {
                            if(modifier!=0){clock.RemoveModifier(modifier);modifier=0;}
                            clock.MinScale=0;
                            if(p==0)foreach(var id in Ids){var cd=abilities.GetCooldown(id); cd.TryConsume(); cd.TryConsume();}
                            if(phase=="entity-quarter")modifier=clock.AddOwnedModifier(EveId.Parse("test:t4_slow"),EveTimeModifierKind.Scale,-.75f,manager);
                            if(phase=="entity-stop"||phase=="raw-floor"){clock.MinScale=phase=="raw-floor"?1:0;modifier=clock.AddOwnedModifier(EveId.Parse("test:t4_stop"),EveTimeModifierKind.Scale,-1,manager);}
                            if(phase=="suspend")manager.enabled=false;
                            if(phase=="unsuspend")manager.enabled=true;
                            if(phase=="world-pause")world.SetPaused(true);
                            if(phase=="world-resume")world.SetPaused(false);
                            view.Resync(); Mark("ability-phase-"+p);
                        }
                        yield return Wait(()=>File.Exists(P("ability-phase-"+p)),"T4 phase "+phase);
                        Time.timeScale=scales[p];
                        if(owner && p==0) yield return Wait(()=>view.Prepared && abilities.Find(Ids[0]).Count==1 && abilities.GetCooldown(Ids[0]).CooldownRemaining>0,"T4 first coherent timing image");
                        if(!host&&phase=="interruption")network.NotifyApplicationPause(true);
                        if(!host&&phase=="recovered")network.NotifyApplicationPause(false);
                        double start=Time.realtimeSinceStartupAsDouble;
                        double duration=phase=="interruption"?1:phase=="recovered"?8:7;
                        while(Time.realtimeSinceStartupAsDouble-start<duration)
                        {
                            var sample=PNTimeService.For(EveRegistrySession.Context).Sample();
                            if(owner&&phase=="normal"&&!inputGuess&&view.Prepared&&abilities.GetCooldown(Ids[0]).CooldownRemaining>0)
                            {
                                // Real binding input remains immediate while timed authority rows reconcile.
                                var skill=subject.GetComponent<PNPlayerSkill>(); var remote=subject.GetComponent<PNSkillRemoteInput>();
                                if(!skill.PC.Controller.Dash.enabled)throw new Exception("T4 ready image did not arm Keyboard InputAction");
                                if(inputStep==0)
                                {
                                    inputCharges=skill.DashRecovery.Current;inputCount=remote.PredictedCount;
                                    inputPosition=subject.transform.position;inputAt=sample.Local;
                                    keyboard=InputSystem.AddDevice<Keyboard>();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.K));inputStep=1;
                                }
                                else
                                {
                                    if(!skill.IsPredictingDash||remote.PredictedCount!=inputCount+1||skill.DashRecovery.Current!=inputCharges-1)throw new Exception("T4 Keyboard Dash lost immediate prediction");
                                    InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                                    inputGuess=true; Mark("ability-immediate-input");
                                }
                            }
                            if(owner&&inputGuess&&!inputMoved&&sample.Local-inputAt>.25)
                            {
                                var delta=subject.transform.position-inputPosition;delta.y=0;
                                if(delta.sqrMagnitude<.0025f)throw new Exception("T4 Keyboard Dash did not move the actual owner body");
                                inputMoved=true;Mark("ability-keyboard-body-moved");
                            }
                            if(owner&&phase=="double"&&!staleRejected&&view.Prepared)
                            {
                                // Keep real row capture metadata; inject an old image into the same sink.
                                var old=(Newtonsoft.Json.Linq.JObject)view.Capture();old["t"]=sample.Host-3;
                                int requests=view.BaselineRequests; ulong before=view.AppliedRevision;
                                view.Apply(old,before+1);
                                if(view.Prepared||view.AppliedRevision!=before||view.BaselineRequests<=requests)throw new Exception("T4 stale image was extrapolated instead of rebaselining");
                                staleRejected=true; Mark("ability-stale-held");
                            }
                            if(owner&&staleRejected&&view.Prepared&&phase!="double"){recovered=true;Mark("ability-baseline-recovered");}
                            if(sample.Local>=nextRow)
                            {
                                nextRow=sample.Local+.05;
                                var captureRates=host?manager.ClockRates:view.CapturedRates;
                                double[] row={sample.Local,sample.Host,sample.Uncertainty,sample.Ready?1:0,view.Prepared?1:0,view.AppliedAge,view.AppliedRevision,
                                    abilities.GetCooldown(Ids[0]).CooldownRemaining,abilities.GetCooldown(Ids[1]).CooldownRemaining,abilities.GetCooldown(Ids[2]).CooldownRemaining,
                                    clock.Scale,clock.RawScale,subject.GetComponent<PNSkillRemoteInput>().PendingPredictions,view.BaselineRequests,captureRates.Game,captureRates.Entity,captureRates.Raw};
                                csv.WriteLine(phase+","+string.Join(",",row.Select(x=>x.ToString("R",CultureInfo.InvariantCulture))));
                            }
                            yield return null;
                        }
                        csv.Flush(); Mark(peer+"-ability-phase-"+p);
                        if(host)yield return Wait(()=>File.Exists(P("client1-ability-phase-"+p))&&File.Exists(P("client2-ability-phase-"+p)),"T4 peer phase "+phase);
                    }
                    if(owner&&(!inputGuess||!inputMoved||!staleRejected||!recovered||!view.Prepared))throw new Exception("T4 input/body/baseline/recovery acceptance missing");
                    if(!host&&!owner&&(view.AppliedCount!=0||view.AppliedRevision!=0))throw new Exception("T4 owner image leaked to observer");
                    Mark(peer+"-ability-complete");
                    yield return Wait(()=>File.Exists(P("host-ability-complete"))&&File.Exists(P("client1-ability-complete"))&&File.Exists(P("client2-ability-complete")),"T4 completion");
                }
                finally
                {
                    Time.timeScale=global; clock.MinScale=minimum;
                    if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
                    if(modifier!=0)clock.RemoveModifier(modifier);
                    if(host){manager.enabled=true;if(world.State==EveWorldState.Paused)world.SetPaused(false);}
                }
            }
        }
    }
}
