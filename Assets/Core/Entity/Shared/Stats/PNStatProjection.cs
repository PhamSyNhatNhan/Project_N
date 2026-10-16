using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using EveEngine;

// Transport-neutral, immutable replacement. LifeOnly never claims coherent numeric values.
public sealed class PNStatFrame
{
    public long Revision { get; }
    public long LifeRevision { get; }
    public bool Dead { get; }
    public bool LifeOnly { get; }
    public IReadOnlyDictionary<EveId, PNResourceValue> Resources { get; }
    public PNStatFrame(long revision,long lifeRevision,bool dead,IDictionary<EveId,PNResourceValue> resources,bool lifeOnly=false)
    {
        if(revision<1||lifeRevision<1||lifeRevision>revision||resources==null||resources.Count>128)throw new FormatException("Stat frame bounds.");
        var copy=new Dictionary<EveId,PNResourceValue>();
        foreach(var entry in resources){if(!entry.Key.IsValid)throw new FormatException("Stat ID required.");entry.Value.Validate();copy.Add(entry.Key,entry.Value);}
        if(lifeOnly&&copy.Count!=0)throw new FormatException("Life notice has resources.");
        Revision=revision;LifeRevision=lifeRevision;Dead=dead;LifeOnly=lifeOnly;Resources=new ReadOnlyDictionary<EveId,PNResourceValue>(copy);
    }
    public bool SameValues(PNStatFrame other)=>other!=null&&Revision==other.Revision&&LifeRevision==other.LifeRevision&&Dead==other.Dead&&LifeOnly==other.LifeOnly&&Resources.Count==other.Resources.Count&&Resources.All(x=>other.Resources.TryGetValue(x.Key,out var value)&&x.Value.Current==value.Current&&x.Value.Max==value.Max);
}
public readonly struct PNResourceValue
{
    public double Current {get;}
    public double Max {get;}
    public PNResourceValue(double current,double max){Current=current;Max=max;Validate();}
    public void Validate(){if(double.IsNaN(Current)||double.IsInfinity(Current)||double.IsNaN(Max)||double.IsInfinity(Max)||Max<0||Current<0||Current>Max)throw new FormatException("Invalid resource pair.");}
}
// The sole host life/version owner, also used by the legacy facade during migration.
public sealed class PNStatProjection
{
    public PNStatFrame State {get;private set;}
    public bool IsCoherent {get;private set;}
    public PNStatFrame Capture(IDictionary<EveId,PNResourceValue> resources,bool dead)
    {
        bool changed=State==null||!IsCoherent||State.Dead!=dead||State.Resources.Count!=resources.Count||resources.Any(x=>!State.Resources.TryGetValue(x.Key,out var old)||old.Current!=x.Value.Current||old.Max!=x.Value.Max);
        if(!changed)return State;
        var next=new PNStatFrame(State==null?1:checked(State.Revision+1),State==null?1:State.Dead!=dead?checked(State.LifeRevision+1):State.LifeRevision,dead,resources);
        State=next;IsCoherent=true;return next;
    }
    public PNStatFrame ObserveLife(bool dead)
    {
        if(State==null||State.Dead==dead)return null;
        var notice=new PNStatFrame(checked(State.Revision+1),checked(State.LifeRevision+1),dead,new Dictionary<EveId,PNResourceValue>(),true);
        State=new PNStatFrame(notice.Revision,notice.LifeRevision,dead,new Dictionary<EveId,PNResourceValue>(State.Resources));IsCoherent=false;return notice;
    }
}
