using System;
using System.Collections.Generic;
using EveEngine;
using Mirror;
using kcp2k;
using ProjectN.Coop;
using UnityEngine;
namespace ProjectN.Composition
{
    public sealed class PNMirrorDriver : IPNNetworkSessionDriver, IPNRoomSessionDriver, IPNLobbyDriver
    {
        private PNNetworkManager manager;
        private bool releaseScheduled;
        private PNLanDiscovery discovery;
        private string lastFailure;
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
            lastFailure=null;
            var profile=config.Network.Snapshot();runtime.EnsureContentLoaded();
            var root=new GameObject("Project N Mirror Room");root.SetActive(false);
            manager=root.AddComponent<PNNetworkManager>();
            try
            {
                var transport=root.AddComponent<KcpTransport>();transport.Timeout=profile.TransportTimeoutMs;transport.Port=profile.Port;
                var auth=root.AddComponent<PNNetworkAuthenticator>();auth.Manager=manager;
                Transport active=transport;
                // Test aid: --pn-sim-latency=<ms>[,<loss%>] wraps KCP in Mirror's latency simulation (off by default).
                if(TryReadSimulation(out var latency,out var loss))
                {
                    var simulation=root.AddComponent<LatencySimulation>();simulation.wrap=transport;simulation.latency=latency;simulation.jitter=.05f;simulation.unreliableLoss=loss;simulation.unreliableScramble=loss/2f;
                    active=simulation;Debug.LogWarning("Network latency simulation on: "+latency+" ms, "+loss+"% unreliable loss.");
                }
                manager.transport=active;manager.authenticator=auth;manager.autoCreatePlayer=false;
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
                if(config.Role==EveSessionRole.Host)Discovery().BeginAdvertising(HostInfo);
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
            lastFailure=manager.LastFailure??lastFailure;discovery?.EndAdvertising();
            var closing=manager;closing.StopSession();
            if(closing.IsTeardownComplete)Release(closing);
            else{releaseScheduled=true;closing.TeardownCompleted+=()=>Release(closing);}
        }
        // IPNLobbyDriver: lobby state, admission lock and LAN discovery for the roguelite flow.
        public bool IsIdle=>manager==null&&!releaseScheduled;
        public string LastFailure=>manager?.LastFailure??lastFailure;
        public PNLobbyInfo Lobby=>manager?.GetLobbyInfo();
        public void SetReady(bool ready)=>manager?.SetLobbyReady(ready);
        public void SetCharacter(string characterId)=>manager?.SetLobbyCharacter(characterId);
        public void LockAdmissions(bool locked)
        {
            if(manager==null)throw new InvalidOperationException("No room.");
            manager.SetAdmissionClosed(locked);
            if(locked)discovery?.EndAdvertising();
            else if(manager.IsPlaying&&NetworkServer.active)Discovery().BeginAdvertising(HostInfo);
        }
        public void MarkRunStarted()
        {
            if(manager==null)throw new InvalidOperationException("No room.");
            manager.MarkRunStarted();discovery?.EndAdvertising();
        }
        public void StartDiscovery()=>Discovery().BeginSearching();
        public void StopDiscovery()=>discovery?.EndSearching();
        public IReadOnlyList<PNLanRoomInfo> Rooms=>discovery!=null?discovery.Rooms():Array.Empty<PNLanRoomInfo>();
        public IReadOnlyList<string> LocalAddresses=>PNLanDiscovery.LocalAddresses();
        public int Port=>manager!=null?manager.Profile.Port:PNGameBootstrap.Config.Network.Port;
        private static bool TryReadSimulation(out float latency,out float loss)
        {
            latency=0;loss=0;
            foreach(var argument in Environment.GetCommandLineArgs())
            {
                if(!argument.StartsWith("--pn-sim-latency=",StringComparison.Ordinal))continue;
                var parts=argument.Substring("--pn-sim-latency=".Length).Split(',');
                if(!float.TryParse(parts[0],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out latency)||latency<0||latency>2000)return false;
                if(parts.Length>1&&!float.TryParse(parts[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out loss))loss=0;
                loss=Mathf.Clamp(loss,0f,20f);return true;
            }
            return false;
        }
        private PNLanDiscovery Discovery()
        {
            if(discovery!=null)return discovery;
            var root=new GameObject("Project N LAN discovery");UnityEngine.Object.DontDestroyOnLoad(root);
            discovery=root.AddComponent<PNLanDiscovery>();discovery.LocalInfo=LocalInfo;return discovery;
        }
        private PNLanResponse LocalInfo()
        {
            var runtime=PNGameBootstrap.Runtime;
            return new PNLanResponse{NetworkVersion=PNNetworkAuthenticator.NetworkVersion,Platform=PNNetworkAuthenticator.PlatformGroup,Digest=runtime!=null&&runtime.Content!=null&&runtime.Content.IsReady?PNLanDiscovery.Digest(runtime.Content.Compatibility):null};
        }
        private PNLanResponse HostInfo()
        {
            var info=LocalInfo();
            if(manager==null)return info;
            var lobby=manager.GetLobbyInfo();
            info.Name=SystemInfo.deviceName;info.Mode=manager.ModeId;info.Port=manager.Profile.Port;
            info.Players=lobby!=null?lobby.Peers.Count:1;info.Capacity=manager.Profile.MaximumClients+1;info.Locked=manager.AdmissionClosed;
            return info;
        }
        private void Release(PNNetworkManager closing)
        {
            if(manager!=closing)return;
            if(NetworkManager.singleton==closing)NetworkManager.ResetStatics();
            closing.gameObject.SetActive(false);UnityEngine.Object.Destroy(closing.gameObject);manager=null;releaseScheduled=false;
        }
    }
}
