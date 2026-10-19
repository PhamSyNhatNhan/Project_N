#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class PNWorldSaveActorKey : MonoBehaviour
{
    [NonSerialized] public string Key;[NonSerialized] public EveEntityId Entity;[NonSerialized] public bool Restored;
    public static void Copy(EveEntityLoader source,EveEntityLoader target)
    {
        var old=source.GetComponent<PNWorldSaveActorKey>();if(old?.Key==null||!old.Restored&&!old.Entity.Equals(source.GetComponent<EveEntityIdentity>().Id))return;
        var next=target.GetComponent<PNWorldSaveActorKey>()??target.gameObject.AddComponent<PNWorldSaveActorKey>();next.Key=old.Key;next.Entity=target.GetComponent<EveEntityIdentity>().Id;next.Restored=false;
    }
}

public sealed partial class PNWorldLifetimeController
{
    private readonly Dictionary<object,Func<long>> externalBudgets=new Dictionary<object,Func<long>>();
    internal long TrackedBytes=>StoredBytes+StagingBytes+externalBudgets.Values.Sum(read=>read());
    internal void TrackIO(object owner,Func<long> read)=>externalBudgets.Add(owner,read);
    internal void UntrackIO(object owner)=>externalBudgets.Remove(owner);
    private readonly Dictionary<Guid,Guid> saveIdentities=new Dictionary<Guid,Guid>();
    private Guid SaveIdentity(EveWorldContext world)
    {if(!saveIdentities.TryGetValue(world.InstanceId,out var id))saveIdentities.Add(world.InstanceId,id=Guid.NewGuid());return id;}
    private static string StableKey(EveEntityLoader actor,string restored=null)
    {
        var marker=actor.GetComponent<PNWorldSaveActorKey>()??actor.gameObject.AddComponent<PNWorldSaveActorKey>();
        if(restored!=null){marker.Key=restored;marker.Restored=true;return restored;}
        var id=actor.GetComponent<EveEntityIdentity>().Id;
        if(marker.Key==null||!marker.Restored&&!marker.Entity.Equals(id))marker.Key="dynamic:"+Guid.NewGuid().ToString("N");
        marker.Restored=false;marker.Entity=id;return marker.Key;
    }
    // Capture leaves the world resident, frozen only for the coherent snapshot interval.
    public PNWorldLifetimeOperation CaptureSnapshot(EveWorldContext world)=>CaptureSnapshot(world,false);
    internal PNWorldLifetimeOperation CaptureSnapshot(EveWorldContext world,bool keepPaused)
    {
        Require(world);if(!IsEmpty(world))throw new InvalidOperationException("Portable capture requires an empty world without pending activity.");
        string descriptor=worlds.CaptureLoadDescriptor(world).ToString(Formatting.None);
        var actors=session.Entities.Snapshot(world).ToArray();var references=actors.ToDictionary(Key,x=>x);
        var work=New(world,PNWorldLifetimeState.Capturing);work.WasPaused=world.State==EveWorldState.Paused;work.SnapshotOnly=true;work.KeepPaused=keepPaused;work.Portable=true;work.Descriptor=descriptor;
        work.Lease=world.AcquireLease("portable capture");world.SetPaused(true);work.Actors=actors;work.References=references;
        work.Job=scheduler.SchedulePreparation(()=>SafeStep(work),error=>Fail(work,error));return work.Receipt;
    }
    public PNWorldSaveEnvelope Export(PNWorldCheckpoint checkpoint)
    {
        if(!session.IsActive||!session.HasAuthority||checkpoint==null||checkpoint.DescriptorJson==null)throw new InvalidOperationException("Portable authority checkpoint required.");
        var policy=session.Registry.Get<PNWorldDefinition>().Get(checkpoint.Definition).Policy;
        var ids=policy.ActorStateProviders.Concat(policy.WorldStateProviders).Distinct();
        var schemas=new JObject();foreach(var id in ids)schemas[id.ToString()]=session.Registry.Get<PNWorldStateDefinition>().Get(id).Schema;
        return PNWorldSaveEnvelope.Create(checkpoint.SaveWorldId,checkpoint.Definition.ToString(),checkpoint.ContentHash,session.Compatibility,
            JObject.Parse(checkpoint.DescriptorJson),schemas,JToken.Parse(checkpoint.Data.Json));
    }
    public Guid Import(PNWorldSaveEnvelope envelope)
    {
        if(!session.IsActive||!session.HasAuthority)throw new InvalidOperationException("Current authority play required.");
        envelope.ValidateCompatibility(session.Compatibility);
        var definition=session.Registry.Get<PNWorldDefinition>().Get(EveId.Parse(envelope.Definition));
        if(definition.ContentHash!=envelope.ContentHash)throw new InvalidOperationException("Saved world content differs.");
        var data=envelope.Document;var schemas=(JObject)data["providers"];
        foreach(var actor in (JArray)data["state"][0]["actors"])
        {session.Registry.Get<EveCharacterDefinition>().Get(EveId.Parse((string)actor["character"]));string template=(string)actor["template"];if(!string.IsNullOrEmpty(template))session.Registry.Get<PNEntityTemplateDefinition>().Get(EveId.Parse(template));}
        var expected=definition.Policy.ActorStateProviders.Concat(definition.Policy.WorldStateProviders).Distinct().ToArray();
        if(schemas.Count!=expected.Length||expected.Any(id=>(int?)schemas[id.ToString()]!=session.Registry.Get<PNWorldStateDefinition>().Get(id).Schema))throw new FormatException("Saved required provider selection/schema differs.");
        worlds.ValidateLoadDescriptor(definition.Id,data["descriptor"]);
        if(TrackedBytes+envelope.Bytes>128L*1024*1024)throw new InvalidOperationException("Checkpoint import exceeds shared budget.");
        var instance=Guid.NewGuid();var checkpoint=new PNWorldCheckpoint(instance,0,definition.Id,definition.ContentHash,new PNWorldStateRecord(data["state"].ToString(Formatting.None)))
        {SaveWorldId=envelope.SaveWorldId,DescriptorJson=data["descriptor"].ToString(Formatting.None)};
        checkpoints.Add(instance,checkpoint);saveIdentities.Add(instance,envelope.SaveWorldId);return instance;
    }
    internal void Cancel(PNWorldLifetimeOperation operation)
    {if(active.TryGetValue(operation.World.InstanceId,out var work)&&ReferenceEquals(work.Receipt,operation))Fail(work,new OperationCanceledException("Save operation cancelled."));}
    internal void ReleaseImportedCheckpoint(Guid instance)
    {
        if(active.ContainsKey(instance))throw new InvalidOperationException("Checkpoint still owned by an active restore.");
        checkpoints.Remove(instance);
        if(!session.Worlds.Snapshot().Any(w=>w.InstanceId==instance)){saveIdentities.Remove(instance);restoreIncarnations.Remove(instance);}
    }
}
#endif
