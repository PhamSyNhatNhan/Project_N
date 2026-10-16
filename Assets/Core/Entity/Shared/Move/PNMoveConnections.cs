using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EveEngine;
using EveEngine.Movement;

// One authoritative movement world shared by all peers. Transport supplies authenticated sender IDs.
// Enrollment is explicit, like EveActorHostConnection; no connection is created by a gameplay entity.
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
    private bool disposed;
    public Exception LastProtocolError { get; private set; }
    public Exception SamplingFailure(MoveIdentity id) => Actors.TryGetValue(id,out var actor)?actor.Failure:null;
    private double lastSample=-1;
    public PNMoveHostWorld(EveSessionContext session)
    {
        if(session==null || !session.IsActive || session.Role!=EveSessionRole.Host)throw new InvalidOperationException("Active host required.");
        this.session=session; session.Stopping+=Dispose;
    }
    public void Track(PNMove move,bool remoteOwned)
    {
        if(!IsActive || move==null || !move.IsReady || move.GetComponent<EveEntityLoader>()?.LoadContext?.Session!=session)throw new InvalidOperationException("Published same-session Move required.");
        var identity=move.NetworkIdentity(session.SessionId);var entity=move.GetComponent<EveEntityIdentity>();
        if(Actors.ContainsKey(identity) || Actors.Count>=256 || remoteOwned && (entity.OwnerId<=0 || !session.Coop.IsPeerAdmitted(entity.OwnerId)))throw new InvalidOperationException("Move enrollment/owner/capacity.");
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
        float scale=clock!=null && clock.isActiveAndEnabled?clock.Scale:1;
        float raw=clock!=null && clock.isActiveAndEnabled?clock.RawScale:1;
        return new MoveMirror(move.NetworkIdentity(session.SessionId),revision,life.LifeRevision,life.Dead,rights,
            (float)stats.Evaluate(EveBuiltInContentModule.MoveSpeed),(float)stats.Evaluate(EveBuiltInContentModule.JumpSpeed),scale,raw,permissions);
    }
    public void Untrack(MoveIdentity id)
    {
        if(!Actors.TryGetValue(id,out var actor))return;
        Actors.Remove(id);actor.Move.PoseRelocated-=actor.Relocation;actor.Move.DetachNetwork();actor.Model.Dispose();
    }
    public MoveReportResult Receive(byte[] bytes,long authenticatedSender,double hostTime)
    {
        if(!IsActive || !session.Coop.IsPeerAdmitted(authenticatedSender))return MoveReportResult.WrongOwner;
        MoveMessage message;
        try { message=MoveCodec.Decode(bytes) as MoveMessage; }
        catch(FormatException error) { LastProtocolError=error;return MoveReportResult.Malformed; }
        if(!(message is MovePoseReport) && !(message is MoveBaselineRequest))return MoveReportResult.Malformed;
        if(!Actors.TryGetValue(message.Identity,out var actor) || !actor.Remote)return MoveReportResult.WrongIdentity;
        if(!actor.Move.NetworkUsable || actor.Move.HostMovement!=actor.Model)return MoveReportResult.NotReady;
        try { Refresh(actor);actor.Failure=null; }
        catch(Exception error) { actor.Failure=error;return MoveReportResult.NotReady; }
        if(message is MoveBaselineRequest request)return actor.Model.RequestBaseline(request,authenticatedSender)?MoveReportResult.Accepted:MoveReportResult.Stale;
        var report=(MovePoseReport)message;
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
    public PNMoveHostConnection Connect(long admittedPeer)
    { if(!IsActive || !session.Coop.IsPeerAdmitted(admittedPeer))throw new InvalidOperationException("Admitted peer required.");return new PNMoveHostConnection(this,admittedPeer); }
    public void Dispose()
    {
        if(disposed)return;disposed=true;session.Stopping-=Dispose;
        foreach(var id in Actors.Keys.ToArray())Untrack(id);
    }
}
// B: latest pose per presence. D: full mirror + correction pair. A refused offered batch is immutable.
// Storage bounded by 256 enrolled actors plus one offered batch; no gameplay tick staggering.
public sealed class PNMoveHostConnection : IDisposable
{
    private readonly PNMoveHostWorld world;
    private readonly long peer;
    private readonly Dictionary<MoveIdentity,byte[][]> latest=new Dictionary<MoveIdentity,byte[][]>();
    private KeyValuePair<MoveIdentity,byte[][]>[] pending;
    private double lastTime=-1;
    private bool disposed;
    internal PNMoveHostConnection(PNMoveHostWorld world,long peer) { this.world=world;this.peer=peer; }
    public int PendingActors => latest.Count+(pending?.Length??0);
    public void Advance(double hostTime)
    {
        if(disposed || !world.IsActive)return;
        if(double.IsNaN(hostTime) || double.IsInfinity(hostTime) || hostTime<0 || hostTime<lastTime)throw new ArgumentOutOfRangeException(nameof(hostTime));
        if(lastTime>=0 && hostTime-lastTime<.05)return;lastTime=hostTime;
        foreach(var stale in latest.Keys.Where(id=>!world.Actors.ContainsKey(id)).ToArray())latest.Remove(stale);
        foreach(var pair in world.Actors)
        {
            var actor=pair.Value;if(!actor.Move.NetworkUsable || !actor.Model.Ready)continue;
            var model=actor.Model;
            latest[pair.Key]=actor.Remote && model.Owner==peer ? new[]{MoveCodec.Encode(model.Mirror),MoveCodec.Encode(model.LastCorrection)} : new[]{MoveCodec.Encode(model.Sample(hostTime))};
        }
    }
    public bool Send(Func<byte[][],bool> accept)
    {
        if(disposed || !world.IsActive)return false;if(accept==null)throw new ArgumentNullException(nameof(accept));
        if(pending==null) { if(latest.Count==0)return false;pending=latest.ToArray();latest.Clear(); }
        var bytes=pending.SelectMany(pair=>pair.Value).Select(b=>(byte[])b.Clone()).ToArray();
        if(!accept(bytes))return false;pending=null;return true;
    }
    public void Dispose() { disposed=true;latest.Clear();pending=null; }
}
public sealed class PNMoveClientConnection : IDisposable
{
    private sealed class RequestCounter { internal uint Value; }
    private static readonly ConditionalWeakTable<EveSessionContext,RequestCounter> requestCounters=new ConditionalWeakTable<EveSessionContext,RequestCounter>();
    private readonly EveSessionContext session;
    private readonly Guid host;
    private readonly Dictionary<MoveIdentity,PNMove> actors=new Dictionary<MoveIdentity,PNMove>();
    private readonly Dictionary<MoveIdentity,Action<byte[]>> subscriptions=new Dictionary<MoveIdentity,Action<byte[]>>();
    private readonly Dictionary<MoveIdentity,byte[]> reports=new Dictionary<MoveIdentity,byte[]>();
    private readonly Dictionary<MoveIdentity,byte[]> baselineRequests=new Dictionary<MoveIdentity,byte[]>();
    private byte[][] pending;
    private bool disposed;
    public Exception LastError { get; private set; }
    public PNMoveClientConnection(EveSessionContext session,Guid admittedHost)
    {
        if(session==null || !session.IsActive || session.Role!=EveSessionRole.Client || !session.LocalOwnerId.HasValue || session.Coop.AdmittedHostSession!=admittedHost || admittedHost==Guid.Empty)
            throw new InvalidOperationException("Trusted owner and host admission required.");
        this.session=session;host=admittedHost;session.Stopping+=Dispose;
    }
    public void Bind(PNMove move)
    {
        if(disposed || !session.IsActive || move==null || move.GetComponent<EveEntityLoader>()?.LoadContext?.Session!=session || !move.NetworkUsable || actors.Count>=256)throw new InvalidOperationException("Published exact session actor required.");
        var id=move.NetworkIdentity(host);if(actors.ContainsKey(id))throw new InvalidOperationException("Already bound.");
        if(move.GetComponent<EveEntityIdentity>().OwnerId==session.LocalOwnerId.Value)
        {
            move.BindOwnerMovement(host);Action<byte[]> listener=bytes=>reports[id]=(byte[])bytes.Clone();
            move.OwnerPoseReport+=listener;subscriptions.Add(id,listener);
            var counter=requestCounters.GetValue(session,_=>new RequestCounter());counter.Value=MoveOrder.Next(counter.Value);
            move.OwnerMovement.ExpectBaseline(counter.Value);
            baselineRequests[id]=MoveCodec.Encode(new MoveBaselineRequest(id,counter.Value));
            reports[id]=baselineRequests[id];
        }
        else move.BindObserverMovement(host);
        actors.Add(id,move);
    }
    public bool Receive(byte[] bytes)
    {
        if(disposed || !session.IsActive)return false;
        try
        {
            var message=MoveCodec.Decode(bytes);var id=message is MoveMirror m?m.Identity:((MoveMessage)message).Identity;
            if(id.Session!=host || !actors.TryGetValue(id,out var move) || !move.NetworkUsable)return false;
            if(move.NetworkRole==PNMoveNetworkRole.Owner)return move.ReceiveOwnerMessage(message);
            return message is MovePoseState state && move.ObserverMovement!=null && move.ObserverMovement.Receive(state,true);
        }
        catch(Exception error) { LastError=error;return false; }
    }
    public void AdvanceObservers(double estimatedHostTime)
    {
        if(disposed || !session.IsActive)return;
        foreach(var pair in actors.ToArray())
        {
            if(!pair.Value.NetworkUsable) { Unbind(pair.Key);continue; }
            if(pair.Value.NetworkRole==PNMoveNetworkRole.Observer)pair.Value.ApplyObserverPose(Math.Max(0,estimatedHostTime-.1));
        }
    }
    public bool SendReports(Func<byte[][],bool> accept)
    {
        if(disposed || !session.IsActive)return false;if(accept==null)throw new ArgumentNullException(nameof(accept));
        foreach(var pair in baselineRequests.ToArray())
            if(actors.TryGetValue(pair.Key,out var move) && move.OwnerMovement?.NeedsBaseline==true)reports[pair.Key]=pair.Value;
            else baselineRequests.Remove(pair.Key);
        if(pending==null) { if(reports.Count==0)return false;pending=reports.Values.Select(b=>(byte[])b.Clone()).ToArray();reports.Clear(); }
        if(!accept(pending.Select(b=>(byte[])b.Clone()).ToArray()))return false;pending=null;return true;
    }
    public void Unbind(MoveIdentity id)
    {
        if(!actors.TryGetValue(id,out var move))return;
        if(subscriptions.TryGetValue(id,out var subscription)) { move.OwnerPoseReport-=subscription;subscriptions.Remove(id); }
        reports.Remove(id);baselineRequests.Remove(id);actors.Remove(id);move.DetachNetwork();
    }
    public void Dispose()
    { if(disposed)return;disposed=true;session.Stopping-=Dispose;foreach(var id in actors.Keys.ToArray())Unbind(id);reports.Clear();baselineRequests.Clear();pending=null; }
}
