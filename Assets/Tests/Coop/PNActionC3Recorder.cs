using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace ProjectN.Coop.Tests
{
    // Opt-in measurement only. Records all eligible runs independently of legacy admission.
    public sealed class PNActionC3Recorder : IDisposable
    {
        readonly PNSkill skill;readonly StreamWriter writer;readonly string resultPath;
        readonly Dictionary<PNActionRunKey,double> progress=new Dictionary<PNActionRunKey,double>();
        PNActionPublication source;PNActionPublication.Subscription peer;double next;long cursor;
        int starts,ends,decodedStarts,decodedEnds,drivers,errors;long bytes;int peakCurrent,peakJournal,peakLeases;double maxError;
        readonly bool previousEnabled;
        Unity.Profiling.ProfilerRecorder frameAllocation;
        UnityEngine.Profiling.Recorder sourceCpu;bool previousCpuEnabled;int cpuHits;
        readonly List<double> cpuMilliseconds=new List<double>();readonly List<long> frameBytes=new List<long>();
        long peakHeap;readonly int initialCollections=GC.CollectionCount(0);
        public static bool Enabled=>Environment.GetCommandLineArgs().Contains("--pn-action-publication-shadow");
        public PNActionC3Recorder(PNSkill subject,string folder,string name)
        {
            skill=subject;writer=new StreamWriter(Path.Combine(folder,name+"-c3.jsonl")){AutoFlush=true};resultPath=Path.Combine(folder,name+"-c3-result.json");
            frameAllocation=Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory,"GC Allocated In Frame",1);sourceCpu=UnityEngine.Profiling.Recorder.Get("PN.ActionPublication");previousCpuEnabled=sourceCpu.enabled;sourceCpu.enabled=true;
            previousEnabled=PNSkill.ActionPublicationEnabled;PNSkill.ActionPublicationEnabled=true;PNActionRunStore.Measurement+=Record;
        }
        void Record(PNActionRunStore store,JObject e)
        {
            if(store!=skill.ActionRuns)return;
            try
            {
                var key=(long)e["cast"]==0?default:new PNActionRunKey((long)e["cast"],(long)e["ordinal"]);string kind=(string)e["kind"];
                if(kind=="attempt"){progress.Add(key,0);starts++;}
                if(kind=="driver")
                {
                    progress[key]+=(double)e["value"];drivers++;
                    var publication=skill.ActionPublication;if(publication==null||!publication.Complete)throw new Exception("Missing complete publication source");
                    double error=Math.Abs(publication.Progress(key)-progress[key]);maxError=Math.Max(maxError,error);if(error>1e-5)throw new Exception("Driver shadow oracle");
                }
                if(kind=="finish-attempt")ends++;
            }
            catch(Exception ex){errors++;writer.WriteLine(new JObject{["error"]=ex.ToString()}.ToString(Newtonsoft.Json.Formatting.None));}
        }
        public void Tick(double now)
        {
            try
            {
                long allocated=frameAllocation.Valid?frameAllocation.LastValue:-1;double cpu=sourceCpu.elapsedNanoseconds/1000000d;
                cpuHits+=sourceCpu.sampleBlockCount;frameBytes.Add(allocated);cpuMilliseconds.Add(cpu);peakHeap=Math.Max(peakHeap,UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong());
                writer.WriteLine(new JObject{["stamp"]=now,["kind"]="performance",["frameAllocatedBytes"]=allocated,["sourceCpuMs"]=cpu,["sourceCpuSamples"]=sourceCpu.sampleBlockCount}.ToString(Newtonsoft.Json.Formatting.None));
                if(now<next)return;next=now+.05;
                if(skill.ActionPublication==null)return;
                if(!ReferenceEquals(source,skill.ActionPublication))
                {peer?.Dispose();source=skill.ActionPublication;peer=new PNActionPublication.Subscription(source);cursor=0;}
                if(!source.Complete||source.Disposed||skill.ActionPublicationErrors!=0)throw new Exception("Source incomplete/faulted");
                foreach(var record in peer.ReadRequired(16384,1000))
                {
                    var a=JArray.Parse(record.Json);if(record.N!=cursor+1)throw new Exception("Required gap");cursor=record.N;
                    int tag=(int)a[0];var key=new PNActionRunKey((long)a[2][0],(long)a[2][1]);
                    if(tag==0)decodedStarts++;
                    if(tag==4){decodedEnds++;double error=Math.Abs((double)a[6]-progress[key]);maxError=Math.Max(maxError,error);if(error>1e-5)throw new Exception("Terminal shadow oracle");}
                    bytes+=record.Bytes;writer.WriteLine(new JObject{["stamp"]=now,["entity"]=skill.GetComponent<EveEngine.EveEntityIdentity>().Id.ToString(),["kind"]="required",["bytes"]=record.Bytes,["record"]=a}.ToString(Newtonsoft.Json.Formatting.None));
                    peer.CommitRequiredSubmitted(record.N,peer.Generation);
                }
                foreach(var sample in peer.ReadLatest(now,16384))
                {int count=System.Text.Encoding.UTF8.GetByteCount(sample.Json);bytes+=count;writer.WriteLine(new JObject{["stamp"]=now,["kind"]="state",["bytes"]=count,["record"]=JArray.Parse(sample.Json)}.ToString(Newtonsoft.Json.Formatting.None));peer.CommitLatestSubmitted(sample,peer.Generation);}
                if(peer.NeedsBaseline)throw new Exception("Unexpected normal coverage reset");
                peakCurrent=Math.Max(peakCurrent,source.CurrentBytes);peakJournal=Math.Max(peakJournal,source.JournalBytes);peakLeases=Math.Max(peakLeases,source.LeaseBytes);
            }
            catch(Exception ex){errors++;writer.WriteLine(new JObject{["error"]=ex.ToString()}.ToString(Newtonsoft.Json.Formatting.None));}
        }
        public void Dispose()
        {
            Tick(Time.unscaledTimeAsDouble);PNActionRunStore.Measurement-=Record;frameAllocation.Dispose();sourceCpu.enabled=previousCpuEnabled;peer?.Dispose();writer.Dispose();
            cpuMilliseconds.Sort();
            File.WriteAllText(resultPath,new JObject{["sourceCpuSamples"]=cpuHits,["sourceCpuCounterValid"]=cpuHits>0,["sourceCpuP99ms"]=cpuMilliseconds.Count==0?0:cpuMilliseconds[Math.Min(cpuMilliseconds.Count-1,(int)(cpuMilliseconds.Count*.99))],["sourceCpuMaxMs"]=cpuMilliseconds.Count==0?0:cpuMilliseconds.Last(),["meanWholePlayerFrameAllocation"]=frameBytes.Count==0?-1:frameBytes.Average(),["maxWholePlayerFrameAllocation"]=frameBytes.Count==0?-1:frameBytes.Max(),["wholePlayerHeapPeak"]=peakHeap,["wholePlayerGen0Collections"]=GC.CollectionCount(0)-initialCollections,["starts"]=starts,["ends"]=ends,["decodedStarts"]=decodedStarts,["decodedEnds"]=decodedEnds,["drivers"]=drivers,["errors"]=errors,["progressMaxError"]=maxError,["bodyBytes"]=bytes,["currentPeakBytes"]=peakCurrent,["journalPeakBytes"]=peakJournal,["leasePeakBytes"]=peakLeases,["pass"]=errors==0&&starts==decodedStarts&&ends==decodedEnds,["note"]="Source shadow with bounded fake sender; not Action wire delivery"}.ToString());
            PNSkill.ActionPublicationEnabled=previousEnabled;
        }
    }
}
