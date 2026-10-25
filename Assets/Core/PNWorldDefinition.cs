#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

// Content definitions are immutable. The selected factory makes a provider per load.
public sealed class PNWorldDefinition : EveIIdentified,EveIRegistrationReferences
{
    public EveId Id { get; }
    public string ContentHash { get; }
    public string Implementation => ImplementationType.FullName;
    [JsonIgnore] public Type ImplementationType { get; }
    private readonly Func<IPNWorldProvider> factory;
    public PNWorldPolicy Policy { get; }
    public PNWorldDefinition(EveId id,string contentHash,Type type,Func<IPNWorldProvider> factory)
        : this(id,contentHash,type,factory,new PNWorldPolicy()) { }
    public PNWorldDefinition(EveId id,string contentHash,Type type,Func<IPNWorldProvider> factory,PNWorldPolicy policy)
    {
        if(!id.IsValid||id.ToString().Length>128||contentHash==null||contentHash.Length!=64||contentHash.Any(c=>!Uri.IsHexDigit(c))||type==null||type.IsAbstract||type.ContainsGenericParameters||!typeof(IPNWorldProvider).IsAssignableFrom(type))
            throw new ArgumentException("Invalid world definition.");
        Id=id;ContentHash=contentHash;ImplementationType=type;this.factory=factory??throw new ArgumentNullException(nameof(factory));Policy=policy??throw new ArgumentNullException(nameof(policy));
    }
    public IPNWorldProvider Create()
    { var result=factory(); if(result==null||!ImplementationType.IsInstanceOfType(result))throw new InvalidOperationException("Invalid selected world provider.");return result; }
    public void ValidateRegistrationReferences(string owner,IReadOnlyCollection<string> dependencies)
    {
        foreach(var id in Policy.TransferParticipants.Concat(Policy.ActorStateProviders).Concat(Policy.WorldStateProviders))
            if(id.Namespace!="base"&&id.Namespace!=owner&&!dependencies.Contains(id.Namespace))throw new InvalidOperationException("World provider reference requires a declared content dependency: "+id);
    }
    public static string HashContent(string canonicalContent)
    { using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonicalContent))).Replace("-","").ToLowerInvariant(); }
}
public interface IPNWorldProvider
{
    IEnumerator Load(PNWorldBuildContext context);
}
// Optional companion: descriptor is pure data needed before Load, not post-load actor state.
public interface IPNPortableWorldProvider
{
    Newtonsoft.Json.Linq.JToken CaptureLoadDescriptor(PNWorldBuildContext context);
    void ValidateLoadDescriptor(Newtonsoft.Json.Linq.JToken descriptor);
}
public interface IPNWorldLifecycleParticipant
{
    void WorldReady(PNWorldBuildContext context);
    void WorldUnloading(PNWorldBuildContext context);
    void WorldDisposed(PNWorldBuildContext context);
}
public interface IPNWorldPresentationPart
{
    bool PresentationVisible { get; set; }
}
public sealed class PNWorldSlot
{
    public string Id { get; }
    public Scene SourceScene { get; }
    public IReadOnlyList<GameObject> Roots { get; }
    public bool Owned { get; }
    internal PNWorldSlot(string id,Scene source,IReadOnlyList<GameObject> roots,bool owned)
    { Id=id;SourceScene=source;Roots=roots;Owned=owned; }
}
public sealed class PNWorldBuildContext
{
    private Newtonsoft.Json.Linq.JToken loadDescriptor;
    public Newtonsoft.Json.Linq.JToken LoadDescriptor => loadDescriptor?.DeepClone();
    public void SetLoadDescriptor(Newtonsoft.Json.Linq.JToken value)
    {if(!IsCurrent||slots.Count!=0)throw new InvalidOperationException("Descriptor must precede content loading.");loadDescriptor=value?.DeepClone();}
    public EveWorldContext World { get; }
    public Scene PhysicsScene { get; }
    public PNWorldEnvironment Environment { get; set; }
    private readonly List<PNWorldSlot> slots=new List<PNWorldSlot>();
    public IReadOnlyList<PNWorldSlot> Slots => slots.AsReadOnly();
    public bool IsCurrent => World.Session.IsActive && World.State==EveWorldState.Loading;
    public PNWorldBuildContext(EveWorldContext world,Scene scene) { World=world;PhysicsScene=scene; }
    // Register cleanup BEFORE starting acquisition, including AsyncOperations that cannot cancel.
    public void Own(Action cleanup) => World.RegisterCleanup(cleanup);
    public void AddSlot(string id,Scene source,IEnumerable<GameObject> roots,bool owned=true)
    {
        if(!IsCurrent||string.IsNullOrWhiteSpace(id)||id.Length>128||slots.Any(x=>x.Id==id))throw new InvalidOperationException("Invalid/duplicate content slot or stale load.");
        var copy=roots.ToArray();
        if(Environment==null)Environment=PNWorldEnvironment.Capture(source);
        if(copy.Any(x=>x==null||x.transform.parent!=null||x.scene!=source)||slots.Any(x=>x.Roots.Intersect(copy).Any()))throw new InvalidOperationException("Content slots require distinct roots and provenance.");
        slots.Add(new PNWorldSlot(id,source,Array.AsReadOnly(copy),owned));
        foreach(var root in copy){if(root.scene!=PhysicsScene)SceneManager.MoveGameObjectToScene(root,PhysicsScene);PNWorldPresentationRegistration.Register(World,root);}
    }
}
// Additive companion API: existing ModAPI6 entity factories retain their ABI.
public interface IPNWorldEntityFactory : IPNEntityFactory
{
    IEnumerable<PNEntityRequest> SceneEntities(EveWorldContext world,string slot=null);
    PNEntityRequest Player(long owner,EveWorldContext world,string slot,string spawn);
    void ReleaseWorld(EveWorldContext world);
}
public interface IPNWorldRuntime
{
    EveWorldContext DefaultWorld { get; }
    PNWorldBuildContext Get(EveWorldContext world);
    EveWorldContext Create(EveId definition,Guid instance=default,ulong incarnation=1);
    EveWorldContext CreateReplica(EveId definition,Guid instance,ulong incarnation,ulong subscription,bool defaultWorld);
    event Action<EveWorldContext> WorldReady;
    event Action<EveWorldContext> WorldUnloading;
    bool Unload(EveWorldContext world);
}
public interface IPNWorldPopulation
{bool RestoredPopulation(EveWorldContext world);}
// Room services/shells never become content of an adopted world.
public sealed class PNRoomResource : MonoBehaviour { }
#endif
