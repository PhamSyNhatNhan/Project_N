using System;
using System.Linq;
using EveEngine;
using Mirror;
using UnityEngine;
namespace ProjectN.Coop
{
    public struct PNWorldClock : NetworkMessage
    {
        public PNWorldTicket Ticket; public long Revision; public double Stamp,Value,Rate; public bool Baseline;
        public void Validate(){Ticket.Validate();if(Revision<1||Ticket.Incarnation>long.MaxValue)throw new FormatException("World clock revision/incarnation.");PNTimeMath.Nonnegative(Stamp);PNTimeMath.Nonnegative(Value);PNTimeMath.Nonnegative(Rate);}
    }
    public struct PNWorldClockRequest : NetworkMessage { public PNWorldTicket Ticket; }
    public static class PNWorldClockWire
    {
        public static void WriteWorldClock(this NetworkWriter w,PNWorldClock p){p.Validate();w.WriteWorldTicket(p.Ticket);w.WriteLong(p.Revision);w.WriteDouble(p.Stamp);w.WriteDouble(p.Value);w.WriteDouble(p.Rate);w.WriteByte((byte)(p.Baseline?1:0));}
        public static PNWorldClock ReadWorldClock(this NetworkReader r){if(r.Remaining>256)throw new FormatException("World clock bound.");var p=new PNWorldClock{Ticket=r.ReadWorldTicket(),Revision=r.ReadLong(),Stamp=r.ReadDouble(),Value=r.ReadDouble(),Rate=r.ReadDouble()};byte baseline=r.ReadByte();if(baseline>1)throw new FormatException("Clock baseline flag.");p.Baseline=baseline==1;p.Validate();return p;}
    }
    public sealed partial class PNNetworkManager
    {
        private const string WorldClockKind="project_n:world_clock";
        private readonly System.Collections.Generic.Dictionary<Guid,PNWorldClock> preparedWorldClocks=new System.Collections.Generic.Dictionary<Guid,PNWorldClock>();
        private readonly System.Collections.Generic.HashSet<Guid> requestedWorldClocks=new System.Collections.Generic.HashSet<Guid>();
        private static PNClockKey ClockKey(PNWorldTicket ticket)=>new PNClockKey(ticket.Play,ticket.World,checked((long)ticket.Incarnation),PNClockDomain.WorldGame);
        private bool WorldClockPrepared(Guid world)=>preparedWorldClocks.TryGetValue(world,out var clock)&&clientWorlds.TryGetValue(world,out var entry)&&entry.Offer.Ticket.Equals(clock.Ticket)&&!requestedWorldClocks.Contains(world);
        public PNClockRead ReadWorldPresentationClock(Guid world,out double value)
        {
            value=0;if(subscribedSession==null)return PNClockRead.MissingBaseline;
            if(NetworkServer.active){if(subscribedSession.Worlds.Snapshot().FirstOrDefault(x=>x.InstanceId==world) is EveWorldContext authority){value=authority.GameplayTime;return PNClockRead.Ready;}return PNClockRead.MissingBaseline;}
            if(!WorldClockPrepared(world))return PNClockRead.MissingBaseline;
            var service=PNTimeService.For(subscribedSession);var time=service.Sample();if(!time.Ready)return PNClockRead.MissingBaseline;
            return service.History.Read(ClockKey(preparedWorldClocks[world].Ticket),time.Presentation,out value);
        }
        private void SendWorldClock(WorldSubscription entry,bool baseline)
        {
            if(entry.Connection is LocalConnectionToClient)return;
            var world=entry.World;double rate=world.CanExecute?Time.timeScale:0;
            bool restored=entry.ClockRestoreRevision!=world.ClockRestoreRevision;
            if(!baseline&&!restored&&entry.ClockRevision>0&&entry.ClockRate==rate)return;
            entry.ClockRestoreRevision=world.ClockRestoreRevision;
            entry.ClockRate=rate;
            var clock=new PNWorldClock{Ticket=entry.Offer.Ticket,Revision=checked(++entry.ClockRevision),Stamp=NetworkTime.localTime,Value=world.GameplayTime,Rate=rate,Baseline=baseline||restored||entry.ClockRevision==1};
            entry.Connection.Send(clock);
        }
        private void ReceiveWorldClockRequest(NetworkConnectionToClient conn,PNWorldClockRequest request)
        {
            if(!IsPlaying||!MatchTicket(request.Ticket)||!worldPeers.TryGetValue((conn.connectionId,request.Ticket.World),out var entry)||!entry.Offer.Ticket.Equals(request.Ticket))return;
            // Coalesce requests; capture once at the next drained LateUpdate boundary.
            entry.ClockBaselineRequested=true;
        }
        private void ReceiveWorldClock(PNWorldClock packet)
        {
            if(NetworkServer.active||!MatchTicket(packet.Ticket))return;
            try
            {
                packet.Validate();Guid id=packet.Ticket.World;
                PNWorldTicket expected=clientWorlds.TryGetValue(id,out var entry)?entry.Offer.Ticket:waitingWorldOffers.TryGetValue(id,out var offer)?offer.Ticket:default;
                if(!packet.Ticket.Equals(expected))return;
                bool known=preparedWorldClocks.TryGetValue(id,out var old)&&old.Ticket.Equals(packet.Ticket);
                if(known&&packet.Revision<old.Revision)return;
                if(known&&packet.Revision==old.Revision){if(packet.Stamp!=old.Stamp||packet.Value!=old.Value||packet.Rate!=old.Rate)throw new FormatException("Conflicting world clock revision.");return;}
                if(!known&&!packet.Baseline){RequestWorldClock(packet.Ticket);return;}
                var service=PNTimeService.For(subscribedSession);var sample=service.Sample();
                // Preparation is independent of input readiness. The authenticated batch is a host-time observation.
                double hostNow=sample.Ready?Math.Max(sample.Host,NetworkClient.connection?.remoteTimeStamp??0):NetworkClient.connection?.remoteTimeStamp??0;
                var result=service.History.Add(new PNClockAnchor(ClockKey(packet.Ticket),packet.Revision,packet.Stamp,packet.Value,packet.Rate),hostNow,sample.Ready?sample.Uncertainty:0,sample.Ready?sample.Presentation:0,packet.Baseline);
                if(result!=PNClockRead.Ready){RequestWorldClock(packet.Ticket);return;}
                if(preparedWorldClocks.Count>=8&&!preparedWorldClocks.ContainsKey(id))throw new FormatException("World clock count bound.");
                preparedWorldClocks[id]=packet;requestedWorldClocks.Remove(id);
                foreach(var binding in bindings.Values.Where(x=>x.Metadata.World==id))SetInput(binding,clientInputReady);
            }
            catch(Exception error){ProtocolFailure(error,false);}
        }
        private void RequestWorldClock(PNWorldTicket ticket)
        {
            if(requestedWorldClocks.Add(ticket.World))NetworkClient.Send(new PNWorldClockRequest{Ticket=ticket});
            foreach(var binding in bindings.Values.Where(x=>x.Metadata.World==ticket.World))SetInput(binding,false);
        }
        private void ForgetWorldClock(PNWorldTicket ticket)
        {
            if(preparedWorldClocks.TryGetValue(ticket.World,out var clock)&&clock.Ticket.Equals(ticket))
            {if(subscribedSession?.IsActive==true){var service=PNTimeService.For(subscribedSession);service.Sample();service.History.Remove(ClockKey(ticket));}preparedWorldClocks.Remove(ticket.World);}
            requestedWorldClocks.Remove(ticket.World);
        }
    }
}
