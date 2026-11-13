#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

// One service per play, sharing the entity scheduler's preparation budget.
public sealed class PNWorldRuntime : IPNWorldRuntime,IPNWorldPopulation
{
    private sealed class Entry
    {
        public PNWorldBuildContext Build;
        public IDisposable Job;
        public IEnumerator Load;
        public double Started;
        public bool Closing;
        public bool DeferReady,Announced,MakeDefault;
        public IPNWorldProvider Provider;
        public bool ProviderReady,ProviderUnloading,ProviderDisposed;
    }
    private readonly EveSessionContext session;
    private readonly EveEntityPreparationScheduler scheduler;
    private readonly Dictionary<Guid,Entry> entries=new Dictionary<Guid,Entry>();
    private static readonly Dictionary<EveSessionContext,PNWorldRuntime> services=new Dictionary<EveSessionContext,PNWorldRuntime>();
    public static PNWorldRuntime Resolve(EveSessionContext session)=>services.TryGetValue(session,out var service)?service:throw new InvalidOperationException("Play world runtime has not been attached.");
    public EveWorldContext DefaultWorld { get; private set; }
    public Action<EveWorldContext> BeforeUnload;
    public Action<EveWorldContext> Ready;
    public event Action<EveWorldContext> WorldReady;
    public event Action<EveWorldContext> WorldUnloading;
    // ---- world features (Docs/EntityModulePlan.md E2) ----
    // A world's features = the mode's + its definition's + its creator's. The host (or an offline player) works them out ONCE when the world is created; a client never works them out, it is given the host's final set.
    // Who knows the mode's features: the game session wires it for a host or an offline player; a runtime built by itself (a test) has none.
    public Func<IEnumerable<EveId>> ModeFeatureSource;
    private readonly Dictionary<Guid,PNWorldFeatures> creatorFeatures=new Dictionary<Guid,PNWorldFeatures>();
    private readonly Queue<Guid> creatorOrder=new Queue<Guid>();
    private const int MaximumRememberedCreators=256;
    private PNWorldFeatures ModeFeatures()=>new PNWorldFeatures(ModeFeatureSource?.Invoke()??Array.Empty<EveId>());
    private PNWorldFeatures Compose(PNWorldDefinition definition,PNWorldFeatures creator,IEnumerable<EveId> supplied)
    {
        PNWorldFeatures result;
        if(supplied!=null)result=new PNWorldFeatures(supplied);
        else if(session.Role==EveSessionRole.Client)result=PNWorldFeatures.Empty;
        else result=ModeFeatures().With(definition.Policy.Features).With(creator?.Ids);
        var registered=session.Registry.Get<PNWorldFeatureDefinition>();
        foreach(var id in result.Ids)if(!registered.TryGet(id,out _))throw new InvalidOperationException("World "+definition.Id+" has unregistered feature "+id+".");
        return result;
    }
    // What the creator of a world instance asked for stays with the instance, so a discarded world that is created again (or restored from a checkpoint) in this play gets the same features. Bounded: the oldest are forgotten.
    private void RememberCreator(Guid instance,PNWorldFeatures creator)
    {
        if(creator==null||creator.Count==0||creatorFeatures.ContainsKey(instance))return;
        creatorFeatures.Add(instance,creator);creatorOrder.Enqueue(instance);
        while(creatorOrder.Count>MaximumRememberedCreators)creatorFeatures.Remove(creatorOrder.Dequeue());
    }
    private Scene entryScene;
    private EveId entryDefinition=EveId.Parse("base:scene_world");
    public EveId EntryDefinition=>entryDefinition;
    public void SetEntryScene(Scene scene,EveId definition=default)
    { if(entryScene.IsValid())throw new InvalidOperationException("Entry scene already recorded.");entryScene=scene;if(definition.IsValid)entryDefinition=definition; }
    private int activeLoads;
    public PNWorldRuntime(EveSessionContext session,EveEntityPreparationScheduler scheduler)
    {
        this.session=session;this.scheduler=scheduler;
        services.Add(session,this);
        session.RegisterCleanup(Dispose,afterEntities:true);
    }
    public PNWorldBuildContext Get(EveWorldContext world)
    {
        if(world==null||!ReferenceEquals(world.Session,session)||!entries.TryGetValue(world.InstanceId,out var entry)||!ReferenceEquals(entry.Build.World,world))throw new InvalidOperationException("Unknown world runtime.");
        return entry.Build;
    }
    public bool RestoredPopulation(EveWorldContext world)=>entries.TryGetValue(world.InstanceId,out var entry)&&ReferenceEquals(entry.Build.World,world)&&entry.DeferReady;
    // Existing scene entry is a borrowed content slot. Scan only once at the boundary;
    // factory/enrollment never search globally after the world has been registered.
    public EveWorldContext Adopt(Scene scene,Guid instance=default,ulong incarnation=1) => Adopt(scene,instance,incarnation,0,null);
    private EveWorldContext Adopt(Scene scene,Guid instance,ulong incarnation,ulong subscription,IEnumerable<EveId> supplied)
    {
        var definition=session.Registry.Get<PNWorldDefinition>().Get(entryDefinition);
        var features=Compose(definition,null,supplied);
        var world=subscription==0?session.Worlds.Create(instance==Guid.Empty?Guid.NewGuid():instance,incarnation,definition.Id,definition.ContentHash):session.Worlds.CreateReplica(instance,incarnation,definition.Id,definition.ContentHash,subscription);
        var owner=CreateOwner(world);world.RegisterCleanup(()=>UnloadScene(owner));
        world.ConfigurePhysics(owner,Application.isPlaying);world.BeginLoading();
        var build=new PNWorldBuildContext(world,owner,features);
        entries.Add(world.InstanceId,new Entry{Build=build});
        var roots=scene.GetRootGameObjects().Where(x=>x.GetComponent<EveRuntime>()==null&&x.GetComponent<PNRoomResource>()==null&&!x.GetComponentsInChildren<PNFactoryEntity>(true).Any()).ToArray();
        world.RegisterCleanup(()=>{foreach(var root in roots)if(root!=null&&root.scene==owner&&scene.IsValid()&&scene.isLoaded)SceneManager.MoveGameObjectToScene(root,scene);});
        build.AddSlot("main",scene,roots,false);
        PNWorldNavigation.Prepare(build);
        world.CompleteLoading();DefaultWorld=world;PublishStaged(world);return world;
    }
    public EveWorldContext Create(EveId definition,Guid instance=default,ulong incarnation=1)=>Create(definition,instance,incarnation,0);
    // The creator's features (for example the run's) join the mode's and the definition's. Not available to a client: its worlds are replicas.
    public EveWorldContext Create(EveId definition,Guid instance,ulong incarnation,IEnumerable<EveId> features)
    {
        if(session.Role==EveSessionRole.Client)throw new InvalidOperationException("A client does not choose world features; it is given the host's set.");
        return Create(definition,instance,incarnation,0,false,null,features==null?null:new PNWorldFeatures(features));
    }
    // G13: the first world of a play made from its definition (host or offline): it becomes DefaultWorld when it is ready, like an adopted entry scene does at once.
    public EveWorldContext CreateDefault(EveId definition)
    {
        if(session.Role==EveSessionRole.Client)throw new InvalidOperationException("A client's default world is a replica of the host's.");
        if(DefaultWorld!=null)throw new InvalidOperationException("The play already has a default world.");
        return Create(definition,Guid.Empty,1,0,false,null,null,null,true);
    }
    public EveWorldContext CreateStaged(EveId definition,Guid instance,ulong incarnation)=>Create(definition,instance,incarnation,0,true);
    public bool SupportsPersistence(EveWorldContext world)=>entries.TryGetValue(world.InstanceId,out var entry)&&entry.Provider is IPNPortableWorldProvider;
    public void ValidateLoadDescriptor(EveId definition,Newtonsoft.Json.Linq.JToken descriptor)
    {
        var provider=session.Registry.Get<PNWorldDefinition>().Get(definition).Create();
        try{if(!(provider is IPNPortableWorldProvider portable))throw new NotSupportedException("Selected provider is not portable.");portable.ValidateLoadDescriptor(descriptor);}
        finally{if(provider is IDisposable disposable)try{disposable.Dispose();}catch{session.RegisterCleanup(disposable.Dispose);throw;}}
    }
    public EveWorldContext CreateStaged(EveId definition,Guid instance,ulong incarnation,Newtonsoft.Json.Linq.JToken descriptor)=>Create(definition,instance,incarnation,0,true,descriptor);
    public Newtonsoft.Json.Linq.JToken CaptureLoadDescriptor(EveWorldContext world)
    {
        var build=Get(world);var entry=entries[world.InstanceId];
        if(!(entry.Provider is IPNPortableWorldProvider portable))throw new NotSupportedException("World provider has no portable load descriptor: "+world.DefinitionId);
        var descriptor=portable.CaptureLoadDescriptor(build)??throw new InvalidOperationException("Portable provider omitted its descriptor.");
        portable.ValidateLoadDescriptor(descriptor);return descriptor.DeepClone();
    }
    private EveWorldContext Create(EveId definition,Guid instance,ulong incarnation,ulong subscription,bool deferReady=false,Newtonsoft.Json.Linq.JToken descriptor=null,PNWorldFeatures creator=null,IEnumerable<EveId> supplied=null,bool makeDefault=false)
    {
        if(instance!=Guid.Empty&&entries.ContainsKey(instance))throw new InvalidOperationException("World runtime still owns that logical identity or its pending callbacks.");
        var selected=session.Registry.Get<PNWorldDefinition>().Get(definition);
        var id=instance==Guid.Empty?Guid.NewGuid():instance;
        if(creator==null&&creatorFeatures.TryGetValue(id,out var kept))creator=kept;
        var features=Compose(selected,creator,supplied);
        var world=subscription==0?session.Worlds.Create(id,incarnation,selected.Id,selected.ContentHash):session.Worlds.CreateReplica(instance,incarnation,selected.Id,selected.ContentHash,subscription);
        RememberCreator(world.InstanceId,creator);
        var entry=new Entry{Build=new PNWorldBuildContext(world,default,features),Started=Time.realtimeSinceStartupAsDouble,DeferReady=deferReady,MakeDefault=makeDefault};entries.Add(world.InstanceId,entry);
        try
        {
            var owner=CreateOwner(world);
            world.RegisterCleanup(()=>UnloadScene(owner));
            world.ConfigurePhysics(owner,Application.isPlaying);
            world.BeginLoading();entry.Build=new PNWorldBuildContext(world,owner,features);if(descriptor!=null)entry.Build.SetLoadDescriptor(descriptor);
            entry.Job=scheduler.SchedulePreparation(()=>LoadStep(entry,selected),error=>{world.Fail(error);DisposeLoad(entry);});
        }
        catch(Exception error){world.Fail(error);throw;}
        return world;
    }
    public EveWorldContext CreateReplica(EveId definition,Guid instance,ulong incarnation,ulong subscription,bool defaultWorld)=>CreateReplica(definition,instance,incarnation,subscription,defaultWorld,null);
    // `features` is the host's final set for this world (the wire carries it from step E4); null leaves the replica with no features.
    public EveWorldContext CreateReplica(EveId definition,Guid instance,ulong incarnation,ulong subscription,bool defaultWorld,IEnumerable<EveId> features)
    {
        if(session.Role!=EveSessionRole.Client)throw new InvalidOperationException("Replica worlds belong to clients.");
        if(defaultWorld&&!entryScene.IsValid())
        {
            // G13: no entry scene was adopted (its world is made from a definition), so the host's default world is built like any other replica and marked as the default.
            if(DefaultWorld!=null)throw new InvalidOperationException("The play already has a default world.");
            return Create(definition,instance,incarnation,subscription,false,null,null,features??Array.Empty<EveId>(),true);
        }
        if(defaultWorld)
        {
            var selected=session.Registry.Get<PNWorldDefinition>().Get(entryDefinition);
            if(definition!=selected.Id||!entryScene.IsValid()||!entryScene.isLoaded)throw new InvalidOperationException("Default world does not match the recorded entry content.");
            return Adopt(entryScene,instance,incarnation,subscription,features??Array.Empty<EveId>());
        }
        return Create(definition,instance,incarnation,subscription,false,null,null,features??Array.Empty<EveId>());
    }
    private bool LoadStep(Entry entry,PNWorldDefinition definition)
    {
        var world=entry.Build.World;
        if(entry.Closing||!entry.Build.IsCurrent){DisposeLoad(entry);return true;}
        if(Time.realtimeSinceStartupAsDouble-entry.Started>60)throw new TimeoutException("World load exceeded transition deadline.");
        if(entry.Load==null)
        {
            if(activeLoads>=2)return false;
            entry.Provider=definition.Create();
            if(entry.Provider is IDisposable disposable)world.RegisterCleanup(disposable.Dispose);
            if(entry.Build.LoadDescriptor!=null)
            {if(!(entry.Provider is IPNPortableWorldProvider portable))throw new NotSupportedException("Selected provider cannot restore a load descriptor.");portable.ValidateLoadDescriptor(entry.Build.LoadDescriptor);}
            entry.Load=entry.Provider.Load(entry.Build)??throw new InvalidOperationException("World provider returned no load job.");activeLoads++;
        }
        if(entry.Load.MoveNext())return false;
        DisposeLoad(entry);Validate(entry.Build);PNWorldNavigation.Prepare(entry.Build);world.CompleteLoading();if(entry.MakeDefault)DefaultWorld=world;if(!entry.DeferReady)PublishStaged(world);return true;
    }
    public void PublishStaged(EveWorldContext world)
    {
        var entry=entries[world.InstanceId];if(!ReferenceEquals(entry.Build.World,world)||world.State!=EveWorldState.Ready&&world.State!=EveWorldState.Paused)throw new InvalidOperationException("Resident staged world required.");
        if(!entry.ProviderReady){(entry.Provider as IPNWorldLifecycleParticipant)?.WorldReady(entry.Build);entry.ProviderReady=true;}
        if(entry.Announced)return;entry.Announced=true;Ready?.Invoke(world);WorldReady?.Invoke(world);
    }
    private void DisposeLoad(Entry entry)
    {
        if(entry.Load==null)return;
        (entry.Load as IDisposable)?.Dispose();entry.Load=null;activeLoads--;
    }
    public static void Validate(PNWorldBuildContext build)
    {
        if(build.Slots.Count==0)throw new InvalidOperationException("World provider supplied no slots.");
        foreach(var slot in build.Slots)
        {
            var roots=slot.Roots.Where(x=>x!=null).ToArray();
            var loaders=roots.SelectMany(x=>x.GetComponentsInChildren<EveEntityLoader>(true)).Where(x=>x.GetComponent<PNFactoryEntity>()==null).ToArray();
            EveSceneEntityKey.ValidateSet(loaders.Select(x=>x.GetComponent<EveSceneEntityKey>()));
            if(loaders.Any(x=>x.gameObject.activeInHierarchy))throw new InvalidOperationException("Authored actors must remain inactive until publication.");
            var spawns=roots.SelectMany(x=>x.GetComponentsInChildren<PNPlayerSpawnPoint>(true)).ToArray();
            foreach(var spawn in spawns)spawn.Validate();
            if(spawns.Select(x=>x.SpawnEntry).Distinct().Count()!=spawns.Length)throw new InvalidOperationException("Duplicate spawn entry in world content slot.");
            PNWorldObject.ValidateSet(roots.SelectMany(x=>x.GetComponentsInChildren<PNWorldObject>(true)));
            PNRoomSpawnGroup.ValidateSet(roots.SelectMany(x=>x.GetComponentsInChildren<PNRoomSpawnGroup>(true)));
        }
    }
    public bool Unload(EveWorldContext world)
    {
        if(world.IsSimulating)return false;
        if(world.State==EveWorldState.Disposed){FinishRemoval(world);return true;}
        if(!entries.TryGetValue(world.InstanceId,out var entry)){session.Worlds.Remove(world);return true;}if(!ReferenceEquals(world,entry.Build.World))throw new InvalidOperationException("Stale world unload.");
        if(!entry.ProviderUnloading){(entry.Provider as IPNWorldLifecycleParticipant)?.WorldUnloading(entry.Build);entry.ProviderUnloading=true;}
        if(!entry.Closing){if(ReferenceEquals(PNLocalWorldContext.Current?.World,world))PNLocalWorldContext.Current.Select(null);world.BeginUnloading();BeforeUnload?.Invoke(world);WorldUnloading?.Invoke(world);entry.Closing=true;}
        entry.Job?.Dispose();DisposeLoad(entry);
        if(!scheduler.RetireWorld(world)||world.LeaseCount!=0)return false;
        try{session.Worlds.Remove(world);}catch(EveWorldCleanupPendingException){return false;}
        FinishRemoval(world);return true;
    }
    private void FinishRemoval(EveWorldContext world)
    {
        if(entries.TryGetValue(world.InstanceId,out var entry)&&!entry.ProviderDisposed){(entry.Provider as IPNWorldLifecycleParticipant)?.WorldDisposed(entry.Build);entry.ProviderDisposed=true;}
        entries.Remove(world.InstanceId);if(ReferenceEquals(DefaultWorld,world))DefaultWorld=null;
    }
    private void Dispose()
    {
        var errors=new List<Exception>();
        foreach(var entry in entries.Values.ToArray())try{if(!Unload(entry.Build.World))throw new EveWorldCleanupPendingException("World cleanup still has members/leases.");}catch(Exception error){errors.Add(error);}
        if(errors.Count>0)throw new AggregateException("World runtime cleanup pending.",errors);
        services.Remove(session);
    }
    private static readonly Dictionary<Scene,AsyncOperation> unloading=new Dictionary<Scene,AsyncOperation>();
    private static Scene CreateOwner(EveWorldContext world)
    {
#if UNITY_EDITOR
        if(!Application.isPlaying)return UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
#endif
        return SceneManager.CreateScene("PN World "+world.InstanceId,new CreateSceneParameters(LocalPhysicsMode.Physics3D));
    }
    internal static void UnloadScene(Scene scene)
    {
        if(!scene.IsValid()||!scene.isLoaded){unloading.Remove(scene);return;}
#if UNITY_EDITOR
        if(!Application.isPlaying){if(UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(scene))UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);else UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);return;}
