using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProjectN.Coop.Tests
{
    // Actual process restart acceptance, activated only in an isolated verification Player.
    public sealed class PNWorldPersistencePlayChecks : MonoBehaviour
    {
        private string folder,step;private int checks;private PNGameSession game;private PNWorldSaveService saves;private Exception preparationFailure;
        private static string Arg(string key)=>Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith(key,StringComparison.Ordinal))?.Substring(key.Length);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Install()
        {
            var root=Arg("--pn-save-test=");if(root==null)return;
            if(Application.companyName!="ProjectNVerification"||Application.productName!="WorldS9Fixture")throw new Exception("Isolated persistence product required.");
            string from=Path.Combine(root,"NativeMods"),to=Path.Combine(Application.persistentDataPath,"Mods");
            foreach(var file in Directory.GetFiles(from,"*",SearchOption.AllDirectories)){var dest=Path.Combine(to,file.Substring(from.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(file,dest,true);}
            File.WriteAllText(Path.Combine(to,"enabled.json"),Arg("--pn-save-step=")=="reject-mod"?"{\"schemaVersion\":1,\"enabledMods\":[\"world_assets\"]}":Directory.Exists(Path.Combine(from,"native_probe"))?"{\"schemaVersion\":1,\"enabledMods\":[\"native_probe\",\"world_probe\",\"world_sandbox\",\"world_assets\"]}":"{\"schemaVersion\":1,\"enabledMods\":[\"world_sandbox\",\"world_assets\"]}");
        }
        private void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Debug.Log("Persistence PASS "+text);}
        private IEnumerator Wait(Func<bool> ready)
        {double until=Time.realtimeSinceStartupAsDouble+120;while(!ready()){if(preparationFailure!=null)throw preparationFailure;saves?.Tick();if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Persistence wait checks="+checks);yield return null;}}
        private IEnumerator Start()
        {
            folder=Arg("--pn-save-test=");if(folder==null)yield break;step=Arg("--pn-save-step=")??"write";
            var stack=new System.Collections.Generic.Stack<IEnumerator>();stack.Push(Run());Exception failed=null;
            while(stack.Count>0){var run=stack.Peek();bool more;try{more=run.MoveNext();}catch(Exception error){failed=error;break;}if(!more){stack.Pop();continue;}if(run.Current is IEnumerator nested){stack.Push(nested);continue;}yield return run.Current;}
            if(failed!=null){while(stack.Count>0)try{(stack.Pop() as IDisposable)?.Dispose();}catch(Exception cleanup){Debug.LogException(cleanup);}File.WriteAllText(Path.Combine(folder,step+".txt"),"FAIL "+failed);Debug.LogException(failed);Application.Quit(1);yield break;}
            try{PNGameBootstrap.End();File.WriteAllText(Path.Combine(folder,step+".txt"),"PASS "+checks+" actual cross-process persistence cases");Application.Quit(0);}catch(Exception error){File.WriteAllText(Path.Combine(folder,step+".txt"),"FAIL teardown "+error);Application.Quit(1);}
        }
        private IEnumerator Run()
        {
            yield return Wait(()=>EveRegistrySession.IsReady);game=PNGameBootstrap.Runtime.GetComponent<PNGameSession>();saves=new PNWorldSaveService(game.Session,game.Worlds,game.Lifetimes,Path.Combine(folder,"Saves"));
            if(step=="bundle-fault")
            {
                string path=Path.Combine(Application.persistentDataPath,"Mods","world_assets","world-scene");var bytes=File.ReadAllBytes(path);EveWorldContext failedWorld=null;
                try{var changed=(byte[])bytes.Clone();changed[0]^=1;File.WriteAllBytes(path,changed);failedWorld=game.Worlds.Create(EveId.Parse("world_assets:scene"));yield return Wait(()=>failedWorld.State==EveWorldState.Failed);Check(failedWorld.FirstFailure!=null,"changed selected bundle fails before readiness");yield return Wait(()=>game.Worlds.Unload(failedWorld));Check(game.WorldAssets.LeaseReferences==0&&game.WorldAssets.LoadedCount==0,"failed bundle verification releases all acquisition ownership");}
                finally{File.WriteAllBytes(path,bytes);}
                yield break;
            }
            if(step=="reject-mod"||step=="reject-schema")
            {
                int worlds=game.Session.Worlds.Snapshot().Count;var store=new PNWorldFileStore(Path.Combine(folder,"Saves"));var original=store.Read("generated");string key="generated";
                if(step=="reject-schema"){var data=original.Envelope.Document;((Newtonsoft.Json.Linq.JObject)data["providers"]).Properties().First().Value=999;key="bad-schema";store.Write(key,new PNWorldSaveEnvelope(data.ToString()));}
                var rejected=saves.RestoreSaved(key);yield return Wait(()=>rejected.IsTerminal);Check(rejected.State==PNWorldSaveState.Failed&&game.Session.Worlds.Snapshot().Count==worlds,"incompatible content/schema rejected before staged creation: "+rejected.Error);Check(store.Read("generated").Revision==original.Revision&&saves.ReservedBytes==0,"failed import preserves committed source and releases bytes");yield break;
            }
            bool writing=step=="write";var types=new[]{"world_sandbox:generated","world_assets:scene"};
            foreach(var type in types)
            {
                var key=type.StartsWith("world_assets",StringComparison.Ordinal)?"bundle":"generated";EveWorldContext world;
                if(writing)
                {
                    world=game.Worlds.Create(EveId.Parse(type));yield return Wait(()=>world.CanExecute||world.State==EveWorldState.Failed);Check(world.CanExecute,"selected external provider loads "+key+": "+world.Failure);
                    foreach(var request in ((PNEntityFactory)game.Factory).SceneEntities(world)){var local=request;var candidate=game.Scheduler.Enqueue(()=>game.Factory.Get(local),l=>game.Factory.Configure(l,local),game.Factory.Return,game.Factory.Published,e=>preparationFailure=e);game.Scheduler.AssociateWorld(candidate,world);}
                    yield return Wait(()=>game.Session.Entities.Snapshot(world).Count==2);
                    var actors=game.Session.Entities.Snapshot(world);var stat=actors[0].GetComponent<PNStat>();stat.SetCurrent(PNContentIds.Health,stat.GetMax(PNContentIds.Health)*.25);
                    if(key=="generated"){var component=actors[0].GetComponents<Component>().First(c=>c!=null&&c.GetType().FullName=="WorldSandboxMod.ActorData");component.GetType().GetField("Value").SetValue(component,111);}
                    world.AdvanceClock(3.25);var snapshot=game.Lifetimes.CaptureSnapshot(world);yield return Wait(()=>snapshot.IsTerminal);Check(snapshot.State==PNWorldLifetimeState.Captured,"portable capture "+key+": "+snapshot.Error);
                    File.WriteAllText(Path.Combine(folder,key+"-old-world"),world.InstanceId.ToString());
                    var saved=saves.SaveCheckpoint(key,snapshot.Snapshot);yield return Wait(()=>saved.IsTerminal);Check(saved.State==PNWorldSaveState.Saved&&saved.Revision>0,"disk committed "+key+": "+saved.Error);
                    yield return Wait(()=>game.Worlds.Unload(world));
                }
                else
                {
                    var loaded=saves.RestoreSaved(key);yield return Wait(()=>loaded.IsTerminal);Check(loaded.State==PNWorldSaveState.Completed,"restart restore "+key+": "+loaded.Error);world=loaded.Restore.World;
                    Check(world.InstanceId.ToString()!=File.ReadAllText(Path.Combine(folder,key+"-old-world")),"runtime world identity is fresh across process "+key);
                    var actors=game.Session.Entities.Snapshot(world);Check(actors.Count==2&&actors.Any(a=>Math.Abs(a.GetComponent<PNStat>().GetCurrent(PNContentIds.Health)/a.GetComponent<PNStat>().GetMax(PNContentIds.Health)-.25)<.0001),"NPC count and HP restored "+key);
                    Check(world.GameplayTime>=3.25,"world clock restored "+key);
                    if(key=="generated")
                    {
                        var data=actors.Select(a=>a.GetComponents<Component>().First(c=>c!=null&&c.GetType().FullName=="WorldSandboxMod.ActorData")).ToArray();
                        Check(data.Any(c=>(int)c.GetType().GetField("Value").GetValue(c)==111)&&data.All(c=>actors.Contains((EveEntityLoader)c.GetType().GetField("Linked").GetValue(c))),"external DLL private data and references remapped");
                    }
                    game.Local.Select(world,actors[0]);yield return null;yield return null;
                    yield return Wait(()=>PNViewCamera.Current!=null&&PNViewCamera.Current.cullingMask!=0&&(PNTransitionController.Current==null||!PNTransitionController.Current.IsShowing));
                    yield return Capture(key+"-"+step);game.Local.Select(game.Worlds.DefaultWorld,game.Session.Entities.Snapshot(game.Worlds.DefaultWorld).FirstOrDefault(a=>PNEntityRoles.IsPlayer(a)));
                    var second=saves.SaveResident(key,world,true);yield return Wait(()=>second.IsTerminal);Check(second.State==PNWorldSaveState.Saved&&world.State==EveWorldState.Disposed,"resave/unload committed "+key+": "+second.Error);
                }
            }
            Check(saves.PendingCount==0&&saves.ReservedBytes==0&&game.Lifetimes.StoredBytes==0&&game.WorldAssets.LeaseReferences==0&&game.WorldAssets.LoadedCount==0,"save/import and shared bundle ownership drained");
        }
        private IEnumerator Capture(string name)
        {
            var camera=PNViewCamera.Current;Check(camera!=null,"view output available");var rt=new RenderTexture(800,600,24);var texture=new Texture2D(800,600,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.StandardRequest{destination=rt});RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,800,600),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),texture.EncodeToPNG());File.WriteAllText(Path.Combine(folder,name+"-camera.txt"),"mask="+camera.cullingMask+" pose="+camera.transform.position+" rotation="+camera.transform.rotation+" world="+game.Local.World.InstanceId);}
            finally{RenderTexture.active=previous;Destroy(texture);Destroy(rt);}yield return null;
        }
    }
}
