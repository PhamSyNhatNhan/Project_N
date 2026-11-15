using System;
using System.Runtime.CompilerServices;
using EveEngine;

public readonly struct PNTimeSample
{
    public readonly Guid Epoch;
    public readonly bool Ready;
    public readonly double Local, Host, Presentation, Uncertainty;
    public PNTimeSample(Guid epoch,bool ready,double local,double host,double presentation,double uncertainty)
    {if(epoch==Guid.Empty)throw new ArgumentException("Epoch required.");PNTimeMath.Nonnegative(local);PNTimeMath.Nonnegative(host);PNTimeMath.Nonnegative(presentation);PNTimeMath.Nonnegative(uncertainty);Epoch=epoch;Ready=ready;Local=local;Host=host;Presentation=presentation;Uncertainty=uncertainty;}
}
public static class PNOwnerImageAge
{
    // Age projection happens once at authoritative image reconciliation, before overlay rebase.
    public static bool TryRead(PNTimeSample now,Guid epoch,double captured,out double age)
    {PNTimeMath.Nonnegative(captured);age=0;if(!now.Ready||epoch!=now.Epoch)return false;double elapsed=now.Host-captured;if(elapsed < -now.Uncertainty || elapsed>2)return false;age=Math.Max(0,elapsed);return true;}
}
public interface IPNNetworkTimeline { PNTimeSample Sample(); }
public sealed class PNLocalTimeline : IPNNetworkTimeline
{
    private readonly Guid epoch;private readonly Func<double> clock;
    public PNLocalTimeline(Guid epoch,Func<double> clock){if(epoch==Guid.Empty)throw new ArgumentException();this.epoch=epoch;this.clock=clock??throw new ArgumentNullException(nameof(clock));}
    public PNTimeSample Sample(){double t=clock();return new PNTimeSample(epoch,true,t,t,t,0);}
}
// Explicitly rebase on a new baseline/generation. Source corrections never become negative Advance.
public sealed class PNPresentationCursor
{
    private Guid epoch; private double stamp; private bool set;
    public void Reset(){set=false;epoch=Guid.Empty;stamp=0;}
    public void Rebase(Guid epoch,double stamp){if(epoch==Guid.Empty)throw new ArgumentException();PNTimeMath.Nonnegative(stamp);this.epoch=epoch;this.stamp=stamp;set=true;}
    public double Advance(PNTimeSample sample){if(!sample.Ready)return 0;if(!set||epoch!=sample.Epoch){Rebase(sample.Epoch,sample.Presentation);return 0;}double next=Math.Max(stamp,sample.Presentation),delta=next-stamp;stamp=next;return delta;}
}
public sealed class PNTimeService : IDisposable
{
    private static readonly ConditionalWeakTable<EveSessionContext,PNTimeService> sessions=new ConditionalWeakTable<EveSessionContext,PNTimeService>();
    private readonly EveSessionContext session;private readonly IPNNetworkTimeline source;private bool disposed;
    public PNClockHistory History {get;private set;}
    private PNTimeService(EveSessionContext session,IPNNetworkTimeline source){this.session=session;this.source=source;}
    public static PNTimeService Attach(EveSessionContext session,IPNNetworkTimeline source)
    {if(session==null||!session.IsActive||source==null)throw new ArgumentException();if(sessions.TryGetValue(session,out _))throw new InvalidOperationException("Timeline already attached.");var service=new PNTimeService(session,source);sessions.Add(session,service);session.RegisterCleanup(service.Dispose);return service;}
    public static PNTimeService For(EveSessionContext session)=>sessions.TryGetValue(session,out var service)?service:throw new InvalidOperationException("No session timeline.");
    public PNTimeSample Sample()
    {if(disposed||!session.IsActive)throw new ObjectDisposedException(nameof(PNTimeService));var s=source.Sample();if(History==null)History=new PNClockHistory(s.Epoch);else if(History.Epoch!=s.Epoch)History.Reset(s.Epoch);return s;}
    public void Dispose(){if(disposed)return;disposed=true;sessions.Remove(session);if(History!=null)History.Reset(History.Epoch);History=null;}
}
// One pending final acknowledgement, scoped by recovery attempt, never by heartbeat sequence.
public sealed class PNTimeRecovery
{
    public Guid Epoch {get;private set;}public long Attempt {get;private set;}public string Digest {get;private set;}
    public bool Ready {get;private set;}private bool ack;private double started,grace;
    public void Begin(Guid epoch,long attempt,string digest,double local,double graceSeconds)
    {if(epoch==Guid.Empty||attempt<1||digest==null||digest.Length!=64)throw new ArgumentException();PNTimeMath.Nonnegative(local);PNTimeMath.Nonnegative(graceSeconds);if(graceSeconds==0)throw new ArgumentOutOfRangeException();if(Epoch==epoch&&Attempt>=attempt)throw new InvalidOperationException("Recovery attempt must increase.");
    // A new attempt within the same interruption never extends its deadline.
    if(Epoch!=epoch||Ready||Attempt==0){started=local;grace=graceSeconds;}Epoch=epoch;Attempt=attempt;Digest=digest;Ready=false;ack=false;}
    public bool Acknowledge(Guid epoch,long attempt,string digest){if(epoch!=Epoch||attempt!=Attempt||digest!=Digest)return false;ack=true;return true;}
    public bool TryComplete(double local,bool appliedStateReady){PNTimeMath.Nonnegative(local);if(local<started)throw new ArgumentOutOfRangeException();if(Ready)return true;if(local-started>=grace){Ready=false;return false;}if(ack&&appliedStateReady)Ready=true;return Ready;}
    public void Reset(){Epoch=Guid.Empty;Attempt=0;Digest=null;Ready=ack=false;started=grace=0;}
}
