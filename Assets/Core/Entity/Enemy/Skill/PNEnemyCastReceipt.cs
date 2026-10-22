#if UNITY_5_3_OR_NEWER
using System;

public enum PNEnemyCastAdmission { Started, Busy, Denied }
public enum PNEnemyCastStatus { Started, Completed, Cancelled, Failed }
public sealed class PNEnemyCastReceipt
{
    public long InvocationId { get; }
    public PNEnemyCastStatus Status { get; private set; }
    public string Reason { get; private set; }
    public Exception Error { get; private set; }
    public bool IsTerminal => Status != PNEnemyCastStatus.Started;
    internal PNEnemyCastReceipt(long invocationId) { InvocationId = invocationId; }
    internal void Finish(PNEnemyCastStatus status, string reason = null, Exception error = null)
    { if (IsTerminal) return; Status = status; Reason = reason; Error = error; }
}
#endif
