#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using UnityEngine;
namespace ProjectN.Coop.Tests
{
    public static class PNWorldWireChecks
    {
        public static string Run()
        {
            PNWeaverChecks.Run();int count=0;var evidence=new List<string>();
            void Check(bool value,string label){if(!value)throw new Exception(label);count++;evidence.Add("PASS "+label);}
            void Reject(Action action,string label){try{action();}catch{Check(true,label);return;}throw new Exception(label);}
            var ticket=new PNWorldTicket{Room=Guid.NewGuid(),Play=Guid.NewGuid(),World=Guid.NewGuid(),RoomIncarnation=2,PlayRevision=3,Incarnation=4,Subscription=5};
            var offer=new PNWorldOffer{Ticket=ticket,Definition="fixture:world",ContentHash=new string('a',64),Active=true,DefaultWorld=true,Paused=true};
            var writer=new NetworkWriter();writer.Write(offer);var copy=new NetworkReader(writer.ToArray()).Read<PNWorldOffer>();
            Check(copy.Ticket.Equals(ticket)&&copy.Definition==offer.Definition&&copy.ContentHash==offer.ContentHash&&copy.Active&&copy.DefaultWorld&&copy.Paused,"actual Weaver bounded descriptor roundtrip");
            Check(writer.Position<PNWorldWire.ControlBytes,"descriptor stays in control message bound");
            var stale=ticket;stale.PlayRevision--;Check(!stale.Equals(ticket),"play revision is part of world ticket");stale=ticket;stale.Subscription--;Check(!stale.Equals(ticket),"subscription revision is part of world ticket");stale=ticket;stale.Incarnation--;Check(!stale.Equals(ticket),"world incarnation is part of ticket");
            var bad=offer;bad.ContentHash=new string('g',64);Reject(()=>{writer.Reset();writer.Write(bad);},"nonhex world hash rejected");
            bad=offer;bad.Definition="fixture:"+new string('x',129);Reject(()=>{writer.Reset();writer.Write(bad);},"oversized descriptor ID rejected");
            writer.Reset();writer.WriteWorldTicket(ticket);writer.WriteUShort(129);Reject(()=>new NetworkReader(writer.ToArray()).ReadWorldOffer(),"oversized UTF8 length rejected before allocation");
            Reject(()=>new NetworkReader(new byte[PNWorldWire.ControlBytes+1]).ReadWorldOffer(),"world control over16KiB rejected before header allocation");
            var scope=new PNWorldBindingScope{World=ticket.World,Incarnation=4,Membership=8,Subscription=5};
            var move=new PNMoveRequest{Request=2,Stream=new PNMoveStream{Session=ticket.Play,NetId=6,Entity=7,Generation=8,Nonce=9,Scope=scope}};
            writer.Reset();writer.Write(move);var moved=new NetworkReader(writer.ToArray()).Read<PNMoveRequest>();Check(moved.Stream.Equals(move.Stream),"Move request carries complete subscription/membership scope");
            var buff=new PNBuffStream{Session=ticket.Play,NetId=6,Entity=7,Generation=8,Nonce=9,Scope=scope};writer.Reset();writer.Write(buff);Check(new NetworkReader(writer.ToArray()).Read<PNBuffStream>().Equals(buff),"Buff stream carries same scope");
            var previous=scope;previous.Subscription--;Check(!scope.Equals(previous),"same actor/nonce with old subscription is a different stream");
            var target=new PNStateFence{Session=ticket.Play,NetId=6,Entity=7,Generation=8,Adapter=2,Nonce=9,Revision=10,Digest=new string('b',64),Scope=scope};
            var fence=new PNWorldFence{Ticket=ticket,Revision=11,Target=target};writer.Reset();writer.Write(fence);var fenced=new NetworkReader(writer.ToArray()).Read<PNWorldFence>();Check(fenced.Ticket.Equals(ticket)&&fenced.Revision==11&&fenced.Target.Scope.Equals(scope)&&fenced.Target.Revision==10,"world fixed applied target roundtrip");
            var applied=new PNWorldApplied{Ticket=ticket,Revision=11,Digest=target.Digest,Confirmed=true,InitialReady=true};writer.Reset();writer.Write(applied);var confirmed=new NetworkReader(writer.ToArray()).Read<PNWorldApplied>();Check(confirmed.Confirmed&&confirmed.InitialReady&&confirmed.Ticket.Equals(ticket)&&confirmed.Digest==target.Digest,"host confirmation is explicit and revisioned");
            var root=new GameObject("Disposable world interest and shared ledger check");root.SetActive(false);
            try
            {
                var manager=root.AddComponent<PNNetworkManager>();var interest=root.AddComponent<PNWorldInterestManagement>();interest.Manager=manager;
                var connection=new NetworkConnectionToClient(991){isAuthenticated=true,authenticationData=new PNPeer{Owner=1}};
                var actor=new GameObject("Unsubscribed authority actor");actor.SetActive(false);
                try
                {
                    var identity=actor.AddComponent<NetworkIdentity>();actor.AddComponent<PNActorShell>().Metadata=new PNActorMetadata{World=ticket.World,WorldIncarnation=4};
                    Check(!interest.OnCheckObserver(identity,connection),"initial actor denied without an authenticated world subscription");
                    Check(typeof(PNWorldInterestManagement).BaseType==typeof(InterestManagementBase),"world interest bypasses Mirror automatic authority-owner inclusion");
                }
                finally{UnityEngine.Object.DestroyImmediate(actor);}
                var reserve=typeof(PNNetworkManager).GetMethod("ReserveWorld",BindingFlags.NonPublic|BindingFlags.Instance);
                var lease=(IDisposable)reserve.Invoke(manager,new object[]{512});
                Check(manager.PendingWorldBytes==512&&manager.PendingBuffBytes==0&&manager.PendingMoveBytes==0,"world descriptors share the required resource ledger without adapter double counting");lease.Dispose();lease.Dispose();
                Check(manager.PendingWorldBytes==0,"descriptor release is idempotent and drains to zero");
                Reject(()=>reserve.Invoke(manager,new object[]{PNWorldWire.DescriptorBytes+1}),"per-peer descriptor cap rejects overflow before reservation");
                Check(manager.PendingWorldBytes==0,"failed reservation does not leak bytes");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
            return "PASS "+count+" world wire/interest/resource cases\n"+string.Join("\n",evidence);
        }
    }
}
#endif
