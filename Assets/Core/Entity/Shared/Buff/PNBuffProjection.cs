using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

public enum PNBuffCaptureStatus { Ready, Busy, Faulted }

public sealed class PNBuffFrame
{
    public PNBuffWireMessage State { get; }
    public double PresentationTime { get; }
    public IReadOnlyDictionary<EveId,long> Groups { get; }
    public long Revision => State.Sequence;
    public PNBuffFrame(PNBuffWireMessage state,double presentationTime,IEnumerable<KeyValuePair<EveId,long>> groups)
    {
        if(state==null||state.Kind!=PNBuffWireKind.Snapshot||state.Sequence<0)throw new ArgumentException("Buff baseline required.");
        PNBuffCollection.Seconds(presentationTime,true);State=state;PresentationTime=presentationTime;
        var copy=groups.ToDictionary(x=>x.Key,x=>x.Value);
        if(copy.Any(x=>!x.Key.IsValid||x.Value<1)||copy.Values.Distinct().Count()!=copy.Count||!new HashSet<EveId>(state.States.Select(x=>x.BuffId)).SetEquals(copy.Keys))throw new FormatException("Buff group order.");
        Groups=new System.Collections.ObjectModel.ReadOnlyDictionary<EveId,long>(copy);
    }
}
public sealed class PNBuffChange
{
    public PNBuffWireMessage State { get; }
    public PNBuffWireState FinalState { get; }
    public double PresentationTime { get; }
    public IReadOnlyDictionary<EveId,long> Groups { get; }
    public PNBuffChange(PNBuffWireMessage state,PNBuffWireState finalState,double time,IEnumerable<KeyValuePair<EveId,long>> groups)
    {State=state??throw new ArgumentNullException(nameof(state));FinalState=finalState;PNBuffCollection.Seconds(time,true);PresentationTime=time;Groups=new System.Collections.ObjectModel.ReadOnlyDictionary<EveId,long>(groups.ToDictionary(x=>x.Key,x=>x.Value));}
}

