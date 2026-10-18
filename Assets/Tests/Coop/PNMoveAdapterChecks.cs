#if UNITY_EDITOR
using System;
using System.Linq;
using EveEngine.Movement;
using Mirror;

namespace ProjectN.Coop.Tests
{
    public static class PNMoveAdapterChecks
    {
        public static string Run()
        {
            PNWeaverChecks.Run();int checks=0;
            void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
            void Reject(Action run,string name){try{run();}catch{checks++;return;}throw new Exception("Expected rejection: "+name);}
            var s=new PNMoveStream{Scope=new PNWorldBindingScope{World=Guid.NewGuid(),Incarnation=2,Membership=3,Subscription=4},Session=Guid.NewGuid(),NetId=5,Entity=42,Generation=3,Nonce=12};
            var state=new MovementState(new MoveVector(2,3,4),new MoveVector(0,4,0),-1,false,1);
            var mirror=new MoveMirror(s.Identity,3,2,false,MoveRights.All,5,7,.5f,1,new[]{"base:voluntary_motion"});
            var correction=new MoveCorrection(s.Identity,2,2,3,state,new MoveRecovery(new MoveVector(0,8,0),default,true),MoveCorrectionReason.Constraint,20,7);
            for(byte kind=0;kind<5;kind++)
            {
                var p=new PNMovePacket{Kind=kind,Stream=s,Request=7,Revision=9,Life=2,Mirror=mirror,Correction=correction,Pose=new MovePoseState(s.Identity,15,2,1.5,state),Report=new MovePoseReport(s.Identity,21,2,3,2,state)};
                var writer=new NetworkWriter();writer.Write(p);var bytes=writer.ToArray();var reader=new NetworkReader(bytes);var q=reader.Read<PNMovePacket>();
                Check(q.Kind==kind&&q.Request==7&&q.Stream.Equals(s)&&q.Revision==9&&q.Life==2&&reader.Remaining==0,"actual Weaver packet "+kind);
                Check(bytes.Length<(kind<=2?PNMoveWire.ControlBytes:PNMoveWire.PoseBytes),"bounded packet "+kind);
                var body=kind<=1?q.Correction.State:kind<=3?q.Pose.State:q.Report.State;
                Check(body.Facing==-1&&body.VerticalView==1&&body.Position.Near(state.Position),"pose fields "+kind);
                bytes[0]=9;Reject(()=>new NetworkReader(bytes).Read<PNMovePacket>(),"schema "+kind);
            }
            var request=new PNMoveRequest{Stream=s,Request=77};var w=new NetworkWriter();w.Write(request);var req=new NetworkReader(w.ToArray()).Read<PNMoveRequest>();Check(req.Request==77&&req.Stream.Equals(s),"request Weaver");
            Reject(()=>PNMoveWire.Validate(new PNMovePacket{Kind=3,Stream=s,Request=1,Revision=0,Pose=new MovePoseState(s.Identity,1,1,0,state)}),"zero revision");
            var bad=s;bad.Nonce=0;Reject(()=>bad.Validate(),"zero binding nonce");
            Reject(()=>new MoveMirror(s.Identity,1,0,false,MoveRights.All,1,1,1,1,Enumerable.Range(0,65).Select(i=>"addon:p"+i)),"permission count");
            Reject(()=>new MoveVector(float.NaN,0,0),"nonfinite vector");
            Reject(()=>new MovementState(new MoveVector(1000001,0,0),default),"coordinate bound");
            var owner=new MoveOwnerModel(s.Identity);owner.SetReady(true);owner.ExpectBaseline(7);owner.ReceiveMirror(mirror,true);owner.ReceiveCorrection(correction,true);
            Check(!owner.CanWrite,"pending correction gates writer");owner.ApplyBoundary((c,m)=>{});Check(owner.CanWrite&&owner.AppliedCorrection==2,"applied fixed target");
            owner.ExpectBaseline(8);Check(!owner.ReceiveCorrection(correction,true),"old request cannot rebind");owner.Dispose();
            var observer=new MoveObserverModel(s.Identity);observer.SetReady(true);observer.Receive(new MovePoseState(s.Identity,1,1,1,state),true);observer.Receive(new MovePoseState(s.Identity,2,2,2,state.WithVelocity(default)),true);
            Check(observer.Count==1&&observer.TrySample(100,out var last)&&last.Position.Near(state.Position),"epoch cut and starvation hold independent of algorithm");observer.Dispose();
            observer=new MoveObserverModel(s.Identity);observer.SetReady(true);
            for(uint i=1;i<=3;i++){float t=i-1;observer.Receive(new MovePoseState(s.Identity,i,1,t*.05,new MovementState(new MoveVector(t,0,t*t),default)),true);}
            Check(observer.TrySample(.075,out var curved)&&Math.Abs(curved.Position.X-1.5f)<.01&&Math.Abs(curved.Position.Z-2.5f)<.01,"nonlinear position producer needs no algorithm message or physical velocity extrapolation");
            for(uint i=4;i<100;i++)observer.Receive(new MovePoseState(s.Identity,i,1,i*.05,state),true);Check(observer.Count==64,"observer history remains bounded");observer.Dispose();
            return "PASS "+checks+" P4 actual Weaver/wire/model checks";
        }
    }
}
#endif
