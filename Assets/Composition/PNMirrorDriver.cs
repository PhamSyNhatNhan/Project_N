using System;
using EveEngine;
using Mirror;
using kcp2k;
using ProjectN.Coop;
using UnityEngine;
namespace ProjectN.Composition
{
    public sealed class PNMirrorDriver : IPNNetworkSessionDriver, IPNRoomSessionDriver
    {
        private PNNetworkManager manager;
        private bool releaseScheduled;
        public bool IsRunning=>IsRoomOpen;
        public bool IsRoomOpen=>manager!=null&&!manager.HasEnded;
        public bool IsPlaying=>manager!=null&&manager.IsPlaying;
        public bool CanBeginPlay=>manager!=null&&manager.CanBeginPlay;
        public string ModeId=>manager?.ModeId;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()=>PNGameBootstrap.RegisterNetworkDriver(new PNMirrorDriver());
        public void Begin(EveGameRuntime runtime,PNPlayConfig config)
        {
            OpenRoom(runtime,config);
            if(config.Role==EveSessionRole.Host)BeginPlay(config.ModeId);
        }
        public void OpenRoom(EveGameRuntime runtime,PNPlayConfig config)
        {
            if(manager!=null||runtime.IsReady)throw new InvalidOperationException("Close the previous room/play first.");
            var profile=config.Network.Snapshot();runtime.EnsureContentLoaded();
            var root=new GameObject("Project N Mirror Room");root.SetActive(false);
            manager=root.AddComponent<PNNetworkManager>();
            try
            {
                var transport=root.AddComponent<KcpTransport>();transport.Timeout=profile.TransportTimeoutMs;transport.Port=profile.Port;
                var auth=root.AddComponent<PNNetworkAuthenticator>();auth.Manager=manager;
                manager.transport=transport;manager.authenticator=auth;manager.autoCreatePlayer=false;
                manager.onlineScene="";manager.offlineScene="";manager.dontDestroyOnLoad=true;
                manager.maxConnections=profile.MaximumClients+1;manager.networkAddress=config.Address;
                manager.Runtime=runtime;manager.Profile=profile;manager.ModeId=config.ModeId;
                manager.StartPlayRuntime=role=>
                {
                    runtime.BeginSession(role);
                    var game=runtime.GetComponent<PNGameSession>();manager.Factory=game.Factory;manager.Scheduler=game.Scheduler;manager.Worlds=game.Worlds;
                    manager.Transfers=game.Transfers;
                    game.Lifetimes.RestoredActor+=manager.PublishRestoredActor;
                };
                root.SetActive(true);
                if(config.Role==EveSessionRole.Host)manager.StartHost();else if(config.Role==EveSessionRole.Client)manager.StartClient();
                else throw new ArgumentException("Offline does not open a network room.");
            }
            catch{End();throw;}
        }
        public void BeginPlay(string modeId){if(manager==null)throw new InvalidOperationException("No room.");manager.BeginPlay(modeId);}
        public void EndPlay(){if(manager!=null)manager.EndRoomPlay();}
        public void CloseRoom()=>End();
        public void End()
        {
            if(manager==null)return;
            if(releaseScheduled)return;
            var closing=manager;closing.StopSession();
            if(closing.IsTeardownComplete)Release(closing);
            else{releaseScheduled=true;closing.TeardownCompleted+=()=>Release(closing);}
        }
        private void Release(PNNetworkManager closing)
        {
            if(manager!=closing)return;
            if(NetworkManager.singleton==closing)NetworkManager.ResetStatics();
            closing.gameObject.SetActive(false);UnityEngine.Object.Destroy(closing.gameObject);manager=null;releaseScheduled=false;
        }
    }
}
