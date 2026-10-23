#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;

public sealed class PNWorldCallbackProbe : IPNWorldProvider,IPNWorldLifecycleParticipant,IDisposable
{
    public static bool FailReady,FailResource,FailDisposed;
    public static int ReadyCalls,UnloadingCalls,DisposedCalls,Resources;
    public IEnumerator Load(PNWorldBuildContext context)
    {
        bool released=false;context.Own(()=>{if(FailResource)throw new InvalidOperationException("Injected world resource cleanup failure");if(!released){released=true;Resources++;}});
        var load=new PNWorldFixtureProvider().Load(context);while(load.MoveNext())yield return load.Current;
    }
    public void WorldReady(PNWorldBuildContext context){if(FailReady)throw new InvalidOperationException("Injected ready callback failure");ReadyCalls++;}
    public void WorldUnloading(PNWorldBuildContext context){UnloadingCalls++;}
    public void WorldDisposed(PNWorldBuildContext context){if(FailDisposed)throw new InvalidOperationException("Injected disposed notification failure");DisposedCalls++;}
    public void Dispose() { }
}
public sealed class PNWorldCallbackModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)=>hub.GetOrCreate<PNWorldDefinition>().Register(new PNWorldDefinition(EveId.Parse("fixture:callbacks"),new string('e',64),typeof(PNWorldCallbackProbe),()=>new PNWorldCallbackProbe()));
}
public static class PNWorldExtensionChecks
{
    public static string Run()
    {
        var rows=new List<string>();void Check(bool value,string label){if(!value)throw new Exception(label);rows.Add("PASS "+label);}
        var docs=new PNGameContentSource(Path.Combine(UnityEngine.Application.streamingAssetsPath,"BaseContent"),".utmp/no-mods",Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule(),new PNWorldCallbackModule()},docs);
        EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"fixture:callbacks"));
        var session=EveRegistrySession.Context;var scheduler=new EveEntityPreparationScheduler(session,new EvePreparationProfile());var worlds=new PNWorldRuntime(session,scheduler);
        void Pump(Func<bool> ready){for(int i=0;i<1000&&!ready();i++)scheduler.AdvanceFrame();if(!ready())throw new Exception("Callback fixture did not drain.");}
        void Reject(Action action,string label){bool rejected=false;try{action();}catch(Exception){rejected=true;}Check(rejected,label);}
        try
        {
            PNWorldCallbackProbe.ReadyCalls=PNWorldCallbackProbe.UnloadingCalls=PNWorldCallbackProbe.DisposedCalls=PNWorldCallbackProbe.Resources=0;
            PNWorldCallbackProbe.FailReady=true;var failed=worlds.Create(EveId.Parse("fixture:callbacks"));Pump(()=>failed.State==EveWorldState.Failed);Check(failed.Failure!=null&&session.Worlds.Snapshot().Contains(failed),"failed ready callback keeps a failed owned world for cleanup");PNWorldCallbackProbe.FailReady=false;Pump(()=>worlds.Unload(failed));
            var world=worlds.Create(EveId.Parse("fixture:callbacks"));Pump(()=>world.CanExecute);int unload=PNWorldCallbackProbe.UnloadingCalls,disposed=PNWorldCallbackProbe.DisposedCalls;
            PNWorldCallbackProbe.FailResource=true;Reject(()=>worlds.Unload(world),"resource disposal error retains world identity/resources");Check(world.State==EveWorldState.Failed&&session.Worlds.Snapshot().Contains(world),"world is not forgotten while extension resources remain");PNWorldCallbackProbe.FailResource=false;
            PNWorldCallbackProbe.FailDisposed=true;Reject(()=>worlds.Unload(world),"disposed-notification failure remains a runtime-owned callback");Check(world.State==EveWorldState.Disposed&&!session.Worlds.Snapshot().Contains(world),"actual resource disposal and pending notification are distinct");
            Reject(()=>worlds.Create(world.DefinitionId,world.InstanceId,world.Incarnation+1),"pending final callback cannot be bypassed by recreating the same logical ID");
            PNWorldCallbackProbe.FailDisposed=false;Check(worlds.Unload(world),"repaired disposed notification completes runtime removal");Check(PNWorldCallbackProbe.UnloadingCalls==unload+1&&PNWorldCallbackProbe.DisposedCalls==disposed+1&&PNWorldCallbackProbe.Resources==2,"successful lifecycle/resource callbacks are not replayed on cleanup retries");
            var definition=hub.Get<PNWorldDefinition>().Get(EveId.Parse("fixture:callbacks"));Reject(()=>new PNWorldDefinition(EveId.Parse("fixture:foreign"),new string('e',64),typeof(PNWorldCallbackProbe),()=>new PNWorldCallbackProbe(),new PNWorldPolicy(participants:new[]{EveId.Parse("foreign:participant")})).ValidateRegistrationReferences("fixture",Array.Empty<string>()),"foreign provider references need declared registration dependencies");
            bool sealedRegistry=false;try{hub.GetOrCreate<PNWorldStateDefinition>();}catch(InvalidOperationException){sealedRegistry=true;}Check(sealedRegistry,"mod extension registries remain sealed during play");
            var selected=new EveRegistryHub();var providers=selected.GetOrCreate<PNWorldStateDefinition>();var original=new PNWorldStateDefinition(EveId.Parse("origin:state"),1,128,typeof(PNStateProbe),()=>new PNStateProbe());var replacement=new PNWorldStateDefinition(EveId.Parse("addon:state"),1,128,typeof(PNStateProbe),()=>new PNStateProbe());providers.Register(original);providers.Register(replacement);providers.RegisterBase(EveId.Parse("base:state"),original.Id,"origin",compatible:(x,y)=>x.Schema==y.Schema);providers.RequestOverride(EveId.Parse("base:state"),replacement.Id,"addon","1.0.0",new[]{"origin"});selected.Seal();
            Check(ReferenceEquals(providers.Get(EveId.Parse("base:state")),replacement),"state extension resolves the selected registry override");using(var first=providers.Get(EveId.Parse("base:state")).Create())using(var second=providers.Get(EveId.Parse("base:state")).Create())Check(!ReferenceEquals(first,second),"selected state factory creates isolated instances");
        }
        finally{PNWorldCallbackProbe.FailReady=PNWorldCallbackProbe.FailResource=PNWorldCallbackProbe.FailDisposed=false;EveRegistrySession.End();}
        return "PASS "+rows.Count+" native world extension lifecycle cases\n"+string.Join("\n",rows);
    }
}
#endif
