#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditorInternal;

// Opt-in offline analysis. Does not attach to a running Player or touch scenes.
public static class PNBuffAllocationTraceReview
{
    private sealed class Group { public long Bytes, Calls; public string Stack; }
    public static string Extract(string path, string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stopped editor required.");
        Directory.CreateDirectory(output);
        string prior = Path.Combine(output, "PriorEditorProfile.raw");
        bool hadPrior = ProfilerDriver.lastFrameIndex >= ProfilerDriver.firstFrameIndex && ProfilerDriver.firstFrameIndex >= 0;
        if (hadPrior) ProfilerDriver.SaveProfile(prior);
        var groups = new Dictionary<string, Group>();
        var addresses = new List<ulong>();
        var frames = new List<object>();
        int totalFrames = 0, withStacks = 0, first = -1, last = -1;
        try
        {
            ProfilerDriver.LoadProfile(path, false);
            first = ProfilerDriver.firstFrameIndex; last = ProfilerDriver.lastFrameIndex;
            for (int f = first; f <= last; f++)
            using (var data = ProfilerDriver.GetRawFrameDataView(f, 0))
            {
                if (!data.valid) continue;
                totalFrames++;
                long bytes = 0, calls = 0;
                for (int i = 0; i < data.sampleCount; i++)
                {
                    if (data.GetSampleName(i) != "GC.Alloc") continue;
                    long size = data.GetSampleMetadataCount(i) > 0 ? data.GetSampleMetadataAsLong(i, 0) : 0;
                    bytes += size; calls++;
                    addresses.Clear(); data.GetSampleCallstack(i, addresses);
                    if (addresses.Count > 0) withStacks++;
                    string stack = string.Join("\n", addresses.Select(a => { var m = data.ResolveMethodInfo(a); return m.methodName + " @ " + m.sourceFileName + ":" + m.sourceFileLine; }));
                    if (stack.Length == 0) stack = "<no callstack>";
                    if (!groups.TryGetValue(stack, out var group)) groups[stack] = group = new Group { Stack = stack };
                    group.Bytes += size; group.Calls++;
                }
                frames.Add(new { frame = f, milliseconds = data.frameTimeMs, bytes, calls });
            }
            File.WriteAllText(Path.Combine(output, "AllocationStacks.json"), JsonConvert.SerializeObject(new { path, first, last, totalFrames, withStacks,
                frames, groups = groups.Values.OrderByDescending(g => g.Bytes).Take(1000).ToArray(),
                caveat = "Main thread only; loaded profiler history bounds reported. Development profiler overhead. Does not establish normal Player FPS or complete allocations on other threads." }, Formatting.Indented));
            return "Profiler frames=" + totalFrames + ";GC.Alloc stacks=" + withStacks + ";groups=" + groups.Count;
        }
        finally { if (hadPrior && File.Exists(prior)) ProfilerDriver.LoadProfile(prior, false); else ProfilerDriver.ClearAllFrames(); }
    }
}
#endif
