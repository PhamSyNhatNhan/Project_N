#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using Unity.Profiling;
// Samples only the single frame-total statistic. Never enumerates GC.Alloc events.
public static class PNActionC3FrameAllocationChecks
{
    static ProfilerRecorder recorder;
    static string folder;static int tick;static double deadline,time;
    static PNActionPublication source;static PNActionPublication.Subscription peer;
    static readonly List<long> idle=new List<long>(),load=new List<long>();
    static byte[] calibration;static long sentinel;static bool running;
    public static void Start(string output)
    {
        if(running||EditorApplication.isPlaying)throw new InvalidOperationException("Idle fixture required");
        folder=output;Directory.CreateDirectory(folder);tick=0;time=1;idle.Clear();load.Clear();sentinel=0;
        recorder=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);
        if(!recorder.Valid){recorder.Dispose();throw new InvalidOperationException("Frame allocation statistic unavailable");}
        deadline=EditorApplication.timeSinceStartup+120;running=true;
        EditorApplication.update+=Update;AssemblyReloadEvents.beforeAssemblyReload+=Cancel;
        EditorApplication.QueuePlayerLoopUpdate();
    }
    static void Cancel(){Finish(new OperationCanceledException("Allocation fixture interrupted by reload"));}
    static void Update()
    {
        try
        {
            if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Frame allocation fixture");
            long previous=recorder.LastValue;
            if(tick==15)calibration=new byte[1024*1024];
            if(tick>=16&&tick<=20)sentinel=Math.Max(sentinel,previous);
            if(tick==22)calibration=null;
            if(tick>=40&&tick<100)idle.Add(previous);
            if(tick==100){source=new PNActionPublication();peer=new PNActionPublication.Subscription(source);for(int i=1;i<=64;i++)source.Begin(new PNActionRunKey(100,i),"test:e","test:d","test:a",1,0,time);}
            if(tick>=100&&tick<420)
            {
                if(tick>=121)load.Add(previous);
                double rate=(tick/100)%2+1;for(int i=1;i<=64;i++)source.Advance(new PNActionRunKey(100,i),time,time+.01,.01*rate,rate);
                time+=.01;source.Seal(time);PNActionC3Checks.AcceptRequired(peer);if(tick%5==0)PNActionC3Checks.AcceptLatest(peer,time);
                if(tick%10==0)using(var lease=source.Capture(PNActionC2Codec.MakeScope(1),time)){}
            }
            tick++;if(tick>=420){Finish(null);return;}EditorApplication.QueuePlayerLoopUpdate();
        }
        catch(Exception e){Finish(e);}
    }
    static void Finish(Exception error)
    {
        if(!running)return;running=false;EditorApplication.update-=Update;AssemblyReloadEvents.beforeAssemblyReload-=Cancel;
        recorder.Dispose();peer?.Dispose();source?.Dispose();peer=null;source=null;calibration=null;
        if(error!=null){File.WriteAllText(Path.Combine(folder,"failure.txt"),error.ToString());return;}
        var result=new JObject{["calibrationBytes"]=1048576,["calibrationObservedFrameBytes"]=sentinel,["counterValid"]=sentinel>=1048576&&idle.Count>0&&load.Count>0,["baselineMeanBytes"]=idle.Average(),["loadMeanWholeFrameBytes"]=load.Average(),["loadMaxWholeFrameBytes"]=load.Max(),["baselineSamples"]=new JArray(idle),["loadSamples"]=new JArray(load),["inclusiveMeanBelow128KiB"]=load.Average()<=131072,["note"]="Entire Editor frame including tool/UI; conservative upper bound, not source-only exact allocation. Capacity1 LastValue only; no event enumeration."};
        File.WriteAllText(Path.Combine(folder,"result.json"),result.ToString());
    }
}
#endif
