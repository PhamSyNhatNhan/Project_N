using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProjectN.Coop.Tests
{
    [DefaultExecutionOrder(-20000)]
    public sealed class PNWorldTransferPlayChecks : MonoBehaviour
    {
        private string folder,role;
        private PNNetworkManager manager;
        private PNGameSession game;
        private PNWorldSaveService diskSaves;
        private bool failed;
        private int checks;
        private Guid a,b;
        private static string Arg(string prefix)=>Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith(prefix,StringComparison.Ordinal))?.Substring(prefix.Length);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void InstallMods()
        {
            var folder=Arg("--pn-transfer-test=");if(folder==null)return;
            if(Application.companyName!="ProjectNVerification"||!Application.productName.StartsWith("WorldS5S8Fixture",StringComparison.Ordinal))throw new Exception("Isolated fixture product required.");
            var source=Path.Combine(folder,"NativeMods");var target=Path.Combine(Application.persistentDataPath,"Mods");
            foreach(var file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
            {
                var to=Path.Combine(target,file.Substring(source.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(to));
                if(!File.Exists(to)||!File.ReadAllBytes(to).SequenceEqual(File.ReadAllBytes(file)))File.Copy(file,to,true);
            }
            var enabled=Path.Combine(target,"enabled.json");string text=Directory.Exists(Path.Combine(source,"world_assets"))?"{\"schemaVersion\":1,\"enabledMods\":[\"native_probe\",\"world_probe\",\"world_sandbox\",\"world_assets\"]}":"{\"schemaVersion\":1,\"enabledMods\":[\"native_probe\",\"world_probe\",\"world_sandbox\"]}";
            if(!File.Exists(enabled)||File.ReadAllText(enabled)!=text)File.WriteAllText(enabled,text);
        }
        private void Awake(){if(Arg("--pn-transfer-test=")!=null){PNGameBootstrap.Config.Network.Port=7799;PNGameBootstrap.Config.Network.PendingBindings=2;}}
        private void Mark(string name,string text="ok"){var path=Path.Combine(folder,name);File.WriteAllText(path+".pending",text);File.Move(path+".pending",path);}
        private bool Has(string name)=>File.Exists(Path.Combine(folder,name));
        private void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Debug.Log("WorldS5S8 PASS "+role+" "+label);}
        private void Fail(Exception error)
        {
            if(failed)return;failed=true;
            string transfers=game?.Transfers==null?"":string.Join("\n",game.Transfers.SnapshotReceipts().Select(x=>x.Id+" "+x.State+" pending="+x.CleanupPending+" error="+x.Error));
            File.WriteAllText(Path.Combine(folder,role+".txt"),"FAIL "+error+"\nTransfers:\n"+transfers+"\nNetwork: "+manager?.LastFailure);Debug.LogException(error);try{PNGameBootstrap.End();}catch{}Application.Quit(1);
        }
        private IEnumerator Wait(Func<bool> ready,double seconds=90)
        {double until=Time.realtimeSinceStartupAsDouble+seconds;while(!failed){bool done=false;try{diskSaves?.Tick();done=ready();if(!done&&Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("checks="+checks+" "+manager?.LastFailure);}catch(Exception error){Fail(error);}if(done||failed)yield break;yield return null;}}
        private EveEntityLoader Player(long owner)
        {
            var all=EveRegistrySession.Context.Entities.Snapshot();var player=all.SingleOrDefault(x=>x.GetComponent<PNPlayerStat>()!=null&&x.LoadContext.Identity.OwnerId==owner);
            return player!=null?player:throw new Exception("Missing player owner="+owner+"; live="+string.Join(",",all.Select(x=>x.name+"/"+x.LoadContext.Identity.OwnerId+"/"+x.LoadContext.Identity.World.InstanceId)));
        }
        private static Type ModType(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(x=>x.GetType(name)).First(x=>x!=null);
        private static int Count(string type,string field)=>(int)ModType(type).GetField(field).GetValue(null);
        private bool OwnReady(Guid world)=>EveRegistrySession.IsReady&&manager.ClientWorldReady(world)&&PNLocalWorldContext.Current?.World?.InstanceId==world&&PNLocalWorldContext.Current.Actor?.IsReady==true&&!PNLocalWorldContext.Current.Actor.GetComponent<PNNetworkInputGate>().Blocked;
        private void CheckView(Guid expected)
        {
            var local=PNLocalWorldContext.Current;Check(local.World.InstanceId==expected,"local presence chooses the viewed world");
            foreach(var world in EveRegistrySession.Context.Worlds.Snapshot())
            {
                if(world.State!=EveWorldState.Ready&&world.State!=EveWorldState.Paused)continue;
                var renderers=world.PhysicsOwner.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Renderer>(true)).ToArray();
                Check(renderers.Length>0&&(world==local.World?renderers.Any(x=>!x.forceRenderingOff):renderers.All(x=>x.forceRenderingOff)),"render/particle/shadow scope "+world.InstanceId);
                foreach(var light in world.PhysicsOwner.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Light>(true)))if(world!=local.World)Check(!light.enabled,"hidden world light disabled");
                foreach(var audio in world.PhysicsOwner.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<AudioSource>(true)))if(world!=local.World)Check(audio.mute,"hidden world audio muted");
            }
            var ui=PNGameBootstrap.Runtime.GetComponent<PNUiRuntime>();ui.RefreshUI();Check(ui.HudPlayer!=null&&ui.HudPlayer.GetComponent<EveEntityIdentity>().World==local.World,"HUD binds only the local viewed presence");
            var cameraDirector=PNGameBootstrap.Runtime?.GetComponent<PNGameSession>()?.CameraDirector;
            if(cameraDirector!=null)
            {
                Check(Camera.main==cameraDirector.ViewCamera&&Camera.main.enabled,"active main camera is the director output");
                Check(Camera.allCameras.Count(x=>x.CompareTag("MainCamera")&&x.enabled)==1,"exactly one enabled MainCamera");
                var live=cameraDirector.Brain.ActiveVirtualCamera as Component;
                Check(live!=null&&PNWorldGameplay.ForScene(EveRegistrySession.Context,live.gameObject.scene)==local.World||live!=null&&live.transform.parent==cameraDirector.transform,"live virtual camera belongs to the viewed world or is the director fallback rig");
            }
            else Check(Camera.main!=null&&Camera.main.gameObject.scene==local.World.PhysicsOwner,"active main camera belongs to the viewed world");
        }
        private IEnumerator Capture(string name)
        {
            yield return null;
            var camera=Camera.main;var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try
            {
                target.Create();var request=new RenderPipeline.StandardRequest{destination=target};
                if(!RenderPipeline.SupportsRenderRequest(camera,request))throw new Exception("Pipeline render request unsupported.");
                RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(folder,role+"-"+name+".png"),texture.EncodeToPNG());
                File.WriteAllText(Path.Combine(folder,role+"-"+name+"-camera.txt"),camera.transform.position+" / "+camera.transform.rotation+" / "+PNLocalWorldContext.Current.World.InstanceId);
            }
            catch(Exception error){Fail(error);}
            finally{RenderTexture.active=previous;target.Release();Destroy(target);Destroy(texture);}
        }
        private IEnumerator Start()
        {
            folder=Arg("--pn-transfer-test=");if(folder==null)yield break;role=PNGameBootstrap.Config.Role==EveSessionRole.Host?"host":Arg("--pn-transfer-client=")??"client1";
            try{PNGameBootstrap.OpenRoom(PNGameBootstrap.Config);manager=(PNNetworkManager)NetworkManager.singleton;manager.WorldLoadAllowed=offer=>(role!="client1"||Has("allow-b")||offer.DefaultWorld)&&(role!="client2"||!Has("delay-disconnect")||offer.DefaultWorld);}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>NetworkClient.isConnected&&NetworkClient.connection.isAuthenticated&&NetworkClient.localPlayer!=null);if(failed)yield break;role=NetworkServer.active?"host":"client"+manager.RoomOwner;Mark(role+"-lobby");
            if(role=="host")yield return Host();else yield return Client();
        }
        private IEnumerator BundleAcceptance()
        {
            EveWorldContext first=null,second=null;PNWorldTransferReceipt moving=null;
            if(Directory.Exists(Path.Combine(folder,"RestartSaves","bundle")))
            {
                PNWorldSaveOperation restoring=null;
                try{diskSaves=new PNWorldSaveService(game.Session,game.Worlds,game.Lifetimes,Path.Combine(folder,"RestartSaves"));restoring=diskSaves.RestoreSaved("bundle");}catch(Exception e){Fail(e);yield break;}
                yield return Wait(()=>restoring.IsTerminal);if(failed)yield break;
                try{Check(restoring.State==PNWorldSaveState.Completed,"new host restores bundled world committed by an earlier Player process: "+restoring.Error);first=restoring.Restore.World;Check(game.Session.Entities.Snapshot(first).Any(a=>Math.Abs(a.GetComponent<PNStat>().GetCurrent(PNContentIds.Health)/a.GetComponent<PNStat>().GetMax(PNContentIds.Health)-.25)<.0001),"restarted host retains saved NPC HP before client join");}catch(Exception e){Fail(e);yield break;}
            }
            try{first=first??game.Worlds.Create(EveId.Parse("world_assets:scene"));second=game.Worlds.Create(EveId.Parse("world_assets:scene"));}catch(Exception e){Fail(e);yield break;}
            yield return Wait(()=>first.CanExecute&&second.CanExecute);if(failed)yield break;
            yield return Wait(()=>game.Session.Entities.Snapshot(first).Count==2&&game.Session.Entities.Snapshot(second).Count==2);if(failed)yield break;
            try{Check(game.WorldAssets.LoadedCount>=2&&game.WorldAssets.LeaseReferences>=4,"two bundle scene instances share loaded dependency leases");moving=game.Transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{Player(1)},first,"chunk-0","player",replacementTemplate:"base:scene_spawn"));}catch(Exception e){Fail(e);yield break;}
            yield return Wait(()=>moving.IsTerminal);if(failed)yield break;
            try{Check(moving.State==PNWorldTransferState.Completed&&Player(1).LoadContext.Identity.World==first,"real client joins bundled world through replacement/catch-up: "+moving.Error);moving=game.Transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{Player(1)},game.Worlds.DefaultWorld,"main","player",replacementTemplate:"base:scene_spawn"));}catch(Exception e){Fail(e);yield break;}
            yield return Wait(()=>moving.IsTerminal);if(failed)yield break;
            try{Check(moving.State==PNWorldTransferState.Completed,"client returns from bundled world: "+moving.Error);}catch(Exception e){Fail(e);yield break;}
            yield return Wait(()=>game.Worlds.Unload(first));if(failed)yield break;
            try{Check(second.CanExecute&&game.Session.Entities.Snapshot(second).Count==2&&game.WorldAssets.LoadedCount>=2,"unload one instance preserves second scene actors and shared assets");}catch(Exception e){Fail(e);yield break;}
            yield return Wait(()=>game.Worlds.Unload(second));if(failed)yield break;
            try{Check(game.WorldAssets.LeaseReferences==0&&game.WorldAssets.LoadedCount==0,"last scene cleanup releases all bundle dependencies");}catch(Exception e){Fail(e);}
        }
        private IEnumerator Host()
        {
            yield return Wait(()=>Has("client1-lobby")&&Has("client2-lobby"));if(failed)yield break;
            try{PNGameBootstrap.BeginPlay("world_sandbox:sandbox");game=PNGameBootstrap.Runtime.GetComponent<PNGameSession>();a=game.Worlds.DefaultWorld.InstanceId;Mark("a",a.ToString());}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("client1-a")&&Has("client2-a"));if(failed)yield break;
            EveEntityLoader one=null;PNBuffHandle buff=default;PNAbilityCooldown cooldown=null;PNWorldTransferReceipt transfer=null;EveWorldContext target=null,extra=null;EveEntityId id=default;long generation=0;int charges=0;
            try
            {
                Check(Count("WorldModeMod","Registered")==1&&Count("WorldModeMod","Started")==1,"old API6 external DLL executes on the new runtime");
                Check(Count("WorldSandboxMod.SandboxMod","Registered")==1&&Count("WorldSandboxMod.SandboxMod","SessionsStarted")==1,"new external DLL mode registers once and starts one play");
                var worlds=(EveWorldContext[])ModType("WorldSandboxMod.Demo").GetMethod("CreateTwoWorlds").Invoke(null,new object[]{game});target=worlds[0];extra=worlds[1];b=target.InstanceId;Mark("b",b.ToString());
            }
            catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>target.CanExecute&&extra.CanExecute&&EveRegistrySession.Context.Entities.Snapshot(target).Count==2);if(failed)yield break;
            try
            {
                Check(target.InstanceId!=extra.InstanceId&&target.DefinitionId==extra.DefinitionId,"external generated provider creates two independent instances");
                one=Player(1);id=one.LoadContext.Identity.Id;generation=one.Generation;var managerBuff=one.GetComponent<PNBuffManager>();var applied=managerBuff.Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("world_sandbox:odd_buff"),PNBuffSource.Capture(one.GetComponent<PNStat>())));buff=applied.Handle;managerBuff.Buffs.ChangeStacks(buff,5);
                cooldown=one.GetComponent<PNAbilityManager>().Abilities.Instances.OfType<PNAbilityCooldown>().First();cooldown.SetCooldown(600);cooldown.ResetToFull();cooldown.TryConsume();charges=cooldown.Current;
                var request=new PNWorldTransferRequest(Guid.NewGuid(),new[]{one},target,"main","player");transfer=game.Transfers.Request(request);Check(ReferenceEquals(transfer,game.Transfers.Request(request)),"duplicate transfer returns the same in-flight operation");
                bool overlap=false;try{game.Transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{one},extra,"main","player"));}catch(InvalidOperationException){overlap=true;}Check(overlap,"overlapping participant request rejected");
            }
            catch(Exception error){Fail(error);yield break;}
            yield return new WaitForSecondsRealtime(2);
            try{Check(!transfer.IsTerminal&&!one.LoadContext.Identity.IsGameplayHeld&&one.LoadContext.Identity.World.InstanceId==a,"late destination load keeps source actor usable before hold; state="+transfer.State+" error="+transfer.Error);Mark("allow-b");}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>transfer.IsTerminal&&!transfer.CleanupPending);if(failed)yield break;
            try
            {
                Check(transfer.State==PNWorldTransferState.Completed,"actual KCP owner transfer completes after body/state applied gate");
                Check(Player(1)==one&&one.LoadContext.Identity.Id==id&&one.Generation==generation&&one.GetComponent<PNBuffManager>().Buffs.Get(buff).Stacks==42&&cooldown.Current==charges&&cooldown.CooldownRemaining>0,"keep actor preserves external private Buff state, handles and cooldown");CheckView(a);Mark("one-b");
            }
            catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("client1-b"));if(failed)yield break;yield return Capture("host-a-client-b");
            try{transfer=game.Transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{one},game.Worlds.DefaultWorld,"main","player"));}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>transfer.IsTerminal&&!transfer.CleanupPending);if(failed)yield break;
            try{Check(transfer.State==PNWorldTransferState.Completed&&Player(1)==one&&one.GetComponent<PNBuffManager>().Buffs.Get(buff).Stacks==42,"actual A to B to A keeps one gameplay avatar and Buff behavior; state="+transfer.State+" error="+transfer.Error+" network="+manager.LastFailure);Mark("one-a");}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("client1-back"));if(failed)yield break;
            try
            {
                var abort=game.Transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{Player(2)},extra,"main","player"));game.Transfers.Cancel(abort.Id);Check(abort.State==PNWorldTransferState.Requested,"abort requested before commit");
                transfer=game.Transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{Player(0),Player(1)},target,"main","player"));
            }
            catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>transfer.IsTerminal&&!transfer.CleanupPending);if(failed)yield break;
            try{Check(transfer.State==PNWorldTransferState.Completed&&Player(0).LoadContext.Identity.World==target&&Player(1).LoadContext.Identity.World==target&&Player(2).LoadContext.Identity.World.InstanceId==a,"group of two transfers independently of the third player");CheckView(b);Mark("group-b");}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("client1-group")&&Has("client2-stayed"));if(failed)yield break;yield return Capture("host-b-client-a");
            try{transfer=game.Transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{Player(0),Player(1)},game.Worlds.DefaultWorld,"main","player"));}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>transfer.IsTerminal&&!transfer.CleanupPending);if(failed)yield break;
            yield return Wait(()=>transfer.IsTerminal&&!transfer.CleanupPending);if(failed)yield break;
            try
            {
                Check(transfer.State==PNWorldTransferState.Completed,"group returns before replacement");
                one.GetComponent<PNBuffManager>().Buffs.Remove(buff,PNBuffEndReason.Dispelled,true);
                transfer=game.Transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{one},game.Worlds.DefaultWorld,"main","player",replacementTemplate:"base:scene_spawn"));
            }
            catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>transfer.IsTerminal&&!transfer.CleanupPending);if(failed)yield break;
            try{Check(transfer.State==PNWorldTransferState.Completed&&Player(1)!=one&&Player(1).GetComponent<PNAbilityManager>().Abilities.GetCooldown(cooldown.Id).Current==charges,"real KCP replacement restores cooldown and replaces one owner presence; error="+transfer.Error);Mark("replacement");}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("client1-replaced"));if(failed)yield break;
            PNWorldLifetimeOperation capture=null;
            try{Check(transfer.State==PNWorldTransferState.Completed&&game.Lifetimes.IsEmpty(target),"all transferred owners release source activity subscriptions");var actors=EveRegistrySession.Context.Entities.Snapshot(target);var data=actors[0].GetComponent(ModType("WorldSandboxMod.ActorData"));data.GetType().GetField("Value").SetValue(data,111);capture=game.Lifetimes.CaptureAndUnload(target);}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>capture.IsTerminal);if(failed)yield break;
            PNWorldLifetimeOperation restore=null;
            try{Check(capture.State==PNWorldLifetimeState.Stored&&target.State==EveWorldState.Disposed,"external world checkpoint unload completes on real host");restore=game.Lifetimes.Restore(b);}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>restore.IsTerminal);if(failed)yield break;
            try
            {
                var actors=EveRegistrySession.Context.Entities.Snapshot(restore.World);var type=ModType("WorldSandboxMod.ActorData");
                Check(restore.State==PNWorldLifetimeState.Completed&&restore.World.Incarnation==2&&actors.Count==2,"restored generated world has new incarnation and no duplicate authored enrollment state="+restore.State+" incarnation="+restore.World.Incarnation+" actors="+actors.Count+" names="+string.Join(",",actors.Select(x=>x.name))+" error="+restore.Error+" worldFailure="+restore.World.Failure);
                Check(actors.Any(x=>(int)type.GetField("Value").GetValue(x.GetComponent(type))==111)&&actors.All(x=>actors.Contains((EveEntityLoader)type.GetField("Linked").GetValue(x.GetComponent(type)))),"external serializer restores custom data and remaps references to new entities");
                Check(Count("WorldSandboxMod.SandboxMod","SessionsStarted")==1&&Count("WorldSandboxMod.GeneratedProvider","ReadyCount")==3,"world lifecycle callbacks stay distinct from the single play callback");
                if(!Directory.Exists(Path.Combine(folder,"NativeMods","world_assets")))Mark("finish");
            }
            catch(Exception error){Fail(error);yield break;}
            if(Directory.Exists(Path.Combine(folder,"NativeMods","world_assets")))
            {yield return BundleAcceptance();if(failed)yield break;Mark("finish");}
            PNWorldTransferReceipt interrupted=null;
            try{Mark("delay-disconnect");interrupted=game.Transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{Player(2)},extra,"main","player"));}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>interrupted.State==PNWorldTransferState.PreparingDestination);if(failed)yield break;
            try{Mark("disconnect-now");}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>interrupted.IsTerminal&&!interrupted.CleanupPending&&!NetworkServer.connections.Values.Any(x=>x.authenticationData is PNPeer p&&p.Owner==2));if(failed)yield break;
            try{Check(interrupted.State==PNWorldTransferState.Aborted&&extra.CanExecute&&Player(0).IsReady&&Player(1).IsReady,"actual peer disconnect during destination preparation aborts without disrupting remaining owners");PNGameBootstrap.EndPlay();}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>EveRegistrySession.State==EveSessionState.Stopped&&manager.PendingWorldBytes==0&&manager.PendingBuffBytes==0&&manager.PendingMoveBytes==0);if(failed)yield break;
            try{Check(game.Transfers.PendingCount==0&&manager.PendingTransferMessages==0,"transfer and required adapter/world ledgers drain");Check(Count("WorldSandboxMod.SandboxMod","SessionsEnded")==1&&Count("WorldSandboxMod.GeneratedProvider","ReadyCount")==Count("WorldSandboxMod.GeneratedProvider","DisposedCount"),"mod world cleanup and one session end run exactly once");Mark("ended");}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("client1-ended")&&Has("client2-ended"));if(failed)yield break;
            try{PNGameBootstrap.End();Mark("closed");File.WriteAllText(Path.Combine(folder,role+".txt"),"PASS "+checks+" actual transfer/presentation/world state/external DLL cases");Application.Quit(0);}catch(Exception error){Fail(error);}
        }
        private IEnumerator Client()
        {
            yield return Wait(()=>Has("a")&&EveRegistrySession.IsReady);if(failed)yield break;a=Guid.Parse(File.ReadAllText(Path.Combine(folder,"a")));
            yield return Wait(()=>OwnReady(a));if(failed)yield break;
            try{game=PNGameBootstrap.Runtime.GetComponent<PNGameSession>();CheckView(a);Mark(role+"-a");}catch(Exception error){Fail(error);yield break;}
            if(role=="client1")
            {
                yield return Wait(()=>Has("one-b")&&Has("b"));if(failed)yield break;b=Guid.Parse(File.ReadAllText(Path.Combine(folder,"b")));yield return Wait(()=>OwnReady(b));if(failed)yield break;
                try{CheckView(b);Check(EveRegistrySession.Context.Entities.Snapshot().Count(x=>x.GetComponent<PNPlayerStat>()!=null&&x.LoadContext.Identity.OwnerId==manager.RoomOwner)==1,"owner has one replica after presence replacement");Mark("client1-b");}catch(Exception error){Fail(error);yield break;}yield return Wait(()=>PNTransitionController.Current==null||!PNTransitionController.Current.IsShowing);if(failed)yield break;try{var transition=PNTransitionController.Current;Check(transition==null||transition.ShowCount>=2&&transition.HideCount==transition.ShowCount&&!transition.IsShowing,"join and transfer each opened and closed one transition cycle show="+transition?.ShowCount+" hide="+transition?.HideCount+" showing="+transition?.IsShowing+" source="+PNTransitionSources.Snapshot().OfType<PNTransferTransitionSource>().FirstOrDefault()?.History+" sources="+PNTransitionSources.Snapshot().Count);}catch(Exception error){Fail(error);yield break;}yield return Capture("host-a-client-b");
                yield return Wait(()=>Has("one-a")&&OwnReady(a));if(failed)yield break;try{CheckView(a);Mark("client1-back");}catch(Exception error){Fail(error);yield break;}
                yield return Wait(()=>Has("group-b")&&OwnReady(b));if(failed)yield break;try{CheckView(b);Mark("client1-group");}catch(Exception error){Fail(error);yield break;}
            }
            else
            {
                yield return Wait(()=>Has("group-b")&&OwnReady(a));if(failed)yield break;try{CheckView(a);Mark("client2-stayed");}catch(Exception error){Fail(error);yield break;}yield return Capture("host-b-client-a");
            }
            if(role=="client1")
            {
                yield return Wait(()=>Has("replacement")&&OwnReady(a));if(failed)yield break;
                try{CheckView(a);Check(EveRegistrySession.Context.Entities.Snapshot().Count(x=>x.GetComponent<PNPlayerStat>()!=null&&x.LoadContext.Identity.OwnerId==manager.RoomOwner)==1,"replacement catch-up releases one owned body/presence");Mark("client1-replaced");}catch(Exception error){Fail(error);yield break;}
            }
            if(role=="client2")
            {
                yield return Wait(()=>Has("disconnect-now"));if(failed)yield break;
                try{NetworkClient.Disconnect();PNGameBootstrap.End();}catch(Exception error){Fail(error);yield break;}
                yield return Wait(()=>EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
                try{Check(manager.PendingWorldBytes==0&&manager.PendingBuffBytes==0&&manager.PendingMoveBytes==0,"disconnect releases client required ledgers");Mark("client2-ended");File.WriteAllText(Path.Combine(folder,role+".txt"),"PASS "+checks+" actual transfer/local-view/disconnect cases");Application.Quit(0);}catch(Exception error){Fail(error);}yield break;
            }
            yield return Wait(()=>Has("ended")&&EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
            try{Check(NetworkClient.isConnected&&manager.PendingWorldBytes==0&&manager.PendingBuffBytes==0&&manager.PendingMoveBytes==0,"client returns to the retained room with ledgers zero");Mark(role+"-ended");}catch(Exception error){Fail(error);yield break;}
            yield return Wait(()=>Has("closed"));if(failed)yield break;try{PNGameBootstrap.End();File.WriteAllText(Path.Combine(folder,role+".txt"),"PASS "+checks+" actual transfer/local-view cases");Application.Quit(0);}catch(Exception error){Fail(error);}
        }
    }
}
