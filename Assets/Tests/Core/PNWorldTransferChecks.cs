#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;

public sealed class PNTransferProbe : IPNWorldTransferParticipant
{
    public static bool WaitPrepare,FailDispose;
    public static int FailCommitAt,Commits;
    public bool Prepare(PNWorldTransferContext c)=>!WaitPrepare;
    public void Hold(PNWorldTransferContext c) { }
    public void Commit(PNWorldTransferContext c){if(++Commits==FailCommitAt)throw new InvalidOperationException("Injected pre-cutover participant failure");}
    public void Abort(PNWorldTransferContext c) { }
    public void Complete(PNWorldTransferContext c) { }
    public void Dispose(){if(FailDispose)throw new InvalidOperationException("Injected participant cleanup failure");}
}
public sealed class PNTransferFixtureModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    {
        hub.GetOrCreate<PNWorldTransferParticipantDefinition>().Register(new PNWorldTransferParticipantDefinition(EveId.Parse("fixture:transfer_probe"),typeof(PNTransferProbe),()=>new PNTransferProbe()));
        hub.GetOrCreate<PNWorldDefinition>().Register(new PNWorldDefinition(EveId.Parse("fixture:transfer_world"),new string('a',64),typeof(PNWorldFixtureProvider),()=>new PNWorldFixtureProvider(),new PNWorldPolicy(participants:new[]{EveId.Parse("base:world_gameplay"),EveId.Parse("fixture:transfer_probe")})));
    }
}
public static class PNWorldTransferChecks
{
    public static string Run()
    {
        if(Application.isPlaying||EveRegistrySession.State!=EveSessionState.Stopped)throw new InvalidOperationException("Stopped disposable clone required.");
        var rows=new List<string>();void Check(bool value,string label){if(!value)throw new Exception(label);rows.Add("PASS "+label);}
        void Reject(Action action,string label){try{action();}catch{Check(true,label);return;}throw new Exception(label);}
        var docs=new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),".utmp/no-mods",Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule(),new PNTransferFixtureModule()},docs);
        EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"fixture:transfer"));
        var session=EveRegistrySession.Context;var scheduler=new EveEntityPreparationScheduler(session,new EvePreparationProfile());
        var worlds=new PNWorldRuntime(session,scheduler);var factory=new PNPlayerFactory(session,worlds);var transfers=new PNWorldTransitionCoordinator(session,worlds,factory,scheduler);
        ulong boundary=0;long id=0;
        void Pump(Func<bool> ready){for(int i=0;i<1000&&!ready();i++){scheduler.AdvanceFrame();transfers.FixedBoundary(++boundary);}if(!ready())throw new Exception("Transfer fixture did not drain.");}
        EveEntityPreparationScheduler.Candidate Spawn(EveWorldContext world,long owner)
        {
            var request=factory.Player(owner,world,"chunk-0","player");
            var candidate=scheduler.Enqueue(()=>factory.Get(request),loader=>{factory.Configure(loader,request);loader.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(--id),EveId.Parse(request.Character),owner);},factory.Return,factory.Published,error=>throw error);
            scheduler.AssociateWorld(candidate,world);Pump(()=>candidate.Loader?.IsReady==true);return candidate;
        }
        try
        {
            PNTransferProbe.Commits=PNTransferProbe.FailCommitAt=0;PNTransferProbe.WaitPrepare=PNTransferProbe.FailDispose=false;
            var a=worlds.Create(EveId.Parse("fixture:transfer_world"));var b=worlds.Create(EveId.Parse("fixture:transfer_world"));Pump(()=>a.CanExecute&&b.CanExecute);
            var first=Spawn(a,0);var actor=first.Loader;var original=actor.LoadContext.Identity;var entity=original.Id;long generation=original.Generation;var skill=actor.GetComponent<PNSkill>();var abilities=actor.GetComponent<PNAbilityManager>().Abilities;
            var abilityHandles=abilities.Instances.Select(x=>x.Handle).ToArray();var stat=actor.GetComponent<PNStat>();stat.SetCurrent(PNContentIds.Health,stat.GetMax(PNContentIds.Health)*.6);
            double hp=stat.GetCurrent(PNContentIds.Health);Vector3 position=actor.transform.position;
            actor.GetComponent<PNMove>().MoveTo(2,10);
            var request=new PNWorldTransferRequest(Guid.NewGuid(),new[]{actor},b,"chunk-1","player");var result=transfers.Request(request);
            Check(ReferenceEquals(result,transfers.Request(request)),"duplicate request returns the same receipt");
            Reject(()=>transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{actor},b,"chunk-1","player")),"overlapping actor transfer is rejected");
            Pump(()=>result.IsTerminal&&!result.CleanupPending);Check(result.State==PNWorldTransferState.Completed,"offline transfer completes through fixed commit and catch-up");
            Check(ReferenceEquals(first.Loader,actor)&&actor.IsReady&&original.Id==entity&&original.Generation==generation&&original.World==b,"transfer preserves gameplay actor/id/generation");
            Check(original.MembershipRevision==2&&session.Entities.Snapshot(a).Count==0&&session.Entities.Snapshot(b).Count==1,"world index commits membership once");
            Check(actor.GetComponent<PNMove>().Mode==EveMoveMode.None,"default hold cancels a running movement command through normal cleanup");
            Check(stat.GetCurrent(PNContentIds.Health)==hp&&abilities.Instances.Select(x=>x.Handle).SequenceEqual(abilityHandles)&&ReferenceEquals(skill,actor.GetComponent<PNSkill>()),"Stat and Ability instances survive without unload/re-equip");
            var boundaryCancel=transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{actor},a,"chunk-0","player"));for(int i=0;i<1000&&boundaryCancel.State!=PNWorldTransferState.Committing;i++)scheduler.AdvanceFrame();
            Check(boundaryCancel.State==PNWorldTransferState.Committing&&original.IsGameplayHeld,"transfer waits held at the fixed commit boundary");transfers.Cancel(boundaryCancel.Id);transfers.FixedBoundary(++boundary);Pump(()=>boundaryCancel.IsTerminal&&!boundaryCancel.CleanupPending);Check(boundaryCancel.State==PNWorldTransferState.Aborted&&original.World==b&&original.MembershipRevision==2&&!original.IsGameplayHeld,"cancel accepted before fixed boundary cannot commit on the next physics tick");
            PNTransferProbe.WaitPrepare=true;var pending=transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{actor},a,"chunk-0","player"));for(int i=0;i<8;i++)scheduler.AdvanceFrame();var beforeCancel=actor.transform.position;
            transfers.Cancel(pending.Id);Pump(()=>pending.IsTerminal&&!pending.CleanupPending);Check(pending.State==PNWorldTransferState.Aborted&&actor.transform.position==beforeCancel&&!original.IsGameplayHeld&&original.World==b,"abort before hold preserves position/state and releases reservation");PNTransferProbe.WaitPrepare=false;
            var second=Spawn(b,1);ulong r1=original.MembershipRevision,r2=second.Loader.LoadContext.Identity.MembershipRevision;
            PNTransferProbe.Commits=0;PNTransferProbe.FailCommitAt=2;
            var group=transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{actor,second.Loader},a,"chunk-0","player"));Pump(()=>group.IsTerminal&&!group.CleanupPending);
            Check(group.State==PNWorldTransferState.Aborted&&original.World==b&&second.Loader.LoadContext.Identity.World==b&&original.MembershipRevision==r1&&second.Loader.LoadContext.Identity.MembershipRevision==r2,"group failure before presence cutover rolls back every membership/revision");PNTransferProbe.FailCommitAt=0;
            PNTransferProbe.FailDispose=true;var cleanup=transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{actor},a,"chunk-0","player"));Pump(()=>cleanup.CleanupPending);
            Check(original.IsGameplayHeld&&transfers.PendingCount==1,"failed participant cleanup keeps actor hold and transfer ownership");PNTransferProbe.FailDispose=false;Pump(()=>cleanup.IsTerminal&&!cleanup.CleanupPending);Check(cleanup.State==PNWorldTransferState.Completed&&!original.IsGameplayHeld,"cleanup retry completes without resetting gameplay");
            var back=transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{actor},b,"chunk-0","player"));Pump(()=>back.IsTerminal&&!back.CleanupPending);Check(back.State==PNWorldTransferState.Completed,"return transfer reuses the same gameplay actor");
            Pump(()=>worlds.Unload(a));Check(actor!=null&&actor.IsReady&&original.World==b,"unloading source pool cannot destroy its transferred live borrow");
            var replacement=Spawn(b,2);var replaced=replacement.Loader;
            var rstat=replaced.GetComponent<PNStat>();rstat.SetCurrent(PNContentIds.Health,rstat.GetMax(PNContentIds.Health)*.4);double rhp=rstat.GetCurrent(PNContentIds.Health);
            replaced.GetComponent<PNPlayerMove>().TryFaceSkill(Vector2.left);
            var rabilities=replaced.GetComponent<PNAbilityManager>().Abilities;
            var cd=rabilities.Define(new PNAbilitySettings{Id="fixture:cooldown",Kind=PNAbilityKind.Cooldown,Maximum=2,InitialAmount=2,Cooldown=11,AutoRecover=true}.Create());cd.TryConsume();
            var removed=rabilities.Define(new PNAbilitySettings{Id="fixture:declared_only",Kind=PNAbilityKind.State}.Create());rabilities.Remove(removed.Handle);
            var snapshot=rabilities.CaptureCheckpoint();
            var replacedTransfer=transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{replaced},b,"chunk-1","player",replacementTemplate:"base:scene_spawn"));
            Pump(()=>replacedTransfer.IsTerminal&&!replacedTransfer.CleanupPending);
            if(replacedTransfer.State!=PNWorldTransferState.Completed)throw new Exception("Replacement failed: "+replacedTransfer.Error);
            Pump(()=>replacement.IsReturned);
            var restored=session.Entities.Snapshot(b).Single(x=>x.LoadContext.Identity.OwnerId==2);
            Check(restored!=replaced&&restored.GetComponent<PNStat>().GetCurrent(PNContentIds.Health)==rhp,"replacement restores pure authoritative HP before activating new actor");
            Check(Newtonsoft.Json.Linq.JToken.DeepEquals(snapshot,restored.GetComponent<PNAbilityManager>().Abilities.CaptureCheckpoint()),"replacement preserves ability charge recovery timers and configuration");
            Check(restored.GetComponent<PNMove>().FacingDirection==-1,"replacement preserves movement facing and passive locomotion state");
            scheduler.RetirePublished(restored);Pump(()=>session.Entities.Snapshot(b).All(x=>x.LoadContext.Identity.OwnerId!=2));
            scheduler.Retire(first);scheduler.Retire(second);Pump(()=>first.IsReturned&&second.IsReturned);Pump(()=>worlds.Unload(b));Check(session.Entities.Count==0&&transfers.PendingCount==0,"transfer/entity/world leases drain on teardown");
        }
        finally{PNTransferProbe.FailDispose=PNTransferProbe.WaitPrepare=false;PNTransferProbe.FailCommitAt=0;transfers.Dispose();EveRegistrySession.End();factory.Dispose();}
        return "PASS "+rows.Count+" native world transfer cases\n"+string.Join("\n",rows);
    }
}
#endif
