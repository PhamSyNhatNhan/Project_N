#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class PNStateProbe : IPNWorldStateProvider
{
    public static bool FailCapture,FailRestore,FailDispose;
    public JToken Capture(PNWorldStateContext context)
    {if(FailCapture)throw new InvalidOperationException("Injected capture failure");return new JObject{["value"]=42};}
    public void Validate(PNWorldStateContext context,JToken state){if((int)state["value"]!=42)throw new FormatException("Probe state differs.");}
    public void Restore(PNWorldStateContext context,JToken state){if(FailRestore)throw new InvalidOperationException("Injected restore failure");Validate(context,state);}
    public void Dispose(){if(FailDispose)throw new InvalidOperationException("Injected serializer disposal failure");}
}
public sealed class PNLifetimeFixtureModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    {
        hub.GetOrCreate<PNWorldStateDefinition>().Register(new PNWorldStateDefinition(EveId.Parse("fixture:state_probe"),1,128,typeof(PNStateProbe),()=>new PNStateProbe()));
        foreach(PNEmptyWorldPolicy policy in Enum.GetValues(typeof(PNEmptyWorldPolicy)))hub.GetOrCreate<PNWorldDefinition>().Register(new PNWorldDefinition(EveId.Parse("fixture:lifetime_"+policy.ToString().ToLowerInvariant()),new string('c',64),typeof(PNWorldFixtureProvider),()=>new PNWorldFixtureProvider(),new PNWorldPolicy(policy,actorState:new[]{EveId.Parse("base:actor_world_state"),EveId.Parse("fixture:state_probe")})));
    }
}
public static class PNWorldLifetimeChecks
{
    public static string Run()
    {
        var rows=new List<string>();void Check(bool value,string label){if(!value)throw new Exception(label);rows.Add("PASS "+label);Debug.Log("Lifetime PASS "+label);}
        var docs=new PNGameContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),".utmp/no-mods",Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule(),new PNLifetimeFixtureModule()},docs);
        EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"fixture:lifetime"));
        var session=EveRegistrySession.Context;var scheduler=new EveEntityPreparationScheduler(session,new EvePreparationProfile());var worlds=new PNWorldRuntime(session,scheduler);var factory=new PNEntityFactory(session,worlds);var life=new PNWorldLifetimeController(session,worlds,factory,scheduler);
        void Pump(Func<bool> ready){for(int i=0;i<2000&&!ready();i++)scheduler.AdvanceFrame();if(!ready())throw new Exception("Lifetime work did not drain.");}
        EveWorldContext Create(PNEmptyWorldPolicy policy){var world=worlds.Create(EveId.Parse("fixture:lifetime_"+policy.ToString().ToLowerInvariant()));Pump(()=>world.CanExecute);return world;}
        EveEntityPreparationScheduler.Candidate[] Spawn(EveWorldContext world)
        {
            var result=new List<EveEntityPreparationScheduler.Candidate>();
            foreach(var request in factory.SceneEntities(world)){var candidate=scheduler.Enqueue(()=>factory.Get(request),loader=>factory.Configure(loader,request),factory.Return,factory.Published,error=>throw error);scheduler.AssociateWorld(candidate,world);result.Add(candidate);}
            Pump(()=>result.All(x=>x.Loader?.IsReady==true));return result.ToArray();
        }
        try
        {
            var keep=Create(PNEmptyWorldPolicy.Continue);life.Tick();keep.AdvanceClock(.5);Check(keep.CanExecute&&keep.GameplayTime==.5,"Continue retains empty-world gameplay clock");
            var pause=Create(PNEmptyWorldPolicy.Pause);life.Tick();pause.AdvanceClock(.5);Check(pause.State==EveWorldState.Paused&&pause.GameplayTime==0,"Pause freezes only the empty world clock");
            using(var pin=pause.AcquireLease("pending join fixture")){life.Tick();Check(pause.CanExecute,"pending join activity resumes an automatically paused world");}
            life.Tick();Check(pause.State==EveWorldState.Paused,"released join activity allows Pause again");
            var discard=Create(PNEmptyWorldPolicy.Discard);var discardedId=discard.InstanceId;
            using(var pin=discard.AcquireLease("pending custom work")){life.Tick();scheduler.AdvanceFrame();Check(discard.CanExecute,"unknown explicit activity prevents automatic Discard");}
            life.Tick();Pump(()=>discard.State==EveWorldState.Disposed);var newer=life.RecreateDiscarded(discard.DefinitionId,discardedId,discard.Incarnation);Pump(()=>newer.CanExecute);Check(newer.Incarnation==2,"Discard recreation advances logical-world incarnation");
            var checkpoint=Create(PNEmptyWorldPolicy.CheckpointUnload);var candidates=Spawn(checkpoint);var stat=candidates[0].Loader.GetComponent<PNStat>();stat.SetCurrent(PNContentIds.Health,stat.GetMax(PNContentIds.Health)*.25);double hp=stat.GetCurrent(PNContentIds.Health);checkpoint.AdvanceClock(3.25);
            Check(life.IsEmpty(checkpoint),"resident NPC leases keep resources owned without inventing player occupancy");
            PNStateProbe.FailCapture=true;var failed=life.CaptureAndUnload(checkpoint);Pump(()=>failed.IsTerminal);PNStateProbe.FailCapture=false;
            Check(failed.State==PNWorldLifetimeState.Failed&&checkpoint.CanExecute&&candidates[0].Loader.IsReady&&stat.GetCurrent(PNContentIds.Health)==hp&&life.GetCheckpoint(checkpoint.InstanceId)==null,"capture failure leaves source resident with unchanged HP and no partial checkpoint");
            var captured=life.CaptureAndUnload(checkpoint);Pump(()=>captured.IsTerminal);if(captured.Error!=null)throw new Exception(captured.Error);Check(checkpoint.State==EveWorldState.Disposed&&life.GetCheckpoint(checkpoint.InstanceId)!=null&&candidates.All(x=>x.IsReturned),"validated checkpoint owns state before source entity/world cleanup");
            string stored=life.GetCheckpoint(checkpoint.InstanceId).Data.Json;
            PNWorldFixtureProvider.InjectLoadFailure=true;var failedLoad=life.Restore(checkpoint.InstanceId);Pump(()=>failedLoad.World.State==EveWorldState.Failed);
            Pump(()=>worlds.Unload(failedLoad.World));Pump(()=>failedLoad.IsTerminal);PNWorldFixtureProvider.InjectLoadFailure=false;
            Check(failedLoad.State==PNWorldLifetimeState.Failed&&failedLoad.Error.Contains("Injected partial world load failure")&&failedLoad.World.FirstFailure!=null,"failed staged load cleaned before restore step retains original cause and releases work");
            Check(life.GetCheckpoint(checkpoint.InstanceId).Data.Json==stored&&life.PendingCount==0,"host-style failed world cleanup does not lose checkpoint or leak restore job");
            PNStateProbe.FailRestore=true;var badRestore=life.Restore(checkpoint.InstanceId);Pump(()=>badRestore.IsTerminal);PNStateProbe.FailRestore=false;
            Check(badRestore.State==PNWorldLifetimeState.Failed&&badRestore.World.State==EveWorldState.Disposed&&session.Entities.Count==0,"restore failure cleans provisional actors and the failed new world");
            Check(life.GetCheckpoint(checkpoint.InstanceId).Data.Json==stored,"failed restore retains the previous valid in-memory checkpoint");
            var restored=life.Restore(checkpoint.InstanceId);Pump(()=>restored.IsTerminal);if(restored.Error!=null)throw new Exception(restored.Error);
            var actors=session.Entities.Snapshot(restored.World);Check(restored.State==PNWorldLifetimeState.Completed&&restored.World.Incarnation==4&&actors.Count==2,"checkpoint restore creates a new incarnation with exactly the saved actors");
            Check(restored.World.GameplayTime==3.25&&actors.Any(x=>x.GetComponent<PNStat>().GetCurrent(PNContentIds.Health)==hp),"world clock and NPC authoritative resource state survive checkpoint unload");
            var pure=actors[0].GetComponent<PNStat>();var source=EveId.Parse("fixture:temporary_source");pure.SetSource(source,new[]{new EveStatModifier(PNContentIds.AttackPower,3,0)});var snap=pure.CaptureWorldState();Check(pure.Set.SourceSnapshot().ContainsKey(source),"pure world capture does not end floor-local stat sources");
            using(var service=new PNWorldStateService(session))
            {
                var context=new PNWorldStateContext(restored.World,actors[0],new Dictionary<string,EveEntityLoader>{{"self",actors[0]}});var ids=new[]{EveId.Parse("base:actor_world_state")};var record=service.Capture(context,ids);var bad=record.Read();bad[0]["schema"]=999;
                bool rejected=false;try{service.Validate(context,new PNWorldStateRecord(bad.ToString()),ids);}catch(FormatException){rejected=true;}Check(rejected&&actors[0].IsReady,"incompatible required serializer schema rejects before restoration");
                PNStateProbe.FailDispose=true;try{service.Capture(context,new[]{EveId.Parse("fixture:state_probe")});}catch(InvalidOperationException){}Check(service.PendingCleanup==1,"serializer cleanup failure remains owned for repair/retry");PNStateProbe.FailDispose=false;service.Dispose();Check(service.PendingCleanup==0,"serializer cleanup retry releases retained provider");
            }
            var portable=life.CaptureSnapshot(restored.World);Pump(()=>portable.IsTerminal);Check(portable.State==PNWorldLifetimeState.Captured&&restored.World.CanExecute&&actors.All(x=>x.IsReady),"portable capture leaves source resident and authoritative state active");
            var envelope=life.Export(portable.Snapshot);var imported=life.Import(new PNWorldSaveEnvelope(envelope.Json));var cross=life.Restore(imported);Pump(()=>cross.IsTerminal);if(cross.Error!=null)throw new Exception(cross.Error);
            var metadata=actors[0].GetComponent<PNWorldSaveActorKey>();var oldEntity=metadata.Entity;var targetRoot=new GameObject("Save key replacement fixture");targetRoot.SetActive(false);targetRoot.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(-9001),EveId.Parse("project_n:dummy"),0);var targetLoader=targetRoot.AddComponent<EveEntityLoader>();
            try{PNWorldSaveActorKey.Copy(actors[0],targetLoader);Check(targetRoot.GetComponent<PNWorldSaveActorKey>()?.Key==metadata.Key,"replacement preserves a current actor stable save key");UnityEngine.Object.DestroyImmediate(targetRoot.GetComponent<PNWorldSaveActorKey>());metadata.Entity=default;PNWorldSaveActorKey.Copy(actors[0],targetLoader);Check(targetRoot.GetComponent<PNWorldSaveActorKey>()==null,"pooled stale actor metadata cannot impersonate the previous saved actor");}
            finally{metadata.Entity=oldEntity;UnityEngine.Object.DestroyImmediate(targetRoot);}
            Check(cross.State==PNWorldLifetimeState.Completed&&cross.World.InstanceId!=restored.World.InstanceId&&session.Entities.Snapshot(cross.World).Count==2,"portable import uses a fresh runtime world and restores two actors");
            var again=life.CaptureSnapshot(cross.World);Pump(()=>again.IsTerminal);Check(life.Export(again.Snapshot).SaveWorldId==envelope.SaveWorldId,"portable world save identity survives restore and recapture");
            foreach(var world in session.Worlds.Snapshot().ToArray())Pump(()=>worlds.Unload(world));Check(session.Entities.Count==0&&life.PendingCount==0,"all world/state/entity resources drain");
        }
        catch(Exception error){Debug.LogError("Lifetime original failure: "+error);throw;}
        finally{PNWorldFixtureProvider.InjectLoadFailure=false;PNStateProbe.FailCapture=PNStateProbe.FailRestore=PNStateProbe.FailDispose=false;life.Dispose();EveRegistrySession.End();factory.Dispose();}
        return "PASS "+rows.Count+" native world lifetime cases\n"+string.Join("\n",rows);
    }
}
#endif
