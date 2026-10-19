#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EveEngine;
using UnityEngine;

public sealed class PNWorldTransferReceipt
{
    public Guid Id { get; internal set; }
    public PNWorldTransferState State { get; internal set; }
    public string Error { get; internal set; }
    public bool IsTerminal=>State==PNWorldTransferState.Completed||State==PNWorldTransferState.Aborted||State==PNWorldTransferState.Failed;
    public bool CleanupPending { get; internal set; }
}
// Main-thread transaction orchestration. Preparation shares the entity budget;
// membership changes are committed by the world's fixed-boundary driver.
public sealed class PNWorldTransitionCoordinator : IDisposable,IPNWorldTransferTransportOwner
{
    private sealed class ActorWork
    {
        public PNWorldTransferContext Context;
        public readonly List<IPNWorldTransferParticipant> Participants=new List<IPNWorldTransferParticipant>();
        public int Prepared;
        public IDisposable Hold,SourceLease;
        public EveEntityIdentity.WorldChange Change;
        public Vector3 Position,Velocity,Angular;
        public Quaternion Rotation;
        public bool MembershipApplied;
        public PNWorldStateRecord State;
        public PNEntityRequest SourcePresentation;
        public IDisposable ReplacementHold;
        public int FinishedCallbacks,DisposedParticipants;
    }
    private sealed class Work
    {
        public PNWorldTransferRequest Request;
        public PNWorldTransferReceipt Receipt;
        public string Signature;
        public long[] Entities;
        public readonly List<ActorWork> Actors=new List<ActorWork>();
        public IDisposable DestinationLease,Job;
        public readonly List<IDisposable> SourcePins=new List<IDisposable>();
        public double Started;
        public bool Cancelled,Committed,CutoverStarted,Completing,FinalSuccess;
        public ulong CommitBoundary;
    }
    private readonly EveSessionContext session;
    private readonly PNWorldRuntime worlds;
    private readonly PNPlayerFactory factory;
    private readonly EveEntityPreparationScheduler scheduler;
    private readonly Dictionary<Guid,Work> active=new Dictionary<Guid,Work>();
    private readonly Dictionary<Guid,(string,PNWorldTransferReceipt)> history=new Dictionary<Guid,(string,PNWorldTransferReceipt)>();
    private readonly HashSet<long> reservedActors=new HashSet<long>();
    private bool disposed;
    private ulong boundary;
    private readonly PNWorldStateService states;
    public IPNWorldTransferTransport Transport { get; set; }
    public Func<PNWorldTransferRequest,bool> Admit { get; set; }=request=>true;
    public int PendingCount=>active.Count;
    // Local-participant observation for the transition screen: the in-flight transfer that includes this actor, if any.
    public bool TryGetActive(long entity,out PNWorldTransferRequest request,out PNWorldTransferState state)
    {
        foreach(var work in active.Values)
            if(Array.IndexOf(work.Entities,entity)>=0){request=work.Request;state=work.Receipt.State;return true;}
        request=null;state=default;return false;
    }
    public IReadOnlyList<PNWorldTransferReceipt> SnapshotReceipts()=>history.Values.Select(x=>x.Item2).ToArray();
    public double MaximumCallbackMilliseconds { get; private set; }
    public PNWorldTransitionCoordinator(EveSessionContext session,PNWorldRuntime worlds,PNPlayerFactory factory,EveEntityPreparationScheduler scheduler)
    {this.session=session;this.worlds=worlds;this.factory=factory;this.scheduler=scheduler;states=new PNWorldStateService(session);session.RegisterCleanup(Dispose);}
    public PNWorldTransferReceipt Request(PNWorldTransferRequest request)
    {
        if(disposed||!session.IsActive||!session.HasAuthority)throw new InvalidOperationException("Only active authority requests transfers.");
        if(session.Role==EveSessionRole.Host&&Transport==null)throw new InvalidOperationException("Host transfer transport has not attached.");
        if(request==null)throw new ArgumentNullException(nameof(request));
        var ids=request.EntityIds.ToArray();string signature=request.Signature;
        if(history.TryGetValue(request.Id,out var previous))
        {if(previous.Item1!=signature)throw new InvalidOperationException("Transfer ID was reused with different participants/destination.");return previous.Item2;}
        if(active.Count>=32||history.Count>=4096||request.Actors.Count>(Transport?.MaximumParticipants??PNGameBootstrap.Config.Network.MaximumClients+1)||ids.Any(x=>reservedActors.Contains(x)))throw new InvalidOperationException("Transfer capacity or per-actor reservation exceeded.");
        if(!ReferenceEquals(request.Destination.Session,session)||!Admit(request))throw new InvalidOperationException("Mode rejected the destination/transfer.");
        var work=new Work{Request=request,Entities=ids,Signature=signature,Started=Time.realtimeSinceStartupAsDouble,Receipt=new PNWorldTransferReceipt{Id=request.Id,State=PNWorldTransferState.Requested}};
        foreach(var id in ids)reservedActors.Add(id);active.Add(request.Id,work);history.Add(request.Id,(signature,work.Receipt));
        try
        {
            work.DestinationLease=request.Destination.AcquireLease("pending transfer");
            foreach(var source in request.Actors.Select(x=>x.LoadContext.Identity.World).Distinct())work.SourcePins.Add(source.AcquireLease("pending transfer source"));
            work.Job=scheduler.SchedulePreparation(()=>SafeStep(work),error=>Fail(work,error));
        }
        catch(Exception error){Fail(work,error);throw;}
        return work.Receipt;
    }
    public bool Cancel(Guid id)
    {if(!active.TryGetValue(id,out var work)||work.Committed||work.CutoverStarted)return false;work.Cancelled=true;return true;}
    private bool SafeStep(Work work)
    {try{return Step(work);}catch(Exception error){Fail(work,error);return !work.Receipt.CleanupPending;}}
    private bool Step(Work work)
    {
        if(work.Receipt.IsTerminal&&!work.Receipt.CleanupPending)return true;
        if(work.Receipt.CleanupPending){Finish(work,false);return !work.Receipt.CleanupPending;}
        if(!session.IsActive||work.Cancelled){Fail(work,new OperationCanceledException("Transfer cancelled before commit."));return work.Receipt.IsTerminal&&!work.Receipt.CleanupPending;}
        if(Time.realtimeSinceStartupAsDouble-work.Started>60){Fail(work,new TimeoutException("Transfer transition deadline exceeded."));return work.Receipt.IsTerminal&&!work.Receipt.CleanupPending;}
        switch(work.Receipt.State)
        {
            case PNWorldTransferState.Requested:work.Receipt.State=PNWorldTransferState.Validating;break;
            case PNWorldTransferState.Validating:
                foreach(var actor in work.Request.Actors)
                {
                    if(actor?.IsReady!=true||!ReferenceEquals(actor.LoadContext.Session,session)||actor.LoadContext.Identity.World?.CanExecute!=true||actor.LoadContext.Identity.IsGameplayHeld)throw new InvalidOperationException("Transfer source is stale/held/unavailable.");
                }
                work.Receipt.State=PNWorldTransferState.PreparingDestination;break;
            case PNWorldTransferState.PreparingDestination:
                if(work.Request.Destination.State==EveWorldState.Loading||work.Request.Destination.State==EveWorldState.Creating)return false;
                if(!work.Request.Destination.CanExecute)throw new InvalidOperationException("Destination cannot execute.");
                if(work.Actors.Count==0)PrepareActors(work);
                foreach(var actor in work.Actors)
                {
                    ValidateActor(actor);
                    while(actor.Prepared<actor.Participants.Count)
                    {if(!actor.Participants[actor.Prepared].Prepare(actor.Context))return false;actor.Prepared++;}
                    if(Transport!=null&&!Transport.IsLoaded(actor.Context))return false;
                }
                work.Receipt.State=PNWorldTransferState.Holding;break;
            case PNWorldTransferState.Holding:
                foreach(var actor in work.Actors)
                {
                    ValidateActor(actor);
                    if(actor.Context.Actor.GetComponent<PNBuffManager>() is PNBuffManager buffs&&!buffs.CanCaptureState)return false;
                }
                foreach(var actor in work.Actors)
                {
                    foreach(var participant in actor.Participants)participant.Hold(actor.Context);
                    if(actor.Context.Replacement){var map=work.Actors.ToDictionary(x=>x.Context.Entity.ToString(),x=>x.Context.Actor);actor.State=states.Capture(new PNWorldStateContext(actor.Context.Source,actor.Context.Actor,map),session.Registry.Get<PNWorldDefinition>().Get(actor.Context.Source.DefinitionId).Policy.ActorStateProviders);}
                    var body=actor.Context.Actor.GetComponent<Rigidbody>();actor.Position=actor.Context.Actor.transform.position;actor.Rotation=actor.Context.Actor.transform.rotation;
                    if(body!=null){actor.Velocity=body.linearVelocity;actor.Angular=body.angularVelocity;}
                    actor.Hold=actor.Context.Actor.LoadContext.Identity.HoldGameplay();Transport?.Hold(actor.Context);
                }
                if(work.Request.ReplacementTemplate!=null)foreach(var actor in work.Actors)PrepareReplacement(work,actor);
                work.Receipt.State=PNWorldTransferState.Committing;break;
            case PNWorldTransferState.CatchingUp:
                if(boundary<=work.CommitBoundary)return false;
                foreach(var actor in work.Actors)
                {
                    if(actor.Context.Actor?.IsReady!=true)throw new InvalidOperationException("Participant retired after commit; mode cleanup owns it.");
                    if(Transport!=null&&!Transport.IsApplied(actor.Context))return false;
                }
                Finish(work,true);return !work.Receipt.CleanupPending;
        }
        return false;
    }
    private void PrepareActors(Work work)
    {
        foreach(var loader in work.Request.Actors)
        {
            var identity=loader.LoadContext.Identity;
            PNEntityRequest target;
            if(loader.GetComponent<PNPlayerStat>()!=null)target=factory.Player(identity.OwnerId,work.Request.Destination,work.Request.Slot,work.Request.Spawn);
            else
            {
                var build=worlds.Get(work.Request.Destination);var slot=work.Request.Slot??(build.Slots.Count==1?build.Slots[0].Id:throw new InvalidOperationException("Explicit content slot required."));
                var marker=loader.GetComponent<PNFactoryEntity>();
                string template=work.Request.TargetTemplate??marker?.Template??throw new InvalidOperationException("Authored actor transfer requires a registered destination template.");
                session.Registry.Get<PNEntityTemplateDefinition>().Get(EveId.Parse(template)).Resolve(build,slot,work.Request.Spawn);
                target=new PNEntityRequest{World=work.Request.Destination,WorldIncarnation=work.Request.Destination.Incarnation,ContentSlot=slot,SpawnEntry=work.Request.Spawn,Template=template,Character=loader.GetComponent<PNStat>().CharacterId.ToString(),Owner=identity.OwnerId,Position=loader.transform.position,SceneKey=loader.GetComponent<EveSceneEntityKey>()?.SceneKey??marker?.SceneKey,EntityKey=""};
            }
            if(work.Request.ReplacementTemplate!=null){target.Template=work.Request.ReplacementTemplate;session.Registry.Get<PNEntityTemplateDefinition>().Get(EveId.Parse(target.Template)).Resolve(worlds.Get(target.World),target.ContentSlot,target.SpawnEntry);}
            if(work.Request.ReplacementTemplate==null&&EveId.Parse(target.Character)!=(identity.CharacterId.IsValid?identity.CharacterId:loader.GetComponent<PNStat>().CharacterId))throw new InvalidOperationException("Destination character differs; request explicit replacement.");
            var prototype=loader.GetComponent<PNFactoryEntity>()?.Prototype;
            if(work.Request.ReplacementTemplate==null&&prototype!=null&&session.Registry.Get<PNEntityTemplateDefinition>().Get(EveId.Parse(target.Template)).Resolve(worlds.Get(target.World),target.ContentSlot,target.SpawnEntry)!=prototype)throw new InvalidOperationException("Destination template selects a different prototype; request explicit replacement.");
            var context=new PNWorldTransferContext(work.Request.Id,loader,work.Request.Destination,target,work.Request.ReplacementTemplate!=null);
            var actor=new ActorWork{Context=context,SourceLease=context.Source.AcquireLease("transfer source")};work.Actors.Add(actor);
            if(target.Player)actor.SourcePresentation=factory.Player(identity.OwnerId,identity.World,identity.ContentSlot,loader.GetComponent<PNFactoryEntity>()?.SpawnEntry);
            var source= session.Registry.Get<PNWorldDefinition>().Get(context.Source.DefinitionId).Policy;
            var destination=session.Registry.Get<PNWorldDefinition>().Get(context.Destination.DefinitionId).Policy;
            foreach(var id in source.TransferParticipants.Concat(destination.TransferParticipants).Distinct())actor.Participants.Add(session.Registry.Get<PNWorldTransferParticipantDefinition>().Get(id).Create());
            Transport?.Validate(context);Transport?.Prepare(context);
        }
    }
    private void PrepareReplacement(Work work,ActorWork actor)
    {
        var context=actor.Context;
        var candidate=scheduler.Enqueue(()=>factory.Get(context.Target),loader=>
        {
            factory.Configure(loader,context.Target);
            var identity=loader.GetComponent<EveEntityIdentity>();
            EveEntityId id;do{id=session.Role==EveSessionRole.Host?session.Entities.AllocateHostId():session.Entities.AllocateOfflineId();}while(session.Entities.IsReserved(id));
            identity.Assign(id,EveId.Parse(context.Target.Character),context.Owner);
            actor.ReplacementHold=identity.HoldGameplay();
            var stage=loader.GetComponent<PNWorldRestoreStage>()??loader.gameObject.AddComponent<PNWorldRestoreStage>();
            stage.Restore=load=>
            {
                var map=work.Actors.ToDictionary(x=>x.Context.Entity.ToString(),x=>x.Context.ReplacementCandidate.Loader);
                states.Restore(new PNWorldStateContext(context.Destination,loader,map),actor.State,session.Registry.Get<PNWorldDefinition>().Get(context.Destination.DefinitionId).Policy.ActorStateProviders);
            };
        },factory.Return,null,error=>Fail(work,error),null,loader=>false);
        context.StageReplacement(candidate);scheduler.AssociateWorld(candidate,context.Destination);
    }
    private void ValidateActor(ActorWork actor)
    {if(actor.Context.Actor?.IsReady!=true||!ReferenceEquals(actor.Context.Actor.LoadContext.Identity.World,actor.Context.Source)||!actor.Context.Source.CanExecute)throw new InvalidOperationException("Source participant changed during preparation.");Transport?.Validate(actor.Context);}
    public void FixedBoundary(ulong value)
    {
        if(value<=boundary)return;boundary=value;
        foreach(var work in active.Values.Where(x=>x.Receipt.State==PNWorldTransferState.Committing).ToArray())
        {
            var watch=Stopwatch.StartNew();
            try
            {
                if(work.Cancelled||Time.realtimeSinceStartupAsDouble-work.Started>60){Fail(work,new OperationCanceledException("Transfer cancelled/expired before fixed commit."));continue;}
                foreach(var actor in work.Actors)ValidateActor(actor);
                if(Transport!=null&&work.Actors.Any(x=>!Transport.IsHeld(x.Context)))continue;
                if(work.Request.ReplacementTemplate!=null)
                {
                    if(work.Actors.Any(x=>x.Context.ReplacementCandidate?.Loader?.State!=EveEntityLoadState.Prepared))continue;
                    work.CutoverStarted=true;
                    scheduler.PublishPrepared(work.Actors.Select(x=>x.Context.ReplacementCandidate).ToArray());
                    foreach(var actor in work.Actors)
                    {
                        actor.Context.ActivateReplacement();
                        PNWorldSaveActorKey.Copy(actor.Context.PreviousActor,actor.Context.Actor);
                        foreach(var participant in actor.Participants)participant.Commit(actor.Context);
                        Transport?.Rebind(actor.Context);if(Transport==null)factory.Published(actor.Context.Actor);
                        scheduler.RetirePublished(actor.Context.PreviousActor);
                    }
                    work.Committed=true;work.CommitBoundary=boundary;work.Receipt.State=PNWorldTransferState.CatchingUp;continue;
                }
                foreach(var actor in work.Actors)actor.Change=actor.Context.Actor.LoadContext.Identity.PrepareWorldChange(actor.Context.Destination,actor.Context.Target.ContentSlot);
                foreach(var actor in work.Actors)
                {
                    actor.Change.Apply();actor.MembershipApplied=true;factory.MoveWorldOwnership(actor.Context.Actor,actor.Context.Destination);scheduler.MovePublishedWorld(actor.Context.Actor,actor.Context.Destination);factory.RebindWorldPresentation(actor.Context.Actor,actor.Context.Target);
                    var body=actor.Context.Actor.GetComponent<Rigidbody>();actor.Context.Actor.transform.position=actor.Context.Target.Position;
                    if(body!=null){body.position=actor.Context.Target.Position;if(!body.isKinematic){body.linearVelocity=actor.Velocity;body.angularVelocity=actor.Angular;}}
                    foreach(var participant in actor.Participants)participant.Commit(actor.Context);
                }
                work.CutoverStarted=true;
                foreach(var actor in work.Actors){Transport?.Rebind(actor.Context);if(Transport==null)factory.Published(actor.Context.Actor);actor.Change.Commit();}
                work.Committed=true;work.CommitBoundary=boundary;work.Receipt.State=PNWorldTransferState.CatchingUp;
            }
            catch(Exception error){Fail(work,error);}
            finally{MaximumCallbackMilliseconds=Math.Max(MaximumCallbackMilliseconds,watch.Elapsed.TotalMilliseconds);}
        }
    }
    private void Fail(Work work,Exception error)
    {
        work.Receipt.Error=error.Message;work.Receipt.State=work.CutoverStarted?PNWorldTransferState.Failed:PNWorldTransferState.Aborted;
        work.Receipt.CleanupPending=true;Finish(work,false);
    }
    private void Finish(Work work,bool success)
    {
        if(work.Completing)success=work.FinalSuccess;
        try
        {
            if(!work.Completing)
            {
                foreach(var actor in work.Actors.AsEnumerable().Reverse())
                {
                    if(!success&&!work.CutoverStarted)
                    {
                        actor.Change?.Dispose();actor.Change=null;
                        if(actor.Context.Actor?.IsReady==true&&actor.MembershipApplied){factory.MoveWorldOwnership(actor.Context.Actor,actor.Context.Source);scheduler.MovePublishedWorld(actor.Context.Actor,actor.Context.Source);if(actor.SourcePresentation!=null)factory.RebindWorldPresentation(actor.Context.Actor,actor.SourcePresentation);actor.MembershipApplied=false;}
                        if(actor.Hold!=null&&actor.Context.Actor?.IsReady==true){actor.Context.Actor.transform.SetPositionAndRotation(actor.Position,actor.Rotation);var body=actor.Context.Actor.GetComponent<Rigidbody>();if(body!=null){body.position=actor.Position;body.rotation=actor.Rotation;if(!body.isKinematic){body.linearVelocity=actor.Velocity;body.angularVelocity=actor.Angular;}}}
                        while(actor.FinishedCallbacks<actor.Participants.Count){actor.Participants[actor.Participants.Count-1-actor.FinishedCallbacks].Abort(actor.Context);actor.FinishedCallbacks++;}
                        if(actor.Context.ReplacementCandidate!=null)scheduler.Retire(actor.Context.ReplacementCandidate);
                    }
                    else if(!success&&actor.Context.Actor!=null)
                    {
                        actor.Context.Destination.Fail(new InvalidOperationException(work.Receipt.Error));
                        if(actor.Context.ReplacementCandidate!=null){scheduler.Retire(actor.Context.ReplacementCandidate);if(actor.Context.PreviousActor?.IsReady==true)scheduler.RetirePublished(actor.Context.PreviousActor);}
                        while(actor.FinishedCallbacks<actor.Participants.Count){actor.Participants[actor.Participants.Count-1-actor.FinishedCallbacks].Abort(actor.Context);actor.FinishedCallbacks++;}
                    }
                    else while(actor.FinishedCallbacks<actor.Participants.Count){actor.Participants[actor.FinishedCallbacks].Complete(actor.Context);actor.FinishedCallbacks++;}
                }
                work.Completing=true;work.FinalSuccess=success;
            }
            foreach(var actor in work.Actors.AsEnumerable().Reverse())
            {
                while(actor.DisposedParticipants<actor.Participants.Count){actor.Participants[actor.Participants.Count-1-actor.DisposedParticipants].Dispose();actor.DisposedParticipants++;}
                actor.Change?.Dispose();actor.Change=null;actor.Hold?.Dispose();actor.Hold=null;actor.ReplacementHold?.Dispose();actor.ReplacementHold=null;
                Transport?.End(actor.Context,work.CutoverStarted,success);actor.SourceLease?.Dispose();actor.SourceLease=null;
            }
            work.DestinationLease?.Dispose();work.DestinationLease=null;
            foreach(var pin in work.SourcePins)pin.Dispose();work.SourcePins.Clear();
            foreach(var id in work.Entities)reservedActors.Remove(id);active.Remove(work.Request.Id);work.Receipt.CleanupPending=false;
            if(success)work.Receipt.State=PNWorldTransferState.Completed;
            work.Actors.Clear();work.Request=null;
        }
        catch(Exception cleanup){work.Receipt.CleanupPending=true;work.Receipt.Error=cleanup.Message;}
    }
    public void Dispose()
    {
        if(disposed)return;
        foreach(var work in active.Values.ToArray()){work.Cancelled=true;Fail(work,new OperationCanceledException("Play teardown."));work.Job?.Dispose();}
        if(active.Count!=0)throw new EveWorldCleanupPendingException("Transfer cleanup retains ownership.");
        disposed=true;
    }
}
#endif
