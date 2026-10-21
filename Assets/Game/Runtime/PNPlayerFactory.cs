#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

public readonly struct PNLocalPlayerBound
{
    public readonly Transform Target;
    public EveWorldContext World=>Target!=null?Target.GetComponent<EveEntityIdentity>()?.World:null;
    public ulong Membership=>Target!=null?Target.GetComponent<EveEntityIdentity>()?.MembershipRevision??0:0;
    public PNLocalPlayerBound(Transform target){Target=target;}
    public static readonly EveId Event=EveId.Parse("project_n:local_player_bound");
}
// Factory instances, including deferred Unity destruction, are never authored enrollment candidates.
public sealed class PNFactoryEntity : MonoBehaviour
{[NonSerialized] public string Template,SceneKey,SpawnEntry;[NonSerialized] public bool Player;[NonSerialized] public GameObject Prototype;}
public sealed class PNPlayerFactory : IPNWorldEntityFactory
{
    private readonly EveSessionContext session;
    private readonly Dictionary<(Guid,GameObject),EveEasyPooling> pools=new Dictionary<(Guid,GameObject),EveEasyPooling>();
    private readonly Dictionary<EveEntityLoader,EveEasyPooling> borrowed=new Dictionary<EveEntityLoader,EveEasyPooling>();
    private readonly HashSet<EveEntityLoader> authored=new HashSet<EveEntityLoader>();
    private readonly HashSet<EveEntityLoader> detached=new HashSet<EveEntityLoader>();
    private readonly Dictionary<EveEntityLoader,Scene> authoredOrigins=new Dictionary<EveEntityLoader,Scene>();
    private readonly Dictionary<GameObject,IPNEntityResourceOwner[]> resources=new Dictionary<GameObject,IPNEntityResourceOwner[]>();
    private readonly Dictionary<GameObject,EveWorldContext> resourceWorlds=new Dictionary<GameObject,EveWorldContext>();
    private readonly HashSet<EveWorldContext> cleanupWorlds=new HashSet<EveWorldContext>();
    private readonly IPNWorldRuntime explicitWorlds;
    private IPNWorldRuntime worlds=>explicitWorlds??PNWorldRuntime.Resolve(session);
    public PNPlayerFactory(EveSessionContext session) : this(session,null) { }
    public PNPlayerFactory(EveSessionContext session,IPNWorldRuntime worlds){this.session=session;explicitWorlds=worlds;}
    private EveWorldContext LegacyWorld()
    {
        if(worlds==null||worlds.DefaultWorld==null||session.Worlds.Snapshot().Count!=1)throw new InvalidOperationException("Use explicit world/slot/spawn when no unambiguous default exists.");
        return worlds.DefaultWorld;
    }
    private PNWorldBuildContext Resolve(PNEntityRequest request)
    {
        request.World=request.World??LegacyWorld();
        if(request.WorldIncarnation==0)request.WorldIncarnation=request.World.Incarnation;
        if(!ReferenceEquals(request.World.Session,session)||request.WorldIncarnation!=request.World.Incarnation||
            request.World.State!=EveWorldState.Ready&&request.World.State!=EveWorldState.Paused)throw new InvalidOperationException("Stale/nonresident factory world.");
        var build=worlds.Get(request.World);
        if(request.ContentSlot==null) {if(build.Slots.Count!=1)throw new InvalidOperationException("Explicit content slot required.");request.ContentSlot=build.Slots[0].Id;}
        return build;
    }
    private PNWorldSlot Slot(PNEntityRequest request)=>Resolve(request).Slots.Single(x=>x.Id==request.ContentSlot);
    private PNPlayerSpawnPoint Spawn(PNEntityRequest request)
    {
        var values=Slot(request).Roots.Where(x=>x!=null).SelectMany(x=>x.GetComponentsInChildren<PNPlayerSpawnPoint>(true)).ToArray();
        var spawn=request.SpawnEntry==null?values.Single():values.Single(x=>x.SpawnEntry==request.SpawnEntry);
        spawn.Validate();request.SpawnEntry=spawn.SpawnEntry;return spawn;
    }
    public IEnumerable<PNEntityRequest> SceneEntities()=>SceneEntities(LegacyWorld());
    public IEnumerable<PNEntityRequest> SceneEntities(EveWorldContext world,string slot=null)
    {
        var build=worlds.Get(world);PNWorldRuntime.Validate(build);
        foreach(var content in build.Slots.Where(x=>slot==null||x.Id==slot))
        foreach(var loader in content.Roots.Where(x=>x!=null).SelectMany(x=>x.GetComponentsInChildren<EveEntityLoader>(true)))
        {
            if(loader.GetComponent<PNFactoryEntity>()!=null||loader.IsReady)continue;
            var key=loader.GetComponent<EveSceneEntityKey>();
            yield return new PNEntityRequest {World=world,WorldIncarnation=world.Incarnation,ContentSlot=content.Id,SceneKey=key.SceneKey,EntityKey=key.EntityKey,Character=loader.GetComponent<PNStat>().CharacterDefinitionId,Position=loader.transform.position};
        }
    }
    public PNEntityRequest Player(long owner)=>Player(owner,LegacyWorld(),null,null);
    public PNEntityRequest Player(long owner,EveWorldContext world,string slot,string spawn)
    {
        if(owner<0)throw new ArgumentOutOfRangeException(nameof(owner));
        var request=new PNEntityRequest{Player=true,Owner=owner,World=world,WorldIncarnation=world.Incarnation,ContentSlot=slot,SpawnEntry=spawn,Template="base:scene_spawn"};
        var point=Spawn(request);
        session.Registry.Characters.Get(EveId.Parse(point.Character));
        request.Character=point.Character;request.SceneKey=point.SceneKey;request.EntityKey="";request.Position=point.transform.position;return request;
    }
    public EveEntityLoader Get(PNEntityRequest request)
    {
        Resolve(request);
        if(cleanupWorlds.Add(request.World))request.World.RegisterCleanup(()=>ReleaseWorld(request.World));
        EveEntityLoader loader;
        if(request.Player||!string.IsNullOrEmpty(request.Template))
        {
            if(request.Player&&request.Template==null)request.Template="base:scene_spawn";
            if(request.Player)Spawn(request);
            session.Registry.Characters.Get(EveId.Parse(request.Character));
            var prefab=session.Registry.Get<PNEntityTemplateDefinition>().Get(EveId.Parse(request.Template)).Resolve(Resolve(request),request.ContentSlot,request.SpawnEntry);
            if(request.Player)prefab=PNPlayerPrefabCatalog.For(session,request.Character,prefab);
            if(!pools.TryGetValue((request.World.InstanceId,prefab),out var pool))
            {
                pool=new EveEasyPooling(prefab);pools.Add((request.World.InstanceId,prefab),pool);
                pool.Created+=item=>{item.AddComponent<PNFactoryEntity>().Prototype=prefab;SceneManager.MoveGameObjectToScene(item.transform.root.gameObject,worlds.Get(request.World).PhysicsScene);resourceWorlds[item]=request.World;resources[item]=item.GetComponentsInChildren<MonoBehaviour>(true).OfType<IPNEntityResourceOwner>().ToArray();};
                pool.Destroyed+=DisposeResources;
            }
            var root=pool.Get();
            try{loader=root.GetComponent<EveEntityLoader>();if(loader==null)throw new InvalidOperationException("Factory entity lost its loader.");borrowed.Add(loader,pool);}
            catch{pool.Release(root);throw;}

        }
        else
        {
            var key=Slot(request).Roots.Where(x=>x!=null).SelectMany(x=>x.GetComponentsInChildren<EveSceneEntityKey>(true)).Single(x=>x.SceneKey==request.SceneKey&&x.EntityKey==request.EntityKey);
            loader=key.GetComponent<EveEntityLoader>();
            if(loader.gameObject.activeInHierarchy||!authored.Add(loader))throw new InvalidOperationException("Authored entity already enrolled.");
            authoredOrigins[loader]=Slot(request).SourceScene;
        }
        return loader;
    }
    public void Configure(EveEntityLoader loader,PNEntityRequest request)
    {
        Resolve(request);
        var marker=loader.GetComponent<PNFactoryEntity>();if(marker!=null){marker.Template=request.Template;marker.SceneKey=request.SceneKey;marker.SpawnEntry=request.SpawnEntry;marker.Player=request.Player;}
        if(session.Role==EveSessionRole.Client)loader.GetComponent<EveEntityIdentity>().BindReplicaWorld(request.World,request.ContentSlot,request.MembershipRevision);
        else loader.GetComponent<EveEntityIdentity>().BindWorld(request.World,request.ContentSlot);
        var gate=loader.GetComponent<PNNetworkInputGate>();if(gate!=null)gate.Blocked=false;
        if(!request.Player&&string.IsNullOrEmpty(request.Template))return;
        var spawn=request.Player?Spawn(request):null;var root=loader.gameObject;
        if(spawn!=null&&request.SceneKey!=spawn.SceneKey)throw new InvalidOperationException("Player factory content/scene mismatch.");
            SceneManager.MoveGameObjectToScene(root,worlds.Get(request.World).PhysicsScene);
            root.transform.position=request.Position;
            // Explicit initialization occurs before prepare. Awake reuses these same input owners.
            if(Application.isPlaying){loader.GetComponent<PNPlayerMove>()?.EnsureInputInitialized();
            loader.GetComponent<PNPlayerSkill>()?.EnsureInputInitialized();}
            if(spawn!=null)foreach(var shadow in root.GetComponentsInChildren<MonoBehaviour>(true).OfType<IPNSceneEntityVisual>())shadow.BindGround(spawn.Ground);
    }
    public void Return(EveEntityLoader loader)
    {
        if(loader==null){borrowed.Remove(loader);authored.Remove(loader);detached.Remove(loader);authoredOrigins.Remove(loader);foreach(var root in resources.Keys.Where(x=>x==null).ToArray())DisposeResources(root);return;}
        if(loader.State!=EveEntityLoadState.Idle)throw new InvalidOperationException("Cleanup must complete before return.");
        loader.gameObject.SetActive(false);loader.GetComponent<EveEntityIdentity>().ResetIdentity();
        if(borrowed.TryGetValue(loader,out var pool)){pool.Release(loader.gameObject);borrowed.Remove(loader);}
        else if(detached.Remove(loader)){DisposeResources(loader.gameObject);DestroyReturned(loader.gameObject);}
        else
        {
            authored.Remove(loader);
            if(authoredOrigins.TryGetValue(loader,out var origin))
            {
                authoredOrigins.Remove(loader);
                if(origin.IsValid()&&origin.isLoaded){if(loader.gameObject.scene!=origin){loader.transform.SetParent(null,true);SceneManager.MoveGameObjectToScene(loader.gameObject,origin);}}
                else DestroyReturned(loader.gameObject);
            }
        }
    }
    private static void DestroyReturned(GameObject root)
    {if(Application.isPlaying)UnityEngine.Object.Destroy(root);else UnityEngine.Object.DestroyImmediate(root);}
    public void MoveWorldOwnership(EveEntityLoader loader,EveWorldContext destination)
    {
        if(loader?.IsReady!=true||!loader.LoadContext.Identity.IsGameplayHeld||loader.LoadContext.Identity.World!=destination)throw new InvalidOperationException("Factory ownership follows held committed membership.");
        if(cleanupWorlds.Add(destination))destination.RegisterCleanup(()=>ReleaseWorld(destination));
        if(borrowed.TryGetValue(loader,out var pool))
        {pool.ExtractActive(loader.gameObject);borrowed.Remove(loader);detached.Add(loader);}
        if(!detached.Contains(loader)&&!authored.Contains(loader))throw new InvalidOperationException("Unknown factory actor.");
        if(resourceWorlds.ContainsKey(loader.gameObject))resourceWorlds[loader.gameObject]=destination;
    }
    public void Published(EveEntityLoader loader)
    {
        loader.gameObject.SetActive(true);
        PNWorldPresentation.RegisterActor(session,loader.gameObject);
        var id=loader.GetComponent<EveEntityIdentity>();
        if(loader.GetComponent<PNPlayerStat>()!=null && (session.Role==EveSessionRole.Offline||id.OwnerId==(session.Role==EveSessionRole.Host?0:session.LocalOwnerId.GetValueOrDefault(-1))))
        {
            if(ReferenceEquals(PNLocalWorldContext.Current?.Session,session))PNLocalWorldContext.Current.Select(id.World,loader);
            foreach(var error in EveEventManager.Global.PublishSafely(PNLocalPlayerBound.Event,new PNLocalPlayerBound(loader.transform)))Debug.LogException(error,loader);
        }
    }
    public void RebindWorldPresentation(EveEntityLoader loader,PNEntityRequest request)
    {
        if(loader.LoadContext.Identity.World!=request.World)throw new InvalidOperationException("Presentation binding must follow actor membership.");
        if(request.Player){var point=Spawn(request);foreach(var visual in loader.GetComponentsInChildren<MonoBehaviour>(true).OfType<IPNSceneEntityVisual>())visual.BindGround(point.Ground);}
        var marker=loader.GetComponent<PNFactoryEntity>();if(marker!=null){marker.SceneKey=request.SceneKey;marker.SpawnEntry=request.SpawnEntry;marker.Template=request.Template;}
    }
    public void ReleaseWorld(EveWorldContext world)
    {
        if(borrowed.Keys.Concat(authored).Concat(detached).Any(x=>x!=null&&ReferenceEquals(x.GetComponent<EveEntityIdentity>().World,world)))throw new InvalidOperationException("World factory still has borrowed actors.");
        foreach(var root in resourceWorlds.Where(x=>ReferenceEquals(x.Value,world)).Select(x=>x.Key).ToArray())DisposeResources(root);
        foreach(var pair in pools.Where(x=>x.Key.Item1==world.InstanceId).ToArray())
        {pair.Value.Dispose();pools.Remove(pair.Key);}
    }
    public void Dispose()
    {
        if(borrowed.Count!=0||authored.Count!=0||detached.Count!=0)throw new InvalidOperationException("Factory still owns resident entities.");
        foreach(var root in resources.Keys.ToArray())DisposeResources(root);
        foreach(var pool in pools.Values)pool.Dispose();pools.Clear();
    }
    private void DisposeResources(GameObject root)
    {
        if(!resources.TryGetValue(root,out var owners))return;
        foreach(var owner in owners)owner.DisposeEntityResources();
        resources.Remove(root);
        resourceWorlds.Remove(root);
    }
}
#endif
