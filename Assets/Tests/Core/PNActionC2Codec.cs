#if UNITY_EDITOR || PN_ACTION_C2_STANDALONE
// C2 experiment only. No runtime registration, Mirror messages, gameplay or renderer.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class PNActionC2Codec
{
    public static int Bytes(JToken t) => Encoding.UTF8.GetByteCount(t.ToString(Formatting.None));
    public static void Need(bool condition, string why) { if (!condition) throw new InvalidDataException(why); }
    public static JArray Array(JToken t, int count) { Need(t is JArray && t.Count() == count, "array arity"); return (JArray)t; }
    public static long Integer(JToken t, long min = 1) { Need(t != null && t.Type == JTokenType.Integer, "integer token"); long n = checked((long)t); Need(n >= min, "integer range"); return n; }
    public static double Number(JToken t, double min = 0) { Need(t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer), "number token"); double n = (double)t; Need(!double.IsNaN(n) && !double.IsInfinity(n) && n >= min, "finite number"); return n == 0 ? 0 : n; }
    public static string Key(JToken t) { var a = Array(t, 2); long cast = Integer(a[0], long.MinValue); Need(cast != 0, "cast zero"); return cast + ":" + Integer(a[1]); }
    public static void Context(JToken t)
    {
        var a = Array(t, 7);
        for (int i = 0; i < 3; i++) { Need(a[i].Type == JTokenType.String, "id type"); string s = (string)a[i]; Need(s.Length <= 96 && Encoding.UTF8.GetByteCount(s) <= 384 && Regex.IsMatch(s, @"^[a-z0-9_.-]+:[a-z0-9_./-]+$"), "id"); string ns=s.Substring(0,s.IndexOf(':')); Need(ns.Length<=64&&ns!="."&&ns!="..","id namespace"); }
        Need(Integer(a[3]) == 1, "capability"); double x = Number(a[4], -1), z = Number(a[5], -1); Need(x <= 1 && z <= 1 && x*x+z*z > 1e-8, "direction"); Number(a[6]);
    }
    public static void Scope(JToken t)
    {
        var a = Array(t, 6);
        foreach (int i in new[] {0,1,5}) Need(a[i].Type == JTokenType.String && Regex.IsMatch((string)a[i], "^[0-9a-f]{32}$") && (string)a[i] != new string('0',32), "scope guid");
        Need(a[2].Type == JTokenType.Integer && checked((ulong)a[2]) >= 1, "incarnation"); Integer(a[3]); Integer(a[4]);
    }
    public static JArray MakeScope(int source) => new JArray(new string('1',32), new string('2',32), 1, source, 1, source.ToString("x32"));
    public static JToken Parse(string text, int cap = 4096)
    {
        Need(Encoding.UTF8.GetByteCount(text) <= cap, "byte cap");
        using (var reader = new JsonTextReader(new StringReader(text)) { MaxDepth = 16, DateParseHandling = DateParseHandling.None })
        { var t = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); Need(!reader.Read(), "trailing JSON"); return t; }
    }
    public static void Record(JArray a)
    {
        Need(Bytes(a) <= 3840, "record cap"); int tag = checked((int)Integer(a[0],0)); Need(tag <= 4, "tag"); Array(a, new[]{4,8,6,7,8}[tag]);
        Key(a[tag == 1 ? 1 : 2]); if(tag != 1) Integer(a[1]);
        if(tag == 0) Context(a[3]);
        if(tag == 1) { Integer(a[2]); Number(a[3]); Number(a[4]); Number(a[5]); Integer(a[6]); Integer(a[7],0); }
        if(tag == 2 || tag == 3) { Integer(a[3]); Integer(a[4]); var anchor = Array(a[tag == 2 ? 5 : 6],3); foreach(var v in anchor) Number(v); if(tag==3) Need(Integer(a[5]) <= 2,"reset reason"); }
        if(tag == 4) { Integer(a[3]); Need(Integer(a[4]) <= 2,"terminal kind"); Number(a[5]); Number(a[6]); Integer(a[7]); }
    }
    public static JObject Envelope(int source, long sub, long q, double t, IEnumerable<JArray> records)
        => new JObject { ["v"]=2,["scope"]=MakeScope(source),["sub"]=sub,["q"]=q,["t"]=t,["records"]=new JArray(records.Select(r=>r.DeepClone())) };

    // Experiment multipart framing, not the final authenticated T5.2 header.
    public static List<JObject> SplitBaseline(JArray baseline,long sub,string id)
    {
        Array(baseline,6);var groups=new List<JArray>();var rows=new JArray();
        JObject Part(JArray values,int index,int count,int total) => new JObject { ["v"]=2,["id"]=id,["sub"]=sub,["scope"]=baseline[1].DeepClone(),["q"]=baseline[2],["t"]=baseline[3],["n"]=baseline[4],["i"]=index,["count"]=count,["total"]=total,["rows"]=values.DeepClone() };
        foreach(var row in (JArray)baseline[5])
        {rows.Add(row.DeepClone());if(Bytes(Part(rows,319,320,1048576))>4096){rows.RemoveAt(rows.Count-1);Need(rows.Count>0,"baseline row too large");groups.Add(rows);rows=new JArray{row.DeepClone()};Need(Bytes(Part(rows,319,320,1048576))<=4096,"baseline row too large");}}
        if(rows.Count>0||groups.Count==0)groups.Add(rows);Need(groups.Count<=320,"baseline part bound");int total=groups.Sum(Bytes);Need(total<=1048576,"baseline byte bound");
        return groups.Select((g,i)=>Part(g,i,groups.Count,total)).ToList();
    }
    public sealed class Assembly : IDisposable
    {
        public readonly double Started,Deadline; public int PeakBytes,PeakParts;public bool Committed;public long AppliedQ;public string AppliedId;
        readonly Dictionary<int,JArray> parts=new Dictionary<int,JArray>();readonly JObject expected;int bytes;
        public Assembly(JObject first,double started) {Started=started;Deadline=started+3;expected=(JObject)first.DeepClone();expected.Remove("rows");expected.Remove("i");}
        public void Receive(string text,double now,Decoder model)
        {
            Need(now<=Deadline,"original recovery deadline");Need(!Committed,"already applied");var p=Parse(text) as JObject;Need(p!=null&&p.Properties().Count()==11,"fragment fields");
            int index=checked((int)Integer(p["i"],0)),count=checked((int)Integer(p["count"])),total=checked((int)Integer(p["total"]));Need(count<=320&&index<count&&total<=1048576,"fragment bounds");Need(p["rows"] is JArray,"fragment rows");
            var identity=(JObject)p.DeepClone();identity.Remove("rows");identity.Remove("i");Need(JToken.DeepEquals(identity,expected),"mixed baseline cut");
            var rows=(JArray)p["rows"];if(parts.TryGetValue(index,out var previous)){Need(JToken.DeepEquals(rows,previous),"fragment duplicate conflict");return;}
            int charge=Bytes(rows);Need(bytes+charge<=total,"staging declared bytes");parts.Add(index,(JArray)rows.DeepClone());bytes+=charge;PeakBytes=Math.Max(PeakBytes,bytes);PeakParts=Math.Max(PeakParts,parts.Count);
            if(parts.Count!=count)return;Need(bytes==total,"baseline total mismatch");var all=new JArray();for(int i=0;i<count;i++)foreach(var row in parts[i])all.Add(row.DeepClone());
            var baseline=new JArray(2,p["scope"].DeepClone(),p["q"],p["t"],p["n"],all);
            model.Baseline(baseline,(long)p["sub"]);Committed=true;AppliedQ=(long)p["q"];AppliedId=(string)p["id"];Dispose();
        }
        public void Dispose(){parts.Clear();bytes=0;}
    }

    public sealed class Run
    {
        public JArray K,D; public int BaseCharge; public long Rev=1,Hg=1; public double T,P,R,Floor; public bool Ended;
        public readonly List<double[]> Anchors = new List<double[]>();
        public double At(double t) { Need(t >= Floor && t <= T + 1e-9,"projection coverage"); var a=Anchors.Last(x=>x[0]<=t+1e-10); double p=a[1]+(t-a[0])*a[2]; Need(!double.IsInfinity(p) && !double.IsNaN(p),"projection overflow"); return p; }
    }
    public sealed class Decoder
    {
        public readonly Dictionary<string,Run> Runs = new Dictionary<string,Run>();
        public long N,Sub=1; public JArray Identity; public int Starts,Ends,Ignored; public double Floor;
        readonly Dictionary<long,string> duplicates = new Dictionary<long,string>();
        int duplicateCharge;
        public void Prune(double time)
        {
            double floor=time-3;
            foreach(var pair in Runs.ToArray())
            {
                var r=pair.Value;if(r.Ended&&r.T<floor){Runs.Remove(pair.Key);continue;}
                while(r.Anchors.Count>1&&r.Anchors[1][0]<=floor)r.Anchors.RemoveAt(0);
                r.Floor=Math.Max(r.Floor,Math.Min(r.T,floor));
            }
        }
        public int ResidentCharge => Runs.Values.Sum(r=>r.BaseCharge+r.Anchors.Count*64)+duplicateCharge;
        public void Packet(string text)
        {
            var o=Parse(text) as JObject; Need(o!=null && o.Properties().Count()==6,"envelope fields");
            Need(Integer(o["v"])==2,"version"); Scope(o["scope"]); long sub=Integer(o["sub"]); Integer(o["q"]); double t=Number(o["t"]);
            Need(o["records"] is JArray,"records");
            if(Identity!=null && (!JToken.DeepEquals(Identity,o["scope"]) || sub<Sub)) {Ignored++;return;}
            Need(sub==Sub,"unprepared subscription"); if(Identity==null) Identity=(JArray)o["scope"].DeepClone();
            foreach(var r in (JArray)o["records"]) { var a=r as JArray; Need(a!=null,"record type"); Record(a); int tag=(int)a[0]; if(tag==0)Need(t>=Number(a[3][6]),"capture start"); if(tag==1)Need(t>=Number(a[3]),"capture state"); if(tag==2||tag==3)Need(t>=Number(a[tag==2?5:6][0]),"capture anchor"); if(tag==4)Need(t>=Number(a[5]),"capture end"); Apply(a); }
        }
        public void Apply(JArray a, bool validate = true)
        {
            if(validate)Record(a); int tag=(int)a[0]; string key=Key(a[tag==1?1:2]);
            if(tag!=1) { long n=(long)a[1]; if(n<=N) {if(duplicates.TryGetValue(n,out var prior))Need(prior==a.ToString(Formatting.None),"duplicate conflict");return;} Need(n==N+1,"required gap"); }
            if(tag==0)
            {
                Need(!Runs.ContainsKey(key),"identity reuse"); var d=(JArray)a[3]; double start=(double)d[6]; var run=new Run {K=(JArray)a[2].DeepClone(),D=(JArray)d.DeepClone(),T=start,Floor=start}; run.BaseCharge=128+Bytes(run.K)+Bytes(run.D); run.Anchors.Add(new[]{start,0d,0d}); Runs.Add(key,run); Starts++;
            }
            else
            {
                Need(Runs.TryGetValue(key,out var r),"missing context");
                if(tag==1)
                {
                    Need((long)a[7]<=N,"state dependency"); if(r.Ended || (long)a[2]<r.Rev)return;
                    Need((long)a[6]==r.Hg,"state generation"); double t=(double)a[3],p=(double)a[4]; Need(t>=r.T && p>=r.P-1e-7,"state monotonic");
                    var last=r.Anchors.Last(); Need(Math.Abs(last[1]+(t-last[0])*last[2]-p)<=1e-5,"state progress oracle");Need((double)a[5]==last[2],"state rate anchor mismatch");
                    r.Rev=(long)a[2];r.T=t;r.P=p;r.R=(double)a[5];
                }
                else
                {
                    Need(!r.Ended,"required after terminal"); long rev=(long)a[tag==4?3:4]; Need(rev>=r.Rev,"revision rollback");
                    if(tag==2 || tag==3)
                    {
                        var anchor=(JArray)a[tag==2?5:6];double t=(double)anchor[0],p=(double)anchor[1],rate=(double)anchor[2]; var last=r.Anchors.Last();
                        if(tag==3) {Need((long)a[3]==checked(r.Hg+1),"reset generation");r.Anchors.Clear();r.Floor=t;r.Hg++;}
                        else {Need((long)a[3]==r.Hg && t>=last[0] && p>=last[1]-1e-7,"anchor order");Need(t!=last[0]||p==last[1],"same stamp correction requires reset");}
                        r.Anchors.Add(new[]{t,p,rate});r.T=Math.Max(t,r.T);r.P=p;r.R=rate;
                    }
                    else {Need((long)a[7]==r.Hg && (double)a[5]>=r.T && (double)a[6]>=r.P-1e-7,"terminal order"); r.T=(double)a[5];r.P=(double)a[6];Need(Math.Abs(r.At(r.T)-r.P)<=1e-5,"terminal oracle");r.R=0;r.Ended=true;Ends++;}
                    r.Rev=rev;
                }
            }
            if(tag!=1) {N=(long)a[1];duplicates[N]=a.ToString(Formatting.None); duplicateCharge+=64+duplicates[N].Length*2; if(duplicates.Count>256){duplicateCharge-=64+duplicates[N-256].Length*2;duplicates.Remove(N-256);}}
        }
        // Called only after complete bounded assembly. Validate into a fresh model before swap.
        public void Baseline(JArray b,long sub)
        {
            Array(b,6);Need(Integer(b[0])==2,"baseline version");Scope(b[1]);Integer(b[2]);double t=Number(b[3]);long cut=Integer(b[4],0);Need(b[5] is JArray,"baseline rows");Need(sub>Sub,"baseline subscription");
            var fresh=new Dictionary<string,Run>();long previousCast=long.MinValue,previousOrdinal=0;
            foreach(var row in (JArray)b[5]) {var a=Array(row,6);string key=Key(a[0]);Context(a[1]);long cast=(long)a[0][0],ordinal=(long)a[0][1];Need(cast>previousCast || cast==previousCast&&ordinal>previousOrdinal,"baseline sorted unique");previousCast=cast;previousOrdinal=ordinal;Integer(a[2]);Integer(a[3]);double p=Number(a[4]),rate=Number(a[5]);Need(t>=(double)a[1][6],"baseline start");var r=new Run{K=(JArray)a[0].DeepClone(),D=(JArray)a[1].DeepClone(),Rev=(long)a[2],Hg=(long)a[3],T=t,P=p,R=rate,Floor=t};r.BaseCharge=128+Bytes(r.K)+Bytes(r.D);r.Anchors.Add(new[]{t,p,rate});fresh.Add(key,r);}
            Runs.Clear();foreach(var p in fresh)Runs.Add(p.Key,p.Value);Identity=(JArray)b[1].DeepClone();N=cut;Sub=sub;Floor=t;duplicates.Clear();duplicateCharge=0;
        }
    }
}

#endif
