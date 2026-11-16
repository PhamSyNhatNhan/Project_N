#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

namespace ProjectN.Coop.Tests
{
    public static class PNBuffViewReplicaChecks
    {
        private sealed class Empty : PNBuffBehavior { }
        private sealed class Presenter : PNBuffPresenter
        {
            internal static readonly List<string> Calls=new List<string>();
            public override void Update(IReadOnlyList<PNBuffWireState> states,double now) { }
            public override void Cue(PNBuffCue c)=>Calls.Add("cue:"+c.Id);
            public override void Removed(PNBuffWireState s,PNBuffEndReason r)=>Calls.Add("removed");
            public override void Dispose()=>Calls.Add("disposed");
        }
        public static string Run()
        {
            int checks=0;void Check(bool b,string s){if(!b)throw new Exception(s);checks++;}
            var hub=new EveRegistryHub();new EveBuiltInContentModule().Register(hub);new PNContentModule().Register(hub);
            var id=EveId.Parse("test:t4b_replica");var presenter=EveId.Parse("test:t4b_presenter");
            hub.BuffPresenters().Register(new PNBuffPresenterDefinition(presenter,presenter,_=>new Presenter(),_=>{}));
            hub.Buffs().Register(new PNBuffDefinition(id,id,_=>new Empty(),null,presenterId:presenter).WithCountdown(PNBuffCountdown.BottomRight));hub.Seal();
            var epoch=Guid.NewGuid();var entity=new EveEntityId(77);
            foreach(double global in new[]{0,.5,1,2,4,8})
            foreach(PNBuffClock kind in new[]{PNBuffClock.Game,PNBuffClock.Entity,PNBuffClock.EntityWithoutMinimum})
            foreach(bool owner in new[]{false,true})
            {
                var history=new PNClockHistory(epoch);var key=new PNClockKey(epoch,Guid.NewGuid(),1,PNClockDomain.BuffGame,77,1);
                using(var projection=new PNClockProjection(history,key))
                using(var replica=new PNBuffReplica(hub,epoch,entity))
                {
                    double rate=kind==PNBuffClock.Game?1:kind==PNBuffClock.Entity?.25:0;
                    var state=new PNBuffWireState(1,1,id,id,entity,null,false,default,kind,0,50,rate,600);
                    replica.ApplyLifecycle(new PNBuffWireMessage(epoch,entity,1,0,PNBuffWireKind.Snapshot,new[]{state}),50);
                    projection.Prepare(new PNClockAnchor(key,1,10,50,global),11,.1,10,true);
                    var sample=new PNTimeSample(epoch,true,11,11,10.8,.1);
                    double stamp=owner?sample.Host:sample.Presentation;
                    double value=projection.ReadAt(sample,stamp);replica.Advance(value);replica.TryReadTiming(id,value,out var timing);
                    double expected=600-(stamp-10)*global*rate;
                    Check(timing.Present&&Math.Abs(timing.Remaining.Value-expected)<1e-8,"selected Host/Presentation applies Game/Entity/raw rate once");
                    var suspended=new PNBuffWireState(1,2,id,id,entity,null,true,default,kind,600-expected,value,rate,600);
                    replica.ApplyLifecycle(new PNBuffWireMessage(epoch,entity,1,1,PNBuffWireKind.Updated,new[]{suspended}),value);
                    replica.Advance(value+20);replica.TryReadTiming(id,value+20,out timing);
                    Check(timing.Suspended&&Math.Abs(timing.Remaining.Value-expected)<1e-8,"suspended view does not age while the selected source advances");
                }
                Check(history.AnchorCount==0,"view clock history disposed");
            }
            Presenter.Calls.Clear();
            using(var replica=new PNBuffReplica(hub,epoch,entity))
            {
                var state=new PNBuffWireState(1,1,id,id,entity,null,false,default,PNBuffClock.Game,0,0,1,.5);
                replica.ApplyLifecycle(new PNBuffWireMessage(epoch,entity,1,0,PNBuffWireKind.Snapshot,new[]{state}),0);
                var cue=new PNBuffOptionalCueMessage(epoch,entity,1,new PNBuffCue(1,1,"once",.1,10));
                replica.Receive(cue,.1);
                replica.ApplyLifecycle(new PNBuffWireMessage(epoch,entity,1,1,PNBuffWireKind.Snapshot,new[]{state}),.2);replica.Receive(cue,.2);
                Check(Presenter.Calls.SequenceEqual(new[]{"cue:1"}),"coherent baseline does not replay optional cue");
                replica.Advance(2);replica.TryReadTiming(id,2,out var timing);Check(timing.Present&&timing.Remaining==0,"projected expiry retains authority presence");
                replica.ApplyLifecycle(new PNBuffWireMessage(epoch,entity,1,2,PNBuffWireKind.Removed,removedId:1,endReason:PNBuffEndReason.Expired,cues:new[]{new PNBuffCue(2,1,"end",2)}),2);
                Check(Presenter.Calls.SequenceEqual(new[]{"cue:1","cue:2","removed","disposed"}),"required ending cue precedes removal/resource retirement");
            }
            return "PASS "+checks+" native Buff view rates/suspend/cue/presence cases";
        }
    }
}
#endif
