using System;
// Starting profiles; these values are configurable correctness limits, not measured guarantees.
public sealed class PNNetworkProfile
{
    public int MaximumClients=3;
    public int TransportTimeoutMs=20000;
    public ushort Port=7777;
    public bool SelectPlayer=true;
    public double MoveSendSeconds=.05,MoveInterpolationSeconds=.1;
    public int CommandBytes=8192;
    public double AdmissionSeconds=15,GraceSeconds=15,RejectDrainSeconds=1;
    public int PendingBindings=64,LatestStateBytes=32768,RequiredEvents=256,RequiredEventBytes=262144,PendingEvents=4096,PendingBytes=8*1024*1024;
    public string ProtocolLimits=>string.Join(":",new[]{"move1-kcp-unreliable-16384-512-64",MoveSendSeconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture),MoveInterpolationSeconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture),"buff1","1048576","65536","16384","4096","32","4096",PendingBindings.ToString(System.Globalization.CultureInfo.InvariantCulture),LatestStateBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),RequiredEvents.ToString(System.Globalization.CultureInfo.InvariantCulture),RequiredEventBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),PendingEvents.ToString(System.Globalization.CultureInfo.InvariantCulture),PendingBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)});
    public void Validate()
    {
        if(double.IsNaN(MoveSendSeconds)||double.IsInfinity(MoveSendSeconds)||MoveSendSeconds<=0||double.IsNaN(MoveInterpolationSeconds)||double.IsInfinity(MoveInterpolationSeconds)||MoveInterpolationSeconds<0||MoveInterpolationSeconds>1)throw new ArgumentOutOfRangeException("Move profile.");
        foreach(double value in new[]{AdmissionSeconds,GraceSeconds,RejectDrainSeconds})if(double.IsNaN(value)||double.IsInfinity(value)||value<=0)throw new ArgumentOutOfRangeException();
        if(RejectDrainSeconds>AdmissionSeconds)throw new ArgumentOutOfRangeException(nameof(RejectDrainSeconds));
        if(MaximumClients<1||MaximumClients>255||TransportTimeoutMs<=GraceSeconds*1000||Port==0||CommandBytes<1||PendingBindings<1||LatestStateBytes<1||RequiredEvents<1||RequiredEventBytes<1||PendingEvents<RequiredEvents||PendingBytes<LatestStateBytes||PendingBytes<RequiredEventBytes)
            throw new ArgumentOutOfRangeException("Invalid network profile.");
    }
}
