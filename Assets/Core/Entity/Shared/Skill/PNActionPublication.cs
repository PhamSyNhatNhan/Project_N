using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text;

// Authority-owned presentation shadow. No transport, execution or renderer ownership.
public enum PNActionPublicationStatus { Ok, UnsupportedCapability, ResourceExhausted, InvariantFault }
public sealed class PNActionPublicationBudget : IDisposable
{
    public readonly int Limit;
    public int Used { get; private set; }
    internal readonly HashSet<PNActionPublicationRecovery> Recoveries=new HashSet<PNActionPublicationRecovery>();
    internal readonly HashSet<PNActionPublication> Sources=new HashSet<PNActionPublication>();
    public PNActionPublicationBudget(int limit=32*1024*1024){if(limit<1)throw new ArgumentOutOfRangeException();Limit=limit;}
    internal bool Take(int bytes){if(bytes<0||bytes>Limit-Used)return false;Used+=bytes;return true;}
    internal void Return(int bytes){Used-=bytes;if(Used<0)throw new InvalidOperationException("Publication charge underflow");}
    public void Dispose(){foreach(var census in Recoveries.ToArray())census.Dispose();foreach(var source in Sources.ToArray())source.Dispose();}
}
public sealed class PNActionPublication : IDisposable
{
    public const double Retention = 3;
    public readonly Guid Source = Guid.NewGuid();
    public readonly int CurrentLimit, JournalLimit;
    public long Sequence { get; private set; }
    public long CaptureRevision { get; private set; }
    public long Resets { get; private set; }
    public long Corrections { get; private set; }
    public bool Complete { get; private set; } = true;
    public bool Disposed { get; private set; }
    public int CurrentBytes { get; private set; }
    public int JournalBytes { get; private set; }
    public int LeaseBytes { get; private set; }
    public int ActiveCount => active.Count;
    public long FirstAvailable => journal.Count == 0 ? (Sequence==long.MaxValue?long.MaxValue:Sequence + 1) : journal.Peek().N;
    public int SubscriptionCount=>subscriptions.Count;
    public PNActionPublicationStatus LastCaptureStatus {get;private set;}
    readonly PNActionPublicationBudget budget;
    public PNActionPublicationStatus Status { get; private set; }
    sealed class State
    {
        public PNActionRunKey Key; public string Entry,Data,Action; public double X,Z,Start,T,P,R,AT,AP,Scale;
        public long Rev=1,Hg=1,Need; public int Charge; public string KeyJson,ContextJson;
        public JArray K => new JArray(Key.Cast,Key.Ordinal);
        public JArray D => MakeContext(Entry,Data,Action,X,Z,Start,Scale);
    }
    static JArray MakeContext(string entry,string data,string action,double x,double z,double start,double scale)
    {
        var row = new JArray(entry,data,action,1,x,z,start);
        if (scale != 1) row.Add(scale);
        return row;
    }
    // Encoded strings are immutable and shared by subscription readers.
    public sealed class Required
    {
        public long N { get; internal set; }
        public double Stamp { get; internal set; }
        public string Json { get; internal set; }
        public int Bytes { get; internal set; }
        internal int Kind,Reason,EndKind;internal PNActionRunKey Key;internal long Revision,HistoryGeneration;
        internal string Entry,Data,Action;internal double X,Z,Start,Time,Progress,Rate,Scale;
        // Frozen scalar view for transport encoders. JSON/ToArray remain compatible.
        public int RecordKind=>Kind;public PNActionRunKey RunKey=>Key;
        public long RunRevision=>Revision;public long RunHistoryGeneration=>HistoryGeneration;
        public string EntryId=>Entry;public string DataId=>Data;public string ActionId=>Action;
        public double DirectionX=>X;public double DirectionZ=>Z;public double StartStamp=>Start;
        public double PresentationScale=>Scale;
        public double RecordTime=>Time;public double RecordedProgress=>Progress;public double RecordedRate=>Rate;
        public int ResetReason=>Reason;public int TerminalKind=>EndKind;
        // Mutable copy of frozen primitives. Callers cannot mutate journal state.
        public JArray ToArray()
        {
            var key=new JArray(Key.Cast,Key.Ordinal);
            if(Kind==0)return new JArray(0,N,key,MakeContext(Entry,Data,Action,X,Z,Start,Scale));
            if(Kind==4)return new JArray(4,N,key,Revision,EndKind,Time,Progress,HistoryGeneration);
            var anchor=new JArray(Time,Progress,Rate);
            return Kind==2?new JArray(2,N,key,HistoryGeneration,Revision,anchor):new JArray(3,N,key,HistoryGeneration,Revision,Reason,anchor);
        }
    }
    readonly Dictionary<PNActionRunKey,State> active=new Dictionary<PNActionRunKey,State>();
    readonly Queue<Required> journal=new Queue<Required>();
    readonly HashSet<Snapshot> leases=new HashSet<Snapshot>();
    readonly HashSet<Subscription> subscriptions=new HashSet<Subscription>();
    void ClearJournal(){while(journal.Count>0){var record=journal.Dequeue();budget.Return(record.Bytes+64);}JournalBytes=0;}
    PNActionPublicationStatus Fail(PNActionPublicationStatus status) { Complete=false;Status=status;return status; }
    static string N(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
    static int Bytes(string s)=>System.Text.Encoding.UTF8.GetByteCount(s);
    static string Encode(JToken value)=>value.ToString(Formatting.None);
    bool Live => !Disposed && Complete;
    void Emit(State s,double time,int tag,string suffix,double at=0,double progress=0,double rate=0,int end=0)
    {
        if(Sequence==long.MaxValue) {Fail(PNActionPublicationStatus.InvariantFault);return;}
        long n=++Sequence;
        string json="["+tag+","+n+","+s.KeyJson+","+suffix+"]";int charge=Bytes(json);
        if(charge>3840){Fail(PNActionPublicationStatus.ResourceExhausted);return;}
        if(charge>JournalLimit-JournalBytes){ClearJournal();Resets++;}
        s.Need=n;
        if(charge>JournalLimit)return; // complete current state survives a lost journal prefix
        if(!budget.Take(charge+64)){ClearJournal();Resets++;if(!budget.Take(charge+64))return;}
        journal.Enqueue(new Required{N=n,Stamp=time,Json=json,Bytes=charge,Kind=tag,Key=s.Key,Revision=tag==4?checked(s.Rev+1):s.Rev,HistoryGeneration=s.Hg,Entry=s.Entry,Data=s.Data,Action=s.Action,X=s.X,Z=s.Z,Start=s.Start,Time=at,Progress=progress,Rate=rate,Reason=1,EndKind=end,Scale=s.Scale});JournalBytes+=charge;
    }
    public PNActionPublication(int currentBytes=1048576,int journalBytes=4194304,PNActionPublicationBudget sessionBudget=null)
    {
        if(currentBytes<1||journalBytes<1)throw new ArgumentOutOfRangeException();CurrentLimit=currentBytes;JournalLimit=journalBytes;budget=sessionBudget??new PNActionPublicationBudget();
        if(budget.Sources.Count>=4096)Fail(PNActionPublicationStatus.ResourceExhausted);else budget.Sources.Add(this);
    }
    public PNActionPublicationStatus Begin(PNActionRunKey key,string entry,string data,string action,double x,double z,double time)
        => Begin(key,entry,data,action,x,z,time,1);

    public PNActionPublicationStatus Begin(PNActionRunKey key,string entry,string data,string action,double x,double z,double time,double scale)
    {
        if(!Live)return Status==PNActionPublicationStatus.Ok?PNActionPublicationStatus.InvariantFault:Status;
        PNTimeMath.Nonnegative(time);PNActionRunWire.Id(entry);PNActionRunWire.Id(data);PNActionRunWire.Id(action);
        if(double.IsNaN(x)||double.IsNaN(z)||Math.Abs(x)>1||Math.Abs(z)>1||x*x+z*z<=1e-8)throw new ArgumentException("Direction");
        if(active.ContainsKey(key))return Fail(PNActionPublicationStatus.InvariantFault);
        PNTimeMath.Nonnegative(scale);
        var s=new State{Key=key,Entry=entry,Data=data,Action=action,X=x,Z=z,Start=time,T=time,AT=time,Scale=scale};
        s.KeyJson=Encode(s.K);s.ContextJson=Encode(s.D);s.Charge=Bytes(s.KeyJson)+Bytes(s.ContextJson)+256; // includes a fixed terminal/control reserve, not lifetime history
        if(s.Charge>CurrentLimit-CurrentBytes||!budget.Take(s.Charge))return Fail(PNActionPublicationStatus.ResourceExhausted);
        active.Add(key,s);CurrentBytes+=s.Charge;Emit(s,time,0,s.ContextJson);if(Live)foreach(var peer in subscriptions)peer.Changed(key,time);return Status;
    }
    // Recovery imports complete authority state, not invented historical events.
    // This private candidate has no readers until the service swaps generations.
    internal PNActionPublicationStatus RestoreCurrent(PNActionRunKey key,string entry,string data,string action,double x,double z,double start,double stamp,double progress,double rate,double cut,double scale=1)
    {
        PNTimeMath.Nonnegative(progress);PNTimeMath.Nonnegative(rate);PNTimeMath.Nonnegative(stamp);PNTimeMath.Nonnegative(cut);
        if(start>stamp||stamp>cut)return Fail(PNActionPublicationStatus.InvariantFault);
        var status=Begin(key,entry,data,action,x,z,start,scale);
        if(status!=PNActionPublicationStatus.Ok)return status;
        ClearJournal(); // imported S is not replayable history; baseline owns the cut
        Sequence=0;
        var row=active[key];row.T=cut;row.P=progress;row.R=stamp<cut?0:rate;
        row.AT=cut;row.AP=progress;row.Need=0;
        return Status;
    }
    void Sample(State s,double time,double progress,double rate,double published)
    {
        bool correction=time==s.AT&&progress!=s.AP;
        bool changed=correction || rate!=s.R || Math.Abs(s.AP+(time-s.AT)*s.R-progress)>1e-7;
        if(changed)
        {
            s.Rev=checked(s.Rev+1);
            if(correction) {s.Hg=checked(s.Hg+1);Corrections++;Emit(s,published,3,s.Hg+","+s.Rev+",1,["+N(time)+","+N(progress)+","+N(rate)+"]",time,progress,rate);}
            else Emit(s,published,2,s.Hg+","+s.Rev+",["+N(time)+","+N(progress)+","+N(rate)+"]",time,progress,rate);
            s.AT=time;s.AP=progress;
        }
        s.T=time;s.P=progress;s.R=rate;s.Rev=checked(s.Rev+1);if(Live)foreach(var peer in subscriptions)peer.Changed(s.Key,published);
    }
    public PNActionPublicationStatus Advance(PNActionRunKey key,double begin,double end,double step,double rate)
    {
        if(!Live)return Status;
        PNTimeMath.Nonnegative(begin);PNTimeMath.Nonnegative(end);PNTimeMath.Nonnegative(step);PNTimeMath.Nonnegative(rate);
        if(begin>end)throw new ArgumentException("Interval");
        if(!active.TryGetValue(key,out var s))return Fail(PNActionPublicationStatus.InvariantFault);
        if(double.IsInfinity(s.P+step))return Fail(PNActionPublicationStatus.InvariantFault);
        end=Math.Max(end,s.T);begin=Math.Max(begin,s.T);double p=s.P;
        if(begin>s.T)Sample(s,s.T,p,0,end);
        Sample(s,begin,p,rate,end);Sample(s,end,p+step,rate,end);return Status;
    }
    public PNActionPublicationStatus End(PNActionRunKey key,double time,PNActionRunEnd kind)
    {
        if(!Live)return Status;PNTimeMath.Nonnegative(time);
        if(!active.TryGetValue(key,out var s))return PNActionPublicationStatus.Ok; // duplicate end is idempotent
        if(kind==PNActionRunEnd.Active||time<s.T)return Fail(PNActionPublicationStatus.InvariantFault);
        if(time>s.T){Sample(s,s.T,s.P,0,time);Sample(s,time,s.P,0,time);}
        // Transfer active reservation before retaining the real E. The fixed reserve
        // covers maximum terminal scalar widths plus journal bookkeeping.
        active.Remove(key);foreach(var peer in subscriptions)peer.Retire(key,time);CurrentBytes-=s.Charge;budget.Return(s.Charge);
        Emit(s,time,4,checked(s.Rev+1)+","+(int)kind+","+N(time)+","+N(s.P)+","+s.Hg,time,s.P,0,(int)kind);return Status;
    }
    public void Seal(double time)
    {
        if(!Live)return;PNTimeMath.Nonnegative(time);
        foreach(var s in active.Values)if(time>s.T){Sample(s,s.T,s.P,0,time);Sample(s,time,s.P,0,time);}
        while(journal.Count>0&&journal.Peek().Stamp<time-Retention){var record=journal.Dequeue();JournalBytes-=record.Bytes;budget.Return(record.Bytes+64);}
        foreach(var lease in leases.ToArray())if(time>lease.Deadline)lease.Dispose();
    }
    public long PublicationCut(double stamp){Seal(stamp);return CaptureRevision=checked(CaptureRevision+1);}
    public double Progress(PNActionRunKey key)=>active[key].P;
    public sealed class Snapshot : IDisposable
    {
        PNActionPublication owner;
        public string Json {get;private set;} public int Bytes {get;private set;}
        public double Deadline {get;private set;}
        internal Snapshot(PNActionPublication owner,string json,double time){this.owner=owner;Json=json;Bytes=PNActionPublication.Bytes(json);Deadline=time+3;}
        public void Dispose(){if(owner==null)return;owner.LeaseBytes-=Bytes;owner.budget.Return(Bytes+64);owner.leases.Remove(this);owner=null;Json=null;Bytes=0;}
    }
    public Snapshot Capture(JArray scope,double time)
    {
        LastCaptureStatus=PNActionPublicationStatus.Ok;
        if(!Live){LastCaptureStatus=Status==PNActionPublicationStatus.Ok?PNActionPublicationStatus.InvariantFault:Status;return null;}PNTimeMath.Nonnegative(time);
        if(scope==null||scope.Count!=6)throw new ArgumentException("Scope");
        if(active.Values.Any(s=>s.T>time))throw new ArgumentException("Capture before source");
        Seal(time);CaptureRevision=checked(CaptureRevision+1);
        var actualScope=(JArray)scope.DeepClone();actualScope[5]=Source.ToString("N");
        var text=new StringBuilder();text.Append("[2,").Append(Encode(actualScope)).Append(',').Append(CaptureRevision).Append(',').Append(N(time)).Append(',').Append(Sequence).Append(",[");
        bool first=true;foreach(var state in active.Values.OrderBy(v=>v.Key.Cast).ThenBy(v=>v.Key.Ordinal))
        {if(!first)text.Append(',');first=false;text.Append('[').Append(state.KeyJson).Append(',').Append(state.ContextJson).Append(',').Append(state.Rev).Append(',').Append(state.Hg).Append(',').Append(N(state.P)).Append(',').Append(N(state.R)).Append(']');}
        string json=text.Append("]]").ToString();
        if(leases.Count>=1024||Bytes(json)>1048576-LeaseBytes||!budget.Take(Bytes(json)+64)){LastCaptureStatus=PNActionPublicationStatus.ResourceExhausted;return null;}
        var lease=new Snapshot(this,json,time);leases.Add(lease);LeaseBytes+=lease.Bytes;return lease;
    }
    public sealed class Subscription : IDisposable
    {
        PNActionPublication owner; public long Submitted {get;private set;}
        public long Generation {get;private set;}=1; public bool NeedsBaseline {get;private set;}
        long preparedRequired;
        readonly HashSet<PNActionRunKey> tracked=new HashSet<PNActionRunKey>();
        public PNActionPublicationStatus Failure {get;private set;}
        public double MaxPendingAge {get;private set;}
        void ReleaseTracked(){if(owner!=null)owner.budget.Return(tracked.Count*256);tracked.Clear();preparedLatest.Clear();revisions.Clear();pending.Clear();}
        bool Track(PNActionRunKey key){if(tracked.Contains(key))return true;if(!owner.budget.Take(256)){Failure=PNActionPublicationStatus.ResourceExhausted;NeedsBaseline=true;return false;}tracked.Add(key);return true;}
        readonly Dictionary<PNActionRunKey,long> preparedLatest=new Dictionary<PNActionRunKey,long>();
        readonly Dictionary<PNActionRunKey,long> revisions=new Dictionary<PNActionRunKey,long>();
        readonly Dictionary<PNActionRunKey,double> pending=new Dictionary<PNActionRunKey,double>();
        public Subscription(PNActionPublication source,long cut=0)
        {if(source==null||!source.Live||cut<0||cut>source.Sequence)throw new ArgumentException("Subscription source/cut");if(source.subscriptions.Count>=32)throw new InvalidOperationException("Publication subscription safety limit");owner=source;Submitted=cut;source.subscriptions.Add(this);}
        internal void Changed(PNActionRunKey key,double stamp){if(Track(key)&&!pending.ContainsKey(key))pending[key]=stamp;}
        internal void Retire(PNActionRunKey key,double stamp)
        {if(pending.TryGetValue(key,out double born))MaxPendingAge=Math.Max(MaxPendingAge,stamp-born);pending.Remove(key);preparedLatest.Remove(key);revisions.Remove(key);if(tracked.Remove(key))owner.budget.Return(256);}
        public bool Disposed=>owner==null;
        public void Reset(long cut){if(owner==null||cut<0||cut>owner.Sequence)throw new ArgumentException("Reset cut");Submitted=cut;Generation=checked(Generation+1);ReleaseTracked();Failure=PNActionPublicationStatus.Ok;NeedsBaseline=false;preparedRequired=cut;}
        public IReadOnlyList<Required> ReadRequired(int byteBudget,int recordBudget)
        {
            if(owner==null||!owner.Live)return Array.Empty<Required>();
            if(Submitted<owner.FirstAvailable-1){NeedsBaseline=true;return Array.Empty<Required>();}
            var result=new List<Required>();
            foreach(var record in owner.journal)
            {if(record.N<=Submitted)continue;if(result.Count>=recordBudget||record.Bytes>byteBudget)break;result.Add(record);byteBudget-=record.Bytes;preparedRequired=record.N;}
            return result;
        }
        public void CommitRequiredSubmitted(long sequence,long generation)
        {if(owner==null||generation!=Generation||sequence<Submitted||sequence>preparedRequired)throw new InvalidOperationException("Unprepared required submission");Submitted=sequence;}
        public sealed class LatestSample
        {
            public PNActionRunKey Key{get;internal set;}public long Revision{get;internal set;}
            public double DirtySince {get;internal set;}
            public double Stamp{get;internal set;}public double Progress{get;internal set;}public double Rate{get;internal set;}
            public long HistoryGeneration{get;internal set;}public long NeedSequence{get;internal set;}
            internal string EncodedKey;string json;
            // A sample owns frozen scalar values. JSON remains the compatible
            // lazy view; typed transports need neither stringify nor parse it.
            public string Json=>json??(json="[1,"+EncodedKey+","+Revision+","+N(Stamp)+","+N(Progress)+","+N(Rate)+","+HistoryGeneration+","+NeedSequence+"]");
        }
        // Immutable payloads remain dirty until accepted by a bounded sender queue.
        public IReadOnlyList<LatestSample> ReadLatest(double now,int byteBudget)
        {
            var rows=new List<LatestSample>();if(owner==null||!owner.Live||NeedsBaseline)return rows;
            foreach(var s in owner.active.Values)
            {
                if(revisions.TryGetValue(s.Key,out long rev)&&rev==s.Rev)continue;
                if(!Track(s.Key))break;
                if(!pending.ContainsKey(s.Key))pending[s.Key]=s.T;
                MaxPendingAge=Math.Max(MaxPendingAge,now-pending[s.Key]);
                if(s.Need>Submitted)continue;
                var sample=new LatestSample{Key=s.Key,Revision=s.Rev,DirtySince=pending[s.Key],EncodedKey=s.KeyJson,Stamp=s.T,Progress=s.P,Rate=s.R,HistoryGeneration=s.Hg,NeedSequence=s.Need};
                if(byteBudget!=int.MaxValue){int bytes=PNActionPublication.Bytes(sample.Json);if(bytes>byteBudget)continue;byteBudget-=bytes;}
                rows.Add(sample);preparedLatest[s.Key]=s.Rev;
            }
            foreach(var key in revisions.Keys.Where(k=>!owner.active.ContainsKey(k)).ToArray())revisions.Remove(key);
            foreach(var key in preparedLatest.Keys.Where(k=>!owner.active.ContainsKey(k)).ToArray())preparedLatest.Remove(key);
            foreach(var key in pending.Keys.Where(k=>!owner.active.ContainsKey(k)).ToArray())pending.Remove(key);
            foreach(var key in tracked.Where(k=>!owner.active.ContainsKey(k)).ToArray()){tracked.Remove(key);owner.budget.Return(256);}
            return rows;
        }
        public void CommitLatestSubmitted(LatestSample sample,long generation)
        {
            if(owner==null||generation!=Generation||sample==null)throw new InvalidOperationException("Stale state submission");
            if(!owner.active.ContainsKey(sample.Key))return;
            if(!preparedLatest.TryGetValue(sample.Key,out long rev)||sample.Revision>rev)throw new InvalidOperationException("Unprepared state submission");
            if(!revisions.TryGetValue(sample.Key,out long prior)||sample.Revision>prior)revisions[sample.Key]=sample.Revision;
            // A newer prepared sample retains its original pending age.
            if(sample.Revision==rev&&owner.active[sample.Key].Rev==sample.Revision){preparedLatest.Remove(sample.Key);pending.Remove(sample.Key);}
        }
        public double OldestPending(double now)=>pending.Count==0?0:now-pending.Values.Min();
        public double OldestRequiredPending(double now)
        {
            if(owner==null||!owner.Live||NeedsBaseline||Submitted>=owner.Sequence||Submitted<owner.FirstAvailable-1)
                return 0; // ReadRequired handles a missing prefix through NeedsBaseline.
            foreach(var record in owner.journal)
                if(record.N>Submitted)
                    return now-record.Stamp;
            return 0;
        }
        public void Dispose(){ReleaseTracked();if(owner!=null)owner.subscriptions.Remove(this);owner=null;}
    }
    public void Dispose(){if(Disposed)return;foreach(var peer in subscriptions.ToArray())peer.Dispose();foreach(var lease in leases.ToArray())lease.Dispose();budget.Return(CurrentBytes);active.Clear();ClearJournal();budget.Sources.Remove(this);CurrentBytes=0;Complete=false;Disposed=true;Status=PNActionPublicationStatus.InvariantFault;}
}
