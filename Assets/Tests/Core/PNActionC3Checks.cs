#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
public static class PNActionC3Checks
{
    static PNActionRunKey K(int n)=>new PNActionRunKey(100,n);
    static void Check(bool c,string why){if(!c)throw new Exception("C3: "+why);}
    public static void AcceptRequired(PNActionPublication.Subscription peer,int bytes=16384,int count=1000)
    {var records=peer.ReadRequired(bytes,count);if(records.Count>0)peer.CommitRequiredSubmitted(records[records.Count-1].N,peer.Generation);}
    public static void AcceptLatest(PNActionPublication.Subscription peer,double time,int bytes=16384)
    {foreach(var sample in peer.ReadLatest(time,bytes))peer.CommitLatestSubmitted(sample,peer.Generation);}
    static void Begin(PNActionPublication s,int n,double t=1)=>Check(s.Begin(K(n),"test:entry","test:data","test:action",1,0,t)==PNActionPublicationStatus.Ok,"begin");
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static WeakReference[] DisposedOwners()
    {
        var budget=new PNActionPublicationBudget();var source=new PNActionPublication(sessionBudget:budget);Begin(source,1);
        var peer=new PNActionPublication.Subscription(source);AcceptRequired(peer);AcceptLatest(peer,1);var snapshot=source.Capture(PNActionC2Codec.MakeScope(1),1);
        var references=new[]{new WeakReference(source),new WeakReference(peer),new WeakReference(snapshot)};budget.Dispose();Check(budget.Used==0,"all session charges released");return references;
    }
    public static string Run(string folder)
    {
        Directory.CreateDirectory(folder);int checks=0;
        void C(bool b,string why){Check(b,why);checks++;}
        var scope=PNActionC2Codec.MakeScope(1);
        using(var s=new PNActionPublication())
        {
            Begin(s,1);s.Advance(K(1),.98,1,.02,1);C(s.Corrections==1&&s.Progress(K(1))==.02,"activation stamp with retrospective first interval");
            using(var lease=s.Capture(scope,1)) {var b=JArray.Parse(lease.Json);C((double)b[5][0][1][6]==1&& (double)b[5][0][4]==.02,"start not backdated and actual step preserved");}
            using(var peer=new PNActionPublication.Subscription(s))
            {C(peer.ReadLatest(1,4096).Count==0,"state barrier");var records=peer.ReadRequired(4096,99);C(peer.Submitted==0,"read is not submission");AcceptRequired(peer,4096,99);var decoder=new PNActionC2Codec.Decoder();foreach(var r in records)decoder.Apply(JArray.Parse(r.Json));foreach(var u in peer.ReadLatest(1,4096)){decoder.Apply(JArray.Parse(u.Json));peer.CommitLatestSubmitted(u,peer.Generation);}C(decoder.Runs["100:1"].P==.02,"reference reconstruction");}
            using(var lease=s.Capture(scope,1)){string frozen=lease.Json;s.Advance(K(1),1,2,1,1);C(lease.Json==frozen,"immutable freeze");}
            s.End(K(1),2,PNActionRunEnd.Completed);s.End(K(1),2,PNActionRunEnd.Completed);C(s.ActiveCount==0&&s.CurrentBytes==0,"terminal release exactly once");s.Seal(6);C(s.JournalBytes==0,"retirement");
            Begin(s,2,6);var stale=s.Capture(scope,6);s.Seal(10);C(stale.Json==null&&s.LeaseBytes==0,"expired lease released");
        }
        using(var s=new PNActionPublication(1048576,250))
        {Begin(s,1);using(var slow=new PNActionPublication.Subscription(s)){for(int i=0;i<30;i++)s.Advance(K(1),1+i*.01,1+(i+1)*.01,.01,i%2+1);slow.ReadRequired(4096,99);C(s.Complete&&s.Resets>0&&slow.NeedsBaseline,"journal pressure recovery");using(var lease=s.Capture(scope,1.3)){C(lease!=null&&JArray.Parse(lease.Json)[5].Count()==1,"complete baseline after pressure");slow.Reset(s.Sequence);C(!slow.NeedsBaseline,"reset fence");}}}
        using(var s=new PNActionPublication(1,4096)){C(s.Begin(K(1),"test:e","test:d","test:a",1,0,1)==PNActionPublicationStatus.ResourceExhausted,"current resource exhausted explicit");C(!s.Complete&&s.Capture(scope,1)==null,"no incomplete baseline");}
        using(var shared=new PNActionPublicationBudget(1024))
        {
            var first=new PNActionPublication(sessionBudget:shared);Begin(first,1);
            using(var snapshot=first.Capture(scope,1))C(snapshot!=null,"shared snapshot charge");
            var second=new PNActionPublication(sessionBudget:shared);second.Begin(K(2),"test:entry","test:data","test:action",1,0,1);
            C(shared.Used<=shared.Limit,"shared session ceiling");var subscription=new PNActionPublication.Subscription(first);shared.Dispose();C(shared.Used==0&&subscription.Disposed&&first.Disposed&&second.Disposed,"session clears source leases peers");
        }
        using(var source=new PNActionPublication())using(var peer=new PNActionPublication.Subscription(source))
        {
            Begin(source,1);source.Advance(K(1),1,2,1,1);AcceptRequired(peer);var sample=peer.ReadLatest(2,4096).Single();
            C(peer.ReadLatest(2.1,4096).Count==1&&peer.OldestPending(2.1)>.09,"latest remains dirty after read");
            long old=peer.Generation;peer.Reset(source.Sequence);bool rejected=false;try{peer.CommitLatestSubmitted(sample,old);}catch(InvalidOperationException){rejected=true;}C(rejected,"reset fences stale commits");
            var leases=new System.Collections.Generic.List<PNActionPublication.Snapshot>();for(int i=0;i<1100;i++){var lease=source.Capture(scope,2);if(lease==null)break;leases.Add(lease);}
            C(source.LastCaptureStatus==PNActionPublicationStatus.ResourceExhausted&&source.Complete,"snapshot refusal preserves current completeness");foreach(var lease in leases)lease.Dispose();C(source.LeaseBytes==0,"all frozen leases released");
        }
        using(var source=new PNActionPublication())using(var peer=new PNActionPublication.Subscription(source))
        {
            Begin(source,1);source.Advance(K(1),1,2,1,1);AcceptRequired(peer);var sample=peer.ReadLatest(2,4096).Single();
            source.Advance(K(1),2,3,1,1);peer.CommitLatestSubmitted(sample,peer.Generation);
            C(peer.OldestPending(3.5)>=2.5,"source mutation preserves pending age across old commit");
            source.End(K(1),3,PNActionRunEnd.Completed);peer.CommitLatestSubmitted(sample,peer.Generation);C(peer.MaxPendingAge>=2&&peer.OldestPending(3)==0,"terminal retirement records never-sent pending age");
        }
        using(var budget=new PNActionPublicationBudget(1024))using(var source=new PNActionPublication(sessionBudget:budget))
        {
            Begin(source,1);int other=budget.Limit-budget.Used;var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(PNActionPublicationBudget).GetMethod("Take",flags).Invoke(budget,new object[]{other});source.End(K(1),1,PNActionRunEnd.Completed);
            C(source.Complete&&source.Resets==0&&source.ActiveCount==0,"terminal converts fixed reserve under full session charge");
            typeof(PNActionPublicationBudget).GetMethod("Return",flags).Invoke(budget,new object[]{other});
        }
        var weak=new System.Collections.Generic.List<WeakReference>();for(int i=0;i<100;i++)weak.AddRange(DisposedOwners());GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();C(weak.All(reference=>!reference.IsAlive),"100 scope cycles release managed owners");
        var culture=System.Globalization.CultureInfo.CurrentCulture;
        try {System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("fr-FR");using(var source=new PNActionPublication())using(var peer=new PNActionPublication.Subscription(source)){Begin(source,1);source.Advance(K(1),1,1.5,.25,.5);foreach(var record in peer.ReadRequired(4096,99))PNActionC2Codec.Record(JArray.Parse(record.Json));C(true,"culture invariant JSON");}}
        finally {System.Globalization.CultureInfo.CurrentCulture=culture;}
        // Warmed real managed allocation/CPU experiment:64 active, live speed every100frames.
        var times=new double[2000];long allocated=0;long baseline=GC.GetTotalMemory(true);int gen0=GC.CollectionCount(0);long resident=0;
        using(var s=new PNActionPublication())using(var peer=new PNActionPublication.Subscription(s))
        {
            for(int i=1;i<=64;i++)Begin(s,i);double t=1;
            for(int tick=0;tick<2100;tick++)
            {long before=GC.GetAllocatedBytesForCurrentThread();long start=Stopwatch.GetTimestamp();double rate=(tick/100)%2+1;for(int i=1;i<=64;i++)s.Advance(K(i),t,t+.01,.01*rate,rate);t+=.01;s.Seal(t);AcceptRequired(peer);if(tick%5==0)AcceptLatest(peer,t);if(tick%10==0)using(var lease=s.Capture(scope,t))C(lease!=null,"coherent capture");if(tick>=100){times[tick-100]=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;allocated+=GC.GetAllocatedBytesForCurrentThread()-before;}if(tick==2099)resident=GC.GetTotalMemory(true)-baseline;}
            C(s.Complete&&s.ActiveCount==64,"overlap current completeness");s.Dispose();C(s.CurrentBytes==0&&s.JournalBytes==0&&s.LeaseBytes==0,"dispose accounting");
        }
        bool allocationValid=allocated>0;
        Array.Sort(times);double p99=times[(int)(times.Length*.99)],meanAllocated=allocationValid?allocated/2000d:-1;
        var report=new JObject{["checks"]=checks,["p99ms"]=p99,["meanAllocatedBytesPerFrame"]=meanAllocated,["residentDeltaBytes"]=resident,["gen0Collections"]=GC.CollectionCount(0)-gen0,["targetsPass"]=allocationValid&&p99<=4&&meanAllocated<=131072&&resident<=16777216,["note"]="Managed CPU/allocations,64runs after100frame warmup; host runtime must be read from provenance; no FPS or device claim"};
        report["allocationCounterSupported"]=allocationValid;
        File.WriteAllText(Path.Combine(folder,"storage.json"),report.ToString());return report.ToString();
    }
}
#endif
