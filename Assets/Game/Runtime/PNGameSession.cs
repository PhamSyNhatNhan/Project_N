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
        service.Local=new PNLocalWorldContext(session);
        service.Presentation=runtime.GetComponent<PNWorldPresentation>()??runtime.gameObject.AddComponent<PNWorldPresentation>();service.Presentation.Bind(session,service.Worlds,service.Local);
        if(!PNCameraDirector.LegacyRequested)service.CameraDirector=PNCameraDirector.Attach(runtime.gameObject,session,service.Worlds,service.Local,service.Presentation);
        var physics=runtime.GetComponent<PNWorldPhysicsLoop>()??runtime.gameObject.AddComponent<PNWorldPhysicsLoop>();physics.Bind(session);
        // One delegate: it must release every world-scoped service, so combine them here instead of assigning separately.
        service.Worlds.BeforeUnload=world=>{runtime.GetComponent<PNAreaManager>()?.ReleaseWorld(world);runtime.GetComponent<PNProjectileManager>()?.ReleaseWorld(world);};
        service.Factory=new PNPlayerFactory(session,service.Worlds);
        session.RegisterCleanup(service.Factory.Dispose,afterEntities:true);
        service.Transfers=new PNWorldTransitionCoordinator(session,service.Worlds,(PNPlayerFactory)service.Factory,service.Scheduler);
        physics.BeforeSimulation+=service.Transfers.FixedBoundary;
        service.Lifetimes=new PNWorldLifetimeController(session,service.Worlds,(PNPlayerFactory)service.Factory,service.Scheduler);
        service.Saves=new PNWorldSaveService(session,service.Worlds,service.Lifetimes,System.IO.Path.Combine(Application.persistentDataPath,"WorldSaves"));
        if(!PNCameraDirector.LegacyRequested)service.Transitions=PNTransitionController.Attach(runtime.gameObject,session,service.Local,service.Transfers,service.CameraDirector);
        var scenes=UnityEngine.Object.FindObjectsByType<PNPlayerSpawnPoint>().Select(x=>x.gameObject.scene).Distinct().ToArray();
        if(scenes.Length==1){var entryMarker=PNEntrySceneMarker.Find(scenes[0]);service.Worlds.SetEntryScene(scenes[0],entryMarker!=null?EveId.Parse(entryMarker.WorldDefinition):default);if(session.Role!=EveSessionRole.Client)service.Presentation.RegisterWorld(service.Worlds.Adopt(scenes[0]));}
        else if(scenes.Length>1)throw new InvalidOperationException("Scene entry requires explicit world selection for multiple scenes.");
        if(session.Role==EveSessionRole.Offline && UnityEngine.Object.FindObjectsByType<PNPlayerSpawnPoint>().Length>0)
        {
            var requests=service.Factory.SceneEntities();
            if(PNGameBootstrap.Config.Network.SelectPlayer)
            {
                // Alone, the character is the one picked in the menu (the profile's last), when it is registered, playable and allowed.
                var player=service.Factory.Player(0);var picked=PickedCharacter(session);
                if(picked!=null)player.Character=picked;
                requests=requests.Concat(new[]{player});
            }
            foreach(var request in requests)
                {
                    var candidate=service.Scheduler.Enqueue(()=>service.Factory.Get(request),loader=>service.Factory.Configure(loader,request),service.Factory.Return,service.Factory.Published,error=>{Debug.LogException(error,service);if(session.IsActive)runtime.EndSession();});
                    service.Scheduler.AssociateWorld(candidate,request.World);
                }
        }
        return service;
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
        if(Session.IsActive){Saves.Tick();Lifetimes.Tick();}
        if(Session.IsActive || EveRegistrySession.State==EveSessionState.Stopping)Scheduler.AdvanceFrame();
    }
    public void RetryCleanup()
    { Scheduler.RetryQuarantines();if(EveRegistrySession.State==EveSessionState.Stopping)EveRegistrySession.RetryCleanup(); }
}
#endif
