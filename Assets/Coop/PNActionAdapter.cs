using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Mirror;
using UnityEngine;
namespace ProjectN.Coop
{
    public enum PNActionPresentationPhase { LateJoin, Steady, Recovery }

    public readonly struct PNActionPresentationStats
    {
        public readonly long Samples, Unknown, MissingPose, LongestUnknownFrames;
        public readonly double LongestUnknownMilliseconds;
        public double? UnknownRatio => Samples==0?(double?)null:(double)Unknown/Samples;

        internal PNActionPresentationStats(long samples,long unknown,long missing,long longest,double milliseconds)
        {
            Samples=samples;Unknown=unknown;MissingPose=missing;
            LongestUnknownFrames=longest;LongestUnknownMilliseconds=milliseconds;
        }
    }

    public sealed partial class PNNetworkManager
    {
        const string ActionKind="project_n:action";
        static readonly Guid UnknownActionSource=new Guid("ffffffffffffffffffffffffffffffff");
        sealed class ActionBindingState
        {
            public bool Known,HasSkill,AckSent;
            public PNActionStream Stream;
            public long Request;
            public double Deadline,LastRequest,LastTraffic;
            public PNActionReceiver Receiver;
            public PNActionPacket? Ack;
            public int PoseFrame=-1, MeasuredFrame=-1;
            public double PoseStamp;
            public bool PresentedBaseline;
            public long PresentedSubscription;
            public PNActionPresentationPhase MeasuredPhase;
            public long UnknownFrames;
            public double UnknownSince;
            public PNSkill Skill;
            public readonly Func<PNActionRunKey,PNActionRunEnd?> EndAtPose;
            public ActionBindingState()
            {
                EndAtPose=key=>Receiver?.PresentationEnd(PoseStamp,key);
            }
        }
        sealed class ActionPeer
        {
            public Binding Binding;public NetworkConnectionToClient Connection;public PNActionStream Stream;public long Request;public double Deadline;public PNActionSender Sender;public bool Recovering=true;public bool FenceSeen;public uint FenceAttempt;
            public bool BeginFence(uint attempt){if(FenceSeen&&FenceAttempt==attempt)return false;FenceSeen=true;FenceAttempt=attempt;return true;}
            public void BeginRecovery(double deadline){Deadline=Recovering?Math.Min(Deadline,deadline):deadline;Recovering=true;}
        }
        readonly Dictionary<(int,uint),ActionPeer> actionPeers=new Dictionary<(int,uint),ActionPeer>();
        readonly PNActionReceiveBudget actionSendLedger=new PNActionReceiveBudget(32*1024*1024);
        readonly PNActionReceiveBudget actionReceiveLedger=new PNActionReceiveBudget(64*1024*1024);
        PNActionTransport actionTransport;
        bool previousPublicationFlag,actionAuthority;
        int actionCursor;
        readonly PNActionPresentationStats[] actionPresentationStats=new PNActionPresentationStats[3];
        readonly List<PNObservedAction> observedRuns=new List<PNObservedAction>();
        readonly List<PNObservedAction> supportedRuns=new List<PNObservedAction>();
        public PNActionPresentationStats ActionPresentationStats(PNActionPresentationPhase phase)
            =>actionPresentationStats[(int)phase];

        bool CanReadActionPresentation(Binding b) => ActionsEnabled&&!NetworkServer.active&&!b.Retiring&&
            b.Actions.HasSkill&&b.Movement.Component?.NetworkRole==PNMoveNetworkRole.Observer;

        public PNActionPresentationCoverage ReadObservedActions(uint netId,List<PNObservedAction> destination,out double? presentation)
        {
            if(bindings.TryGetValue(netId,out var binding)&&CanReadActionPresentation(binding)&&
                binding.Actions.PoseFrame==Time.frameCount)
            {
                presentation=binding.Actions.PoseStamp;
                if(binding.Actions.Receiver!=null)
                    return binding.Actions.Receiver.ReadPresentation(presentation.Value,destination);
            }
            else presentation=null;
            destination?.Clear();
            return PNActionPresentationCoverage.Unknown;
        }

