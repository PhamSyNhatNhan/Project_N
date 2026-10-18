using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using EveEngine;
using EveEngine.Movement;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ProjectN.Coop.Tests
{
    [DefaultExecutionOrder(-20000)]
    public sealed class PNMoveAdapterPlayChecks:MonoBehaviour
    {
        private string folder,roleName;private int checks;private bool failed;private Keyboard keyboard;
        private Action<NetworkWriter,PNMovePacket> originalMoveWriter;private bool corrupted;
        private static string Arg(string prefix)=>Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith(prefix,StringComparison.Ordinal))?.Substring(prefix.Length);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void InstallPreviousApiMod()
        {
            string output=Arg("--pn-move-test=");if(output==null)return;string from=Path.Combine(output,"NativeMods");if(!Directory.Exists(from))return;
            if(Application.companyName!="ProjectNVerification"||!Application.productName.StartsWith("P4Fixture",StringComparison.Ordinal))throw new InvalidOperationException("P4 mod fixture requires isolated product storage.");
            string mods=Path.Combine(Application.persistentDataPath,"Mods");
            foreach(string file in Directory.GetFiles(from,"*",SearchOption.AllDirectories)){string to=Path.Combine(mods,file.Substring(from.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(to));File.Copy(file,to,true);}
            File.WriteAllText(Path.Combine(mods,"enabled.json"),"{\"schemaVersion\":1,\"enabledMods\":[\"native_probe\"]}");
        }
        private void Awake(){if(Arg("--pn-move-test=")!=null){PNGameBootstrap.Config.Network.Port=7794;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.name="Disposable verification catch floor";ground.transform.position=new Vector3(0,-.5f,0);ground.transform.localScale=new Vector3(1000,1,1000);ground.GetComponent<MeshRenderer>().enabled=false;}}
        private void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Debug.Log("P4 PASS "+roleName+" "+message);}
        private void Fail(Exception error){if(failed)return;failed=true;File.WriteAllText(Path.Combine(folder,roleName+".txt"),"FAIL "+error);Debug.LogException(error);try{PNGameBootstrap.End();}catch{}Application.Quit(1);}
        private void OnDestroy(){if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(originalMoveWriter!=null)Writer<PNMovePacket>.write=originalMoveWriter;}
        private IEnumerator Wait(Func<bool> condition)
        {
            double until=Time.realtimeSinceStartupAsDouble+90;
            while(!failed){bool done=false;try{done=condition();if(!done&&Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("P4 "+roleName+" checks="+checks+" failure="+(NetworkManager.singleton as PNNetworkManager)?.LastFailure);}catch(Exception e){Fail(e);}if(done||failed)yield break;yield return null;}
        }
        private string FileName(string name)=>Path.Combine(folder,name);
        private void Mark(string name)=>File.WriteAllText(FileName(name),"ok");
        private PNPlayerMove[] Players()=>FindObjectsByType<PNPlayerMove>().Where(x=>x.GetComponent<EveEntityLoader>()?.IsReady==true).ToArray();
        private void Capture(string path)
        {
            var camera=Camera.main??FindObjectsByType<Camera>().First(x=>x.enabled);var old=camera.targetTexture;var active=RenderTexture.active;var target=new RenderTexture(960,540,24);Texture2D image=null;
            try{target.Create();camera.targetTexture=target;camera.Render();RenderTexture.active=target;image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();Check(image.GetPixels32().Count(x=>x.r>20||x.g>20||x.b>20)>100,"actual world render pixels");File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{camera.targetTexture=old;RenderTexture.active=active;if(image!=null)Destroy(image);target.Release();Destroy(target);}
        }
        private IEnumerator Start()
        {
            folder=Arg("--pn-move-test=");if(folder==null)yield break;
            bool host=PNGameBootstrap.Config.Role==EveSessionRole.Host;roleName=host?"host":Arg("--pn-move-client=")??"client1";int clients=int.Parse(Arg("--pn-move-clients=")??"3");
            yield return Wait(()=>NetworkManager.singleton is PNNetworkManager m&&m.PublishedCount>=2);if(failed)yield break;var manager=(PNNetworkManager)NetworkManager.singleton;
            if(host){if(Directory.Exists(FileName("NativeMods"))){try{var player=Players().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId==0);var stat=player.GetComponent<PNStat>();player.GetComponent<PNBuffManager>().Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("native_probe:buff_probe"),new PNBuffSource(EveRegistrySession.Context.SessionId,stat.EntityId)));Check(true,"previous API6 DLL behavior executes on P4 runtime");}catch(Exception e){Fail(e);yield break;}}Mark("host-ready");yield return Wait(()=>Players().Length==clients+1);}
            else{yield return Wait(()=>manager.ClientInputReady&&manager.Worlds.DefaultWorld!=null&&manager.ClientWorldReady(manager.Worlds.DefaultWorld.InstanceId)&&Players().Length>=2);Mark(roleName+"-joined");}
            if(failed)yield break;
            yield return Wait(()=>Players().Length==clients+1&&(host||manager.ClientInputReady&&manager.Worlds.DefaultWorld!=null&&manager.ClientWorldReady(manager.Worlds.DefaultWorld.InstanceId)&&Players().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId==manager.RoomOwner).MovementBaselineApplied&&!Players().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId==manager.RoomOwner).MovementHeld));if(failed)yield break;
            long owner=host?0:EveRegistrySession.Context.LocalOwnerId.Value;var own=Players().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId==owner);
            if(Arg("--pn-move-fault=") is string fault&&fault.Length>0){yield return FaultCase(host,manager,fault);yield break;}
            try
            {
                Check(own.NetworkRole==(host?PNMoveNetworkRole.Local:PNMoveNetworkRole.Owner),"real actor role");
                Check(host||!own.GetComponent<EveEntityLoader>().LoadContext.CanMutate,"owner movement does not grant gameplay authority");
                Check(host||own.MovementBaselineApplied&&!own.MovementHeld,"baseline applied before input");
                if(host){foreach(var p in Players()){long id=p.GetComponent<EveEntityIdentity>().Id.Value;var model=manager.MovementAuthority.TypedModel(p.NetworkIdentity(EveRegistrySession.Context.SessionId));var state=new MovementState(new MoveVector(p.GetComponent<EveEntityIdentity>().OwnerId*4,1.2f,0),default);manager.MovementAuthority.Relocate(model.Identity,state,default);}Mark("separated");}
            }catch(Exception e){Fail(e);yield break;}
            yield return Wait(()=>File.Exists(FileName("separated")));yield return new WaitForSecondsRealtime(1);if(failed)yield break;
            float start=own.Body.position.x;
            try{keyboard=InputSystem.AddDevice<Keyboard>();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D));}catch(Exception e){Fail(e);yield break;}
            yield return new WaitForSecondsRealtime(.7f);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return new WaitForSecondsRealtime(.3f);
            try{Check(own.Body.position.x>start+.2f,"actual InputSystem and FixedUpdate movement start="+start+" end="+own.Body.position+" input="+own.CanInput+" ready="+own.IsReady+" held="+own.MovementHeld);File.WriteAllText(FileName(roleName+"-moved"),own.Body.position.x.ToString("R",CultureInfo.InvariantCulture));}catch(Exception e){Fail(e);yield break;}
            yield return Wait(()=>Enumerable.Range(1,clients).All(i=>File.Exists(FileName("client"+i+"-moved"))));if(failed)yield break;
            if(host)
            {
                yield return new WaitForSecondsRealtime(.6f);
                try{foreach(var p in Players().Where(x=>x.GetComponent<EveEntityIdentity>().OwnerId>0)){long pid=p.GetComponent<EveEntityIdentity>().OwnerId;float expected=float.Parse(File.ReadAllText(FileName("client"+pid+"-moved")),CultureInfo.InvariantCulture);Check(Math.Abs(p.Body.position.x-expected)<.5,"host accepted actual owner "+pid);}Mark("all-moved");}catch(Exception e){Fail(e);yield break;}
            }
            yield return Wait(()=>File.Exists(FileName("all-moved")));if(failed)yield break;
            yield return Wait(()=>own.IsGrounded&&!own.MovementHeld);if(failed)yield break;float jumpStart=own.Body.position.y;
            try{Check(own.TryJump(),"existing jump API accepts owned physical jump");}catch(Exception e){Fail(e);yield break;}
            yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
            try{Check(own.Body.position.y>jumpStart+.001f,"actual physics advances owner jump");Mark(roleName+"-jumped");}catch(Exception e){Fail(e);yield break;}
            yield return Wait(()=>Enumerable.Range(1,clients).All(i=>File.Exists(FileName("client"+i+"-jumped"))));if(failed)yield break;
            yield return NetworkPhases(host,manager,clients);if(failed)yield break;
            if(!host)
            {
                try{Check(Players().Where(x=>x!=own).All(x=>x.NetworkRole==PNMoveNetworkRole.Observer&&x.Body.isKinematic),"observers are kinematic and never owner writers");manager.NotifyApplicationPause(true);}catch(Exception e){Fail(e);yield break;}
                yield return new WaitForSecondsRealtime(.4f);
                try{Check(own.MovementHeld&&own.Body.isKinematic,"interruption holds body");manager.NotifyApplicationPause(false);}catch(Exception e){Fail(e);yield break;}
                yield return Wait(()=>manager.ClientInputReady&&!own.MovementHeld);if(failed)yield break;
                try{Check(own.MovementBaselineApplied&&manager.PendingMoveBytes==0&&manager.PendingMoveEvents==0,"grace applies coherent baseline and releases reservations");Capture(FileName(roleName+"-world.png"));Mark(roleName+"-resumed");}catch(Exception e){Fail(e);yield break;}
                yield return Wait(()=>File.Exists(FileName("end-clients")));if(failed)yield break;
            }
            else
            {
                yield return Wait(()=>Enumerable.Range(1,clients).All(i=>File.Exists(FileName("client"+i+"-resumed"))));if(failed)yield break;Mark("end-clients");yield return Wait(()=>Players().Length==1);if(failed)yield break;
            }
            PNGameBootstrap.End();yield return Wait(()=>EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
            try{Check(manager.PendingMoveBytes==0&&manager.PendingMoveEvents==0,"teardown releases Move pending resources");File.WriteAllText(FileName(roleName+".txt"),"PASS "+checks+" P4 actual KCP/input "+roleName);Application.Quit(0);}catch(Exception e){Fail(e);}
        }
        private IEnumerator NetworkPhases(bool host,PNNetworkManager manager,int clients)
        {
            var target=Players().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId==1);var stat=target.GetComponent<PNStat>();var clock=target.GetComponent<EveTimeScale>();var source=EveId.Parse("fixture:move_freeze");
            int died=0;stat.Died+=_=>died++;
            var hostShell=FindObjectsByType<PNActorShell>().Single(x=>x.Metadata.Owner==0&&x.HasMove);long oldToken=manager.BindingToken(hostShell.netId);uint hostNetId=hostShell.netId;
            for(int phase=0;phase<7;phase++)
            {
                string signal="move-phase"+phase;
                if(host)
                {
                    try
                    {
                        if(phase==0){var model=manager.MovementAuthority.TypedModel(target.NetworkIdentity(EveRegistrySession.Context.SessionId));manager.MovementAuthority.Relocate(model.Identity,new MovementState(new MoveVector(20,4,0),default),default);}
                        if(phase==1){clock.MinScale=0;clock.AddOwnedModifier(source,EveTimeModifierKind.Scale,-1,target);}
                        if(phase==2){clock.RemoveSource(source);clock.MinScale=1;}
                        if(phase==3)stat.SetCurrent(PNContentIds.Health,0);
                        if(phase==4)stat.SetCurrent(PNContentIds.Health,stat.GetMax(PNContentIds.Health));
                        if(phase==6)
                        {
                            // Exercise installed Mirror hide/show in one frame; reflection stays test-only.
                            var connection=NetworkServer.connections.Values.Single(x=>x.authenticationData is PNPeer p&&p.Owner==1);var identity=hostShell.netIdentity;var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                            typeof(NetworkIdentity).GetMethod("RemoveObserver",flags).Invoke(identity,new object[]{connection});
                            typeof(NetworkConnectionToClient).GetMethod("RemoveFromObserving",flags).Invoke(connection,new object[]{identity,false});
                            typeof(NetworkIdentity).GetMethod("AddObserver",flags).Invoke(identity,new object[]{connection});
                        }
                        Mark(signal);
                    }catch(Exception e){Fail(e);yield break;}
                    yield return Wait(()=>Enumerable.Range(1,clients).All(i=>File.Exists(FileName("client"+i+"-"+signal))));if(failed)yield break;
                    Check(NetworkServer.active&&EveRegistrySession.Context.IsActive,"host remains consistent phase "+phase);
                }
                else
                {
                    yield return Wait(()=>File.Exists(FileName(signal)));if(failed)yield break;
                    bool isOwner=target.NetworkRole==PNMoveNetworkRole.Owner;
                    if(phase==0)yield return Wait(()=>Math.Abs(target.Body.position.x-20)<.2);
                    if(phase==1)yield return Wait(()=>!isOwner||target.OwnerMovement.Mirror.Scale==0);
                    if(phase==2)yield return Wait(()=>!isOwner||target.OwnerMovement.Mirror.Scale==1&&!target.Body.isKinematic);
                    if(phase==3)yield return Wait(()=>target.GetComponent<PNStatReplication>().View.Dead&&(!isOwner||target.OwnerMovement.Mirror.Dead&&target.Body.isKinematic));
                    if(phase==4)yield return Wait(()=>!target.GetComponent<PNStatReplication>().View.Dead&&(!isOwner||!target.OwnerMovement.Mirror.Dead&&!target.Body.isKinematic));
                    if(phase==5&&isOwner){target.enabled=false;yield return new WaitForSecondsRealtime(.2f);target.enabled=true;yield return Wait(()=>target.MovementBaselineApplied&&!target.MovementHeld&&target.NetworkRole==PNMoveNetworkRole.Owner);}
                    if(phase==6&&EveRegistrySession.Context.LocalOwnerId==1)yield return Wait(()=>manager.BindingToken(hostNetId)!=0&&manager.BindingToken(hostNetId)!=oldToken&&Players().Length==clients+1&&manager.ClientInputReady);
                    if(failed)yield break;
                    try{Check(died==0,"no client gameplay Died phase "+phase);Check(manager.ClientInputReady,"session input remains admitted phase "+phase);Mark(roleName+"-"+signal);}catch(Exception e){Fail(e);yield break;}
                }
            }
        }
        private IEnumerator FaultCase(bool host,PNNetworkManager manager,string fault)
        {
            if(host)
            {
                yield return Wait(()=>File.Exists(FileName("client1-joined")));
                if(fault=="required")
                {
                    try{originalMoveWriter=Writer<PNMovePacket>.write;Writer<PNMovePacket>.write=(w,p)=>{if(!corrupted&&p.Kind<=2){corrupted=true;w.WriteByte(255);return;}originalMoveWriter(w,p);};var p=Players().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId==0);var id=p.NetworkIdentity(EveRegistrySession.Context.SessionId);var model=manager.MovementAuthority.TypedModel(id);manager.MovementAuthority.Relocate(id,new MovementState(new MoveVector(model.State.Position.X+1,2,0),default),default);}catch(Exception e){Fail(e);yield break;}
                }
                Mark("fault-go");yield return Wait(()=>File.Exists(FileName("fault-client-done"))&&Players().Length==1);if(failed)yield break;
                try{Check(NetworkServer.active&&EveRegistrySession.Context.IsActive,"protocol failure is isolated from authoritative host");if(fault=="required")Check(corrupted,"actual writer emitted malformed required control");}catch(Exception e){Fail(e);yield break;}
            }
            else
            {
                yield return Wait(()=>File.Exists(FileName("fault-go")));if(failed)yield break;
                if(fault=="wrong-owner")
                {
                    try{var shell=FindObjectsByType<PNActorShell>().Single(x=>x.Metadata.Owner==0&&x.HasMove);var stream=new PNMoveStream{Scope=manager.BindingWorldScope(shell.netId),Session=shell.Metadata.HostSession,NetId=shell.netId,Entity=shell.Metadata.Entity,Generation=shell.Metadata.Generation,Nonce=(ulong)manager.BindingToken(shell.netId)};NetworkClient.Send(new PNMovePacket{Kind=4,Stream=stream,Request=1,Revision=1,Life=1,Report=new MovePoseReport(stream.Identity,1,1,1,1,new MovementState(new MoveVector(99,1,0),default))},Channels.Unreliable);}catch(Exception e){Fail(e);yield break;}
                }
                yield return Wait(()=>manager.HasEnded&&EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
                try{Check(!NetworkClient.isConnected,"required/owner protocol violation closes client");Check(manager.PendingMoveBytes==0&&manager.PendingMoveEvents==0,"failed stream releases reservations");Mark("fault-client-done");}catch(Exception e){Fail(e);yield break;}
            }
            PNGameBootstrap.End();yield return Wait(()=>EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
            File.WriteAllText(FileName(roleName+".txt"),"PASS "+checks+" P4 "+fault+" "+roleName);Application.Quit(0);
        }
    }
}
