using System;
using Mirror;
namespace ProjectN.Coop.Tests
{
    public static class PNWorldClockChecks
    {
        public static string Run()
        {
            int checks=0;void Check(bool ok){if(!ok)throw new Exception("World clock wire check "+checks);checks++;}
            void Reject(Action a){bool failed=false;try{a();}catch(Exception){failed=true;}Check(failed);}
            var ticket=new PNWorldTicket{Room=Guid.NewGuid(),Play=Guid.NewGuid(),World=Guid.NewGuid(),RoomIncarnation=1,PlayRevision=2,Incarnation=3,Subscription=4};
            var packet=new PNWorldClock{Ticket=ticket,Revision=1,Stamp=30,Value=12,Rate=.5,Baseline=true};
            using(var writer=NetworkWriterPool.Get())
            {
                writer.Write(packet);var copy=new NetworkReader(writer.ToArray()).Read<PNWorldClock>();
                Check(copy.Ticket.Equals(ticket)&&copy.Revision==1&&copy.Stamp==30&&copy.Value==12&&copy.Rate==.5&&copy.Baseline);
                Check(writer.Position<=256);packet.Revision=2;packet.Rate=0;packet.Baseline=false;writer.Reset();writer.Write(packet);copy=new NetworkReader(writer.ToArray()).Read<PNWorldClock>();Check(copy.Rate==0&&!copy.Baseline);
                foreach(double rate in new[]{double.NaN,double.PositiveInfinity,-1}){packet.Rate=rate;Reject(()=>{writer.Reset();writer.WriteWorldClock(packet);});}
                packet.Rate=1;packet.Revision=0;Reject(()=>{writer.Reset();writer.WriteWorldClock(packet);});packet.Revision=1;
                packet.Ticket.Incarnation=ulong.MaxValue;Reject(()=>{writer.Reset();writer.WriteWorldClock(packet);});packet.Ticket=ticket;
                writer.Reset();writer.WriteWorldClock(packet);var bytes=writer.ToArray();bytes[bytes.Length-1]=2;Reject(()=>new NetworkReader(bytes).ReadWorldClock());
                Reject(()=>new NetworkReader(new byte[257]).ReadWorldClock());
            }
            return "PASS "+checks+" world clock wire cases";
        }
    }
}
