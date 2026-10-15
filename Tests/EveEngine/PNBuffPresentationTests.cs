using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using static EveSessionSmokeTests;

public static class PNBuffPresentationTests
{
    private sealed class Display : PNBuffBehavior
    {
        public int Stacks=1, secondsCalls;
        private long? seconds;
        private readonly bool timed;
        public Display(bool timed){this.timed=timed;}
        public override double? StackCount=>Stacks;
        public override void OnApply(PNBuffContext c,Newtonsoft.Json.Linq.JToken input)
        {
            if(timed){c.SetClock(PNBuffClock.EntityWithoutMinimum);c.SetDuration(3);Presentation.WatchRemainingSeconds(value=>{secondsCalls++;seconds=value;Send();});}
            Send();
        }
        public void Send()=>Presentation.Publish(new PNBuffNumbers(0,Stacks,-2,seconds));
        public override bool OnReapply(PNBuffContext c,PNBuffApplyRequest r){Stacks++;Send();return true;}
        public override void OnBehaviorStarted(PNBuffContext c,bool migrated)=>OnApply(c,null);
    }
    private sealed class Broken : PNBuffBehavior
    {public override void OnApply(PNBuffContext c,Newtonsoft.Json.Linq.JToken input){Presentation.Publish(new PNBuffNumbers(topLeft:99));throw new Exception("fixture apply error");}}
    public static void Run()
    {
        var hub=new EveRegistryHub();new EveBuiltInContentModule().Register(hub);new PNContentModule().Register(hub);
        var a=EveId.Parse("test:display_a");var b=EveId.Parse("test:display_b");var timed=EveId.Parse("test:display_timed");var bad=EveId.Parse("test:display_bad");
        var made=new List<Display>();
        foreach(var id in new[]{a,b,timed}){bool usesTime=id==timed;hub.Buffs().Register(new PNBuffDefinition(id, EveId.Parse("test:pnbuffpresentationtests_factory_1"),_=>{var value=new Display(usesTime);made.Add(value);return value;},null,multipleInstances:true,maximumInstances:8));}
        hub.Buffs().Register(new PNBuffDefinition(bad, EveId.Parse("test:pnbuffpresentationtests_factory_2"),_=>new Broken(),null));hub.Seal();
        var session=Guid.NewGuid();var source=new PNBuffSource(session,new EveEntityId(1));
        using var buffs=new PNBuffCollection(hub,session);var changes=new List<PNBuffPresentationChange>();buffs.PresentationChanged+=changes.Add;
        var first=buffs.Apply(new PNBuffApplyRequest(a,source));var second=buffs.Apply(new PNBuffApplyRequest(b,source));var third=buffs.Apply(new PNBuffApplyRequest(a,source));
        var rows=buffs.PresentationSnapshot();
        Check(rows.Select(x=>x.Handle).SequenceEqual(new[]{first.Handle,third.Handle,second.Handle}),"Presentation groups canonical IDs without merging instances");
        Check(rows[0].Numbers.TopLeft==0 && rows[0].Numbers.TopRight==1 && rows[0].Numbers.BottomLeft==-2 && rows[0].Numbers.BottomRight==null,"Four slots preserve zero negative and absent values");
        int count=changes.Count;made[0].Send();Check(changes.Count==count,"Identical full publication emits no duplicate update");
        made[0].Presentation.Publish(new PNBuffNumbers(bottomRight:8));
        var changed=buffs.PresentationSnapshot().First(x=>x.Handle.Equals(first.Handle));
        Check(changed.Numbers.TopLeft==null && changed.Numbers.TopRight==null && changed.Numbers.BottomRight==8,"Full publication clears omitted corners");
        Check(rows[0].Numbers.TopRight==1 && buffs.Get(first.Handle).Stacks==1,"Presentation snapshots immutable and gameplay stacks independent");
        buffs.Remove(first.Handle);Check(buffs.PresentationSnapshot().First().Handle.Equals(third.Handle),"Group retains order after its oldest instance ends");
        buffs.Remove(third.Handle);var again=buffs.Apply(new PNBuffApplyRequest(a,source));Check(buffs.PresentationSnapshot()[0].Handle.Equals(second.Handle),"Reappearing empty group joins end");
        var stale=made[0].Presentation;count=changes.Count;stale.Publish(new PNBuffNumbers(topRight:900));Check(changes.Count==count,"Ended writer cannot publish");
        var clock=buffs.Apply(new PNBuffApplyRequest(timed,source));var timer=made.Last();
        Check(buffs.PresentationSnapshot().Last().Numbers.BottomRight==3,"Initial duration published by source helper");
        count=changes.Count;buffs.Advance(.4,1,0);Check(changes.Count==count && timer.secondsCalls==1,"Raw frozen clock does not ask behavior or emit updates");
        buffs.Advance(.2,1,1);Check(timer.secondsCalls==1,"Same displayed second does not invoke presentation callback");
        buffs.Advance(1.1,1,1);Check(buffs.PresentationSnapshot().Last().Numbers.BottomRight==2 && timer.secondsCalls==2,"Duration changes only at integer boundary");
        buffs.Suspend(true);Check(buffs.PresentationSnapshot().All(x=>x.Suspended),"Suspend publishes metadata while retaining numbers");
        count=timer.secondsCalls;buffs.Advance(1,1,1);Check(timer.secondsCalls==count,"Suspension stops duration presentation");
        buffs.Suspend(false);Check(timer.secondsCalls==count,"Resume without timer change does not replay publication callback");
        var previous=timer.Presentation;
        using(var replacement=buffs.Contribute("presentation override",new PNBuffOverrideContribution(b,1),exact:clock.Handle,selection:PNBuffSelection.Current))
        {
            Check(buffs.PresentationSnapshot().Last().ProfileId==b && buffs.PresentationSnapshot().Last().Numbers.BottomRight==null,"Override replaces metadata and clears prior timer slots");
            count=changes.Count;previous.Publish(new PNBuffNumbers(topLeft:99));Check(changes.Count==count,"Retired behavior writer cannot update replacement");
        }
        count=changes.Count;var failure=buffs.Apply(new PNBuffApplyRequest(bad,source));Check(failure.Status==PNBuffStatus.Failed && changes.Count==count,"Failed apply never exposes staged presentation");
        var writer=made.Last().Presentation;
        Action<PNBuffPresentationChange> updateAgain=change=>{if(change.Kind==PNBuffPresentationChangeKind.Updated && change.State.Numbers.TopLeft==20)writer.Publish(new PNBuffNumbers(topLeft:21));};
        buffs.PresentationChanged+=updateAgain;writer.Publish(new PNBuffNumbers(topLeft:20));buffs.PresentationChanged-=updateAgain;
        Check(buffs.PresentationSnapshot().Last().Numbers.TopLeft==21,"Publication inside observer is delivered at the next coordinator boundary");
        buffs.Suspend(true);
        using(var suspendedOverride=buffs.Contribute("suspended presentation override",new PNBuffOverrideContribution(b,2),exact:clock.Handle,selection:PNBuffSelection.Current))
        {
            var pending=buffs.PresentationSnapshot().Last();
            Check(pending.Suspended && pending.ProfileId==b && pending.Numbers.Equals(default(PNBuffNumbers)),"Suspended replacement has empty slots until behavior startup");
            buffs.Suspend(false);
            Check(buffs.PresentationSnapshot().Last().Numbers.TopRight==1,"Resume initializes replacement presentation without old timer");
        }
        int errors=0;buffs.Failed+=_=>errors++;Action<PNBuffPresentationChange> throwing=_=>throw new Exception("fixture observer");buffs.PresentationChanged+=throwing;
        made.Last().Presentation.Publish(new PNBuffNumbers(topRight:4));Check(errors==1 && clock.Handle.IsAlive,"Presentation observer error does not end Buff");buffs.PresentationChanged-=throwing;
        buffs.Dispose();Check(buffs.PresentationSnapshot().Count==0 && changes.Last().Kind==PNBuffPresentationChangeKind.Reset,"Dispose clears cache and delivers reset");
    }
}
