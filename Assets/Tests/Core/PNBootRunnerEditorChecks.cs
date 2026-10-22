#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

// Native checks for the staged boot (Docs/LoadingHandoffPlan.md P5): the real game runtime's content loads step by step through PNBootRunner with
// the real content and mods folder, in order, with measured step times; the runtime object stays inactive until the last step. Run in the clone
// (it creates and unloads a real runtime, which the main Editor session should not do).
public static class PNBootRunnerEditorChecks
{
    public static string Run()
    {
        int checks = 0; void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        string report;
        PNGameBootstrap.ResetForTests();
        try
        {
            var runner = PNGameBootstrap.BeginStagedContent();
            var runtime = PNGameBootstrap.Runtime;
            Check(runner != null && !runner.Done && runtime != null && !runtime.gameObject.activeSelf && !runtime.IsContentReady, "the staged boot creates an inactive runtime and loads nothing yet");
            int pumps = 0; float last = 0;
            while (!runner.Pump(0))
            {
                pumps++;
                Check(runner.Fraction > last && !runner.Failed, "every pump with no budget runs exactly one step and progress rises");
                last = runner.Fraction;
                Check(!runtime.IsContentReady || runner.StepName == "published", "content is not ready before its last step");
                if (pumps > 20) throw new Exception("the staged boot did not finish");
            }
            Check(runner.Done && !runner.Failed && pumps == 5, "five steps, then done (pumps=" + pumps + ")");
            Check(runtime.IsContentReady && runtime.gameObject.activeSelf, "the runtime is ready and activated when the last step is done");
            Check(runner.Timings.Select(t => t.Name).SequenceEqual(new[] { "content documents", "code mods", "content registry", "compatibility", "published" }), "the timings carry the step names in order");
            Check(runner.Timings.All(t => t.Milliseconds >= 0) && runner.TotalMilliseconds >= runner.LongestStepMilliseconds && runner.LongestStepMilliseconds > 0, "step times are measured");
            Check(runner.Pump(1000), "pumping a finished runner stays done");
            report = string.Join(", ", runner.Timings.Select(t => t.Name + " " + t.Milliseconds.ToString("0") + " ms")) + "; total " + runner.TotalMilliseconds.ToString("0") + " ms, longest step " + runner.LongestStepMilliseconds.ToString("0") + " ms";

            // content already loaded: a finished runner, runtime active
            var again = PNGameBootstrap.BeginStagedContent();
            Check(again.Done && !again.Failed && again.Fraction == 1f && runtime.gameObject.activeSelf, "a second staged boot on loaded content is finished at once");
        }
        finally
        {
            var runtime = PNGameBootstrap.Runtime;
            if (runtime != null) { try { runtime.UnloadContent(); } catch (Exception) { } UnityEngine.Object.DestroyImmediate(runtime.gameObject); }
            PNGameBootstrap.ResetForTests();
        }

        // a big budget runs everything in one pump
        try
        {
            var runner = PNGameBootstrap.BeginStagedContent();
            Check(runner.Pump(1e9) && runner.Done && PNGameBootstrap.Runtime.IsContentReady, "a large budget finishes the whole load in one pump");
        }
        finally
        {
            var runtime = PNGameBootstrap.Runtime;
            if (runtime != null) { try { runtime.UnloadContent(); } catch (Exception) { } UnityEngine.Object.DestroyImmediate(runtime.gameObject); }
            PNGameBootstrap.ResetForTests();
        }
        return "PNBootRunnerEditorChecks PASS (" + checks + " checks) " + report;
    }
}
#endif
