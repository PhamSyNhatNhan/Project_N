using System;

// A presentation reader over the shared history. Preparation never depends on input readiness.
public sealed class PNClockProjection : IDisposable
{
    private readonly PNClockHistory history;
    public PNClockKey Key { get; }
    public bool Prepared { get; private set; }
    public double Value { get; private set; }
    public PNClockRead LastRead { get; private set; } = PNClockRead.MissingBaseline;
    private bool disposed;
    public PNClockProjection(PNClockHistory history,PNClockKey key)
    {this.history=history??throw new ArgumentNullException(nameof(history));key.Validate();Key=key;}
    public PNClockRead Prepare(PNClockAnchor anchor,double hostNow,double uncertainty,double minimumNeeded,bool baseline)
    {
        if(disposed)throw new ObjectDisposedException(nameof(PNClockProjection));
        if(!anchor.Key.Equals(Key))throw new ArgumentException("Projection clock identity.");
        var result=history.Add(anchor,hostNow,uncertainty,minimumNeeded,baseline);
        Prepared=result==PNClockRead.Ready;
        if(Prepared&&baseline)Value=anchor.Value; // coherent seek, never replay interrupted elapsed
        LastRead=result;return result;
    }
    public double Read(PNTimeSample sample)
        => ReadAt(sample, sample.Presentation);
    // Owner image reconciliation selects Host explicitly; never use this as a simulation delta.
    public double ReadAt(PNTimeSample sample, double stamp)
    {
        PNTimeMath.Nonnegative(stamp);
        if(disposed)throw new ObjectDisposedException(nameof(PNClockProjection));
        if(!Prepared||!sample.Ready||sample.Epoch!=Key.Epoch)
        {LastRead=sample.Epoch!=Key.Epoch?PNClockRead.StaleEpoch:PNClockRead.MissingBaseline;return Value;}
        LastRead=history.Read(Key,stamp,out var value);
        if(LastRead==PNClockRead.Ready)Value=Math.Max(Value,value);
        return Value;
    }
    public void Dispose(){if(disposed)return;disposed=true;Prepared=false;history.Remove(Key);}
}
