using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace ProjectN.Coop {
// Runtime grammar and model derived from C2 vectors, audited for bounds/atomic use.
internal static class PNActionState {
    static readonly Unity.Profiling.ProfilerMarker CloneMarker = new Unity.Profiling.ProfilerMarker("PN.Action.DecodeClone");
    static readonly Unity.Profiling.ProfilerMarker PruneMarker = new Unity.Profiling.ProfilerMarker("PN.Action.DecodePrune");
    static readonly Unity.Profiling.ProfilerMarker ChargeMarker = new Unity.Profiling.ProfilerMarker("PN.Action.DecodeCharge");
    public static int Bytes(JToken t)
    {
        if(t is JArray array){int n=2+Math.Max(0,array.Count-1);foreach(var value in array)n=checked(n+Bytes(value));return n;}
        if(t.Type==JTokenType.Integer)return JsonConvert.ToString(checked((long)t)).Length;
        if(t.Type==JTokenType.Float)return JsonConvert.ToString((double)t).Length;
        if(t.Type==JTokenType.String){string text=(string)t;bool plain=true;foreach(char c in text)if(c<32||c>126||c=='"'||c=='\\'){plain=false;break;}if(plain)return text.Length+2;}
        return Encoding.UTF8.GetByteCount(t.ToString(Formatting.None));
    }
    // Validated records have a fixed primitive grammar. Avoid a JsonTextWriter
    // and its scratch buffers for every retained duplicate; preserve its exact text.
    public static string Canonical(JToken token)
    {
        var text=new StringBuilder(128);
        void Append(JToken value)
        {
            if(value is JArray array){text.Append('[');bool first=true;foreach(var item in array){if(!first)text.Append(',');first=false;Append(item);}text.Append(']');return;}
            if(value.Type==JTokenType.Integer){text.Append(JsonConvert.ToString(checked((long)value)));return;}
            if(value.Type==JTokenType.Float){object number=((JValue)value).Value;text.Append(number is decimal dec?JsonConvert.ToString(dec):number is float single?JsonConvert.ToString(single):JsonConvert.ToString((double)value));return;}
            if(value.Type==JTokenType.String){string item=(string)value;bool plain=true;foreach(char c in item)if(c<32||c>126||c=='"'||c=='\\'){plain=false;break;}if(plain)text.Append('"').Append(item).Append('"');else text.Append(JsonConvert.ToString(item));return;}
            text.Append(value.ToString(Formatting.None));
        }
        Append(token);return text.ToString();
    }
    public static void Need(bool condition, string why) { if (!condition) throw new InvalidDataException(why); }
    public static JArray Array(JToken t, int count) { Need(t is JArray && t.Count() == count, "array arity"); return (JArray)t; }
    public static long Integer(JToken t, long min = 1) { Need(t != null && t.Type == JTokenType.Integer, "integer token"); long n = checked((long)t); Need(n >= min, "integer range"); return n; }
    public static double Number(JToken t, double min = 0) { Need(t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer), "number token"); double n = (double)t; Need(!double.IsNaN(n) && !double.IsInfinity(n) && n >= min, "finite number"); return n == 0 ? 0 : n; }
    public static string Key(JToken t) { var a = Array(t, 2); long cast = Integer(a[0], long.MinValue); Need(cast != 0, "cast zero"); return cast + ":" + Integer(a[1]); }
    public static void Context(JToken t)
    {
        Need(t is JArray && ((JArray)t).Count >= 7 && ((JArray)t).Count <= 9, "Action context size");
        var a = (JArray)t;
        for (int i = 0; i < 3; i++) { Need(a[i].Type == JTokenType.String, "id type"); string s = (string)a[i]; Need(s.Length <= 96 && Encoding.UTF8.GetByteCount(s) <= 384 && Regex.IsMatch(s, @"^[a-z0-9_.-]+:[a-z0-9_./-]+$"), "id"); string ns=s.Substring(0,s.IndexOf(':')); Need(ns.Length<=64&&ns!="."&&ns!="..","id namespace"); }
        // Capability is the context mask: 1 direction, 2 position, 4 targets (G8c). Mask 1 is the whole of the first stages' rows.
        long mask = Integer(a[3], 0); Need(mask <= PNActionRunContext.AllBits, "capability");
        double x = Number(a[4], -1), z = Number(a[5], -1);
        if ((mask & PNActionRunContext.DirectionBit) != 0) Need(x <= 1 && z <= 1 && x*x+z*z > 1e-8, "direction"); else Need(x == 0 && z == 0, "direction absent");
        Number(a[6]);
        bool extra = (mask & (PNActionRunContext.PositionBit | PNActionRunContext.TargetsBit)) != 0;
        Need(extra ? a.Count == 9 : (a.Count == 7 || a.Count == 8), "Action context arity");
        if (a.Count >= 8) Number(a[7]);
        if (extra)
        {
            var ext = (JArray)(a[8] is JArray ? a[8] : null); Need(ext != null, "context extra");
            int at = 0;
            if ((mask & PNActionRunContext.PositionBit) != 0)
            {
                Need(ext.Count >= 3, "position size");
                for (int i = 0; i < 3; i++) { Need(ext[i].Type == JTokenType.Float || ext[i].Type == JTokenType.Integer, "position token"); double v = (double)ext[i]; Need(!double.IsNaN(v) && !double.IsInfinity(v) && Math.Abs(v) <= PNActionRunContext.MaxCoordinate, "position range"); }
                at = 3;
            }
            int targets = ext.Count - at;
            if ((mask & PNActionRunContext.TargetsBit) != 0) Need(targets >= 1 && targets <= PNActionRunContext.MaxTargets, "targets size"); else Need(targets == 0, "targets absent");
            for (int i = at; i < ext.Count; i++) Integer(ext[i]);
        }
    }
    public static void Scope(JToken t)
    {
        var a = Array(t, 6);
        foreach (int i in new[] {0,1,5}) Need(a[i].Type == JTokenType.String && Regex.IsMatch((string)a[i], "^[0-9a-f]{32}$") && (string)a[i] != new string('0',32), "scope guid");
        Need(a[2].Type == JTokenType.Integer && checked((ulong)a[2]) >= 1, "incarnation"); Integer(a[3]); Integer(a[4]);
    }
    // PV2: a presentation fact `[age, key, values...]` (PNActionFact bounds); returns the immutable value the views read.
    public static PNActionFact Fact(JToken t)
    {
        Need(t is JArray && ((JArray)t).Count >= 2 && ((JArray)t).Count <= 2 + PNActionFact.MaxValues, "fact arity");
        var a = (JArray)t; double age = Number(a[0]);
        Need(a[1].Type == JTokenType.String && PNActionFact.ValidKey((string)a[1]), "fact key");
        var values = new double[a.Count - 2];
        for (int i = 2; i < a.Count; i++) { Need(a[i].Type == JTokenType.Float || a[i].Type == JTokenType.Integer, "fact number token"); double v = (double)a[i]; Need(PNActionFact.ValidValue(v), "fact number"); values[i - 2] = v; }
        return new PNActionFact(age, (string)a[1], values);
    }
    public static JArray MakeScope(int source) => new JArray(new string('1',32), new string('2',32), 1, source, 1, source.ToString("x32"));
    public static void Record(JArray a)
    {
        // Fixed arities, 96-character IDs and bounded numeric scalars imply a
        // canonical JSON row below3840 bytes. Do not stringify every U/R just to
        // recheck that redundant bound; wire decode already bounds the body.
        Need(a!=null&&a.Count>0,"record cap/arity"); int tag = checked((int)Integer(a[0],0)); Need(tag <= 5, "tag"); Array(a, new[]{4,8,6,7,8,5}[tag]);
        Key(a[tag == 1 ? 1 : 2]); if(tag != 1) Integer(a[1]);
        if(tag == 0) Context(a[3]);
        if(tag == 1) { Integer(a[2]); Number(a[3]); Number(a[4]); Number(a[5]); Integer(a[6]); Integer(a[7],0); }
        if(tag == 2 || tag == 3) { Integer(a[3]); Integer(a[4]); var anchor = Array(a[tag == 2 ? 5 : 6],3); foreach(var v in anchor) Number(v); if(tag==3) Need(Integer(a[5]) <= 2,"reset reason"); }
        if(tag == 4) { Integer(a[3]); Need(Integer(a[4]) <= 2,"terminal kind"); Number(a[5]); Number(a[6]); Integer(a[7]); }
        if(tag == 5) { Number(a[3]); Fact(a[4]); }
    }
    public sealed class Run
    {
        public JArray K,D; public int BaseCharge; public long Rev=1,Hg=1; public double T,P,R,Floor; public bool Ended;
        public PNActionRunEnd EndKind;
        public readonly List<double[]> Anchors = new List<double[]>();
        // PV2: the run's presentation facts in arrival order (append only, at most PNActionFact.MaxPerRun).
        public readonly List<PNActionFact> Facts = new List<PNActionFact>();
        public double At(double t,double retentionFloor) { Need(t >= Math.Max(Floor,Math.Min(T,retentionFloor)) && t <= T + 1e-9,"projection coverage"); var a=Anchors.Last(x=>x[0]<=t+1e-10); double p=a[1]+(t-a[0])*a[2]; Need(!double.IsInfinity(p) && !double.IsNaN(p),"projection overflow"); return p; }
    }
    public sealed class Decoder
    {
        public readonly Dictionary<string,Run> Runs;
        public long N,Sub=1; public JArray Identity; public int Starts,Ends,Ignored; public double Floor,RetentionFloor,CompleteThrough;
        bool borrowed;HashSet<string> owned;
        Run Own(string key,Run r)
        {
            if(!borrowed||owned.Contains(key))return r;
            var v=new Run{K=r.K,D=r.D,BaseCharge=r.BaseCharge,Rev=r.Rev,Hg=r.Hg,T=r.T,P=r.P,R=r.R,Floor=r.Floor,Ended=r.Ended,EndKind=r.EndKind};v.Anchors.AddRange(r.Anchors);v.Facts.AddRange(r.Facts);Runs[key]=v;owned.Add(key);return v;
        }
        Dictionary<long,string> duplicates; bool duplicateBorrowed;
        public Decoder(int runCapacity=0,int duplicateCapacity=0){Runs=new Dictionary<string,Run>(runCapacity);duplicates=new Dictionary<long,string>(duplicateCapacity);}
        int duplicateCharge,runCharge;
        public void Prune(double time)
        { using(PruneMarker.Auto()) {
            double floor=time-3;
            RetentionFloor=Math.Max(RetentionFloor,floor);
            List<KeyValuePair<string,Run>> changes=null;
            foreach(var pair in Runs)
                if(pair.Value.Ended&&pair.Value.T<floor||pair.Value.Anchors.Count>1&&pair.Value.Anchors[1][0]<=floor)
                    (changes??(changes=new List<KeyValuePair<string,Run>>())).Add(pair);
            if(changes!=null)foreach(var pair in changes)
            {
                var r=pair.Value;if(r.Ended&&r.T<floor){runCharge-=r.BaseCharge+r.Anchors.Count*64;Runs.Remove(pair.Key);continue;}
                // Coverage moves globally; copying every untouched Run merely to
                // advance its floor defeats copy-on-write. Own only real trims.
                if(r.Anchors.Count>1&&r.Anchors[1][0]<=floor){r=Own(pair.Key,r);while(r.Anchors.Count>1&&r.Anchors[1][0]<=floor){r.Anchors.RemoveAt(0);runCharge-=64;}r.Floor=Math.Max(r.Floor,Math.Min(r.T,floor));}
            }
        }
        }
        public int ResidentCharge { get { using(ChargeMarker.Auto()) return runCharge+duplicateCharge; } }
        public Decoder Clone()
        { using(CloneMarker.Auto()) {
            // Identity/context/key arrays and numeric anchor arrays are immutable
            // after creation. Only the Run fields and anchor LIST mutate. Sharing
            // these values preserves rollback without cloning every JSON scalar
            // in the three-second history on each live batch.
            var copy=new Decoder(Math.Min(4096,Runs.Count+32),0){N=N,Sub=Sub,Identity=Identity,Starts=Starts,Ends=Ends,Ignored=Ignored,Floor=Floor,RetentionFloor=RetentionFloor,CompleteThrough=CompleteThrough,duplicateCharge=duplicateCharge,runCharge=runCharge,duplicateBorrowed=true,borrowed=true,owned=new HashSet<string>()};
            duplicateBorrowed=true;copy.duplicates=duplicates;
            foreach(var pair in Runs)copy.Runs.Add(pair.Key,pair.Value);return copy;
        }
        }
        public bool Apply(JArray a, bool validate = true)
        {
            if(validate)Record(a); int tag=(int)a[0]; string key=Key(a[tag==1?1:2]);
            if(tag!=1) { long n=(long)a[1]; if(n<=N) {if(duplicates.TryGetValue(n,out var prior))Need(prior==Canonical(a),"duplicate conflict");return false;} Need(n==N+1,"required gap"); }
            if(tag==0)
            {
                Need(!Runs.ContainsKey(key),"identity reuse"); var d=(JArray)a[3]; double start=(double)d[6]; var run=new Run {K=(JArray)a[2].DeepClone(),D=(JArray)d.DeepClone(),T=start,Floor=start}; run.BaseCharge=128+Bytes(run.K)+Bytes(run.D); run.Anchors.Add(new[]{start,0d,0d}); Need(Runs.Count<4096,"run bound");Runs.Add(key,run);runCharge+=run.BaseCharge+64;if(borrowed)owned.Add(key); Starts++;
            }
            else
            {
                Need(Runs.TryGetValue(key,out var r),"missing context");
                r=Own(key,r);
                if(tag==5)
                {
                    Need(!r.Ended,"fact after terminal"); Need(r.Facts.Count<PNActionFact.MaxPerRun,"fact bound");
                    Need((double)a[3]>=r.Floor,"fact stamp"); r.Facts.Add(Fact(a[4])); runCharge+=64;
                }
                else if(tag==1)
                {
                    Need((long)a[7]<=N,"state dependency"); if(r.Ended || (long)a[2]<r.Rev)return false;
                    Need((long)a[6]==r.Hg,"state generation"); double t=(double)a[3],p=(double)a[4]; Need(t>=r.T && p>=r.P-1e-7,"state monotonic");
                    var last=r.Anchors.Last(); Need(Math.Abs(last[1]+(t-last[0])*last[2]-p)<=1e-5,"state progress oracle");Need((double)a[5]==last[2],"state rate anchor mismatch");
                    bool changed=r.Rev!=(long)a[2]||r.T!=t||r.P!=p||r.R!=(double)a[5];
                    r.Rev=(long)a[2];r.T=t;r.P=p;r.R=(double)a[5];return changed;
                }
                else
                {
                    Need(!r.Ended,"required after terminal"); long rev=(long)a[tag==4?3:4]; Need(rev>=r.Rev,"revision rollback");
                    if(tag==2 || tag==3)
                    {
                        int oldAnchorCount=r.Anchors.Count;var anchor=(JArray)a[tag==2?5:6];double t=(double)anchor[0],p=(double)anchor[1],rate=(double)anchor[2]; var last=r.Anchors.Last();
                        if(tag==3) {Need((long)a[3]==checked(r.Hg+1),"reset generation");r.Anchors.Clear();r.Floor=t;r.Hg++;}
                        else {Need((long)a[3]==r.Hg && t>=last[0] && p>=last[1]-1e-7,"anchor order");Need(t!=last[0]||p==last[1],"same stamp correction requires reset");}
                        Need(r.Anchors.Count<1024,"anchor bound");r.Anchors.Add(new[]{t,p,rate});runCharge+=(r.Anchors.Count-oldAnchorCount)*64;r.T=Math.Max(t,r.T);r.P=p;r.R=rate;
                    }
                    else {Need((long)a[7]==r.Hg && (double)a[5]>=r.T && (double)a[6]>=r.P-1e-7,"terminal order"); r.T=(double)a[5];r.P=(double)a[6];Need(Math.Abs(r.At(r.T,RetentionFloor)-r.P)<=1e-5,"terminal oracle");r.R=0;r.Ended=true;r.EndKind=(PNActionRunEnd)(int)a[4];Ends++;}
                    r.Rev=rev;
                }
            }
            if(tag!=1) {if(duplicateBorrowed){duplicates=new Dictionary<long,string>(duplicates);duplicateBorrowed=false;}N=(long)a[1];duplicates[N]=Canonical(a); duplicateCharge+=64+duplicates[N].Length*2; if(duplicates.Count>256){duplicateCharge-=64+duplicates[N-256].Length*2;duplicates.Remove(N-256);}}
            return true;
        }
        public bool ApplyLatest(PNActionWire.LatestValue value)
        {
            string key=value.Cast+":"+value.Ordinal;Need(Runs.TryGetValue(key,out var r),"missing context");
            Need(value.NeedSequence<=N,"state dependency");if(r.Ended||value.Revision<r.Rev)return false;
            Need(value.HistoryGeneration==r.Hg,"state generation");Need(value.Stamp>=r.T&&value.Progress>=r.P-1e-7,"state monotonic");
            var last=r.Anchors[r.Anchors.Count-1];Need(Math.Abs(last[1]+(value.Stamp-last[0])*last[2]-value.Progress)<=1e-5,"state progress oracle");Need(value.Rate==last[2],"state rate anchor mismatch");
            bool changed=r.Rev!=value.Revision||r.T!=value.Stamp||r.P!=value.Progress||r.R!=value.Rate;
            r=Own(key,r);r.Rev=value.Revision;r.T=value.Stamp;r.P=value.Progress;r.R=value.Rate;return changed;
        }
        // Called only after complete bounded assembly. Validate into a fresh model before swap.
        public void Baseline(JArray b,long sub)
        {
            Array(b,6);Need(Integer(b[0])==2,"baseline version");Scope(b[1]);Integer(b[2]);double t=Number(b[3]);long cut=Integer(b[4],0);Need(b[5] is JArray,"baseline rows");Need(sub>Sub,"baseline subscription");
            var fresh=new Dictionary<string,Run>();long previousCast=long.MinValue,previousOrdinal=0;
            foreach(var row in (JArray)b[5]) {Need(row is JArray && (((JArray)row).Count==6||((JArray)row).Count==7),"baseline row arity");var a=(JArray)row;string key=Key(a[0]);Context(a[1]);long cast=(long)a[0][0],ordinal=(long)a[0][1];Need(cast>previousCast || cast==previousCast&&ordinal>previousOrdinal,"baseline sorted unique");previousCast=cast;previousOrdinal=ordinal;Integer(a[2]);Integer(a[3]);double p=Number(a[4]),rate=Number(a[5]);Need(t>=(double)a[1][6],"baseline start");var r=new Run{K=(JArray)a[0].DeepClone(),D=(JArray)a[1].DeepClone(),Rev=(long)a[2],Hg=(long)a[3],T=t,P=p,R=rate,Floor=t};r.BaseCharge=128+Bytes(r.K)+Bytes(r.D);r.Anchors.Add(new[]{t,p,rate});if(a.Count==7){Need(a[6] is JArray && ((JArray)a[6]).Count>=1 && ((JArray)a[6]).Count<=PNActionFact.MaxPerRun,"baseline facts");foreach(var f in (JArray)a[6])r.Facts.Add(Fact(f));r.BaseCharge+=64*r.Facts.Count;}Need(fresh.Count<4096,"baseline run bound");fresh.Add(key,r);}
            Runs.Clear();foreach(var p in fresh)Runs.Add(p.Key,p.Value);Identity=(JArray)b[1].DeepClone();N=cut;Sub=sub;Floor=t;RetentionFloor=t-3;CompleteThrough=t;borrowed=false;owned=null;duplicates=new Dictionary<long,string>();duplicateBorrowed=false;duplicateCharge=0;runCharge=fresh.Values.Sum(r=>r.BaseCharge+r.Anchors.Count*64);
        }
    }
}
}