public sealed partial class PNBuffReplica
{
    private static bool SameState(PNBuffWireState a,PNBuffWireState b)=>a!=null&&b!=null&&a.InstanceId==b.InstanceId&&a.Revision==b.Revision&&a.BuffId==b.BuffId&&a.ProfileId==b.ProfileId&&a.SourceId==b.SourceId&&a.Stacks==b.Stacks&&a.Suspended==b.Suspended&&a.Numbers.Equals(b.Numbers)&&a.Clock==b.Clock&&a.BuffTime==b.BuffTime&&a.AnchorTime==b.AnchorTime&&a.Rate==b.Rate&&a.Expiration==b.Expiration&&a.MatchesDynamic(b.DynamicData,b.PayloadVersion);
    internal void ValidateTypedFrame(PNBuffFrame frame)
    {
        if(Epoch==0)return;
        if(frame.Revision<Sequence||frame.State.States.Any(s=>retired.Contains(s.InstanceId)||!states.ContainsKey(s.InstanceId)&&s.InstanceId<=highestInstance))throw new FormatException("Buff snapshot resurrects retired/stale state.");
        if(frame.Revision==Sequence&&(frame.State.States.Count!=states.Count||frame.State.States.Any(s=>!states.TryGetValue(s.InstanceId,out var old)||!SameState(old,s))||frame.Groups.Any(x=>!groups.TryGetValue(x.Key,out var order)||order!=x.Value)))throw new FormatException("Conflicting same-revision Buff baseline.");
    }
    internal void ValidateTypedChange(PNBuffChange change)
    {
        if(change.State.Kind==PNBuffWireKind.Removed)
        {ValidateState(change.FinalState);if(!states.TryGetValue(change.State.RemovedId,out var old)||!SameState(old,change.FinalState))throw new FormatException("Buff final removal state mismatch.");}
        else if(change.FinalState!=null)throw new FormatException("Unexpected Buff final state.");
    }
    internal void ApplyTypedChange(PNBuffChange change)
    {
        ValidateTypedChange(change);var order=change.Groups.ToDictionary(x=>x.Key,x=>x.Value);
        if(change.FinalState!=null&&!order.ContainsKey(change.FinalState.BuffId)&&groups.TryGetValue(change.FinalState.BuffId,out long prior))order.Add(change.FinalState.BuffId,prior);
        SetGroups(order,false);ApplyLifecycle(change.State,change.PresentationTime);SetGroups(change.Groups);
    }
    private void Present(Action action)
    {
        if(publicationDepth==0){action();return;}
        if(publicationNotices.Count>=16384)throw new InvalidOperationException("Buff presentation journal bound.");
        publicationNotices.Enqueue(action);
    }
    internal IReadOnlyDictionary<EveId,long> GroupSnapshot()=>new System.Collections.ObjectModel.ReadOnlyDictionary<EveId,long>(new Dictionary<EveId,long>(groups));
    internal void SetGroups(IReadOnlyDictionary<EveId,long> incomingGroups,bool validateMembership=true)
    {
        if(validateMembership&&!new HashSet<EveId>(states.Values.Select(x=>x.BuffId)).SetEquals(incomingGroups.Keys)||incomingGroups.Any(x=>x.Value<=0)||incomingGroups.Values.Distinct().Count()!=incomingGroups.Count)throw new FormatException("Buff group membership/order.");
        groups.Clear();foreach(var pair in incomingGroups)groups.Add(pair.Key,pair.Value);
        if(groups.Count>0)groupSequence=Math.Max(groupSequence,groups.Values.Max());
    }
    // Seed data only: no renderer, subscription or resource is shared before publication.
    internal void Seed(PNBuffReplica before)
    {
        if(before==null)return;
        foreach(var pair in before.states)states.Add(pair.Key,pair.Value);
        foreach(var pair in before.groups)groups.Add(pair.Key,pair.Value);
        foreach(var pair in before.shown)shown.Add(pair.Key,pair.Value);
        foreach(var id in before.countdownOverflowReported)countdownOverflowReported.Add(id);foreach(var id in before.overflowingCountdowns)overflowingCountdowns.Add(id);
        foreach(var id in before.seenCues)seenCues.Add(id);foreach(var id in before.cueOrder)cueOrder.Enqueue(id);
        foreach(var id in before.retired)retired.Add(id);foreach(var id in before.retiredOrder)retiredOrder.Enqueue(id);
        groupSequence=before.groupSequence;uiSequence=before.uiSequence;highestInstance=before.highestInstance;
        Epoch=before.Epoch;Sequence=before.Sequence;Status=before.Status;earliestCountdownChange=before.earliestCountdownChange;
    }
    internal void AdoptPresentation(PNBuffReplica before)
    {
        if(before==null)return;
        foreach(var pair in before.presenters)presenters.Add(pair.Key,pair.Value);before.presenters.Clear();
        before.observers.Changed+=observers.Changed;before.observers.PresenterFailure+=observers.PresenterFailure;before.observers.Cue+=observers.Cue;observers=before.observers;
        before.SnapshotRequested=null;before.Failed=null;
        before.publicationNotices.Clear();before.states.Clear();before.groups.Clear();before.shown.Clear();before.buffered.Clear();before.incoming.Clear();
        before.retired.Clear();before.retiredOrder.Clear();before.seenCues.Clear();before.cueOrder.Clear();before.countdownOverflowReported.Clear();before.overflowingCountdowns.Clear();before.overflowBuffer.Clear();before.countdownBuffer.Clear();before.Status=PNBuffReplicaStatus.Closed;
    }
    internal void DiscardPrepared()
    {
        publicationNotices.Clear();publicationDepth=0;states.Clear();Status=PNBuffReplicaStatus.Closed;
    }
}

