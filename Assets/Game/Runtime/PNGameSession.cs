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
    public static PNGameSession Attach(EveRuntime runtime,EveSessionContext session)
    {
        var service=runtime.GetComponent<PNGameSession>()??runtime.gameObject.AddComponent<PNGameSession>();
        if(ReferenceEquals(service.Session,session))return service;
        service.Session=session;service.Factory=new PNPlayerFactory(session);
        session.RegisterCleanup(service.Factory.Dispose,afterEntities:true);
        service.Scheduler=new EveEntityPreparationScheduler(session,PNGameBootstrap.Config.Preparation);
        if(session.Role==EveSessionRole.Offline && UnityEngine.Object.FindObjectsByType<PNPlayerSpawnPoint>().Length>0)
        {
            var requests=service.Factory.SceneEntities();
            if(PNGameBootstrap.Config.Network.SelectPlayer)requests=requests.Concat(new[]{service.Factory.Player(0)});
            foreach(var request in requests)
                service.Scheduler.Enqueue(()=>service.Factory.Get(request),loader=>service.Factory.Configure(loader,request),service.Factory.Return,service.Factory.Published,error=>{Debug.LogException(error,service);if(session.IsActive)runtime.EndSession();});
        }
        return service;
    }
    private void Update()
    {
        if(Session==null || Scheduler==null)return;
        if(Session.IsActive || EveRegistrySession.State==EveSessionState.Stopping)Scheduler.AdvanceFrame();
    }
    public void RetryCleanup()
    { Scheduler.RetryQuarantines();if(EveRegistrySession.State==EveSessionState.Stopping)EveRegistrySession.RetryCleanup(); }
}
#endif
