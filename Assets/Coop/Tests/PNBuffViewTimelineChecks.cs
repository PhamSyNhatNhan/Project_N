#if UNITY_EDITOR
using System;

namespace ProjectN.Coop.Tests
{
    public static class PNBuffViewTimelineChecks
    {
        public static string Run()
        {
            int count=0;void Check(bool b,string s){if(!b)throw new Exception(s);count++;}
            var epoch=Guid.NewGuid();
            PNTimeSample Sample(double host,double presentation,bool ready=true)=>new PNTimeSample(epoch,ready,host,host,presentation,.1);
            var view=new PNBuffViewTimeline();
            Check(PNBuffViewTimeline.Resolve(true,false,PNMoveNetworkRole.Owner)==PNBuffViewRole.Unresolved,"unattached default cannot choose live role");
            Check(PNBuffViewTimeline.Resolve(false,false,PNMoveNetworkRole.Local)==PNBuffViewRole.Presentation,"no Move is explicitly observed");
            Check(PNBuffViewTimeline.Resolve(true,true,PNMoveNetworkRole.Local)==PNBuffViewRole.Unresolved,"client Local is not guessed as predicted");
            Check(PNBuffViewTimeline.Resolve(true,true,PNMoveNetworkRole.RemoteOwnerHost)==PNBuffViewRole.Unresolved,"host simulation role cannot become predicted client");
            Check(PNBuffViewTimeline.Resolve(true,true,PNMoveNetworkRole.Observer)==PNBuffViewRole.Presentation,"observed pet role stays presentation");
            Check(PNBuffViewTimeline.Resolve(true,true,PNMoveNetworkRole.Owner)==PNBuffViewRole.Host,"predicted body reads host estimate");
            view.Observe(PNBuffViewRole.Unresolved,10,2);
            Check(!view.Prepared&&!view.Expired(11.9)&&view.Expired(12),"unresolved empty binding has finite admission deadline");
            Check(!view.Observe(PNBuffViewRole.Host,11,2)&&view.Prepared,"initial role establishes without a recovery readiness cycle");
            Check(view.Select(Sample(12,11.8))==12,"owner does not wait on observer buffer");
            view.Published(12);
            Check(view.Observe(PNBuffViewRole.Presentation,12,2)&&!view.Prepared,"same-binding host to observer closes live view");
            Check(!view.HoldBaseline(7,12.1,Sample(12.4,12.2)),"candidate can cut only after previous source and capture are reached");
            view.CommitBaseline(7,Sample(12.4,12.2));
            Check(view.Prepared&&view.Select(Sample(13,12.8))==12.8,"observer resumes at buffered stamp");
            view.Published(13.5);view.Published(13);
            Check(view.PublishedStamp==13.5,"unscaled cut boundary does not rewind even at paused game clock");
            Check(view.Observe(PNBuffViewRole.Host,14,2),"observer to host starts one cut request");
            Check(!view.HoldBaseline(8,14,Sample(14.2,14)),"forward seek candidate ready");
            view.CommitBaseline(8,Sample(14.2,14));
            Check(view.Prepared&&view.Active==PNBuffViewRole.Host,"forward cut commits selected role");
            view.Published(15);view.Observe(PNBuffViewRole.Presentation,15,2);
            Check(view.HoldBaseline(9,15.1,Sample(15.2,14.9))&&view.Frozen,"backward source holds the current image");
            double deadline=view.Deadline;
            Check(!view.Observe(PNBuffViewRole.Unresolved,15.5,200)&&view.Deadline==deadline,"detach cannot renew original cut budget");
            Check(view.HoldBaseline(9,15.1,Sample(15.8,15.6)),"unknown target cannot publish candidate");
            Check(!view.Observe(PNBuffViewRole.Presentation,15.8,200)&&view.Deadline==deadline,"retry role keeps original deadline");
            Check(view.HoldBaseline(9,15.1,Sample(16,15.9,false)),"unready sample is not a valid live cut");
            Check(!view.HoldBaseline(9,15.1,Sample(16,15.9)),"the retained candidate catches up, not a moving fresh baseline");
            view.CommitBaseline(9,Sample(16,15.9));
            Check(view.Prepared&&!view.Frozen&&view.Candidate==0&&view.Deadline==0,"cut releases candidate/deadline");
            view.Observe(PNBuffViewRole.Host,17,0);
            Check(view.Expired(17),"zero remaining policy budget expires immediately");
            view.HoldBaseline(10,17,Sample(17.1,16.9));
            view.RecoveryBaseline(PNBuffViewRole.Host);
            Check(view.Prepared&&!view.Transition&&!view.Frozen&&view.PublishedStamp==0,"authenticated unready recovery baseline cancels old cut without waiting on readiness");
            view.RecoveryBaseline(PNBuffViewRole.Unresolved);view.Observe(PNBuffViewRole.Unresolved,18,1);
            Check(view.Expired(19),"empty unknown role still expires after recovery");
            // Exercise the real monotonic reader at a held backward cut, including
            // a paused manager value whose unscaled boundary still advances.
            var history=new PNClockHistory(epoch);
            var key=new PNClockKey(epoch,Guid.NewGuid(),1,PNClockDomain.BuffGame,7,1);
            using(var clock=new PNClockProjection(history,key))
            {
                view.RecoveryBaseline(PNBuffViewRole.Host);
                clock.Prepare(new PNClockAnchor(key,1,20,100,8),21,.1,20,true);
                Check(clock.ReadAt(Sample(21,20.8),21)==108,"owner clock at8x projects exactly once");view.Published(21);
                view.Observe(PNBuffViewRole.Presentation,21,2);
                Check(view.HoldBaseline(11,20.95,Sample(21.1,20.9))&&clock.Value==108,"backward cut holds actual clock and state image");
                Check(!view.HoldBaseline(11,20.95,Sample(21.4,21.1)),"presentation reaches published unscaled boundary");
                clock.Prepare(new PNClockAnchor(key,2,20.95,107.6,8),21.4,.1,21.1,true);
                view.CommitBaseline(11,Sample(21.4,21.1));
                Check(Math.Abs(clock.ReadAt(Sample(21.4,21.1),view.Select(Sample(21.4,21.1)))-108.8)<1e-8,"explicit coherent cut reads new timeline without subtracting old elapsed");
                clock.Prepare(new PNClockAnchor(key,3,22,116,0),23,.1,22,false);
                Check(clock.ReadAt(Sample(23,22.8),22.8)==116,"pause holds manager value while source stamp advances");view.Published(22.8);
                view.Observe(PNBuffViewRole.Host,23,1);view.HoldBaseline(12,23,Sample(23.1,22.9));
                clock.Prepare(new PNClockAnchor(key,4,23,116,0),23.1,.1,23.1,true);view.CommitBaseline(12,Sample(23.1,22.9));
                Check(clock.ReadAt(Sample(23.1,22.9),view.Select(Sample(23.1,22.9)))==116,"paused forward cut neither ages nor replays elapsed");
            }
            Check(history.AnchorCount==0,"owned view history released");
            using(var clock=new PNClockProjection(history,key))
            {
                clock.Prepare(new PNClockAnchor(key,1,30,100,8),31,.2,30,true);
                clock.ReadAt(Sample(31,30.8),31);view.Snapshot(8);
                clock.Prepare(new PNClockAnchor(key,2,31,106.7,0),31.2,.2,31,false);
                Check(clock.ReadAt(Sample(31.2,31),31.2)==108&&view.CapturedRate==8,"partial pause anchor cannot by itself reconcile the old projected image");
                clock.Prepare(new PNClockAnchor(key,2,31,106.7,0),31.2,.2,31,true);view.Snapshot(0);
                Check(Math.Abs(clock.ReadAt(Sample(31.2,31),31.2)-106.7)<1e-8&&view.CapturedRate==0,"full owner snapshot explicitly corrects pause overshoot without negative gameplay Advance");
                Check(clock.ReadAt(Sample(34,33.8),34)==106.7,"paused owner remains at corrected authority value instead of ahead for the entire pause");
            }
            return "PASS "+count+" Buff view role/cut/deadline cases";
        }
    }
}
#endif

