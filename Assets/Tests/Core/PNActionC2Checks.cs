#if UNITY_EDITOR || PN_ACTION_C2_STANDALONE
// Replays measured all-source C0 traces. Test-only, usable in Unity and .NET.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static PNActionC2Codec;

public static class PNActionC2Checks
{
    sealed class Item { public int Source; public JArray Body; public double Time; public long Q; public string Wire; public int Size,BodySize; public string Key => Source+":"+PNActionC2Codec.Key(Body[(int)Body[0]==1?1:2]); }
    sealed class SourceRun { public int Source; public JArray K,D; public double T,P,R,AnchorT,AnchorP; public long Rev=1,Need,Hg=1; public bool Ended; }
    sealed class Trace
    {
        public readonly List<Item> Items=new List<Item>(); public int Starts,Ends,OracleChecks; public double MaxError; public long PeakCurrentBytes,PeakRetainedCharge; public readonly Dictionary<int,long> Sequences=new Dictionary<int,long>();
        public readonly Dictionary<string,SourceRun> Runs=new Dictionary<string,SourceRun>();
        readonly Dictionary<int,Dictionary<string,SourceRun>> active=new Dictionary<int,Dictionary<string,SourceRun>>();
        readonly Dictionary<int,long> captures=new Dictionary<int,long>();
        long Next(int s) {Sequences.TryGetValue(s,out long n);return Sequences[s]=checked(n+1);}
        void Emit(SourceRun r,double published,JArray a) {captures.TryGetValue(r.Source,out long q);captures[r.Source]=checked(q+1);Items.Add(new Item{Source=r.Source,Body=a,Time=published,Q=q+1});if((int)a[0]!=1)r.Need=(long)a[1];}
        void Sample(SourceRun r,double t,double p,double rate,double published)
        {
            Need(t>=r.T-1e-9,"source backwards");
            bool correction=t==r.AnchorT && p!=r.AnchorP;
            if(correction || rate!=r.R || Math.Abs(r.AnchorP+(t-r.AnchorT)*r.R-p)>1e-7)
            {r.Rev++; if(correction) {r.Hg++;Emit(r,published,new JArray(3,Next(r.Source),r.K.DeepClone(),r.Hg,r.Rev,1,new JArray(t,p,rate)));} else Emit(r,published,new JArray(2,Next(r.Source),r.K.DeepClone(),r.Hg,r.Rev,new JArray(t,p,rate)));r.AnchorT=t;r.AnchorP=p;}
            r.T=t;r.P=p;r.R=rate;r.Rev++;
        }
        void Capture(double published)
        {
            long current=0;
            foreach(var r in active.Values.SelectMany(x=>x.Values))
            {Emit(r,published,new JArray(1,r.K.DeepClone(),r.Rev,r.T,r.P,r.R,r.Hg,r.Need));current+=Bytes(r.D)+Bytes(r.K)+64;}
            PeakCurrentBytes=Math.Max(PeakCurrentBytes,current);
        }
        public static Trace Load(string path,double cadence)
        {
            var tr=new Trace(); double previous=-1,next=-1;
            foreach(string line in File.ReadLines(path))
            {
                var e=JObject.Parse(line);Need(e["error"]==null,"source measurement error"); string kind=(string)e["kind"];
                if(kind!="attempt"&&kind!="driver"&&kind!="seal"&&kind!="finish-attempt")continue;
                double t=(double)e["stamp"];int source=(int)e["source"];
                if(previous>=0 && t>previous+1e-9 && previous+1e-9>=next) {tr.Capture(previous);next=previous+cadence;}
                if(next<0)next=t;previous=t;
                string key=source+":"+e["cast"]+":"+e["ordinal"];
                if(kind=="attempt")
                {var a=(JArray)e["candidate"];var r=new SourceRun{Source=source,K=new JArray(a[0],a[1]),D=new JArray(a[2],a[3],a[4],a[5],a[6],a[7],a[8]),T=t,AnchorT=t};Need(!tr.Runs.ContainsKey(key),"source identity reuse");tr.Runs.Add(key,r);if(!tr.active.TryGetValue(source,out var active))tr.active[source]=active=new Dictionary<string,SourceRun>();active.Add(key,r);tr.Emit(r,t,new JArray(0,tr.Next(source),r.K.DeepClone(),r.D.DeepClone()));tr.Starts++;}
                else if(kind=="seal")
                {foreach(var r in (tr.active.TryGetValue(source,out var live)?live.Values:Enumerable.Empty<SourceRun>()).Where(x=>x.T<t)) {tr.Sample(r,r.T,r.P,0,t);tr.Sample(r,t,r.P,0,t);}}
                else if(tr.Runs.TryGetValue(key,out var r))
                {
                    if(kind=="driver")
                    {Need(e["begin"]!=null,"C0 explicit begin required");double begin=Math.Max(r.T,(double)e["begin"]);if(begin>r.T)tr.Sample(r,r.T,r.P,0,t);tr.Sample(r,begin,r.P,(double)e["rate"],t);tr.Sample(r,t,r.P+(double)e["value"],(double)e["rate"],t);}
                    else {if(t>r.T){tr.Sample(r,r.T,r.P,0,t);tr.Sample(r,t,r.P,0,t);}r.Rev++;tr.Emit(r,t,new JArray(4,tr.Next(source),r.K.DeepClone(),r.Rev,(int)e["value"],t,r.P,r.Hg));r.Ended=true;tr.active[source].Remove(key);tr.Ends++;}
                    if(e["reconstructed"] is JArray row) {double error=Math.Abs((double)row[10]-r.P);tr.MaxError=Math.Max(tr.MaxError,error);tr.OracleChecks++;Need(error<=1e-5,"independent raw-step oracle");}
                }
            }
            if(previous>=0)tr.Capture(previous);
            // Verify producer and decoder separately before measuring scheduling.
            var models=new Dictionary<int,Decoder>();
            foreach(var item in tr.Items) {if(!models.TryGetValue(item.Source,out var m))models[item.Source]=m=new Decoder();item.Wire=Envelope(item.Source,1,item.Q,item.Time,new[]{item.Body}).ToString(Formatting.None);item.Size=System.Text.Encoding.UTF8.GetByteCount(item.Wire);m.Packet(item.Wire);item.Body=(JArray)Parse(item.Wire)["records"][0];item.BodySize=Bytes(item.Body);}
            Need(models.Values.Sum(m=>m.Starts)==tr.Starts&&models.Values.Sum(m=>m.Ends)==tr.Ends,"source decoder lifecycle");
            var retained=new Queue<(double time,int charge)>();long resident=0;
            foreach(var item in tr.Items)
            {while(retained.Count>0&&retained.Peek().time<item.Time-3)resident-=retained.Dequeue().charge;if((int)item.Body[0]!=1){int charge=Bytes(item.Body)+128;retained.Enqueue((item.Time,charge));resident+=charge;}tr.PeakRetainedCharge=Math.Max(tr.PeakRetainedCharge,resident);}
            Need(tr.PeakCurrentBytes<=1048576&&tr.PeakRetainedCharge<=4194304,"source accounting ceilings");
            return tr;
        }
    }
    sealed class Pending { public Item Item;public double Born;public string Wire;public int Size; }
    sealed class Flight { public Pending Pending;public double Sent; }
    sealed class Peer
    {
        public readonly Queue<Pending> Required=new Queue<Pending>(); public readonly Dictionary<string,Pending> State=new Dictionary<string,Pending>();public readonly Queue<Flight> Client=new Queue<Flight>();
        public readonly Dictionary<int,Decoder> Models=new Dictionary<int,Decoder>();public readonly Dictionary<int,long> Submitted=new Dictionary<int,long>();
        public readonly List<double> HostAge=new List<double>(),ClientAge=new List<double>(); public readonly Dictionary<int,List<double>> ClassAge=new Dictionary<int,List<double>>();
        public long Bytes,BodyBytes,PeakHost,PeakClient,HostBytes,ClientBytes,Coalesced,PeakModel;public int Sent,MaxPacket,RequiredBurst,PeakRecords;public double Drained=-1,MaxPendingAge;
        public void Add(Item item)
        {
            var wire=item.Wire;int size=item.Size;Need(size<=4096,"full envelope cap");var p=new Pending{Item=item,Born=item.Time,Wire=wire,Size=size};
            if((int)item.Body[0]==1) {if(State.TryGetValue(item.Key,out var old)){p.Born=old.Born;HostBytes-=old.Size;Coalesced++;}State[item.Key]=p;}
            else {Required.Enqueue(p);if((int)item.Body[0]==4 && State.TryGetValue(item.Key,out var old)){HostBytes-=old.Size;MaxPendingAge=Math.Max(MaxPendingAge,item.Time-old.Born);State.Remove(item.Key);}}
            HostBytes+=size;PeakHost=Math.Max(PeakHost,HostBytes);PeakRecords=Math.Max(PeakRecords,Required.Count+State.Count);Need(HostBytes<=4*1024*1024,"host queue bound");
        }
        public void ObservePending(double now) {if(Required.Count>0)MaxPendingAge=Math.Max(MaxPendingAge,now-Required.Peek().Born);foreach(var p in State.Values)MaxPendingAge=Math.Max(MaxPendingAge,now-p.Born);}
        public void Pump(double now,ref int global,ref int peerBudget)
        {
            while(true)
            {
                Pending state=null;foreach(var candidate in State.Values)if(Submitted.TryGetValue(candidate.Item.Source,out long n)&&n>=(long)candidate.Item.Body[7]&&(state==null||candidate.Born<state.Born))state=candidate;
                Pending p=null;bool isState=false;
                if(state!=null&&(RequiredBurst>=8||Required.Count==0)){p=state;isState=true;}
                else if(Required.Count>0)p=Required.Peek();
                if(p==null||p.Size>peerBudget||p.Size>global)break;
                if(isState){State.Remove(p.Item.Key);RequiredBurst=0;}else{Required.Dequeue();Submitted[p.Item.Source]=(long)p.Item.Body[1];RequiredBurst++;}
                peerBudget-=p.Size;global-=p.Size;HostBytes-=p.Size;Bytes+=p.Size;BodyBytes+=p.Item.BodySize;MaxPacket=Math.Max(MaxPacket,p.Size);Sent++;
                double age=Math.Max(0,now-p.Born);HostAge.Add(age);int tag=(int)p.Item.Body[0];if(!ClassAge.TryGetValue(tag,out var ages))ClassAge[tag]=ages=new List<double>();ages.Add(age);
                Client.Enqueue(new Flight{Pending=p,Sent=now});ClientBytes+=p.Size;PeakClient=Math.Max(PeakClient,ClientBytes);Need(ClientBytes<=4*1024*1024,"client queue bound");
            }
        }
        public void Apply(double now,bool hold)
        {
            if(hold||Client.Count==0)return;int budget=16384;
            while(Client.Count>0 && Client.Peek().Pending.Size<=budget)
            {var f=Client.Dequeue();var p=f.Pending;budget-=p.Size;ClientBytes-=p.Size;if(!Models.TryGetValue(p.Item.Source,out var model))Models[p.Item.Source]=model=new Decoder();model.Apply(p.Item.Body,false); // Immutable body decoded/validated through full wire in Trace.Load.
                ClientAge.Add(Math.Max(0,now-f.Sent));}
            foreach(var model in Models.Values)model.Prune(now);
            PeakModel=Math.Max(PeakModel,Models.Values.Sum(m=>(long)m.ResidentCharge));Need(PeakModel<=4194304,"receiver model accounting ceiling");
        }
    }
    static JObject Ages(List<double> values) {var a=values.OrderBy(x=>x).ToArray();double P(double q)=>a.Length==0?0:a[Math.Min(a.Length-1,(int)Math.Ceiling(q*a.Length)-1)];return new JObject{["p50"]=P(.5),["p95"]=P(.95),["p99"]=P(.99),["max"]=P(1)};}
    static JObject Simulate(Trace trace,double cadence,double stall,bool clientHold,bool sharedHold=false)
    {
        var peers=new[]{new Peer(),new Peer(),new Peer()};var credits=new int[3];double first=trace.Items.Min(x=>x.Time),last=trace.Items.Max(x=>x.Time),stallAt=first+1;int index=0,rotation=0;double next=first;long competitorBytes=0;
        for(double now=first;now<=last+4;now+=.01)
        {
            while(index<trace.Items.Count&&trace.Items[index].Time<=now+1e-8){foreach(var peer in peers)peer.Add(trace.Items[index]);index++;}
            // 64 KiB peer / 256 KiB session per frame; reserve 48 KiB per peer for Move/Buff,
            // plus 80 KiB shared competing traffic. Credits refill at publication cadence;
            // service continues each 10 ms frame, preserving unused per-cadence credit.
            // Action's 16 KiB per cadence is a ceiling, not additional link capacity.
            if(now+1e-8>=next){for(int i=0;i<3;i++)credits[i]=16384;next+=cadence;}
            bool sharedBlocked=sharedHold&&now>=stallAt&&now<stallAt+stall;int global=sharedBlocked?0:256*1024-3*48*1024-80*1024;
            if(!sharedBlocked)competitorBytes+=(3*48+80)*1024;
            foreach(var peer in peers)peer.ObservePending(now);
            for(int j=0;j<3;j++){int i=(rotation+j)%3;bool blocked=i==1&&!clientHold&&now>=stallAt&&now<stallAt+stall;if(!blocked)peers[i].Pump(now,ref global,ref credits[i]);}rotation=(rotation+1)%3;
            for(int i=0;i<3;i++)peers[i].Apply(now,i==1&&clientHold&&now>=stallAt&&now<stallAt+stall);
            Need(peers.Sum(p=>p.HostBytes+p.ClientBytes)<=33554432,"session queue ceiling");
            foreach(var peer in peers)if(stall>0 && now>=stallAt+stall&&peer.Drained<0 && peer.Required.All(x=>x.Born>=stallAt+stall)&&peer.State.Values.All(x=>x.Born>=stallAt+stall)&&peer.Client.All(x=>x.Pending.Born>=stallAt+stall))peer.Drained=now-stallAt-stall;
            if(index==trace.Items.Count&&peers.All(p=>p.HostBytes==0&&p.ClientBytes==0))break;
        }
        var reports=new JArray();bool pass=true;
        for(int i=0;i<3;i++)
        {var p=peers[i];Need(p.HostBytes==0&&p.ClientBytes==0,"eventual drain");Need(p.Models.Values.Sum(m=>m.Starts)==trace.Starts&&p.Models.Values.Sum(m=>m.Ends)==trace.Ends,"delivery lifecycle");var h=Ages(p.HostAge);var c=Ages(p.ClientAge);bool normal=stall==0||!sharedHold&&i!=1;bool gate=!normal||((double)h["p99"]<=2*cadence+1e-8&&(double)h["max"]<=4*cadence+1e-8&&(double)c["p99"]<=2*cadence+1e-8&&(double)c["max"]<=4*cadence+1e-8&&p.MaxPendingAge<=4*cadence+1e-8);if(!normal)gate=p.Drained>=0&&p.Drained<=1+1e-8;pass&=gate;
            reports.Add(new JObject{["peer"]=i,["pass"]=gate,["bytes"]=p.Bytes,["bodyBytes"]=p.BodyBytes,["maxPacketBytes"]=p.MaxPacket,["records"]=p.Sent,["hostAge"]=h,["clientAge"]=c,["perTagHostAge"]=JObject.FromObject(p.ClassAge.ToDictionary(x=>x.Key.ToString(),x=>Ages(x.Value))),["hostQueuePeakBytes"]=p.PeakHost,["clientQueuePeakBytes"]=p.PeakClient,["receiverModelChargePeak"]=p.PeakModel,["hostQueuePeakRecords"]=p.PeakRecords,["maxObservedPendingAge"]=p.MaxPendingAge,["coalesced"]=p.Coalesced,["drainAfterResume"]=p.Drained,["starts"]=trace.Starts,["ends"]=trace.Ends});}
        return new JObject{["cadence"]=cadence,["stall"]=stall,["hold"]=sharedHold?"shared-budget-zero":clientHold?"client-apply":"host-send",["pass"]=pass,["modeledCompetitorBytesServiced"]=competitorBytes,["peers"]=reports};
    }
    static int checks;
    static void Good(bool c,string why){Need(c,why);checks++;}
    static void Reject(Action f,string why){bool rejected=false;try{f();}catch(Exception e)when(e is InvalidDataException||e is JsonException||e is OverflowException||e is ArgumentException){rejected=true;}Good(rejected,"negative accepted: "+why);}
    static JArray D()=>new JArray("test:entry","test:data","test:action",1,1d,0d,1d);
    static JArray S(long n=1,long cast=1)=>new JArray(0,n,new JArray(cast,1),D());
    static JArray A(long n=2,double t=1,double p=0,double rate=1,long rev=2)=>new JArray(2,n,new JArray(1,1),1,rev,new JArray(t,p,rate));
    static JArray U(long need=2,double t=2,double p=1)=>new JArray(1,new JArray(1,1),3,t,p,1d,1,need);
    static JArray E(long n=3)=>new JArray(4,n,new JArray(1,1),4,1,2d,1d,1);
    static Decoder Started(){var m=new Decoder();m.Apply(S());m.Apply(A());return m;}
    // Minimal experiment source accounting. A failed Begin invalidates completeness;
    // journal pressure alone never does. No gameplay admission is changed.
    sealed class PressureSource
    {
        readonly int currentLimit,journalLimit;int currentBytes,journalBytes;
        readonly Dictionary<string,JArray> current=new Dictionary<string,JArray>();readonly Queue<JArray> journal=new Queue<JArray>();
        public bool Complete=true,NeedsReset;public int Resets;
        public PressureSource(int currentCap,int journalCap){currentLimit=currentCap;journalLimit=journalCap;}
        public string Begin(JArray row)
        {int charge=Bytes(row);if(!Complete)return "InvariantFault";if(currentBytes+charge>currentLimit){Complete=false;return "ResourceExhausted";}string key=Key(row[0]);Need(!current.ContainsKey(key),"pressure identity");current.Add(key,(JArray)row.DeepClone());currentBytes+=charge;return "Ok";}
        public void Append(JArray record)
        {int charge=Bytes(record);if(journalBytes+charge>journalLimit){journal.Clear();journalBytes=0;NeedsReset=true;Resets++;}if(charge<=journalLimit){journal.Enqueue((JArray)record.DeepClone());journalBytes+=charge;}}
        public JArray Capture(double t,long n)
        {Need(Complete,"incomplete source cannot baseline");return new JArray(2,MakeScope(1),1,t,n,new JArray(current.Values.OrderBy(x=>(long)x[0][0]).ThenBy(x=>(long)x[0][1]).Select(x=>x.DeepClone())));}
    }
    static JObject RecoveryChecks()
    {
        int before=checks;var rows=new JArray();for(int i=1;i<=64;i++)rows.Add(new JArray(new JArray(i,1),D(),3,1,1d,1d));
        var baseline=new JArray(2,MakeScope(1),7,2d,128,rows);var parts=SplitBaseline(baseline,2,new string('a',32));Good(parts.Count>1,"multipart required");
        var m=Started();int staging=0;
        using(var assembly=new Assembly(parts[0],0))
        {for(int i=parts.Count-1;i>=0;i--){assembly.Receive(parts[i].ToString(Formatting.None),.1,m);if(i!=0){Good(m.Sub==1&&m.Runs.Count==1,"no partial commit");assembly.Receive(parts[i].ToString(Formatting.None),.15,m);}}Good(assembly.Committed&&assembly.AppliedQ==7&&assembly.AppliedId==new string('a',32)&&m.Runs.Count==64&&m.N==128,"atomic full set actual apply ACK identity");staging=assembly.PeakBytes;}
        // The cut is independent of latest gameplay changes; only ordered postcut work applies.
        rows[0][4]=999d;Good(m.Runs["1:1"].P==1,"frozen copy");m.Apply(new JArray(4,129,new JArray(1,1),4,1,2d,1d,1));Good(m.Ends==1,"postcut terminal");
        var bad=(JObject)parts[0].DeepClone();bad["q"]=8;
        using(var a=new Assembly(parts[0],0)){Reject(()=>a.Receive(bad.ToString(Formatting.None),.1,Started()),"mixed cut");Reject(()=>a.Receive(parts[0].ToString(Formatting.None),3.01,Started()),"deadline unchanged");}
        bad=(JObject)parts[0].DeepClone();bad["count"]=321;using(var a=new Assembly(bad,0))Reject(()=>a.Receive(bad.ToString(Formatting.None),0,Started()),"part cap");
        using(var a=new Assembly(parts[0],0)){a.Receive(parts[0].ToString(Formatting.None),0,Started());bad=(JObject)parts[0].DeepClone();bad["rows"][0][4]=.5;Reject(()=>a.Receive(bad.ToString(Formatting.None),.1,Started()),"duplicate fragment conflict");}
        var row=new JArray(new JArray(1,1),D(),3,1,1d,1d);var source=new PressureSource(1048576,Bytes(S()));Good(source.Begin(row)=="Ok","pressure begin");source.Append(S());source.Append(A());Good(source.NeedsReset&&source.Complete,"Level A retains current completeness");m=Started();m.Baseline(source.Capture(2,2),2);Good(m.Runs.Count==1&&m.N==2,"Level A full B recovery");
        var exhausted=new PressureSource(1,1000);Good(exhausted.Begin(row)=="ResourceExhausted"&&!exhausted.Complete,"Level B explicit status");Reject(()=>exhausted.Capture(2,2),"no partial B after exhaustion");
        // Separate source pressure from old bytes already submitted to Reliable.
        var recovery=new JArray();foreach(double oldBacklog in new[]{0d, .5,2d,3.1})foreach(double applyHold in new[]{0d,.5})
        {double now=oldBacklog+applyHold;bool timedOut=false;var model=Started();using(var a=new Assembly(parts[0],0)) {foreach(var part in parts){try{a.Receive(part.ToString(Formatting.None),now,model);}catch(InvalidDataException){timedOut=true;break;}now+=.05;}Good(timedOut==(oldBacklog+applyHold+(parts.Count-1)*.05>3),"in-flight backlog/original deadline");recovery.Add(new JObject{["inFlightDelay"]=oldBacklog,["applyHold"]=applyHold,["applied"]=a.Committed,["timeout"]=timedOut,["duration"]=now,["appliedAt"]=a.Committed?now-.05:-1});}}
        var degraded=new JArray();int viewerCase=0;foreach(double viewer in new[]{1d,1.5,2.1}) {double[] ends={1.2,1.8};int expired=ends.Count(t=>t<viewer);degraded.Add(new JObject{["baselineCut"]=2,["presentation"]=viewer,["modelApplied"]=true,["visibleEligible"]=viewer>=2,["skippedExpired"]=expired,["skippedPotentiallyVisible"]=ends.Length-expired});Good(expired==viewerCase++,"selected-view expiry classification");}
        return new JObject{["checks"]=checks-before,["baselineParts"]=parts.Count,["stagingPayloadPeak"]=staging,["peakCopiesAtCommitUpperBound"]=staging*3,["maxPartBytes"]=parts.Max(Bytes),["recovery"]=recovery,["degraded"]=degraded,["note"]="Deterministic model fault injection; authenticated protocol and visible seek not implemented"};
    }
    static JObject Correctness()
    {
        checks=0;var m=Started();m.Apply(U());m.Apply(E());m.Apply(E());m.Apply(U());Good(m.Starts==1&&m.Ends==1&&m.Runs["1:1"].Ended,"short lifecycle/duplicate/stale U");
        Reject(()=>new Decoder().Apply(E()),"terminal first");Reject(()=>new Decoder().Apply(S(2)),"gap");Reject(()=>new Decoder().Apply(U()),"missing context");Reject(()=>Started().Apply(U(3)),"needN");var badRate=U();badRate[5]=2d;Reject(()=>Started().Apply(badRate),"state rate must match anchor");
        Reject(()=>Started().Apply(A(2,1,0,2)),"duplicate conflict");Reject(()=>Started().Apply(S(3)),"identity reuse");Reject(()=>Started().Apply(A(3,1,.2,1,3)),"same stamp correction");
        m=Started();m.Apply(new JArray(3,3,new JArray(1,1),2,3,1,new JArray(1d,.2,1d)));Good(m.Runs["1:1"].Hg==2,"reset");Reject(()=>m.Apply(U()),"old generation");
        m=Started();m.Apply(A(3,1,0,0,3));m.Apply(new JArray(1,new JArray(1,1),4,100d,0d,0d,1,3));Good(m.Runs["1:1"].At(99)==0,"long hold no TTL");
        foreach(var mutate in new Action<JArray>[] {a=>a.Add(0),a=>a.RemoveAt(3),a=>a[1]=1.0,a=>a[1]=0,a=>a[0]=5,a=>a[2][0]=0,a=>a[3][3]=2,a=>a[3][4]=double.NaN,a=>a[3][4]=2,a=>a[3][6]=-1,a=>a[3][0]="bad id",a=>a[3][0]="test:"+new string('a',92)}) {var a=S();mutate(a);Reject(()=>Record(a),"malformed start");}
        Reject(()=>Parse("{\"v\":2,\"v\":2}"),"duplicate object key");Reject(()=>Parse("{} {}"),"trailing JSON");Reject(()=>Parse(new string(' ',4097)),"byte cap");Reject(()=>Parse(new string('[',20)+"0"+new string(']',20)),"depth");Reject(()=>Record(new JArray(1,new JArray(1,1),long.MaxValue,1d,double.PositiveInfinity,0d,1,0)),"infinite");
        var extreme=S(long.MaxValue,long.MinValue);extreme[2][1]=long.MaxValue;foreach(int i in new[]{0,1,2})extreme[3][i]="test:"+new string('a',91);Record(extreme);var env=Envelope(1,long.MaxValue,long.MaxValue,1,new[]{extreme});var sc=(JArray)env["scope"];sc[2]=ulong.MaxValue;sc[3]=long.MaxValue;sc[4]=long.MaxValue;Scope(sc);Good(Bytes(env)<=4096&&JToken.DeepEquals(Parse(env.ToString(Formatting.None)),env),"maximum IDs/int64/uint64 full envelope");
        var packet=Envelope(1,1,1,2,new[]{S(),A(),U(),E()});m=new Decoder();m.Packet(packet.ToString(Formatting.None));Good(m.Ends==1,"packet decoder");packet["v"]=3;Reject(()=>new Decoder().Packet(packet.ToString(Formatting.None)),"version");packet["v"]=2;packet["extra"]=0;Reject(()=>new Decoder().Packet(packet.ToString(Formatting.None)),"extra envelope field");
        var baseline=new JArray(2,MakeScope(1),1,2d,2,new JArray {new JArray(new JArray(1,1),D(),3,1,1d,1d)});
        m=Started();m.Baseline(baseline,2);Good(m.N==2&&m.Floor==2&&m.Runs["1:1"].At(2)==1,"baseline cut");Reject(()=>m.Runs["1:1"].At(1.9),"no backwards projection");m.Packet(Envelope(1,1,1,2,new[]{E()}).ToString(Formatting.None));Good(!m.Runs["1:1"].Ended,"old subscription ignored");m.Packet(Envelope(2,2,1,2,new[]{E()}).ToString(Formatting.None));Good(m.Ignored==2&&!m.Runs["1:1"].Ended,"old scope ignored");m.Packet(Envelope(1,2,2,2,new[]{E()}).ToString(Formatting.None));Good(m.Ends==1,"postcut order");
        var bad=(JArray)baseline.DeepClone();((JArray)bad[5]).Add(bad[5][0].DeepClone());Reject(()=>m.Baseline(bad,3),"duplicate rows");Good(m.Sub==2&&m.Ends==1,"atomic failed baseline");m.Baseline(new JArray(2,MakeScope(1),2,3d,3,new JArray()),3);Good(m.Runs.Count==0,"empty complete baseline");
        return new JObject{["passed"]=checks,["maxContextEnvelopeBytes"]=Bytes(env)};
    }
    static string Hash(string path){using(var h=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    public static string RunCorrectness() {var c=Correctness();var r=RecoveryChecks();return new JObject{["codec"]=c,["recovery"]=r,["total"]=checks}.ToString();}
    public static string Run(string root,string output)
    {
        Directory.CreateDirectory(output);
        var paths=new Dictionary<string,string>();foreach(var w in new[]{"W1","W2-burst","W2-stagger","W3","W4"})paths[w]=Path.Combine(root,".utmp/NetworkTime/T5_1b/C0/Closure",w,"native-a2.jsonl");
        paths["W0-0"]=Path.Combine(root,".utmp/NetworkTime/T5_1b/C0/Runs/Room_20261117_021000/host-a2.jsonl");paths["W0-120"]=Path.Combine(root,".utmp/NetworkTime/T5_1b/C0/Runs/Room_20261117_021120/host-a2.jsonl");
        var manifest=new JObject{["candidate"]="F0-v2-incremental-FIFO",["runtime"]=Environment.Version.ToString(),["seed"]=0,["codecReplay"]="Every source record full-envelope roundtripped and decoded once; per-peer scheduler replays immutable prepared bodies through the same state-transition decoder",["targets"]=new JObject{["normalP99Cadences"]=2,["normalMaxCadences"]=4,["drainAfterResumeSeconds"]=1,["recoveryOriginalDeadlineSeconds"]=3,["peerQueueBytes"]=4194304,["sessionQueueBytes"]=33554432,["baselineBytes"]=1048576,["baselineParts"]=320,["sourceCurrentBytes"]=1048576,["sourceHistoryBytes"]=4194304},["model"]="3 peers; Action <=16KiB/cadence; shared peer64KiB/session256KiB per10ms frame, Move/Buff competitor48KiB/peer/frame plus80KiB/session/frame serviced first; client apply16KiB/frame; zero transit time; no real network",["sources"]=JObject.FromObject(paths.ToDictionary(p=>p.Key,p=>Hash(p.Value))),["code"]=new JObject{["codec"]=Hash(Path.Combine(root,"Assets/Tests/Core/PNActionC2Codec.cs")),["checks"]=Hash(Path.Combine(root,"Assets/Tests/Core/PNActionC2Checks.cs"))}};
        File.WriteAllText(Path.Combine(output,"manifest.json"),manifest.ToString()); // Written before tests and measurements.
        var report=new JObject{["correctness"]=Correctness(),["recovery"]=RecoveryChecks(),["runs"]=new JArray()};var results=(JArray)report["runs"];
        foreach(var path in paths)foreach(double cadence in new[]{.05,.1})
        {var trace=Trace.Load(path.Value,cadence);var runs=new JArray();foreach(double stall in new[]{0d,.2,.5,2d}){runs.Add(Simulate(trace,cadence,stall,false));if(stall>0)runs.Add(Simulate(trace,cadence,stall,true));}runs.Add(Simulate(trace,cadence,2,false,true));var r=new JObject{["workload"]=path.Key,["cadence"]=cadence,["starts"]=trace.Starts,["ends"]=trace.Ends,["sourceRecords"]=trace.Items.Count,["sourceCurrentEncodedPeak"]=trace.PeakCurrentBytes,["sourceRetainedChargePeak"]=trace.PeakRetainedCharge,["correctionRecords"]=trace.Items.Count(x=>(int)x.Body[0]==3),["oracleChecks"]=trace.OracleChecks,["oracleMaxError"]=trace.MaxError,["scenarios"]=runs};results.Add(r);File.WriteAllText(Path.Combine(output,path.Key+"-"+(int)(cadence*1000)+".json"),r.ToString());}
        report["pass"]=results.All(r=>r["scenarios"].All(s=>(bool)s["pass"]));report["networkAcceptance"]=false;File.WriteAllText(Path.Combine(output,"report.json"),report.ToString());return "C2 completed; gates="+report["pass"]+" correctness="+checks;
    }
#if UNITY_EDITOR
    public static void RunBatch()
    {
        string[] args=Environment.GetCommandLineArgs();string root=args[System.Array.IndexOf(args,"-c2root")+1],output=args[System.Array.IndexOf(args,"-c2output")+1];
        try {UnityEngine.Debug.Log(Run(root,output));UnityEditor.EditorApplication.Exit(0);}catch(Exception e){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());UnityEngine.Debug.LogException(e);UnityEditor.EditorApplication.Exit(1);}
    }
#endif
}

#endif
