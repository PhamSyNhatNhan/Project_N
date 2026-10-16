using System;
using System.Collections;
using System.IO;
using System.Linq;
using EveEngine;
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ProjectN.Coop.Tests
{
    // Dedicated disposable Player fixture. Content is a real independently compiled DLL mod.
    [DefaultExecutionOrder(-20000)]
    public sealed class PNBuffAdapterPlayChecks:MonoBehaviour
    {
        private string folder,name;private bool failed;private int checks;
        private Action<NetworkWriter,PNBuffPacket> originalPacketWriter;private bool corruptedOptional;
        private void OnDestroy(){if(originalPacketWriter!=null)Writer<PNBuffPacket>.write=originalPacketWriter;}
        private static string Arg(string prefix)=>Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith(prefix,StringComparison.Ordinal))?.Substring(prefix.Length);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void InstallFixtureMod()
        {
            string output=Arg("--pn-buff-test=");if(output==null)return;
            if(Application.companyName!="ProjectNVerification"||!Application.productName.StartsWith("P3Fixture",StringComparison.Ordinal))throw new InvalidOperationException("P3 DLL fixture requires isolated Player product storage.");
            string mods=Path.Combine(Application.persistentDataPath,"Mods");string from=Path.Combine(output,"NativeMods");
            foreach(string file in Directory.GetFiles(from,"*",SearchOption.AllDirectories)){string to=Path.Combine(mods,file.Substring(from.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(to));if(!File.Exists(to)||!File.ReadAllBytes(to).SequenceEqual(File.ReadAllBytes(file)))File.Copy(file,to,true);}
            File.WriteAllText(Path.Combine(mods,"enabled.json"),"{\"schemaVersion\":1,\"enabledMods\":[\"native_probe\"]}");
        }
        private void Awake(){if(Arg("--pn-buff-test=")!=null)PNGameBootstrap.Config.Network.Port=7793;}
        private void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Debug.Log("P3 PASS "+name+" "+message);}
        private void CaptureWorld(string path)
        {
            var camera=Camera.main??FindObjectsByType<Camera>().First(x=>x.enabled);var previous=camera.targetTexture;var active=RenderTexture.active;
            var texture=new RenderTexture(960,540,24);Texture2D output=null;
            try{texture.Create();camera.targetTexture=texture;camera.Render();RenderTexture.active=texture;output=new Texture2D(960,540,TextureFormat.RGB24,false);output.ReadPixels(new Rect(0,0,960,540),0,0);output.Apply();Check(output.GetPixels32().Count(x=>x.r>20||x.g>20||x.b>20)>100,"actual offscreen scene pixels");File.WriteAllBytes(path,output.EncodeToPNG());}
            finally{camera.targetTexture=previous;RenderTexture.active=active;if(output!=null)Destroy(output);texture.Release();Destroy(texture);}
        }
        private void Fail(Exception error){failed=true;Debug.LogException(error);File.WriteAllText(Path.Combine(folder,name+".txt"),"FAIL "+error);try{PNGameBootstrap.End();}catch{}Application.Quit(1);}
        private IEnumerator Wait(Func<bool> condition)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+120;
            while(!failed){bool ready=false;try{ready=condition();if(!ready&&Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("P3 "+name+" checks="+checks+" "+(NetworkManager.singleton as PNNetworkManager)?.LastFailure);}catch(Exception error){Fail(error);}if(ready||failed)yield break;yield return null;}
        }
        private IEnumerator Start()
        {
            folder=Arg("--pn-buff-test=");if(folder==null)yield break;
            bool host=PNGameBootstrap.Config.Role==EveSessionRole.Host;name=host?"host":Arg("--pn-buff-client=")??"client1";
            int clients=int.Parse(Arg("--pn-buff-clients=")??"1");
            yield return Wait(()=>NetworkManager.singleton is PNNetworkManager m&&m.PublishedCount>=2);if(failed)yield break;
            var manager=(PNNetworkManager)NetworkManager.singleton;
            var id=EveId.Parse("native_probe:buff_probe");PNBuffManager buffs=null;
            if(host)
            {
                try
                {
                    buffs=FindObjectsByType<PNPlayerStat>().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId==0).GetComponent<PNBuffManager>();
                    for(int i=0;i<12;i++)buffs.Buffs.Apply(new PNBuffApplyRequest(id,new PNBuffSource(EveRegistrySession.Context.SessionId,buffs.GetComponent<PNStat>().EntityId)));
                    Check(buffs.Buffs.Snapshot().Count==12&&buffs.Replica.PresenterCount==12,"real DLL custom Buff and presenter on host");
                    File.WriteAllText(Path.Combine(folder,"host-ready"),"ready");
                }catch(Exception error){Fail(error);yield break;}
            }
            else
            {
                yield return Wait(()=>manager.PublishedCount>=3&&manager.ClientInputReady);if(failed)yield break;
                try
                {
                    buffs=FindObjectsByType<PNPlayerStat>().Single(x=>x.GetComponent<EveEntityIdentity>().OwnerId==0).GetComponent<PNBuffManager>();
                    Check(buffs.HasBaseline&&buffs.Replica.Snapshot().Count==12,"late join atomic multi-chunk baseline");
                    Check(buffs.Replica.Snapshot().All(x=>x.Stacks==null&&x.Expiration==null&&x.DynamicData["custom"][2].Value<long>()==long.MaxValue),"custom payload/no stacks/no expiry/Int64");
                    Check(buffs.Buffs.Snapshot().Count==0,"client never constructs gameplay Buff instances");
                    Check(buffs.Replica.PresenterCount==12,"DLL presenter instantiated remotely");
                    CaptureWorld(Path.Combine(folder,name+"-baseline.png"));
                    File.WriteAllText(Path.Combine(folder,name+"-joined"),"ready");
                }catch(Exception error){Fail(error);yield break;}
            }
            if(host){yield return Wait(()=>Enumerable.Range(1,clients).All(i=>File.Exists(Path.Combine(folder,"client"+i+"-joined"))));if(failed)yield break;}
            if(Arg("--pn-buff-fault=")=="required"||Arg("--pn-buff-fault=")=="duplicate"){yield return RequiredFailure(host,manager,buffs);yield break;}
            if(!host)
            {
                manager.RequiredAdapters.Add("fixture:missing");manager.NotifyApplicationPause(true);yield return new WaitForSecondsRealtime(.2f);manager.NotifyApplicationPause(false);yield return new WaitForSecondsRealtime(.6f);
                try{Check(!manager.ClientInputReady,"missing required adapter keeps input blocked");manager.RequiredAdapters.Remove("fixture:missing");}catch(Exception error){Fail(error);yield break;}
                yield return Wait(()=>manager.ClientInputReady);if(failed)yield break;
            }
            int cues=0;buffs.Replica.CuePresented+=cue=>{if(cue.Name=="probe_end")cues++;};
            var burn=EveId.Parse("project_n:burn");
            for(int phase=0;phase<7;phase++)
            {
                string key=Path.Combine(folder,"phase"+phase);
                if(host)
                {
                    try
                    {
                        if(phase==0)
                        {
                            originalPacketWriter=Writer<PNBuffPacket>.write;
                            Writer<PNBuffPacket>.write=(writer,packet)=>{if(packet.Kind==2&&!corruptedOptional){packet.Body=new byte[]{255};packet.Total=packet.Count=1;packet.Index=0;corruptedOptional=true;}originalPacketWriter(writer,packet);};
                            foreach(var item in buffs.Buffs.Snapshot())buffs.Buffs.Trigger(item.Handle);
                        }
                        if(phase==1)buffs.enabled=false;
                        if(phase==2)buffs.enabled=true;
                        if(phase==3)foreach(var item in buffs.Buffs.Snapshot())buffs.Buffs.Remove(item.Handle);
                        if(phase==4)buffs.Buffs.Apply(new PNBuffApplyRequest(id,new PNBuffSource(EveRegistrySession.Context.SessionId,buffs.GetComponent<PNStat>().EntityId)));
                        if(phase==5)buffs.Buffs.Apply(new PNBuffApplyRequest(burn,PNBuffSource.Capture(buffs.GetComponent<PNStat>())));
                        if(phase==6)buffs.Buffs.RemoveMatching(x=>x.Id==burn,PNBuffEndReason.Dispelled);
                        File.WriteAllText(key,"ready");
                    }catch(Exception error){Fail(error);yield break;}
                    yield return Wait(()=>Enumerable.Range(1,clients).All(i=>File.Exists(key+"-client"+i)));if(failed)yield break;
                    if(phase==0){try{Check(corruptedOptional,"malformed optional payload injected through actual writer");}catch(Exception error){Fail(error);yield break;}}
                }
                else
                {
                    yield return Wait(()=>File.Exists(key));if(failed)yield break;
                    yield return Wait(()=>phase==0?buffs.Replica.Snapshot().Count==12&&buffs.Replica.Snapshot().All(x=>x.DynamicData["counter"].Value<int>()==1):phase==1?buffs.Replica.Snapshot().Count==12&&buffs.Replica.Snapshot().All(x=>x.Suspended)&&buffs.Replica.PresenterCount==0:phase==2?buffs.Replica.PresenterCount==12:phase==3?buffs.Replica.Snapshot().Count==0:phase==5?buffs.Replica.Snapshot().Count==2&&buffs.Replica.Snapshot().Any(x=>x.BuffId==burn)&&buffs.Replica.PresenterCount==2:buffs.Replica.Snapshot().Count==1);if(failed)yield break;
                    try{Check(buffs.Buffs.Snapshot().Count==0,"phase "+phase+" replica does not mutate gameplay");if(phase==3)Check(cues==12,"ending cues before removal exactly once");}catch(Exception error){Fail(error);yield break;}
                    if(phase==5)
                    {yield return new WaitForEndOfFrame();try{CaptureWorld(Path.Combine(folder,name+"-burn.png"));}catch(Exception error){Fail(error);yield break;}}
                    File.WriteAllText(key+"-"+name,"ok");
                }
            }
            if(!host)
            {
                File.WriteAllText(Path.Combine(folder,name+"-optional-drops"),manager.DroppedOptionalBuffCues.ToString(System.Globalization.CultureInfo.InvariantCulture));
                manager.NotifyApplicationPause(true);yield return new WaitForSecondsRealtime(.3f);manager.NotifyApplicationPause(false);
                yield return Wait(()=>manager.ClientInputReady);if(failed)yield break;
                try{Check(buffs.HasBaseline&&buffs.Replica.Snapshot().Count==1,"Stat+Buff resume coherent current state");Check(buffs.Replica.PresenterCount==1,"refresh preserves selected presenter");Check(manager.PendingBuffBytes==0&&manager.PendingBuffEvents==0,"resume releases pending transfer/DOM/journal accounting");File.WriteAllText(Path.Combine(folder,name+"-resumed"),"ok");}catch(Exception error){Fail(error);yield break;}
                // Let host observe both peer resumes before either is retired.
                yield return Wait(()=>File.Exists(Path.Combine(folder,"all-resumed")));if(failed)yield break;
                PNGameBootstrap.End();yield return Wait(()=>EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
            }
            else
            {
                yield return Wait(()=>Enumerable.Range(1,clients).All(i=>File.Exists(Path.Combine(folder,"client"+i+"-resumed"))));if(failed)yield break;
                try{Check(Enumerable.Range(1,clients).Sum(i=>long.Parse(File.ReadAllText(Path.Combine(folder,"client"+i+"-optional-drops"))))==1,"malformed optional isolated without disconnect or required state loss");}catch(Exception error){Fail(error);yield break;}
                File.WriteAllText(Path.Combine(folder,"all-resumed"),"ok");
                yield return Wait(()=>manager.PublishedCount==2);if(failed)yield break;
                PNGameBootstrap.End();yield return Wait(()=>EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
            }
            File.WriteAllText(Path.Combine(folder,name+".txt"),"PASS "+checks+" P3 actual KCP/DLL "+name);Application.Quit(0);
        }
        private IEnumerator RequiredFailure(bool host,PNNetworkManager manager,PNBuffManager buffs)
        {
            if(host)
            {
                try
                {
                    originalPacketWriter=Writer<PNBuffPacket>.write;
                    Writer<PNBuffPacket>.write=(writer,packet)=>{if(packet.Kind==1&&!corruptedOptional){if(Arg("--pn-buff-fault=")=="duplicate")packet.Revision--;else{packet.Body=new byte[]{255};packet.Total=packet.Count=1;packet.Index=0;}corruptedOptional=true;}originalPacketWriter(writer,packet);};
                    buffs.Buffs.Trigger(buffs.Buffs.Snapshot().First().Handle);
                }catch(Exception error){Fail(error);yield break;}
                yield return Wait(()=>File.Exists(Path.Combine(folder,"required-client-done"))&&manager.PublishedCount==2);if(failed)yield break;
                try{Check(corruptedOptional&&NetworkServer.active&&EveRegistrySession.Context.IsActive,"required client failure preserves authoritative host");PNGameBootstrap.End();}catch(Exception error){Fail(error);yield break;}
                yield return Wait(()=>EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
            }
            else
            {
                yield return Wait(()=>manager.HasEnded&&EveRegistrySession.State==EveSessionState.Stopped);if(failed)yield break;
                try{Check(!NetworkClient.isConnected,"malformed required Buff closes client transport");Check(manager.PendingBuffBytes==0&&manager.PendingBuffEvents==0,"required failure releases assemblies and DOM reservations");File.WriteAllText(Path.Combine(folder,"required-client-done"),"ok");}catch(Exception error){Fail(error);yield break;}
            }
            File.WriteAllText(Path.Combine(folder,name+".txt"),"PASS "+checks+" P3 KCP required fault "+name);Application.Quit(0);
        }
    }
}
