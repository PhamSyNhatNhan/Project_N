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
            // the features of the world as the host fixed them (Docs/EntityModulePlan.md E4)
            Check(copy.Features!=null&&copy.Features.Length==0,"an offer without features reads back with an empty list");
            var featured=offer;featured.Features=new[]{"base:alpha","project_n:rogue_buff","zeta:last"};writer.Reset();writer.Write(featured);var featuredCopy=new NetworkReader(writer.ToArray()).Read<PNWorldOffer>();
            Check(featuredCopy.Features.Length==3&&featuredCopy.Features[0]=="base:alpha"&&featuredCopy.Features[1]=="project_n:rogue_buff"&&featuredCopy.Features[2]=="zeta:last"&&featuredCopy.Ticket.Equals(ticket)&&featuredCopy.Paused,"the feature list round-trips with the rest of the descriptor");
            var maxFeatures=offer;maxFeatures.Features=new string[32];for(int i=0;i<32;i++)maxFeatures.Features[i]="test:f"+i.ToString("00");writer.Reset();writer.Write(maxFeatures);
            Check(new NetworkReader(writer.ToArray()).Read<PNWorldOffer>().Features.Length==32&&writer.Position<PNWorldWire.ControlBytes,"the maximum number of features fits the control message bound");
            var tooMany=offer;tooMany.Features=new string[33];for(int i=0;i<33;i++)tooMany.Features[i]="test:f"+i.ToString("00");Reject(()=>{writer.Reset();writer.Write(tooMany);},"more than the maximum number of features is refused on write");
            var unsorted=offer;unsorted.Features=new[]{"test:b","test:a"};Reject(()=>{writer.Reset();writer.Write(unsorted);},"unsorted features are refused on write");
            var duplicated=offer;duplicated.Features=new[]{"test:a","test:a"};Reject(()=>{writer.Reset();writer.Write(duplicated);},"duplicated features are refused on write");
            var invalidFeature=offer;invalidFeature.Features=new[]{"not an id"};Reject(()=>{writer.Reset();writer.Write(invalidFeature);},"an invalid feature id is refused on write");
            var longFeature=offer;longFeature.Features=new[]{"test:"+new string('x',140)};Reject(()=>{writer.Reset();writer.Write(longFeature);},"an oversized feature id is refused on write");
            // the read side: the count byte follows the flags byte, so it is the last byte of the same offer written without features
            writer.Reset();writer.Write(offer);int countAt=writer.Position-1;writer.Reset();writer.Write(featured);var raised=writer.ToArray();raised[countAt]=33;
            Reject(()=>new NetworkReader(raised).ReadWorldOffer(),"a feature count above the bound is refused on read");
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
