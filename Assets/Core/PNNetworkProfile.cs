using System;
// Starting profiles; these values are configurable correctness limits, not measured guarantees.
public sealed class PNNetworkProfile
{
    public int MaximumClients=3;
    public int TransportTimeoutMs=20000;
    public ushort Port=7777;
    public bool SelectPlayer=true;
    public bool ActionReplication=true;
    public int ActionReceiveBytes=32*1024*1024;
    public double ActionSendSeconds=.05,ActionPendingSeconds=.5,ActionSilenceSeconds=2;
    public double MoveSendSeconds=.05,MoveInterpolationSeconds=.1;
    public int CommandBytes=8192;
    public double AdmissionSeconds=15,GraceSeconds=15,RejectDrainSeconds=1;
    public int PendingBindings=64,LatestStateBytes=32768,RequiredEvents=256,RequiredEventBytes=262144,PendingEvents=4096,PendingBytes=8*1024*1024;
    private string CanonicalLimits=>string.Join(":",new[]{"action-wire3-"+(ActionReplication?"1":"0"),ActionReceiveBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),ActionSendSeconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture),ActionPendingSeconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture),ActionSilenceSeconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture),"move3-unscaled-kcp-unreliable-16384-512-64",MoveSendSeconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture),MoveInterpolationSeconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture),"buff3-clock2","ability-owner2-clock1","action-run1-4096-4-16-16-3","world2-transfer1-8-2-60-16384-262144-4096-32-128-256","1048576","65536","16384","4096","32","4096",PendingBindings.ToString(System.Globalization.CultureInfo.InvariantCulture),LatestStateBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),RequiredEvents.ToString(System.Globalization.CultureInfo.InvariantCulture),RequiredEventBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),PendingEvents.ToString(System.Globalization.CultureInfo.InvariantCulture),PendingBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)});
    public string ProtocolLimits
    {
        get {using(var hash=System.Security.Cryptography.SHA256.Create()){var bytes=hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(CanonicalLimits));return "pn-limits-sha256:"+BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}}
    }
    public PNNetworkProfile Snapshot(){Validate();return (PNNetworkProfile)MemberwiseClone();}
    public void Validate()
    {
        if(double.IsNaN(MoveSendSeconds)||double.IsInfinity(MoveSendSeconds)||MoveSendSeconds<=0||double.IsNaN(MoveInterpolationSeconds)||double.IsInfinity(MoveInterpolationSeconds)||MoveInterpolationSeconds<0||MoveInterpolationSeconds>1)throw new ArgumentOutOfRangeException("Move profile.");
        foreach(double value in new[]{AdmissionSeconds,GraceSeconds,RejectDrainSeconds,ActionSendSeconds,ActionPendingSeconds,ActionSilenceSeconds})if(double.IsNaN(value)||double.IsInfinity(value)||value<=0)throw new ArgumentOutOfRangeException();
        if(RejectDrainSeconds>AdmissionSeconds)throw new ArgumentOutOfRangeException(nameof(RejectDrainSeconds));
        if(ActionPendingSeconds<ActionSendSeconds||ActionSilenceSeconds<ActionSendSeconds)throw new ArgumentOutOfRangeException("Action cadence");
        if(ActionReceiveBytes<20*1024*1024||ActionReceiveBytes>64*1024*1024)throw new ArgumentOutOfRangeException(nameof(ActionReceiveBytes));
        if(MaximumClients<1||MaximumClients>255||TransportTimeoutMs<=GraceSeconds*1000||Port==0||CommandBytes<1||PendingBindings<1||LatestStateBytes<1||RequiredEvents<1||RequiredEventBytes<1||PendingEvents<RequiredEvents||PendingBytes<LatestStateBytes||PendingBytes<RequiredEventBytes)
            throw new ArgumentOutOfRangeException("Invalid network profile.");
    }
}
