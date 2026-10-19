using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using EveEngine.Movement;
using UnityEngine;

public enum PNMoveNetworkRole { Local, RemoteOwnerHost, Owner, Observer }

// Native body adapter. No gameplay authority, Stat/Control mutation or Skill admission is granted here.
public partial class PNMove
{
    public PNMoveNetworkRole NetworkRole { get; private set; }
    public bool NetworkUsable => loadContext!=null && loadContext.CanUse && loader!=null && loader.IsReady && body!=null && isActiveAndEnabled;
    internal bool NetworkLifetimeActive => loadContext!=null && loadContext.CanUse && loader!=null && loader.IsReady;
    public MoveOwnerModel OwnerMovement { get; private set; }
    public MoveObserverModel ObserverMovement { get; private set; }
    public MoveHostModel HostMovement { get; private set; }
    private bool networkKinematic, previousKinematic;
    private bool networkDetached;
    public bool IsPredictedOwner => NetworkRole==PNMoveNetworkRole.Owner;
    protected override bool CanExecuteMovement => !MovementHeld && (NetworkRole==PNMoveNetworkRole.Local ? base.CanExecuteMovement :
        IsPredictedOwner && !networkKinematic && loadContext!=null && loadContext.CanUse && OwnerMovement!=null && OwnerMovement.CanWrite);
    private float NetworkScale => OwnerMovement?.Mirror?.Scale ?? 0;
    private float NetworkMoveSpeed => OwnerMovement?.Mirror?.MoveSpeed ?? 0;
    private float NetworkJumpSpeed => OwnerMovement?.Mirror?.JumpSpeed ?? 0;
    protected override void ApplyVelocity(Vector2 velocity)
    {
        if(NetworkRole==PNMoveNetworkRole.RemoteOwnerHost || NetworkRole==PNMoveNetworkRole.Observer || IsPredictedOwner && networkKinematic)return;
        base.ApplyVelocity(velocity);
    }
    public bool CanWriteMotion => !MovementHeld && loadContext!=null && loadContext.CanUse && isActiveAndEnabled &&
        (NetworkRole==PNMoveNetworkRole.Local && loadContext.CanMutate || IsPredictedOwner && !networkKinematic && OwnerMovement!=null && OwnerMovement.CanWrite) && MovementTimeScale>0;
    private readonly List<PNMoveExecution> motionExecutions=new List<PNMoveExecution>();
    private ulong nextExecution;
    private bool motionTransition;
    private MoveIdentity observerHistoryIdentity;
    private uint observerSequenceFloor,observerEpochFloor;
    private bool NetworkAllows(EveId id)
    {
        if(MovementHeld)return false;
        if(!IsPredictedOwner)return true;
        var mirror=OwnerMovement?.Mirror;
        if(mirror==null || mirror.Dead)return false;
        MoveRights right=id==PNControlIds.Move?MoveRights.Walk:id==PNControlIds.Jump?MoveRights.Jump:id==PNControlIds.FaceControl?MoveRights.FaceControl:id==PNControlIds.FaceSkill?MoveRights.FaceSkill:MoveRights.None;
        return right==MoveRights.None ? mirror.AllowsMotion(id.ToString()) : (mirror.Rights&right)!=0;
    }
    private void RequireNetworkReady()
    { if(loadContext==null || !loadContext.CanUse || loader==null || !loader.IsReady || body==null || !isActiveAndEnabled)throw new InvalidOperationException("Published active Move required."); }
    public MoveIdentity NetworkIdentity(Guid hostSession) { RequireNetworkReady(); return new MoveIdentity(hostSession,loadContext.Identity.Id.Value,loadContext.Generation); }
    public void BindOwnerMovement(Guid hostSession)
    {
        RequireNetworkReady();
        if(loadContext.Session.Role!=EveSessionRole.Client || loadContext.Session.Admission.AdmittedHostSession!=hostSession ||
            !loadContext.Session.LocalOwnerId.HasValue || loadContext.Identity.OwnerId!=loadContext.Session.LocalOwnerId.Value)
            throw new InvalidOperationException("Admitted local owner required.");
        DetachNetwork(); networkDetached=false; NetworkRole=PNMoveNetworkRole.Owner;
        OwnerMovement=new MoveOwnerModel(NetworkIdentity(hostSession)); OwnerMovement.SetReady(true);
        SetNetworkKinematic();
    }
    public void BindObserverMovement(Guid hostSession)
    {
        RequireNetworkReady();
        if(loadContext.Session.Role!=EveSessionRole.Client || loadContext.Session.Admission.AdmittedHostSession!=hostSession)throw new InvalidOperationException("Admitted host required.");
        DetachNetwork(); networkDetached=false; NetworkRole=PNMoveNetworkRole.Observer;
        var id=NetworkIdentity(hostSession);
        ObserverMovement=new MoveObserverModel(id,id.Equals(observerHistoryIdentity)?observerSequenceFloor:0,id.Equals(observerHistoryIdentity)?observerEpochFloor:0); ObserverMovement.SetReady(true); SetNetworkKinematic();
    }
    public void BindRemoteOwnerHost(MoveHostModel model)
    {
        RequireNetworkReady();
        if(loadContext.Session.Role!=EveSessionRole.Host || model==null || !model.Identity.Equals(NetworkIdentity(loadContext.Session.SessionId)) || model.Owner!=loadContext.Identity.OwnerId)
            throw new InvalidOperationException("Exact remote owner model required.");
        DetachNetwork(); networkDetached=false; NetworkRole=PNMoveNetworkRole.RemoteOwnerHost; HostMovement=model; SetNetworkKinematic();
    }
    private void SetNetworkKinematic()
    { if(networkKinematic)return;previousKinematic=body.isKinematic; networkKinematic=true; if(!body.isKinematic) { body.linearVelocity=Vector3.zero; body.angularVelocity=Vector3.zero; body.isKinematic=true; } }
    public MovementState ReadMovementState()
    {
        RequireNetworkReady();
        if(NetworkRole==PNMoveNetworkRole.RemoteOwnerHost)return HostMovement.State;
        return new MovementState(Vector(body.position),Vector(body.linearVelocity),FacingDirection,IsGrounded,(byte)VerticalMotion);
    }
    public MoveRecovery ReadMovementRecovery()
    {
        RequireNetworkReady(); float scale=previousScale>0?previousScale:1;
        return new MoveRecovery(Vector(frozen?savedVelocity:body.linearVelocity/scale),Vector(frozen?savedAngularVelocity:body.angularVelocity/scale),AwaitingGroundDeparture);
    }
    public void ApplyAcceptedHostPose()
    {
        RequireNetworkReady(); if(NetworkRole!=PNMoveNetworkRole.RemoteOwnerHost)throw new InvalidOperationException();
        ApplyPose(HostMovement.State);
    }
    public bool ReceiveOwnerMessage(object message)
    {
        RequireNetworkReady(); if(!IsPredictedOwner)return false;
        if(message is MoveMirror mirror)return OwnerMovement.ReceiveMirror(mirror,true);
        if(message is MoveCorrection correction)return OwnerMovement.ReceiveCorrection(correction,true);
        return false;
    }
    // Sample represents the completed preceding physics step plus intervening Update producers,
    // after a pending correction, BEFORE this step's locomotion/gravity/physics integration.
    // One report for each actual FixedUpdate; host timestamps reception, no client time is trusted.
    public void NetworkFixedBoundary()
    {
        if(managedMovement && NetworkRole==PNMoveNetworkRole.Observer){try{ApplyInitialObserverBoundary();}catch(Exception error){ReportMovementFailure(error);}return;}
        if(!IsPredictedOwner || loadContext==null || !loadContext.CanUse || !isActiveAndEnabled || body==null)return;
        var selected=OwnerMovement;
        try { selected.ApplyBoundary((correction,mirror)=>
        {
            NotifyMotionCorrection(correction,mirror);
            if(!ReferenceEquals(OwnerMovement,selected) || !NetworkUsable)return;
            ApplyPose(correction.State);
            if(networkKinematic) { body.isKinematic=previousKinematic;networkKinematic=false; }
            SyncPhysicsScale(mirror.Scale);
            savedVelocity=Unity(correction.Recovery.ResumeVelocity); savedAngularVelocity=Unity(correction.Recovery.ResumeAngularVelocity);
            holdRecovery=correction.Recovery;
            if(mirror.Dead) { savedVelocity=Vector3.zero; savedAngularVelocity=Vector3.zero; }
            if(!frozen)
            {
                // Recovery uses unscaled units; adapt to the CURRENT compatible mirror, never the old scale.
                body.linearVelocity=savedVelocity*mirror.Scale; body.angularVelocity=savedAngularVelocity*mirror.Scale;
                if((mirror.Rights&MoveRights.Walk)==0 && motionExecutions.Count==0)body.linearVelocity=new Vector3(0,body.linearVelocity.y,0);
            }
            RestoreLocomotionView(correction.State.Grounded,correction.State.VerticalView,correction.Recovery.AwaitingGroundDeparture);
        });
        if(managedMovement && !selected.NeedsBaseline && pendingMovementControl!=0){appliedMovementControl=pendingMovementControl;pendingMovementControl=0;initialMovementApplied=true;}
        }
        catch(Exception error){ReportMovementFailure(error);return;}
        finally
        {
            if(ReferenceEquals(OwnerMovement,selected) && NetworkUsable && (MovementHeld || selected.NeedsBaseline || selected.Mirror?.Dead==true) && !networkKinematic)SetNetworkKinematic();
        }
        if(!ReferenceEquals(OwnerMovement,selected) || !NetworkUsable || !loadContext.CanExecute)return;
        if(selected.CanWrite && !networkKinematic && !MovementHeld)
        {
            SyncPhysicsScale(NetworkScale);
            var report=OwnerMovement.Report(ReadMovementState());
            try{ReportTyped(report);}catch(Exception error){ReportMovementFailure(error);return;}
        }
    }
    public bool ApplyObserverPose(double hostRenderTime)
    {
        RequireNetworkReady(); if(NetworkRole!=PNMoveNetworkRole.Observer)return false;
        if(!ObserverMovement.TrySample(hostRenderTime,out var state))return false; ApplyPose(state); return true;
    }
    private void ApplyPose(MovementState state)
    {
        body.position=Unity(state.Position); transform.position=body.position; RestoreFacing(state.Facing);
        RefreshFacingVisual();
        RestoreLocomotionView(state.Grounded,state.VerticalView,false);
#if UNITY_5_3_OR_NEWER
        Physics.SyncTransforms();
#endif
    }
    protected virtual void RefreshFacingVisual() { }
    public PNMoveExecution RegisterMotion(EveId permission,Action notifyOwner)
    {
        RequireNetworkReady(); if(notifyOwner==null)throw new ArgumentNullException(nameof(notifyOwner));
        networkDetached=false;
        if(motionTransition || motionExecutions.Count!=0)throw new InvalidOperationException("Exclusive planar motion is transitioning or already owned.");
        permission=loadContext.Registry.ControlPermissions().ResolveIdentity(permission);
        var token=new PNMoveExecution(this,checked(++nextExecution),permission,notifyOwner); motionExecutions.Add(token); return token;
    }
    public void DetachNetwork()
    {
        if(networkDetached)return; networkDetached=true;
        foreach(var execution in motionExecutions.ToArray())execution.Invalidate();
        if(ObserverMovement!=null) { observerHistoryIdentity=ObserverMovement.Identity;observerSequenceFloor=ObserverMovement.LastSequence;observerEpochFloor=ObserverMovement.LastEpoch; }
        OwnerMovement?.Dispose(); ObserverMovement?.Dispose(); OwnerMovement=null; ObserverMovement=null; HostMovement=null;
        if(!managedMovement){if(NetworkRole!=PNMoveNetworkRole.Local)RestorePhysics();
        if(networkKinematic && body!=null)body.isKinematic=previousKinematic; networkKinematic=false;}
        NetworkRole=PNMoveNetworkRole.Local;
    }
    internal void ReleaseMotion(PNMoveExecution execution) => motionExecutions.Remove(execution);
    internal bool HasLegalNetworkMotion(MoveMirror mirror) => motionExecutions.Any(x=>x.IsValid && mirror.AllowsMotion(x.Permission.ToString()));
    internal void NotifyMotionCorrection(MoveCorrection correction,MoveMirror mirror)
    {
        bool cancel=mirror.Dead || correction.Reason==MoveCorrectionReason.Death || correction.Reason==MoveCorrectionReason.Teleport || correction.Reason==MoveCorrectionReason.RejectedBranch || correction.Reason==MoveCorrectionReason.Authority;
        bool previous=motionTransition;motionTransition=true;
        try
        {
            foreach(var execution in motionExecutions.ToArray())
                if(cancel || !mirror.AllowsMotion(execution.Permission.ToString()))execution.Invalidate();
        }
        finally { motionTransition=previous; }
    }
    internal void ApplyHostRelocation(MoveCorrection correction,MoveMirror mirror)
    {
        RequireNetworkReady();NotifyMotionCorrection(correction,mirror);if(!NetworkUsable)return;ApplyPose(correction.State);
        if(!body.isKinematic)
        {
            SyncPhysicsScale(mirror.Scale);savedVelocity=Unity(correction.Recovery.ResumeVelocity);savedAngularVelocity=Unity(correction.Recovery.ResumeAngularVelocity);
            if(!frozen) { body.linearVelocity=Unity(correction.State.Velocity);body.angularVelocity=savedAngularVelocity*mirror.Scale; }
        }
    }
    internal static MoveVector Vector(Vector3 v) => new MoveVector(v.x,v.y,v.z);
    internal static Vector3 Unity(MoveVector v) => new Vector3(v.X,v.Y,v.Z);
}
public sealed class PNMoveExecution : IDisposable
{
    private PNMove owner;
    private Action notify;
    public ulong Id { get; }
    public EveId Permission { get; }
    public bool IsValid => owner!=null;
    public bool CanWrite => owner!=null && owner.CanWriteMotion;
    internal PNMoveExecution(PNMove owner,ulong id,EveId permission,Action notify) { this.owner=owner; Id=id; Permission=permission; this.notify=notify; }
    public void Invalidate()
    {
        if(owner==null)return;
        var callback=notify; Dispose(); // Fence/remove before callback; a re-entrant replacement has a new token.
        try { callback?.Invoke(); } catch(Exception error) { Debug.LogException(error,null); }
    }
    public void Dispose() { var selected=owner; owner=null; notify=null; selected?.ReleaseMotion(this); }
}
