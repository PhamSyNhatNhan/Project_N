using System;
using System.Collections;
using System.Collections.Generic;
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
    // Small correctness scenario, not a load/performance fixture. No gameplay transfer.
    [DefaultExecutionOrder(-20000)]
    public sealed class PNWorldPeerChecks : MonoBehaviour
    {
        private string folder,role;
        private PNNetworkManager manager;
        private int checks;
        private bool failed;
        private Keyboard keyboard;
        private double allowLoadAt;
        private Guid a,b;
        private PNWorldBindingScope oldScope;
        private ulong oldNonce;
        private uint ownedNetId;
        private Guid room;
        private uint roomShell;
        private static string Arg(string prefix)=>Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith(prefix,StringComparison.Ordinal))?.Substring(prefix.Length);
        private void Awake(){if(Arg("--pn-world-test=")==null)return;PNGameBootstrap.Config.Network.Port=7798;PNGameBootstrap.Config.Network.PendingBindings=2;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;}
        private void Mark(string name,string body="ok") {string path=Path.Combine(folder,name);File.WriteAllText(path+".pending",body);File.Move(path+".pending",path);}
        private bool Has(string name)=>File.Exists(Path.Combine(folder,name));
        private void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Debug.Log("WorldS4 PASS "+role+" "+name);}
        private void Fail(Exception error)
        {if(failed)return;failed=true;File.WriteAllText(Path.Combine(folder,role+".txt"),"FAIL "+error);Debug.LogException(error);try{PNGameBootstrap.End();}catch{}Application.Quit(1);}
        private IEnumerator Wait(Func<bool> condition,double seconds=90)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+seconds;
            while(!failed)
            {
                bool ready=false;try{ready=condition();if(!ready&&Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("checks="+checks+" "+manager?.LastFailure);}catch(Exception error){Fail(error);}
                if(ready||failed)yield break;yield return null;
            }
        }
        private PNActorShell[] Shells()=>FindObjectsByType<PNActorShell>();
        private bool ReadyIn(Guid world)=>EveRegistrySession.IsReady&&manager.ClientInputReady&&manager.ClientWorldReady(world)&&EveRegistrySession.Context.Entities.Snapshot().Count>0&&Shells().All(x=>x.Metadata.World==world);
        private void Inject(string method,object value)=>typeof(PNNetworkManager).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(manager,new[]{value});
        private IEnumerator Start()
        {
            folder=Arg("--pn-world-test=");if(folder==null)yield break;
            role=PNGameBootstrap.Config.Role==EveSessionRole.Host?"host":Arg("--pn-world-client=")??"client1";
            try
            {
                PNGameBootstrap.OpenRoom(PNGameBootstrap.Config);manager=(PNNetworkManager)NetworkManager.singleton;
                allowLoadAt=role=="client3"?double.PositiveInfinity:0;
                manager.WorldLoadAllowed=offer=>{if(double.IsPositiveInfinity(allowLoadAt))allowLoadAt=Time.realtimeSinceStartupAsDouble+17;return Time.realtimeSinceStartupAsDouble>=allowLoadAt&&!(role=="client2"&&Has("delay-b")&&offer.Ticket.World==b);};
            }
            catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>NetworkClient.isConnected&&NetworkClient.connection.isAuthenticated&&NetworkClient.localPlayer!=null);if(failed)yield break;
            room=manager.RoomId;roomShell=NetworkClient.localPlayer.netId;Mark(role+"-lobby");
            if(role=="host")yield return Host();else yield return Client();
        }
        private IEnumerator Host()
        {
            yield return Wait(()=>Has("client1-lobby")&&Has("client2-lobby"));if(failed)yield break;
            try{PNGameBootstrap.BeginPlay();a=manager.Worlds.DefaultWorld.InstanceId;Mark("world-a",a.ToString());}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("client1-a")&&Has("client2-a"));if(failed)yield break;
            EveWorldContext second=null;
            try{Check(EveRegistrySession.Context.Worlds.Snapshot().Count==1,"one initial world");second=manager.Worlds.Create(EveId.Parse("base:scene_world"));b=second.InstanceId;Mark("world-b",b.ToString());}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>second.State==EveWorldState.Ready&&Shells().Any(x=>x.Metadata.World==b));if(failed)yield break;
            var peer2=NetworkServer.connections.Values.Single(c=>c.authenticationData is PNPeer p&&p.Owner==2);
            try
            {
                Check(second.PhysicsOwner.GetPhysicsScene()!=manager.Worlds.DefaultWorld.PhysicsOwner.GetPhysicsScene(),"built-in additive provider has separate real physics owner");
                manager.UnsubscribeWorld(peer2,a);manager.SubscribeWorld(peer2,second);Mark("host-split");
            }
            catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("client1-split")&&Has("client2-b")&&Has("client3-waiting"));if(failed)yield break;
            float before=0;
            try
            {
                var player=EveRegistrySession.Context.Entities.Snapshot().Single(x=>x.LoadContext.Identity.OwnerId==1&&x.GetComponent<PNPlayerMove>()!=null);
                before=player.transform.position.x;Mark("move-now");
            }
            catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("client1-moved"));if(failed)yield break;
            // The file marker and KCP pose use independent delivery paths. Wait
            // for the actual host body, bounded while the late peer still waits.
            double poseDeadline=Time.realtimeSinceStartupAsDouble+3;
            yield return Wait(()=>
            {
                var player=EveRegistrySession.Context.Entities.Snapshot().Single(x=>x.LoadContext.Identity.OwnerId==1&&x.GetComponent<PNPlayerMove>()!=null);
                if(player.transform.position.x>before+.05f)return true;
                if(Time.realtimeSinceStartupAsDouble>poseDeadline)throw new TimeoutException("Owner pose did not reach host within three seconds.");
                return false;
            });if(failed)yield break;
            try
            {
                var player=EveRegistrySession.Context.Entities.Snapshot().Single(x=>x.LoadContext.Identity.OwnerId==1&&x.GetComponent<PNPlayerMove>()!=null);
                Check(player.transform.position.x>before+.05f,"existing owner continues actual movement while another peer waits on world content");
                Check(!Has("client3-a"),"owner pose reaches host while late peer still waits on world content");
                Check(peer2.observing.Where(x=>x.GetComponent<PNActorShell>()!=null).All(x=>x.GetComponent<PNActorShell>().Metadata.World==b),"server observer set excludes unauthorized initial actor state");
            }
            catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("client3-a"));if(failed)yield break;
            try{Check(NetworkServer.connections.Values.Count(c=>c.authenticationData is PNPeer p&&p.Owner>0)==3,"late peer survives world load beyond interruption grace");Mark("delay-b");manager.UnsubscribeWorld(peer2,b);manager.SubscribeWorld(peer2,second);Mark("host-b-rejoin");}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("client2-b-pending"));if(failed)yield break;
            try{manager.Worlds.Unload(second);}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>{try{return manager.Worlds.Unload(second);}catch(EveWorldCleanupPendingException){return false;}});if(failed)yield break;
            try{Check(EveRegistrySession.Context.Entities.Snapshot(manager.Worlds.DefaultWorld).Count>=4&&manager.IsPlaying,"unload B during join leaves A and its players running");manager.Worlds.DefaultWorld.SetPaused(true);manager.SubscribeWorld(peer2,manager.Worlds.DefaultWorld);Mark("host-return-a");}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("client2-paused-ready"));if(failed)yield break;
            try{Check(manager.Worlds.DefaultWorld.State==EveWorldState.Paused,"late observation settles while its world is paused");manager.Worlds.DefaultWorld.SetPaused(false);}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("client2-returned")&&Has("client3-negative-ready"));if(failed)yield break;
            Mark("bad-request-now");
            yield return Wait(()=>Has("client3.txt")&&NetworkServer.connections.Values.Count(c=>c.authenticationData is PNPeer p&&p.Owner>0)==2);if(failed)yield break;
            yield return Wait(()=>manager.MovementAuthority.TrackedCount==EveRegistrySession.Context.Entities.Snapshot().Count(x=>x.GetComponent<PNMove>()!=null));if(failed)yield break;
            try{Check(true,"retired owner movement model is untracked after loader invalidation");}catch(Exception error){Fail(error);yield break;}
            try{Check(manager.IsPlaying&&EveRegistrySession.IsReady,"unauthorized report/request isolates offending peer");PNGameBootstrap.EndPlay();Mark("play-ended");}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>manager.CanBeginPlay&&Has("client1-ended")&&Has("client2-ended"));if(failed)yield break;
            try
            {
                Check(manager.PendingWorldBytes==0&&manager.PendingBuffBytes==0&&manager.PendingMoveBytes==0&&manager.WorldSubscriptionCount==0,"host required resource ledgers drain to zero");
                Check(manager.RoomId==room&&NetworkClient.localPlayer.netId==roomShell,"room/control shell survives world operations and play end");
                PNGameBootstrap.CloseRoom();Mark("host-closed");File.WriteAllText(Path.Combine(folder,"host.txt"),"PASS "+checks+" world host cases");
            }
            catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("client1.txt")&&Has("client2.txt"));if(!failed)Application.Quit(0);
        }
        private IEnumerator Client()
        {
            yield return Wait(()=>manager.IsPlaying&&Has("world-a"));if(failed)yield break;
            a=Guid.Parse(File.ReadAllText(Path.Combine(folder,"world-a")));
            if(role=="client3")
            {
                yield return Wait(()=>manager.ClientInputReady);if(failed)yield break;
                try{Check(manager.PublishedCount==0&&EveRegistrySession.Context.Worlds.Snapshot().Count==0,"room/play heartbeat can settle while world content is still waiting");Mark("client3-waiting");}catch(Exception error){Fail(error);yield break;}
                yield return Wait(()=>Time.realtimeSinceStartupAsDouble>=allowLoadAt-.6);if(failed)yield break;
                try{Check(NetworkClient.isConnected&&manager.IsPlaying&&manager.PublishedCount==0,"world transition wait exceeds 15 seconds without becoming transport grace");}catch(Exception error){Fail(error);yield break;}
            }
            yield return Wait(()=>ReadyIn(a));if(failed)yield break;
            try
            {
                Check(Shells().All(x=>x.Metadata.World==a),"initial state contains only the authorized world");
                var owned=Shells().Single(x=>x.Metadata.Owner==manager.RoomOwner&&PNEntityRoles.IsPlayer(EveRegistrySession.Context.Registry,x.Metadata.Template));ownedNetId=owned.netId;oldScope=manager.BindingWorldScope(ownedNetId);oldNonce=(ulong)manager.BindingToken(ownedNetId);Mark(role+"-a");
            }
            catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("world-b"));if(failed)yield break;b=Guid.Parse(File.ReadAllText(Path.Combine(folder,"world-b")));
            if(role=="client1")
            {
                yield return Wait(()=>Has("host-split"));if(failed)yield break;
                try{Check(ReadyIn(a)&&Shells().All(x=>x.Metadata.World!=b),"peer A has no B actor snapshots");Mark("client1-split");}catch(Exception error){Fail(error);yield break;}
                yield return Wait(()=>Has("move-now"));if(failed)yield break;
                PNPlayerMove move=null;float start=0;
                try{move=FindObjectsByType<PNPlayerMove>().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId==manager.RoomOwner);start=move.transform.position.x;keyboard=InputSystem.AddDevice<Keyboard>();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D));}catch(Exception error){Fail(error);yield break;}
                yield return Wait(()=>move.transform.position.x>start+.2f);if(failed)yield break;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.RemoveDevice(keyboard);keyboard=null;Mark("client1-moved");
            }
            else if(role=="client2")
            {
                yield return Wait(()=>ReadyIn(b));if(failed)yield break;
                try{Check(Shells().All(x=>x.Metadata.World==b)&&EveRegistrySession.Context.Worlds.Snapshot().All(x=>x.InstanceId==b),"individual subscription loads built-in B and releases A replica");Mark("client2-b");}catch(Exception error){Fail(error);yield break;}
                yield return Wait(()=>Has("host-b-rejoin")&&manager.ClientWorldRevision(b)>0&&!manager.ClientWorldReady(b)&&manager.PublishedCount==0);if(failed)yield break;
                try{Check(manager.ClientInputReady,"individual pending subscription retains room/play readiness");Mark("client2-b-pending");}catch(Exception error){Fail(error);yield break;}
                yield return Wait(()=>Has("host-return-a")&&ReadyIn(a));if(failed)yield break;
                PNPlayerMove ownMove=null;
                try
                {
                    ownMove=FindObjectsByType<PNPlayerMove>().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId==manager.RoomOwner);
                    Check(manager.Worlds.DefaultWorld.State==EveWorldState.Paused&&ownMove.GetComponent<PNNetworkInputGate>().Blocked,"new observation in a paused world keeps input blocked after baseline");Mark("client2-paused-ready");
                }catch(Exception error){Fail(error);yield break;}
                yield return Wait(()=>manager.Worlds.DefaultWorld.State==EveWorldState.Ready&&!ownMove.GetComponent<PNNetworkInputGate>().Blocked&&!ownMove.MovementHeld&&ownMove.CanInput);if(failed)yield break;
                try{Check(true,"resume refreshes the initial paused observation input gate");}catch(Exception error){Fail(error);yield break;}
                try
                {
                    var shell=Shells().Single(x=>x.Metadata.Owner==manager.RoomOwner&&PNEntityRoles.IsPlayer(EveRegistrySession.Context.Registry,x.Metadata.Template));
                    var current=manager.BindingWorldScope(shell.netId);Check(shell.netId==ownedNetId&&current.Subscription>oldScope.Subscription,"reobserve same actor with a new subscription and unchanged authoritative presence");
                    var stat=EveRegistrySession.Context.Entities.Snapshot().Single(x=>x.LoadContext.Identity.OwnerId==manager.RoomOwner&&x.GetComponent<PNPlayerMove>()!=null).GetComponent<PNStatReplication>();
                    long revision=stat.FullViewRevision;var frame=new PNStatFrame(shell.State.Revision+10000,shell.State.LifeRevision+1,true,shell.State.Resources.ToDictionary(x=>x.Key,x=>new PNResourceValue(0,x.Value.Max)));
                    Inject("ReceiveStatMessage",new PNStatNotice{NetId=shell.netId,Session=shell.Metadata.HostSession,Entity=shell.Metadata.Entity,Generation=shell.Metadata.Generation,Scope=oldScope,State=frame});
                    Inject("ReceiveBuffPacket",new PNBuffPacket{Stream=new PNBuffStream{Session=shell.Metadata.HostSession,Entity=shell.Metadata.Entity,Generation=shell.Metadata.Generation,NetId=shell.netId,Nonce=oldNonce,Scope=oldScope},Transfer=1,Kind=0,Revision=0,Time=0,HostTime=0,Rate=1,Total=1,Index=0,Count=1,Body=new byte[]{255}});
                    var ticket=new PNWorldTicket{Room=manager.RoomId,RoomIncarnation=manager.RoomIncarnation,Play=manager.PlaySession,PlayRevision=manager.PlayRevision,World=a,Incarnation=oldScope.Incarnation,Subscription=oldScope.Subscription};
                    Inject("ReceiveWorldConfirmation",new PNWorldApplied{Ticket=ticket,Revision=ulong.MaxValue,Digest=new string('a',64),Confirmed=true});
                    Check(stat.FullViewRevision==revision&&manager.ClientWorldReady(a)&&manager.IsPlaying,"stale Stat/Buff packets and old applied callback cannot change/release the new binding");
                    Check(manager.RoomId==room&&NetworkClient.localPlayer.netId==roomShell,"subscription changes retain connection and room shell");Mark("client2-returned");
                }
                catch(Exception error){Fail(error);yield break;}
            }
            else
            {
                Mark("client3-negative-ready");yield return Wait(()=>Has("bad-request-now"));if(failed)yield break;
                try
                {
                    var shell=Shells().First(x=>x.HasMove);var scope=manager.BindingWorldScope(shell.netId);scope.World=b;
                    NetworkClient.Send(new PNMoveRequest{Request=1,Stream=new PNMoveStream{Session=shell.Metadata.HostSession,NetId=shell.netId,Entity=shell.Metadata.Entity,Generation=shell.Metadata.Generation,Nonce=(ulong)manager.BindingToken(shell.netId),Scope=scope}});
                }
                catch(Exception error){Fail(error);yield break;}
                yield return Wait(()=>!NetworkClient.isConnected);if(failed)yield break;
                try{Check(true,"unauthorized cross-world request is rejected by host");PNGameBootstrap.End();}catch(Exception error){Fail(error);yield break;}
                yield return Wait(()=>EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
                try{Check(manager.PendingWorldBytes==0&&manager.PendingBuffBytes==0&&manager.PendingMoveBytes==0,"rejected peer releases all required world/adapter resources");File.WriteAllText(Path.Combine(folder,role+".txt"),"PASS "+checks+" world late/rejection cases");Application.Quit(0);}catch(Exception error){Fail(error);}yield break;
            }
            yield return Wait(()=>Has("play-ended")&&!manager.IsPlaying&&EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
            try{Check(NetworkClient.isConnected&&manager.RoomId==room&&NetworkClient.localPlayer.netId==roomShell,"play end retains coherent room connection/control shell");Check(manager.PendingWorldBytes==0&&manager.PendingBuffBytes==0&&manager.PendingMoveBytes==0,"client ledgers drain after world/play teardown");Mark(role+"-ended");}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("host-closed"));if(failed)yield break;
            try{PNGameBootstrap.End();File.WriteAllText(Path.Combine(folder,role+".txt"),"PASS "+checks+" world client cases");Application.Quit(0);}catch(Exception error){Fail(error);}
        }
    }
}
