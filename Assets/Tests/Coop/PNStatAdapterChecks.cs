#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Mirror;

namespace ProjectN.Coop.Tests
{
    public static class PNStatAdapterChecks
    {
        public static string Run()
        {
            PNWeaverChecks.Run();int checks=0;
            void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
            void Reject(Action action,string label){try{action();}catch{checks++;return;}throw new Exception(label);}
            var custom=EveId.Parse("addon:rage");
            var frame=new PNStatFrame(8,3,false,new Dictionary<EveId,PNResourceValue>{{custom,new PNResourceValue(.125,80)}});
            var message=new PNStatNotice{NetId=5,Session=Guid.NewGuid(),Entity=7,Generation=2,State=frame};
            var writer=new NetworkWriter();writer.Write(message);var copy=new NetworkReader(writer.ToArray()).Read<PNStatNotice>();
            Check(copy.State.SameValues(frame)&&copy.Entity==7&&copy.Session==message.Session,"actual Weaver typed frame/custom resource");
            writer.Reset();writer.WriteStatFrame(frame);Check(writer.Position==PNStatWire.Size(frame),"actual wire accounting exact");
            var bytes=writer.ToArray();Reject(()=>new NetworkReader(bytes.Take(bytes.Length-1).ToArray()).ReadStatFrame(),"truncated typed frame");
            bytes[2]=4;Reject(()=>new NetworkReader(bytes).ReadStatFrame(),"unsupported frame flags");
            writer.Reset();writer.WriteByte(1);writer.WriteByte(1);writer.WriteByte(0);writer.WriteLong(1);writer.WriteLong(1);writer.WriteByte(0);writer.WriteUShort(129);
            Reject(()=>new NetworkReader(writer.ToArray()).ReadStatFrame(),"count rejected before entries allocation/read");
            Reject(()=>PNStatWire.Size(new PNStatFrame(1,1,false,new Dictionary<EveId,PNResourceValue>{{EveId.Parse("addon:"+new string('a',124)),new PNResourceValue(1,2)}})),"oversized ID");
            var values=new Dictionary<EveId,PNResourceValue>();for(int i=0;i<128;i++)values.Add(EveId.Parse("addon:r"+i),new PNResourceValue(i,128));
            var maximum=new PNStatFrame(1,1,false,values);writer.Reset();writer.WriteStatFrame(maximum);
            Check(new NetworkReader(writer.ToArray()).ReadStatFrame().Resources.Count==128,"maximum resource count roundtrip");
            var empty=new PNStatFrame(1,1,true,new Dictionary<EveId,PNResourceValue>());writer.Reset();writer.WriteStatFrame(empty);
            Check(new NetworkReader(writer.ToArray()).ReadStatFrame().Dead,"None resources retain life");
            writer.Reset();writer.WriteStatFrame(null);Check(new NetworkReader(writer.ToArray()).ReadStatFrame()==null,"non-Stat actor marker");
            var target=new PNStateFence{Attempt=9,NetId=5,Entity=7,Generation=2,Revision=8,Digest=new string('a',64)};
            writer.Reset();writer.Write(target);var fence=new NetworkReader(writer.ToArray()).Read<PNStateFence>();
            Check(fence.Attempt==9&&fence.Revision==8&&fence.Generation==2,"actual Weaver grace target");
            return "PASS "+checks+" P2 wire/validation checks";
        }
    }
}
#endif
