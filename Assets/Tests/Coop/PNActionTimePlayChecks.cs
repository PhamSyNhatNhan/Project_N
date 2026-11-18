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
    // PreT5/T5 diagnostic fixture. A2 reconstructs refused-run history without
    // changing admission, gameplay or the production observer/wire protocol.
    public static class PNActionTimePlayChecks
    {
        public static bool Enabled => Environment.GetCommandLineArgs().Contains("--pn-action-time");
        static bool PresentationAcceptance => Environment.GetCommandLineArgs().Contains("--pn-action-presentation-acceptance");
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static IEnumerator Wait(Func<bool> ready,string label)
        { double until=Time.realtimeSinceStartupAsDouble+45;while(!ready()){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException(label);yield return null;} }
        public static IEnumerator Run(string folder,string peer,PNPlayerMove subject)
        {
            using var a2 = new A2Recorder(Path.Combine(folder, peer + "-a2.jsonl"));
            using var measurements = new StreamWriter(Path.Combine(folder, peer + "-all-runs.jsonl")) { AutoFlush = true };
            var storeIds = new Dictionary<PNActionRunStore, int>();
            void Record(PNActionRunStore store, Newtonsoft.Json.Linq.JObject item)
            {
                if (a2.Replaying) return;
                if (!storeIds.TryGetValue(store, out var id)) storeIds.Add(store, id = storeIds.Count + 1);
                item["store"] = id; item["frame"] = Time.frameCount;
                measurements.WriteLine(item.ToString(Formatting.None));
            }
            PNActionRunStore.Measurement += Record;
            Exception failure=null;var stack=new Stack<IEnumerator>();
            stack.Push(PresentationAcceptance?MeasurePresentation(folder,peer,subject):Measure(folder,peer,subject));
            try { while(stack.Count>0){var e=stack.Peek();bool next=false;try{next=e.MoveNext();}catch(Exception x){failure=x;}
                if(failure!=null)break;if(!next){(stack.Pop() as IDisposable)?.Dispose();continue;}
                if(e.Current is IEnumerator child){stack.Push(child);continue;}yield return e.Current;} }
            finally {PNActionRunStore.Measurement -= Record; while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();}
            if (a2.Errors != 0) failure = new Exception("A2 recorder/oracle failures: " + a2.Errors);
            string label=PresentationAcceptance?"T5.3-C presentation fixture":"PreT5 measurement fixture";
            File.WriteAllText(Path.Combine(folder,peer+".txt"),failure==null?"PASS "+label:"FAIL "+label+" "+failure);
            if(failure!=null)Debug.LogException(failure);Application.Quit(failure==null?0:1);
        }
        private static IEnumerator MeasurePresentation(string folder,string peer,PNPlayerMove subject)
        {
            string PathFor(string name)=>Path.Combine(folder,name);
            bool Has(string name)=>File.Exists(PathFor(name));
            void Mark(string name)=>File.WriteAllText(PathFor(name),"ok");
            var manager=(PNNetworkManager)NetworkManager.singleton;
            var skill=subject.GetComponent<EveCharacterSkill>();
            var stat=subject.GetComponent<PNStat>();
            bool host=peer=="host",observer=peer=="client2";
            long secondOwner=long.Parse(File.ReadAllText(PathFor("client2.owner")));
            var secondSkill=EveRegistrySession.Context.Entities.Snapshot().Single(x=>
                x.LoadContext?.Identity.OwnerId==secondOwner&&x.GetComponent<EveCharacterSkill>()!=null).GetComponent<EveCharacterSkill>();
            var entity=subject.GetComponent<EveEntityIdentity>().Id.Value;
            var spawned=host?NetworkServer.spawned:NetworkClient.spawned;
            var shell=spawned.Values.Select(x=>x.GetComponent<PNActorShell>()).Single(x=>x!=null&&x.Metadata.Entity==entity);
            var bindings=(IDictionary)typeof(PNNetworkManager).GetField("bindings",Flags).GetValue(manager);
            object binding=bindings[shell.netId];
            object actions=binding.GetType().GetField("Actions",Flags).GetValue(binding);
            var receiverField=actions.GetType().GetField("Receiver",Flags);
            PNActionReceiver Receiver()=>(PNActionReceiver)receiverField.GetValue(actions);
            var request=typeof(PNNetworkManager).GetMethod("RequestAction",Flags);
            var views=(IDictionary)typeof(PNSkill).GetField("presentedActions",Flags).GetValue(skill);
            uint netId=shell.netId;
            var expected=new List<PNObservedAction>();
            var modifier=EveId.Parse("test:t53_sweep_speed");
            var scaleModifier=EveId.Parse("test:o1_sweep_scale");
            const double expectedScale=2;
            var factors = new[] { (PNContentIds.MeleeScale, expectedScale), (PNContentIds.ProjectileScale, 1.25),
                (PNContentIds.ProjectileSpeed, 1.5), (PNContentIds.ProjectileLifetime, 1.75),
                (PNContentIds.AreaScale, 2.25), (PNContentIds.BlastScale, 2.5), (PNContentIds.SummonScale, 2.75) };
            bool HasInitialScales()
            {
                var view = subject.GetComponent<PNSkillScaleState>();
                return view != null && factors.All(factor => view.ReadScale(factor.Item1) == factor.Item2);
            }
            bool HasUpdatedScale() => subject.GetComponent<PNSkillScaleState>()?.ReadScale(PNContentIds.MeleeScale) == 1.5;
            var sweepData=skill.GetData(EveId.Parse("project_n:eve_attack_right")).Action.Perform;
            float expectedRadius=EveSweepSettings.Parse(sweepData).Radius*(float)expectedScale;
            bool Scaled(EveSweepVisual visual)=>visual!=null&&Math.Abs(visual.Radius-expectedRadius)<.0001;

            double baseSpeed=host?stat.Evaluate(PNContentIds.AttackSpeed):0;
            void Speed(double value)=>stat.SetSource(modifier,new[]{new EveStatModifier(PNContentIds.AttackSpeed,value-baseSpeed,0)});
            using var frames=new StreamWriter(PathFor(peer+"-presentation.jsonl")){AutoFlush=true};
            using var packets=new StreamWriter(PathFor(peer+"-action-packets.jsonl")){AutoFlush=true};
            PNActionRunKey key=default;
            EveSweepVisual visible=null,first=null;
            double progress=0,firstProgress=0;
            bool hasComparison=false;
            string phase="steady";
            Exception problem=null;
            void Packet(NetworkDiagnostics.MessageInfo info)
            {
                if(info.message is PNActionPacket packet&&packet.Stream.NetId==netId&&packet.Kind!=1)
                    packets.WriteLine(JsonConvert.SerializeObject(new {frame=Time.frameCount,packet.Kind,packet.T,packet.N,
                        packet.Request,packet.Stream.Subscription,packet.Stream.Generation}));
            }
            void Observe()
            {
                try
                {
                    var viewRows=new List<object>();
                    PNActionPresentationCoverage coverage=manager.ReadObservedActions(netId,expected,out var stamp);
                    visible=null;
                    var seenKeys=new HashSet<PNActionRunKey>();
                    foreach(DictionaryEntry pair in views)
                    {
                        var value=(EveSweepVisual)pair.Value.GetType().GetField("View",Flags).GetValue(pair.Value);
                        if(value==null||!value.gameObject.activeInHierarchy)continue;
                        var drawnKey=(PNActionRunKey)pair.Key;
                        if(!observer||!seenKeys.Add(drawnKey)||coverage!=PNActionPresentationCoverage.Active)
                            throw new Exception("Unexpected or duplicated observed view "+drawnKey.Cast);
                        var row=expected.Single(x=>x.Key.Equals(drawnKey));
                        var settings=EveSweepSettings.Parse(skill.GetData(EveId.Parse(row.DataId)).Action.Perform);
                        if(row.PresentationScale!=expectedScale||!Scaled(value))
                            throw new Exception("Observer sweep radius differs from captured scale");
                        double normalized=Math.Min(1,row.Progress/settings.Duration);
                        if(Math.Abs(value.Progress-normalized)>.0001||skill.ObservedActionCount!=0||skill.CurrentInvocation!=null)
                            throw new Exception("Rendered sweep disagrees with committed pose-time model");
                        key=drawnKey;visible=value;progress=value.Progress;hasComparison=true;
                        viewRows.Add(new {drawnKey.Cast,drawnKey.Ordinal,progress=value.Progress,absolute=row.Progress,
                            row.Rate,row.PresentationScale,radius=value.Radius,objectId=value.GetEntityId().GetHashCode(),bladeSamples=value.BladeSamples});
                    }
                    var authority=host?subject.GetComponentInChildren<EveSweepVisual>():null;
                    var secondary=secondSkill==null?null:secondSkill.GetComponentInChildren<EveSweepVisual>();
                    frames.WriteLine(JsonConvert.SerializeObject(new {frame=Time.frameCount,phase,role=subject.NetworkRole.ToString(),
                        stamp,coverage=coverage.ToString(),subscription=Receiver()?.Stream.Subscription,
                        receiveState=Receiver()?.State.ToString(),active=expected.Count,views=viewRows,
                        authority=authority?.Progress,secondary=secondary?.Progress,
                        attackSpeed=host?stat.Evaluate(PNContentIds.AttackSpeed):0}));
                }
                catch(Exception error){problem??=error;}
            }
            IEnumerator Until(Func<bool> ready,string description)
            {
                yield return Wait(()=>problem!=null||ready(),description);
                if(problem!=null)throw problem;
            }
            void Attack(EveCharacterSkill actor)
            {
                if(!actor.RequestSlot(PNSkillSlots.Attack,new PNSkillInputEvent(PNSkillInputPhase.Started,PNSkillInteraction.Tap))||
                    !actor.RequestSlot(PNSkillSlots.Attack,new PNSkillInputEvent(PNSkillInputPhase.Performed,PNSkillInteraction.Tap)))
                    throw new Exception("owner sweep input declined");
                actor.RequestSlot(PNSkillSlots.Attack,new PNSkillInputEvent(PNSkillInputPhase.Canceled,PNSkillInteraction.Tap));
            }
            NetworkLoop.OnLateUpdate+=Observe;
            if(!host)NetworkDiagnostics.InMessageEvent+=Packet;
            IPNCameraHandle camera=null;
            try
            {
                if(observer)camera=PNCameraServices.Resolve(EveRegistrySession.Context).RequestCinematic(subject,"t53-acceptance",
                    PNCinematicShot.Follow(subject.transform,new Vector3(0,2.8f,-2.8f),new Vector3(45,0,0),
                    fieldOfView:38,blendIn:0,blendOut:0,priority:100,aimTarget:subject.transform,aimOffset:new Vector3(0,.5f,0)));
                Mark(peer+"-presentation-ready");
                yield return Until(()=>Has("host-presentation-ready")&&Has("client1-presentation-ready")&&Has("client2-presentation-ready"),"presentation peers ready");
                yield return new WaitForSecondsRealtime(.5f);
                phase="live-sweep";
                if(host)
                {
                    Speed(30);
                    stat.SetSource(scaleModifier, factors.Select(factor => new EveStatModifier(factor.Item1, factor.Item2 - 1, 1)));
                    Mark("sweep-ready");
                    yield return Until(()=>subject.GetComponentsInChildren<EveSweepVisual>().Any(x=>Scaled(x)&&x.Progress>=.35f&&x.Progress<.65f),"live authority sweep reaches hold point");
                    Speed(0);
                    stat.SetSource(scaleModifier, new[] { new EveStatModifier(PNContentIds.MeleeScale, .5, 1) });
                    Mark("sweep-held");
                    yield return Until(()=>Has("observer-recovered"),"observer recovers held live sweep");
                    var connection=NetworkServer.connections.Values.Single(c=>c.authenticationData is PNPeer p&&p.Owner==secondOwner);
                    var removeObserver=typeof(InterestManagementBase).GetMethod("RemoveObserver",Flags);
                    removeObserver.Invoke(NetworkServer.aoi,new object[]{connection,shell.netIdentity});
                    Mark("observer-hidden");
                    yield return Until(()=>Has("observer-retired"),"observer retires old binding");
                    NetworkServer.RebuildObservers(shell.netIdentity,false);
                    yield return Until(()=>Has("observer-rebound"),"new observer binding seeks held sweep");
                    Speed(120);
                    yield return Until(()=>!skill.IsBusy,"authority resumed and completed sweep");
                    stat.RemoveSource(modifier);
                    Mark("sweep-finished");
                }
                else if(!observer)
                {
                    yield return Until(()=>Has("sweep-ready")&&Has("observer-scale-ready")&&!skill.IsBusy&&HasInitialScales(),"owner ready");
                    Attack(skill);
                    yield return Until(()=>subject.GetComponentsInChildren<EveSweepVisual>().Any(Scaled),"owner predicts scaled sweep");
                    yield return Until(()=>Has("overlap-input")&&secondSkill.ObservedPresentationsStarted>0&&
                        secondSkill.GetComponentInChildren<EveSweepVisual>()!=null,"client1 observes client2 sweep during primary hold");
                    Mark("overlap-observed");
                }
                else
                {
                    yield return Until(()=>Has("sweep-ready")&&HasInitialScales(),"observer receives the seven entity factors");
                    Mark("observer-scale-ready");
                    yield return Until(()=>Has("sweep-held")&&visible!=null&&progress>=.2&&HasUpdatedScale(),"observer renders the frozen sweep with a newer entity scale");
                    first=visible;firstProgress=progress;
                    var originalKey=key;
                    Attack(secondSkill);
                    Mark("overlap-input");
                    yield return Until(()=>Has("overlap-observed")&&visible==first,"both clients render overlapping authority sweeps");
                    var capture=typeof(PNTimeBaselineChecks).GetMethod("CaptureClockFrame",BindingFlags.Static|BindingFlags.NonPublic);
                    capture.Invoke(null,new object[]{PathFor("observer-before-recovery.png")});
                    phase="recovery";
                    long subscription=Receiver().Stream.Subscription;
                    request.Invoke(manager,new[]{binding,(object)true});
                    yield return Until(()=>Receiver()?.Stream.Subscription>subscription&&Receiver().State==PNActionReceiveState.Prepared&&
                        visible!=null&&key.Equals(originalKey),"observer renders same sweep after baseline");
                    if(visible!=first||progress+.0001<firstProgress)throw new Exception("Recovery restarted or rewound sweep");
                    capture.Invoke(null,new object[]{PathFor("observer-after-recovery.png")});
                    phase="move-role-transition";
                    subject.DetachNetwork();
                    if(subject.NetworkRole!=PNMoveNetworkRole.Local||
                        manager.ReadObservedActions(netId,expected,out _)!=PNActionPresentationCoverage.Unknown)
                        throw new Exception("Local Move role still exposes observed presentation");
                    yield return Until(()=>subject.NetworkRole==PNMoveNetworkRole.Observer&&visible!=null&&key.Equals(originalKey),
                        "observer Move role and presentation recover");
                    Mark("observer-recovered");
                    phase="rebind";
                    yield return Until(()=>Has("observer-hidden")&&!bindings.Contains(netId),"old observer binding retired by Mirror");
                    if(views.Count!=0||first!=null&&first.gameObject.activeInHierarchy)
                        throw new Exception("Retired observer binding still owns a sweep view");
                    Mark("observer-retired");
                    yield return Until(()=>bindings.Contains(netId)&&EveRegistrySession.Context.Entities.Snapshot().Any(x=>
                        x.LoadContext?.Identity.Id.Value==entity&&x.GetComponent<EveCharacterSkill>()?.IsReady==true),"new observer binding ready");
                    binding=bindings[netId];
                    actions=binding.GetType().GetField("Actions",Flags).GetValue(binding);
                    subject=EveRegistrySession.Context.Entities.Snapshot().Single(x=>x.LoadContext?.Identity.Id.Value==entity).GetComponent<PNPlayerMove>();
                    skill=subject.GetComponent<EveCharacterSkill>();
                    views=(IDictionary)typeof(PNSkill).GetField("presentedActions",Flags).GetValue(skill);
                    yield return Until(()=>visible!=null&&key.Equals(originalKey)&&HasUpdatedScale(),"fresh observer receives current entity scale and seeks the frozen run");
                    if(visible==first||progress+.0001<firstProgress)throw new Exception("Rebind reused stale view or replayed startup");
                    capture.Invoke(null,new object[]{PathFor("observer-rebound.png")});
                    Mark("observer-rebound");
                }
                yield return Until(()=>Has("sweep-finished"),"sweep completion");
                if(observer)
                {
                    yield return Until(()=>visible==null&&views.Count==0,"observer retires completed sweep");
                    if(!hasComparison)throw new Exception("No model/render join");
                }
                yield return new WaitForSecondsRealtime(.35f);
                var stats=Enum.GetValues(typeof(PNActionPresentationPhase)).Cast<PNActionPresentationPhase>().Select(p=>{
                    var s=manager.ActionPresentationStats(p);
                    return new {phase=p.ToString(),s.Samples,s.Unknown,s.MissingPose,s.LongestUnknownFrames,s.LongestUnknownMilliseconds,s.UnknownRatio};
                }).ToArray();
                File.WriteAllText(PathFor(peer+"-presentation-stats.json"),JsonConvert.SerializeObject(stats,Formatting.Indented));
                Mark(peer+"-presentation-done");
                yield return Until(()=>Has("host-presentation-done")&&Has("client1-presentation-done")&&Has("client2-presentation-done"),"finish barrier");
            }
            finally
            {
                NetworkLoop.OnLateUpdate-=Observe;
                NetworkDiagnostics.InMessageEvent-=Packet;
                if(host&&stat!=null&&stat.IsReady){stat.RemoveSource(modifier);stat.RemoveSource(scaleModifier);}
                camera?.Dispose();
            }
        }
        // Diagnostic-only replay: one isolated budget per run, so production admission
        // cannot censor holds/history. Calls only producer math, never gameplay effects.
        public sealed class A2Recorder : IDisposable
        {
            private readonly StreamWriter writer;
            private readonly Dictionary<PNActionRunStore, int> sources = new Dictionary<PNActionRunStore, int>();
            private readonly Dictionary<string, PNActionRunStore> shadows = new Dictionary<string, PNActionRunStore>();
            private readonly HashSet<string> active = new HashSet<string>();
            private readonly Dictionary<string, double> captures = new Dictionary<string, double>();
            private bool replaying;
            public bool Replaying => replaying;
            private int events;
            private long written;
            private readonly long byteLimit;
            private void Write(Newtonsoft.Json.Linq.JObject e)
            {
                string text = e.ToString(Formatting.None);
                written += System.Text.Encoding.UTF8.GetByteCount(text) + 1;
                if (written > byteLimit) throw new Exception("A2 diagnostic file cap");
                writer.WriteLine(text);
            }
            private readonly bool omitFirstDriver;
            private bool omitted;
            private int replaySource;
            public int Errors { get; private set; }
            public double ProgressFor(PNActionRunStore source, PNActionRunKey key, double stamp)
                => shadows[sources[source] + ":" + key.Cast + ":" + key.Ordinal].Snapshot(stamp).Single().Progress;
            public A2Recorder(string path, bool omitFirstDriver = false, long byteLimit = 512L * 1024 * 1024) { this.byteLimit = byteLimit; this.omitFirstDriver = omitFirstDriver; writer = new StreamWriter(path) { AutoFlush = true }; PNActionRunStore.Measurement += Record; }
            private void Record(PNActionRunStore source, Newtonsoft.Json.Linq.JObject e)
            {
                if (replaying)
                {
                    string nestedKind = (string)e["kind"];
                    if (nestedKind == "sample" || nestedKind == "before-cut")
                    {
                        try { if (++events > 2000000) throw new Exception("A2 event cap"); e["kind"] = "shadow-" + nestedKind; e["source"] = replaySource; Write(e); }
                        catch { Errors++; PNActionRunStore.Measurement -= Record; }
                    }
                    return;
                }
                replaying = true;
                try
                {
                    if (++events > 2000000) throw new Exception("A2 event cap");
                    if (!sources.TryGetValue(source, out var id)) sources.Add(source, id = sources.Count + 1);
                    replaySource = id;
                    string kind = (string)e["kind"]; double stamp = (double)e["stamp"];
                    if (kind == "driver" && omitFirstDriver && !omitted) { omitted = true; return; }
                    long cast = (long)e["cast"], ordinal = (long)e["ordinal"];
                    string prefix = id + ":", name = prefix + cast + ":" + ordinal;
                    var key = cast == 0 ? default : new PNActionRunKey(cast, ordinal);
                    if (kind == "attempt")
                    {
                        if (shadows.Count >= 8192) throw new Exception("A2 shadow cap");
                        var row = (Newtonsoft.Json.Linq.JArray)e["candidate"];
                        var shadow = new PNActionRunStore(new PNActionRunBudget());
                        if (!shadow.Admit(key, (string)row[2], (string)row[3], (string)row[4], (double)row[6], (double)row[7], stamp)) throw new Exception("A2 isolated admission");
                        shadows.Add(name, shadow);
                        active.Add(name);
                    }
                    if (kind == "seal")
                        foreach (var nameOfRun in active.Where(p => p.StartsWith(prefix, StringComparison.Ordinal))) shadows[nameOfRun].CaptureHolds(stamp);
                    if (shadows.TryGetValue(name, out var run))
                    {
                        if (kind == "driver")
                        {
                            if (e["begin"] != null) run.AdvanceInterval(key, (double)e["begin"], stamp, (double)e["value"], (double)e["rate"]);
                            else run.Advance(key, stamp, (double)e["value"], (double)e["rate"]);
                            if (!captures.TryGetValue(name, out var previous) || stamp - previous >= .099999)
                            { e["reconstructed"] = PNActionRunWire.Row(run.Snapshot(stamp).Single()); captures[name] = stamp; }
                        }
                        if (kind == "finish-attempt")
                        {
                            run.Finish(key, stamp, (PNActionRunEnd)(int)e["value"]);
                            var record = run.Snapshot(stamp).Single();
                            e["reconstructed"] = PNActionRunWire.Row(record);
                            active.Remove(name);
                        }
                        if (kind == "terminal")
                        {
                            var actual = e["candidate"];
                            var expected = PNActionRunWire.Row(run.Snapshot(stamp).Single());
                            if (!Newtonsoft.Json.Linq.JToken.DeepEquals(actual, expected)) throw new Exception("A2 admitted terminal history mismatch " + name);
                        }
                    }
                    e["source"] = id; Write(e);
                }
                catch (Exception ex) { Errors++; PNActionRunStore.Measurement -= Record; writer.WriteLine(JsonConvert.SerializeObject(new { error = ex.ToString() })); }
                finally { replaying = false; }
            }
            public void Dispose() { PNActionRunStore.Measurement -= Record; foreach (var s in shadows.Values) s.Dispose(); writer.Dispose(); }
        }
        private static IEnumerator Measure(string folder,string peer,PNPlayerMove subject)
        {
            bool host=NetworkServer.active,owner=peer=="client1";
            string P(string s)=>Path.Combine(folder,s);
            void Mark(string s)=>File.WriteAllText(P(s),"ok");
            var skill=subject.GetComponent<EveCharacterSkill>();
            using var c3=host&&PNActionC3Recorder.Enabled?new PNActionC3Recorder(skill,folder,peer):null;
            var cast=subject.GetComponent<PNSkillCastView>();
            var remote=subject.GetComponent<PNSkillRemoteInput>();var session=EveRegistrySession.Context;
            if(skill==null||cast==null)throw new Exception("PreT5 needs Eve sweep character");
            var clock=PNTimeService.For(session);IPNCameraHandle camera=null;
            var stat=subject.GetComponent<PNStat>();var speedSource=EveId.Parse("test:pret5_speed");int captures=0;int lastPlayed=skill.ObservedActionsPlayed+skill.ObservedPresentationsStarted;int currentPulse=-1;
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
                probe.Tick=()=>{if(!session.IsActive)return;var actionManager=NetworkManager.singleton as PNNetworkManager;var s=clock.Sample();c3?.Tick(s.Local);var at=subject.transform.position;
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
                        actionSent=actionManager?.ActionPacketsSent??0,actionPeers=actionManager?.ActionPeerCount??0,actionPrepared=actionManager?.ActionPreparedBindings??0,actionSequence=actionManager?.ActionAppliedSequence??0,actionQueue=actionManager?.ActionQueuedBytes??0,actionReceive=actionManager?.ActionReceiveBytes??0,actionRecoveries=actionManager?.ActionRecoveries??0,actionVisibleSkipped=actionManager?.ActionKnownVisibleSkipped??0,actionUnknownResets=actionManager?.ActionUnknownCoverageResets??0,
                        x=at.x,z=at.z,role=subject.NetworkRole.ToString(),observed=skill.ObservedActionCount,played=cast.PlayedCount,scheduled=skill.ObservedPresentationsStarted,serial=cast.LastSerial,busy=skill.IsBusy,
                        pending=remote.PendingPredictions,ack=remote.AckedKey,castKey=skill.CurrentInvocation?.CastKey??0,
                        attackSpeed=host?stat.Evaluate(PNContentIds.AttackSpeed):0,globalRate=Time.timeScale,entityRate=entityClock!=null?entityClock.Scale:1}));
                    int drawn=skill.ObservedActionsPlayed+skill.ObservedPresentationsStarted;
                    if(peer=="client2"&&drawn!=lastPlayed&&skill.GetComponentInChildren<EveSweepVisual>()!=null&&captures<6)
                    {lastPlayed=drawn;typeof(PNTimeBaselineChecks).GetMethod("CaptureClockFrame",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{P("observed-"+(captures++)+".png")});}
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
