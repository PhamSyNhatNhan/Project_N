using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine.Movement;
using static EveSessionSmokeTests;

public static class MoveProtocolTests
{
    private static readonly MoveIdentity Id=new MoveIdentity(Guid.Parse("aaafeed0-4333-4444-9999-111111111111"),301,2);
    private static MovementState Pose(float x,float y=0,float velocity=0) => new MovementState(new MoveVector(x,y,0),new MoveVector(velocity,0,0));
    private static MoveMirror Mirror(uint revision=1,float scale=1,MoveRights rights=MoveRights.All,bool dead=false,long life=1,float speed=7) =>
        new MoveMirror(Id,revision,life,dead,rights,speed,5,scale,scale,new[]{"base:voluntary_motion"});
    private sealed class Rules : IMoveConstraints
    {
        public bool Accept(MovementState previous,MovementState proposed,MoveMirror mirror,uint known) =>
            !mirror.Dead && (mirror.Scale!=0 || previous.Position.Near(proposed.Position) && proposed.Velocity.Near(default)) &&
            ((mirror.Rights&MoveRights.Walk)!=0 || Math.Abs(previous.Position.X-proposed.Position.X)<.001f);
    }
    private static MoveHostModel Host() => new MoveHostModel(Id,7,Mirror(),Pose(0),default,new Rules(),4){Ready=true};
    private static MoveOwnerModel Owner(MoveHostModel host)
    {
        var o=new MoveOwnerModel(Id); o.SetReady(true); o.ReceiveMirror(host.Mirror,true); o.ReceiveCorrection(host.LastCorrection,true); o.ApplyBoundary((c,m)=>{}); return o;
    }
    public static void Run()
    {
        Check(MoveOrder.Newer(1,uint.MaxValue) && !MoveOrder.Newer(uint.MaxValue,1) && !MoveOrder.Newer(1,1),"Move serial wrap ordering");
        Check(MoveOrder.Next(uint.MaxValue)==1,"Move serial skips reserved zero");
        foreach(var message in new object[]{new MoveBaselineRequest(Id,6),new MovePoseReport(Id,1,2,3,4,Pose(12,3,4)),Mirror(),new MoveCorrection(Id,2,3,1,Pose(8),new MoveRecovery(new MoveVector(1,2,3),new MoveVector(4,5,6),true),MoveCorrectionReason.Constraint,55,6),new MovePoseState(Id,7,2,1.125,Pose(4))})
        {
            var bytes=MoveCodec.Encode(message); var decoded=MoveCodec.Decode(bytes);
            Check(bytes.SequenceEqual(MoveCodec.Encode(decoded)),"Move binary canonical roundtrip "+message.GetType().Name);
            for(int i=0;i<bytes.Length;i++) { var truncated=bytes.Take(i).ToArray(); Reject(()=>MoveCodec.Decode(truncated),"Move truncation "+message.GetType().Name+" "+i); }
            Reject(()=>MoveCodec.Decode(bytes.Concat(new byte[]{0}).ToArray()),"Move trailing bytes");
        }
        Reject(()=>new MoveVector(float.NaN,0,0),"Move NaN");
        Reject(()=>Pose(1000001),"Move bounds");
        Reject(()=>new MovementState(default,default,0),"Move facing");
        Reject(()=>new MoveMirror(Id,1,0,false,MoveRights.All,7,5,1,1,new[]{"base:a","base:a"}),"Move duplicate permission");
        Reject(()=>MoveCodec.Decode(new byte[MoveCodec.MaximumBytes+1]),"Move oversized frame");
        var invalid=MoveCodec.Encode(Mirror()); invalid[0]=2; Reject(()=>MoveCodec.Decode(invalid),"Move future codec version");
        invalid=MoveCodec.Encode(Mirror()); invalid[1]=9; Reject(()=>MoveCodec.Decode(invalid),"Move unknown kind");
        invalid=MoveCodec.Encode(Mirror()); invalid[46]=2; Reject(()=>MoveCodec.Decode(invalid),"Move noncanonical dead bool");

        using(var h=Host())using(var o=Owner(h))
        {
            var a=o.Report(Pose(1)); var b=o.Report(Pose(2));
            Check(h.Receive(b,7,.02)==MoveReportResult.Accepted,"Move accepts latest report");
            Check(h.Receive(a,7,.03)==MoveReportResult.Stale && h.State.Position.X==2,"Move rejects reorder");
            Check(h.Receive(b,7,.04)==MoveReportResult.Stale,"Move rejects duplicate");
            Check(h.Receive(o.Report(Pose(3)),8,.05)==MoveReportResult.WrongOwner,"Move authenticated owner required");
            var foreign=new MovePoseReport(new MoveIdentity(Guid.NewGuid(),301,2),100,1,1,1,Pose(99));
            Check(h.Receive(foreign,7,.05)==MoveReportResult.WrongIdentity && h.State.Position.X==2,"Move session fence");
            foreign=new MovePoseReport(new MoveIdentity(Id.Session,301,1),100,1,1,1,Pose(99));
            Check(h.Receive(foreign,7,.05)==MoveReportResult.WrongIdentity,"Move generation fence");
            h.SetMirror(Mirror(2,rights:MoveRights.Jump));
            Check(h.Receive(o.Report(Pose(4)),7,.06)==MoveReportResult.Constraint,"Move current hard constraint beats stale mirror");
            var correction=h.LastCorrection; Check(correction.Epoch==2,"Move constraint advances epoch once");
            Check(h.Receive(o.Report(Pose(5)),7,.07)==MoveReportResult.Fenced && ReferenceEquals(h.LastCorrection,correction),"Move dropped notice fence retries same correction");
            Check(!o.ReceiveCorrection(correction,false),"Move host authentication");
            Check(o.ReceiveCorrection(correction,true) && !o.CanWrite,"Move pending correction fences Update writes");
            Check(!o.ApplyBoundary((c,m)=>throw new Exception()) && o.AppliedCorrection==1,"Move waits for missing mirror");
            o.ReceiveMirror(h.Mirror,true); int applies=0;
            Check(o.ApplyBoundary((c,m)=>{applies++;Check(!o.CanWrite,"Move gates body during correction");}),"Move applies next boundary");
            Check(!o.ReceiveCorrection(correction,true) && !o.ApplyBoundary((c,m)=>applies++) && applies==1,"Move correction apply once");
            Check(h.Receive(o.Report(Pose(2)),7,.08)==MoveReportResult.Accepted,"Move notice retry recovers fenced owner");
            Check(h.SetMirror(Mirror(3,rights:MoveRights.Jump,speed:8)),"Move speed-only mirror revision");
            Check(!h.SetMirror(Mirror(4,rights:MoveRights.Jump,speed:8)),"Move unchanged mirror no publication");
            Reject(()=>h.SetMirror(Mirror(2,speed:9)),"Move backward mirror rejected");
            o.ReceiveMirror(Mirror(4,scale:0),true);
            var older=new MoveCorrection(Id,3,3,3,Pose(2),new MoveRecovery(new MoveVector(1,3,0),default),MoveCorrectionReason.Constraint);
            o.ReceiveCorrection(older,true); o.ApplyBoundary((c,m)=>Check(m.Revision==4 && m.Scale==0,"Move older correction keeps newer mirror"));
            Check(!o.ReceiveMirror(Mirror(3),true),"Move stale mirror ignored");
            Check(h.Sample(100).State.Position.X==2 && h.Sample(200).State.Position.X==2,"Move host starvation holds pose");
        }
        using(var h=Host())using(var o=Owner(h))
        {
            h.Receive(o.Report(Pose(1)),7,.1); uint first=1;
            h.Receive(o.Report(Pose(2)),7,.2);
            Check(h.RejectBranch(first,false).State.Position.X==1,"Move highest ring anchor <= start sequence");
            uint epoch=h.Epoch;
            Check(h.RejectBranch(first,false).State.Position.X==1 && h.Epoch==MoveOrder.Next(epoch),"Move discarded anchor still fences branch");
            epoch=h.Epoch; Check(h.RejectBranch(first,true)==null && h.Epoch==epoch,"Move later accepted cast not overwritten");
        }
        using(var h=Host())using(var o=Owner(h))
        {
            for(int i=1;i<8;i++)h.Receive(o.Report(Pose(i)),7,i);
            Check(h.RejectBranch(1,false).State.Position.X==7,"Move bounded ring evicted anchor fallback current");
            o.SetReady(false); Check(!o.CanWrite && !o.ReceiveMirror(h.Mirror,true) && !o.ReceiveCorrection(h.LastCorrection,true),"Move disable drops queued dependent work");
            o.SetReady(true); Check(!o.CanWrite,"Move re-enable requires full baseline");
            o.ReceiveMirror(h.Mirror,true); o.ReceiveCorrection(h.LastCorrection,true);o.ApplyBoundary((c,m)=>{});Check(o.CanWrite,"Move coherent rebaseline");
        }
        using(var h=Host())using(var o=new MoveOwnerModel(Id))
        {
            Check(!o.ReceiveMirror(h.Mirror,true) && !o.ReceiveCorrection(h.LastCorrection,true),"Move pre-Ready notice dropped");
            o.SetReady(true);o.ReceiveMirror(h.Mirror,true);o.ReceiveCorrection(h.LastCorrection,true);
            Reject(()=>o.ApplyBoundary((c,m)=>{throw new InvalidOperationException("fixture");}),"Move callback failure surfaced");
            Check(o.NeedsBaseline && !o.CanWrite && o.AppliedCorrection==0,"Move callback failure cannot acknowledge/write");
            Check(o.ApplyBoundary((c,m)=>{}) && o.CanWrite,"Move retry after failed body apply");
        }
        using(var h=Host())
        {
            using(var old=Owner(h))for(int i=1;i<=20;i++)h.Receive(old.Report(Pose(i)),7,i);
            var retiredBaseline=h.LastCorrection;
            var request=new MoveBaselineRequest(Id,1);
            Check(h.RequestBaseline(request,7),"Move owner rebind requests current baseline");var baseline=h.LastCorrection;
            Check(!h.RequestBaseline(new MoveBaselineRequest(Id,2),8),"Move bootstrap authenticated owner fence");
            Check(!h.RequestBaseline(request,7) && ReferenceEquals(baseline,h.LastCorrection),"Move repeated bootstrap request idempotent");
            using(var rebound=new MoveOwnerModel(Id))
            {
                rebound.SetReady(true);rebound.ExpectBaseline(1);rebound.ReceiveMirror(h.Mirror,true);
                Check(!rebound.ReceiveCorrection(retiredBaseline,true) && !rebound.CanWrite,"Move rebind rejects queued prior baseline");
                rebound.ReceiveCorrection(baseline,true);rebound.ApplyBoundary((c,m)=>{});
                var report=rebound.Report(Pose(21));
                Check(report.Sequence==21 && h.Receive(report,7,21)==MoveReportResult.Accepted,"Move rebind resumes report sequence floor");
                rebound.ReceiveCorrection(h.Correct(Pose(22),default,MoveCorrectionReason.Constraint),true);
                Check(!rebound.ApplyBoundary((c,m)=>{rebound.SetReady(false);rebound.SetReady(true);}) && rebound.NeedsBaseline,"Move callback re-entry cannot acknowledge a retired activation");
            }
        }
        using(var h=Host())using(var o=Owner(h))
        {
            o.ReceiveCorrection(new MoveCorrection(Id,2,2,1,Pose(0),default,MoveCorrectionReason.Constraint),true);
            o.ReceiveMirror(Mirror(2,dead:true,life:2),true);
            Check(!o.ApplyBoundary((c,m)=>{}),"Move newer life mirror retires an already pending older life correction");
            Check(!o.CanWrite && o.NeedsBaseline,"Move life transition requires compatible branch baseline");
            Check(!o.ReceiveCorrection(new MoveCorrection(Id,2,2,1,Pose(0),default,MoveCorrectionReason.Constraint),true),"Move old life correction cannot unlock newer life mirror");
            o.ReceiveCorrection(new MoveCorrection(Id,3,3,2,Pose(0),default,MoveCorrectionReason.Death),true);o.ApplyBoundary((c,m)=>{});
            Check(!o.CanWrite,"Move acknowledged dead state still gates input");
            o.ReceiveMirror(Mirror(3,dead:false,life:3),true);
            Check(!o.CanWrite && !o.ReceiveCorrection(new MoveCorrection(Id,4,4,2,Pose(1),default,MoveCorrectionReason.Death),true),"Move revive waits for new life correction");
            o.ReceiveCorrection(new MoveCorrection(Id,5,5,3,Pose(1),default,MoveCorrectionReason.Death),true);o.ApplyBoundary((c,m)=>{});
            Check(o.CanWrite,"Move revived branch resumes after compatible correction");
        }
        using(var observer=new MoveObserverModel(Id))
        {
            var a=new MovePoseState(Id,1,1,1,Pose(0)); var b=new MovePoseState(Id,2,1,2,Pose(10));
            Check(!observer.Receive(a,true),"Move observer pre-Ready");observer.SetReady(true);
            Check(!observer.Receive(a,false) && observer.Receive(a,true) && observer.Receive(b,true),"Move observer trusted samples");
            Check(observer.TrySample(1.5,out var middle) && Math.Abs(middle.Position.X-5)<.001,"Move observer position interpolation with zero physical velocity");
            Check(observer.TrySample(99,out var held) && held.Position.X==10,"Move observer starvation hold");
            Check(!observer.Receive(a,true),"Move observer stale sequence");
            Check(observer.Receive(new MovePoseState(Id,3,2,3,Pose(100)),true) && observer.TrySample(1.5,out var cut) && cut.Position.X==100 && observer.Count==1,"Move epoch cut never sweeps teleport");
            Check(!observer.Receive(new MovePoseState(Id,4,1,4,Pose(-100)),true),"Move observer old epoch despite new sequence");
            for(uint i=4;i<100;i++)observer.Receive(new MovePoseState(Id,i,2,i,Pose(i)),true);
            Check(observer.Count==64,"Move observer bounded history");
            observer.SetReady(false);Check(!observer.TrySample(100,out _),"Move observer disable history cleared");
        }
        // Deterministic scripted transport: binary dispatch, one lost notice, duplicates and reordering.
        using(var h=Host())using(var o=Owner(h))using(var observer=new MoveObserverModel(Id))
        {
            observer.SetReady(true);var reports=new List<byte[]>();
            for(int tick=1;tick<=20;tick++)reports.Add(MoveCodec.Encode(o.Report(Pose(tick))));
            foreach(int index in new[]{2,0,2,7,4,10,19,18})h.Receive((MovePoseReport)MoveCodec.Decode(reports[index]),7,index+.1);
            Check(h.State.Position.X==20,"Move scripted duplicate/reorder latest accepted");
            var c=h.Correct(Pose(5),default,MoveCorrectionReason.Teleport); // first delivery lost
            Check(h.Receive(o.Report(Pose(21)),7,21)==MoveReportResult.Fenced,"Move scripted lost correction fence");
            o.ReceiveCorrection((MoveCorrection)MoveCodec.Decode(MoveCodec.Encode(h.LastCorrection)),true);o.ApplyBoundary((correction,mirror)=>{});
            Check(h.Receive(o.Report(Pose(6)),7,22)==MoveReportResult.Accepted,"Move scripted retry converges");
            observer.Receive((MovePoseState)MoveCodec.Decode(MoveCodec.Encode(h.Sample(22))),true);
            Check(observer.TrySample(23,out var final) && final.Position.X==6,"Move scripted observer converges");
        }
    }
}
