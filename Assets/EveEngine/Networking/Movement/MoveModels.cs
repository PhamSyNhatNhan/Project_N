using System;
using System.Collections.Generic;

namespace EveEngine.Movement
{
    public enum MoveReportResult { Accepted, NotReady, WrongIdentity, WrongOwner, Stale, Fenced, Constraint, Malformed }
    // Game adapter knows which active executions legally suppress walking. Never infer legality from client claims.
    public interface IMoveConstraints
    {
        bool Accept(MovementState previous, MovementState proposed, MoveMirror current, uint ownerMirror);
    }
    public sealed class MoveHostModel : IDisposable
    {
        private readonly struct HistorySample
        {
            internal readonly uint Sequence, Epoch;
            internal readonly MovementState State;
            internal HistorySample(uint sequence,uint epoch,MovementState state) { Sequence=sequence; Epoch=epoch; State=state; }
        }
        private readonly Queue<HistorySample> ring = new Queue<HistorySample>();
        private readonly int ringCapacity;
        private readonly IMoveConstraints constraints;
        private bool disposed;
        private uint lastReport, stateSequence;
        private uint lastBaselineRequest;
        public MoveIdentity Identity { get; }
        public long Owner { get; }
        public MoveMirror Mirror { get; private set; }
        public MoveCorrection LastCorrection { get; private set; }
        public uint Epoch => LastCorrection.Epoch;
        public MovementState State { get; private set; }
        public double LastAcceptedTime { get; private set; }
        public bool Ready { get; set; }
        public MoveHostModel(MoveIdentity id,long owner,MoveMirror mirror,MovementState baseline,MoveRecovery recovery,IMoveConstraints constraints,int ringCapacity=64)
        {
            id.Validate(); baseline.Validate();
            if(owner<0 || mirror==null || !id.Equals(mirror.Identity) || ringCapacity<1 || ringCapacity>1024)throw new ArgumentException();
            Identity=id; Owner=owner; Mirror=mirror; State=baseline; this.constraints=constraints??throw new ArgumentNullException(nameof(constraints)); this.ringCapacity=ringCapacity;
            LastCorrection=new MoveCorrection(id,1,1,mirror.Revision,baseline,recovery,MoveCorrectionReason.Baseline);
        }
        // Idempotent retry: transports resend these immutable objects, not a new correction/epoch.
        public MoveReportResult Receive(MovePoseReport report,long authenticatedOwner,double now)
        {
            MoveOrder.Time(now);
            if(disposed || !Ready)return MoveReportResult.NotReady;
            if(report==null || !Identity.Equals(report.Identity))return MoveReportResult.WrongIdentity;
            if(authenticatedOwner!=Owner)return MoveReportResult.WrongOwner;
            if(lastReport!=0 && !MoveOrder.Newer(report.Sequence,lastReport))return MoveReportResult.Stale;
            if(report.Epoch!=Epoch || report.CorrectionId!=LastCorrection.Id || MoveOrder.Newer(report.MirrorRevision,Mirror.Revision))return MoveReportResult.Fenced;
            // Mark processed even on rejection: replay cannot generate fresh correction storms.
            lastReport=report.Sequence;
            if(!constraints.Accept(State,report.State,Mirror,report.MirrorRevision))
            {
                Correct(State,RecoveryFor(State),MoveCorrectionReason.Constraint);
                return MoveReportResult.Constraint;
            }
            State=report.State; LastAcceptedTime=now;
            ring.Enqueue(new HistorySample(report.Sequence,Epoch,State)); while(ring.Count>ringCapacity)ring.Dequeue();
            return MoveReportResult.Accepted;
        }
        public bool SetMirror(MoveMirror next)
        {
            if(disposed)throw new ObjectDisposedException(nameof(MoveHostModel));
            if(next==null || !Identity.Equals(next.Identity))throw new ArgumentException();
            if(Mirror.SameValues(next))return false;
            if(!MoveOrder.Newer(next.Revision,Mirror.Revision) || next.LifeRevision<Mirror.LifeRevision)throw new ArgumentException("Mirror must advance.");
            Mirror=next; return true;
        }
        public MoveCorrection Correct(MovementState state,MoveRecovery recovery,MoveCorrectionReason reason)
        {
            if(disposed)throw new ObjectDisposedException(nameof(MoveHostModel));
            var correction=new MoveCorrection(Identity,MoveOrder.Next(LastCorrection.Id),MoveOrder.Next(Epoch),Mirror.Revision,state,recovery,reason,lastReport,lastBaselineRequest);
            State=state; ring.Clear(); return LastCorrection=correction;
        }
        private MoveRecovery RecoveryFor(MovementState state) => Mirror.Scale>0?
            new MoveRecovery(new MoveVector(state.Velocity.X/Mirror.Scale,state.Velocity.Y/Mirror.Scale,state.Velocity.Z/Mirror.Scale),LastCorrection.Recovery.ResumeAngularVelocity,!state.Grounded && state.VerticalView==1):LastCorrection.Recovery;
        public void ObserveHostState(MovementState state)
        { if(disposed || !Ready)throw new InvalidOperationException(); state.Validate(); State=state; }
        public bool RequestBaseline(MoveBaselineRequest request,long authenticatedOwner)
        {
            if(disposed || request==null || authenticatedOwner!=Owner || !Identity.Equals(request.Identity) || lastBaselineRequest!=0 && !MoveOrder.Newer(request.RequestId,lastBaselineRequest))return false;
            lastBaselineRequest=request.RequestId;Rebaseline();return true;
        }
        public MoveCorrection Rebaseline() { var correction=Correct(State,RecoveryFor(State),MoveCorrectionReason.Baseline);Ready=true;return correction; }
        // Reserved Skill anchor operation. Later accepted cast fencing stays with the Skill adapter.
        public MoveCorrection RejectBranch(uint startPoseSequence,bool supersededByAcceptedCast)
        {
            if(supersededByAcceptedCast)return null;
            MovementState anchor=State;
            bool found=false;
            foreach(var entry in ring)
                if(entry.Epoch==Epoch && MoveOrder.AtLeast(startPoseSequence,entry.Sequence)) { anchor=entry.State; found=true; }
            var recovery=found ? RecoveryFor(anchor) : RecoveryFor(State);
            return Correct(anchor,recovery,MoveCorrectionReason.RejectedBranch);
        }
        // No integration on starvation. Sample current accepted/host-controlled state at a declared boundary.
        public MovePoseState Sample(double hostTime)
        {
            if(disposed || !Ready)throw new InvalidOperationException("Move host not Ready.");
            stateSequence=MoveOrder.Next(stateSequence);
            return new MovePoseState(Identity,stateSequence,Epoch,hostTime,State);
        }
        public void Dispose() { disposed=true; Ready=false; ring.Clear(); }
    }
    public sealed class MoveOwnerModel : IDisposable
    {
        private MoveCorrection pending;
        private uint sequence;
        private bool ready, disposed, applying, needsBaseline=true;
        private long activation;
        private uint expectedBaseline;
        private uint minimumLifeCorrectionMirror;
        public MoveIdentity Identity { get; }
        public MoveMirror Mirror { get; private set; }
        public uint AppliedCorrection { get; private set; }
        public uint Epoch { get; private set; }
        public bool NeedsBaseline => needsBaseline;
        public bool CanWrite => !disposed && ready && !needsBaseline && !applying && pending==null && Mirror!=null && !Mirror.Dead;
        public MoveOwnerModel(MoveIdentity id) { id.Validate(); Identity=id; }
        public void ExpectBaseline(uint request)
        { if(request==0 || disposed)throw new InvalidOperationException();expectedBaseline=request;needsBaseline=true;pending=null; }
        // Called only after entity Ready and trusted host admission by the adapter.
        public void SetReady(bool value)
        {
            if(disposed)return;
            if(!value) { activation++; ready=false; pending=null; Mirror=null; needsBaseline=true; return; }
            ready=true;
        }
        public bool ReceiveMirror(MoveMirror mirror,bool authenticatedHost)
        {
            if(disposed || !ready || !authenticatedHost || mirror==null || !Identity.Equals(mirror.Identity))return false;
            if(Mirror!=null && (!MoveOrder.Newer(mirror.Revision,Mirror.Revision) || mirror.LifeRevision<Mirror.LifeRevision))return false;
            if(Mirror!=null && mirror.LifeRevision!=Mirror.LifeRevision)
            {
                needsBaseline=true;minimumLifeCorrectionMirror=mirror.Revision;
                if(pending!=null && !MoveOrder.AtLeast(pending.MirrorRevision,minimumLifeCorrectionMirror))pending=null;
            }
            Mirror=mirror; return true;
        }
        public bool ReceiveCorrection(MoveCorrection correction,bool authenticatedHost)
        {
            if(disposed || !ready || !authenticatedHost || correction==null || !Identity.Equals(correction.Identity))return false;
            if(needsBaseline && expectedBaseline!=0 && correction.BaselineRequestId!=expectedBaseline)return false;
            if(minimumLifeCorrectionMirror!=0 && !MoveOrder.AtLeast(correction.MirrorRevision,minimumLifeCorrectionMirror))return false;
            if(AppliedCorrection!=0 && !MoveOrder.Newer(correction.Id,AppliedCorrection) && !(needsBaseline && correction.Id==AppliedCorrection))return false;
            if(Epoch!=0 && !MoveOrder.AtLeast(correction.Epoch,Epoch))return false;
            if(pending!=null && !MoveOrder.Newer(correction.Id,pending.Id))return false;
            pending=correction; return true;
        }
        // Explicit next-fixed boundary. Writes are fenced before owner callbacks or body application.
        // Failure leaves at most one pending correction, no ack, and asks for a coherent baseline.
        public bool ApplyBoundary(Action<MoveCorrection,MoveMirror> apply)
        {
            if(disposed || !ready || applying || pending==null || Mirror==null || !MoveOrder.AtLeast(Mirror.Revision,pending.MirrorRevision))return false;
            if(apply==null)throw new ArgumentNullException(nameof(apply));
            var selected=pending; var mirror=Mirror; long before=activation;
            applying=true;
            try
            {
                apply(selected,mirror);
                if(disposed || !ready || before!=activation)return false;
                AppliedCorrection=selected.Id; Epoch=selected.Epoch; needsBaseline=false;
                expectedBaseline=0;
                if(sequence==0 || MoveOrder.Newer(selected.ReportSequenceFloor,sequence))sequence=selected.ReportSequenceFloor;
                if(ReferenceEquals(pending,selected))pending=null;
                return true;
            }
            catch { needsBaseline=true; throw; }
            finally { applying=false; }
        }
        public MovePoseReport Report(MovementState state)
        {
            if(!CanWrite)throw new InvalidOperationException("Move owner needs an applied coherent baseline.");
            sequence=MoveOrder.Next(sequence);
            return new MovePoseReport(Identity,sequence,Epoch,Mirror.Revision,AppliedCorrection,state);
        }
        public void Dispose() { disposed=true; ready=false; pending=null; Mirror=null; needsBaseline=true; }
    }
    public sealed class MoveObserverModel : IDisposable
    {
        private readonly List<MovePoseState> samples=new List<MovePoseState>();
        private bool ready,disposed;
        public MoveIdentity Identity { get; }
        private uint lastSequence,lastEpoch;
        public uint LastSequence => lastSequence;
        public uint LastEpoch => lastEpoch;
        public int Count => samples.Count;
        public MoveObserverModel(MoveIdentity id,uint sequenceFloor=0,uint epochFloor=0) { id.Validate(); Identity=id;lastSequence=sequenceFloor;lastEpoch=epochFloor; }
        public void SetReady(bool value) { if(disposed)return; ready=value; if(!value)samples.Clear(); }
        public bool Receive(MovePoseState state,bool authenticatedHost)
        {
            if(disposed || !ready || !authenticatedHost || state==null || !Identity.Equals(state.Identity))return false;
            if(lastSequence!=0 && !MoveOrder.Newer(state.Sequence,lastSequence))return false;
            if(lastEpoch!=0 && !MoveOrder.AtLeast(state.Epoch,lastEpoch))return false;
            if(state.Epoch!=lastEpoch)samples.Clear();
            else if(samples.Count>0 && state.SampleTime<samples[samples.Count-1].SampleTime)return false;
            lastSequence=state.Sequence; lastEpoch=state.Epoch;
            samples.Add(state); if(samples.Count>64)samples.RemoveAt(0); return true;
        }
        // Caller maps local monotonic time to host seconds; no hidden synchronized-clock assumption.
        public bool TrySample(double hostRenderTime,out MovementState state)
        {
            MoveOrder.Time(hostRenderTime); state=default;
            if(disposed || !ready || samples.Count==0)return false;
            var first=samples[0]; state=first.State;
            for(int i=1;i<samples.Count;i++)
            {
                var next=samples[i];
                if(hostRenderTime<next.SampleTime)
                {
                    double span=next.SampleTime-first.SampleTime;
                    float t=span<=0?1:(float)Math.Max(0,Math.Min(1,(hostRenderTime-first.SampleTime)/span));
                    state=new MovementState(MoveVector.Lerp(first.State.Position,next.State.Position,t),MoveVector.Lerp(first.State.Velocity,next.State.Velocity,t),first.State.Facing,first.State.Grounded,first.State.VerticalView);
                    return true;
                }
                first=next; state=next.State;
            }
            return true; // Starvation holds last position, including position-driven zero-velocity motion.
        }
        public void Dispose() { disposed=true; ready=false; samples.Clear(); }
    }
}
