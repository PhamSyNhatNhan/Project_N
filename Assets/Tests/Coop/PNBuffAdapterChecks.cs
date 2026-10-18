#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EveEngine;
using Mirror;
using Newtonsoft.Json.Linq;

namespace ProjectN.Coop.Tests
{
    public static class PNBuffAdapterChecks
    {
        public static string Run()
        {
            PNWeaverChecks.Run();
            int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
            void Reject(Action action,string name){try{action();}catch{checks++;return;}throw new Exception("Expected rejection: "+name);}
            var id=EveId.Parse("test:p3");var session=Guid.NewGuid();var entity=new EveEntityId(7);
            var data=new JObject{["private_visual"]=new JArray(long.MaxValue,null,1.25,"custom")};
            var state=new PNBuffWireState(12,4,id,id,entity,null,false,new PNBuffNumbers(long.MaxValue,0,null,long.MinValue),PNBuffClock.Entity,2,3,.5,null,data);
            var groups=new Dictionary<EveId,long>{{id,8}};
            var frame=new PNBuffFrame(new PNBuffWireMessage(session,entity,3,8,PNBuffWireKind.Snapshot,new[]{state}),3,groups);
            var stream=new PNBuffStream{Scope=new PNWorldBindingScope{World=Guid.NewGuid(),Incarnation=2,Membership=3,Subscription=4},Session=session,NetId=5,Entity=7,Generation=3,Nonce=11};
            var body=PNBuffWire.Encode(frame);
            var packet=new PNBuffPacket{Stream=stream,Transfer=1,Kind=0,Revision=8,Time=3,HostTime=4,Rate=1,Total=body.Length,Index=0,Count=1,Body=body};
            using(var writer=NetworkWriterPool.Get())
            {
                writer.Write(packet);var copy=new NetworkReader(writer.ToArray()).Read<PNBuffPacket>();
                Check(copy.Stream.Equals(stream)&&copy.Body.SequenceEqual(body),"actual Weaver packet and custom bounded reader");
                var decoded=PNBuffWire.DecodeFrame(copy.Body,copy);
                Check(decoded.State.States[0].Numbers.TopLeft==long.MaxValue&&decoded.State.States[0].Numbers.BottomRight==long.MinValue,"nullable Int64 wire exact");
                Check(decoded.State.States[0].Stacks==null&&decoded.State.States[0].Expiration==null,"custom Buff no stack/no expiry");
                Check(JToken.DeepEquals(decoded.State.States[0].DynamicData,data)&&decoded.Groups[id]==8,"custom payload and canonical group order");
            }
            var removed=new PNBuffChange(new PNBuffWireMessage(session,entity,3,9,PNBuffWireKind.Removed,removedId:12,endReason:PNBuffEndReason.Expired,cues:new[]{new PNBuffCue(1,12,"end",4)}),state,4,new Dictionary<EveId,long>());
            packet.Kind=1;packet.Revision=9;var change=PNBuffWire.DecodeChange(PNBuffWire.Encode(removed),packet);
            Check(change.FinalState.InstanceId==12&&change.State.EndReason==PNBuffEndReason.Expired&&change.State.Cues[0].Name=="end","removal final state/reason/required cue");
            var empty=new PNBuffFrame(new PNBuffWireMessage(session,entity,3,0,PNBuffWireKind.Snapshot),0,new Dictionary<EveId,long>());
            packet.Revision=0;packet.Time=0;Check(PNBuffWire.DecodeFrame(PNBuffWire.Encode(empty),packet).State.States.Count==0,"empty baseline zero revision");
            Reject(()=>PNBuffWire.DecodeFrame(body.Take(body.Length-1).ToArray(),packet),"truncated body");
            Reject(()=>PNBuffWire.DecodeFrame(body.Concat(new byte[]{0}).ToArray(),packet),"trailing body");
            var many=new List<PNBuffWireState>();for(int i=0;i<12;i++)many.Add(new PNBuffWireState(i+1,1,id,id,entity,null,false,default,PNBuffClock.Game,0,0,1,null,new JObject{["x"]=new string('x',4000)}));
            var large=PNBuffWire.Encode(new PNBuffFrame(new PNBuffWireMessage(session,entity,3,3,PNBuffWireKind.Snapshot,many),0,groups));
            Check(large.Length>32768,"baseline genuinely exceeds old32KiB latest state slot");
            var header=packet;header.Revision=3;int nodes=0;var decode=new PNBuffWire.FrameDecoder(large,header,n=>nodes+=n);
            Check(!decode.Advance()&&decode.Result==null,"large frame decode yields after bounded state quantum");
            Check(decode.Advance()&&decode.Result.State.States.Count==12&&nodes>0,"decoded DOM nodes charged before allocation");
            var denied=new PNBuffWire.FrameDecoder(large,header,_=>throw new InvalidOperationException("quota"));Reject(()=>denied.Advance(),"DOM reservation rejects before payload construction");
            int reserved=0;int count=(large.Length+PNBuffWire.ChunkBytes-1)/PNBuffWire.ChunkBytes;
            PNBuffPacket Part(int index){var bytes=large.Skip(index*PNBuffWire.ChunkBytes).Take(PNBuffWire.ChunkBytes).ToArray();return new PNBuffPacket{Stream=stream,Transfer=2,Kind=0,Revision=3,Total=large.Length,Index=index,Count=count,Body=bytes};}
            using(var assembly=new PNBuffBaselineAssembly(Part(0),0,size=>{reserved+=size;return new PNBuffRelease(()=>reserved-=size);}))
            {
                Check(!assembly.Complete&&reserved>large.Length,"reserve peak ownership before assembly allocation");
                Reject(()=>assembly.Body(),"partial baseline cannot apply");
                Reject(()=>assembly.Add(Part(0)),"duplicate conflicting position rejected");
                for(int i=1;i<count;i++)assembly.Add(Part(i));
                Check(assembly.Complete&&assembly.Body().SequenceEqual(large),"multi-chunk atomic assembly exact bytes");
            }
            Check(reserved==0,"assembly disposal releases quota");
            Reject(()=>new PNBuffBaselineAssembly(Part(1),0,_=>throw new Exception("must validate first")),"first fragment must be zero");
            var invalid=Part(0);invalid.Total=int.MaxValue;Reject(()=>PNBuffWire.ValidatePacket(invalid),"declared size bound before reserve");
            var inbox=new EveBindingInbox{MaximumTotalBytes=100};inbox.Reserve(1,1,2);var lease=inbox.ReserveBytes(1,1,98);
            Reject(()=>inbox.ReserveBytes(1,1,1),"assembly shares pending quota");lease.Dispose();lease.Dispose();Check(inbox.TotalBytes==2,"quota lease idempotent");inbox.Release(1,1);
            Check(inbox.TotalBytes==0,"no pending accounting after retirement");
            return "PASS "+checks+" P3 wire/Weaver/assembly checks";
        }
    }
}
#endif
