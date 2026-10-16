using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

public sealed class PNSkillOverrideRequest
{
    public IReadOnlyDictionary<EveId,EveId> Slots {get;}
    public int Priority {get;}
    public PNSkillTransition ApplyTransition {get;}
    public PNSkillTransition ReleaseTransition {get;}
    public PNSkillOverrideRequest(IEnumerable<KeyValuePair<EveId,EveId>> slots,int priority=0,
        PNSkillTransition apply=PNSkillTransition.FinishCurrent,PNSkillTransition release=PNSkillTransition.FinishCurrent)
    {Slots=new PNSkillBaseBindings(EveId.Parse("project_n:request"),slots).Slots;if(Slots.Count==0||!Enum.IsDefined(typeof(PNSkillTransition),apply)||!Enum.IsDefined(typeof(PNSkillTransition),release))throw new ArgumentException("Invalid override request.");Priority=priority;ApplyTransition=apply;ReleaseTransition=release;}
}
public enum PNSkillBindingReceiptStatus { Pending, Applied, Failed }
public sealed class PNSkillBindingReceipt
{
    public PNSkillBindingReceiptStatus Status {get;internal set;}
    public Exception Error {get;internal set;}
    public PNSkillBindingLease Lease {get;internal set;}
    public event Action<PNSkillBindingReceipt> Completed;
    internal void Finish(PNSkillBindingReceiptStatus status,Exception error=null)
    {
        Status=status;Error=error;
        foreach(Action<PNSkillBindingReceipt> observer in Completed?.GetInvocationList()??Array.Empty<Delegate>())try{observer(this);}catch(Exception e){Report?.Invoke(e);}
        Completed=null;
    }
    internal Action<Exception> Report;
}
public sealed class PNSkillBindingLease:IDisposable
{
    internal readonly PNSkillBindingState State;
    internal readonly PNSkillOverrideRequest Request;
    public long Sequence {get;}
    public string Owner {get;}
    public bool IsActive {get;internal set;}
    internal bool Removed;
    internal PNSkillBindingLease(PNSkillBindingState state,long sequence,string owner,PNSkillOverrideRequest request)
    {State=state;Sequence=sequence;Owner=owner;Request=request;}
    public void Dispose(){if(Removed)return;Removed=true;State.Release(this);}
}
public sealed class PNSkillBindingChange
{
    public long Revision {get;}
    public IReadOnlyDictionary<EveId,EveId> Previous {get;}
    public IReadOnlyDictionary<EveId,EveId> Current {get;}
    public PNSkillTransition Transition {get;}
    internal PNSkillBindingChange(long revision,IDictionary<EveId,EveId> previous,IDictionary<EveId,EveId> current,PNSkillTransition transition)
    {Revision=revision;Previous=Freeze(previous);Current=Freeze(current);Transition=transition;}
    private static IReadOnlyDictionary<EveId,EveId> Freeze(IDictionary<EveId,EveId> values)=>new System.Collections.ObjectModel.ReadOnlyDictionary<EveId,EveId>(new Dictionary<EveId,EveId>(values));
}
// Resolver owns selection only. Gameplay execution remains in PNSkill/behavior.
public sealed class PNSkillBindingState:IDisposable
{
    private readonly Dictionary<EveId,EveId> bases;
    private Dictionary<EveId,EveId> current;
    private readonly List<PNSkillBindingLease> leases=new List<PNSkillBindingLease>();
    private readonly Queue<(Action Run,Action<Exception> Abort)> pending=new Queue<(Action,Action<Exception>)>();
    private readonly Func<bool> authority;
    private readonly Action<EveId> validate;
    private readonly Action<IReadOnlyDictionary<EveId,EveId>> prepare;
    private bool draining,disposed;
    private long next;
    public long Revision {get;private set;}=1;
    public bool IsFaulted {get;private set;}
    public event Action<PNSkillBindingChange> Changed;
    public event Action<Exception> Failed;
    public IReadOnlyDictionary<EveId,EveId> Snapshot()=>new System.Collections.ObjectModel.ReadOnlyDictionary<EveId,EveId>(new Dictionary<EveId,EveId>(current));
    public bool TryGet(EveId slot,out EveId profile)=>current.TryGetValue(slot,out profile);
    public PNSkillBindingState(IEnumerable<KeyValuePair<EveId,EveId>> slots,Action<EveId> validate,Func<bool> authority=null,Action<IReadOnlyDictionary<EveId,EveId>> prepare=null)
    {this.prepare=prepare;bases=new Dictionary<EveId,EveId>();this.validate=validate??throw new ArgumentNullException(nameof(validate));this.authority=authority??(()=>true);foreach(var pair in slots){this.validate(pair.Value);bases.Add(pair.Key,pair.Value);}this.prepare?.Invoke(bases);current=new Dictionary<EveId,EveId>(bases);}
    public PNSkillBindingReceipt AcquireOverride(string owner,PNSkillOverrideRequest request)
    {
        if(disposed||IsFaulted||!authority())throw new InvalidOperationException("Binding mutation requires live authority.");
        if(string.IsNullOrWhiteSpace(owner)||request==null)throw new ArgumentException("Override owner/request required.");
        var receipt=new PNSkillBindingReceipt{Report=Report};var lease=new PNSkillBindingLease(this,checked(++next),owner,request);receipt.Lease=lease;
        Submit(()=>{try{foreach(var pair in request.Slots){if(!bases.ContainsKey(pair.Key))throw new ArgumentException("Unknown slot: "+pair.Key);validate(pair.Value);}prepare?.Invoke(request.Slots);if(lease.Removed){receipt.Finish(PNSkillBindingReceiptStatus.Applied);return;}leases.Add(lease);lease.IsActive=true;Publish(request.ApplyTransition);receipt.Finish(PNSkillBindingReceiptStatus.Applied);}catch(Exception e){lease.Removed=true;lease.IsActive=false;leases.Remove(lease);receipt.Finish(PNSkillBindingReceiptStatus.Failed,e);Report(e);}},error=>{lease.Removed=true;lease.IsActive=false;receipt.Finish(PNSkillBindingReceiptStatus.Failed,error);});
        return receipt;
    }
    internal void Release(PNSkillBindingLease lease)
    {if(disposed)return;Submit(()=>{if(leases.Remove(lease)){lease.IsActive=false;Publish(lease.Request.ReleaseTransition);}});}
    private void Publish(PNSkillTransition transition)
    {
        var selected=new Dictionary<EveId,EveId>(bases);
        foreach(var lease in leases.Where(x=>x.IsActive).OrderBy(x=>x.Request.Priority).ThenBy(x=>x.Sequence))foreach(var pair in lease.Request.Slots)selected[pair.Key]=pair.Value;
        if(selected.All(x=>current.TryGetValue(x.Key,out var value)&&value==x.Value))return;
        var previous=current;current=selected;var change=new PNSkillBindingChange(checked(++Revision),previous,current,transition);
        foreach(Action<PNSkillBindingChange> observer in Changed?.GetInvocationList()??Array.Empty<Delegate>())try{observer(change);}catch(Exception e){Report(e);}
    }
    private void Report(Exception error){foreach(Action<Exception> observer in Failed?.GetInvocationList()??Array.Empty<Delegate>())try{observer(error);}catch{}}
    private void Submit(Action mutation,Action<Exception> abort=null)
    {
        if(disposed||IsFaulted){abort?.Invoke(new InvalidOperationException("Binding state unavailable."));return;}
        if(pending.Count>=256){var error=new InvalidOperationException("Binding pending queue exhausted.");abort?.Invoke(error);Fault(error);return;}
        pending.Enqueue((mutation,abort));if(draining)return;
        draining=true;
        try{int count=0;while(pending.Count>0){if(++count>64){Fault(new InvalidOperationException("Binding mutation cycle exhausted."));break;}pending.Dequeue().Run();}}
        finally{draining=false;}
    }
    private void Fault(Exception error)
    {
        if(IsFaulted)return;IsFaulted=true;
        while(pending.Count>0)pending.Dequeue().Abort?.Invoke(error);
        foreach(var lease in leases){lease.IsActive=false;lease.Removed=true;}leases.Clear();
        Publish(PNSkillTransition.CancelCurrent);Report(error);
    }
    public void Dispose(){if(disposed)return;disposed=true;foreach(var lease in leases){lease.IsActive=false;lease.Removed=true;}leases.Clear();while(pending.Count>0)pending.Dequeue().Abort?.Invoke(new ObjectDisposedException(nameof(PNSkillBindingState)));Changed=null;Failed=null;}
}
