using System;
using System.Collections.Generic;
using System.Text;

public enum PNActionRecoveryState { Idle, Pending, Recovered, Failed }

// Authority service census: independent of both legacy admission and publication.
// All methods run on the gameplay thread; recovery is invoked only after drivers.
public sealed class PNActionPublicationRecovery : IDisposable
{
    sealed class Row
    {
        public PNActionRunKey Key;
        public string Entry, Data, Action;
        public double X, Z, Start, Stamp, Progress, Rate, Scale;
        public int Charge;
        public PNActionRunContext Ctx;
        public readonly List<PNActionFact> Facts = new List<PNActionFact>();
    }
    readonly Dictionary<PNActionRunKey, Row> live = new Dictionary<PNActionRunKey, Row>();
    readonly PNActionPublicationBudget budget;
    readonly int limit;
    int bytes;
    double lastTime;
    bool disposed;
    public bool Complete { get; private set; } = true;
    public int Count => live.Count;
    public PNActionRecoveryState State { get; private set; }
    public PNActionPublicationStatus Failure { get; private set; }
    public double Deadline { get; private set; } = double.PositiveInfinity;
    public long Attempts { get; private set; }
    public PNActionPublicationRecovery(PNActionPublicationBudget budget, int limit = 4*1024*1024)
    { this.budget = budget ?? throw new ArgumentNullException(nameof(budget)); if(limit<1)throw new ArgumentOutOfRangeException(nameof(limit));this.limit=limit;if(budget.Recoveries.Count>=4096){Complete=false;Failure=PNActionPublicationStatus.ResourceExhausted;disposed=true;}else budget.Recoveries.Add(this); }
    public void Invalidate() { Complete=false; Failure=PNActionPublicationStatus.InvariantFault; }
    public void Begin(PNActionRunKey key,string entry,string data,string action,double x,double z,double stamp)
        => Begin(key,entry,data,action,x,z,stamp,1);

    public void Begin(PNActionRunKey key,string entry,string data,string action,double x,double z,double stamp,double scale)
        => Begin(key,entry,data,action,stamp,scale,PNActionRunContext.FromDirection(x,z));

    public void Begin(PNActionRunKey key,string entry,string data,string action,double stamp,double scale,PNActionRunContext ctx)
    {
        if(disposed)return;
        if(live.ContainsKey(key)){Invalidate();return;}
        PNTimeMath.Nonnegative(scale);
        int charge=checked(256+Encoding.UTF8.GetByteCount(entry)+Encoding.UTF8.GetByteCount(data)+Encoding.UTF8.GetByteCount(action)+ctx.Targets.Count*8);
        if(live.Count>=4096||charge>limit-bytes||!budget.Take(charge)) { Complete=false;Failure=PNActionPublicationStatus.ResourceExhausted;return; }
        live.Add(key,new Row{Key=key,Entry=entry,Data=data,Action=action,X=ctx.X,Z=ctx.Z,Start=stamp,Stamp=stamp,Charge=charge,Scale=scale,Ctx=ctx});bytes+=charge;
    }
    public void Advance(PNActionRunKey key,double stamp,double step,double rate)
    {
        if(disposed)return;
        if(!live.TryGetValue(key,out var row)){Invalidate();return;}
        row.Progress+=step;row.Stamp=Math.Max(stamp,row.Stamp);row.Rate=rate;
        if(double.IsNaN(row.Progress)||double.IsInfinity(row.Progress))Invalidate();
    }
    // PV2: the facts of a live run come back with it after a recovery (the same per-run bound as the stream).
    public void Fact(PNActionRunKey key,PNActionFact fact)
    {
        if(disposed||fact==null)return;
        if(!live.TryGetValue(key,out var row)||row.Facts.Count>=PNActionFact.MaxPerRun)return;
        int charge=64+fact.Key.Length+fact.Count*8;
        if(charge>limit-bytes||!budget.Take(charge)){Complete=false;Failure=PNActionPublicationStatus.ResourceExhausted;return;}
        row.Facts.Add(fact);row.Charge+=charge;bytes+=charge;
    }
    public void End(PNActionRunKey key)
    { if(live.TryGetValue(key,out var row)){live.Remove(key);bytes-=row.Charge;budget.Return(row.Charge);} }
    public void Seal(double stamp)
    {foreach(var row in live.Values)if(stamp>row.Stamp){row.Stamp=stamp;row.Rate=0;}}
    public PNActionRecoveryState Recover(ref PNActionPublication source,double stamp,double originalDeadline)
    {
        PNTimeMath.Nonnegative(stamp);PNTimeMath.Nonnegative(originalDeadline);
        if(disposed)return PNActionRecoveryState.Failed;
        if(State==PNActionRecoveryState.Failed)return State;
        if(stamp<lastTime){Failure=PNActionPublicationStatus.InvariantFault;return State=PNActionRecoveryState.Failed;}
        lastTime=stamp;
        if(source!=null&&source.Complete&&!source.Disposed){State=PNActionRecoveryState.Idle;Deadline=double.PositiveInfinity;return State;}
        if(State!=PNActionRecoveryState.Pending){Deadline=originalDeadline;State=PNActionRecoveryState.Pending;}
        else Deadline=Math.Min(Deadline,originalDeadline);
        if(stamp>=Deadline){Failure=PNActionPublicationStatus.ResourceExhausted;return State=PNActionRecoveryState.Failed;}
        if(!Complete){if(Failure==PNActionPublicationStatus.Ok)Failure=PNActionPublicationStatus.InvariantFault;return State=PNActionRecoveryState.Failed;}
        // Obsolete generation cannot provide a valid baseline; release it before
        // acquiring bounded staging. It stays visibly disposed on failed attempts.
        source?.Dispose();
        PNActionPublication candidate=null;
        Attempts++;
        try
        {
            candidate=new PNActionPublication(sessionBudget:budget);
            foreach(var row in live.Values)
            {
                var status=candidate.RestoreCurrent(row.Key,row.Entry,row.Data,row.Action,row.Ctx,row.Start,row.Stamp,row.Progress,row.Rate,stamp,row.Scale,row.Facts);
                if(status!=PNActionPublicationStatus.Ok){Failure=status;return State=status==PNActionPublicationStatus.ResourceExhausted?PNActionRecoveryState.Pending:PNActionRecoveryState.Failed;}
            }
            if(!candidate.Complete){Failure=candidate.Status;return State=PNActionRecoveryState.Pending;}
            source=candidate;candidate=null;Failure=PNActionPublicationStatus.Ok;return State=PNActionRecoveryState.Recovered;
        }
        catch(Exception){Failure=PNActionPublicationStatus.InvariantFault;return State=PNActionRecoveryState.Failed;}
        finally{candidate?.Dispose();}
    }
    public void Dispose(){if(disposed)return;disposed=true;budget.Return(bytes);bytes=0;live.Clear();budget.Recoveries.Remove(this);Complete=false;State=PNActionRecoveryState.Failed;}
}
