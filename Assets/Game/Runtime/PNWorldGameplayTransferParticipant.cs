#if UNITY_5_3_OR_NEWER
using UnityEngine;
public sealed class PNWorldGameplayTransferParticipant : IPNWorldTransferParticipant
{
    public bool Prepare(PNWorldTransferContext context)
        =>context.Actor.GetComponent<PNBuffManager>()==null||context.Actor.GetComponent<PNBuffManager>().CanCaptureState;
    public void Hold(PNWorldTransferContext context)
    {
        foreach(var skill in context.Actor.GetComponents<PNSkill>())skill.CancelWorldExecutions();
        context.Actor.GetComponent<PNStat>()?.PrepareWorldPresentationChange();
        context.Actor.GetComponent<PNMove>()?.CancelWorldMotion();
        PNGameBootstrap.Runtime?.GetComponent<PNAreaManager>()?.ActorWorldChanging(context.Actor.GetComponent<PNStat>());
    }
    public void Commit(PNWorldTransferContext context) { }
    public void Abort(PNWorldTransferContext context) { }
    public void Complete(PNWorldTransferContext context) { }
    public void Dispose() { }
}
#endif
