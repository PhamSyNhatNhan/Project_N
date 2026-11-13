#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;
using EveEngine;
using UnityEngine;
[DefaultExecutionOrder(-9500)]
public sealed class PNGameSession : MonoBehaviour
{
    public EveEntityPreparationScheduler Scheduler {get;private set;}
    public IPNEntityFactory Factory {get;private set;}
    public EveSessionContext Session {get;private set;}
    public PNWorldRuntime Worlds {get;private set;}
    public PNSyncService Sync {get;private set;}
    public PNRunDirector Runs {get;private set;}
    public PNRunStateSync RunView {get;private set;}
    public PNRogueStateSync RogueView {get;private set;}
    public PNWorldTransitionCoordinator Transfers {get;private set;}
    public PNLocalWorldContext Local {get;private set;}
    public PNWorldLifetimeController Lifetimes {get;private set;}
    public PNWorldSaveService Saves {get;private set;}
    public PNWorldBundleService WorldAssets {get;private set;}
    public PNWorldPresentation Presentation {get;private set;}
    public PNCameraDirector CameraDirector {get;private set;}
    public PNTransitionController Transitions {get;private set;}
    private PNAgentGroupService agentGroups;
    public PNAgentGroupService AgentGroups
    {
        get
        {
            if (Session?.IsActive != true || !Session.HasAuthority) throw new InvalidOperationException("Groups require an active authoritative play.");
            return agentGroups ??= new PNAgentGroupService(Session);
        }
    }
    private void LateUpdate() { agentGroups?.Tick(); }
    public static PNGameSession Attach(EveRuntime runtime,EveSessionContext session)
    {
        var service=runtime.GetComponent<PNGameSession>()??runtime.gameObject.AddComponent<PNGameSession>();
        if(ReferenceEquals(service.Session,session))return service;
        service.agentGroups?.Dispose(); service.agentGroups = null;
        service.Session=session;
        service.WorldAssets=(runtime as EveGameRuntime)?.WorldAssets;service.WorldAssets?.BindSession(session);
        service.Scheduler=new EveEntityPreparationScheduler(session,PNGameBootstrap.Config.Preparation);
        service.Worlds=new PNWorldRuntime(session,service.Scheduler);
        // A client is given each world's features by the host; the host and an offline player work them out, the mode's first.
        if(session.Role!=EveSessionRole.Client)service.Worlds.ModeFeatureSource=()=>PNModeDefinition.Select(session.Registry,PNGameBootstrap.ModeId,PNGameBootstrap.Config.Network).Features;
        // Before any world can become ready (the entry scene is adopted at the end of this method): the objects of every world are registered as they appear.
        service.Sync=new PNSyncService(session.Registry,session.HasAuthority,0,session);
        var syncWorlds=new PNSyncWorldBinder(service.Worlds,service.Sync);
        session.RegisterCleanup(()=>{syncWorlds.Dispose();service.Sync.Dispose();});
        service.Local=new PNLocalWorldContext(session);
        service.Presentation=runtime.GetComponent<PNWorldPresentation>()??runtime.gameObject.AddComponent<PNWorldPresentation>();service.Presentation.Bind(session,service.Worlds,service.Local);
        if(!PNCameraDirector.LegacyRequested)service.CameraDirector=PNCameraDirector.Attach(runtime.gameObject,session,service.Worlds,service.Local,service.Presentation);
        var physics=runtime.GetComponent<PNWorldPhysicsLoop>()??runtime.gameObject.AddComponent<PNWorldPhysicsLoop>();physics.Bind(session);
        // One delegate: it must release every world-scoped service, so combine them here instead of assigning separately.
        var areas=runtime.GetComponent<PNAreaManager>();var projectiles=runtime.GetComponent<PNProjectileManager>();
        service.Worlds.BeforeUnload=world=>{if(areas!=null)areas.ReleaseWorld(world);if(projectiles!=null)projectiles.ReleaseWorld(world);};
        service.Factory=new PNEntityFactory(session,service.Worlds){Scheduler=service.Scheduler};
        session.RegisterCleanup(service.Factory.Dispose,afterEntities:true);
        PNEntityHosts.Set(session,new PNLocalEntityHost(session,service.Factory,service.Scheduler));session.RegisterCleanup(()=>{PNEntityHosts.Remove(session);PNSummons.Reset();});
        service.Transfers=new PNWorldTransitionCoordinator(session,service.Worlds,(PNEntityFactory)service.Factory,service.Scheduler);
        physics.BeforeSimulation+=service.Transfers.FixedBoundary;
        service.Runs=new PNRunDirector(service);
        service.RunView=new PNRunStateSync(service.Sync,service.Runs);session.RegisterCleanup(service.RunView.Dispose);
        service.RogueView=new PNRogueStateSync(service.Sync,service.Runs);session.RegisterCleanup(service.RogueView.Dispose);
        service.Lifetimes=new PNWorldLifetimeController(session,service.Worlds,(PNEntityFactory)service.Factory,service.Scheduler);
        service.Saves=new PNWorldSaveService(session,service.Worlds,service.Lifetimes,System.IO.Path.Combine(Application.persistentDataPath,"WorldSaves"));
        if(!PNCameraDirector.LegacyRequested)service.Transitions=PNTransitionController.Attach(runtime.gameObject,session,service.Local,service.Transfers,service.CameraDirector);
        // A scene authored with its own player spawn point was adopted as a world until G14; every world is now made from a definition (a shell scene only holds the runtime, the marker and the camera).
        if(UnityEngine.Object.FindObjectsByType<PNPlayerSpawnPoint>().Length>0)throw new InvalidOperationException("The entry scene holds a player spawn point: scenes are no longer adopted as worlds. Make the scene a shell with a PNEntrySceneMarker and move the spawn into a grid map.");
        StartEntryWorld(service,session,runtime);
        return service;
    }
    // G13: an entry scene that holds no player spawn point is a shell (EveRuntime, marker, camera): the first world of the play is made from the definition the marker names, becomes the default world when it
    // is ready, and the entities of the world and (offline) the player are made then, at the world's own `player` entry. A host's players are enrolled by the room manager from the same ready event; a client
    // is offered the host's default world. A scene with a spawn point is refused (G14 removed the adoption of scenes as worlds).
    private static void StartEntryWorld(PNGameSession service,EveSessionContext session,EveRuntime runtime)
    {
        var marker=UnityEngine.Object.FindObjectsByType<PNEntrySceneMarker>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault();
        if(marker==null||string.IsNullOrWhiteSpace(marker.WorldDefinition)){Debug.LogError("The entry scene has no PNEntrySceneMarker naming its world definition: no world is made.");return;}
        var definition=EveId.Parse(marker.WorldDefinition);
        // The shell scene holds the camera (and the rest of the presentation) of the entry world; no world owns that scene, so it is handed to the default world when it is ready, as an adopted scene would be.
        var shell=marker.gameObject.scene;
        Action<EveWorldContext> stage=null;
        stage=world=>
        {
            if(!ReferenceEquals(world,service.Worlds.DefaultWorld))return;
            service.Worlds.WorldReady-=stage;
            PNWorldPresentation.StageScene(session,world,shell);
        };
        service.Worlds.WorldReady+=stage;session.RegisterCleanup(()=>service.Worlds.WorldReady-=stage);
        if(session.Role==EveSessionRole.Offline)
        {
            Action<EveWorldContext> ready=null;
            ready=world=>
            {
                if(!ReferenceEquals(world,service.Worlds.DefaultWorld))return;
                service.Worlds.WorldReady-=ready;
                if(!(service.Factory is IPNWorldEntityFactory worldFactory))return;
                var requests=worldFactory.SceneEntities(world).ToList();
                if(PNGameBootstrap.Config.Network.SelectPlayer)
                {
                    var player=worldFactory.Player(0,world,null,null);var picked=PickedCharacter(session);
                    if(picked!=null)player.Character=picked;
                    requests.Add(player);
                }
                foreach(var request in requests)
                    PNEntitySpawner.Enqueue(service.Scheduler,service.Factory,request,failed:error=>{Debug.LogException(error,service);if(session.IsActive)runtime.EndSession();});
            };
            service.Worlds.WorldReady+=ready;session.RegisterCleanup(()=>service.Worlds.WorldReady-=ready);
        }
        if(session.Role!=EveSessionRole.Client)service.Worlds.CreateDefault(definition);   // a client is offered the host's default world; presentation registers it when it is ready (WorldReady)
    }
    private static string PickedCharacter(EveSessionContext session)
    {
        string id=PNPlayerProfileService.Current.LastCharacter;
        if(string.IsNullOrEmpty(id)||!EveId.TryParse(id,out var parsed))return null;
        try{var playable=PNPlayableSettings.Parse(session.Registry.Characters.Get(parsed).Data);return playable!=null&&PNPlayerProfileService.CanPlay(id,playable)?id:null;}
        catch(Exception e) when(e is System.Collections.Generic.KeyNotFoundException||e is FormatException){return null;}
    }
    private void Update()
    {
        if(Session==null || Scheduler==null)return;
        if(Session.IsActive){Saves.Tick();Lifetimes.Tick();Runs?.Tick(Time.deltaTime);FlushSync();}
        if(Session.IsActive || EveRegistrySession.State==EveSessionState.Stopping)Scheduler.AdvanceFrame();
    }
    private void FlushSync()
    {
        try{Sync?.Flush();}
        catch(Exception error){Debug.LogException(error,this);}
    }
    public void RetryCleanup()
    { Scheduler.RetryQuarantines();if(EveRegistrySession.State==EveSessionState.Stopping)EveRegistrySession.RetryCleanup(); }
}
#endif
