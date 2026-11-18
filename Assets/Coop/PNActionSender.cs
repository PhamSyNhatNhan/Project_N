using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace ProjectN.Coop
{
    // Bounded local handoff, not a second reliable retransmission layer.
    public sealed class PNActionSender : IDisposable
    {
        static readonly Unity.Profiling.ProfilerMarker CaptureMarker=new Unity.Profiling.ProfilerMarker("PN.Action.SenderCapture");
        sealed class Item {public PNActionPacket Packet;public double Born;public int Charge;public bool Latest;}
        readonly Queue<Item> queue=new Queue<Item>();
        readonly PNActionReceiveBudget budget;
        readonly Func<int,bool> additionalAdmission;
        readonly PNActionPublication source;
        readonly PNActionPublication.Subscription reader;
        readonly int limit;
        double latestAt=-1,heartbeatAt=-1;
        const int LatestSlots = 32;
        bool disposed;
        public PNActionStream Stream {get;}
        public long Request {get;}
        public PNActionBaselineReceipt Receipt {get;private set;}
        public double Deadline {get;}
        public bool NeedsBaseline {get;private set;}
        public int Bytes {get;private set;}
        public int Count=>queue.Count;
        public long SubmittedSequence=>reader.Submitted;
        public long SentBytes {get;private set;}
        public double MaxPendingAge {get;private set;}
        public PNActionSender(PNActionPublication source,PNActionStream stream,long request,double now,double deadline,PNActionReceiveBudget budget,Func<int,bool> admission=null,int limit=4*1024*1024)
        {
            this.source=source??throw new ArgumentNullException(nameof(source));this.budget=budget??throw new ArgumentNullException(nameof(budget));additionalAdmission=admission;this.limit=limit;Stream=stream;Request=request;Deadline=deadline;
            if(!source.Complete||source.Disposed||source.Source!=stream.Source)throw new InvalidOperationException("Action source unavailable");
            reader=new PNActionPublication.Subscription(source);
            try
            {
                using(var lease=source.Capture(new JArray(stream.Session.ToString("N"),stream.Scope.World.ToString("N"),checked((long)stream.Scope.Incarnation),stream.Entity,stream.Generation,stream.Source.ToString("N")),now))
                {
                    if(lease==null)throw new InvalidOperationException("Action snapshot unavailable");var body=JArray.Parse(lease.Json);
                    var packets=PNActionWire.Pack(stream,request,(long)body[2],now,(long)body[4],body,true);
                    if(!Enqueue(packets,now,false))throw new InvalidOperationException("Action baseline queue exhausted");
                    reader.Reset((long)body[4]);Receipt=new PNActionBaselineReceipt(packets[0],deadline);
                }
            }
            catch{Dispose();throw;}
        }
        bool Enqueue(PNActionPacket[] packets,double now,bool latest,bool completeThroughCapture=false)
        {
            int charge=packets.Sum(p=>p.Body.Length+192);
            if(charge>limit-Bytes||additionalAdmission!=null&&!additionalAdmission(charge))return false;
            try{budget.Take(charge);}catch(InvalidOperationException){return false;}
            foreach(var packet in packets)
            {
                var p=packet;
                p.CompleteThroughCapture=completeThroughCapture;
                queue.Enqueue(new Item{Packet=p,Born=now,Charge=p.Body.Length+192,Latest=latest});
            }
            Bytes+=charge;return true;
        }
        // Required is captured every service pass. Latest is coalesced at source
        // until accepted into this bounded queue; no cross-frame batch fill wait.
        public void Capture(double now,double cadence=.05)
        {using(CaptureMarker.Auto()){
            if(disposed||NeedsBaseline)return;
            if(!source.Complete||source.Disposed){NeedsBaseline=true;return;}
            long q=source.PublicationCut(now);
            var required=reader.ReadRequired(3000,256);if(reader.NeedsBaseline){NeedsBaseline=true;return;}
            int requiredCount=0,encoded=5;long last=reader.Submitted;
            foreach(var row in required){int size=PNActionWire.RequiredSize(row);if(size>PNActionWire.ChunkBytes-encoded)break;requiredCount++;encoded+=size;last=row.N;}
            if(requiredCount>0)
            {
                if(!Enqueue(PNActionWire.PackRequired(Stream,Request,q,now,last,required,requiredCount),now,false,last==source.Sequence)){NeedsBaseline=true;return;}
                reader.CommitRequiredSubmitted(last,reader.Generation);
            }
            if(now-latestAt<cadence)return;latestAt=now;
            var chosen=new List<PNActionPublication.Subscription.LatestSample>(reader.ReadLatest(now,int.MaxValue));
            chosen.Sort(CompareLatest);
            int slots=Math.Min(LatestSlots,(PNActionWire.ChunkBytes-5)/PNActionWire.LatestRowBytes);
            if(chosen.Count>slots)chosen.RemoveRange(slots,chosen.Count-slots);
            bool complete=reader.Submitted==source.Sequence;
            if(chosen.Count==0&&now-heartbeatAt>=.25){if(!Enqueue(PNActionWire.Pack(Stream,Request,q,now,reader.Submitted,new JArray(),false),now,false,complete)){NeedsBaseline=true;return;}heartbeatAt=now;}
            if(chosen.Count>0)
            {
                heartbeatAt=now;
                if(!Enqueue(PNActionWire.PackLatest(Stream,Request,q,now,reader.Submitted,chosen),now,true,complete)){NeedsBaseline=true;return;}
                foreach(var sample in chosen)reader.CommitLatestSubmitted(sample,reader.Generation);
            }
            MaxPendingAge=Math.Max(MaxPendingAge,reader.MaxPendingAge);
        }}
        static int CompareLatest(PNActionPublication.Subscription.LatestSample left,PNActionPublication.Subscription.LatestSample right)
        {
            int ageOrder=left.DirtySince.CompareTo(right.DirtySince);
            if(ageOrder!=0)return ageOrder;
            int castOrder=left.Key.Cast.CompareTo(right.Key.Cast);
            return castOrder!=0?castOrder:left.Key.Ordinal.CompareTo(right.Key.Ordinal);
        }
        internal double OldestQueued(double now)=>queue.Count==0?0:now-queue.Peek().Born;
        internal double OldestDirty(double now)=>reader.OldestPending(now);
        public double OldestRequired(double now)=>reader.OldestRequiredPending(now);
        public double OldestPending(double now)=>Math.Max(OldestRequired(now),Math.Max(OldestQueued(now),OldestDirty(now)));
        public bool PendingExpired(double now,double limit)=>Receipt.Applied&&OldestPending(now)>limit;
        public bool Confirm(PNActionPacket ack,double now)=>Receipt!=null&&Receipt.Confirm(ack,now);
        public bool DrainOne(double now,Func<int,bool> takeBudget,Action<PNActionPacket> send)
        {
            if(disposed||queue.Count==0)return false;var item=queue.Peek();if(!takeBudget(item.Charge))return false;
            send(item.Packet);queue.Dequeue();Bytes-=item.Charge;budget.Return(item.Charge);SentBytes+=item.Charge;MaxPendingAge=Math.Max(MaxPendingAge,now-item.Born);return true;
        }
        public void Dispose(){if(disposed)return;disposed=true;reader?.Dispose();budget.Return(Bytes);Bytes=0;queue.Clear();}
    }
}
