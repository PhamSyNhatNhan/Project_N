using System;
using System.Collections.Generic;

public enum PNClockDomain { WorldGame, Entity, EntityWithoutMinimum, Instance, BuffGame, AbilityGame, AbilityEntity, AbilityRaw }
public readonly struct PNClockKey : IEquatable<PNClockKey>
{
    public readonly Guid Epoch, World;
    public readonly long Incarnation, Owner, Generation;
    public readonly PNClockDomain Domain;
    public PNClockKey(Guid epoch, Guid world, long incarnation, PNClockDomain domain, long owner=0, long generation=0)
    {
        if(epoch==Guid.Empty || world==Guid.Empty || incarnation<1 || !Enum.IsDefined(typeof(PNClockDomain),domain) || owner<0 || generation<0 || (domain!=PNClockDomain.WorldGame && (owner==0 || generation==0)))throw new ArgumentException("Clock identity.");
        Epoch=epoch; World=world; Incarnation=incarnation; Domain=domain; Owner=owner; Generation=generation;
    }
    public bool Equals(PNClockKey k)=>Epoch==k.Epoch&&World==k.World&&Incarnation==k.Incarnation&&Owner==k.Owner&&Generation==k.Generation&&Domain==k.Domain;
    public override bool Equals(object o)=>o is PNClockKey k&&Equals(k);
    public override int GetHashCode()=>Epoch.GetHashCode()^World.GetHashCode()^Incarnation.GetHashCode()^Owner.GetHashCode()^Generation.GetHashCode()^(int)Domain;
    internal void Validate(){new PNClockKey(Epoch,World,Incarnation,Domain,Owner,Generation);}
}
public readonly struct PNClockAnchor
{
    public readonly PNClockKey Key;
    public readonly long Revision;
    public readonly double Stamp, Value, Rate;
    public PNClockAnchor(PNClockKey key,long revision,double stamp,double value,double rate)
    {key.Validate(); if(revision<0)throw new ArgumentOutOfRangeException(nameof(revision)); PNTimeMath.Nonnegative(stamp);PNTimeMath.Nonnegative(value);PNTimeMath.Nonnegative(rate);Key=key;Revision=revision;Stamp=stamp;Value=value;Rate=rate;}
}
public static class PNTimeMath
{
    public static void Nonnegative(double x){if(double.IsNaN(x)||double.IsInfinity(x)||x<0)throw new ArgumentOutOfRangeException(nameof(x));}
}
public enum PNClockRead { Ready, MissingBaseline, BeforeBaseline, Gap, Capacity, StaleEpoch, FutureStamp }
public sealed class PNClockHistory
{
    private Dictionary<PNClockKey,List<PNClockAnchor>> clocks=new Dictionary<PNClockKey,List<PNClockAnchor>>();
    public Guid Epoch {get;private set;}
    public int AnchorCount {get;private set;}
    private int reservedKeys;
    // Conservative retained-capacity charge, including dictionary slots kept after Remove.
    public int ChargedBytes { get { int bytes=reservedKeys*512;foreach(var list in clocks.Values)bytes+=list.Capacity*128;return bytes;} }
    public long Recoveries {get;private set;}
    public readonly int PerClock, MaximumAnchors, MaximumBytes;
    public readonly double Horizon;
    public PNClockHistory(Guid epoch,int perClock=64,int maximumAnchors=16384,int maximumBytes=2097152,double horizon=10)
    {if(perClock<1||maximumAnchors<1||maximumBytes<1024)throw new ArgumentOutOfRangeException();PNTimeMath.Nonnegative(horizon);PerClock=perClock;MaximumAnchors=maximumAnchors;MaximumBytes=maximumBytes;Horizon=horizon;Reset(epoch);}
    public void Reset(Guid epoch){if(epoch==Guid.Empty)throw new ArgumentException("Epoch required.");clocks=new Dictionary<PNClockKey,List<PNClockAnchor>>();reservedKeys=0;AnchorCount=0;Epoch=epoch;}
    public void Remove(PNClockKey key){if(clocks.TryGetValue(key,out var list)){AnchorCount-=list.Count;clocks.Remove(key);}}
    private PNClockRead Fail(PNClockKey key,PNClockRead reason){Remove(key);Recoveries++;return reason;}
    // minimumNeeded is the oldest consumer cursor. Never discard a segment it still needs.
    public PNClockRead Add(PNClockAnchor a,double hostNow,double uncertainty,double minimumNeeded,bool baseline=false)
    {
        a.Key.Validate();PNTimeMath.Nonnegative(a.Stamp);PNTimeMath.Nonnegative(a.Value);PNTimeMath.Nonnegative(a.Rate);
        PNTimeMath.Nonnegative(hostNow);PNTimeMath.Nonnegative(uncertainty);PNTimeMath.Nonnegative(minimumNeeded);
        if(a.Revision<0)throw new ArgumentOutOfRangeException();
        if(a.Key.Epoch!=Epoch)return PNClockRead.StaleEpoch;
        if(a.Stamp-hostNow>2+uncertainty)return Fail(a.Key,PNClockRead.FutureStamp);
        clocks.TryGetValue(a.Key,out var list);
        if(baseline){if(list!=null && a.Revision<list[list.Count-1].Revision)return PNClockRead.Ready;Remove(a.Key);list=null;}
        if(list==null && !baseline)return PNClockRead.MissingBaseline;
        if(list!=null)
        {
            foreach(var old in list)if(old.Revision==a.Revision)return old.Stamp==a.Stamp&&old.Value==a.Value&&old.Rate==a.Rate?PNClockRead.Ready:Fail(a.Key,PNClockRead.Gap);
            var last=list[list.Count-1];
            if(a.Revision<last.Revision)return PNClockRead.Ready; // already superseded; never reapply old corrections
            if(a.Revision-last.Revision!=1 || a.Stamp<last.Stamp)return Fail(a.Key,PNClockRead.Gap);
            while(list.Count>1 && list[1].Stamp<=minimumNeeded && hostNow-list[1].Stamp>Horizon){list.RemoveAt(0);AnchorCount--;}
        }
        int keyCost=list==null && clocks.Count>=reservedKeys?512:0;
        int capacity=list?.Capacity??0, count=list?.Count??0;
        int growth=count==capacity?(capacity==0?4:capacity)*128:0;
        if(count>=PerClock || AnchorCount>=MaximumAnchors || ChargedBytes+keyCost+growth>MaximumBytes)return Fail(a.Key,PNClockRead.Capacity);
        if(list==null){list=new List<PNClockAnchor>();clocks.Add(a.Key,list);reservedKeys=Math.Max(reservedKeys,clocks.Count);}list.Add(a);AnchorCount++;return PNClockRead.Ready;
    }
    public PNClockRead Read(PNClockKey key,double stamp,out double value)
    {
        key.Validate();PNTimeMath.Nonnegative(stamp);value=0;
        if(key.Epoch!=Epoch)return PNClockRead.StaleEpoch;
        if(!clocks.TryGetValue(key,out var list))return PNClockRead.MissingBaseline;
        for(int i=list.Count-1;i>=0;i--)if(stamp>=list[i].Stamp){var a=list[i];value=a.Value+(stamp-a.Stamp)*a.Rate;if(double.IsInfinity(value))throw new OverflowException("Clock projection.");return PNClockRead.Ready;}
        return PNClockRead.BeforeBaseline;
    }
}
