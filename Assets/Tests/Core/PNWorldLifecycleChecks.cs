#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PNWorldFixtureStat : PNEnemyStat { public override string CharacterDefinitionId => "project_n:dummy"; }
public sealed class PNWorldFixtureProvider : IPNWorldProvider
{
    public static bool InjectLoadFailure,RepairCleanup=true;
    public IEnumerator Load(PNWorldBuildContext context)
    {
        bool fault=InjectLoadFailure;
        if(fault)context.Own(()=>{if(!RepairCleanup)throw new InvalidOperationException("Injected world resource cleanup failure");});
        for(int i=0;i<2;i++)
        {
            var root=new GameObject("World fixture content "+i);SceneManager.MoveGameObjectToScene(root,context.PhysicsScene);
            context.Own(()=>{if(root!=null)UnityEngine.Object.DestroyImmediate(root);});
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(root.transform);floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(50,1,50);
            var spawn=new GameObject("spawn").AddComponent<PNPlayerSpawnPoint>();spawn.transform.SetParent(root.transform);spawn.transform.position=new Vector3(i*4,1,0);
            spawn.Prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Character/Eve/Prefab/Eve.prefab");spawn.Ground=floor.GetComponent<Collider>();
            var actor=new GameObject("authored");actor.SetActive(false);actor.transform.SetParent(root.transform);
            actor.AddComponent<EveEntityIdentity>();actor.AddComponent<PNWorldFixtureStat>();actor.AddComponent<EveEntityLoader>();actor.AddComponent<EveSceneEntityKey>().Configure("fixture:scene","dummy");
            context.AddSlot("chunk-"+i,context.PhysicsScene,new[]{root});yield return null;
            if(fault)throw new InvalidOperationException("Injected partial world load failure");
        }
    }
}
public sealed class PNWorldFixtureModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    { hub.GetOrCreate<PNWorldDefinition>().Register(new PNWorldDefinition(EveId.Parse("fixture:world"),new string('a',64),typeof(PNWorldFixtureProvider),()=>new PNWorldFixtureProvider())); }
}
public static class PNWorldLifecycleChecks
{
    public static string Run()
    {
        if(Application.isPlaying||EveRegistrySession.State!=EveSessionState.Stopped||SceneManager.GetActiveScene().rootCount!=0)throw new InvalidOperationException("Empty stopped disposable clone required.");
        int count=0;var checks=new List<string>();
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add("PASS "+label);Debug.Log("WorldS2 PASS "+label);count++;}
        void Reject(Action action,string label){try{action();}catch{Check(true,label);return;}throw new Exception(label);}
        var docs=new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),".utmp/no-mods",Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule(),new PNWorldFixtureModule()},docs);
        EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Host,EveContentCompatibility.Create(hub,docs,"fixture:world"));
        var session=EveRegistrySession.Context;
        var scheduler=new EveEntityPreparationScheduler(session,new EvePreparationProfile());
        var worlds=new PNWorldRuntime(session,scheduler);var factory=new PNPlayerFactory(session,worlds);
        void Pump(Func<bool> until){for(int i=0;i<1000&&!until();i++)scheduler.AdvanceFrame();if(!until())throw new Exception("Fixture did not drain");}
        try
        {
            var a=worlds.Create(EveId.Parse("fixture:world"));var b=worlds.Create(EveId.Parse("fixture:world"));
            Pump(()=>a.State==EveWorldState.Ready&&b.State==EveWorldState.Ready);
            Check(a.InstanceId!=b.InstanceId&&worlds.Get(a).PhysicsScene!=worlds.Get(b).PhysicsScene,"same definition creates independent instances");
            var ar=factory.SceneEntities(a).ToArray();var br=factory.SceneEntities(b).ToArray();
            Check(ar.Length==2&&br.Length==2&&ar[0].SceneKey==br[0].SceneKey&&ar[0].EntityKey==br[0].EntityKey,"same authored keys allowed across worlds and content slots");
            var ap=factory.Player(0,a,"chunk-0","player");var bp=factory.Player(1,b,"chunk-1","player");
            var al=factory.Get(ap);factory.Configure(al,ap);var bl=factory.Get(bp);factory.Configure(bl,bp);
            Check(al.gameObject.scene==worlds.Get(a).PhysicsScene&&bl.gameObject.scene==worlds.Get(b).PhysicsScene,"explicit factory selects each world spawn and physics owner");
            factory.Return(al);factory.Return(bl);
            Reject(()=>factory.Player(0,a,null,null),"multiple content slots require explicit selection");
            var stale=factory.Player(0,a,"chunk-0","player");stale.WorldIncarnation++;
            Reject(()=>factory.Get(stale),"factory rejects stale world incarnation");
            var candidates=new List<EveEntityPreparationScheduler.Candidate>();
            foreach(var request in ar.Concat(br))
            {
                var candidate=scheduler.Enqueue(()=>factory.Get(request),l=>{factory.Configure(l,request);l.GetComponent<EveEntityIdentity>().Assign(session.Entities.AllocateHostId(),EveId.Parse(request.Character),0);},factory.Return,factory.Published,e=>{throw new Exception("Unexpected enrollment",e);});
                scheduler.AssociateWorld(candidate,request.World);candidates.Add(candidate);
            }
            Pump(()=>session.Entities.Snapshot().Count==4);
            Check(session.Entities.Snapshot(a).Count==2&&session.Entities.Snapshot(b).Count==2,"scoped world index keeps globally unique entity IDs");
            var lease=a.AcquireLease("fixture join");Check(!worlds.Unload(a),"world unload waits for explicit join lease");lease.Dispose();
            Pump(()=>candidates.Take(2).All(x=>x.IsReturned));Check(worlds.Unload(a),"world A drains entities and resources");
            Check(session.Entities.Snapshot(b).Count==2&&bl!=null&&b.State==EveWorldState.Ready,"unload A preserves world B actors and pool");
            var again=factory.Get(bp);factory.Configure(again,bp);factory.Return(again);Check(ReferenceEquals(again,bl),"B reuses its own retained pool after A unload");
            PNWorldFixtureProvider.InjectLoadFailure=true;PNWorldFixtureProvider.RepairCleanup=false;
            var failed=worlds.Create(EveId.Parse("fixture:world"));Pump(()=>failed.State==EveWorldState.Failed);
            Check(b.State==EveWorldState.Ready,"partial load failure isolated from B");
            Reject(()=>worlds.Unload(failed),"failed resource cleanup remains owned for retry");
            Check(session.Worlds.TryGet(failed.InstanceId,failed.Incarnation,out _)&&failed.PendingCleanup>0,"failed world retains directory identity and cleanup");
            PNWorldFixtureProvider.RepairCleanup=true;Check(worlds.Unload(failed),"world cleanup retry succeeds after fault repair");
            var io=session.Worlds.Create(EveId.Parse("fixture:pending"),new string('c',64));bool ioReady=false;io.BeginLoading();io.CompleteLoading();
            io.RegisterCleanup(()=>{if(!ioReady)throw new EveWorldCleanupPendingException("Fixture noncancelable scene IO pending");});
            Reject(()=>session.Worlds.Remove(io),"pending scene IO retains cleanup without pretending completion");
            Check(io.State==EveWorldState.Unloading&&io.Failure==null&&session.Worlds.TryGet(io.InstanceId,io.Incarnation,out _),"pending IO is Unloading rather than a runtime fault");
            ioReady=true;session.Worlds.Remove(io);Check(io.State==EveWorldState.Disposed,"pending scene cleanup completes after IO drains");
            Reject(()=>session.Worlds.Create(a.InstanceId,a.Incarnation,a.DefinitionId,a.ContentHash),"disposed world rejects stale incarnation reuse");
            Pump(()=>{worlds.Unload(b);return candidates.All(x=>x.IsReturned);});worlds.Unload(b);
            Check(session.Entities.Count==0&&session.Worlds.Snapshot().Count==0,"all scoped entity and world ownership drained");
        }
        catch(Exception error){Debug.LogException(error);throw;}
        finally
        {
            PNWorldFixtureProvider.InjectLoadFailure=false;PNWorldFixtureProvider.RepairCleanup=true;
            factory.Dispose();EveRegistrySession.End();
        }
        return "PASS "+count+" world lifecycle cases\n"+string.Join("\n",checks);
    }
}
#endif
