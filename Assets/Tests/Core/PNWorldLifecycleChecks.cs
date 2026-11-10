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
public sealed class PNWorldFixtureProvider : IPNWorldProvider,IPNPortableWorldProvider
{
    public Newtonsoft.Json.Linq.JToken CaptureLoadDescriptor(PNWorldBuildContext context)=>new Newtonsoft.Json.Linq.JObject{["fixture"]=1};
    public void ValidateLoadDescriptor(Newtonsoft.Json.Linq.JToken descriptor){if((int?)descriptor["fixture"]!=1)throw new FormatException("Fixture descriptor mismatch.");}
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
            actor.AddComponent<EveEntityIdentity>();actor.AddComponent<PNWorldFixtureStat>();actor.AddComponent<EveEntityLoader>();if(i==0)actor.AddComponent<PNSceneEntityRole>().Configure("project_n:enemy");actor.AddComponent<EveSceneEntityKey>().Configure("fixture:scene","dummy");
            context.AddSlot("chunk-"+i,context.PhysicsScene,new[]{root});yield return null;
            if(fault)throw new InvalidOperationException("Injected partial world load failure");
        }
    }
}
public sealed class PNFixtureProbe : MonoBehaviour { }
public sealed class PNFixtureEnemyProbe : MonoBehaviour { }
public sealed class PNWorldFixtureModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    {
        hub.GetOrCreate<PNWorldDefinition>().Register(new PNWorldDefinition(EveId.Parse("fixture:world"),new string('a',64),typeof(PNWorldFixtureProvider),()=>new PNWorldFixtureProvider()));
        foreach(var feature in new[]{"fixture:feature_mode","fixture:feature_a","fixture:feature_b"})hub.WorldFeatures().Register(new PNWorldFeatureDefinition(EveId.Parse(feature)));
        hub.GetOrCreate<PNWorldDefinition>().Register(new PNWorldDefinition(EveId.Parse("fixture:world_featured"),new string('b',64),typeof(PNWorldFixtureProvider),()=>new PNWorldFixtureProvider(),
            new PNWorldPolicy(PNEmptyWorldPolicy.Continue,null,null,null,default,new[]{EveId.Parse("fixture:feature_a")})));
        // entity modules (E3): every player in a world with feature_a carries PNFixtureProbe; an entity with the registered Enemy role carries PNFixtureEnemyProbe
        hub.EntityModules().Register(new PNEntityModuleDefinition(EveId.Parse("fixture:player_probe"),PNEntityModuleRoles.Player,null,new[]{EveId.Parse("fixture:feature_a")},new[]{typeof(PNFixtureProbe)}));
        hub.EntityModules().Register(new PNEntityModuleDefinition(EveId.Parse("fixture:enemy_probe"),PNEntityModuleRoles.None,new[]{EveId.Parse("project_n:enemy")},new[]{EveId.Parse("fixture:feature_a")},new[]{typeof(PNFixtureEnemyProbe)}));
    }
}
public static class PNWorldLifecycleChecks
{
    public static string Run()
    {
        if(Application.isPlaying||EveRegistrySession.State!=EveSessionState.Stopped||SceneManager.GetActiveScene().rootCount!=0)throw new InvalidOperationException("Empty stopped disposable clone required.");
        int count=0;var checks=new List<string>();
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add("PASS "+label);Debug.Log("WorldS2 PASS "+label);count++;}
        void Reject(Action action,string label){try{action();}catch{Check(true,label);return;}throw new Exception(label);}
        var docs=new PNGameContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),".utmp/no-mods",Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule(),new PNWorldFixtureModule()},docs);
        EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Host,EveContentCompatibility.Create(hub,docs,"fixture:world"));
        var session=EveRegistrySession.Context;
        var scheduler=new EveEntityPreparationScheduler(session,new EvePreparationProfile());
        var worlds=new PNWorldRuntime(session,scheduler);var factory=new PNEntityFactory(session,worlds);
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
            // an authored entity that names a role gets that role's identity; one that does not has no role (Docs/EntityModulePlan.md E2b)
            var authoredEntities=session.Entities.Snapshot(a).ToArray();
            var withRole=authoredEntities.Where(e=>e.GetComponent<PNSceneEntityRole>()!=null).ToArray();var withoutRole=authoredEntities.Where(e=>e.GetComponent<PNSceneEntityRole>()==null).ToArray();
            Check(withRole.Length==1&&withoutRole.Length==1&&withRole[0].GetComponent<PNEntityRole>()?.Entity=="project_n:enemy"&&!PNEntityRoles.IsPlayer(withRole[0])&&withRole[0].GetComponent<PNEntityRole>().Persist,"an authored entity that names a role gets that role's identity");
            Check(withoutRole[0].GetComponent<PNEntityRole>()==null&&!PNEntityRoles.IsPlayer(withoutRole[0]),"and one that names none keeps having no role");
            var lease=a.AcquireLease("fixture join");Check(!worlds.Unload(a),"world unload waits for explicit join lease");lease.Dispose();
            Pump(()=>candidates.Take(2).All(x=>x.IsReturned));Check(worlds.Unload(a),"world A drains entities and resources");
            Check(session.Entities.Snapshot(b).Count==2&&bl!=null&&b.State==EveWorldState.Ready,"unload A preserves world B actors and pool");
            var again=factory.Get(bp);factory.Configure(again,bp);factory.Return(again);Check(ReferenceEquals(again,bl),"B reuses its own retained pool after A unload");
            PNWorldFixtureProvider.InjectLoadFailure=true;PNWorldFixtureProvider.RepairCleanup=false;
            var failed=worlds.Create(EveId.Parse("fixture:world"));Pump(()=>failed.State==EveWorldState.Failed);
            Check(b.State==EveWorldState.Ready,"partial load failure isolated from B");
            Reject(()=>worlds.Unload(failed),"failed resource cleanup remains owned for retry");
            Check(session.Worlds.TryGet(failed.InstanceId,failed.Incarnation,out _)&&failed.PendingCleanup>0,"failed world retains directory identity and cleanup");
            var originalFailure=failed.FirstFailure;Check(originalFailure!=null,"failed load records its first failure");
            PNWorldFixtureProvider.RepairCleanup=true;Check(worlds.Unload(failed),"world cleanup retry succeeds after fault repair");
            Check(failed.Failure==null&&ReferenceEquals(failed.FirstFailure,originalFailure),"cleanup clears Failure but keeps the original cause for later lifecycle errors");
            var io=session.Worlds.Create(EveId.Parse("fixture:pending"),new string('c',64));bool ioReady=false;io.BeginLoading();io.CompleteLoading();
            io.RegisterCleanup(()=>{if(!ioReady)throw new EveWorldCleanupPendingException("Fixture noncancelable scene IO pending");});
            Reject(()=>session.Worlds.Remove(io),"pending scene IO retains cleanup without pretending completion");
            Check(io.State==EveWorldState.Unloading&&io.Failure==null&&session.Worlds.TryGet(io.InstanceId,io.Incarnation,out _),"pending IO is Unloading rather than a runtime fault");
            ioReady=true;session.Worlds.Remove(io);Check(io.State==EveWorldState.Disposed,"pending scene cleanup completes after IO drains");
            Reject(()=>session.Worlds.Create(a.InstanceId,a.Incarnation,a.DefinitionId,a.ContentHash),"disposed world rejects stale incarnation reuse");
            // ---- world features (Docs/EntityModulePlan.md E2): mode + definition + creator, fixed at creation, remembered for a world created again ----
            PNWorldFixtureProvider.InjectLoadFailure=false;
            worlds.ModeFeatureSource=()=>new[]{EveId.Parse("fixture:feature_mode")};
            string Feat(EveWorldContext w)=>worlds.Get(w).Features.ToString();
            var plain=worlds.Create(EveId.Parse("fixture:world"));
            var viaDefinition=worlds.Create(EveId.Parse("fixture:world_featured"));
            var viaCreator=worlds.Create(EveId.Parse("fixture:world_featured"),Guid.Empty,1,new[]{EveId.Parse("fixture:feature_b")});
            Pump(()=>plain.State==EveWorldState.Ready&&viaDefinition.State==EveWorldState.Ready&&viaCreator.State==EveWorldState.Ready);
            Check(Feat(plain)=="fixture:feature_mode","a world has the mode's features and nothing else when nobody adds any");
            Check(Feat(viaDefinition)=="fixture:feature_a,fixture:feature_mode","a world also has the features its definition declares, sorted by id");
            Check(Feat(viaCreator)=="fixture:feature_a,fixture:feature_b,fixture:feature_mode","and the features its creator asked for");
            Check(worlds.Get(plain).Features==worlds.Get(plain).Features&&!worlds.Get(plain).Features.Has(EveId.Parse("fixture:feature_a")),"the set belongs to the world instance and does not change");
            Reject(()=>worlds.Create(EveId.Parse("fixture:world"),Guid.Empty,1,new[]{EveId.Parse("fixture:feature_missing")}),"a creator naming an unregistered feature is refused before the world exists");
            Reject(()=>worlds.Create(EveId.Parse("fixture:world"),Guid.Empty,1,Enumerable.Range(0,PNEntityModuleLimits.MaxFeaturesPerWorld+1).Select(i=>EveId.Parse("fixture:f"+i))),"and so is a creator that asks for more than the maximum number of features");
            var creatorWorld=viaCreator.InstanceId;var creatorIncarnation=viaCreator.Incarnation;
            Pump(()=>worlds.Unload(viaCreator));
            var recreated=worlds.Create(EveId.Parse("fixture:world_featured"),creatorWorld,checked(creatorIncarnation+1));Pump(()=>recreated.State==EveWorldState.Ready);
            Check(Feat(recreated)=="fixture:feature_a,fixture:feature_b,fixture:feature_mode","a world created again with the same identity (discarded, restored) gets the creator's features back");
            Pump(()=>worlds.Unload(recreated));Pump(()=>worlds.Unload(viaDefinition));Pump(()=>worlds.Unload(plain));
            // ---- entity modules (Docs/EntityModulePlan.md E3): the factory attaches what matches the entity's role and the features of ITS world ----
            var noFeature=worlds.Create(EveId.Parse("fixture:world"));var withFeature=worlds.Create(EveId.Parse("fixture:world_featured"));
            Pump(()=>noFeature.State==EveWorldState.Ready&&withFeature.State==EveWorldState.Ready);
            var plainRequest=factory.Player(0,noFeature,"chunk-0","player");var plainLoader=factory.Get(plainRequest);factory.Configure(plainLoader,plainRequest);
            Check(plainLoader.GetComponent<PNFixtureProbe>()==null&&factory.ModulesFor(plainLoader,noFeature).Count==0&&(plainLoader.GetComponent<PNEntityModuleMarker>()==null||plainLoader.GetComponent<PNEntityModuleMarker>().Applied.Count==0),"a player in a world without the feature gets no module");
            var featuredRequest=factory.Player(1,withFeature,"chunk-0","player");var featuredLoader=factory.Get(featuredRequest);factory.Configure(featuredLoader,featuredRequest);
            Check(featuredLoader.GetComponent<PNFixtureProbe>()!=null&&featuredLoader.GetComponent<PNFixtureEnemyProbe>()==null&&featuredLoader.GetComponent<PNEntityModuleMarker>().ToString()=="fixture:player_probe","a player in a world with the feature gets exactly the modules that match it");
            factory.Return(featuredLoader);var featuredAgain=factory.Get(featuredRequest);factory.Configure(featuredAgain,featuredRequest);
            Check(ReferenceEquals(featuredAgain,featuredLoader)&&featuredAgain.GetComponents<PNFixtureProbe>().Length==1&&featuredAgain.GetComponent<PNEntityModuleMarker>().Applied.Count==1,"a reused pooled player holds each component once and its marker is written again, not extended");
            var authoredRequests=factory.SceneEntities(withFeature).ToArray();var named=authoredRequests.Select(r=>(request:r,loader:factory.Get(r))).ToArray();
            foreach(var pair in named)factory.Configure(pair.loader,pair.request);
            Check(named.Count(x=>x.loader.GetComponent<PNFixtureEnemyProbe>()!=null)==1&&named.Count(x=>x.loader.GetComponent<PNSceneEntityRole>()!=null&&x.loader.GetComponent<PNFixtureEnemyProbe>()!=null)==1,"an authored entity that names the Enemy role gets the Enemy module in a world with the feature, one that names none gets none");
            foreach(var pair in named)factory.Return(pair.loader);
            var noFeatureAuthored=factory.SceneEntities(noFeature).Select(r=>(request:r,loader:factory.Get(r))).ToArray();
            foreach(var pair in noFeatureAuthored)factory.Configure(pair.loader,pair.request);
            Check(noFeatureAuthored.All(x=>x.loader.GetComponent<PNFixtureEnemyProbe>()==null),"and none in a world without it");
            foreach(var pair in noFeatureAuthored)factory.Return(pair.loader);
            factory.Return(plainLoader);factory.Return(featuredAgain);
            Pump(()=>worlds.Unload(noFeature));Pump(()=>worlds.Unload(withFeature));
            worlds.ModeFeatureSource=null;

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
