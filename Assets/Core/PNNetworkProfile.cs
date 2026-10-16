using System;
// Starting profiles; these values are configurable correctness limits, not measured guarantees.
public sealed class PNNetworkProfile
{
    public int MaximumClients=3;
    public int TransportTimeoutMs=20000;
    public ushort Port=7777;
    public bool SelectPlayer=true;
    public int CommandBytes=8192;
    public double AdmissionSeconds=15,GraceSeconds=15,RejectDrainSeconds=1;
    public int PendingBindings=64,LatestStateBytes=32768,RequiredEvents=64,RequiredEventBytes=65536,PendingEvents=4096,PendingBytes=4*1024*1024;
    public void Validate()
    {
        foreach(double value in new[]{AdmissionSeconds,GraceSeconds,RejectDrainSeconds})if(double.IsNaN(value)||double.IsInfinity(value)||value<=0)throw new ArgumentOutOfRangeException();
        if(RejectDrainSeconds>AdmissionSeconds)throw new ArgumentOutOfRangeException(nameof(RejectDrainSeconds));
        if(MaximumClients<1||MaximumClients>255||TransportTimeoutMs<=GraceSeconds*1000||Port==0||CommandBytes<1||PendingBindings<1||LatestStateBytes<1||RequiredEvents<1||RequiredEventBytes<1||PendingEvents<RequiredEvents||PendingBytes<LatestStateBytes||PendingBytes<RequiredEventBytes)
            throw new ArgumentOutOfRangeException("Invalid network profile.");
    }
}
