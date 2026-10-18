using System;
using System.Collections;
using System.IO;
using System.Linq;
using EveEngine;
using Mirror;
using ProjectN.Coop;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace ProjectN.Coop.Tests
{
    // Disposable scene/Player verification, deliberately independent of the old protocol matrices.
    [DefaultExecutionOrder(-20000)]
    public sealed class PNFoundationPlayChecks:MonoBehaviour
    {
        private string output;
        private int checks;
        private bool failed;
        private Guid content;
        private double deadline;
        private Keyboard keyboard;
        private Action<NetworkWriter,PNAdmissionRequest> originalWriter;
        private bool sawInterrupted,sawResumed;
        private int commandExecutions;
        private static string Argument(string prefix)=>Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith(prefix,StringComparison.Ordinal))?.Substring(prefix.Length);
        private void Awake()
        {
            if(Environment.GetCommandLineArgs().Contains("--pn-foundation-s1"))InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            if(Environment.GetCommandLineArgs().Contains("--pn-peer-test"))PNGameBootstrap.Config.Network.Port=7788;
            if(Environment.GetCommandLineArgs().Contains("--pn-cap-fixture"))PNGameBootstrap.Config.Network.MaximumClients=1;
            string reject=Argument("--pn-reject=");if(reject==null||reject=="capacity")return;
            originalWriter=Writer<PNAdmissionRequest>.write;
            if(originalWriter==null)throw new Exception("Weaver runtime initializer did not register admission writer.");
            Writer<PNAdmissionRequest>.write=(writer,request)=>
            {
                switch(reject)
                {
                    case "version":request.NetworkVersion++;break;
                    case "platform":request.Platform="Mobile";break;
                    case "profile":request.Limits+="fixture";break;
                    case "rules":request.Content.Rules+="fixture";break;
                    case "pack":
                        int i=Array.FindIndex(request.Content.Hashes,x=>x.Key.StartsWith("pack:",StringComparison.Ordinal));
                        if(i<0)throw new Exception("Fixture requires a named content pack.");request.Content.Hashes[i].Value=new string('f',64);break;
                    default:throw new Exception("Unknown rejection fixture.");
                }
                originalWriter(writer,request);
            };
        }
        private void OnDestroy(){if(originalWriter!=null)Writer<PNAdmissionRequest>.write=originalWriter;}
        private void Update()
        {
            if(NetworkClient.isConnected&&!NetworkServer.active&&NetworkManager.singleton is PNNetworkManager manager)
            {if(!manager.ClientInputReady)sawInterrupted=true;else if(sawInterrupted)sawResumed=true;}
            if(!NetworkServer.active)return;
            foreach(var connection in NetworkServer.connections.Values)
                if(connection.authenticationData is PNPeer peer&&peer.Owner>0)
                {if(!peer.InputReady)sawInterrupted=true;else if(sawInterrupted)sawResumed=true;}
        }
        private IEnumerator Until(Func<bool> condition,double seconds)
        {
            deadline=Time.realtimeSinceStartupAsDouble+seconds;
            while(true)
            {
                bool waiting;
                try{waiting=Await(condition);}catch(Exception error){Fail(error);yield break;}
                if(!waiting)break;yield return null;
            }
        }

        private void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        private void Fail(Exception error)
        {failed=true;Write("FAIL "+error);Debug.LogException(error);Finish();}
        private void Write(string text)
        {Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,text);}
        private void Finish()
        {
            if(keyboard!=null){InputSystem.RemoveDevice(keyboard);keyboard=null;}
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit(failed?1:0);
#endif
        }
        private bool Await(Func<bool> condition)
        {
            if(condition())return false;
            if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Native stage timeout: "+checks+" "+(NetworkManager.singleton as PNNetworkManager)?.LastFailure);
            return true;
        }
        private IEnumerator Start()
        {
            if(Environment.GetCommandLineArgs().Any(x=>x.StartsWith("--pn-stat-test=")))yield break;
            output=Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith("--pn-result="))?.Substring(12)??".utmp/P1S4S10/Play.txt";
            yield return null;
            bool standalone=Environment.GetCommandLineArgs().Contains("--pn-peer-test");
            if(standalone){yield return RunPeer();yield break;}
            deadline=Time.realtimeSinceStartupAsDouble+20;
            Func<bool> offlineReady=()=>EveRegistrySession.IsReady&&EveRegistrySession.Context.Entities.Count==2&&FindObjectsByType<PNPlayerMove>().Length==1;
            while(true){bool waiting;try{waiting=Await(offlineReady);}catch(Exception e){Fail(e);yield break;}if(!waiting)break;yield return null;}
            PNPlayerMove move=null;Vector3 start=default;
            try
            {
                var runtime=PNGameBootstrap.Runtime;content=runtime.Content.GenerationId;
                Check(EveRegistrySession.Context.Role==EveSessionRole.Offline,"Offline entry");
                Check(FindObjectsByType<PNPlayerMove>().Length==1,"one factory player; all="+string.Join(";",FindObjectsByType<PNPlayerMove>(FindObjectsInactive.Include).Select(x=>x.name+":"+x.gameObject.activeInHierarchy+":"+x.transform.parent)));
                move=FindAnyObjectByType<PNPlayerMove>();start=move.transform.position;
                Check(move.PC!=null&&move.GetComponent<PNPlayerSkill>().PC!=null,"explicit input initialization");
                Check(move.isActiveAndEnabled&&move.GetComponent<EveEntityLoader>().IsReady,"activation after Ready");
                var cameraManager=FindObjectsByType<MonoBehaviour>().Single(x=>x.GetType().Name=="CameraManager");
                var camera=cameraManager.GetType().GetField("virtualCamera",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(cameraManager);
                Check(camera!=null&&ReferenceEquals(camera.GetType().GetProperty("Follow").GetValue(camera),move.transform),"factory event binds camera after scene entry");
                keyboard=InputSystem.AddDevice<Keyboard>();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D));
            }
            catch(Exception e){Fail(e);yield break;}
            yield return new WaitForSecondsRealtime(.25f);
            try
            {
                Check(move.transform.position.x>start.x+.01f,"actual factory Eve movement");InputSystem.RemoveDevice(keyboard);keyboard=null;
                var ui=PNGameBootstrap.Runtime.GetComponent<PNUiRuntime>();ui.OpenMenu();Check(ui.MenuOpen&&Time.timeScale==0&&!move.CanInput,"pause input");
                PNGameBootstrap.End();Check(!move.CanInput&&!move.gameObject.activeInHierarchy&&Time.timeScale==1,"immediate retirement and input unblock ownership");
            }
            catch(Exception e){Fail(e);yield break;}
            deadline=Time.realtimeSinceStartupAsDouble+20;
            while(true){bool waiting;try{waiting=Await(()=>EveRegistrySession.State==EveSessionState.Stopped);}catch(Exception e){Fail(e);yield break;}if(!waiting)break;yield return null;}
            try {Check(PNGameBootstrap.Runtime.Content.GenerationId==content,"content retained after Offline");var nativeHostConfig=new PNPlayConfig(EveSessionRole.Host);nativeHostConfig.Network.Port=7789;PNGameBootstrap.Begin(nativeHostConfig);}catch(Exception e){Fail(e);yield break;}
            deadline=Time.realtimeSinceStartupAsDouble+20;
            while(true){bool waiting;try{waiting=Await(()=>NetworkClient.localPlayer!=null&&((PNNetworkManager)NetworkManager.singleton).PublishedCount==2);}catch(Exception e){Fail(e);yield break;}if(!waiting)break;yield return null;}
            try
            {
                Check(FindObjectsByType<PNPlayerMove>().Length==1,"host-local no replica duplicate");
                Check(EveRegistrySession.Context.Entities.Count==2,"host directory matches published shells");
                Check(PNGameBootstrap.Runtime.Content.GenerationId==content,"Host reused loaded content");PNGameBootstrap.End();
            }
            catch(Exception e){Fail(e);yield break;}
            deadline=Time.realtimeSinceStartupAsDouble+20;
            while(true){bool waiting;try{waiting=Await(()=>EveRegistrySession.State==EveSessionState.Stopped);}catch(Exception e){Fail(e);yield break;}if(!waiting)break;yield return null;}
            yield return null;
            try {PNGameBootstrap.Begin(new PNPlayConfig(EveSessionRole.Offline));}catch(Exception e){Fail(e);yield break;}
            deadline=Time.realtimeSinceStartupAsDouble+20;
            while(true){bool waiting;try{waiting=Await(()=>EveRegistrySession.IsReady&&EveRegistrySession.Context.Entities.Count==2&&FindObjectsByType<PNPlayerMove>().Length==1);}catch(Exception e){Fail(e);yield break;}if(!waiting)break;yield return null;}
            try {Check(PNGameBootstrap.Runtime.Content.GenerationId==content&&FindObjectsByType<PNPlayerMove>().Length==1,"Offline after Host without content reload or leaked player");PNGameBootstrap.End();Write("PASS "+checks+" native factory/Offline/Host lifecycle checks");}
            catch(Exception e){Fail(e);yield break;}
            yield return Until(()=>EveRegistrySession.State==EveSessionState.Stopped,15);if(failed)yield break;
            Finish();
        }
        private IEnumerator RunPeer()
        {
            bool host=PNGameBootstrap.Config.Role==EveSessionRole.Host;
            string scenario=Argument("--pn-case=")??"basic",reject=Argument("--pn-reject=");
            PNNetworkManager manager=null;
            yield return Until(()=>NetworkManager.singleton is PNNetworkManager,15);if(failed)yield break;
            manager=(PNNetworkManager)NetworkManager.singleton;
            if(host&&scenario=="intent")EveRegistrySession.Context.Coop.RegisterCommand(EveId.Parse("fixture:probe"),(_,__)=>commandExecutions++);
            if(reject!=null)
            {
                yield return Until(()=>manager.HasEnded,20);if(failed)yield break;
                try
                {
                    Check(manager.PublishedCount==0&&!EveRegistrySession.IsReady,"rejected before actor enrollment");
                    string reason=manager.LastFailure??"";
                    if(reject!="capacity")Check(reason.IndexOf(reject=="pack"?"pack:":reject,StringComparison.OrdinalIgnoreCase)>=0,"useful rejection reason: "+reason);
                    PNGameBootstrap.End();Write("PASS "+checks+" real KCP admission rejection: "+reject+" reason="+reason);
                }
                catch(Exception error){Fail(error);yield break;}
                Finish();yield break;
            }
            int expected=scenario=="reject"||scenario=="shell"?2:3;
            yield return Until(()=>manager.PublishedCount>=expected&&(host?(scenario=="reject"||NetworkServer.connections.Count>=2):NetworkClient.localPlayer!=null),35);if(failed)yield break;
            long owner=0;
            try
            {
                Check(manager.PublishedCount==expected,"expected actor count");
                Check(EveRegistrySession.Context.Entities.Count==expected,"directory matches published bindings");
                if(host&&scenario!="reject")Check(NetworkServer.connections.Count>=2,"host plus remote connection");
                if(!host)
                {
                    owner=EveRegistrySession.Context.LocalOwnerId.GetValueOrDefault(-1);Check(owner>0&&EveRegistrySession.Context.Coop.AdmittedHostSession!=Guid.Empty,"admitted owner and host session");
                    var shell=FindObjectsByType<PNActorShell>().First();
                    Check(!manager.BufferRequired(shell.netId,manager.BindingToken(shell.netId)+1000,new byte[]{1},false)&&NetworkClient.isConnected,"stale callback is rejected without closing a healthy connection");
                    var stale=PNNetworkManager.CreateShell<PNActorShell>("Retired shell callback fixture");
                    typeof(NetworkIdentity).GetProperty("netId").SetValue(stale.GetComponent<NetworkIdentity>(),shell.netId);
                    manager.RemoveActor(stale.GetComponent<PNActorShell>());
                    Check(manager.PublishedCount==expected&&shell!=null,"retired shell instance cannot retire a newer binding with the same netId");Destroy(stale);
                }
                Write("READY role="+PNGameBootstrap.Config.Role+" actors="+manager.PublishedCount+" owner="+owner);
            }
            catch(Exception error){Fail(error);yield break;}
            if(!host&&scenario=="intent")
            {
                var session=EveRegistrySession.Context;var owned=session.Entities.Snapshot().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId==owner).GetComponent<EveEntityIdentity>();
                var shell=NetworkClient.localPlayer.GetComponent<PNSessionShell>();
                shell.CmdIntent(session.Coop.AdmittedHostSession,owned.Id.Value,owned.Generation,1,"fixture:probe","{}");
                yield return new WaitForSecondsRealtime(.3f);
                var other=session.Entities.Snapshot().First(x=>x.GetComponent<EveEntityIdentity>().OwnerId==0).GetComponent<EveEntityIdentity>();
                shell.CmdIntent(session.Coop.AdmittedHostSession,other.Id.Value,other.Generation,2,"fixture:probe","{}");
                yield return Until(()=>manager.HasEnded,15);if(failed)yield break;
                Check(!EveRegistrySession.IsReady,"forged target owner is disconnected");
            }
            if(!host&&(scenario=="grace"||scenario=="expiry"))
            {
                bool stateReady=false;manager.RequiredStateSettled=()=>stateReady;
                try{manager.NotifyApplicationPause(true);Check(!manager.ClientInputReady,"pause blocks input immediately");}catch(Exception error){Fail(error);yield break;}
                yield return new WaitForSecondsRealtime(.3f);
                manager.NotifyApplicationPause(false);
                yield return new WaitForSecondsRealtime(.3f);
                try{Check(!manager.ClientInputReady,"input waits for required-state barrier");}catch(Exception error){Fail(error);yield break;}
                if(scenario=="expiry")
                {
                    yield return Until(()=>manager.HasEnded,20);if(failed)yield break;
                    try{Check(!EveRegistrySession.IsReady,"unfinished catch-up cannot extend grace");}catch(Exception error){Fail(error);yield break;}
                }
                else
                {
                    stateReady=true;yield return Until(()=>manager.ClientInputReady,10);if(failed)yield break;
                    try{Check(EveRegistrySession.Context.LocalOwnerId==owner&&manager.PublishedCount==3,"same binding resumes without takeover/duplicate");Write("RESUMED owner="+owner);}catch(Exception error){Fail(error);yield break;}
                }
            }
            if(host&&scenario=="restart")
            {
                long first=NetworkServer.connections.Values.Where(x=>x.authenticationData is PNPeer p&&p.Owner>0).Select(x=>((PNPeer)x.authenticationData).Owner).Single();
                var firstLoader=EveRegistrySession.Context.Entities.Snapshot().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId==first);
                var controls=firstLoader.GetComponent<PNPlayerMove>().PC;long oldId=firstLoader.GetComponent<EveEntityIdentity>().Id.Value;
                yield return Until(()=>manager.PublishedCount==2&&NetworkServer.connections.Count==1&&EveRegistrySession.Context.Entities.Count==2&&manager.Scheduler.ResidentCount==2,20);if(failed)yield break;
                Write("RETIRED owner="+first);
                yield return Until(()=>manager.PublishedCount==3&&NetworkServer.connections.Values.Any(x=>x.authenticationData is PNPeer p&&p.Owner>first),35);if(failed)yield break;
                try
                {
                    Check(NetworkServer.connections.Values.Any(x=>x.authenticationData is PNPeer p&&p.Owner>first),"rejoin receives fresh monotonic owner");
                    var reused=EveRegistrySession.Context.Entities.Snapshot().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId>first);
                    Check(ReferenceEquals(firstLoader,reused)&&ReferenceEquals(controls,reused.GetComponent<PNPlayerMove>().PC)&&reused.GetComponent<EveEntityIdentity>().Id.Value>oldId,"player pool reuse retains one input owner and receives a fresh entity identity");
                }catch(Exception error){Fail(error);yield break;}
                yield return new WaitForSecondsRealtime(3);
            }
            else yield return new WaitForSecondsRealtime(scenario=="lost"||scenario=="hostlost"||scenario=="capacity"?(host?13:10):host?(scenario=="expiry"?17:4):1);
            try
            {
                if(host)
                {
                    Check(manager.PublishedCount==2&&EveRegistrySession.Context.Entities.Count==2,"owner departure retires only its player");
                    if(scenario=="grace")Check(sawInterrupted&&sawResumed,"host observed interruption and valid resume");
                    if(scenario=="expiry")Check(sawInterrupted&&!sawResumed,"expired catch-up retired without reopening input");
                    if(scenario=="lost")Check(sawInterrupted&&sawResumed,"missing process traffic triggers grace and valid resume without pause notice");
                    if(scenario=="intent")Check(commandExecutions==1,"only the authenticated owner's command executes once");
                }
                else if(scenario=="hostlost")Check(sawInterrupted&&sawResumed,"client blocks on missing host traffic and resumes the same binding");
                PNGameBootstrap.End();
            }
            catch(Exception error){Fail(error);yield break;}
            yield return Until(()=>EveRegistrySession.State==EveSessionState.Stopped,15);if(failed)yield break;
            Write("PASS "+checks+" real-process KCP "+scenario+" role="+(host?"Host":"Client"));Finish();
        }
    }
}
