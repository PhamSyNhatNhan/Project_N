using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace EveEngine
{
    // Opt-in diagnostics. Exclusive time removes only instrumented direct children.
    public static class EveHotPathMetrics
    {
        public struct Sample { public long Calls, Ticks, ExclusiveTicks, AllocatedBytes; public double Milliseconds => Ticks * 1000d / Stopwatch.Frequency; public double ExclusiveMilliseconds => ExclusiveTicks * 1000d / Stopwatch.Frequency; }
        public struct Event
        {
            public string Kind, Origin;
            public Guid Session;
            public long Target, Epoch, Instance, Sequence, Revision, Reasons, Causes, Bytes;
            public double AtMs;
        }
        private struct Timing { internal long Id, Children; }
        [ThreadStatic] private static Dictionary<string, Sample> samples;
        [ThreadStatic] private static List<Timing> stack;
        [ThreadStatic] private static List<Event> events;
        [ThreadStatic] private static long nextId;
        public const int MaximumEventsPerDrain = 65536;
        public static bool Enabled { get; set; }
        public static bool CountOnly { get; set; }
        public static bool TraceAllocations { get; set; }
        public static bool Active => Enabled || CountOnly;
        public static void Increment(string name, long count = 1)
        {
            if (!Active) return;
            if (samples == null) samples = new Dictionary<string, Sample>();
            samples.TryGetValue(name, out var value); value.Calls += count; samples[name] = value;
        }
        public static void Record(Event value)
        {
            if (!Active) return;
            if (events == null) events = new List<Event>(256);
            if (events.Count >= MaximumEventsPerDrain) { Increment("diagnostic.events-dropped"); return; }
            value.AtMs = Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency; events.Add(value);
        }
        public static Event[] DrainEvents()
        { if (events == null || events.Count == 0) return Array.Empty<Event>(); var result = events.ToArray(); events.Clear(); return result; }
        public readonly struct Scope : IDisposable
        {
            private readonly string name;
            private readonly long start;
            private readonly long allocated;
            private readonly bool trace;
            private readonly long id;
            internal Scope(string name)
            {
                this.name = Active ? name : null;
                start = this.name != null && Enabled ? Stopwatch.GetTimestamp() : 0;
                trace = this.name != null && TraceAllocations;
                allocated = trace ? GC.GetAllocatedBytesForCurrentThread() : 0;
                id = 0;
                if (start != 0)
                { if (stack == null) stack = new List<Timing>(16); id = ++nextId; stack.Add(new Timing { Id = id }); }
            }
            public void Dispose()
            {
                if (name == null) return;
                if (samples == null) samples = new Dictionary<string, Sample>();
                samples.TryGetValue(name, out var value); value.Calls++;
                if (start != 0)
                {
                    long elapsed = Stopwatch.GetTimestamp() - start;
                    int index = stack.Count - 1;
                    if (index < 0 || stack[index].Id != id) throw new InvalidOperationException("Diagnostic scopes must be disposed once in nesting order.");
                    value.Ticks += elapsed; value.ExclusiveTicks += Math.Max(0, elapsed - stack[index].Children);
                    stack.RemoveAt(index);
                    if (index > 0) { var parent = stack[index - 1]; parent.Children += elapsed; stack[index - 1] = parent; }
                }
                if (trace) value.AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
                samples[name] = value;
            }
        }
        public static Scope Measure(string name) => new Scope(name);
        public static Dictionary<string, Sample> Drain() { if (samples == null) return new Dictionary<string, Sample>(); var result = new Dictionary<string, Sample>(samples); samples.Clear(); return result; }
    }
}
