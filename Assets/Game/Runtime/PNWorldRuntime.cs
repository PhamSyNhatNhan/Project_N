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
        public bool DeferReady,Announced;
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
    public EveWorldContext Adopt(Scene scene,Guid instance=default,ulong incarnation=1) => Adopt(scene,instance,incarnation,0);
    private EveWorldContext Adopt(Scene scene,Guid instance,ulong incarnation,ulong subscription)
    {
        var definition=session.Registry.Get<PNWorldDefinition>().Get(entryDefinition);
        var world=subscription==0?session.Worlds.Create(instance==Guid.Empty?Guid.NewGuid():instance,incarnation,definition.Id,definition.ContentHash):session.Worlds.CreateReplica(instance,incarnation,definition.Id,definition.ContentHash,subscription);
        var owner=CreateOwner(world);world.RegisterCleanup(()=>UnloadScene(owner));
        world.ConfigurePhysics(owner,Application.isPlaying);world.BeginLoading();
        var build=new PNWorldBuildContext(world,owner);
        entries.Add(world.InstanceId,new Entry{Build=build});
        var roots=scene.GetRootGameObjects().Where(x=>x.GetComponent<EveRuntime>()==null&&x.GetComponent<PNRoomResource>()==null&&!x.GetComponentsInChildren<PNFactoryEntity>(true).Any()).ToArray();
        world.RegisterCleanup(()=>{foreach(var root in roots)if(root!=null&&root.scene==owner&&scene.IsValid()&&scene.isLoaded)SceneManager.MoveGameObjectToScene(root,scene);});
        build.AddSlot("main",scene,roots,false);
        world.CompleteLoading();DefaultWorld=world;PublishStaged(world);return world;
    }
    public EveWorldContext Create(EveId definition,Guid instance=default,ulong incarnation=1)=>Create(definition,instance,incarnation,0);
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
    private EveWorldContext Create(EveId definition,Guid instance,ulong incarnation,ulong subscription,bool deferReady=false,Newtonsoft.Json.Linq.JToken descriptor=null)
    {
        if(instance!=Guid.Empty&&entries.ContainsKey(instance))throw new InvalidOperationException("World runtime still owns that logical identity or its pending callbacks.");
        var selected=session.Registry.Get<PNWorldDefinition>().Get(definition);
        var world=subscription==0?session.Worlds.Create(instance==Guid.Empty?Guid.NewGuid():instance,incarnation,selected.Id,selected.ContentHash):session.Worlds.CreateReplica(instance,incarnation,selected.Id,selected.ContentHash,subscription);
        var entry=new Entry{Build=new PNWorldBuildContext(world,default),Started=Time.realtimeSinceStartupAsDouble,DeferReady=deferReady};entries.Add(world.InstanceId,entry);
        try
        {
            var owner=CreateOwner(world);
            world.RegisterCleanup(()=>UnloadScene(owner));
            world.ConfigurePhysics(owner,Application.isPlaying);
            world.BeginLoading();entry.Build=new PNWorldBuildContext(world,owner);if(descriptor!=null)entry.Build.SetLoadDescriptor(descriptor);
            entry.Job=scheduler.SchedulePreparation(()=>LoadStep(entry,selected),error=>{world.Fail(error);DisposeLoad(entry);});
        }
        catch(Exception error){world.Fail(error);throw;}
        return world;
    }
    public EveWorldContext CreateReplica(EveId definition,Guid instance,ulong incarnation,ulong subscription,bool defaultWorld)
    {
        if(session.Role!=EveSessionRole.Client)throw new InvalidOperationException("Replica worlds belong to clients.");
        if(defaultWorld)
        {
            var selected=session.Registry.Get<PNWorldDefinition>().Get(entryDefinition);
            if(definition!=selected.Id||!entryScene.IsValid()||!entryScene.isLoaded)throw new InvalidOperationException("Default world does not match the recorded entry content.");
            return Adopt(entryScene,instance,incarnation,subscription);
        }
        return Create(definition,instance,incarnation,subscription);
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
        DisposeLoad(entry);Validate(entry.Build);world.CompleteLoading();if(!entry.DeferReady)PublishStaged(world);return true;
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
