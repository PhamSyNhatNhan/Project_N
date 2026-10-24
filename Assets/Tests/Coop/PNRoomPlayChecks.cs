using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
namespace ProjectN.Coop.Tests
{
    [DefaultExecutionOrder(-20000)]
    public sealed class PNRoomPlayChecks:MonoBehaviour
    {
        private string folder,name;private int checks;private bool failed;
        private static string Arg(string prefix)=>Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith(prefix,StringComparison.Ordinal))?.Substring(prefix.Length);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void InstallMod()
        {
            var folder=Arg("--pn-room-test=");if(folder==null)return;
            if(Application.companyName!="ProjectNVerification"||!Application.productName.StartsWith("WorldS1Fixture",StringComparison.Ordinal))throw new Exception("Requires isolated fixture product storage.");
            var source=Path.Combine(folder,"NativeMods");var target=Path.Combine(Application.persistentDataPath,"Mods");
            foreach(var file in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){var to=Path.Combine(target,file.Substring(source.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(to));File.Copy(file,to,true);}
            File.WriteAllText(Path.Combine(target,"enabled.json"),"{\"schemaVersion\":1,\"enabledMods\":[\"native_probe\",\"world_probe\"]}");
        }
        private void Awake(){if(Arg("--pn-room-test=")!=null){PNGameBootstrap.Config.Network.Port=7796;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;}}
        private void Mark(string value){var path=Path.Combine(folder,value);File.WriteAllText(path+".pending","ok");File.Move(path+".pending",path);}
        private bool Has(string value)=>File.Exists(Path.Combine(folder,value));
        private void Check(bool ok,string value){if(!ok)throw new Exception(value);checks++;Debug.Log("S1 PASS "+name+" "+value);}
        private void Fail(Exception error){if(failed)return;failed=true;File.WriteAllText(Path.Combine(folder,name+".txt"),"FAIL "+error);Debug.LogException(error);try{PNGameBootstrap.End();}catch{}Application.Quit(1);}
        private IEnumerator Wait(Func<bool> ready)
        {
            double until=Time.realtimeSinceStartupAsDouble+90;
            while(!failed){bool done=false;try{done=ready();if(!done&&Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("checks="+checks+" "+(NetworkManager.singleton as PNNetworkManager)?.LastFailure);}catch(Exception e){Fail(e);}if(done||failed)yield break;yield return null;}
        }
        private int Mod(string field)=> (int)AppDomain.CurrentDomain.GetAssemblies().Select(x=>x.GetType("WorldModeMod")).First(x=>x!=null).GetField(field).GetValue(null);
        private PNPlayerMove[] Players()=>FindObjectsByType<PNPlayerMove>().Where(x=>x.GetComponent<EveEntityLoader>()?.IsReady==true).ToArray();
        private IEnumerator Start()
        {
            folder=Arg("--pn-room-test=");if(folder==null)yield break;
            bool host=PNGameBootstrap.Config.Role==EveSessionRole.Host;name=host?"host":Arg("--pn-room-client=")??"client1";int clients=int.Parse(Arg("--pn-room-clients=")??"2");bool late=Environment.GetCommandLineArgs().Contains("--pn-room-late-join"),latePeer=late&&name=="client"+clients;int lobbyClients=late?clients-1:clients;
            try{PNGameBootstrap.OpenRoom(PNGameBootstrap.Config);}catch(Exception e){Fail(e);yield break;}
            yield return Wait(()=>NetworkClient.isConnected&&NetworkClient.connection.isAuthenticated);if(failed)yield break;
            var manager=(PNNetworkManager)NetworkManager.singleton;var content=PNGameBootstrap.Runtime.Content;var room=manager.RoomId;
            long owner=manager.RoomOwner;var connection=NetworkClient.connection;uint shell=0;
            try{Check(latePeer||!EveRegistrySession.IsReady&&Players().Length==0&&!manager.IsPlaying,latePeer?"late peer authenticated room":"authenticated lobby without play/avatar");Check(Mod("Registered")==1&&(latePeer?Mod("Started")<=1:Mod("Started")==0),"content registration at room admission");if(!host&&!latePeer){manager.NotifyApplicationPause(true);manager.NotifyApplicationPause(false);}Mark(name+"-lobby");}catch(Exception e){Fail(e);yield break;}
            yield return Wait(()=>host?Enumerable.Range(1,lobbyClients).All(i=>Has("client"+i+"-lobby")):Has("host-lobby"));if(failed)yield break;
            if(host){yield return new WaitForSecondsRealtime(16);try{Check(NetworkServer.connections.Values.Count(c=>c.authenticationData is PNPeer p&&p.Owner>0)==lobbyClients,"room heartbeat survives >15 seconds without Context");Mark("lobby-heartbeat");}catch(Exception e){Fail(e);yield break;}}
            yield return Wait(()=>Has("lobby-heartbeat"));if(failed)yield break;
            Guid old=Guid.Empty;ulong oldRevision=0;bool releaseCleanup=false,releaseClientCleanup=false;
            for(int round=1;round<=3;round++)
            {
                string mode=round==2?"world_probe:alternate":"roguelite";
                bool injectClient=!host&&(round==2&&name=="client1"||late&&round==1&&latePeer);
                bool expectClientCleanup=round==2||late&&round==1;
                if(host){yield return Wait(()=>manager.CanBeginPlay);if(failed)yield break;try{PNGameBootstrap.BeginPlay(mode);if(round==1)Mark("host-play-started");}catch(Exception e){Fail(e);yield break;}}
                yield return Wait(()=>manager.IsPlaying&&manager.PlayRevision==(ulong)round&&Players().Length==clients+1&&(host||manager.ClientInputReady));if(failed)yield break;
                try
                {
                    Check(ReferenceEquals(content,PNGameBootstrap.Runtime.Content)&&manager.RoomId==room&&ReferenceEquals(connection,NetworkClient.connection)&&owner==manager.RoomOwner,"content/room/connection/peer retained round "+round);
                    Check(manager.PlaySession!=old&&manager.ModeId==mode,"new play identity and registered mode "+round);
                    Check(Mod("Registered")==1&&Mod("Started")==round&&Mod("Ended")==round-1&&Mod("Disposed")==0,"mod callbacks per play "+round);
                    if(host)
                    {
                        var actor=Players().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId==0);var stat=actor.GetComponent<PNStat>();
                        actor.GetComponent<PNBuffManager>().Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("native_probe:buff_probe"),PNCombatSource.System(EveRegistrySession.Context.SessionId,stat.EntityId)));
                        Check(true,"previous API6 DLL buff executes in new play "+round);
                    }
                    if(NetworkClient.localPlayer!=null){if(shell!=0)Check(shell==NetworkClient.localPlayer.netId,"room control shell retained");shell=NetworkClient.localPlayer.netId;}
                    if(round==1)
                    {
                        var extra=SceneManager.CreateScene("Disposable additive S1");var entry=new GameObject("Additional runtime entry");entry.SetActive(false);SceneManager.MoveGameObjectToScene(entry,extra);entry.AddComponent<EveGameRuntime>();entry.SetActive(true);SceneManager.UnloadSceneAsync(extra);
                        Check(ReferenceEquals(content,PNGameBootstrap.Runtime.Content)&&manager.PlayRevision==1,"additive scene entry does not begin/reload play");
                    }
                    if(round==2)
                    {
                        if(host)foreach(var c in NetworkServer.connections.Values.Where(c=>!(c is LocalConnectionToClient)))c.Send(new PNPlayStatus{Room=room,Incarnation=manager.RoomIncarnation,Revision=oldRevision,Session=old,Active=true,Mode="roguelite"});
                        else
                        {
                            NetworkClient.Send(new PNHeartbeat{Session=old,PlayRevision=oldRevision,Sequence=uint.MaxValue});
                            NetworkClient.Send(new PNResume{Session=old,PlayRevision=oldRevision,Sequence=uint.MaxValue,Acknowledged=true});
                            NetworkClient.localPlayer.GetComponent<PNSessionShell>().CmdIntent(old,1,1,1,"stale","{}");
                        }
                    }
                    old=manager.PlaySession;oldRevision=manager.PlayRevision;
                    Mark(name+"-round"+round);
                }catch(Exception e){Fail(e);yield break;}
                // Actual input and Rigidbody movement on each newly attached owner.
                var own=Players().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId==owner);float start=own.Body.position.x;
                var keyboard=InputSystem.AddDevice<Keyboard>();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D));
                yield return new WaitForSecondsRealtime(.5f);InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.RemoveDevice(keyboard);
                try{Check(own.Body.position.x>start+.1f,"actual owner movement after attach "+round);Mark(name+"-moved"+round);}catch(Exception e){Fail(e);yield break;}
                yield return Wait(()=>Enumerable.Range(1,clients).All(i=>Has("client"+i+"-moved"+round))&&Has("host-moved"+round));if(failed)yield break;
                if(injectClient)
                {
                    try{EveRegistrySession.Context.RegisterCleanup(()=>{if(!releaseClientCleanup)throw new InvalidOperationException("Injected client S1 cleanup quarantine");});Mark("client-cleanup-armed-"+round);}catch(Exception e){Fail(e);yield break;}
                }
                if(host&&expectClientCleanup){yield return Wait(()=>Has("client-cleanup-armed-"+round));if(failed)yield break;}
                if(host)
                {
                    if(round==1)EveRegistrySession.Context.RegisterCleanup(()=>{if(!releaseCleanup)throw new InvalidOperationException("Injected S1 cleanup quarantine");});
                    try{PNGameBootstrap.EndPlay();}catch(Exception e){if(round!=1){Fail(e);yield break;}}
                    if(round==1)
                    {
                        try{Check(!manager.CanBeginPlay&&PNGameBootstrap.IsRoomOpen,"cleanup failure retains room and blocks next play");bool rejected=false;try{PNGameBootstrap.BeginPlay();}catch(InvalidOperationException){rejected=true;}Check(rejected,"begin refuses pending cleanup");}catch(Exception e){Fail(e);yield break;}
                        releaseCleanup=true;
                    }
                }
                if(injectClient)
                {
                    yield return Wait(()=>!manager.IsPlaying&&EveRegistrySession.State==EveSessionState.Stopping);if(failed)yield break;
                    try{Check(NetworkClient.isConnected&&!manager.HasEnded,"client cleanup failure retains room without acknowledging old play");Mark("client-cleanup-blocked-"+round);}catch(Exception e){Fail(e);yield break;}
                    yield return Wait(()=>Has("host-observed-client-cleanup-"+round));if(failed)yield break;releaseClientCleanup=true;
                }
                if(host&&expectClientCleanup)
                {
                    yield return Wait(()=>Has("client-cleanup-blocked-"+round)&&EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
                    try{Check(!manager.CanBeginPlay&&NetworkServer.connections.Values.Any(c=>c.authenticationData is PNPeer p&&p.Owner==(round==1?clients:1)&&p.EndedRevision!=manager.PlayRevision),"host waits for client cleanup ACK before next play");Mark("host-observed-client-cleanup-"+round);}catch(Exception e){Fail(e);yield break;}
                }
                yield return Wait(()=>!manager.IsPlaying&&EveRegistrySession.State==EveSessionState.Stopped&&Players().Length==0);if(failed)yield break;
                try{Check(NetworkClient.isConnected&&manager.PendingBuffBytes==0&&manager.PendingMoveBytes==0&&manager.PendingBuffEvents==0&&manager.PendingMoveEvents==0,"end play releases adapters while retaining connection "+round);Check(Mod("Ended")==round&&Mod("Disposed")==0,"mod end without content dispose "+round);Mark(name+"-ended"+round);}catch(Exception e){Fail(e);yield break;}
                yield return Wait(()=>Has("host-ended"+round)&&Enumerable.Range(1,clients).All(i=>Has("client"+i+"-ended"+round)));if(failed)yield break;
            }
            if(!host&&name=="client1")
            {
                try{PNGameBootstrap.CloseRoom();Check(!PNGameBootstrap.IsRoomOpen&&EveRegistrySession.State==EveSessionState.Stopped,"client leaves lobby without active play");Mark("client1-left");File.WriteAllText(Path.Combine(folder,name+".txt"),"PASS "+checks);Application.Quit();}catch(Exception e){Fail(e);}yield break;
            }
            if(host)
            {
                yield return Wait(()=>Has("client1-left")&&NetworkServer.connections.Values.Count(c=>c.authenticationData is PNPeer p&&p.Owner>0)==clients-1);if(failed)yield break;
                try{Check(manager.CanBeginPlay,"lobby disconnect leaves room usable");PNGameBootstrap.CloseRoom();Check(!PNGameBootstrap.IsRoomOpen&&!EveRegistrySession.IsReady,"host closes lobby");Mark("host-closed");}catch(Exception e){Fail(e);yield break;}
            }
            else{yield return Wait(()=>Has("host-closed")&&!NetworkClient.isConnected);if(failed)yield break;try{Check(EveRegistrySession.State==EveSessionState.Stopped&&!manager.IsPlaying,"host close handled without active play");PNGameBootstrap.CloseRoom();}catch(Exception e){Fail(e);yield break;}}
            File.WriteAllText(Path.Combine(folder,name+".txt"),"PASS "+checks);Application.Quit();
        }
    }
}