#endif
        if(!unloading.TryGetValue(scene,out var operation)){operation=SceneManager.UnloadSceneAsync(scene);unloading.Add(scene,operation);}
        if(operation!=null&&!operation.isDone)throw new EveWorldCleanupPendingException("World scene unload is pending.");
        unloading.Remove(scene);
    }
}

// Built-in content provider: paths are selected content, never supplied by peers.
public sealed class PNSceneWorldProvider : IPNWorldProvider
{
    private int ownedAcquisitions;
    public bool CleanupPending=>ownedAcquisitions!=0;
    private readonly string[] paths;
    private static readonly HashSet<string> acquiring=new HashSet<string>();
    public PNSceneWorldProvider(params string[] paths){this.paths=(string[])paths.Clone();}
    public IEnumerator Load(PNWorldBuildContext context)
    {
        for(int i=0;i<paths.Length;i++)
        {
            string path=paths[i];
            while(!acquiring.Add(path))yield return null;
            Scene loaded=default;AsyncOperation operation=null;
            void Loaded(Scene scene,LoadSceneMode mode)
            {if(scene.path==path){loaded=scene;PNWorldPresentation.StageScene(context.World.Session,context.World,scene);}}
            bool released=false;
            void Release(){if(released)return;acquiring.Remove(path);released=true;}
            var known=new HashSet<Scene>(Enumerable.Range(0,SceneManager.sceneCount).Select(n=>SceneManager.GetSceneAt(n)));
            bool cleaned=false;ownedAcquisitions++;
            context.Own(()=>{
                if(cleaned)return;
                if(operation!=null&&!operation.isDone)throw new EveWorldCleanupPendingException("Owned scene acquisition is still pending.");
                SceneManager.sceneLoaded-=Loaded;
                if(!loaded.IsValid())loaded=Enumerable.Range(0,SceneManager.sceneCount).Select(n=>SceneManager.GetSceneAt(n)).SingleOrDefault(s=>s.path==path&&!known.Contains(s));
                Release();if(loaded.IsValid())PNWorldRuntime.UnloadScene(loaded);cleaned=true;ownedAcquisitions--;
            });
            SceneManager.sceneLoaded+=Loaded;
            operation=SceneManager.LoadSceneAsync(path,new LoadSceneParameters(LoadSceneMode.Additive,LocalPhysicsMode.Physics3D));
            if(operation==null)throw new InvalidOperationException("Built-in world scene is unavailable.");
            while(!operation.isDone)yield return null;
            SceneManager.sceneLoaded-=Loaded;
            loaded=Enumerable.Range(0,SceneManager.sceneCount).Select(n=>SceneManager.GetSceneAt(n)).Single(s=>s.path==path&&!known.Contains(s));
            Release();context.AddSlot("chunk-"+i,loaded,loaded.GetRootGameObjects());yield return null;
        }
    }
}
// A useful empty generated world; mods supply their own selected provider for content.
public sealed class PNGeneratedWorldProvider : IPNWorldProvider
{
    public IEnumerator Load(PNWorldBuildContext context)
    { context.AddSlot("main",context.PhysicsScene,Array.Empty<GameObject>());yield break; }
}
#endif
