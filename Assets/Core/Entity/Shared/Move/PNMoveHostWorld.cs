using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EveEngine;
using EveEngine.Movement;

// One authoritative movement world shared by all peers. Transport supplies authenticated sender IDs.
// Enrollment is explicit; no gameplay entity enrolls itself.
public sealed class PNMoveHostWorld : IDisposable
{
    internal sealed class Actor
    {
        internal PNMove Move;
        internal MoveHostModel Model;
        internal bool Remote;
        internal Action Relocation;
        internal Exception Failure;
    }
    private sealed class Constraints : IMoveConstraints
    {
        private readonly PNMove move;
        internal Constraints(PNMove move) { this.move=move; }
        public bool Accept(MovementState previous,MovementState next,MoveMirror mirror,uint known)
        {
            if(mirror.Dead || mirror.Scale==0)return previous.Position.Near(next.Position,.002f) && next.Velocity.Near(default,.002f);
            if((mirror.Rights&MoveRights.Walk)==0 && !move.HasLegalNetworkMotion(mirror))
                return Math.Abs(previous.Position.X-next.Position.X)<.02f && Math.Abs(previous.Position.Z-next.Position.Z)<.02f && Math.Abs(next.Velocity.X)<.02f && Math.Abs(next.Velocity.Z)<.02f;
            return true; // Friends PvE: not speed/teleport anti-cheat. Skill legality belongs to host execution owners.
        }
    }
    private readonly EveSessionContext session;
    internal readonly Dictionary<MoveIdentity,Actor> Actors=new Dictionary<MoveIdentity,Actor>();
    public bool IsActive => !disposed && session.IsActive;
    public int TrackedCount=>Actors.Count;
    private bool disposed;
    public Exception SamplingFailure(MoveIdentity id) => Actors.TryGetValue(id,out var actor)?actor.Failure:null;
    private double lastSample=-1;
    private readonly int capacity;
    public PNMoveHostWorld(EveSessionContext session)
        :this(session,256){}
    public PNMoveHostWorld(EveSessionContext session,int capacity)
    {
        if(session==null || !session.IsActive || session.Role!=EveSessionRole.Host)throw new InvalidOperationException("Active host required.");
        if(capacity<1||capacity>4096)throw new ArgumentOutOfRangeException(nameof(capacity));this.capacity=capacity;
        this.session=session; session.Stopping+=Dispose;
    }
    public void Track(PNMove move,bool remoteOwned)
    {
        if(!IsActive || move==null || !move.NetworkUsable || move.GetComponent<EveEntityLoader>()?.LoadContext?.Session!=session)throw new InvalidOperationException("Published same-session Move required.");
        var identity=move.NetworkIdentity(session.SessionId);var entity=move.GetComponent<EveEntityIdentity>();
        if(Actors.ContainsKey(identity) || Actors.Count>=capacity || remoteOwned && (entity.OwnerId<=0 || !session.Admission.IsPeerAdmitted(entity.OwnerId)))throw new InvalidOperationException("Move enrollment/owner/capacity.");
        var actor=new Actor{Move=move,Remote=remoteOwned};
        var mirror=ReadMirror(actor,1);
        actor.Model=new MoveHostModel(identity,entity.OwnerId,mirror,move.ReadMovementState(),move.ReadMovementRecovery(),new Constraints(move)){Ready=true};
        Actors.Add(identity,actor);
        actor.Relocation=()=>actor.Model.Correct(move.ReadMovementState(),move.ReadMovementRecovery(),MoveCorrectionReason.Teleport);
        move.PoseRelocated+=actor.Relocation;
        if(remoteOwned)move.BindRemoteOwnerHost(actor.Model);
    }
    private MoveMirror ReadMirror(Actor actor,uint revision)
    {
        var move=actor.Move;var stat=move.GetComponent<PNStatReplication>();
        if(stat==null || !stat.CanCaptureReplication)throw new InvalidOperationException("Move needs committed shared Stat life.");
        // The shared typed Stat source owns lifeRevision; no legacy encoding is needed to refresh it.
        var life=stat.CaptureState();
        var control=move.GetComponent<PNControlManager>(); var clock=move.GetComponent<EveTimeScale>();
        var stats=move.GetComponent<EveStat>();var registry=session.Registry;
        MoveRights rights=MoveRights.None;
        if(move.BaseCanMove && control.State.IsAllowed(PNControlIds.Move))rights|=MoveRights.Walk;
        if(move.BaseCanJump && control.State.IsAllowed(PNControlIds.Jump))rights|=MoveRights.Jump;
        if(move.BaseCanFlip && control.State.IsAllowed(PNControlIds.FaceControl))rights|=MoveRights.FaceControl;
        if(move.BaseCanFlip && control.State.IsAllowed(PNControlIds.FaceSkill))rights|=MoveRights.FaceSkill;
        // Carry effective permission IDs, not a closed enum of movement algorithms.
        var permissions=registry.ControlPermissions().Snapshot().Keys.Select(registry.ControlPermissions().ResolveIdentity).Distinct()
            .Where(control.State.IsAllowed).Select(id=>id.ToString());
        float scale=PNWorldGameplay.CanExecute(move)?(clock!=null && clock.isActiveAndEnabled?clock.Scale:1):0;
        float raw=clock!=null && clock.isActiveAndEnabled?clock.RawScale:1;
        return new MoveMirror(move.NetworkIdentity(session.SessionId),revision,life.LifeRevision,life.Dead,rights,
            (float)stats.Evaluate(EveBuiltInContentModule.MoveSpeed),(float)stats.Evaluate(EveBuiltInContentModule.JumpSpeed),scale,raw,permissions);
    }
    public void Untrack(MoveIdentity id)
    {
        if(!Actors.TryGetValue(id,out var actor))return;
        Actors.Remove(id);actor.Move.PoseRelocated-=actor.Relocation;actor.Move.DetachNetwork();actor.Model.Dispose();
    }
    public MoveReportResult Receive(MoveMessage message,long authenticatedSender,double hostTime)
    {
        if(!IsActive || !session.Admission.IsPeerAdmitted(authenticatedSender))return MoveReportResult.WrongOwner;
        if(!(message is MovePoseReport) && !(message is MoveBaselineRequest))return MoveReportResult.Malformed;
        if(!Actors.TryGetValue(message.Identity,out var actor) || !actor.Remote)return MoveReportResult.WrongIdentity;
        if(!actor.Move.NetworkUsable || actor.Move.HostMovement!=actor.Model)return MoveReportResult.NotReady;
        if(actor.Move.GetComponent<PNStatReplication>()?.CanCaptureReplication!=true)return MoveReportResult.NotReady;
        try { Refresh(actor);actor.Failure=null; }
        catch(Exception error) { actor.Failure=error;return MoveReportResult.NotReady; }
        if(message is MoveBaselineRequest request)return actor.Model.RequestBaseline(request,authenticatedSender)?MoveReportResult.Accepted:MoveReportResult.Stale;
        var report=(MovePoseReport)message;
        if(!PNWorldGameplay.CanExecute(actor.Move))return MoveReportResult.NotReady;
        var result=actor.Model.Receive(report,authenticatedSender,hostTime);
        if(result==MoveReportResult.Accepted || result==MoveReportResult.Constraint)actor.Move.ApplyAcceptedHostPose();
        return result;
    }
    private void Refresh(Actor actor)
    {
        var old=actor.Model.Mirror;var next=ReadMirror(actor,MoveOrder.Next(old.Revision));
        if(!actor.Model.SetMirror(next))return;
        if(!actor.Remote)return;
        bool life=next.LifeRevision!=old.LifeRevision;
        bool hold=next.Dead || next.Scale==0;
        bool walkLock=(next.Rights&MoveRights.Walk)==0 && !actor.Move.HasLegalNetworkMotion(next);
        if(hold || walkLock || life)
        {
            var state=actor.Model.State;
            var recovery=old.Scale>0 ? new MoveRecovery(new MoveVector(state.Velocity.X/old.Scale,state.Velocity.Y/old.Scale,state.Velocity.Z/old.Scale),default) : actor.Model.LastCorrection.Recovery;
            if(next.Dead)recovery=default;
            var velocity=hold?default:new MoveVector(0,state.Velocity.Y,0);
            var corrected=state.WithVelocity(velocity);
            var correction=actor.Model.Correct(corrected,recovery,life?MoveCorrectionReason.Death:MoveCorrectionReason.Constraint);
            actor.Move.NotifyMotionCorrection(correction,next);
            actor.Move.ApplyAcceptedHostPose();
        }
    }
    // Caller runs at a drained mutation boundary after producer/physics advancement, in host seconds.
    public bool Advance(double hostTime)
    {
        if(!IsActive)return false;
        if(double.IsNaN(hostTime) || double.IsInfinity(hostTime) || hostTime<0 || hostTime<lastSample)throw new ArgumentOutOfRangeException(nameof(hostTime));
        foreach(var pair in Actors.ToArray())
        {
            var actor=pair.Value;
            if(!actor.Move.NetworkLifetimeActive) { Untrack(pair.Key);continue; }
            if(!actor.Move.NetworkUsable) { actor.Model.Ready=false;continue; }
            bool resume=actor.Remote && !actor.Model.Ready;
            uint correctionBefore=actor.Model.LastCorrection.Id;
            if(actor.Remote && actor.Move.HostMovement!=actor.Model)actor.Move.BindRemoteOwnerHost(actor.Model);
            if(!actor.Remote && !actor.Model.Ready)
            { actor.Model.Ready=true;actor.Model.Correct(actor.Move.ReadMovementState(),actor.Move.ReadMovementRecovery(),MoveCorrectionReason.Baseline); }
            if(actor.Move.GetComponent<PNStatReplication>()?.CanCaptureReplication!=true)continue;
            try
            {
                Refresh(actor);
                if(resume)
                {
                    if(actor.Model.LastCorrection.Id==correctionBefore)actor.Model.Rebaseline();
                    else actor.Model.Ready=true;
                    actor.Move.ApplyAcceptedHostPose();
                }
                if(!actor.Remote)actor.Model.ObserveHostState(actor.Move.ReadMovementState());actor.Failure=null;
            }
            catch(Exception error) { actor.Failure=error; }
        }
        if(lastSample>=0 && hostTime-lastSample<.05)return false;lastSample=hostTime;
        return true;
    }
    public MoveCorrection Relocate(MoveIdentity id,MovementState state,MoveRecovery recovery)
    {
        if(!IsActive || !Actors.TryGetValue(id,out var actor))throw new InvalidOperationException();
        var correction=actor.Model.Correct(state,recovery,MoveCorrectionReason.Teleport);
        actor.Move.ApplyHostRelocation(correction,actor.Model.Mirror);return correction;
    }
    public MoveHostModel TypedModel(MoveIdentity identity)=>Actors.TryGetValue(identity,out var actor)?actor.Model:null;
    public void Dispose()
    {
        if(disposed)return;disposed=true;session.Stopping-=Dispose;
        foreach(var id in Actors.Keys.ToArray())Untrack(id);
    }
}
