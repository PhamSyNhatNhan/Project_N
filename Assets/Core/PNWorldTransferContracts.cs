#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public enum PNWorldTransferState { Requested,Validating,PreparingDestination,Holding,Committing,CatchingUp,Completed,Aborted,Failed }
public enum PNEmptyWorldPolicy { Continue,Pause,Discard,CheckpointUnload }
public sealed class PNWorldPolicy
{
    public PNEmptyWorldPolicy Empty { get; }
    public IReadOnlyList<EveId> TransferParticipants { get; }
    public IReadOnlyList<EveId> ActorStateProviders { get; }
    public IReadOnlyList<EveId> WorldStateProviders { get; }
    public PNWorldPolicy(PNEmptyWorldPolicy empty=PNEmptyWorldPolicy.Continue,IEnumerable<EveId> participants=null,IEnumerable<EveId> actorState=null,IEnumerable<EveId> worldState=null)
    {
        if(!Enum.IsDefined(typeof(PNEmptyWorldPolicy),empty))throw new ArgumentOutOfRangeException(nameof(empty));Empty=empty;
        TransferParticipants=Copy(participants??new[]{EveId.Parse("base:world_gameplay")});
        ActorStateProviders=Copy(actorState??new[]{EveId.Parse("base:actor_world_state")});WorldStateProviders=Copy(worldState??Array.Empty<EveId>());
    }
    private static IReadOnlyList<EveId> Copy(IEnumerable<EveId> ids)
    {var copy=ids.ToArray();if(copy.Length>64||copy.Any(x=>!x.IsValid)||copy.Distinct().Count()!=copy.Length)throw new ArgumentException("Unique bounded provider IDs required.");return Array.AsReadOnly(copy);}
}
public sealed class PNWorldTransferRequest
{
    public Guid Id { get; }
    public IReadOnlyList<EveEntityLoader> Actors { get; }
    public EveWorldContext Destination { get; }
    public string Slot { get; }
    public string Spawn { get; }
    public string ReplacementTemplate { get; }
    public string TargetTemplate { get; }
    public string Signature { get; }
    public IReadOnlyList<long> EntityIds { get; }
    public PNWorldTransferRequest(Guid id,IEnumerable<EveEntityLoader> actors,EveWorldContext destination,string slot=null,string spawn=null,string replacementTemplate=null,string targetTemplate=null)
    {
        var values=actors?.ToArray()??throw new ArgumentNullException(nameof(actors));
        if(id==Guid.Empty||destination==null||values.Length==0||values.Length>64||values.Any(x=>x==null)||values.Distinct().Count()!=values.Length||slot?.Length>128||spawn?.Length>128||replacementTemplate?.Length>128)throw new ArgumentException("Bounded unique transfer participants and destination required.");
        if(targetTemplate?.Length>128)throw new ArgumentException("Bounded template ID required.");
        Id=id;Actors=Array.AsReadOnly(values);Destination=destination;Slot=slot;Spawn=spawn;ReplacementTemplate=replacementTemplate;TargetTemplate=targetTemplate;
        if(values.Any(x=>x.LoadContext?.Identity.HasId!=true))throw new ArgumentException("Loaded identified transfer actors required.");
        EntityIds=Array.AsReadOnly(values.Select(x=>x.LoadContext.Identity.Id.Value).ToArray());
        Signature=new JArray(destination.InstanceId.ToString("N"),destination.Incarnation,slot,spawn,replacementTemplate,targetTemplate,new JArray(values.OrderBy(x=>x.LoadContext.Identity.Id.Value).Select(x=>new JArray(x.LoadContext.Identity.Id.Value,x.LoadContext.Generation,x.LoadContext.Identity.MembershipRevision)))).ToString(Formatting.None);
    }
}
public sealed class PNWorldTransferContext
{
    public Guid TransferId { get; }
    public EveEntityLoader Actor { get; internal set; }
    public EveEntityLoader PreviousActor { get; }
    public EveEntityPreparationScheduler.Candidate ReplacementCandidate { get; internal set; }
    public EveWorldContext Source { get; }
    public EveWorldContext Destination { get; }
    public PNEntityRequest Target { get; }
    public bool Replacement { get; }
    public long Entity { get; }
    public long Generation { get; }
    public long Owner { get; }
    public void StageReplacement(EveEntityPreparationScheduler.Candidate candidate)
    {if(!Replacement||ReplacementCandidate!=null||candidate==null)throw new InvalidOperationException("Replacement may be staged once.");ReplacementCandidate=candidate;}
    public void ActivateReplacement()
    {if(!Replacement||Actor!=PreviousActor||ReplacementCandidate?.Loader?.IsReady!=true||ReplacementCandidate.Loader.LoadContext.Identity.World!=Destination||!ReplacementCandidate.Loader.LoadContext.Identity.IsGameplayHeld)throw new InvalidOperationException("Held published destination replacement required.");Actor=ReplacementCandidate.Loader;}
    public PNWorldTransferContext(Guid id,EveEntityLoader actor,EveWorldContext destination,PNEntityRequest target,bool replacement)
    {TransferId=id;Actor=actor;PreviousActor=actor;Source=actor.LoadContext.Identity.World;Destination=destination;Target=target;Replacement=replacement;Entity=actor.LoadContext.Identity.Id.Value;Generation=actor.LoadContext.Generation;Owner=actor.LoadContext.Identity.OwnerId;}
}
public interface IPNWorldTransferParticipant : IDisposable
{
    bool Prepare(PNWorldTransferContext context);
    void Hold(PNWorldTransferContext context);
    void Commit(PNWorldTransferContext context);
    void Abort(PNWorldTransferContext context);
    void Complete(PNWorldTransferContext context);
}
public sealed class PNWorldTransferParticipantDefinition : EveIIdentified
{
    public EveId Id { get; }
    public string Implementation=>ImplementationType.FullName;
    [JsonIgnore] public Type ImplementationType { get; }
    private readonly Func<IPNWorldTransferParticipant> factory;
    public PNWorldTransferParticipantDefinition(EveId id,Type type,Func<IPNWorldTransferParticipant> factory)
    {if(!id.IsValid||type==null||type.IsAbstract||!typeof(IPNWorldTransferParticipant).IsAssignableFrom(type))throw new ArgumentException("Selected transfer participant required.");Id=id;ImplementationType=type;this.factory=factory??throw new ArgumentNullException(nameof(factory));}
    public IPNWorldTransferParticipant Create()
    {var result=factory();if(result==null||!ImplementationType.IsInstanceOfType(result))throw new InvalidOperationException("Invalid selected transfer participant.");return result;}
}
// The transport seam carries no Mirror types. Loaded and applied are distinct.
public interface IPNWorldTransferTransport
{
    int MaximumParticipants { get; }
    void Validate(PNWorldTransferContext context);
    void Prepare(PNWorldTransferContext context);
    bool IsLoaded(PNWorldTransferContext context);
    void Hold(PNWorldTransferContext context);
    bool IsHeld(PNWorldTransferContext context);
    void Rebind(PNWorldTransferContext context);
    bool IsApplied(PNWorldTransferContext context);
    void End(PNWorldTransferContext context,bool committed,bool success);
}
public interface IPNWorldTransferTransportOwner
{IPNWorldTransferTransport Transport {get;set;} }
public interface IPNWorldStateProvider : IDisposable
{
    JToken Capture(PNWorldStateContext context);
    void Validate(PNWorldStateContext context,JToken state);
    void Restore(PNWorldStateContext context,JToken state);
}
public sealed class PNWorldStateContext
{
    public EveSessionContext Session { get; }
    public EveWorldContext World { get; }
    public EveEntityLoader Actor { get; }
    private readonly IReadOnlyDictionary<string,EveEntityLoader> entities;
    public PNWorldStateContext(EveWorldContext world,EveEntityLoader actor,IReadOnlyDictionary<string,EveEntityLoader> entities)
    {
        if(world==null||entities==null||entities.Count>256||entities.Any(x=>string.IsNullOrWhiteSpace(x.Key)||x.Key.Length>4096||x.Value==null))throw new ArgumentException("Bounded stable entity reference map required.");
        Session=world.Session;World=world;Actor=actor;this.entities=new System.Collections.ObjectModel.ReadOnlyDictionary<string,EveEntityLoader>(entities.ToDictionary(x=>x.Key,x=>x.Value));
    }
    public string Reference(EveEntityLoader actor)
    {foreach(var pair in entities)if(ReferenceEquals(pair.Value,actor))return pair.Key;throw new InvalidOperationException("Checkpoint reference is outside the captured world.");}
    public EveEntityLoader Resolve(string key)=>entities.TryGetValue(key,out var value)?value:throw new FormatException("Unresolved checkpoint reference.");
}
public sealed class PNWorldStateDefinition : EveIIdentified
{
    public EveId Id { get; }
    public int Schema { get; }
    public int MaximumBytes { get; }
    public string Implementation=>ImplementationType.FullName;
    [JsonIgnore] public Type ImplementationType { get; }
    private readonly Func<IPNWorldStateProvider> factory;
    public PNWorldStateDefinition(EveId id,int schema,int maximumBytes,Type type,Func<IPNWorldStateProvider> factory)
    {if(!id.IsValid||schema<1||maximumBytes<1||maximumBytes>64*1024*1024||type==null||type.IsAbstract||!typeof(IPNWorldStateProvider).IsAssignableFrom(type))throw new ArgumentException("Versioned bounded selected state provider required.");Id=id;Schema=schema;MaximumBytes=maximumBytes;ImplementationType=type;this.factory=factory??throw new ArgumentNullException(nameof(factory));}
    public IPNWorldStateProvider Create()
    {var result=factory();if(result==null||!ImplementationType.IsInstanceOfType(result))throw new InvalidOperationException("Invalid selected state provider.");return result;}
}
#endif
