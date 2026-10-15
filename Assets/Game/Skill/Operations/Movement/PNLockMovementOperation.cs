using System;
using Newtonsoft.Json.Linq;

// Releases only this invocation's lease; never restores another owner's bools.
public class PNLockMovementOperation : PNSkillOperation
{
    private PNControlLease lease;
    private bool executed, disposed;
    public override void Execute(PNSkillActionContext context, JToken data)
    {
        if (executed || disposed) throw new InvalidOperationException("Movement lock cannot execute twice.");
        if (context == null) throw new ArgumentNullException(nameof(context));
        executed = true;
        lease = context.Skill.Controls.Acquire("action:" + context.SkillId,
            new[] { PNControlIds.Move, PNControlIds.FaceControl, PNControlIds.Jump });
    }
    public override void Cancel() => Dispose();
    public override void Dispose() { disposed = true; var previous = lease; lease = null; previous?.Dispose(); }
}
