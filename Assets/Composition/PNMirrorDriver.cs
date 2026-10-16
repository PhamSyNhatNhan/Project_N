using System;
using EveEngine;
using Mirror;
using kcp2k;
using ProjectN.Coop;
using UnityEngine;
namespace ProjectN.Composition
{
    public sealed class PNMirrorDriver : IPNNetworkSessionDriver
    {
        private PNNetworkManager manager;
        public bool IsRunning=>manager!=null&&!manager.HasEnded;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()=>PNGameBootstrap.RegisterNetworkDriver(new PNMirrorDriver());
        public void Begin(EveGameRuntime runtime,PNPlayConfig config)
        {
            if(manager!=null)throw new InvalidOperationException("Previous network manager has not been disposed.");
            var profile=config.Network;profile.Validate();
            runtime.BeginSession(config.Role);
            var game=runtime.GetComponent<PNGameSession>();
            var root=new GameObject("Project N Mirror Session");
            root.SetActive(false); // Manager Awake must see its assigned transport/authenticator.
            manager=root.AddComponent<PNNetworkManager>();
            try
            {
                var transport=root.AddComponent<KcpTransport>();transport.Timeout=profile.TransportTimeoutMs;transport.Port=profile.Port;
                var auth=root.AddComponent<PNNetworkAuthenticator>();auth.Manager=manager;
                manager.transport=transport;manager.authenticator=auth;manager.autoCreatePlayer=false;
                manager.onlineScene="";manager.offlineScene="";manager.dontDestroyOnLoad=true;
                manager.maxConnections=profile.MaximumClients+1;manager.networkAddress=config.Address;
                manager.Runtime=runtime;manager.Factory=game.Factory;manager.Scheduler=game.Scheduler;manager.Profile=profile;manager.ModeId=config.ModeId;
                manager.Policy=new PNRoguelitePolicy(profile);
                if(config.ModeId!="roguelite")throw new InvalidOperationException("No policy registered for mode: "+config.ModeId);
                root.SetActive(true);
                if(config.Role==EveSessionRole.Host)manager.StartHost();else manager.StartClient();
            }
            catch {End();throw;}
        }
        public void End()
        {
            if(manager==null)return;
            manager.StopSession();
            if(NetworkManager.singleton==manager)NetworkManager.ResetStatics();
            manager.gameObject.SetActive(false);UnityEngine.Object.Destroy(manager.gameObject);manager=null;
        }
    }
}
