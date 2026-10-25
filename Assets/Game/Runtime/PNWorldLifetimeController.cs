#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public enum PNWorldLifetimeState { Capturing,Unloading,Stored,Restoring,Completed,Failed,Captured }
public sealed class PNWorldLifetimeOperation
{
    public EveWorldContext World { get; internal set; }
    public PNWorldLifetimeState State { get; internal set; }
    public string Error { get; internal set; }
    public string CleanupError {get;internal set;}
    public bool CleanupPending { get; internal set; }
    public PNWorldCheckpoint Snapshot {get;internal set;}
    internal IDisposable CaptureLease;
    public bool IsTerminal=>!CleanupPending&&(State==PNWorldLifetimeState.Stored||State==PNWorldLifetimeState.Completed||State==PNWorldLifetimeState.Failed||State==PNWorldLifetimeState.Captured);
}
public sealed class PNWorldCheckpoint
{
    public Guid Instance { get; }
    public ulong Incarnation { get; }
    public EveId Definition { get; }
    public string ContentHash { get; }
    public PNWorldStateRecord Data { get; }
    public string DescriptorJson {get;internal set;}
    public Guid SaveWorldId {get;internal set;}
    internal PNWorldCheckpoint(EveWorldContext world,PNWorldStateRecord data){Instance=world.InstanceId;Incarnation=world.Incarnation;Definition=world.DefinitionId;ContentHash=world.ContentHash;Data=data;}
    internal PNWorldCheckpoint(Guid instance,ulong incarnation,EveId definition,string hash,PNWorldStateRecord data)
    {Instance=instance;Incarnation=incarnation;Definition=definition;ContentHash=hash;Data=data;}
}
// In-memory only. An Activity lease pins a join/transfer/job; resident actor and
// binding leases keep resources owned without making an NPC an active occupant.
public sealed partial class PNWorldLifetimeController : IDisposable
{
    private sealed class Work
    {
        public PNWorldLifetimeOperation Receipt;
        public PNWorldCheckpoint Checkpoint;
        public IDisposable Job,Lease;
        public bool WasPaused,Restore,Created,Started;
        public bool SnapshotOnly,Portable,KeepPaused;
        public string Descriptor;
        public EveEntityLoader[] Actors;
        public Dictionary<string,EveEntityLoader> References;
        public readonly List<(EveEntityPreparationScheduler.Candidate Candidate,PNEntityRequest Request,JObject Record)> Candidates=new List<(EveEntityPreparationScheduler.Candidate,PNEntityRequest,JObject)>();
        public readonly JArray Records=new JArray();
        public int Cursor;
        public double StartedAt;
        public int CleanupAttempts;
        public double RetryAt;
        public long StagingBytes;
    }
    private readonly EveSessionContext session;
    private readonly PNWorldRuntime worlds;
    private readonly PNEntityFactory factory;
    private readonly EveEntityPreparationScheduler scheduler;
    private readonly PNWorldStateService states;
    private readonly Dictionary<Guid,PNWorldCheckpoint> checkpoints=new Dictionary<Guid,PNWorldCheckpoint>();
    private readonly Dictionary<Guid,Work> active=new Dictionary<Guid,Work>();
    private readonly Dictionary<Guid,ulong> restoreIncarnations=new Dictionary<Guid,ulong>();
    private readonly HashSet<EveWorldContext> autoPaused=new HashSet<EveWorldContext>(),attempted=new HashSet<EveWorldContext>();
    public event Action<EveEntityPreparationScheduler.Candidate,PNEntityRequest> RestoredActor;
    public event Action<PNWorldLifetimeOperation> Failed;
    public long StoredBytes=>checkpoints.Values.Sum(x=>(long)x.Data.Bytes);
    public long StagingBytes=>active.Values.Sum(x=>x.StagingBytes);
    public int PendingCount=>active.Count;
    public PNWorldLifetimeController(EveSessionContext session,PNWorldRuntime worlds,PNEntityFactory factory,EveEntityPreparationScheduler scheduler)
    {this.session=session;this.worlds=worlds;this.factory=factory;this.scheduler=scheduler;states=new PNWorldStateService(session);session.RegisterCleanup(Dispose);}
    public bool IsEmpty(EveWorldContext world)=>world.ActivityLeaseCount==0&&!scheduler.HasPendingWork(world)&&!session.Entities.Snapshot(world).Any(x=>x.GetComponent<PNPlayerStat>()!=null||PNEntityRoles.IsPlayer(session.Registry,x.GetComponent<PNFactoryEntity>()?.Entity)||x.LoadContext.Identity.OwnerId>0);
    public PNWorldCheckpoint GetCheckpoint(Guid instance)=>checkpoints.TryGetValue(instance,out var state)?state:null;
    public void Tick()
    {
        if(!session.IsActive||!session.HasAuthority)return;
        foreach(var world in session.Worlds.Snapshot())
        {
            if(world.State!=EveWorldState.Ready&&world.State!=EveWorldState.Paused||active.ContainsKey(world.InstanceId))continue;
            bool empty=IsEmpty(world);
            if(!empty){attempted.Remove(world);if(autoPaused.Remove(world)&&world.State==EveWorldState.Paused)world.SetPaused(false);continue;}
            if(attempted.Contains(world))continue;
            switch(session.Registry.Get<PNWorldDefinition>().Get(world.DefinitionId).Policy.Empty)
            {
                case PNEmptyWorldPolicy.Continue:break;
                case PNEmptyWorldPolicy.Pause:if(world.CanExecute){world.SetPaused(true);autoPaused.Add(world);}break;
                case PNEmptyWorldPolicy.Discard:attempted.Add(world);StartUnload(world);break;
                case PNEmptyWorldPolicy.CheckpointUnload:attempted.Add(world);CaptureAndUnload(world);break;
            }
        }
    }
    public PNWorldLifetimeOperation CaptureAndUnload(EveWorldContext world)
    {
        Require(world);if(!IsEmpty(world))throw new InvalidOperationException("World has occupants, pending work or activity leases.");
        var actors=session.Entities.Snapshot(world).Where(x=>x.GetComponent<PNEntityRole>()?.Persist!=false).ToArray();var references=actors.ToDictionary(Key,x=>x);
        string descriptor=worlds.SupportsPersistence(world)?worlds.CaptureLoadDescriptor(world).ToString(Formatting.None):null;
        var work=New(world,PNWorldLifetimeState.Capturing);work.WasPaused=world.State==EveWorldState.Paused;work.Descriptor=descriptor;
        work.Lease=world.AcquireLease("checkpoint capture");world.SetPaused(true);
        work.Actors=actors;work.References=references;
        work.Job=scheduler.SchedulePreparation(()=>SafeStep(work),error=>Fail(work,error));return work.Receipt;
    }
    private PNWorldLifetimeOperation StartUnload(EveWorldContext world)
    {Require(world);var work=New(world,PNWorldLifetimeState.Unloading);work.Job=scheduler.SchedulePreparation(()=>SafeStep(work),error=>Fail(work,error));return work.Receipt;}
    public PNWorldLifetimeOperation Restore(Guid instance)
    {
        if(!session.IsActive||!session.HasAuthority||active.ContainsKey(instance)||session.Worlds.Snapshot().Any(x=>x.InstanceId==instance)||!checkpoints.TryGetValue(instance,out var checkpoint))throw new InvalidOperationException("A stored, nonresident world is required.");
        var selected=session.Registry.Get<PNWorldDefinition>().Get(checkpoint.Definition);if(selected.ContentHash!=checkpoint.ContentHash)throw new InvalidOperationException("Selected world content differs from checkpoint.");
        if(TrackedBytes+checkpoint.Data.Bytes>128L*1024*1024)throw new InvalidOperationException("Checkpoint restore staging budget exceeded.");
        restoreIncarnations.TryGetValue(instance,out var previous);var incarnation=checked(Math.Max(previous,checkpoint.Incarnation)+1);
        var world=checkpoint.DescriptorJson==null?worlds.CreateStaged(checkpoint.Definition,instance,incarnation):worlds.CreateStaged(checkpoint.Definition,instance,incarnation,JToken.Parse(checkpoint.DescriptorJson));restoreIncarnations[instance]=incarnation;var work=New(world,PNWorldLifetimeState.Restoring);work.StagingBytes=checkpoint.Data.Bytes;work.Restore=true;work.Checkpoint=checkpoint;work.Lease=world.AcquireLease("checkpoint restore");
        work.Job=scheduler.SchedulePreparation(()=>SafeStep(work),error=>Fail(work,error));return work.Receipt;
    }
    public EveWorldContext RecreateDiscarded(EveId definition,Guid instance,ulong previousIncarnation)
    {if(GetCheckpoint(instance)!=null)throw new InvalidOperationException("Use checkpoint restoration for stored state.");return worlds.Create(definition,instance,checked(previousIncarnation+1));}
    private void Require(EveWorldContext world)
    {if(!session.HasAuthority||!session.IsActive||!ReferenceEquals(world?.Session,session)||world.State!=EveWorldState.Ready&&world.State!=EveWorldState.Paused||active.ContainsKey(world.InstanceId))throw new InvalidOperationException("Current authority world required.");}
    private Work New(EveWorldContext world,PNWorldLifetimeState state)
    {var work=new Work{Receipt=new PNWorldLifetimeOperation{World=world,State=state},StartedAt=Time.realtimeSinceStartupAsDouble};active.Add(world.InstanceId,work);return work;}
    private bool SafeStep(Work work)
    {try{if(work.Receipt.CleanupPending)return TryFailureCleanup(work);return Step(work);}catch(Exception error){Fail(work,error);return !work.Receipt.CleanupPending;}}
    private bool Step(Work work)
    {
        var world=work.Receipt.World;if(Time.realtimeSinceStartupAsDouble-work.StartedAt>60)throw new TimeoutException("World state transition deadline exceeded.");
        var policy=session.Registry.Get<PNWorldDefinition>().Get(world.DefinitionId).Policy;
        if(work.Receipt.State==PNWorldLifetimeState.Capturing)
        {
            if(world.ActivityLeaseCount!=1||scheduler.HasPendingWork(world)||work.Actors.Any(x=>x?.IsReady!=true||x.LoadContext.Identity.World!=world))throw new InvalidOperationException("World changed during checkpoint preparation.");
            if(work.Cursor<work.Actors.Length)
            {
                var actor=work.Actors[work.Cursor++];var identity=actor.LoadContext.Identity;var marker=actor.GetComponent<PNFactoryEntity>();var key=actor.GetComponent<EveSceneEntityKey>();
                var state=states.Capture(new PNWorldStateContext(world,actor,work.References),policy.ActorStateProviders);var body=actor.GetComponent<Rigidbody>();
                work.Records.Add(new JObject{["key"]=work.References.Single(x=>x.Value==actor).Key,["slot"]=identity.ContentSlot,["template"]=marker?.Template,["spawn"]=marker?.SpawnEntry,["scene"]=marker?.SceneKey??key?.SceneKey,["entity"]=marker!=null?"":key?.EntityKey,["character"]=actor.GetComponent<PNStat>().CharacterId.ToString(),["position"]=Vector(actor.transform.position),["rotation"]=new JArray(actor.transform.rotation.x,actor.transform.rotation.y,actor.transform.rotation.z,actor.transform.rotation.w),["velocity"]=Vector(body!=null?body.linearVelocity:Vector3.zero),["angular"]=Vector(body!=null?body.angularVelocity:Vector3.zero),["state"]=state.Read()});
                EnsureBound(work);return false;
            }
            var worldState=states.Capture(new PNWorldStateContext(world,null,work.References),policy.WorldStateProviders);
            var data=new PNWorldStateRecord(new JArray(new JObject{["schema"]=1,["time"]=world.GameplayTime,["actors"]=work.Records,["world"]=worldState.Read()}).ToString(Formatting.None));
            work.StagingBytes=data.Bytes;
            if(TrackedBytes>128L*1024*1024)throw new InvalidOperationException("Checkpoint store/staging/IO exceeds 128 MiB.");
            work.Checkpoint=new PNWorldCheckpoint(world,data){DescriptorJson=work.Descriptor,SaveWorldId=SaveIdentity(world)};work.Receipt.Snapshot=work.Checkpoint;
            if(work.SnapshotOnly)
            {
                if(work.KeepPaused){var receipt=work.Receipt;receipt.CaptureLease=work.Lease;session.RegisterCleanup(()=>{receipt.CaptureLease?.Dispose();receipt.CaptureLease=null;});}
                else{if(!work.WasPaused)world.SetPaused(false);work.Lease.Dispose();}
                work.Lease=null;work.Receipt.State=PNWorldLifetimeState.Captured;Complete(work);return true;
            }
            work.Lease.Dispose();work.Lease=null;work.Receipt.State=PNWorldLifetimeState.Unloading;
        }
        if(work.Receipt.State==PNWorldLifetimeState.Unloading)
        {
            if(!worlds.Unload(world))return false;
            if(work.Checkpoint!=null)checkpoints[world.InstanceId]=work.Checkpoint;
            work.Receipt.State=PNWorldLifetimeState.Stored;Complete(work);return true;
        }
        if(work.Receipt.State==PNWorldLifetimeState.Restoring)
        {
            if(world.State==EveWorldState.Creating||world.State==EveWorldState.Loading)return false;
            if(world.State==EveWorldState.Failed)throw world.Failure;
            var data=(JObject)work.Checkpoint.Data.Read().Single();if((int)data["schema"]!=1)throw new FormatException("Unknown checkpoint schema.");
            if(!work.Started)
            {
                work.Started=true;RequireResident(world,"restore start");world.SetPaused(true);world.RestoreClock((double)data["time"]);var records=(JArray)data["actors"];
                if(records.Count>256||records.Select(x=>(string)x["key"]).Distinct().Count()!=records.Count)throw new FormatException("Bounded unique checkpoint actor keys required.");
                foreach(var record in records.Cast<JObject>())
                {
                    var request=new PNEntityRequest{World=world,WorldIncarnation=world.Incarnation,ContentSlot=(string)record["slot"],Template=(string)record["template"],SpawnEntry=(string)record["spawn"],SceneKey=(string)record["scene"],EntityKey=(string)record["entity"],Character=(string)record["character"],Position=ReadVector(record["position"])};
                    var candidate=PNEntitySpawner.Enqueue(scheduler,factory,request,loader=>
                    {
                        StableKey(loader,(string)record["key"]);EveEntityId id;do{id=session.Role==EveSessionRole.Host?session.Entities.AllocateHostId():session.Entities.AllocateOfflineId();}while(session.Entities.IsReserved(id));loader.GetComponent<EveEntityIdentity>().Assign(id,EveId.Parse(request.Character),0);
                        var stage=loader.GetComponent<PNWorldRestoreStage>()??loader.gameObject.AddComponent<PNWorldRestoreStage>();stage.Restore=load=>
                        {
                            states.Restore(new PNWorldStateContext(world,loader,work.References),Record(record["state"]),policy.ActorStateProviders);
                            var rotation=(JArray)record["rotation"];var q=new Quaternion((float)rotation[0],(float)rotation[1],(float)rotation[2],(float)rotation[3]);loader.transform.SetPositionAndRotation(request.Position,q);
                            var body=loader.GetComponent<Rigidbody>();if(body!=null){body.position=request.Position;body.rotation=q;if(!body.isKinematic){body.linearVelocity=ReadVector(record["velocity"]);body.angularVelocity=ReadVector(record["angular"]);}}
                        };
                    },null,error=>Fail(work,error),null,loader=>false,factoryPublished:false);
                    work.Candidates.Add((candidate,request,record));
                }
                return false;
            }
            if(work.Candidates.Any(x=>x.Candidate.Loader?.State!=EveEntityLoadState.Prepared))return false;
            work.References=work.Candidates.ToDictionary(x=>(string)x.Record["key"],x=>x.Candidate.Loader);
            // Validate every custom state before invoking any restore callback.
            // Actor providers are validated/restored inside the loader module scope.
            states.Validate(new PNWorldStateContext(world,null,work.References),Record(data["world"]),policy.WorldStateProviders);
            if(work.Candidates.Count>0)scheduler.PublishPrepared(work.Candidates.Select(x=>x.Candidate).ToArray());
            states.Restore(new PNWorldStateContext(world,null,work.References),Record(data["world"]),policy.WorldStateProviders);
            foreach(var candidate in work.Candidates){RestoredActor?.Invoke(candidate.Candidate,candidate.Request);factory.Published(candidate.Candidate.Loader);}
            RequireResident(world,"publication");world.SetPaused(false);worlds.PublishStaged(world);work.Lease.Dispose();work.Lease=null;work.Receipt.State=PNWorldLifetimeState.Completed;Complete(work);return true;
        }
        return false;
    }
    // A staged world can fail while loading and be unloaded by host cleanup before the restore step runs; name the original cause, not the state error.
    private static void RequireResident(EveWorldContext world,string step)
    {
        if(world.State==EveWorldState.Ready||world.State==EveWorldState.Paused)return;
        var cause=world.FirstFailure??world.Failure;
        throw new InvalidOperationException("Restored world left the resident state before "+step+" (state "+world.State+")"+(cause!=null?": "+cause.Message:""),cause);
    }
    private void EnsureBound(Work work){work.StagingBytes=System.Text.Encoding.UTF8.GetByteCount(work.Records.ToString(Formatting.None));if(work.StagingBytes>64L*1024*1024||TrackedBytes>128L*1024*1024)throw new InvalidOperationException("Checkpoint staging budget exceeded.");}
    private static string Key(EveEntityLoader actor)
    {
        var key=actor.GetComponent<EveSceneEntityKey>();if(actor.GetComponent<PNFactoryEntity>()==null&&key!=null)
        {var value="content:"+new JArray(actor.LoadContext.Identity.ContentSlot,key.SceneKey,key.EntityKey).ToString(Formatting.None);var marker=actor.GetComponent<PNWorldSaveActorKey>()??actor.gameObject.AddComponent<PNWorldSaveActorKey>();marker.Key=value;marker.Entity=actor.GetComponent<EveEntityIdentity>().Id;marker.Restored=false;return value;}
        return StableKey(actor);
    }
    private static JArray Vector(Vector3 value)=>new JArray(value.x,value.y,value.z);
    private static Vector3 ReadVector(JToken value)
    {var a=(JArray)value;if(a.Count!=3)throw new FormatException("Vector3 required.");var v=new Vector3((float)a[0],(float)a[1],(float)a[2]);if(float.IsNaN(v.x)||float.IsInfinity(v.x)||float.IsNaN(v.y)||float.IsInfinity(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.z))throw new FormatException("Finite vector required.");return v;}
    private static PNWorldStateRecord Record(JToken value)=>new PNWorldStateRecord(value.ToString(Formatting.None));
    private void Complete(Work work){active.Remove(work.Receipt.World.InstanceId);work.Actors=null;work.References=null;work.Candidates.Clear();work.Records.Clear();}
    private void Fail(Work work,Exception error)
    {
        if(work.Receipt.State==PNWorldLifetimeState.Failed)return;
        bool unloading=work.Receipt.State==PNWorldLifetimeState.Unloading;
        // Diagnostic: the failure reason used to live only in Receipt.Error; log it (warning, since callers may expect failures in negative checks).
        Debug.LogWarning("World lifetime operation failed in state "+work.Receipt.State+" (restore="+work.Restore+", world="+work.Receipt.World.InstanceId+"): "+error);
        work.Receipt.Error=error.Message;work.Receipt.State=PNWorldLifetimeState.Failed;work.Lease?.Dispose();work.Lease=null;
        var world=work.Receipt.World;
        if(work.Restore||unloading)
        {
            foreach(var candidate in work.Candidates)scheduler.Retire(candidate.Candidate);
            if(work.Checkpoint!=null&&!work.Restore)checkpoints[world.InstanceId]=work.Checkpoint;
            if(world.State!=EveWorldState.Disposed)world.Fail(error);work.Receipt.CleanupPending=true;
        }
        else if(world.State==EveWorldState.Paused&&!work.WasPaused)world.SetPaused(false);
        if(!work.Receipt.CleanupPending)Complete(work);
        foreach(Action<PNWorldLifetimeOperation> observer in Failed?.GetInvocationList()??Array.Empty<Delegate>())try{observer(work.Receipt);}catch(Exception callback){Debug.LogException(callback);}
    }
    private bool TryFailureCleanup(Work work)
    {
        if(work.CleanupAttempts>=3||Time.realtimeSinceStartupAsDouble<work.RetryAt)return false;
        try{if(!worlds.Unload(work.Receipt.World))return false;work.Receipt.CleanupPending=false;Complete(work);return true;}
        catch(Exception error){work.Receipt.CleanupError=error.Message;work.CleanupAttempts++;work.RetryAt=Time.realtimeSinceStartupAsDouble+.25;return false;}
    }
    public void RetryCleanup(){foreach(var work in active.Values.Where(x=>x.Receipt.CleanupPending)){work.CleanupAttempts=0;work.RetryAt=0;}scheduler.RetryQuarantines();states.Dispose();}
    public void RetryProviderCleanup()=>states.Dispose();
    public void Dispose()
    {
        foreach(var work in active.Values.ToArray()){work.Job?.Dispose();Fail(work,new OperationCanceledException("Play ended."));if(work.Receipt.CleanupPending){work.CleanupAttempts=0;work.RetryAt=0;TryFailureCleanup(work);}}
        if(active.Count!=0)throw new EveWorldCleanupPendingException("World state transition cleanup remains owned.");
        states.Dispose();checkpoints.Clear();restoreIncarnations.Clear();saveIdentities.Clear();autoPaused.Clear();attempted.Clear();RestoredActor=null;Failed=null;
    }
}
#endif
