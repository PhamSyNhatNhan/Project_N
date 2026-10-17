using System;
using EveEngine;
using EveEngine.Movement;
using UnityEngine;

// Typed transport seam. Network readiness and movement permission are separate.
public partial class PNMove
{
    private bool managedMovement, movementHeld, initialMovementApplied;
    private ulong appliedMovementControl, pendingMovementControl;
    private MovePoseState initialObserver;
    private MoveRecovery holdRecovery;
    public bool MovementHeld => managedMovement && movementHeld;
    public bool UsesTypedMovementTransport => managedMovement;
    public bool MovementBaselineApplied => !managedMovement || initialMovementApplied;
    public ulong AppliedMovementControl => appliedMovementControl;
    public event Action<MovePoseReport> MovementReportPublished;
    public event Action<Exception> MovementFailed;

    public void ConfigureNetworkMovement()
    {
        managedMovement=true; movementHeld=true; initialMovementApplied=false;
        appliedMovementControl=pendingMovementControl=0; initialObserver=null;
        SetMoveDirection(Vector2.zero);
    }
    public void SetMovementHeld(bool held)
    {
        if(!managedMovement)return;
        if(movementHeld==held)return;
        movementHeld=held;
        if(held){SetMoveDirection(Vector2.zero);EnsureMovementHold();}
        else if(initialMovementApplied && NetworkRole==PNMoveNetworkRole.Owner && OwnerMovement?.CanWrite==true)
        {
            if(networkKinematic){body.isKinematic=previousKinematic;networkKinematic=false;}
            var recovery=holdRecovery;
            if(!body.isKinematic && !frozen){body.linearVelocity=Unity(recovery.ResumeVelocity)*NetworkScale;body.angularVelocity=Unity(recovery.ResumeAngularVelocity)*NetworkScale;}
        }
        else if(NetworkRole==PNMoveNetworkRole.Local && networkKinematic){body.isKinematic=previousKinematic;networkKinematic=false;if(!body.isKinematic){body.linearVelocity=Unity(holdRecovery.ResumeVelocity)*previousScale;body.angularVelocity=Unity(holdRecovery.ResumeAngularVelocity)*previousScale;}}
    }
    private void EnsureMovementHold()
    { if(managedMovement && movementHeld && body!=null){if(!networkKinematic)holdRecovery=new MoveRecovery(Vector(frozen?savedVelocity:body.linearVelocity/previousScale),Vector(frozen?savedAngularVelocity:body.angularVelocity/previousScale),AwaitingGroundDeparture);SetNetworkKinematic();} }
    public void ReleaseNetworkMovement()
    {
        networkDetached=false;
        managedMovement=false; movementHeld=false;initialMovementApplied=false; initialObserver=null;
        DetachNetwork();MovementReportPublished=null;MovementFailed=null;appliedMovementControl=pendingMovementControl=0;
    }
    public void StageMovementControl(MoveMirror mirror,MoveCorrection correction,ulong revision)
    {
        RequireNetworkReady();
        if(!managedMovement || revision==0 || revision<=appliedMovementControl)return;
        if(mirror==null||correction==null||!mirror.Identity.Equals(correction.Identity)||!MoveOrder.AtLeast(mirror.Revision,correction.MirrorRevision))throw new ArgumentException("Movement control pair.");
        if(!IsPredictedOwner)throw new InvalidOperationException("Movement owner required.");
        ReceiveOwnerMessage(mirror);
        if(OwnerMovement.AppliedCorrection==correction.Id && !OwnerMovement.NeedsBaseline)
        {appliedMovementControl=revision;return;}
        if(!ReceiveOwnerMessage(correction))throw new InvalidOperationException("Movement control correction rejected.");
        pendingMovementControl=revision;
    }
    public void StageObserverMovement(MovePoseState pose,ulong revision)
    {
        RequireNetworkReady();
        if(NetworkRole!=PNMoveNetworkRole.Observer||revision==0)throw new InvalidOperationException("Movement observer required.");
        initialObserver=pose??throw new ArgumentNullException(nameof(pose));pendingMovementControl=revision;
    }
    private void ApplyInitialObserverBoundary()
    {
        if(initialObserver==null || !NetworkUsable)return;
        var pose=initialObserver;initialObserver=null;
        if(!ObserverMovement.Receive(pose,true))throw new InvalidOperationException("Observer baseline rejected.");
        ApplyPose(pose.State);initialMovementApplied=true;appliedMovementControl=pendingMovementControl;pendingMovementControl=0;
    }
    private void ReportTyped(MovePoseReport report)
    {
        foreach(Action<MovePoseReport> listener in MovementReportPublished?.GetInvocationList()??Array.Empty<Delegate>())listener(report);
    }
    private void ReportMovementFailure(Exception error)
    {
        if(!managedMovement)throw error;
        movementHeld=true;EnsureMovementHold();
        foreach(Action<Exception> listener in MovementFailed?.GetInvocationList()??Array.Empty<Delegate>())listener(error);
    }
}
