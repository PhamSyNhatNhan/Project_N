using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace ProjectN.Coop
{
    public enum PNActionReceiveState { Waiting, Assembling, ReadyToApply, Prepared, NeedsRecovery, Failed }
    public enum PNActionPresentationCoverage { Unknown, Empty, Active }

    public readonly struct PNObservedAction
    {
        public readonly Guid Source;
        public readonly PNActionRunKey Key;
        public readonly string EntryId, DataId, ActionId;
        public readonly long Capability;
        public readonly double DirectionX, DirectionZ, Start, Progress, Rate, PresentationScale;
        // G8c: the whole frozen context (direction, position, target ids); `Capability` is its mask.
        public readonly PNActionRunContext Context;
        // PV2: the run's presentation facts known so far (append only; the view takes the new ones and holds those from the future).
        public readonly IReadOnlyList<PNActionFact> Facts;

        internal PNObservedAction(Guid source, PNActionState.Run run, double progress, double rate)
        {
            Source=source;
            Key=new PNActionRunKey((long)run.K[0],(long)run.K[1]);
            EntryId=(string)run.D[0]; DataId=(string)run.D[1]; ActionId=(string)run.D[2];
            Capability=(long)run.D[3];
            DirectionX=(double)run.D[4]; DirectionZ=(double)run.D[5]; Start=(double)run.D[6];
            PresentationScale=run.D.Count>=8?(double)run.D[7]:1;
            Context=ReadContext(run.D,(int)Capability);
            Progress=progress; Rate=rate;
            Facts=run.Facts.Count==0?(IReadOnlyList<PNActionFact>)Array.Empty<PNActionFact>():run.Facts.ToArray();
        }
        static PNActionRunContext ReadContext(JArray d,int mask)
        {
            double x=(double)d[4],z=(double)d[5];
            bool extra=(mask&(PNActionRunContext.PositionBit|PNActionRunContext.TargetsBit))!=0;
            double px=0,py=0,pz=0;var ids=new List<long>();
            if(extra)
            {
                var ext=(JArray)d[8];int at=0;
                if((mask&PNActionRunContext.PositionBit)!=0){px=(double)ext[0];py=(double)ext[1];pz=(double)ext[2];at=3;}
                for(int i=at;i<ext.Count;i++)ids.Add((long)ext[i]);
            }
            return PNActionRunContext.Of((mask&PNActionRunContext.DirectionBit)!=0,x,z,(mask&PNActionRunContext.PositionBit)!=0,px,py,pz,ids);
        }
    }
    public sealed class PNActionReceiveBudget
    {
        public int Used { get; private set; }
        public readonly int Limit;
        public Func<int,bool> AdditionalAdmission;
        public PNActionReceiveBudget(int limit=64*1024*1024){if(limit<1)throw new ArgumentOutOfRangeException();Limit=limit;}
        internal void Take(int n){if(n<0||n>Limit-Used||AdditionalAdmission!=null&&!AdditionalAdmission(n))throw new InvalidOperationException("Action receive budget");Used+=n;}
        internal void Return(int n){Used-=n;if(Used<0)throw new InvalidOperationException("Action charge underflow");}
    }
    // One explicitly bound subscription; caller authenticates/relevance-checks before Receive.
    public sealed class PNActionReceiver : IDisposable
    {
        const int ModelLimit=4*1024*1024,DecodeReserve=16*1024*1024,PendingLimit=262144;
        readonly PNActionReceiveBudget budget;
        readonly double recoverySeconds;
        PNActionState.Decoder model=new PNActionState.Decoder();
        PNActionState.Decoder priorPresentation;
        Guid committedSource, priorSource;
        int priorCharge;
        PNActionPacket? declaration;
        byte[][] parts;
        int received,stageCharge,modelCharge,pendingBytes;
        readonly List<PNActionPacket> pending=new List<PNActionPacket>();
        bool disposed;
        double lastTime;
        public PNActionStream Stream {get;private set;}
        public long Request {get;private set;}
        public double Deadline {get;private set;}
        public PNActionReceiveState State {get;private set;}
        public string Failure {get;private set;}
        public long KnownExpiredSkipped {get;private set;}
        public long KnownPotentiallyVisibleSkipped {get;private set;}
        public long UnknownCoverageResets {get;private set;}
        public long UnknownActiveRetirements {get;private set;}
        public long AppliedSequence=>model.N;
        public int RunCount=>model.Runs.Count;
        public double Progress(long cast,long ordinal)=>model.Runs[cast+":"+ordinal].P;

        // The caller supplies the stamp actually sampled by the actor's pose.
        // A null destination checks coverage without materializing run views.
        public PNActionPresentationCoverage ReadPresentation(double stamp, List<PNObservedAction> destination)
        {
            PNTimeMath.Nonnegative(stamp);
            destination?.Clear();
            var current=stamp<model.Floor?priorPresentation:model;
            if(disposed||State==PNActionReceiveState.Failed||current?.Identity==null||
                stamp<Math.Max(current.Floor,current.RetentionFloor)||stamp>current.CompleteThrough)
                return PNActionPresentationCoverage.Unknown;

            var result=PNActionPresentationCoverage.Empty;
            foreach(var run in current.Runs.Values)
            {
                if(stamp<(double)run.D[6]||run.Ended&&stamp>=run.T)continue;
                if(stamp<run.Floor||stamp>run.T)
                {
                    destination?.Clear();
                    return PNActionPresentationCoverage.Unknown;
                }
                result=PNActionPresentationCoverage.Active;
                if(destination==null)continue;
                int index=run.Anchors.Count-1;
                while(run.Anchors[index][0]>stamp)index--;
                var anchor=run.Anchors[index];
                destination.Add(new PNObservedAction(current==model?committedSource:priorSource,
                    run,anchor[1]+(stamp-anchor[0])*anchor[2],anchor[2]));
            }
            return result;
        }

        internal double BaselinePresentation => model.Identity==null?double.PositiveInfinity:model.Floor;
        internal PNActionRunEnd? PresentationEnd(double stamp, PNActionRunKey key)
        {
            var current=stamp<model.Floor?priorPresentation:model;
            if(current!=null&&current.Runs.TryGetValue(key.Cast+":"+key.Ordinal,out var run)&&run.Ended&&stamp>=run.T)
                return run.EndKind;
            return null;
        }
        internal void RetirePastPresentation(double stamp)
        {
            if(stamp>=model.Floor)ClearPriorPresentation();
        }
        void ClearPriorPresentation()
        {
            priorPresentation=null;
            priorSource=default;
            budget.Return(priorCharge);
            priorCharge=0;
        }
        public PNActionReceiver(PNActionReceiveBudget budget,PNActionStream stream,long request,double deadline,double maximumRecoverySeconds=3)
        {this.budget=budget??throw new ArgumentNullException(nameof(budget));stream.Validate();if(request<=0)throw new ArgumentOutOfRangeException();PNTimeMath.Nonnegative(deadline);PNTimeMath.Nonnegative(maximumRecoverySeconds);if(maximumRecoverySeconds==0)throw new ArgumentOutOfRangeException();recoverySeconds=maximumRecoverySeconds;Stream=stream;Request=request;Deadline=deadline;}
        public void Reset(PNActionStream stream,long request,double originalDeadline)
        {
            if(disposed)throw new ObjectDisposedException(nameof(PNActionReceiver));stream.Validate();PNTimeMath.Nonnegative(originalDeadline);
            if(stream.Subscription<=Stream.Subscription||request<=Request)throw new InvalidOperationException("Action reset fence");
            if(stream.Session!=Stream.Session||stream.NetId!=Stream.NetId||stream.Entity!=Stream.Entity||
                stream.Generation!=Stream.Generation||stream.Nonce!=Stream.Nonce||!stream.Scope.Equals(Stream.Scope))
            {
                ClearPriorPresentation();
                budget.Return(modelCharge);
                modelCharge=0;
                model=new PNActionState.Decoder();
                committedSource=default;
            }
            ClearStage();Stream=stream;Request=request;Deadline=State==PNActionReceiveState.Prepared?originalDeadline:Math.Min(Deadline,originalDeadline);State=PNActionReceiveState.Waiting;Failure=null;
        }
        // A request carries the last known source. A concurrent host rebuild may
        // resolve that provisional identity on the first authenticated baseline.
        public void ResolveSource(PNActionStream actual,long request)
        {
            actual.Validate();var expected=Stream;expected.Source=actual.Source;
            if(disposed||State!=PNActionReceiveState.Waiting||request!=Request||!expected.Equals(actual))throw new InvalidOperationException("Action provisional source fence");
            Stream=actual;
        }
        void ClearStage(){budget.Return(stageCharge);stageCharge=0;parts=null;declaration=null;pending.Clear();pendingBytes=received=0;}
        void Fault(string reason,bool expired=false){if(State==PNActionReceiveState.Prepared)Deadline=Math.Min(double.MaxValue,lastTime+recoverySeconds);ClearStage();Failure=reason;State=expired?PNActionReceiveState.Failed:PNActionReceiveState.NeedsRecovery;}
        public void Tick(double now){PNTimeMath.Nonnegative(now);if(now<lastTime)throw new InvalidOperationException("Action receiver clock");lastTime=now;if(!disposed&&State!=PNActionReceiveState.Prepared&&now>=Deadline)Fault("Action original deadline",true);}
        public (int Required,int Latest,int Ignored) LastLiveCommit { get; private set; }
        public bool Receive(PNActionPacket packet,double now)
        {
            LastLiveCommit=default;
            if(disposed)return false;Tick(now);if(State==PNActionReceiveState.Failed||State==PNActionReceiveState.NeedsRecovery)return false;
            if(!packet.Stream.Equals(Stream)||packet.Request!=Request)return false;
            try
            {
                PNActionWire.Header(packet);int size=packet.Kind>=2?0:Math.Min(PNActionWire.ChunkBytes,packet.Total-packet.Index*PNActionWire.ChunkBytes);
                if(packet.Body==null||packet.Body.Length!=size)throw new FormatException("Action body size");
                if(packet.Kind==0)
                {
                    if(State==PNActionReceiveState.Prepared)return false;
                    if(!declaration.HasValue){budget.Take(packet.Total+packet.Count*16);stageCharge+=packet.Total+packet.Count*16;declaration=packet;declaration=WithoutBody(packet);parts=new byte[packet.Count][];State=PNActionReceiveState.Assembling;}
                    var d=declaration.Value;if(!SameTransfer(d,packet))throw new FormatException("Action mixed baseline");
                    if(parts[packet.Index]!=null){if(!PNActionWire.Same(parts[packet.Index],packet.Body))throw new FormatException("Action duplicate conflict");return false;}
                    parts[packet.Index]=(byte[])packet.Body.Clone();if(++received==packet.Count)State=PNActionReceiveState.ReadyToApply;return true;
                }
                if(packet.Kind!=1)throw new FormatException("Action receiver kind");
                if(State==PNActionReceiveState.Prepared){ApplyLive(packet);return true;}
                if(pending.Count>=128||packet.Body.Length+128>PendingLimit-pendingBytes)throw new FormatException("Action post-cut queue");
                budget.Take(packet.Body.Length+128);stageCharge+=packet.Body.Length+128;pendingBytes+=packet.Body.Length+128;packet.Body=(byte[])packet.Body.Clone();packet.Digest=(byte[])packet.Digest.Clone();pending.Add(packet);return true;
            }
            catch(Exception e){Fault(e.Message);return false;}
        }
        static PNActionPacket WithoutBody(PNActionPacket p){p.Body=null;p.Digest=(byte[])p.Digest.Clone();return p;}
        public static bool SameTransfer(PNActionPacket a,PNActionPacket b)=>a.Stream.Equals(b.Stream)&&a.Request==b.Request&&a.Q==b.Q&&a.N==b.N&&a.T==b.T&&a.Total==b.Total&&a.Count==b.Count&&PNActionWire.Same(a.Digest,b.Digest);
        (int Required,int Latest,int Ignored) Live(PNActionState.Decoder target,PNActionPacket p)
        {
            if(!PNActionWire.Same(PNActionWire.Hash(p.Body),p.Digest))throw new FormatException("Action live digest");
            int requiredCount=0,latestCount=0,ignoredCount=0;
            var latest=PNActionWire.DecodeLatest(p.Body);
            if(latest!=null)
            {
                target.Prune(p.T);
                foreach(var value in latest){if(value.Stamp>p.T||value.Stamp<target.Floor)throw new FormatException("Action capture/coverage");if(target.ApplyLatest(value))latestCount++;else ignoredCount++;if(target.ResidentCharge>ModelLimit)throw new FormatException("Action model bound");}
                Certify(target,p);
                return (requiredCount,latestCount,ignoredCount);
            }
            var records=PNActionWire.Decode(p.Body);if(records.Count>256)throw new FormatException("Action batch count");
            // One retention pass per atomic batch, not an allocation of the
            // whole run dictionary for every individual U/R record.
            target.Prune(p.T);
            foreach(var token in records){var a=token as JArray;PNActionState.Record(a);int tag=(int)a[0];double stamp=tag==0?(double)a[3][6]:tag==1?(double)a[3]:tag==4?(double)a[5]:tag==5?(double)a[3]:(double)a[tag==2?5:6][0];if(stamp>p.T||stamp<target.Floor)throw new FormatException("Action capture/coverage");if(!target.Apply(a,false))ignoredCount++;else if(tag==1)latestCount++;else requiredCount++;if(target.ResidentCharge>ModelLimit)throw new FormatException("Action model bound");}
            Certify(target,p);
            return (requiredCount,latestCount,ignoredCount);
        }
        static void Certify(PNActionState.Decoder target, PNActionPacket packet)
        {
            if(!packet.CompleteThroughCapture)return;
            if(packet.N!=target.N)throw new FormatException("Action coverage sequence");
            target.CompleteThrough=Math.Max(target.CompleteThrough,packet.T);
        }
        void Commit(PNActionState.Decoder candidate, bool retainPrior=false)
        {
            int charge=candidate.ResidentCharge;
            if(charge>ModelLimit)throw new FormatException("Action model bound");
            budget.Take(charge);
            if(retainPrior)
            {
                ClearPriorPresentation();
                priorPresentation=model;
                priorSource=committedSource;
                priorCharge=modelCharge;
            }
            else budget.Return(modelCharge);
            model=candidate;
            modelCharge=charge;
        }
        void ApplyLive(PNActionPacket p)
        {budget.Take(DecodeReserve);try{var candidate=model.Clone();var result=Live(candidate,p);Commit(candidate);LastLiveCommit=result;}finally{budget.Return(DecodeReserve);}}
        // Calling Receive cannot produce an ACK. Explicit model apply is the boundary.
        public PNActionPacket? TryApply(double now,double? selectedPresentation=null)
        {
            if(disposed)return null;if(selectedPresentation.HasValue)PNTimeMath.Nonnegative(selectedPresentation.Value);Tick(now);if(State!=PNActionReceiveState.ReadyToApply)return null;
            bool reserved=false;
            try
            {
                budget.Take(DecodeReserve);reserved=true;var d=declaration.Value;var body=new byte[d.Total];int at=0;foreach(var part in parts){Buffer.BlockCopy(part,0,body,at,part.Length);at+=part.Length;}
                if(!PNActionWire.Same(PNActionWire.Hash(body),d.Digest))throw new FormatException("Action baseline digest");
                var b=PNActionWire.Decode(body);PNActionState.Array(b,6);PNActionState.Scope(b[1]);var scope=(JArray)b[1];
                if((long)b[2]!=d.Q||(double)b[3]!=d.T||(long)b[4]!=d.N||(string)scope[0]!=Stream.Session.ToString("N")||(string)scope[1]!=Stream.Scope.World.ToString("N")||(ulong)scope[2]!=Stream.Scope.Incarnation||(long)scope[3]!=Stream.Entity||(long)scope[4]!=Stream.Generation||(string)scope[5]!=Stream.Source.ToString("N"))throw new FormatException("Action baseline cut/scope");
                var candidate=new PNActionState.Decoder{Sub=0};candidate.Baseline(b,Stream.Subscription);
                foreach(var p in pending)Live(candidate,p);
                var prior=model;
                if(prior.Identity!=null&&candidate.Floor<prior.Floor)throw new FormatException("Action baseline presentation rollback");
                Commit(candidate,prior.Identity!=null&&(!selectedPresentation.HasValue||selectedPresentation.Value<d.T));
                committedSource=Stream.Source;
                if(selectedPresentation.HasValue&&selectedPresentation.Value>=d.T)ClearPriorPresentation();
                if(prior.Identity!=null){UnknownCoverageResets++;foreach(var pair in prior.Runs){if(candidate.Runs.ContainsKey(pair.Key))continue;if(!pair.Value.Ended)UnknownActiveRetirements++;else if(selectedPresentation.HasValue){if(pair.Value.T<selectedPresentation.Value)KnownExpiredSkipped++;else KnownPotentiallyVisibleSkipped++;}}}
                ClearStage();State=PNActionReceiveState.Prepared;d.Kind=3;d.Total=d.Count=d.Index=0;d.Body=Array.Empty<byte>();return d;
            }
            catch(Exception e){Fault(e.Message);return null;}
            finally{if(reserved)budget.Return(DecodeReserve);}
        }
        public void Dispose(){if(disposed)return;ClearStage();ClearPriorPresentation();budget.Return(modelCharge);modelCharge=0;model=new PNActionState.Decoder();disposed=true;State=PNActionReceiveState.Failed;}
    }
}
