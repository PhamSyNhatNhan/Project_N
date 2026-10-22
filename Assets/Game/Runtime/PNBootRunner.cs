#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using EveEngine;

// Runs the staged content load (EveRuntime.ContentLoadSteps: documents, code mods, registry, compatibility, publish) across frames: every Pump
// runs steps until a time budget is used up (at least one step each time), records how long each step really took, and keeps the progress for the
// loading screen. The runner owns nothing of the content itself; it only drives and measures the enumerator. Docs/LoadingHandoffPlan.md P5.
public sealed class PNBootRunner
{
    public readonly struct Timing
    {
        public readonly string Name; public readonly double Milliseconds;
        public Timing(string name, double milliseconds) { Name = name; Milliseconds = milliseconds; }
    }
    private readonly IEnumerator<EveBootStep> steps;
    private readonly Action onDone;
    private readonly List<Timing> timings = new List<Timing>();
    public bool Done { get; private set; }
    public bool Failed => Failure != null;
    public Exception Failure { get; private set; }
    // 0..1, the nominal progress of the last finished step.
    public float Fraction { get; private set; }
    public string StepName { get; private set; } = "";
    public IReadOnlyList<Timing> Timings => timings;
    public double TotalMilliseconds { get; private set; }
    public double LongestStepMilliseconds { get; private set; }

    public PNBootRunner(IEnumerator<EveBootStep> steps, Action onDone = null)
    {
        this.steps = steps ?? throw new ArgumentNullException(nameof(steps)); this.onDone = onDone;
    }
    // A runner for content that is already loaded.
    public static PNBootRunner Finished() { var runner = new PNBootRunner(new List<EveBootStep>().GetEnumerator()); runner.Done = true; runner.Fraction = 1f; return runner; }

    // Run steps for about `budgetMilliseconds` (a step that is already running is never cut short). Returns true once everything is done or failed.
    public bool Pump(double budgetMilliseconds)
    {
        if (Done || Failed) return true;
        var clock = Stopwatch.StartNew();
        try
        {
            do
            {
                var step = Stopwatch.StartNew();
                bool more = steps.MoveNext();
                double ms = step.Elapsed.TotalMilliseconds;
                TotalMilliseconds += ms; if (ms > LongestStepMilliseconds) LongestStepMilliseconds = ms;
                if (!more) { Done = true; Fraction = 1f; onDone?.Invoke(); return true; }
                StepName = steps.Current.Name; Fraction = steps.Current.Fraction; timings.Add(new Timing(StepName, ms));
            }
            while (clock.Elapsed.TotalMilliseconds < budgetMilliseconds);
        }
        catch (Exception error) { Failure = error; return true; }
        return false;
    }
}
#endif
