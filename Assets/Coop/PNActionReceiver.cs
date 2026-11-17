using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace ProjectN.Coop
{
    public enum PNActionReceiveState { Waiting, Assembling, ReadyToApply, Prepared, NeedsRecovery, Failed }
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
        public PNActionReceiver(PNActionReceiveBudget budget,PNActionStream stream,long request,double deadline,double maximumRecoverySeconds=3)
        {this.budget=budget??throw new ArgumentNullException(nameof(budget));stream.Validate();if(request<=0)throw new ArgumentOutOfRangeException();PNTimeMath.Nonnegative(deadline);PNTimeMath.Nonnegative(maximumRecoverySeconds);if(maximumRecoverySeconds==0)throw new ArgumentOutOfRangeException();recoverySeconds=maximumRecoverySeconds;Stream=stream;Request=request;Deadline=deadline;}
        public void Reset(PNActionStream stream,long request,double originalDeadline)
        {
            if(disposed)throw new ObjectDisposedException(nameof(PNActionReceiver));stream.Validate();PNTimeMath.Nonnegative(originalDeadline);
            if(stream.Subscription<=Stream.Subscription||request<=Request)throw new InvalidOperationException("Action reset fence");
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
                return (requiredCount,latestCount,ignoredCount);
            }
            var records=PNActionWire.Decode(p.Body);if(records.Count>256)throw new FormatException("Action batch count");
            // One retention pass per atomic batch, not an allocation of the
            // whole run dictionary for every individual U/R record.
            target.Prune(p.T);
            foreach(var token in records){var a=token as JArray;PNActionState.Record(a);int tag=(int)a[0];double stamp=tag==0?(double)a[3][6]:tag==1?(double)a[3]:tag==4?(double)a[5]:(double)a[tag==2?5:6][0];if(stamp>p.T||stamp<target.Floor)throw new FormatException("Action capture/coverage");if(!target.Apply(a,false))ignoredCount++;else if(tag==1)latestCount++;else requiredCount++;if(target.ResidentCharge>ModelLimit)throw new FormatException("Action model bound");}
            return (requiredCount,latestCount,ignoredCount);
        }
        void Commit(PNActionState.Decoder candidate)
        {int charge=candidate.ResidentCharge;if(charge>ModelLimit)throw new FormatException("Action model bound");budget.Take(charge);var old=modelCharge;model=candidate;modelCharge=charge;budget.Return(old);}
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
                var prior=model;Commit(candidate);
                if(prior.Identity!=null){UnknownCoverageResets++;foreach(var pair in prior.Runs){if(candidate.Runs.ContainsKey(pair.Key))continue;if(!pair.Value.Ended)UnknownActiveRetirements++;else if(selectedPresentation.HasValue){if(pair.Value.T<selectedPresentation.Value)KnownExpiredSkipped++;else KnownPotentiallyVisibleSkipped++;}}}
                ClearStage();State=PNActionReceiveState.Prepared;d.Kind=3;d.Total=d.Count=d.Index=0;d.Body=Array.Empty<byte>();return d;
            }
            catch(Exception e){Fault(e.Message);return null;}
            finally{if(reserved)budget.Return(DecodeReserve);}
        }
        public void Dispose(){if(disposed)return;ClearStage();budget.Return(modelCharge);modelCharge=0;model=new PNActionState.Decoder();disposed=true;State=PNActionReceiveState.Failed;}
    }
}
