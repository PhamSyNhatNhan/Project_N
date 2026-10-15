using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine.Experimental.Lifecycle;
using static EveSessionSmokeTests;

public static class OptionalReplicationTests
{
    private static ModelPolicy Policy(int messages=32,int bytes=8192,int accepted=8192)
        => new ModelPolicy(8,4096,1024,512,8,messages,bytes,accepted,16,16,3,1000,1000,1000,1000,4000,12000);
    public static void Run()
    {
        var session=Guid.NewGuid();var key=new PresenceKey(1,1);double now=0;
        var limits=Policy();var optional=new OptionalReplicationPolicy(4,2048,4,2048,1024);
        HostModel Create(OptionalReplicationPolicy p=null,ModelPolicy m=null) {
            var host=new HostModel(session,1,m??limits,now,p??optional);host.Admit(true,true,now);return host;
        }
        void Live(HostModel host) {
            host.Capture(Array.Empty<ActorState>(),now);
            while(host.TrySend(16,_=>true,now))host.ReleaseAccepted((int)host.AcceptedBytes);
            host.Applied(1,host.SnapshotId,host.EndSequence,now);
            Check(host.Phase==HostPhase.Live,"optional fixture live");
        }
        var h=Create();
        Check(h.TryDeltaOptional(MessageKind.Updated,key,new byte[1],now)==OptionalAdmission.NotLive,"optional denied before Live");
        Live(h);
        var packet=new byte[33];int wire=Envelope.HeaderBytes+packet.Length;
        Check(h.TryDeltaOptional(MessageKind.Updated,key,packet,now)==OptionalAdmission.Admitted,"optional admitted");
        Check(h.OptionalQueuedMessages==1 && h.OptionalQueuedBytes==wire,"optional occupancy exact");
        byte[][] first=null,second=null;
        h.TrySend(16,b=>{first=b.Select(x=>(byte[])x.Clone()).ToArray();b[0][0]^=1;return false;},now);
        for(int i=0;i<3;i++)h.TryDeltaOptional(MessageKind.Updated,key,packet,now);
        Check(h.TryDeltaOptional(MessageKind.Updated,key,packet,now)==OptionalAdmission.Budget,"optional message cap");
        h.TrySend(16,b=>{second=b.Select(x=>(byte[])x.Clone()).ToArray();return false;},now);
        Check(first.Length==second.Length && first.Zip(second,(a,b)=>a.SequenceEqual(b)).All(x=>x),"pending retries immutable with newer optional");
        h.TrySend(16,_=>true,now);
        Check(h.OptionalQueuedMessages==3 && h.OptionalQueuedBytes==3L*wire && h.AcceptedBytes==wire,"accept releases queue occupancy once");
        h.ReleaseAccepted(wire-1);Check(h.AcceptedBytes==1,"aggregate partial release no FIFO assumption");
        h.IntentResult(new byte[800],now);long controls=h.ControlQueuedBytes;
        h.RequestSnapshot(now);
        Check(h.OptionalQueuedMessages==0 && h.OptionalQueuedBytes==0 && h.ControlQueuedBytes==controls && h.AcceptedBytes==1,"recovery clears optional queue but retains controls and accepted");
        Check(h.TryDeltaOptional(MessageKind.Updated,key,packet,now)==OptionalAdmission.NotLive,"recovery optional denied");
        Live(h);Check(h.ControlQueuedBytes==0 && h.AcceptedBytes==0,"recovery snapshot control drains once");
        h.Close();Check(h.OptionalQueuedMessages==0 && h.OptionalQueuedBytes==0 && h.ControlQueuedBytes==0 && h.AcceptedBytes==0,"close occupancy empty");

        // Exact accepted commitment edge, including controls, even across refused pending.
        int unit=Envelope.HeaderBytes+100;
        var edge=Create(new OptionalReplicationPolicy(8,4096,0,0,8192-unit*2));Live(edge);
        edge.IntentResult(new byte[100],now);
        edge.TrySend(16,_=>false,now);
        // Budget leaves exactly 2 units; pending control is counted once via ControlQueuedBytes.
        Check(edge.TryDeltaOptional(MessageKind.Updated,key,new byte[100],now)==OptionalAdmission.Admitted,"control+optional exact commitment edge");
        Check(edge.TryDeltaOptional(MessageKind.Updated,key,new byte[1],now)==OptionalAdmission.Commitment,"one more entry exceeds commitment");
        var edgeOver=Create(new OptionalReplicationPolicy(8,4096,0,0,8192-unit*2));Live(edgeOver);
        edgeOver.IntentResult(new byte[100],now);
        Check(edgeOver.TryDeltaOptional(MessageKind.Updated,key,new byte[101],now)==OptionalAdmission.Commitment,"control commitment refuses exactly one byte over edge");
        var bytes=Create(new OptionalReplicationPolicy(8,wire,0,0,0));Live(bytes);
        Check(bytes.TryDeltaOptional(MessageKind.Updated,key,packet,now)==OptionalAdmission.Admitted && bytes.TryDeltaOptional(MessageKind.Updated,key,new byte[1],now)==OptionalAdmission.Budget,"optional byte budget edge");
        bool invalid=false;try{Create(new OptionalReplicationPolicy(8,4096,31,0,0));}catch(ArgumentException){invalid=true;}
        Check(invalid,"inconsistent optional policy rejected");
        var reserve=Create(new OptionalReplicationPolicy(1,4096,31,0,0));Live(reserve);
        reserve.Delta(MessageKind.Updated,key,new byte[1],now);
        Check(reserve.TryDeltaOptional(MessageKind.Updated,key,new byte[1],now)==OptionalAdmission.Reserve,"required message reserve edge");
        var byteReserve=Create(new OptionalReplicationPolicy(4,wire,0,8192-wire,0));Live(byteReserve);
        Check(byteReserve.TryDeltaOptional(MessageKind.Updated,key,packet,now)==OptionalAdmission.Admitted,"queue byte reserve exact edge");
        foreach(int extra in new[]{0,1}){
            var queueEdge=Create(new OptionalReplicationPolicy(4,2048,0,2048,0));Live(queueEdge);
            for(int i=0;i<5;i++)queueEdge.Delta(MessageKind.Updated,key,new byte[1024],now);
            Check(queueEdge.TryDeltaOptional(MessageKind.Updated,key,new byte[592+extra],now)==(extra==0?OptionalAdmission.Admitted:OptionalAdmission.Reserve),"queued reserve exact edge plus "+extra);
        }
        // Pending optional is a view, not another byte commitment.
        var pending=Create(new OptionalReplicationPolicy(8,4096,0,0,8192-unit*2));Live(pending);
        pending.TryDeltaOptional(MessageKind.Updated,key,new byte[100],now);pending.TrySend(16,_=>false,now);
        Check(pending.TryDeltaOptional(MessageKind.Updated,key,new byte[100],now)==OptionalAdmission.Admitted,"pending delta counted exactly once at edge");
        var weak=Create(new OptionalReplicationPolicy(1,1024,1,0,0),Policy(2));Live(weak);
        weak.TryDeltaOptional(MessageKind.Updated,key,new byte[1],now);
        weak.Delta(MessageKind.Updated,key,new byte[1],now);weak.Delta(MessageKind.Updated,key,new byte[1],now);
        Check(weak.Phase==HostPhase.WaitCapture && weak.OptionalQueuedBytes==0,"weak guarantee: later required can overflow and retire optional");
        var bigControl=Create();Live(bigControl);
        for(int i=0;i<9;i++)bigControl.IntentResult(new byte[1024],now);
        Check(bigControl.ControlQueuedBytes>8192 && bigControl.TryDeltaOptional(MessageKind.Updated,key,new byte[1],now)==OptionalAdmission.Commitment,"large control backlog blocks optional conservatively");
        var seq=Create();Live(seq);long expected=seq.SentSequence;
        var encoded=new List<byte[]>();
        seq.TryDeltaOptional(MessageKind.Updated,key,new byte[1],now);
        seq.Delta(MessageKind.Updated,key,new byte[2],now);
        while(seq.TrySend(16,b=>{encoded.AddRange(b);return true;},now))seq.ReleaseAccepted((int)seq.AcceptedBytes);
        Check(encoded.All(b=>ModelCodec.Decode(b,limits).Sequence==++expected),"mixed stream contiguous actor sequence");
        Check(seq.OptionalQueuedBytes==0 && seq.OptionalQueuedMessages==0 && seq.RequiredAccepted==1,"mixed accept counters by class");
        List<int> RequiredTrace(bool admission) {
            var model=admission?Create(new OptionalReplicationPolicy(1,1024,1,1024,1024)):new HostModel(session,1,limits,now);
            if(!admission)model.Admit(true,true,now);Live(model);var values=new List<int>();
            for(int i=0;i<100;i++){
                byte[] payload={(byte)(i%3==0?1:0),(byte)i};
                if(admission && payload[0]==1)model.TryDeltaOptional(MessageKind.Updated,key,payload,now);
                else model.Delta(MessageKind.Updated,key,payload,now);
                if(i%5==4)while(model.TrySend(16,b=>{foreach(var bytes in b){var item=ModelCodec.Decode(bytes,limits);if(item.Kind==MessageKind.Updated && item.Payload[0]==0)values.Add(item.Payload[1]);}return true;},now))model.ReleaseAccepted((int)model.AcceptedBytes);
            }
            return values;
        }
        var baselineTrace=RequiredTrace(false);var admittedTrace=RequiredTrace(true);
        Check(baselineTrace.Count==66 && baselineTrace.SequenceEqual(admittedTrace),"deterministic required event sequence exactly equal with admission on/off");
    }
}