public sealed partial class PNBuffManager
{
    private readonly Queue<Action> projectionPublications=new Queue<Action>();
    private bool publishingProjection;
    private void PublishProjection(Action action)
    {
        if(projectionPublications.Count>=256){ProjectionFault(new InvalidOperationException("Buff publication queue bound."));return;}
        projectionPublications.Enqueue(action);if(publishingProjection)return;publishingProjection=true;
        try{int count=0;while(projectionPublications.Count>0){if(++count>10000)throw new InvalidOperationException("Buff publication cycle.");projectionPublications.Dequeue()();}}
        catch(Exception error){projectionPublications.Clear();ProjectionFault(error);}
        finally{publishingProjection=false;}
    }
    private PresentationProxy presentationProxy;
    private sealed class PresentationProxy:PNIBuffPresentationSource,IDisposable
    {
        private PNBuffReplica source;private readonly PNBuffManager owner;
        public PresentationProxy(PNBuffManager owner){this.owner=owner;}
        public event Action<PNBuffPresentationChange> PresentationChanged;
        public void Bind(PNBuffReplica next){if(ReferenceEquals(source,next))return;if(source!=null)source.PresentationChanged-=Changed;source=next;if(source!=null)source.PresentationChanged+=Changed;}
        private void Changed(PNBuffPresentationChange change){foreach(Action<PNBuffPresentationChange> listener in PresentationChanged?.GetInvocationList()??Array.Empty<Delegate>())try{listener(change);}catch(Exception error){owner.Report(error);}}
        public IReadOnlyList<PNBuffPresentationSnapshot> PresentationSnapshot()=>source?.PresentationSnapshot()??Array.Empty<PNBuffPresentationSnapshot>();
        public void Dispose(){Bind(null);PresentationChanged=null;}
    }
    private PNIBuffPresentationSource StablePresentation(){if(presentationProxy==null)presentationProxy=new PresentationProxy(this);presentationProxy.Bind(replica);return presentationProxy;}
    private bool typedTransport;
    private readonly Dictionary<long,PNBuffWireState> typedStates=new Dictionary<long,PNBuffWireState>();
    private TypedStage typedStage;
    public event Action<PNBuffChange> StateChanged;
    public event Action<PNBuffOptionalCueMessage,double> OptionalCuePublished;
    public event Action<Exception> ProjectionFailed;
    public long AppliedRevision => replica?.Sequence??0;
    public bool HasBaseline => replica!=null&&replica.Status==PNBuffReplicaStatus.Live;
    public void UseTypedTransport(){typedTransport=true;}
    private void ProjectionFault(Exception error)
    { RuntimeFailure=error;foreach(Action<Exception> listener in ProjectionFailed?.GetInvocationList()??Array.Empty<Delegate>())try{listener(error);}catch(Exception failure){Report(failure);} }
    private void ObserveTyped(PNBuffWireMessage message,double observed)
    {
        PNBuffWireState final=null;
        if(message.Kind==PNBuffWireKind.Removed){typedStates.TryGetValue(message.RemovedId,out final);typedStates.Remove(message.RemovedId);}
        else foreach(var state in message.States)typedStates[state.InstanceId]=state;
        // Local presentation has the same authoritative order, and has already consumed this change.
        if(StateChanged==null)return;
        var change=new PNBuffChange(message,final,observed,replica.GroupSnapshot());
        foreach(Action<PNBuffChange> listener in StateChanged.GetInvocationList())try{listener(change);}catch(Exception error){ProjectionFault(error);}
    }
    private void ObserveTypedCue(PNBuffOptionalCueMessage message,double observed)
    {foreach(Action<PNBuffOptionalCueMessage,double> listener in OptionalCuePublished?.GetInvocationList()??Array.Empty<Delegate>())try{listener(message,observed);}catch(Exception error){ProjectionFault(error);} }
    public PNBuffCaptureStatus TryCaptureState(out PNBuffFrame frame)
    {
        frame=null;if(StateCaptureFaulted)return PNBuffCaptureStatus.Faulted;
        if(!CanCaptureReplication)return PNBuffCaptureStatus.Busy;
        try
        {
            Publisher.Capture(presentationTime,clock!=null&&clock.isActiveAndEnabled?clock.Scale:1,clock!=null&&clock.isActiveAndEnabled?Math.Max(0,clock.RawScale):1);
            if(StateCaptureFaulted)return PNBuffCaptureStatus.Faulted;
            var snapshot=Publisher.Snapshot();foreach(var state in snapshot.States)typedStates[state.InstanceId]=state;
            frame=new PNBuffFrame(snapshot,presentationTime,replica.GroupSnapshot());return PNBuffCaptureStatus.Ready;
        }
        catch(Exception error){ProjectionFault(error);return PNBuffCaptureStatus.Faulted;}
    }
    public EveIReplicaStage StageState(PNBuffFrame frame,Guid host,long generation)
    {
        if(context==null||!context.IsValid||session.HasAuthority||frame.State.SessionId!=host||frame.State.TargetId!=context.Identity.Id||frame.State.Epoch!=generation)throw new InvalidOperationException("Invalid typed Buff stage binding.");
        if(typedStage!=null){typedStage.AppendFrame(frame);return typedStage;}
        return typedStage=new TypedStage(this,frame);
    }
    public void ApplyChange(PNBuffChange change,Guid host,long generation)
    {
        if(typedStage!=null){typedStage.Append(change);return;}
        if(!IsReady||session.HasAuthority||!HasBaseline||change.State.SessionId!=host||change.State.Epoch!=generation||change.State.Sequence!=replica.Sequence+1)throw new FormatException("Buff live revision/binding.");
        using(var probe=new PNBuffReplica(session.Registry,host,context.Identity.Id,render:false)){probe.Seed(replica);probe.ApplyTypedChange(change);probe.DiscardPrepared();}
        replica.ApplyTypedChange(change);presentationTime=change.PresentationTime;
    }
    public void ApplyOptionalCue(PNBuffOptionalCueMessage cue,double time)
    {if(IsReady&&!session.HasAuthority)replica?.Receive(cue,AuthoritativePresentationTime?.Invoke()??time);}
    private sealed class TypedStage:EveIReplicaStage
    {
        private readonly PNBuffManager owner;private readonly EveEntityLoadContext admitted;
        private readonly PNBuffReplica before,candidate;private IDisposable deferred;
        private bool committed,published,disposed;private double time;private readonly double beforeTime;
        public TypedStage(PNBuffManager owner,PNBuffFrame frame)
        {
            this.owner=owner;admitted=owner.context;before=owner.replica;beforeTime=owner.presentationTime;time=frame.PresentationTime;
            candidate=new PNBuffReplica(owner.session.Registry,frame.State.SessionId,frame.State.TargetId,owner.target,owner.RenderPresentation);
            if(before==null)candidate.PresenterFailed+=owner.Report;
            candidate.Failed+=(code,message)=>owner.session?.FailConnection(code.ToString(),message);
            candidate.Seed(before);deferred=candidate.DeferPresentation();
            try{candidate.ValidateTypedFrame(frame);candidate.SetGroups(frame.Groups,false);candidate.ApplyLifecycle(frame.State,time);candidate.SetGroups(frame.Groups);}catch{candidate.DiscardPrepared();throw;}
        }
        public void Commit(){if(disposed||!ReferenceEquals(owner.context,admitted)||!admitted.IsValid)throw new InvalidOperationException("Stale Buff transaction.");owner.replica=candidate;owner.presentationTime=time;committed=true;}
        public void Append(PNBuffChange change)
        {
            if(disposed||published||change.State.Sequence!=candidate.Sequence+1||change.State.SessionId!=candidate.SessionId||change.State.Epoch!=candidate.Epoch)throw new FormatException("Buff staged change identity/revision.");
            candidate.ApplyTypedChange(change);time=change.PresentationTime;
        }
        public void AppendFrame(PNBuffFrame frame)
        {if(disposed||published)throw new FormatException("Closed pending Buff baseline.");candidate.ValidateTypedFrame(frame);candidate.SetGroups(frame.Groups,false);candidate.ApplyLifecycle(frame.State,frame.PresentationTime);candidate.SetGroups(frame.Groups);time=frame.PresentationTime;}
        public void Published(){if(published)return;if(disposed||!committed)throw new InvalidOperationException("Uncommitted Buff transaction.");published=true;candidate.AdoptPresentation(before);owner.StablePresentation();deferred.Dispose();deferred=null;owner.typedStage=null;}
        public void Dispose(){if(disposed)return;disposed=true;if(!published){if(committed&&ReferenceEquals(owner.replica,candidate)){owner.replica=before;owner.presentationTime=beforeTime;}candidate.DiscardPrepared();}if(ReferenceEquals(owner.typedStage,this))owner.typedStage=null;deferred=null;}
    }
    private void UnloadTyped(){typedStage?.Dispose();typedStage=null;projectionPublications.Clear();presentationProxy?.Dispose();presentationProxy=null;typedStates.Clear();typedTransport=false;StateChanged=null;OptionalCuePublished=null;ProjectionFailed=null;}
}
