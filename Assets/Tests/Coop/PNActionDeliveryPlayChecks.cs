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
using Unity.Profiling;
using UnityEngine;
namespace ProjectN.Coop.Tests
{
    // Registered by the disposable verification code mod, never by a character.
    public sealed class PNActionDeliveryProbe : PNSkillAction
    {
        public double Progress;
        public override void Start(PNSkillActionContext c) => c.BeginAction();
        public override void Perform(PNSkillActionContext c) { }
        public void Advance(float dt) => Progress += dt;
        public override void Exit(PNSkillActionContext c) => c.CompleteAction();
        public override void Cancel(PNSkillActionContext c) => c.CancelEffects();
    }
    public static class PNActionDeliveryPlayChecks
    {
        public static bool Enabled => Environment.GetCommandLineArgs().Contains("--pn-action-delivery");
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        sealed class Live
        {public PNSkill Skill;public PNActionDeliveryProbe Action;public PNSkillActionContext Context;public double End;}
        struct FrameSample
        {
            public int KcpSendQueue,KcpSendBuffer,KcpReceiveQueue,KcpReceiveBuffer;
            public double CloneMs,PruneMs,ChargeMs,DecodeMs,SenderMs;
            public long CallbackTicks,CallbackCount,InclusiveTicks,ExclusiveTicks;
            public string Phase;public int Frame,G0,G1,Queue,Receive,RawPending;public double Now,Delta,Driver,SourceMs;public long Alloc,Heap,Recoveries,Skipped,Resets,RawSent,RawDrops;public bool Ready;
            public JObject Json(double cpu)=>new JObject{["callbackTicksCumulative"]=CallbackTicks,["callbackCountCumulative"]=CallbackCount,["coreInclusiveTicksCumulative"]=InclusiveTicks,["coreExclusiveTicksCumulative"]=ExclusiveTicks,["tickFrequency"]=System.Diagnostics.Stopwatch.Frequency,["phase"]=Phase,["frame"]=Frame,["now"]=Now,["dt"]=Delta,["driverMs"]=Driver,["sourceCpuMsPreviousFrame"]=SourceMs,["kcpSendQueue"]=KcpSendQueue,["kcpSendBuffer"]=KcpSendBuffer,["kcpReceiveQueue"]=KcpReceiveQueue,["kcpReceiveBuffer"]=KcpReceiveBuffer,["senderMsPreviousFrame"]=SenderMs,["cloneMsPreviousFrame"]=CloneMs,["pruneMsPreviousFrame"]=PruneMs,["chargeMsPreviousFrame"]=ChargeMs,["decodeMsPreviousFrame"]=DecodeMs,["cpuMs"]=cpu,["alloc"]=Alloc,["heap"]=Heap,["gc0"]=G0,["gc1"]=G1,["queue"]=Queue,["receive"]=Receive,["recoveries"]=Recoveries,["skipped"]=Skipped,["resets"]=Resets,["inputReady"]=Ready,["rawSent"]=RawSent,["rawDrops"]=RawDrops,["rawPending"]=RawPending};
        }
        struct StageSample
        {
            public string Kind,Phase;public int Peer,Frame,Bytes,Tag;public long Tick,N,Request,Subscription,Entity,AppliedN;public Guid Source;public double Now,Value,Capture;
            public JObject Json()=>new JObject{["kind"]=Kind,["phase"]=Phase,["peer"]=Peer,["frame"]=Frame,["tick"]=Tick,["frequency"]=System.Diagnostics.Stopwatch.Frequency,["now"]=Now,["value"]=Value,["bytes"]=Bytes,["tag"]=Tag,["n"]=N,["request"]=Request,["subscription"]=Subscription,["entity"]=Entity,["source"]=Source.ToString("N"),["appliedN"]=AppliedN,["capture"]=Capture};
        }
        struct ApplySample
        {
            public string Phase;public int Frame;public long Entity,Subscription,Request,Required;public Guid Source,Session,World;public ulong Incarnation,Nonce;public long Generation,AppliedN;
            public bool ClockReady,ConfirmedOutcome;public int CommittedRequired,CommittedLatest,Ignored;public double Capture,HostEstimate,Uncertainty,LocalApplied,HandlerMs;
            public JObject Json()=>new JObject{["phase"]=Phase,["frame"]=Frame,["entity"]=Entity,["source"]=Source.ToString("N"),["session"]=Session.ToString("N"),["world"]=World.ToString("N"),["incarnation"]=Incarnation,["nonce"]=Nonce,["generation"]=Generation,["appliedN"]=AppliedN,["subscription"]=Subscription,["request"]=Request,["required"]=Required,["clockReady"]=ClockReady,["capture"]=Capture,["hostEstimate"]=HostEstimate,["uncertainty"]=Uncertainty,["localApplied"]=LocalApplied,["handlerMs"]=HandlerMs,["confirmedOutcome"]=ConfirmedOutcome,["committedRequired"]=CommittedRequired,["committedLatest"]=CommittedLatest,["ignored"]=Ignored};
        }
        static JObject Raw(ProjectN.Composition.PNKcpFaultTransport t)=>t==null?new JObject{["measured"]=false}:new JObject{["measured"]=true,["latencyMs"]=t.DatagramLatencyMs,["lossPercent"]=t.DatagramLossPercent,["seed"]=t.Seed,["attempts"]=t.AttemptedDatagrams,["attemptedBytes"]=t.AttemptedBytes,["drops"]=t.DroppedDatagrams,["droppedBytes"]=t.DroppedBytes,["sent"]=t.SentDatagrams,["sentBytes"]=t.SentBytes,["pendingBytes"]=t.PendingBytes,["peakPendingBytes"]=t.PeakPendingBytes,["tier"]="UDP payload after KCP headers, data and ACK; IP/UDP headers unmeasured; teardown controls bypass fault delay"};
        static string ReadPublished(string path){try{using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(f))return reader.ReadToEnd();}catch(IOException){return null;}}
        static double Now => NetworkTime.localTime;
        static void Publish(string path,string value){File.WriteAllText(path+".tmp",value);File.Move(path+".tmp",path);}
        static void Need(bool ok,string why){if(!ok)throw new Exception("D: "+why);}
        static IEnumerator Wait(Func<bool> ready,string why,double seconds=15)
        {double until=Now+seconds;while(!ready()){Need(Now<until,"timeout "+why);yield return null;}}
        public static IEnumerator Run(string folder,string peer,PNPlayerMove subject)
        {
            var raw=UnityEngine.Object.FindAnyObjectByType<ProjectN.Composition.PNKcpFaultTransport>();var rawStart=Raw(raw);
            Exception failure=null;var stack=new Stack<IEnumerator>();stack.Push(PNActionBoundaryPlayChecks.Enabled?PNActionBoundaryPlayChecks.Run(folder,peer):Measure(folder,peer));
            try{while(stack.Count>0){var e=stack.Peek();bool next=false;try{next=e.MoveNext();}catch(Exception x){failure=x;}
                if(failure!=null)break;if(!next){(stack.Pop() as IDisposable)?.Dispose();continue;}
                if(e.Current is IEnumerator child){stack.Push(child);continue;}yield return e.Current;}}
            finally{while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();PNActionDiagnostics.BlockSend=null;}
            File.WriteAllText(Path.Combine(folder,peer+"-datagrams.json"),new JObject{["start"]=rawStart,["end"]=Raw(raw)}.ToString());
            File.WriteAllText(Path.Combine(folder,peer+".txt"),failure==null?"PASS T5.2-D Player assertions":"FAIL T5.2-D "+failure);
            if(failure!=null)Debug.LogException(failure);Application.Quit(failure==null?0:1);
        }
        static JObject Snapshot(PNActionReceiver receiver)
        {
            var model=typeof(PNActionReceiver).GetField("model",F).GetValue(receiver);
            var runs=(System.Collections.IDictionary)model.GetType().GetField("Runs",F).GetValue(model);var rows=new JObject();
            foreach(System.Collections.DictionaryEntry item in runs){var r=item.Value;var t=r.GetType();rows[(string)item.Key]=new JObject{
                ["p"]=(double)t.GetField("P",F).GetValue(r),["ended"]=(bool)t.GetField("Ended",F).GetValue(r),
                ["d"]=(JArray)((JArray)t.GetField("D",F).GetValue(r)).DeepClone()};}
            return rows;
        }
        static IEnumerator Measure(string folder,string peer)
        {
            bool host=NetworkServer.active;var manager=(PNNetworkManager)NetworkManager.singleton;
            var raw=UnityEngine.Object.FindAnyObjectByType<ProjectN.Composition.PNKcpFaultTransport>();
            bool quiet=Environment.GetCommandLineArgs().Contains("--pn-action-delivery-quiet");
            bool quietFaults=Environment.GetCommandLineArgs().Contains("--pn-action-delivery-quiet-faults");
            const string loadPrefix="--pn-action-delivery-load=";
            var loadArgs=Environment.GetCommandLineArgs().Where(arg=>arg.StartsWith(loadPrefix,StringComparison.Ordinal)).ToArray();
            bool fixedWorkload=loadArgs.Length!=0;
            int load=64;
            if(fixedWorkload)
            {
                Need(quiet&&!quietFaults&&loadArgs.Length==1,"explicit workload requires quiet normal delivery");
                Need(int.TryParse(loadArgs[0].Substring(loadPrefix.Length),out load)&&(load==32||load==64),"workload must be 32 or 64");
            }
            bool stageEnabled=!Environment.GetCommandLineArgs().Contains("--pn-action-delivery-no-stages");
            bool testStageOverflow=host&&Environment.GetCommandLineArgs().Contains("--pn-action-delivery-test-stage-overflow");
            Need(!testStageOverflow||(quiet&&quietFaults&&stageEnabled),"stage overflow test requires quiet faults and stage recording");
            int stageCapacity=testStageOverflow?256:host&&quietFaults?524288:262144;
            var stageSamples=new List<StageSample>(stageCapacity);
            long droppedStageSamples=0;
            bool control=Environment.GetCommandLineArgs().Contains("--pn-action-delivery-control");
            bool withoutService=Environment.GetCommandLineArgs().Contains("--pn-action-delivery-without-service");
            Need(!control||quiet,"control requires quiet performance matrix");
            Need(!withoutService||control,"service-off is a matched performance control only");
            var applySamples=new List<ApplySample>(32768);var frameSamples=new List<FrameSample>(32768);var cpuSamples=new double[32768];var cpuFrames=new int[32768];
            var receivers=new Dictionary<long,PNActionReceiver>();var live=new List<Live>();var invocations=new List<PNSkillInvocation>();
            string phase="warmup";long starts=0,ends=0;int peak=0;double blockedUntil=0,driverMs=0;int blockedPeer=-1;
            using var applied=new StreamWriter(Path.Combine(folder,peer+"-applied.jsonl"));
            using var events=new StreamWriter(Path.Combine(folder,peer+"-delivery.jsonl"));
            using var frames=new StreamWriter(Path.Combine(folder,peer+"-frames.jsonl"));
            using var network=new StreamWriter(Path.Combine(folder,peer+"-network.jsonl"));
            using var alloc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);
            using var sourceCpu=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"PN.ActionPublication",1,ProfilerRecorderOptions.Default|ProfilerRecorderOptions.SumAllSamplesInFrame);
            using var senderCpu=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"PN.Action.SenderCapture",1,ProfilerRecorderOptions.Default|ProfilerRecorderOptions.SumAllSamplesInFrame);
            using var cloneCpu=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"PN.Action.DecodeClone",1,ProfilerRecorderOptions.Default|ProfilerRecorderOptions.SumAllSamplesInFrame);
            using var pruneCpu=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"PN.Action.DecodePrune",1,ProfilerRecorderOptions.Default|ProfilerRecorderOptions.SumAllSamplesInFrame);
            using var chargeCpu=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"PN.Action.DecodeCharge",1,ProfilerRecorderOptions.Default|ProfilerRecorderOptions.SumAllSamplesInFrame);
            using var decodeCpu=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"PN.Action.DecodeTree",1,ProfilerRecorderOptions.Default|ProfilerRecorderOptions.SumAllSamplesInFrame);
            void Packet(string direction,NetworkDiagnostics.MessageInfo info){if(!quiet)network.WriteLine(JsonConvert.SerializeObject(new{phase,now=Now,frame=Time.frameCount,direction,type=info.message.GetType().Name,info.bytes,info.count}));}
            Action<NetworkDiagnostics.MessageInfo> incoming=i=>Packet("in",i),outgoing=i=>Packet("out",i);
            if(!control){NetworkDiagnostics.InMessageEvent+=incoming;NetworkDiagnostics.OutMessageEvent+=outgoing;}
            var protocolInjected=new HashSet<string>();
            void Event(string kind,int connection,PNActionPacket? p,PNActionReceiver r,double now,double value)
            {
                if(stageEnabled&&stageSamples.Count==stageCapacity)
                    droppedStageSamples++;
                else if(stageEnabled)
                {
                    var v=p.GetValueOrDefault();
                    stageSamples.Add(new StageSample{Kind=kind,Phase=phase,Peer=connection,Frame=Time.frameCount,Tick=System.Diagnostics.Stopwatch.GetTimestamp(),Now=now,Value=value,Bytes=v.Body?.Length??0,Tag=p.HasValue?v.Kind:-1,N=v.N,Request=v.Request,Subscription=v.Stream.Subscription,Entity=v.Stream.Entity,Source=v.Stream.Source,Capture=v.T,AppliedN=r?.AppliedSequence??-1});
                }
                if(r!=null)receivers[r.Stream.Entity]=r;
                if(kind=="pump"||kind=="receive"){int slot=Time.frameCount%cpuSamples.Length;if(cpuFrames[slot]!=Time.frameCount){cpuFrames[slot]=Time.frameCount;cpuSamples[slot]=0;}cpuSamples[slot]+=value;}
                if((kind=="receive"||kind=="live-outcome")&&p.HasValue&&p.Value.Kind==1&&r!=null&&r.State==PNActionReceiveState.Prepared&&p.Value.Stream.Equals(r.Stream)&&p.Value.Request==r.Request)
                {
                    Need(applySamples.Count<32768,"bounded apply metric buffer");var timing=PNTimeService.For(EveRegistrySession.Context).Sample();var packet=p.Value;bool confirmed=kind=="live-outcome";var outcome=confirmed?r.LastLiveCommit:default;
                    applySamples.Add(new ApplySample{Phase=phase,Frame=Time.frameCount,Entity=packet.Stream.Entity,Source=packet.Stream.Source,Session=packet.Stream.Session,World=packet.Stream.Scope.World,Incarnation=packet.Stream.Scope.Incarnation,Nonce=packet.Stream.Nonce,Generation=packet.Stream.Generation,AppliedN=r.AppliedSequence,Subscription=packet.Stream.Subscription,Request=packet.Request,Required=packet.N,ClockReady=timing.Ready,Capture=packet.T,HostEstimate=timing.Ready?timing.Host:-1,Uncertainty=timing.Ready?timing.Uncertainty:-1,LocalApplied=Time.realtimeSinceStartupAsDouble,HandlerMs=value,ConfirmedOutcome=confirmed,CommittedRequired=outcome.Required,CommittedLatest=outcome.Latest,Ignored=outcome.Ignored});
                }
                if(quiet)return;
                var row=new JObject{["phase"]=phase,["frame"]=Time.frameCount,["now"]=now,["realtime"]=Time.realtimeSinceStartupAsDouble,["kind"]=kind,["peer"]=connection,["value"]=value};
                if(p.HasValue){var timing=PNTimeService.For(EveRegistrySession.Context).Sample();row["hostNow"]=timing.Host;row["uncertainty"]=timing.Uncertainty;var v=p.Value;row["entity"]=v.Stream.Entity;row["source"]=v.Stream.Source.ToString("N");row["subscription"]=v.Stream.Subscription;row["request"]=v.Request;
                    row["tag"]=v.Kind;row["n"]=v.N;row["t"]=v.T;row["total"]=v.Total;row["part"]=v.Index;row["count"]=v.Count;
                    row["body"]=v.Body==null?null:Convert.ToBase64String(v.Body);row["digest"]=v.Digest==null?null:Convert.ToBase64String(v.Digest);}
                if(r!=null){row["state"]=r.State.ToString();row["appliedN"]=r.AppliedSequence;row["failure"]=r.Failure;row["receiverEntity"]=r.Stream.Entity;row["matches"]=p.HasValue&&p.Value.Stream.Equals(r.Stream)&&p.Value.Request==r.Request;}
                events.WriteLine(row.ToString(Formatting.None));
                if(host&&kind=="send"&&connection==blockedPeer&&p.HasValue&&p.Value.Kind==1&&phase.StartsWith("protocol-")&&!protocolInjected.Contains(phase))
                {
                    var packet=p.Value;
                    if(phase=="protocol-duplicate"){NetworkServer.connections[connection].Send(packet,Channels.Reliable);protocolInjected.Add(phase);}
                    else if(phase=="protocol-gap"){
                        var records=PNActionWire.Decode(packet.Body);var required=records.OfType<JArray>().FirstOrDefault(a=>(int)a[0]!=1);
                        if(required!=null){var bad=(JArray)required.DeepClone();bad[1]=(long)bad[1]+100000;
                            var broken=PNActionWire.Pack(packet.Stream,packet.Request,packet.Q,packet.T,packet.N,new JArray { bad },false)[0];NetworkServer.connections[connection].Send(broken,Channels.Reliable);protocolInjected.Add(phase);}}
                }
            }
            if(!control){PNActionDiagnostics.Sample+=Event;PNActionDiagnostics.BlockSend=id=>id==blockedPeer&&Now<blockedUntil;}
            // Controlled normal baseline episode after callback attachment: same production request path.
            if(!host&&!control){var all=(System.Collections.IDictionary)typeof(PNNetworkManager).GetField("bindings",F).GetValue(manager);foreach(var binding in all.Values)typeof(PNNetworkManager).GetMethod("RequestAction",F).Invoke(manager,new object[]{binding,false});}
            var skills=UnityEngine.Object.FindObjectsByType<PNPlayerMove>(FindObjectsSortMode.None).Where(x=>x.GetComponent<EveEntityLoader>()?.IsReady==true)
                .OrderBy(x=>x.GetComponent<EveEntityIdentity>().Id.Value).Select(x=>x.GetComponent<PNSkill>()).ToArray();
            var casts=new Dictionary<PNSkill,List<PNSkillInvocation>>();
            var positions=skills.ToDictionary(x=>x,x=>x.transform.position);
            PNBuffHandle competitor=default;double nextBuff=0;long buffPulses=0;
            var phaseWorkload=new JObject();
            var castStarts=new int[skills.Length,4];
            var entityStarts=new int[skills.Length];
            int phasePeak=0;
            bool TakeBeat(double begin,double now,double interval,int limit,ref int beat,ref int missed)
            {
                int previous=beat;
                while(beat<limit&&now>=begin+beat*interval)beat++;
                if(beat==previous)return false;
                missed+=beat-previous-1;
                return true;
            }
            var data=new PNSkillData(new JObject(),new PNSkillActionData(EveId.Parse("delivery_probe:action")));
            void Start(PNSkill skill,double end,bool instant=false)
            {
                if(!casts.TryGetValue(skill,out var pool)){
                    pool=new List<PNSkillInvocation>();casts[skill]=pool;
                    for(int i=0;i<4;i++){var cast=(PNSkillInvocation)typeof(PNSkillEffectState).GetMethod("Prepare",F).Invoke(skill.Effects,new object[]{EveId.Parse("project_n:eve_attack_right"),PNSkillMode.Authority});
                        typeof(PNSkill).GetMethod("AssignCastKey",F).Invoke(skill,new object[]{cast});pool.Add(cast);invocations.Add(cast);}}
                int entity=fixedWorkload?Array.IndexOf(skills,skill):-1;
                int castIndex=fixedWorkload?entityStarts[entity]++%pool.Count:(int)(starts%pool.Count);
                if(fixedWorkload)castStarts[entity,castIndex]++;
                var invocation=pool[castIndex];
                var c=new PNSkillActionContext(skill,EveId.Parse("project_n:eve_attack_right"),data,EveRegistrySession.Current,null,Vector3.right,invocation);
                var a=(PNActionDeliveryProbe)EveRegistrySession.Current.Actions().Get(data.Action.Id).Create();
                // Match the actual driver: start at the framework interval's
                // beginning, then consume that interval in Tick. Starting at
                // wall-end and giving it the full delta manufactures a reset.
                using(var frame=skill.BeginActionExecutionFrame(Time.deltaTime)){a.Start(c);a.Perform(c);if(instant){a.Exit(c);a.Dispose();starts++;ends++;return;}}
                live.Add(new Live{Skill=skill,Action=a,Context=c,End=end});starts++;peak=Math.Max(peak,live.Count);
                if(fixedWorkload)phasePeak=Math.Max(phasePeak,live.Count);
            }
            void End(Live item,bool cancel=false){if(cancel)item.Action.Cancel(item.Context);else item.Action.Exit(item.Context);item.Action.Dispose();live.Remove(item);ends++;}
            void Tick(double rate,bool pulseBuff)
            {
                // Real Move/Buff producers share the same observation window.
                foreach(var skill in skills)skill.transform.position=positions[skill]+new Vector3((float)Math.Sin(Now*2)*.15f,0,0);
                if(pulseBuff){if(competitor.IsAlive)skills[0].GetComponent<PNBuffManager>().Buffs.Remove(competitor);var receipt=skills[0].GetComponent<PNBuffManager>().Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("project_n:timescale_reduction"),PNCombatSource.Capture(skills[0].GetComponent<PNStat>()),new JObject{["duration"]=1,["reduction"]=0,["minimumReduction"]=0}));Need(receipt.Status==PNBuffStatus.Applied,"Buff competitor admission");competitor=receipt.Handle;nextBuff=Now+.2;buffPulses++;}
                foreach(var item in live.ToArray()){
                    if(Now>=item.End){End(item);continue;}
                    using(var f=item.Skill.BeginActionExecutionFrame(Time.deltaTime)){
                        if(rate==0)f.Hold(item.Context);else f.Advance(item.Context,(float)(Time.deltaTime*rate),rate,item.Action.Advance);}
                    if(!withoutService){Need(item.Skill.ActionPublication!=null&&item.Skill.ActionPublication.Complete,"complete source");
                    Need(Math.Abs(item.Skill.ActionPublication.Progress(new PNActionRunKey(item.Context.Invocation.CastKey,item.Context.ActionOrdinal))-item.Action.Progress)<1e-5,"actual Action/authority progress");}
                    Need(item.Skill.ActionPublicationErrors==0,"source publication error");}
            }
            void Frame(){int sq=0,sb=0,rq=0,rb=0;raw?.ReliableQueues(out sq,out sb,out rq,out rb);Need(frameSamples.Count<32768,"bounded metric buffer");frameSamples.Add(new FrameSample{CallbackTicks=PNActionDiagnostics.CallbackTicks,CallbackCount=PNActionDiagnostics.CallbackCount,InclusiveTicks=PNActionDiagnostics.CoreInclusiveTicks,ExclusiveTicks=PNActionDiagnostics.CoreExclusiveTicks,KcpSendQueue=sq,KcpSendBuffer=sb,KcpReceiveQueue=rq,KcpReceiveBuffer=rb,Phase=phase,Frame=Time.frameCount,Now=Now,Delta=Time.unscaledDeltaTime,Driver=driverMs,Alloc=alloc.Valid?alloc.LastValue:-1,SenderMs=senderCpu.Valid?senderCpu.LastValue/1000000d:-1,CloneMs=cloneCpu.Valid?cloneCpu.LastValue/1000000d:-1,PruneMs=pruneCpu.Valid?pruneCpu.LastValue/1000000d:-1,ChargeMs=chargeCpu.Valid?chargeCpu.LastValue/1000000d:-1,DecodeMs=decodeCpu.Valid?decodeCpu.LastValue/1000000d:-1,
                Heap=GC.GetTotalMemory(false),G0=GC.CollectionCount(0),G1=GC.CollectionCount(1),Queue=manager.ActionQueuedBytes,Receive=manager.ActionReceiveBytes,Recoveries=manager.ActionRecoveries,Skipped=manager.ActionKnownVisibleSkipped,Resets=manager.ActionUnknownCoverageResets,Ready=manager.ClientInputReady,SourceMs=sourceCpu.Valid?sourceCpu.LastValue/1000000d:-1,RawSent=raw!=null?raw.SentBytes:-1,RawDrops=raw!=null?raw.DroppedDatagrams:-1,RawPending=raw!=null?raw.PendingBytes:-1});}
            try
            {
                File.WriteAllText(Path.Combine(folder,peer+"-profile.json"),JsonConvert.SerializeObject(manager.Profile,Formatting.Indented));
                var calibration=new byte[1048576];calibration[0]=1;long calibrated=0;
                for(int i=0;i<3;i++){yield return null;calibrated=Math.Max(calibrated,alloc.Valid?alloc.LastValue:-1);}
                GC.KeepAlive(calibration);calibration=null;Need(calibrated>=1048576,"frame allocation recorder calibration");
                File.WriteAllText(Path.Combine(folder,peer+"-allocation-calibration.json"),new JObject{["knownBytes"]=1048576,["observedFrameBytes"]=calibrated}.ToString());
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();long retainedBefore=GC.GetTotalMemory(false);
                File.WriteAllText(Path.Combine(folder,peer+"-delivery-ready"),"ready");
                yield return Wait(()=>File.Exists(Path.Combine(folder,"client1-delivery-ready"))&&File.Exists(Path.Combine(folder,"client2-delivery-ready")),"D peers ready");
                if(host)
                {
                    Need(skills.Length==3,"three real replicated player entities");
                    long slowOwner=long.Parse(File.ReadAllText(Path.Combine(folder,"client2.owner")));
                    blockedPeer=NetworkServer.connections.Values.Single(x=>x.authenticationData is PNPeer p&&p.Owner==slowOwner).connectionId;
                    File.WriteAllText(Path.Combine(folder,"slow-peer.json"),new JObject{["peer"]="client2",["owner"]=slowOwner,["connection"]=blockedPeer}.ToString());
                    int phaseIndex=0;
                    string[] phases=quiet&&!quietFaults?new[]{"control","short","overlap64","distributed64","hold-rate64","dense64","retirement"}:new[]{"control","short","overlap64","distributed64","hold-rate64","dense64","send200","send500","send2000","apply200","apply500","apply2000","budgetzero","source-rebuild","protocol-duplicate","protocol-gap","retirement"};
                    var phaseStarts=new JObject();
                    foreach(var label in phases)
                    {
                        phase=label;Publish(Path.Combine(folder,"phase-"+(phaseIndex++).ToString("D2")+"-"+label+".mark"),"");double begin=Now,next=begin;long recoveryBefore=manager.ActionRecoveries;
                        long startsBefore=starts,endsBefore=ends,buffsBefore=buffPulses;
                        int frameStart=frameSamples.Count;
                        int actionBeat=0,buffBeat=0,missedActionBeats=0,missedBuffBeats=0;
                        bool continuous=label=="overlap64",shortPhase=label=="short",distributed=label=="distributed64";
                        int count=label=="control"||label=="retirement"||shortPhase?0:load;
                        phasePeak=0;
                        Array.Clear(castStarts,0,castStarts.Length);
                        Array.Clear(entityStarts,0,entityStarts.Length);
                        if(!continuous)for(int i=0;i<count;i++)Start(skills[distributed?i%skills.Length:0],double.MaxValue);
                        double duration=label=="control"?8:label=="overlap64"?20:8;
                        int expectedActionBeats=shortPhase||continuous?(int)duration*10:0;
                        int expectedBuffBeats=(int)duration*5;
                        if(label.StartsWith("send"))blockedUntil=Now+int.Parse(label.Substring(4))/1000d;
                        if(label=="budgetzero")blockedUntil=Now+2;
                        if(label.StartsWith("send")||label=="budgetzero")Event("send-stall-start",blockedPeer,null,null,Now,blockedUntil-Now);
                        bool rebuilt=false;bool resumeCaptured=false;
                        void ResumeCut(double origin){var cut=new JObject();foreach(var sk in skills){var src=sk.ActionPublication;var id=sk.GetComponent<EveEntityIdentity>().Id.Value;using(var snapshot=src.Capture(new JArray(Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"),1,id,1,src.Source.ToString("N")),Now)){Need(snapshot!=null,"resume complete cut");cut[id.ToString()]=new JObject{["n"]=src.Sequence,["source"]=src.Source.ToString("N"),["rows"]=JArray.Parse(snapshot.Json)[5]};}}Publish(Path.Combine(folder,label+"-freshness-cut.json"),new JObject{["origin"]=origin,["cutStamp"]=Now,["entities"]=cut}.ToString());}
                        while(Now-begin<duration)
                        {
                            double frameTime=Now,elapsed=frameTime-begin,rate=1;
                            if(quietFaults&&!resumeCaptured&&(label.StartsWith("send")||label=="budgetzero")&&Now>=blockedUntil){resumeCaptured=true;ResumeCut(blockedUntil);var cut=new JObject();foreach(var sk in skills){var src=sk.ActionPublication;cut[sk.GetComponent<EveEntityIdentity>().Id.Value.ToString()]=new JObject{["n"]=src.Sequence,["source"]=src.Source.ToString("N")};}Publish(Path.Combine(folder,label+"-resume-cut.json"),cut.ToString());Event("resume-required-cut",blockedPeer,null,null,Now,0);}
                            if(quietFaults&&!resumeCaptured&&label.StartsWith("apply")&&File.Exists(Path.Combine(folder,label+"-resumed"))){resumeCaptured=true;ResumeCut(Now);}
                            foreach(var expired in live.Where(x=>frameTime>=x.End).ToArray())End(expired);
                            if(fixedWorkload)
                            {
                                if(TakeBeat(begin,frameTime,.1,expectedActionBeats,ref actionBeat,ref missedActionBeats))
                                    for(int i=0;i<load/8;i++)Start(skills[0],begin+(actionBeat-1+8)*.1,shortPhase);
                            }
                            else
                            {
                                if(shortPhase&&Now>=next){for(int i=0;i<8;i++)Start(skills[0],Now,true);next=Now+.1;}
                                if(continuous&&Now>=next){for(int i=0;i<8;i++)Start(skills[0],Now+.8);next=Now+.1;}
                            }
                            if(label=="hold-rate64")rate=new[]{1d,2,0,8,0,1}[(int)elapsed%6];
                            if(label=="dense64")rate=((int)(elapsed*4)%2)==0?1:8;
                            bool pulseBuff=fixedWorkload?TakeBeat(begin,frameTime,.2,expectedBuffBeats,ref buffBeat,ref missedBuffBeats):Now>=nextBuff;
                            long driverBegin=System.Diagnostics.Stopwatch.GetTimestamp();Tick(rate,pulseBuff);driverMs=(System.Diagnostics.Stopwatch.GetTimestamp()-driverBegin)*1000d/System.Diagnostics.Stopwatch.Frequency;
                            if(label=="source-rebuild"&&!rebuilt&&elapsed>1){var old=skills[0].ActionPublication;old.Dispose();Need(skills[0].PrepareActionPublication(Now,Now+3)==PNActionRecoveryState.Recovered,"source rebuild");rebuilt=true;}
                            Frame();yield return null;
                        }
                        var expected=new JObject();foreach(var skill in skills){var identity=skill.GetComponent<EveEntityIdentity>();var rows=new JObject();
                            if(!withoutService)using(var cut=skill.ActionPublication.Capture(new JArray(Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"),1,identity.Id.Value,1,Guid.NewGuid().ToString("N")),Now)){
                                Need(cut!=null,"checkpoint complete");foreach(JArray row in (JArray)JArray.Parse(cut.Json)[5]){row.Add(true);rows[((long)row[0][0])+":"+((long)row[0][1])]=row;}}
                            foreach(var item in live.Where(x=>x.Skill==skill).ToArray())End(item);
                            skill.SealActionRuns(Now);
                            expected[identity.Id.Value.ToString()]=new JObject{["n"]=skill.ActionPublication?.Sequence??0,["source"]=skill.ActionPublication?.Source.ToString("N")??"",["rows"]=rows};}
                        Event("checkpoint-published",-1,null,null,Now,0);
                        Publish(Path.Combine(folder,label+"-checkpoint.json"),expected.ToString());
                        phaseStarts[label]=starts-startsBefore;
                        if(fixedWorkload)
                        {
                            var byCast=new JArray();
                            for(int entity=0;entity<skills.Length;entity++)
                                byCast.Add(new JArray(castStarts[entity,0],castStarts[entity,1],castStarts[entity,2],castStarts[entity,3]));
                            phaseWorkload[label]=new JObject{
                                ["starts"]=starts-startsBefore,["ends"]=ends-endsBefore,["peak"]=phasePeak,
                                ["startsByEntityAndCast"]=byCast,["missedActionBeats"]=missedActionBeats+expectedActionBeats-actionBeat,
                                ["buffPulses"]=buffPulses-buffsBefore,["missedBuffBeats"]=missedBuffBeats+expectedBuffBeats-buffBeat,
                                ["moveTicksPerEntity"]=frameSamples.Count-frameStart,["liveAfterSeal"]=live.Count};
                        }
                        yield return Wait(()=>File.Exists(Path.Combine(folder,"client1-"+label+"-ok"))&&File.Exists(Path.Combine(folder,"client2-"+label+"-ok")),"checkpoint "+label);
                        if(label=="control"||label=="short"||label=="overlap64"||label=="distributed64"||label=="hold-rate64"||label=="dense64")Need(manager.ActionRecoveries==recoveryBefore,"routine recovery under supported "+label);
                        phase="drain";double drain=Now+1;while(Now<drain){Frame();yield return null;}
                    }
                    File.WriteAllText(Path.Combine(folder,"delivery-finished"),"done");
                    yield return Wait(()=>File.Exists(Path.Combine(folder,"client1-delivery-done"))&&File.Exists(Path.Combine(folder,"client2-delivery-done")),"client completion");
                    var workload=new JObject{["starts"]=starts,["ends"]=ends,["phaseStarts"]=phaseStarts,["peak"]=peak,["entities"]=skills.Length,["castsPerEntity"]=4,["buffPulses"]=buffPulses,["protocolInjected"]=new JArray(protocolInjected)};
                    if(fixedWorkload){workload["load"]=load;workload["phases"]=phaseWorkload;}
                    File.WriteAllText(Path.Combine(folder,"workload.json"),workload.ToString());
                    Need(starts==ends&&peak>=load,"lifecycle and network overlap coverage");
                }
                else
                {
                    string seen="";double phaseBegin=Now;bool slept=false;
                    while(!File.Exists(Path.Combine(folder,"delivery-finished")))
                    {
                        var marker=Directory.GetFiles(folder,"phase-*.mark").OrderByDescending(x=>x,StringComparer.Ordinal).FirstOrDefault();
                        if(marker!=null){phase=Path.GetFileNameWithoutExtension(marker).Substring(9);if(phase!=seen){seen=phase;phaseBegin=Now;slept=false;}}
                        if(peer=="client2"&&phase.StartsWith("apply")&&!slept&&Now-phaseBegin>.5){slept=true;int ms=int.Parse(phase.Substring(5));Event("apply-stall-start",-1,null,null,Now,ms/1000d);System.Threading.Thread.Sleep(ms);Event("apply-stall-end",-1,null,null,Now,ms/1000d);if(quietFaults)Publish(Path.Combine(folder,phase+"-resumed"),System.Diagnostics.Stopwatch.GetTimestamp().ToString(System.Globalization.CultureInfo.InvariantCulture));}
                        string freshness=Path.Combine(folder,phase+"-freshness-cut.json"),freshOk=Path.Combine(folder,peer+"-"+phase+"-freshness-ok");
                        string freshnessText=quietFaults&&!File.Exists(freshOk)?ReadPublished(freshness):null;
                        if(freshnessText!=null){var cut=JObject.Parse(freshnessText);bool caught=true;foreach(var ep in ((JObject)cut["entities"]).Properties()){if(!receivers.TryGetValue(long.Parse(ep.Name),out var rr)||rr.State!=PNActionReceiveState.Prepared||rr.Stream.Source.ToString("N")!=(string)ep.Value["source"]||rr.AppliedSequence<(long)ep.Value["n"]){caught=false;break;}var actual=Snapshot(rr);foreach(JArray row in (JArray)ep.Value["rows"]){string key=((long)row[0][0])+":"+((long)row[0][1]);if(actual[key]==null||(double)actual[key]["p"]+1e-5<(double)row[4]){caught=false;break;}}if(!caught)break;}
                            if(caught){var ts=PNTimeService.For(EveRegistrySession.Context).Sample();double elapsed=ts.Ready?ts.Host-(double)cut["origin"]:-1;if(peer=="client2"&&phase.StartsWith("apply"))elapsed=(System.Diagnostics.Stopwatch.GetTimestamp()-long.Parse(ReadPublished(Path.Combine(folder,phase+"-resumed")),System.Globalization.CultureInfo.InvariantCulture))/(double)System.Diagnostics.Stopwatch.Frequency;Event("resume-freshness-applied",-1,null,null,Now,elapsed);File.WriteAllText(freshOk,new JObject{["elapsed"]=elapsed,["uncertainty"]=phase.StartsWith("apply")&&peer=="client2"?0:ts.Uncertainty,["clockReady"]=ts.Ready,["hostCutStamp"]=cut["cutStamp"],["note"]="required watermark and all resume-cut run progress covered; apply-client2 origin is same-process real resume, target cut is later and conservative; other origins retain clock uncertainty"}.ToString());}}
                        string resumePath=Path.Combine(folder,phase+"-resume-cut.json"),resumeOk=Path.Combine(folder,peer+"-"+phase+"-resume-ok");
                        string resumeText=quietFaults&&!File.Exists(resumeOk)?ReadPublished(resumePath):null;
                        if(resumeText!=null){var cut=JObject.Parse(resumeText);if(cut.Properties().All(p=>receivers.TryGetValue(long.Parse(p.Name),out var rr)&&rr.State==PNActionReceiveState.Prepared&&rr.Stream.Source.ToString("N")== (string)p.Value["source"]&&rr.AppliedSequence>=(long)p.Value["n"])){Event("resume-required-applied",-1,null,null,Now,0);File.WriteAllText(resumeOk,"required-only watermark, latest freshness not proven");}}
                        string path=Path.Combine(folder,phase+"-checkpoint.json"),ok=Path.Combine(folder,peer+"-"+phase+"-ok");
                        if(File.Exists(path)&&!File.Exists(ok)){
                            if(control){var bindings=(System.Collections.IDictionary)typeof(PNNetworkManager).GetField("bindings",F).GetValue(manager);foreach(var binding in bindings.Values){var actions=binding.GetType().GetField("Actions",F).GetValue(binding);var receiver=(PNActionReceiver)actions.GetType().GetField("Receiver",F).GetValue(actions);if(receiver!=null)receivers[receiver.Stream.Entity]=receiver;}}
                            var expected=JObject.Parse(File.ReadAllText(path));bool ready=withoutService||expected.Properties().All(p=>receivers.TryGetValue(long.Parse(p.Name),out var r)&&r.State==PNActionReceiveState.Prepared&&r.Stream.Source.ToString("N")== (string)p.Value["source"]&&r.AppliedSequence>=(long)p.Value["n"]);
                            if(ready){foreach(var p in expected.Properties()){
                                if(withoutService)continue;
                                var actual=Snapshot(receivers[long.Parse(p.Name)]);foreach(var row in ((JObject)p.Value["rows"]).Properties()){
                                    Need(actual[row.Name]!=null,"missing lifecycle "+row.Name+" "+phase);
                                    var a=(JArray)row.Value;Need((bool)actual[row.Name]["ended"]==(bool)a[6],"terminal state "+row.Name);
                                    Need(Math.Abs((double)actual[row.Name]["p"]-(double)a[4])<1e-5,"receiver final progress "+row.Name);
                                    Need(JToken.DeepEquals(actual[row.Name]["d"],a[1]),"immutable context "+row.Name);}}
                                Event("checkpoint-applied",-1,null,null,Now,0);File.WriteAllText(ok,"PASS actual model checkpoint");}}
                        Frame();yield return null;
                    }
                    File.WriteAllText(Path.Combine(folder,peer+"-delivery-done"),"done");
                    yield return new WaitForSecondsRealtime(.5f);
                }
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();File.WriteAllText(Path.Combine(folder,peer+"-retained.json"),new JObject{["before"]=retainedBefore,["after"]=GC.GetTotalMemory(false),["note"]="explicit full collections outside active measurement; after retirement"}.ToString());
            }
            finally{PNActionDiagnostics.Sample-=Event;NetworkDiagnostics.InMessageEvent-=incoming;NetworkDiagnostics.OutMessageEvent-=outgoing;PNActionDiagnostics.BlockSend=null;foreach(var item in live.ToArray())End(item,true);foreach(var invocation in invocations)invocation.Dispose();if(host&&competitor.IsAlive)skills[0].GetComponent<PNBuffManager>().Buffs.Remove(competitor);if(host)foreach(var pair in positions)if(pair.Key!=null)pair.Key.transform.position=pair.Value;
                if(testStageOverflow)Need(stageSamples.Count==stageCapacity&&droppedStageSamples>0,"forced stage overflow");
                using(var stages=new StreamWriter(Path.Combine(folder,peer+"-stages.jsonl")))foreach(var sample in stageSamples)stages.WriteLine(sample.Json().ToString(Formatting.None));
                File.WriteAllText(Path.Combine(folder,peer+"-stages-summary.json"),new JObject{["enabled"]=stageEnabled,["count"]=stageSamples.Count,["capacity"]=stageCapacity,["overflow"]=droppedStageSamples>0,["dropped"]=droppedStageSamples,["quietFaults"]=quietFaults,["note"]="overflow makes stage metrics incomplete. Monotonic ticks are process-local. Callbacks are disabled in matched allocation controls."}.ToString());
                using(var committed=new StreamWriter(Path.Combine(folder,peer+"-live-outcomes.jsonl")))
                    foreach(var sample in applySamples)(sample.ConfirmedOutcome?committed:applied).WriteLine(sample.Json().ToString(Formatting.None));
                foreach(var sample in frameSamples)frames.WriteLine(sample.Json(cpuFrames[sample.Frame%cpuSamples.Length]==sample.Frame?cpuSamples[sample.Frame%cpuSamples.Length]:double.NaN).ToString(Formatting.None));}
        }
    }
}