        void MeasureActionPresentation()
        {
            if(!ActionsEnabled||NetworkServer.active)return;
            int frame=Time.frameCount;
            double now=ActionNow;
            foreach(var b in bindings.Values)
            {
                var a=b.Actions;
                if(!CanReadActionPresentation(b)){a.UnknownFrames=0;a.Skill?.ClearObservedActionPresentation();continue;}
                if(a.MeasuredFrame==frame)continue;
                bool hasPose=a.PoseFrame==frame;
                var receiver=a.Receiver;
                bool atBaseline=hasPose&&receiver!=null&&a.PoseStamp>=receiver.BaselinePresentation;
                bool settled=atBaseline&&receiver.State==PNActionReceiveState.Prepared;
                var phase=!a.PresentedBaseline?PNActionPresentationPhase.LateJoin:
                    a.AckSent&&receiver?.State==PNActionReceiveState.Prepared&&
                    a.PresentedSubscription==receiver.Stream.Subscription
                    ?PNActionPresentationPhase.Steady:PNActionPresentationPhase.Recovery;
                if(settled){a.PresentedBaseline=true;a.PresentedSubscription=receiver.Stream.Subscription;}
                if(a.MeasuredFrame!=frame-1||a.MeasuredPhase!=phase)a.UnknownFrames=0;
                a.MeasuredFrame=frame;
                a.MeasuredPhase=phase;
                var stats=actionPresentationStats[(int)phase];
                var coverage=hasPose&&receiver!=null?receiver.ReadPresentation(a.PoseStamp,observedRuns):PNActionPresentationCoverage.Unknown;
                bool unknown=hasPose&&coverage==PNActionPresentationCoverage.Unknown;
                PresentActions(a,coverage);
                if(atBaseline)receiver.RetirePastPresentation(a.PoseStamp);
                if(unknown)
                {
                    if(a.UnknownFrames==0)a.UnknownSince=now;
                    a.UnknownFrames++;
                }
                else a.UnknownFrames=0;
                actionPresentationStats[(int)phase]=new PNActionPresentationStats(stats.Samples+(hasPose?1:0),
                    stats.Unknown+(unknown?1:0),stats.MissingPose+(hasPose?0:1),
                    Math.Max(stats.LongestUnknownFrames,a.UnknownFrames),
                    Math.Max(stats.LongestUnknownMilliseconds,unknown?(now-a.UnknownSince)*1000:0));
            }
        }
        void PresentActions(ActionBindingState binding,PNActionPresentationCoverage coverage)
        {
            var skill=binding.Skill;
            if(skill==null)return;
            if(!skill.CanUseObservedActionPresentation)
            {
                skill.ClearObservedActionPresentation();
                return;
            }
            if(coverage==PNActionPresentationCoverage.Active)
            {
                supportedRuns.Clear();
                foreach(var run in observedRuns)
                    if(run.Capability==1&&skill.CanPresentObservedAction(run.DataId,run.ActionId))
                        supportedRuns.Add(run);
                int winner=0;
                for(int i=1;i<supportedRuns.Count;i++)
                {
                    var run=supportedRuns[i];var last=supportedRuns[winner];
                    if(run.Start>last.Start||run.Start==last.Start&&
                        (run.Key.Cast>last.Key.Cast||run.Key.Cast==last.Key.Cast&&run.Key.Ordinal>last.Key.Ordinal))
                        winner=i;
                }
                for(int i=0;i<supportedRuns.Count;i++)
                {
                    var run=supportedRuns[i];
                    skill.PresentObservedAction(run.Key,run.DataId,
                        new Vector3((float)run.DirectionX,0,(float)run.DirectionZ).normalized,run.Progress,i==winner,run.PresentationScale);
                }
            }
            skill.ReconcileObservedActionPresentation(coverage!=PNActionPresentationCoverage.Unknown,binding.EndAtPose);
        }
        public long ActionRecoveries {get;private set;}
        public long ActionPacketsSent {get;private set;}
        public long ActionFenceBaselines {get;private set;}
        public int ActionQueuedBytes=>actionSendLedger.Used;
        public int ActionReceiveBytes=>actionReceiveLedger.Used;
        public int ActionPeerCount=>actionPeers.Count;
        public int ActionPreparedBindings=>bindings.Values.Count(b=>b.Actions.Receiver?.State==PNActionReceiveState.Prepared&&b.Actions.AckSent);
        public long ActionAppliedSequence=>bindings.Values.Sum(b=>b.Actions.Receiver?.AppliedSequence??0);
        public long ActionKnownVisibleSkipped=>bindings.Values.Sum(b=>b.Actions.Receiver?.KnownPotentiallyVisibleSkipped??0);
        public long ActionUnknownCoverageResets=>bindings.Values.Sum(b=>b.Actions.Receiver?.UnknownCoverageResets??0);
        bool ActionsEnabled=>Profile.ActionReplication;
        double ActionNow=>NetworkTime.localTime;
        double ActionGrace=>Math.Min(GraceLimit,Scheduler.TimeoutMilliseconds/1000d);
        double ClientActionDeadline(double now)=>now+Math.Min(ActionGrace,RemainingClockRecoverySeconds);
        double HostActionDeadline(NetworkConnectionToClient conn,double now)
            =>now+Math.Min(ActionGrace,conn.authenticationData is PNPeer p&&p.InterruptedAt>=0?Math.Max(0,p.InterruptedAt+GraceLimit-Time.realtimeSinceStartupAsDouble):ActionGrace);
        bool ActionPrepared(Binding b)=>!ActionsEnabled||NetworkServer.active||b.Actions.Known&&!b.Actions.HasSkill||b.Actions.AckSent&&b.Actions.Receiver?.State==PNActionReceiveState.Prepared;
        int ActionPeerBytes(int connection)=>actionPeers.Values.Where(x=>x.Connection.connectionId==connection).Sum(x=>x.Sender?.Bytes??0);
        void InitializeActions()
        {
            if(!ActionsEnabled)return;
            Array.Clear(actionPresentationStats,0,actionPresentationStats.Length);
            if(NetworkServer.active){actionAuthority=true;previousPublicationFlag=PNSkill.ActionPublicationEnabled;PNSkill.ActionPublicationEnabled=true;}
            actionReceiveLedger.AdditionalAdmission=n=>(long)actionReceiveLedger.Used+pendingBuffBytes+inbox.TotalBytes+n<=Profile.ActionReceiveBytes;
            RequiredAdapters.Add(ActionKind);
            RegisterStateAdapter(ActionKind,()=>NetworkServer.active||bindings.Values.Where(b=>!b.Retiring&&RoomCatchupBinding(b)).All(b=>ActionPrepared(b)));
            actionTransport=new PNActionTransport(NetworkServer.active?(Func<NetworkConnectionToClient,PNActionStream,bool>)AuthorizeActionPeer:null,NetworkServer.active?(Action<NetworkConnectionToClient,PNActionPacket>)ReceiveActionControl:null,
                NetworkClient.active?(Func<PNActionStream,bool>)AuthorizeActionHost:null,NetworkClient.active?(Action<PNActionPacket>)ReceiveActionPacket:null);
        }
        bool AuthorizeActionPeer(NetworkConnectionToClient conn,PNActionStream s)
        {
            if(stopping||!IsPlaying||fatalSerialization||conn is LocalConnectionToClient||!(conn.authenticationData is PNPeer)||!bindings.TryGetValue(s.NetId,out var b)||b.Retiring)return false;
            return s.Session==b.Metadata.HostSession&&s.Entity==b.Metadata.Entity&&s.Generation==b.Metadata.Generation&&b.Shell.netIdentity.observers.ContainsKey(conn.connectionId)&&AuthorizeWorldRequest(conn,b,s.Scope);
        }
        bool AuthorizeActionHost(PNActionStream s)
            =>!NetworkServer.active&&!stopping&&IsPlaying&&!fatalSerialization&&bindings.TryGetValue(s.NetId,out var b)&&!b.Retiring&&s.Session==b.Metadata.HostSession&&s.Entity==b.Metadata.Entity&&s.Generation==b.Metadata.Generation&&s.Nonce==(ulong)b.Token&&Scope(b).Equals(s.Scope);
        PNActionStream ClientActionStream(Binding b)=>new PNActionStream{Session=b.Metadata.HostSession,NetId=b.Shell.netId,Entity=b.Metadata.Entity,Generation=b.Metadata.Generation,Nonce=(ulong)b.Token,Scope=Scope(b),Source=b.Actions.Stream.Source==Guid.Empty?UnknownActionSource:b.Actions.Stream.Source,Subscription=checked(b.Actions.Stream.Subscription+1)};
        bool PrepareAction(Binding b,EveEntityLoader loader)
        {
            if(!ActionsEnabled||NetworkServer.active)return true;
            b.Actions.Known=true;
            b.Actions.Skill=loader.GetComponent<PNSkill>();
            b.Actions.HasSkill=b.Actions.Skill!=null;
            if(!b.Actions.HasSkill)return true;
            b.Actions.Skill.SetObservedActionPresentation(true);
            if(b.Actions.Request==0)RequestAction(b,false);
            return ActionPrepared(b);
        }
        void RequestAction(Binding b,bool recovery)
        {
            var a=b.Actions;double now=ActionNow;
            if(a.Request==0||a.AckSent)a.Deadline=ClientActionDeadline(now);else a.Deadline=Math.Min(a.Deadline,ClientActionDeadline(now));
            a.Stream=ClientActionStream(b);a.Request=checked(a.Request+1);a.AckSent=false;a.Ack=null;a.LastRequest=-1;a.LastTraffic=now;
            if(a.Receiver!=null)a.Receiver.Reset(a.Stream,a.Request,a.Deadline);
            SetInput(b,false);if(recovery)ActionRecoveries++;
            PNActionDiagnostics.Emit("baseline-request",-1,ActionRequest(a),a.Receiver,now,0);
        }
        PNActionPacket ActionRequest(ActionBindingState a)=>new PNActionPacket{Stream=a.Stream,Kind=2,Request=a.Request,Digest=new byte[32],Body=Array.Empty<byte>()};
        void ReceiveActionControl(NetworkConnectionToClient conn,PNActionPacket packet)
        {
            var key=(conn.connectionId,packet.Stream.NetId);double now=ActionNow;
            if(packet.Kind==3){if(actionPeers.TryGetValue(key,out var found)&&found.Sender?.Confirm(packet,now)==true){found.Recovering=false;PNActionDiagnostics.Emit("ack",conn.connectionId,packet,null,now,now-packet.T);}return;}
            if(!bindings.TryGetValue(packet.Stream.NetId,out var b)||b.Candidate?.Loader?.GetComponent<PNSkill>()==null)return;
            PNActionDiagnostics.Emit("baseline-host-request",conn.connectionId,packet,null,now,0);
            double deadline=HostActionDeadline(conn,now);
            if(actionPeers.TryGetValue(key,out var old))
            {
                if(packet.Stream.Nonce<old.Stream.Nonce||packet.Stream.Nonce==old.Stream.Nonce&&packet.Request<=old.Request)return;
                if(packet.Stream.Nonce==old.Stream.Nonce&&old.Recovering)deadline=Math.Min(deadline,old.Deadline);
                RemoveActionPeer(old);
            }
            actionPeers.Add(key,new ActionPeer{Binding=b,Connection=conn,Stream=packet.Stream,Request=packet.Request,Deadline=deadline});
        }
        void ReceiveActionPacket(PNActionPacket packet)
        {
            if(!PNActionDiagnostics.Enabled){ReceiveActionPacketCore(packet);return;}
            long observer=PNActionDiagnostics.ObserverTicks,begin=System.Diagnostics.Stopwatch.GetTimestamp();
            bool accepted=ReceiveActionPacketCore(packet);
            long inclusive=System.Diagnostics.Stopwatch.GetTimestamp()-begin,observerDelta=PNActionDiagnostics.ObserverTicks-observer;
            PNActionDiagnostics.RecordCore(inclusive,observerDelta);
            double ms=(inclusive-observerDelta)*1000d/System.Diagnostics.Stopwatch.Frequency;
            var receiver=bindings.TryGetValue(packet.Stream.NetId,out var b)?b.Actions.Receiver:null;
            PNActionDiagnostics.Emit("receive",-1,packet,receiver,ActionNow,ms);
            if(accepted&&packet.Kind==1&&receiver.State==PNActionReceiveState.Prepared)
                PNActionDiagnostics.Emit("live-outcome",-1,packet,receiver,ActionNow,ms);
        }
        bool ReceiveActionPacketCore(PNActionPacket packet)
        {
            var b=bindings[packet.Stream.NetId];var a=b.Actions;double now=ActionNow;
            if(!a.HasSkill||packet.Request<a.Request||packet.Stream.Subscription<a.Stream.Subscription)return false;
            if(packet.Kind==0&&(a.Receiver==null||!a.Stream.Equals(packet.Stream)))
            {
                if(a.Receiver!=null&&packet.Stream.Subscription==a.Receiver.Stream.Subscription&&packet.Request==a.Request&&!a.AckSent&&a.Receiver.State==PNActionReceiveState.Waiting)
                {a.Receiver.ResolveSource(packet.Stream,packet.Request);a.Stream=packet.Stream;}
                else if(a.Receiver!=null&&packet.Stream.Subscription<=a.Receiver.Stream.Subscription)return false;
                if(a.AckSent)a.Deadline=ClientActionDeadline(now);
                a.Deadline=Math.Min(a.Deadline,ClientActionDeadline(now));a.Stream=packet.Stream;a.Request=packet.Request;a.AckSent=false;a.Ack=null;
                if(a.Receiver==null)a.Receiver=new PNActionReceiver(actionReceiveLedger,a.Stream,a.Request,a.Deadline,ActionGrace);
                else if(!a.Receiver.Stream.Equals(a.Stream))a.Receiver.Reset(a.Stream,a.Request,a.Deadline);
                SetInput(b,false);
            }
            if(!a.Stream.Equals(packet.Stream)||packet.Request!=a.Request)return false;
            if(a.Receiver?.Receive(packet,now)!=true)return false;
            a.LastTraffic=now;return true;
        }
        void RemoveActionPeer(ActionPeer p){p.Sender?.Dispose();p.Sender=null;actionPeers.Remove((p.Connection.connectionId,p.Stream.NetId));if(!actionPeers.Values.Any(x=>x.Connection==p.Connection))actionCredit.Remove(p.Connection.connectionId);}
        void UnhookAction(Binding b)
        {b.Actions.Skill?.SetObservedActionPresentation(false);b.Actions.Skill=null;b.Actions.Receiver?.Dispose();b.Actions.Receiver=null;b.Actions.Ack=null;b.Actions.PoseFrame=-1;b.Actions.PresentedBaseline=false;b.Actions.UnknownFrames=0;foreach(var p in actionPeers.Values.Where(p=>p.Binding==b).ToArray())RemoveActionPeer(p);}
        void DisposeActions()
        {RequiredAdapters.Remove(ActionKind);actionTransport?.Dispose();actionTransport=null;foreach(var p in actionPeers.Values.ToArray())RemoveActionPeer(p);actionCredit.Clear();if(actionAuthority){PNSkill.ActionPublicationEnabled=previousPublicationFlag;actionAuthority=false;}}
        void PumpActions()
        {
            if(!PNActionDiagnostics.Enabled){PumpActionsCore();return;}
            long observer=PNActionDiagnostics.ObserverTicks,begin=System.Diagnostics.Stopwatch.GetTimestamp();
            PumpActionsCore();
            long inclusive=System.Diagnostics.Stopwatch.GetTimestamp()-begin,observerDelta=PNActionDiagnostics.ObserverTicks-observer;
            PNActionDiagnostics.RecordCore(inclusive,observerDelta);
            double ms=(inclusive-observerDelta)*1000d/System.Diagnostics.Stopwatch.Frequency;
            PNActionDiagnostics.Emit("pump",-1,null,null,ActionNow,ms);
        }
        void PumpActionsCore()
        {
            if(!ActionsEnabled)return;double now=ActionNow;
            if(!NetworkServer.active)
            {
                foreach(var b in bindings.Values.Where(b=>!b.Retiring).ToArray())
                {
                    var a=b.Actions;if(!a.Known&&b.Candidate?.Loader!=null)PrepareAction(b,b.Candidate.Loader);
                    if(!a.HasSkill)continue;
                    a.Receiver?.Tick(now);
                    if(a.Receiver?.State==PNActionReceiveState.Failed||!a.AckSent&&now>=a.Deadline)throw new TimeoutException("Action baseline original deadline");
                    if(a.Receiver?.State==PNActionReceiveState.NeedsRecovery||a.AckSent&&now-a.LastTraffic>=Profile.ActionSilenceSeconds){PNActionDiagnostics.Emit("client-recovery",-1,null,a.Receiver,now,a.Receiver?.State==PNActionReceiveState.NeedsRecovery?1:2);RequestAction(b,true);}
                    if(a.Receiver?.State==PNActionReceiveState.ReadyToApply){a.Ack=a.Receiver.TryApply(now,a.PoseFrame==Time.frameCount?(double?)a.PoseStamp:null);if(a.Ack.HasValue)PNActionDiagnostics.Emit("baseline-applied",-1,a.Ack,a.Receiver,now,0);}
                    if(a.Ack.HasValue&&TakeSendBudget(-1,192,true)){NetworkClient.Send(a.Ack.Value,Channels.Reliable);a.Ack=null;a.AckSent=true;SetInput(b,clientInputReady);}
                    if(!a.AckSent&&a.Receiver?.State!=PNActionReceiveState.ReadyToApply&&now-a.LastRequest>=.25&&TakeSendBudget(-1,192,true))
                    {NetworkClient.Send(ActionRequest(a),Channels.Reliable);a.LastRequest=now;}
                }
                return;
            }
            var peers=actionPeers.Values.ToArray();
            for(int i=0;i<peers.Length;i++)
            {
                var p=peers[(i+actionCursor)%peers.Length];var b=p.Binding;
                if(b.Retiring||!NetworkServer.connections.ContainsKey(p.Connection.connectionId)||!AuthorizeActionPeer(p.Connection,p.Stream)){RemoveActionPeer(p);continue;}
                if(p.Recovering&&now>=p.Deadline){p.Connection.Disconnect();RemoveActionPeer(p);continue;}
                var skill=b.Candidate?.Loader?.GetComponent<PNSkill>();if(skill==null){RemoveActionPeer(p);continue;}
                try
                {
                    var source=skill.ActionPublication;
                    if(source==null||!source.Complete||source.Disposed)
                    {
                        p.BeginRecovery(HostActionDeadline(p.Connection,now));
                        var state=skill.PrepareActionPublication(now,p.Deadline);
                        if(state==PNActionRecoveryState.Failed){p.Connection.Disconnect();RemoveActionPeer(p);continue;}
                        if(state==PNActionRecoveryState.Pending)continue;source=skill.ActionPublication;
                    }
                    if(p.Sender!=null&&(p.Sender.Stream.Source!=source.Source||p.Sender.NeedsBaseline||p.Sender.PendingExpired(now,Profile.ActionPendingSeconds)))
                    {
                        PNActionDiagnostics.Emit("recovery",p.Connection.connectionId,null,null,now,p.Sender.Stream.Source!=source.Source?1:p.Sender.NeedsBaseline?2:3);
                        p.BeginRecovery(HostActionDeadline(p.Connection,now));
                        p.Sender.Dispose();p.Sender=null;p.Stream.Subscription=checked(p.Stream.Subscription+1);p.Request=checked(p.Request+1);ActionRecoveries++;
                    }
                    if(p.Sender==null)
                    {
                        p.Stream.Source=source.Source;
                        try{p.Sender=new PNActionSender(source,p.Stream,p.Request,now,p.Deadline,actionSendLedger,
                            extra=>ActionPeerBytes(p.Connection.connectionId)+movePeers.Values.Where(m=>m.Connection==p.Connection).Sum(m=>m.Bytes)+buffPeers.Values.Where(m=>m.Connection==p.Connection).Sum(m=>m.Bytes)+extra<=8*1024*1024&&(long)actionSendLedger.Used+hostMoveBytes+hostBuffBytes+extra<=32*1024*1024);}
                        catch(InvalidOperationException){continue;}
                    }
                    p.Sender.Capture(now,Profile.ActionSendSeconds);
                    if(PNActionDiagnostics.Enabled)
                    {
                        var identity=new PNActionPacket{Stream=p.Sender.Stream,Request=p.Sender.Request,N=p.Sender.SubmittedSequence};
                        double dirty=p.Sender.OldestDirty(now),queued=p.Sender.OldestQueued(now),required=p.Sender.OldestRequired(now);
                        PNActionDiagnostics.Emit("pending",p.Connection.connectionId,identity,null,now,Math.Max(required,Math.Max(dirty,queued)));
                        PNActionDiagnostics.Emit("pending-dirty",p.Connection.connectionId,identity,null,now,dirty);
                        PNActionDiagnostics.Emit("pending-queued",p.Connection.connectionId,identity,null,now,queued);
                        PNActionDiagnostics.Emit("pending-required",p.Connection.connectionId,identity,null,now,required);
                    }
                    if(p.Sender.NeedsBaseline){p.BeginRecovery(HostActionDeadline(p.Connection,now));continue;}
                    for(int n=0;n<8;n++)if(!p.Sender.DrainOne(now,bytes=>TakeActionBudget(p.Connection.connectionId,bytes,now),packet=>{p.Connection.Send(packet,Channels.Reliable);ActionPacketsSent++;PNActionDiagnostics.Emit("send",p.Connection.connectionId,packet,null,now,0);}))break;
                }
                catch(Exception e){LastFailure="Action stream: "+e.Message;p.Connection.Disconnect();RemoveActionPeer(p);}
            }
            actionCursor=(actionCursor+1)%Math.Max(1,peers.Length);
        }
        readonly Dictionary<int,(double At,int Bytes)> actionCredit=new Dictionary<int,(double,int)>();
        bool TakeActionBudget(int connection,int bytes,double now)
        {
            if(PNActionDiagnostics.BlockSend?.Invoke(connection)==true)return false;
            if(!actionCredit.TryGetValue(connection,out var credit))credit=(now,65536);
            if(now-credit.At>=.05)credit=(now,Math.Min(65536,credit.Bytes+(int)((now-credit.At)/.05)*16384));
            if(credit.Bytes<bytes){actionCredit[connection]=credit;return false;}
            if(!TakeSendBudget(connection,bytes,true))return false;
            actionCredit[connection]=(credit.At,credit.Bytes-bytes);return true;
        }
        bool ActionPeerPrepared(Binding b,NetworkConnectionToClient conn,uint attempt)
        {
            if(!ActionsEnabled||b.Candidate?.Loader?.GetComponent<PNSkill>()==null)return true;
            if(!actionPeers.TryGetValue((conn.connectionId,b.Shell.netId),out var p))return false;
            if(p.BeginFence(attempt))
            {
                if(p.Sender!=null)
                {
                    p.BeginRecovery(HostActionDeadline(conn,ActionNow));p.Sender.Dispose();p.Sender=null;
                    p.Stream.Subscription=checked(p.Stream.Subscription+1);p.Request=checked(p.Request+1);ActionFenceBaselines++;
                }
            }
            return !p.Recovering&&p.Sender?.Receipt.Applied==true;
        }
    }
}
