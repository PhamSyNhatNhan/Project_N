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
{[NonSerialized] public string Entity,Template,SceneKey,SpawnEntry;[NonSerialized] public GameObject Prototype;
    // Receipts of the spawn Buffs the entity's role applied when it was published (queued: they resolve on the Buff manager's next advance).
    [NonSerialized] public readonly List<PNBuffReceipt> SpawnBuffReceipts=new List<PNBuffReceipt>();}
public sealed class PNEntityFactory : IPNWorldEntityFactory
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
    // The scheduler that owns the spawned entities; an entity with a lifetime is retired through it. The game session sets it.
    public EveEntityPreparationScheduler Scheduler{get;set;}
    // Spawn points per (world, slot), read once when first asked and dropped with the world.
    private readonly Dictionary<(Guid,string),PNPlayerSpawnPoint[]> spawnPoints=new Dictionary<(Guid,string),PNPlayerSpawnPoint[]>();
    private IPNWorldRuntime worlds=>explicitWorlds??PNWorldRuntime.Resolve(session);
    public PNEntityFactory(EveSessionContext session) : this(session,null) { }
    public PNEntityFactory(EveSessionContext session,IPNWorldRuntime worlds){this.session=session;explicitWorlds=worlds;}
    private EveWorldContext LegacyWorld()
    {
        if(worlds==null||worlds.DefaultWorld==null||session.Worlds.Snapshot().Count!=1)throw new InvalidOperationException("Use explicit world/slot/spawn when no unambiguous default exists.");
        return worlds.DefaultWorld;
    }
    private PNWorldBuildContext Resolve(PNEntityRequest request)
    {
        request.World=request.World??LegacyWorld();
        if(string.IsNullOrEmpty(request.Entity))request.Entity=request.Template;else if(string.IsNullOrEmpty(request.Template))request.Template=request.Entity;
        else if(request.Entity!=request.Template)throw new InvalidOperationException("Entity and Template name different roles.");
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
        var values=SpawnPoints(request);
        var spawn=request.SpawnEntry==null?values.Single():values.Single(x=>x.SpawnEntry==request.SpawnEntry);
        spawn.Validate();request.SpawnEntry=spawn.SpawnEntry;return spawn;
    }
    private PNPlayerSpawnPoint[] SpawnPoints(PNEntityRequest request)
    {
        Resolve(request);
        var key=(request.World.InstanceId,request.ContentSlot);
        if(spawnPoints.TryGetValue(key,out var cached)&&cached.All(x=>x!=null))return cached;
        var values=Slot(request).Roots.Where(x=>x!=null).SelectMany(x=>x.GetComponentsInChildren<PNPlayerSpawnPoint>(true)).ToArray();
        spawnPoints[key]=values;return values;
    }
    private PNEntityDefinition Role(PNEntityRequest request)=>session.Registry.Instances().GetEffectiveKind<PNEntityDefinition>(EveId.Parse(request.Entity??request.Template));
    // A player role (its recipe says so) is placed at a spawn point and gets input and ground binding.
    private static bool IsPlayer(PNEntityDefinition role)=>role.IsPlayer;
    private bool HasRole(PNEntityRequest request)=>!string.IsNullOrEmpty(request.Entity)||!string.IsNullOrEmpty(request.Template);
    private static PNEntityRecipe Recipe(PNEntityDefinition role)=>role.Recipe as PNEntityRecipe??throw new InvalidOperationException("Entity "+role.Id+" has no game recipe.");
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
        var request=new PNEntityRequest{Owner=owner,World=world,WorldIncarnation=world.Incarnation,ContentSlot=slot,SpawnEntry=spawn,Template="base:scene_spawn",Entity="base:scene_spawn"};
        var point=Spawn(request);
        session.Registry.Characters.Get(EveId.Parse(point.Character));
        request.Character=point.Character;request.SceneKey=point.SceneKey;request.EntityKey="";request.Position=point.transform.position;return request;
    }
    // The prefab the request spawns from (validates the role, the character and the spawn point). The transfer coordinator compares it with the prototype of a live actor.
    public GameObject ResolvePrefab(PNEntityRequest request)
    {
        Resolve(request);
        if(!HasRole(request))throw new InvalidOperationException("An authored entity has no role to resolve a prefab from.");
        var role=Role(request);
        session.Registry.Characters.Get(EveId.Parse(request.Character));
        // The spawn point supplies the prefab only for a character that names none; entities without a spawn point need a catalog key.
        var point=IsPlayer(role)||request.SpawnEntry!=null?Spawn(request):null;
        return PNEntityPrefabs.For(session,request.Character,point?.Prefab,role)??throw new InvalidOperationException("Entity "+request.Entity+" of character "+request.Character+" has no prefab: name one with body.prefab.");
    }
    public EveEntityLoader Get(PNEntityRequest request)
    {
        Resolve(request);
        if(cleanupWorlds.Add(request.World))request.World.RegisterCleanup(()=>ReleaseWorld(request.World));
        EveEntityLoader loader;
        if(HasRole(request))
        {
            var prefab=ResolvePrefab(request);
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
        var marker=loader.GetComponent<PNFactoryEntity>();if(marker!=null){marker.Entity=request.Entity;marker.Template=request.Template;marker.SceneKey=request.SceneKey;marker.SpawnEntry=request.SpawnEntry;}
        if(session.Role==EveSessionRole.Client)loader.GetComponent<EveEntityIdentity>().BindReplicaWorld(request.World,request.ContentSlot,request.MembershipRevision);
        else loader.GetComponent<EveEntityIdentity>().BindWorld(request.World,request.ContentSlot);
        var gate=loader.GetComponent<PNNetworkInputGate>();if(gate!=null)gate.Blocked=false;
        if(!HasRole(request))return;
        var role=Role(request);var spawn=IsPlayer(role)?Spawn(request):null;var root=loader.gameObject;
        if(spawn!=null&&request.SceneKey!=spawn.SceneKey)throw new InvalidOperationException("Player factory content/scene mismatch.");
        SceneManager.MoveGameObjectToScene(root,worlds.Get(request.World).PhysicsScene);
        root.transform.position=request.Position;
        Recipe(role).Configure(loader,request,spawn);
        var body=PNBodySettings.Parse(session.Registry.Characters.Get(EveId.Parse(request.Character)).Data);
        double? lifetime=request.Lifetime??role.Settings.Lifetime;
        (loader.GetComponent<PNEntityRole>()??loader.gameObject.AddComponent<PNEntityRole>()).Apply(request.Entity,role.Settings,body,lifetime);
        var life=loader.GetComponent<PNEntityLifetime>();
        // Only the authority removes an entity by time; a client entity is removed when the host's removal reaches it.
        if(lifetime.HasValue&&session.Role!=EveSessionRole.Client)
        {
            life=life??loader.gameObject.AddComponent<PNEntityLifetime>();
            life.Begin(lifetime.Value,()=>{if(Scheduler==null)throw new InvalidOperationException("Entity lifetime needs the factory scheduler.");Scheduler.RetirePublished(loader);});
        }
        else life?.Cancel();
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
        // An authored entity has no role of its own: it keeps the player behaviour (a no-op unless it is a player stat).
        var entity=loader.GetComponent<PNFactoryEntity>()?.Entity;
        var role=session.Registry.Instances().GetEffectiveKind<PNEntityDefinition>(EveId.Parse(string.IsNullOrEmpty(entity)?"base:scene_spawn":entity));
        Recipe(role).Published(session,loader);
        // Buffs a role applies at spawn (an elite's aura). Only the authority applies them; clients receive the result.
        if(string.IsNullOrEmpty(entity)||session.Role==EveSessionRole.Client||role.Settings.SpawnBuffs.Count==0)return;
        var buffs=loader.GetComponent<PNBuffManager>();var stat=loader.GetComponent<PNStat>();
        if(buffs==null||stat==null)throw new InvalidOperationException("Entity "+entity+" names spawn Buffs but has no Buff manager or stat.");
        var marker=loader.GetComponent<PNFactoryEntity>();marker.SpawnBuffReceipts.Clear();
        foreach(var id in role.Settings.SpawnBuffs)marker.SpawnBuffReceipts.Add(buffs.Buffs.Apply(new PNBuffApplyRequest(id,PNCombatSource.Capture(stat))));
    }
    public void RebindWorldPresentation(EveEntityLoader loader,PNEntityRequest request)
    {
        if(loader.LoadContext.Identity.World!=request.World)throw new InvalidOperationException("Presentation binding must follow actor membership.");
        if(HasRole(request)&&IsPlayer(Role(request))){var point=Spawn(request);foreach(var visual in loader.GetComponentsInChildren<MonoBehaviour>(true).OfType<IPNSceneEntityVisual>())visual.BindGround(point.Ground);}
        var marker=loader.GetComponent<PNFactoryEntity>();if(marker!=null){marker.SceneKey=request.SceneKey;marker.SpawnEntry=request.SpawnEntry;marker.Template=request.Template;marker.Entity=request.Entity??request.Template;}
    }
    public void ReleaseWorld(EveWorldContext world)
    {
        if(borrowed.Keys.Concat(authored).Concat(detached).Any(x=>x!=null&&ReferenceEquals(x.GetComponent<EveEntityIdentity>().World,world)))throw new InvalidOperationException("World factory still has borrowed actors.");
        foreach(var root in resourceWorlds.Where(x=>ReferenceEquals(x.Value,world)).Select(x=>x.Key).ToArray())DisposeResources(root);
        foreach(var pair in pools.Where(x=>x.Key.Item1==world.InstanceId).ToArray())
        {pair.Value.Dispose();pools.Remove(pair.Key);}
        foreach(var key in spawnPoints.Keys.Where(x=>x.Item1==world.InstanceId).ToArray())spawnPoints.Remove(key);
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
