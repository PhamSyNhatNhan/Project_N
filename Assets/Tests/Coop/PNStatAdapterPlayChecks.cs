using System;
using System.Collections;
using System.IO;
using System.Linq;
using EveEngine;
using Mirror;
using UnityEngine;

namespace ProjectN.Coop.Tests
{
    public sealed class PNStatAdapterPlayChecks:MonoBehaviour
    {
        private string folder,role;private int checks;private bool failed;
        private static string Arg(string prefix)=>Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith(prefix,StringComparison.Ordinal))?.Substring(prefix.Length);
        private void Awake(){if(Arg("--pn-stat-test=")!=null)PNGameBootstrap.Config.Network.Port=7788;}
        private void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Debug.Log("P2 PASS "+label);}
        private void Fail(Exception error){failed=true;Debug.LogException(error);File.WriteAllText(Path.Combine(folder,role+".txt"),"FAIL "+error);PNGameBootstrap.End();Application.Quit(1);}
        private IEnumerator Wait(Func<bool> condition)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+40;
            while(!failed)
            {
                bool ready=false;try{ready=condition();if(!ready&&Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("P2 "+role+" checks="+checks+" "+(NetworkManager.singleton as PNNetworkManager)?.LastFailure);}catch(Exception error){Fail(error);}
                if(ready||failed)yield break;yield return null;
            }
        }
        private IEnumerator Start()
        {
            folder=Arg("--pn-stat-test=");if(folder==null)yield break;
            role=PNGameBootstrap.Config.Role==EveSessionRole.Host?"host":"client";
            yield return Wait(()=>NetworkManager.singleton is PNNetworkManager);if(failed)yield break;
            var manager=(PNNetworkManager)NetworkManager.singleton;bool host=role=="host";
            if(host)
            {
                yield return Wait(()=>manager.PublishedCount==2);if(failed)yield break;
                try{var stat=FindObjectsByType<PNPlayerStat>().Single();stat.SetCurrent(PNContentIds.Health,0);stat.GetComponent<PNStatReplication>().CaptureState();File.WriteAllText(Path.Combine(folder,"host-ready"),"ready dead baseline");}catch(Exception e){Fail(e);yield break;}
            }
            yield return Wait(()=>manager.PublishedCount==3);if(failed)yield break;
            var target=FindObjectsByType<PNPlayerStat>().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId==0);
            var part=target.GetComponent<PNStatReplication>();
            var special=EveId.Parse("project_n:fixture_rage");
            if(!host)
            {
                yield return Wait(()=>part.View!=null&&part.View.Dead&&part.View.Resources[PNContentIds.Health].Current==0&&manager.ClientInputReady&&manager.Worlds.DefaultWorld!=null&&manager.ClientWorldReady(manager.Worlds.DefaultWorld.InstanceId));if(failed)yield break;
                try{Check(PNVitals.TryRead(target,PNContentIds.Health,out var hp,out _,out var dead)&&hp==0&&dead,"dead late-join baseline via PNVitals");Check(!target.IsDead,"replica leaves initialized PNStat unchanged");Check(part.View.Resources.ContainsKey(special),"content-defined special resource baseline");File.WriteAllText(Path.Combine(folder,"joined"),"ready");}catch(Exception e){Fail(e);yield break;}
            }
            else{yield return Wait(()=>File.Exists(Path.Combine(folder,"joined")));if(failed)yield break;}
            var fault=Arg("--pn-stat-fault=");
            if(fault!=null){yield return RunFault(host,manager,target,fault);yield break;}
            int died=0;target.Died+=_=>died++;
            for(int phase=0;phase<5;phase++)
            {
                string path=Path.Combine(folder,"phase"+phase);
                if(host)
                {
                    try
                    {
                        if(phase==0)target.SetCurrent(PNContentIds.Health,target.GetMax(PNContentIds.Health)*.5);
                        if(phase==1)target.SetSource(EveId.Parse("fixture:max"),new[]{new EveStatModifier(PNContentIds.Health,100d,0)});
                        if(phase==2)target.GetComponent<PNBuffManager>().Buffs.Coordinator.Submit(()=>target.ReceiveDamage(new PNDamage(EveRegistrySession.Context.SessionId,target.EntityId,1e12,new[]{PNContentIds.Physical})));
                        if(phase==3)target.SetCurrent(PNContentIds.Health,target.GetMax(PNContentIds.Health)*.25);
                        if(phase==4)target.RemoveSource(EveId.Parse("fixture:max"));
                        target.SetCurrent(special,phase+1);
                        var state=part.CaptureState();
                        // Publish the marker only after its contents are closed; another Player polls this path.
                        File.WriteAllText(path+".pending",string.Join("|",state.Resources[PNContentIds.Health].Current.ToString("R",System.Globalization.CultureInfo.InvariantCulture),state.Resources[PNContentIds.Health].Max.ToString("R",System.Globalization.CultureInfo.InvariantCulture),state.Dead.ToString(),state.LifeRevision.ToString()));
                        File.Move(path+".pending",path);
                    }catch(Exception e){Fail(e);yield break;}
                    yield return Wait(()=>File.Exists(path+"-ack"));if(failed)yield break;
                }
                else
                {
                    yield return Wait(()=>File.Exists(path));if(failed)yield break;
                    string[] expected=File.ReadAllText(path).Split('|');double hp=double.Parse(expected[0],System.Globalization.CultureInfo.InvariantCulture),max=double.Parse(expected[1],System.Globalization.CultureInfo.InvariantCulture);bool dead=bool.Parse(expected[2]);long life=long.Parse(expected[3]);
                    yield return Wait(()=>PNVitals.TryRead(target,PNContentIds.Health,out var current,out var maximum,out var isDead)&&Math.Abs(current-hp)<1e-9&&Math.Abs(maximum-max)<1e-9&&isDead==dead&&part.View.LifeRevision==life&&part.View.Resources[special].Current==phase+1);if(failed)yield break;
                    try{Check(died==0,"phase "+phase+" no client gameplay Died");Check(target.GetCurrent(PNContentIds.Health)!=hp,"phase "+phase+" independent client value view");File.WriteAllText(path+"-ack","ok");}catch(Exception e){Fail(e);yield break;}
                }
            }
            if(!host)
            {
                manager.NotifyApplicationPause(true);yield return new WaitForSecondsRealtime(.3f);manager.NotifyApplicationPause(false);
                yield return Wait(()=>manager.ClientInputReady);if(failed)yield break;
                try{Check(part.FullViewRevision>=1,"resume has coherent full revision");File.WriteAllText(Path.Combine(folder,"resumed"),"ok");}catch(Exception e){Fail(e);yield break;}
                // Exercise the foundation failure path outside manager BufferRequired.
                EveRegistrySession.Context.FailConnection("fixture","P2 external required failure");
                yield return Wait(()=>manager.HasEnded&&EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
            }
            else
            {
                yield return Wait(()=>File.Exists(Path.Combine(folder,"resumed"))&&manager.PublishedCount==2);if(failed)yield break;
                try{Check(died==1,"host combat death executes once");PNGameBootstrap.End();}catch(Exception e){Fail(e);yield break;}
                yield return Wait(()=>EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
            }
            File.WriteAllText(Path.Combine(folder,role+".txt"),"PASS "+checks+" P2 native KCP "+role);Application.Quit(0);
        }
        private IEnumerator RunFault(bool host,PNNetworkManager manager,PNPlayerStat target,string fault)
        {
            if(host)
            {
                if(fault=="host")manager.ProtocolFailure(new FormatException("Fixture authoritative projection failure"),true);
                else if(fault=="malformed")
                {
                    var shell=FindObjectsByType<PNActorShell>().Single(x=>x.Metadata.Entity==target.EntityId.Value);
                    var notice=new PNStatNotice{Scope=manager.BindingWorldScope(shell.netId),NetId=shell.netId,Session=shell.Metadata.HostSession,Entity=shell.Metadata.Entity,Generation=shell.Metadata.Generation,State=null};
                    foreach(var conn in NetworkServer.connections.Values)if(!(conn is LocalConnectionToClient))conn.Send(notice);
                }
                if(fault=="host")
                {
                    yield return Wait(()=>manager.HasEnded&&EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
                    try{Check(!NetworkServer.active,"authoritative failure closes host transport");PNGameBootstrap.End();}catch(Exception e){Fail(e);yield break;}
                }
                else
                {
                    yield return Wait(()=>File.Exists(Path.Combine(folder,"fault-client-done"))&&manager.PublishedCount==2);if(failed)yield break;
                    try{Check(NetworkServer.active&&EveRegistrySession.Context.IsActive,"client required failure preserves consistent host");PNGameBootstrap.End();}catch(Exception e){Fail(e);yield break;}
                    yield return Wait(()=>EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
                }
            }
            else
            {
                if(fault=="missing")
                {
                    manager.RequiredAdapters.Add("fixture:missing");manager.NotifyApplicationPause(true);
                    yield return new WaitForSecondsRealtime(.3f);manager.NotifyApplicationPause(false);
                    yield return new WaitForSecondsRealtime(.6f);
                    try{Check(!manager.ClientInputReady,"missing required adapter cannot settle");}catch(Exception e){Fail(e);yield break;}
                }
                yield return Wait(()=>manager.HasEnded&&EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
                try{Check(!NetworkClient.isConnected,"required failure closes client");File.WriteAllText(Path.Combine(folder,"fault-client-done"),"ok");PNGameBootstrap.End();}catch(Exception e){Fail(e);yield break;}
            }
            File.WriteAllText(Path.Combine(folder,role+".txt"),"PASS "+checks+" P2 KCP fault "+fault+" "+role);Application.Quit(0);
        }
    }
}
